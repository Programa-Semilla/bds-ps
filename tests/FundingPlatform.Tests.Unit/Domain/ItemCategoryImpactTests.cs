using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 035 (evolved 2026-06-16) / TE018 / TE024 — domain behavior for application-level
/// impacts, per-item category fields, per-item impact attribution + justification, and
/// the submit gates in <see cref="Application.Validate"/>.
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

    /// <summary>Declares an impact on the app and stamps its Id (in-memory has Id=0 otherwise).</summary>
    private static ApplicationImpact DeclareImpact(AppEntity app, int impactId, ImpactTemplate template)
    {
        var impact = app.AddImpact(template, System.Array.Empty<ImpactParameterValue>());
        typeof(ApplicationImpact).GetProperty("Id")!.SetValue(impact, impactId);
        return impact;
    }

    // ---- Item.AttributeImpacts + SetImpactJustification (TE024) ----

    [Test]
    public void AttributeImpacts_ReplacesAll_AndDeduplicates()
    {
        var item = new Item("Laptop", categoryId: 1);

        item.AttributeImpacts(new[] { 1, 2, 2, 3 });
        Assert.That(item.ItemImpacts.Select(ii => ii.ApplicationImpactId),
            Is.EquivalentTo(new[] { 1, 2, 3 }));

        item.AttributeImpacts(new[] { 5 });
        Assert.That(item.ItemImpacts.Select(ii => ii.ApplicationImpactId), Is.EquivalentTo(new[] { 5 }));
    }

    [Test]
    public void SetImpactJustification_Trims_AndNullsBlank()
    {
        var item = new Item("Laptop", categoryId: 1);

        item.SetImpactJustification("  apoya el empleo  ");
        Assert.That(item.ImpactJustification, Is.EqualTo("apoya el empleo"));

        item.SetImpactJustification("   ");
        Assert.That(item.ImpactJustification, Is.Null);
    }

    [Test]
    public void SetImpactJustification_Over300_Throws()
    {
        var item = new Item("Laptop", categoryId: 1);
        var tooLong = new string('x', 301);

        Assert.That(() => item.SetImpactJustification(tooLong), Throws.ArgumentException);
    }

    [Test]
    public void SetImpactJustification_Exactly300_Allowed()
    {
        var item = new Item("Laptop", categoryId: 1);
        var max = new string('x', 300);

        item.SetImpactJustification(max);
        Assert.That(item.ImpactJustification, Has.Length.EqualTo(300));
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
    public void ChangeCategory_ToDifferentCategory_ClearsValues_ButKeepsAttribution()
    {
        var item = new Item("Laptop", categoryId: 1);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(1, "Dell") });
        item.AttributeImpacts(new[] { 7 });

        item.ChangeCategory(2);

        Assert.That(item.CategoryId, Is.EqualTo(2));
        Assert.That(item.CategoryFieldValues, Is.Empty);
        Assert.That(item.ItemImpacts, Has.Count.EqualTo(1), "attribution survives a category change");
    }

    [Test]
    public void ChangeCategory_ToSameCategory_KeepsValues()
    {
        var item = new Item("Laptop", categoryId: 1);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(1, "Dell") });

        item.ChangeCategory(1);

        Assert.That(item.CategoryFieldValues, Has.Count.EqualTo(1));
    }

    // ---- Application.AddImpact / RemoveImpact (TE018) ----

    [Test]
    public void AddImpact_DuplicateTemplate_Throws()
    {
        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        var template = MakeTemplate(42);
        app.AddImpact(template, System.Array.Empty<ImpactParameterValue>());

        Assert.That(() => app.AddImpact(template, System.Array.Empty<ImpactParameterValue>()),
            Throws.InvalidOperationException);
    }

    [Test]
    public void RemoveImpact_StripsItemAttributions()
    {
        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        var impact = DeclareImpact(app, impactId: 55, MakeTemplate(42));
        var item = new Item("Laptop", categoryId: 1);
        item.AttributeImpacts(new[] { 55 });
        app.AddItem(item);

        app.RemoveImpact(55);

        Assert.That(app.Impacts, Is.Empty);
        Assert.That(item.ItemImpacts, Is.Empty, "SC-007 — removing a declared impact strips attributions");
    }

    // ---- Application.Validate gates (TE024) ----

    [Test]
    public void Validate_NoDeclaredImpact_ReportsIt()
    {
        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        var item = new Item("Laptop", categoryId: 1);
        item.AttributeImpacts(new[] { 1 });
        item.SetImpactJustification("apoya");
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.Some.Contains("al menos un impacto"));
    }

    [Test]
    public void Validate_ItemWithoutAttribution_ReportsIt()
    {
        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        DeclareImpact(app, 1, MakeTemplate());
        var item = new Item("Laptop", categoryId: 1);
        item.SetImpactJustification("apoya");
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.Some.Contains("asociado al menos a un impacto"));
    }

    [Test]
    public void Validate_ItemWithoutJustification_ReportsIt()
    {
        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        DeclareImpact(app, 1, MakeTemplate());
        var item = new Item("Laptop", categoryId: 1);
        item.AttributeImpacts(new[] { 1 });
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.Some.Contains("justificación de impacto"));
    }

    [Test]
    public void Validate_HappyPath_NoImpactErrors()
    {
        var category = new Category("Equipo", null, isActive: true);
        category.AddField("modelo", "Modelo", ParameterDataType.Text, isRequired: true, sortOrder: 1);
        typeof(CategoryField).GetProperty("Id")!.SetValue(category.Fields[0], 5);

        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        DeclareImpact(app, 1, MakeTemplate());
        var item = new Item("Laptop", categoryId: 1);
        item.AttributeImpacts(new[] { 1 });
        item.SetImpactJustification("apoya el empleo local");
        typeof(Item).GetProperty("Category")!.SetValue(item, category);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(5, "XPS 13") });
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.None.Contains("impacto"));
        Assert.That(errors, Has.None.Contains("Modelo"));
    }

    [Test]
    public void Validate_ItemMissingRequiredCategoryField_ReportsIt()
    {
        var category = new Category("Equipo", null, isActive: true);
        category.AddField("modelo", "Modelo", ParameterDataType.Text, isRequired: true, sortOrder: 1);
        typeof(CategoryField).GetProperty("Id")!.SetValue(category.Fields[0], 5);

        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
        DeclareImpact(app, 1, MakeTemplate());
        var item = new Item("Laptop", categoryId: 1);
        item.AttributeImpacts(new[] { 1 });
        item.SetImpactJustification("apoya");
        typeof(Item).GetProperty("Category")!.SetValue(item, category);
        item.SetCategoryFieldValues(new[] { new CategoryFieldValue(5, "  ") });
        app.AddItem(item);

        var errors = app.Validate(minQuotations: 0);

        Assert.That(errors, Has.Some.Contains("Modelo"));
    }

    [Test]
    public void CountQuotationsReferencingDocument_CountsAcrossItems()
    {
        var app = new AppEntity(applicantId: 1, 1, null,"ACME");
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
            currency: "CRC",
            deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));
        var field = typeof(Item).GetField("_quotations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<Quotation>)field!.GetValue(item)!).Add(quotation);
    }
}
