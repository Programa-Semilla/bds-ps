using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class SupplierScoreTests
{
    [Test]
    public void SingleQuotation_GetsLowestPricePoint()
    {
        var supplier = CreateSupplier(1, ccss: false, hacienda: false, sicop: false, eInvoice: false);
        var quotation = CreateQuotation(10, supplierId: 1, price: 100m);

        var results = SupplierScore.ComputeForItem([(quotation, supplier, null)]);

        Assert.That(results, Has.Count.EqualTo(1));
        var score = results[0].Score;
        Assert.That(score.Total, Is.EqualTo(1));
        Assert.That(score.HasLowestPrice, Is.True);
        Assert.That(score.IsRecommended, Is.True);
        Assert.That(score.IsPreSelected, Is.True);
    }

    [Test]
    public void MultipleQuotations_VaryingCompliance_ScoresCorrectly()
    {
        var supplier1 = CreateSupplier(1, ccss: true, hacienda: true, sicop: true, eInvoice: true);
        var supplier2 = CreateSupplier(2, ccss: true, hacienda: false, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 1, price: 1500m);
        var quotation2 = CreateQuotation(20, supplierId: 2, price: 500m);

        var results = SupplierScore.ComputeForItem([(quotation1, supplier1, null), (quotation2, supplier2, null)]);

        Assert.That(results, Has.Count.EqualTo(2));

        var score1 = results.First(r => r.QuotationId == 10).Score;
        Assert.That(score1.Total, Is.EqualTo(4));
        Assert.That(score1.HasLowestPrice, Is.False);

        var score2 = results.First(r => r.QuotationId == 20).Score;
        Assert.That(score2.Total, Is.EqualTo(2));
        Assert.That(score2.HasLowestPrice, Is.True);
    }

    [Test]
    public void PriceTieHandling_BothGetPricePoint()
    {
        var supplier1 = CreateSupplier(1, ccss: true, hacienda: false, sicop: false, eInvoice: false);
        var supplier2 = CreateSupplier(2, ccss: false, hacienda: true, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 1, price: 1000m);
        var quotation2 = CreateQuotation(20, supplierId: 2, price: 1000m);

        var results = SupplierScore.ComputeForItem([(quotation1, supplier1, null), (quotation2, supplier2, null)]);

        var score1 = results.First(r => r.QuotationId == 10).Score;
        var score2 = results.First(r => r.QuotationId == 20).Score;

        Assert.That(score1.HasLowestPrice, Is.True);
        Assert.That(score2.HasLowestPrice, Is.True);
        Assert.That(score1.Total, Is.EqualTo(2));
        Assert.That(score2.Total, Is.EqualTo(2));
    }

    [Test]
    public void AllIdenticalScores_AllRecommended()
    {
        var supplier1 = CreateSupplier(1, ccss: true, hacienda: false, sicop: false, eInvoice: false);
        var supplier2 = CreateSupplier(2, ccss: true, hacienda: false, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 1, price: 500m);
        var quotation2 = CreateQuotation(20, supplierId: 2, price: 500m);

        var results = SupplierScore.ComputeForItem([(quotation1, supplier1, null), (quotation2, supplier2, null)]);

        Assert.That(results.All(r => r.Score.IsRecommended), Is.True);
        Assert.That(results.All(r => r.Score.Total == 2), Is.True);
    }

    [Test]
    public void ZeroCompliance_OnlyPricePoints()
    {
        var supplier1 = CreateSupplier(1, ccss: false, hacienda: false, sicop: false, eInvoice: false);
        var supplier2 = CreateSupplier(2, ccss: false, hacienda: false, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 1, price: 100m);
        var quotation2 = CreateQuotation(20, supplierId: 2, price: 200m);

        var results = SupplierScore.ComputeForItem([(quotation1, supplier1, null), (quotation2, supplier2, null)]);

        var score1 = results.First(r => r.QuotationId == 10).Score;
        var score2 = results.First(r => r.QuotationId == 20).Score;

        Assert.That(score1.Total, Is.EqualTo(1));
        Assert.That(score2.Total, Is.EqualTo(0));
        Assert.That(score1.IsRecommended, Is.True);
        Assert.That(score2.IsRecommended, Is.False);
    }

    [Test]
    public void RecommendationFlag_OnlyHighestScorers()
    {
        var supplier1 = CreateSupplier(1, ccss: true, hacienda: true, sicop: true, eInvoice: true);
        var supplier2 = CreateSupplier(2, ccss: true, hacienda: false, sicop: false, eInvoice: false);
        var supplier3 = CreateSupplier(3, ccss: false, hacienda: false, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 1, price: 500m);
        var quotation2 = CreateQuotation(20, supplierId: 2, price: 500m);
        var quotation3 = CreateQuotation(30, supplierId: 3, price: 500m);

        var results = SupplierScore.ComputeForItem([
            (quotation1, supplier1, null), (quotation2, supplier2, null), (quotation3, supplier3, null)]);

        var score1 = results.First(r => r.QuotationId == 10).Score;
        var score2 = results.First(r => r.QuotationId == 20).Score;
        var score3 = results.First(r => r.QuotationId == 30).Score;

        Assert.That(score1.Total, Is.EqualTo(5));
        Assert.That(score1.IsRecommended, Is.True);
        Assert.That(score2.IsRecommended, Is.False);
        Assert.That(score3.IsRecommended, Is.False);
    }

    [Test]
    public void PreSelection_TieBreaksByLowestSupplierId()
    {
        var supplier5 = CreateSupplier(5, ccss: true, hacienda: true, sicop: false, eInvoice: false);
        var supplier3 = CreateSupplier(3, ccss: true, hacienda: true, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 5, price: 1000m);
        var quotation2 = CreateQuotation(20, supplierId: 3, price: 1000m);

        var results = SupplierScore.ComputeForItem([(quotation1, supplier5, null), (quotation2, supplier3, null)]);

        var score5 = results.First(r => r.QuotationId == 10).Score;
        var score3 = results.First(r => r.QuotationId == 20).Score;

        Assert.That(score5.IsRecommended, Is.True);
        Assert.That(score3.IsRecommended, Is.True);
        Assert.That(score3.IsPreSelected, Is.True);
        Assert.That(score5.IsPreSelected, Is.False);
    }

    [Test]
    public void ResultsSortedByScoreDescending()
    {
        var supplier1 = CreateSupplier(1, ccss: true, hacienda: true, sicop: true, eInvoice: true);
        var supplier2 = CreateSupplier(2, ccss: false, hacienda: false, sicop: false, eInvoice: false);
        var quotation1 = CreateQuotation(10, supplierId: 1, price: 500m);
        var quotation2 = CreateQuotation(20, supplierId: 2, price: 1000m);

        var results = SupplierScore.ComputeForItem([(quotation2, supplier2, null), (quotation1, supplier1, null)]);

        Assert.That(results[0].Score.Total, Is.GreaterThanOrEqualTo(results[1].Score.Total));
        Assert.That(results[0].QuotationId, Is.EqualTo(10));
    }

    [Test]
    public void EmptyList_ReturnsEmpty()
    {
        var results = SupplierScore.ComputeForItem([]);
        Assert.That(results, Is.Empty);
    }

    // -----------------------  Spec 013 additions  -----------------------

    [Test]
    public void SupplierVerified_FlagPropagates()
    {
        var supplier = CreateSupplier(1, ccss: true, hacienda: true, sicop: true, eInvoice: true,
            status: SupplierVerificationStatus.Verified);
        var q = CreateQuotation(10, supplierId: 1, price: 100m);

        var results = SupplierScore.ComputeForItem([(q, supplier, null)]);
        Assert.That(results[0].Score.IsSupplierVerified, Is.True);
        Assert.That(results[0].Score.IsSupplierRejected, Is.False);
    }

    [Test]
    public void SupplierRejected_NeverRecommendedEvenAtMaxScore()
    {
        var rejected = CreateSupplier(1, ccss: true, hacienda: true, sicop: true, eInvoice: true,
            status: SupplierVerificationStatus.Rejected);
        var verified = CreateSupplier(2, ccss: true, hacienda: true, sicop: true, eInvoice: true,
            status: SupplierVerificationStatus.Verified);

        var qRejected = CreateQuotation(10, supplierId: 1, price: 500m);
        var qVerified = CreateQuotation(20, supplierId: 2, price: 500m);

        var results = SupplierScore.ComputeForItem([(qRejected, rejected, null), (qVerified, verified, null)]);

        var rejectedScore = results.First(r => r.QuotationId == 10).Score;
        var verifiedScore = results.First(r => r.QuotationId == 20).Score;

        Assert.That(rejectedScore.Total, Is.EqualTo(verifiedScore.Total),
            "Math should still award the same total");
        Assert.That(rejectedScore.IsRecommended, Is.False, "Rejected supplier must never carry IsRecommended");
        Assert.That(rejectedScore.IsSupplierRejected, Is.True);
        Assert.That(verifiedScore.IsRecommended, Is.True);
    }

    [Test]
    public void SupplierPending_NotVerifiedNotRejected()
    {
        var supplier = CreateSupplier(1, ccss: false, hacienda: false, sicop: false, eInvoice: false,
            status: SupplierVerificationStatus.PendingReview);
        var q = CreateQuotation(10, supplierId: 1, price: 100m);

        var results = SupplierScore.ComputeForItem([(q, supplier, null)]);
        Assert.That(results[0].Score.IsSupplierVerified, Is.False);
        Assert.That(results[0].Score.IsSupplierRejected, Is.False);
    }

    private static Supplier CreateSupplier(int id, bool ccss, bool hacienda, bool sicop, bool eInvoice,
        SupplierVerificationStatus status = SupplierVerificationStatus.Verified)
    {
        // Use the spec 013 CreateDraft factory then promote via reflection / EditByAdmin
        // to set compliance flags & verification status.
        var supplier = Supplier.CreateDraft(
            legalId: $"LEG-{id}",
            name: $"Supplier {id}",
            createdByApplicantId: 1,
            firstBranchName: "Sede principal",
            firstBranchContactName: null,
            firstBranchEmail: null,
            firstBranchPhone: null,
            firstBranchAddressLine: null,
            firstBranchProvince: null,
            firstBranchShippingDetails: null,
            firstBranchWarrantyInfo: null);

        // Apply admin flags via the entity's EditByAdmin method (Constitution II).
        supplier.EditByAdmin(supplier.Name,
            hasElectronicInvoice: eInvoice,
            isCompliantCCSS: ccss,
            isCompliantHacienda: hacienda,
            isCompliantSICOP: sicop);

        // Set Id + VerificationStatus via reflection (private setters).
        typeof(Supplier).GetProperty("Id")!.SetValue(supplier, id);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(supplier, status);

        return supplier;
    }

    private static Quotation CreateQuotation(int id, int supplierId, decimal price)
    {
        var quotation = new Quotation(
            supplierId: supplierId,
            supplierBranchId: supplierId * 100,
            documentId: 1,
            price: price,
            validUntil: DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),
            currency: "USD");

        typeof(Quotation).GetProperty("Id")!.SetValue(quotation, id);

        return quotation;
    }
}
