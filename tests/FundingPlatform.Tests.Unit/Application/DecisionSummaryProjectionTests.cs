using System.Reflection;
using System.Runtime.CompilerServices;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 027 / US4 — the shared per-line decision projection. Builds an
/// Application aggregate (reflection sets the EF-private state/navigations that
/// have no public mutator) and asserts the mapping rules from
/// <c>contracts/decision-summary.md</c>.
/// </summary>
[TestFixture]
public class DecisionSummaryProjectionTests
{
    private readonly IDecisionSummaryProjection _projection = new DecisionSummaryProjection();

    [Test]
    public void Project_OrdersByLineCode_NullsLast_ThenById()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "ACME");
        AddItem(app, id: 10, lineCode: "B-02", product: "B");
        AddItem(app, id: 11, lineCode: null, product: "NoCode-low-id");
        AddItem(app, id: 12, lineCode: "A-01", product: "A");
        AddItem(app, id: 9, lineCode: null, product: "NoCode-lower-id");

        var lines = _projection.Project(app);

        Assert.That(lines.Select(l => l.ProductName),
            Is.EqualTo(new[] { "A", "B", "NoCode-lower-id", "NoCode-low-id" }));
    }

    [Test]
    public void Project_ApprovedLine_CarriesSelectedSupplierAndAmount()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "ACME");
        var item = AddItem(app, id: 1, lineCode: "A-01", product: "Laptop", category: "Equipo");
        AddQuotation(item, supplierId: 5, supplierName: "Proveedor Alfa", price: 900m, currency: "CRC");
        AddQuotation(item, supplierId: 6, supplierName: "Proveedor Beta", price: 1100m, currency: "CRC");
        Approve(item, selectedSupplierId: 6);

        var line = _projection.Project(app).Single();

        Assert.That(line.ReviewStatus, Is.EqualTo(ItemReviewStatus.Approved));
        Assert.That(line.ApprovedSupplierName, Is.EqualTo("Proveedor Beta"));
        Assert.That(line.ApprovedAmount, Is.Not.Null);
        Assert.That(line.ApprovedAmount!.Amount, Is.EqualTo(1100m));
        Assert.That(line.CategoryName, Is.EqualTo("Equipo"));
    }

    [Test]
    public void Project_RejectedLine_CarriesReasonAndAllQuotedSuppliers()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "ACME");
        var item = AddItem(app, id: 1, lineCode: "A-01", product: "Laptop");
        AddQuotation(item, supplierId: 5, supplierName: "Proveedor Alfa", price: 900m, currency: "CRC");
        AddQuotation(item, supplierId: 6, supplierName: "Proveedor Beta", price: 1100m, currency: "CRC");
        Reject(item, comment: "Fuera de presupuesto.");

        var line = _projection.Project(app).Single();

        Assert.That(line.ReviewStatus, Is.EqualTo(ItemReviewStatus.Rejected));
        Assert.That(line.ReviewComment, Is.EqualTo("Fuera de presupuesto."));
        Assert.That(line.ApprovedSupplierName, Is.Null);
        Assert.That(line.ApprovedAmount, Is.Null);
        Assert.That(line.Quotations.Select(q => q.SupplierName),
            Is.EquivalentTo(new[] { "Proveedor Alfa", "Proveedor Beta" }));
    }

    [Test]
    public void Project_PendingLine_HasStatusOnly()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "ACME");
        AddItem(app, id: 1, lineCode: "A-01", product: "Laptop");

        var line = _projection.Project(app).Single();

        Assert.That(line.ReviewStatus, Is.EqualTo(ItemReviewStatus.Pending));
        Assert.That(line.ApprovedSupplierName, Is.Null);
        Assert.That(line.ApprovedAmount, Is.Null);
        Assert.That(line.ApplicantDecision, Is.Null);
    }

    [Test]
    public void Project_NonCrcQuote_HasConversionNote_CrcDoesNot()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "ACME");
        var item = AddItem(app, id: 1, lineCode: "A-01", product: "Laptop");
        AddQuotation(item, supplierId: 5, supplierName: "CRC Co", price: 900m, currency: "CRC");
        var usd = AddQuotation(item, supplierId: 6, supplierName: "USD Co", price: 100m, currency: "USD");
        usd.AttachLegacyRate(
            new ExchangeRateSnapshot(Guid.NewGuid(), 525.123456m, RateType.Buy, new DateTime(2026, 1, 15)),
            convertedCrc: 52512m);

        var line = _projection.Project(app).Single();
        var crcView = line.Quotations.Single(q => q.Currency == "CRC");
        var usdView = line.Quotations.Single(q => q.Currency == "USD");

        Assert.That(crcView.CurrencyConversionNote, Is.Null);
        Assert.That(usdView.CurrencyConversionNote, Does.Contain("Conversión: 1 USD = ₡"));
        Assert.That(usdView.CurrencyConversionNote, Does.Contain("Tipo Compra"));
        Assert.That(usdView.CurrencyConversionNote, Does.Contain("2026-01-15"));
        Assert.That(usdView.ConvertedCrcAmount, Is.EqualTo(52512m));
    }

    [Test]
    public void Project_ApplicantDecision_ComesFromLatestResponse()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "ACME");
        AddItem(app, id: 1, lineCode: "A-01", product: "Laptop");
        AddItem(app, id: 2, lineCode: "A-02", product: "Monitor");

        FundingPlatform.Tests.Unit.Domain.ApplicationResponseTransitionsTests.SetState(app, ApplicationState.Resolved);
        app.SubmitResponse(
            new Dictionary<int, ItemResponseDecision>
            {
                [1] = ItemResponseDecision.Accept,
                [2] = ItemResponseDecision.Reject,
            },
            "applicant-user");

        var lines = _projection.Project(app);

        Assert.That(lines.Single(l => l.ProductName == "Laptop").ApplicantDecision, Is.EqualTo("Aceptado"));
        Assert.That(lines.Single(l => l.ProductName == "Monitor").ApplicantDecision, Is.EqualTo("Rechazado"));
    }

    // ---- reflection-backed aggregate builder ----------------------------------

    private static Item AddItem(AppEntity app, int id, string? lineCode, string product,
        string category = "Categoría", string specs = "specs")
    {
        var item = new Item(product, categoryId: 1);
        SetProp(item, "Id", id);
        SetProp(item, "LineCode", lineCode);
        SetProp(item, "Category", MakeCategory(category));
        app.AddItem(item);
        return item;
    }

    private static Quotation AddQuotation(Item item, int supplierId, string supplierName,
        decimal price, string currency)
    {
        var quotation = new Quotation(supplierId, supplierBranchId: 1, documentId: 1, price,
            validUntil: new DateOnly(2027, 12, 31), currency: currency,
            deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));
        SetProp(quotation, "Id", supplierId); // unique enough for these tests
        SetProp(quotation, "Supplier", MakeSupplier(supplierId, supplierName));
        QuotationsField(item).Add(quotation);
        return quotation;
    }

    private static void Approve(Item item, int selectedSupplierId)
    {
        SetProp(item, "ReviewStatus", ItemReviewStatus.Approved);
        SetProp(item, "SelectedSupplierId", selectedSupplierId);
    }

    private static void Reject(Item item, string comment)
    {
        SetProp(item, "ReviewStatus", ItemReviewStatus.Rejected);
        SetProp(item, "ReviewComment", comment);
    }

    private static Category MakeCategory(string name)
    {
        var c = (Category)RuntimeHelpers.GetUninitializedObject(typeof(Category));
        SetProp(c, "Name", name);
        return c;
    }

    private static Supplier MakeSupplier(int id, string name)
    {
        var s = (Supplier)RuntimeHelpers.GetUninitializedObject(typeof(Supplier));
        SetProp(s, "Id", id);
        SetProp(s, "Name", name);
        return s;
    }

    private static List<Quotation> QuotationsField(Item item)
    {
        var field = typeof(Item).GetField("_quotations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (List<Quotation>)field.GetValue(item)!;
    }

    private static void SetProp(object target, string prop, object? value) =>
        target.GetType()
            .GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
