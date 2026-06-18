using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 039 — the seven-criterion (§14) recommendation algorithm. Each eligible
/// provider gets a base 1 per criterion and the winner(s) 2; total 7–14; strict
/// single max is recommended; CCSS sin inscripción is excluded; two distinct tie
/// rules (price → all 1; delivery/warranty → all 2).
/// </summary>
[TestFixture]
public class SupplierScoreTests
{
    private static readonly TimeDuration D30 = new(30, DurationUnit.Days);
    private static readonly TimeDuration W12 = new(12, DurationUnit.Months);

    [Test]
    public void EmptyList_ReturnsEmpty()
    {
        Assert.That(SupplierScore.ComputeForItem([]), Is.Empty);
    }

    [Test]
    public void SingleEligibleProvider_WinsQuoteCriteria_AndIsRecommended()
    {
        var supplier = CreateSupplier(1, ccss: null, hacienda: null, sicop: null, pyme: false);
        var q = CreateQuotation(10, supplierId: 1, price: 100m, delivery: D30, warranty: W12);

        var score = SupplierScore.ComputeForItem([(q, supplier, null)]).Single().Score;

        Assert.That(score.IsEligible, Is.True);
        Assert.That(score.PriceScore, Is.EqualTo(2));
        Assert.That(score.DeliveryLeadTimeScore, Is.EqualTo(2));
        Assert.That(score.WarrantyTimeScore, Is.EqualTo(2));
        // Unreviewed statuses + not PYME → base 1 each.
        Assert.That(score.HaciendaScore, Is.EqualTo(1));
        Assert.That(score.CcssScore, Is.EqualTo(1));
        Assert.That(score.SicopScore, Is.EqualTo(1));
        Assert.That(score.PmeOrPymeScore, Is.EqualTo(1));
        Assert.That(score.Total, Is.EqualTo(2 + 2 + 2 + 1 + 1 + 1 + 1)); // 10
        Assert.That(score.IsRecommended, Is.True);
        Assert.That(score.IsTiedAtTop, Is.False);
    }

    [Test]
    public void NonLowestPriceProvider_WithBetterDeliveryWarrantyStatuses_IsRecommended()
    {
        // A: cheapest, but slow delivery, short warranty, unreviewed statuses.
        var sA = CreateSupplier(1, ccss: null, hacienda: null, sicop: null, pyme: false);
        var qA = CreateQuotation(10, supplierId: 1, price: 500m,
            delivery: new TimeDuration(60, DurationUnit.Days), warranty: new TimeDuration(6, DurationUnit.Months));
        // B: pricier, but fastest delivery, longest warranty, all favorable + PYME.
        var sB = CreateSupplier(2, ccss: CcssStatus.AlDia, hacienda: HaciendaStatus.AlDia,
            sicop: SicopStatus.SinSanciones, pyme: true);
        var qB = CreateQuotation(20, supplierId: 2, price: 900m,
            delivery: new TimeDuration(10, DurationUnit.Days), warranty: new TimeDuration(24, DurationUnit.Months));

        var results = SupplierScore.ComputeForItem([(qA, sA, null), (qB, sB, null)]);
        var a = results.Single(r => r.QuotationId == 10).Score;
        var b = results.Single(r => r.QuotationId == 20).Score;

        Assert.That(a.PriceScore, Is.EqualTo(2)); // A cheapest
        Assert.That(b.PriceScore, Is.EqualTo(1));
        Assert.That(b.Total, Is.GreaterThan(a.Total));
        Assert.That(b.IsRecommended, Is.True);
        Assert.That(a.IsRecommended, Is.False);
    }

    [Test]
    public void PriceTie_AllTiedGetOne_NoneGetTwo()
    {
        var s1 = CreateSupplier(1);
        var s2 = CreateSupplier(2);
        var q1 = CreateQuotation(10, 1, price: 1000m, delivery: D30, warranty: W12);
        var q2 = CreateQuotation(20, 2, price: 1000m, delivery: D30, warranty: W12);

        var results = SupplierScore.ComputeForItem([(q1, s1, null), (q2, s2, null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.PriceScore, Is.EqualTo(1));
        Assert.That(results.Single(r => r.QuotationId == 20).Score.PriceScore, Is.EqualTo(1));
    }

    [Test]
    public void DeliveryTie_AllTiedGetTwo()
    {
        var q1 = CreateQuotation(10, 1, price: 100m, delivery: D30, warranty: W12);
        var q2 = CreateQuotation(20, 2, price: 200m, delivery: D30, warranty: W12);

        var results = SupplierScore.ComputeForItem([(q1, CreateSupplier(1), null), (q2, CreateSupplier(2), null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.DeliveryLeadTimeScore, Is.EqualTo(2));
        Assert.That(results.Single(r => r.QuotationId == 20).Score.DeliveryLeadTimeScore, Is.EqualTo(2));
    }

    [Test]
    public void WarrantyTie_AllTiedGetTwo()
    {
        var q1 = CreateQuotation(10, 1, price: 100m, delivery: D30, warranty: W12);
        var q2 = CreateQuotation(20, 2, price: 200m, delivery: new TimeDuration(40, DurationUnit.Days), warranty: W12);

        var results = SupplierScore.ComputeForItem([(q1, CreateSupplier(1), null), (q2, CreateSupplier(2), null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.WarrantyTimeScore, Is.EqualTo(2));
        Assert.That(results.Single(r => r.QuotationId == 20).Score.WarrantyTimeScore, Is.EqualTo(2));
    }

    [TestCase(true, 2)]
    [TestCase(false, 1)]
    public void HaciendaCriterion_IsBinaryOnAlDia(bool alDia, int expected)
    {
        var s = CreateSupplier(1, hacienda: alDia ? HaciendaStatus.AlDia : HaciendaStatus.EstadoMoroso);
        var q = CreateQuotation(10, 1, 100m, D30, W12);
        Assert.That(SupplierScore.ComputeForItem([(q, s, null)]).Single().Score.HaciendaScore, Is.EqualTo(expected));
    }

    [TestCase(true, 2)]
    [TestCase(false, 1)]
    public void CcssCriterion_IsBinaryOnAlDia(bool alDia, int expected)
    {
        var s = CreateSupplier(1, ccss: alDia ? CcssStatus.AlDia : CcssStatus.EstadoMoroso);
        var q = CreateQuotation(10, 1, 100m, D30, W12);
        Assert.That(SupplierScore.ComputeForItem([(q, s, null)]).Single().Score.CcssScore, Is.EqualTo(expected));
    }

    [TestCase(true, 2)]
    [TestCase(false, 1)]
    public void SicopCriterion_IsBinaryOnSinSanciones(bool clean, int expected)
    {
        var s = CreateSupplier(1, sicop: clean ? SicopStatus.SinSanciones : SicopStatus.ConSanciones);
        var q = CreateQuotation(10, 1, 100m, D30, W12);
        Assert.That(SupplierScore.ComputeForItem([(q, s, null)]).Single().Score.SicopScore, Is.EqualTo(expected));
    }

    [TestCase(true, 2)]
    [TestCase(false, 1)]
    public void PmeCriterion_IsBinaryOnFlag(bool pyme, int expected)
    {
        var s = CreateSupplier(1, pyme: pyme);
        var q = CreateQuotation(10, 1, 100m, D30, W12);
        Assert.That(SupplierScore.ComputeForItem([(q, s, null)]).Single().Score.PmeOrPymeScore, Is.EqualTo(expected));
    }

    [Test]
    public void TotalRange_IsBetweenSevenAndFourteen()
    {
        // Worst eligible loser: loses every quote criterion, all statuses unfavorable → 7.
        var loser = CreateSupplier(1, ccss: CcssStatus.EstadoMoroso, hacienda: HaciendaStatus.EstadoMoroso,
            sicop: SicopStatus.ConSanciones, pyme: false);
        var qLoser = CreateQuotation(10, 1, price: 900m,
            delivery: new TimeDuration(90, DurationUnit.Days), warranty: new TimeDuration(1, DurationUnit.Months));
        // Best: wins every quote criterion + all favorable + PYME → 14.
        var winner = CreateSupplier(2, ccss: CcssStatus.AlDia, hacienda: HaciendaStatus.AlDia,
            sicop: SicopStatus.SinSanciones, pyme: true);
        var qWinner = CreateQuotation(20, 2, price: 100m,
            delivery: new TimeDuration(5, DurationUnit.Days), warranty: new TimeDuration(36, DurationUnit.Months));

        var results = SupplierScore.ComputeForItem([(qLoser, loser, null), (qWinner, winner, null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.Total, Is.EqualTo(7));
        Assert.That(results.Single(r => r.QuotationId == 20).Score.Total, Is.EqualTo(14));
    }

    [Test]
    public void Price_ComparedOnConvertedCrcAmount_NotRawPrice()
    {
        // CRC quote: 1000 CRC. USD quote: raw price 10 (USD) but 5000 CRC converted.
        // The CRC quote is cheaper on the CRC-normalized amount, so it wins price.
        var qCrc = CreateQuotation(10, 1, price: 1000m, delivery: D30, warranty: W12, currency: "CRC");
        var qUsd = CreateQuotation(20, 2, price: 10m, delivery: D30, warranty: W12,
            currency: "USD", convertedCrc: 5000m);

        var results = SupplierScore.ComputeForItem([(qCrc, CreateSupplier(1), null), (qUsd, CreateSupplier(2), null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.PriceScore, Is.EqualTo(2)); // CRC cheaper
        Assert.That(results.Single(r => r.QuotationId == 20).Score.PriceScore, Is.EqualTo(1));
    }

    [Test]
    public void Delivery_NormalizedToDays_AcrossMixedUnits()
    {
        // 25 days < 1 month (30 days) → the 25-day provider wins delivery.
        var qDays = CreateQuotation(10, 1, price: 100m, delivery: new TimeDuration(25, DurationUnit.Days), warranty: W12);
        var qMonth = CreateQuotation(20, 2, price: 200m, delivery: new TimeDuration(1, DurationUnit.Months), warranty: W12);

        var results = SupplierScore.ComputeForItem([(qDays, CreateSupplier(1), null), (qMonth, CreateSupplier(2), null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.DeliveryLeadTimeScore, Is.EqualTo(2));
        Assert.That(results.Single(r => r.QuotationId == 20).Score.DeliveryLeadTimeScore, Is.EqualTo(1));
    }

    [Test]
    public void CcssSinInscripcion_ExcludedFromScoring_AndNeverRecommended()
    {
        var blocked = CreateSupplier(1, ccss: CcssStatus.SinInscripcion, hacienda: HaciendaStatus.AlDia,
            sicop: SicopStatus.SinSanciones, pyme: true);
        var qBlocked = CreateQuotation(10, 1, price: 1m, // cheapest, would win if scored
            delivery: new TimeDuration(1, DurationUnit.Days), warranty: new TimeDuration(99, DurationUnit.Months));
        var eligible = CreateSupplier(2, ccss: CcssStatus.AlDia);
        var qEligible = CreateQuotation(20, 2, price: 1000m, delivery: D30, warranty: W12);

        var results = SupplierScore.ComputeForItem([(qBlocked, blocked, null), (qEligible, eligible, null)]);
        var b = results.Single(r => r.QuotationId == 10).Score;
        var e = results.Single(r => r.QuotationId == 20).Score;

        Assert.That(b.IsEligible, Is.False);
        Assert.That(b.BlockReason, Is.EqualTo(SupplierBlockReason.CcssSinInscripcion));
        Assert.That(b.Total, Is.EqualTo(0));
        Assert.That(b.IsRecommended, Is.False);
        Assert.That(b.IsTiedAtTop, Is.False);
        // Per-criterion scores are zeroed on a blocked (not-scored) provider.
        Assert.That(b.PriceScore, Is.EqualTo(0));
        Assert.That(b.DeliveryLeadTimeScore, Is.EqualTo(0));
        Assert.That(b.WarrantyTimeScore, Is.EqualTo(0));
        // Winner comparisons run over the eligible set only → the lone eligible
        // provider wins price/delivery/warranty even though the blocked one was cheaper.
        Assert.That(e.IsEligible, Is.True);
        Assert.That(e.PriceScore, Is.EqualTo(2));
        Assert.That(e.IsRecommended, Is.True);
    }

    [Test]
    public void NullCcss_IsNotABlock_ScoresOne()
    {
        var s = CreateSupplier(1, ccss: null);
        var q = CreateQuotation(10, 1, 100m, D30, W12);
        var score = SupplierScore.ComputeForItem([(q, s, null)]).Single().Score;

        Assert.That(score.IsEligible, Is.True);
        Assert.That(score.CcssScore, Is.EqualTo(1));
    }

    [Test]
    public void AllProvidersBlocked_NoEligible_NoneRecommended()
    {
        var s1 = CreateSupplier(1, ccss: CcssStatus.SinInscripcion);
        var s2 = CreateSupplier(2, ccss: CcssStatus.SinInscripcion);
        var q1 = CreateQuotation(10, 1, 100m, D30, W12);
        var q2 = CreateQuotation(20, 2, 200m, D30, W12);

        var results = SupplierScore.ComputeForItem([(q1, s1, null), (q2, s2, null)]);

        Assert.That(results.All(r => !r.Score.IsEligible), Is.True);
        Assert.That(results.All(r => !r.Score.IsRecommended), Is.True);
    }

    // ----------------------- US4: top-score tie -----------------------

    [Test]
    public void SingleStrictMax_SetsExactlyOneRecommended()
    {
        var sWin = CreateSupplier(1, ccss: CcssStatus.AlDia);
        var qWin = CreateQuotation(10, 1, price: 100m, delivery: D30, warranty: W12);
        var sLose = CreateSupplier(2, ccss: CcssStatus.EstadoMoroso);
        var qLose = CreateQuotation(20, 2, price: 200m, delivery: D30, warranty: W12);

        var results = SupplierScore.ComputeForItem([(qWin, sWin, null), (qLose, sLose, null)]);

        Assert.That(results.Count(r => r.Score.IsRecommended), Is.EqualTo(1));
        Assert.That(results.Single(r => r.Score.IsRecommended).QuotationId, Is.EqualTo(10));
        Assert.That(results.Any(r => r.Score.IsTiedAtTop), Is.False);
    }

    [Test]
    public void TopScoreTie_NoneRecommended_AllTiedFlagged()
    {
        // Two identical eligible providers → same total, tie at top.
        var s1 = CreateSupplier(1, ccss: CcssStatus.AlDia);
        var s2 = CreateSupplier(2, ccss: CcssStatus.AlDia);
        var q1 = CreateQuotation(10, 1, price: 500m, delivery: D30, warranty: W12);
        var q2 = CreateQuotation(20, 2, price: 500m, delivery: D30, warranty: W12);

        var results = SupplierScore.ComputeForItem([(q1, s1, null), (q2, s2, null)]);

        Assert.That(results.Any(r => r.Score.IsRecommended), Is.False);
        Assert.That(results.All(r => r.Score.IsTiedAtTop), Is.True);
    }

    [Test]
    public void ThreeWayPartialTie_OnlyTopTwoFlagged_LowerNotFlagged()
    {
        // Two providers tie at the top; a third scores strictly lower. Only the top
        // two carry IsTiedAtTop; the lower one must not (FR-021 flags "the tied set").
        var sTop1 = CreateSupplier(1, ccss: CcssStatus.AlDia);
        var sTop2 = CreateSupplier(2, ccss: CcssStatus.AlDia);
        var sLow = CreateSupplier(3, ccss: CcssStatus.EstadoMoroso); // loses CCSS point
        var qTop1 = CreateQuotation(10, 1, price: 500m, delivery: D30, warranty: W12);
        var qTop2 = CreateQuotation(20, 2, price: 500m, delivery: D30, warranty: W12);
        var qLow = CreateQuotation(30, 3, price: 500m, delivery: D30, warranty: W12);

        var results = SupplierScore.ComputeForItem(
            [(qTop1, sTop1, null), (qTop2, sTop2, null), (qLow, sLow, null)]);

        Assert.That(results.Single(r => r.QuotationId == 10).Score.IsTiedAtTop, Is.True);
        Assert.That(results.Single(r => r.QuotationId == 20).Score.IsTiedAtTop, Is.True);
        Assert.That(results.Single(r => r.QuotationId == 30).Score.IsTiedAtTop, Is.False);
        Assert.That(results.Any(r => r.Score.IsRecommended), Is.False);
    }

    // ----------------------- helpers -----------------------

    private static Supplier CreateSupplier(
        int id,
        CcssStatus? ccss = CcssStatus.AlDia,
        HaciendaStatus? hacienda = null,
        SicopStatus? sicop = null,
        bool pyme = false,
        SupplierVerificationStatus status = SupplierVerificationStatus.Verified)
    {
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

        supplier.ApplyRegulatoryEdit(
            hacienda, ccss, sicop,
            isPmeOrPyme: pyme,
            hasWarning: false,
            warningNote: null,
            actorUserId: "test-actor",
            nowUtc: DateTime.UtcNow);

        typeof(Supplier).GetProperty("Id")!.SetValue(supplier, id);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(supplier, status);

        return supplier;
    }

    private static Quotation CreateQuotation(
        int id, int supplierId, decimal price, TimeDuration delivery, TimeDuration warranty,
        string currency = "CRC", decimal? convertedCrc = null)
    {
        var quotation = new Quotation(
            supplierId: supplierId,
            supplierBranchId: supplierId * 100,
            documentId: 1,
            price: price,
            validUntil: DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),
            currency: currency,
            deliveryLeadTime: delivery,
            warranty: warranty);

        typeof(Quotation).GetProperty("Id")!.SetValue(quotation, id);
        if (convertedCrc.HasValue)
        {
            typeof(Quotation).GetProperty("ConvertedCrcAmount")!.SetValue(quotation, convertedCrc);
        }

        return quotation;
    }
}
