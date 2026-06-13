using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 035 / US2 / T038 — domain behavior for per-item category fields + impact
/// and the per-item submit gates.
/// </summary>
[TestFixture]
public class ItemCategoryImpactTests
{
    private static ImpactTemplate MakeTemplate(int id = 10, string name = "Impacto A")
    {
        var template = new ImpactTemplate(name, description: null, isActive: true);
        typeof(ImpactTemplate).GetProperty("Id")!.SetValue(template, id);
        return template;
    }

    private static CategoryField MakeField(int id, string label, bool required)
    {
        var field = new CategoryField(label.ToLowerInvariant(), label, ParameterDataType.Text, required, 0);
        typeof(CategoryField).GetProperty("Id")!.SetValue(field, id);
        return field;
    }

    [Test]
    public void SetImpact_PopulatesTemplateAndValues()
    {
        var item = new Item("Laptop", categoryId: 1);
        var template = MakeTemplate(42);
        var values = new[] { new ImpactParameterValue(1, "100") };

        item.SetImpact(template, values);

        Assert.That(item.ImpactTemplateId, Is.EqualTo(42));
        Assert.That(item.ImpactTemplate, Is.SameAs(template));
        Assert.That(item.ImpactParameterValues, Has.Count.EqualTo(1));
        Assert.That(item.Impact, Is.Not.Null);
    }

    [Test]
    public void SetCategoryFieldValues_ReplacesAll()
    {
        var item = new Item("Laptop", categoryId: 1);

        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(1, "Dell"), new CategoryFieldValue(2, "XPS") });
        Assert.That(item.CategoryFieldValues, Has.Count.EqualTo(2));

        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(1, "HP") });
        Assert.That(item.CategoryFieldValues, Has.Count.EqualTo(1));
        Assert.That(item.CategoryFieldValues[0].Value, Is.EqualTo("HP"));
    }

    [Test]
    public void ChangeCategory_ToDifferentCategory_ClearsValues()
    {
        var item = new Item("Laptop", categoryId: 1);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(1, "Dell") });

        item.ChangeCategory(2);

        Assert.That(item.CategoryId, Is.EqualTo(2));
        Assert.That(item.CategoryFieldValues, Is.Empty);
    }

    [Test]
    public void ChangeCategory_ToSameCategory_KeepsValues()
    {
        var item = new Item("Laptop", categoryId: 1);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(1, "Dell") });

        item.ChangeCategory(1);

        Assert.That(item.CategoryFieldValues, Has.Count.EqualTo(1));
    }

    [Test]
    public void Validate_ItemWithoutImpact_ReportsMissingImpact()
    {
        var app = new AppEntity(applicantId: 1, 1, "ACME");
        var item = new Item("Laptop", categoryId: 1);
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.Some.Contains("impact template"));
    }

    [Test]
    public void Validate_ItemMissingRequiredCategoryField_ReportsIt()
    {
        // Build a category with one required field; attach it to the item via its Category nav.
        var category = new Category("Equipo", null, isActive: true);
        category.AddField("modelo", "Modelo", ParameterDataType.Text, isRequired: true, sortOrder: 1);
        var requiredField = category.Fields[0];
        typeof(CategoryField).GetProperty("Id")!.SetValue(requiredField, 5);

        var app = new AppEntity(applicantId: 1, 1, "ACME");
        var item = new Item("Laptop", categoryId: 1);
        item.SetImpact(MakeTemplate(), System.Array.Empty<ImpactParameterValue>());
        // Attach the Category nav (loaded) but leave the required field blank.
        typeof(Item).GetProperty("Category")!.SetValue(item, category);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(5, "  ") });
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.Some.Contains("Modelo"));
    }

    [Test]
    public void Validate_ItemWithImpactAndFilledRequiredField_NoImpactOrFieldError()
    {
        var category = new Category("Equipo", null, isActive: true);
        category.AddField("modelo", "Modelo", ParameterDataType.Text, isRequired: true, sortOrder: 1);
        var requiredField = category.Fields[0];
        typeof(CategoryField).GetProperty("Id")!.SetValue(requiredField, 5);

        var app = new AppEntity(applicantId: 1, 1, "ACME");
        var item = new Item("Laptop", categoryId: 1);
        item.SetImpact(MakeTemplate(), System.Array.Empty<ImpactParameterValue>());
        typeof(Item).GetProperty("Category")!.SetValue(item, category);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(5, "XPS 13") });
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.None.Contains("impact template"));
        Assert.That(errors, Has.None.Contains("Modelo"));
    }

    [Test]
    public void CountQuotationsReferencingDocument_CountsAcrossItems()
    {
        var app = new AppEntity(applicantId: 1, 1, "ACME");
        var itemA = new Item("A", 1);
        var itemB = new Item("B", 1);
        StuffQuotation(itemA, documentId: 7);
        StuffQuotation(itemB, documentId: 7);
        StuffQuotation(itemB, documentId: 9);
        app.AddItem(itemA);
        app.AddItem(itemB);

        Assert.That(app.CountQuotationsReferencingDocument(7), Is.EqualTo(2));
        Assert.That(app.CountQuotationsReferencingDocument(9), Is.EqualTo(1));
        Assert.That(app.CountQuotationsReferencingDocument(99), Is.EqualTo(0));
    }

    private static void StuffQuotation(Item item, int documentId)
    {
        var quotation = new Quotation(
            supplierId: documentId * 100 + item.Quotations.Count, // unique supplier per quote
            supplierBranchId: 1,
            documentId: documentId,
            price: 100m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            currency: "CRC");
        var field = typeof(Item).GetField("_quotations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<Quotation>)field!.GetValue(item)!).Add(quotation);
    }
}
