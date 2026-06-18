using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 039 — the client's seven-criterion, deterministic, explainable supplier
/// recommendation (master §14), replacing the price-dominant /4 score. Each
/// criterion gives every <b>eligible</b> provider a base 1 point and the winner(s)
/// 2; the total is the sum (7–14). The eligible provider with the strict single
/// maximum total is recommended; a top-score tie yields no auto-recommendation
/// (FR-021). A provider with CCSS <c>sin inscripción</c> is excluded from scoring
/// (FR-016) — never scored, never recommended, flagged <see cref="BlockReason"/>.
///
/// Two distinct tie rules (research D2): price ties → all tied get 1 (FR-008);
/// delivery (shortest) and warranty (longest) ties → all tied get 2 (FR-009/FR-010).
/// Price is compared on the spec-015 CRC-normalized amount (research D6); delivery
/// and warranty on their normalized-to-days value (research D5).
///
/// Pure function — no I/O, deterministic, recomputed on every read (no persisted
/// score table, research D7).
///
/// <para><b>Scope note:</b> this algorithm knows only CCSS eligibility (spec 039);
/// it is intentionally agnostic of supplier <c>VerificationStatus</c>. The spec-013
/// FR-043 rule "a Rejected supplier is never recommended" is applied DOWNSTREAM in
/// <c>ReviewService.MapToReviewDto</c> (<c>IsRecommended &amp;&amp; !isRejected</c>),
/// not here — so a Rejected provider may still appear as the strict-max winner inside
/// this result and is masked at the mapping layer.</para>
/// </summary>
public record SupplierScore(
    int SupplierId,
    bool IsEligible,
    SupplierBlockReason BlockReason,
    int PriceScore,
    int DeliveryLeadTimeScore,
    int WarrantyTimeScore,
    int HaciendaScore,
    int CcssScore,
    int SicopScore,
    int PmeOrPymeScore,
    int Total,
    bool IsRecommended,
    bool IsTiedAtTop)
{
    /// <summary>
    /// Computes scores for every quotation on an item. The branch is reserved for
    /// reviewer-UI display use and does not affect the score math.
    /// </summary>
    public static List<(int QuotationId, SupplierScore Score)> ComputeForItem(
        List<(Quotation Quotation, Supplier Supplier, SupplierBranch? Branch)> quotations)
    {
        if (quotations.Count == 0)
        {
            return [];
        }

        // FR-016 — CCSS sin inscripción is excluded from the candidate set before
        // scoring. null CCSS (sin revisar) is NOT a block (research D4).
        var eligible = quotations
            .Where(q => q.Supplier.CcssStatus != CcssStatus.SinInscripcion)
            .ToList();

        // Quote-level winner thresholds are computed over the eligible set only.
        decimal? minPrice = eligible.Count > 0 ? eligible.Min(q => PriceKey(q.Quotation)) : null;
        var lowestPriceCount = eligible.Count(q => PriceKey(q.Quotation) == minPrice);
        var priceTie = lowestPriceCount >= 2; // FR-008 — price tie → all tied get 1.

        int? minDeliveryDays = eligible.Count > 0 ? eligible.Min(q => q.Quotation.DeliveryLeadTime.InDays) : null;
        int? maxWarrantyDays = eligible.Count > 0 ? eligible.Max(q => q.Quotation.Warranty.InDays) : null;

        var maxTotal = 0;
        var scored = new Dictionary<int, (int total, int supplierId, int price, int delivery, int warranty, int hac, int ccss, int sicop, int pme)>();

        foreach (var q in eligible)
        {
            // FR-008 — lowest CRC price → 2; price ties → all 1.
            var priceScore = (!priceTie && PriceKey(q.Quotation) == minPrice) ? 2 : 1;
            // FR-009 — shortest delivery → 2; ties → all 2.
            var deliveryScore = q.Quotation.DeliveryLeadTime.InDays == minDeliveryDays ? 2 : 1;
            // FR-010 — longest warranty → 2; ties → all 2.
            var warrantyScore = q.Quotation.Warranty.InDays == maxWarrantyDays ? 2 : 1;
            // FR-011/FR-012/FR-013/FR-014 — binary status criteria.
            var haciendaScore = q.Supplier.HaciendaStatus == HaciendaStatus.AlDia ? 2 : 1;
            var ccssScore = q.Supplier.CcssStatus == CcssStatus.AlDia ? 2 : 1;
            var sicopScore = q.Supplier.SicopStatus == SicopStatus.SinSanciones ? 2 : 1;
            var pmeScore = q.Supplier.IsPmeOrPyme ? 2 : 1;

            var total = priceScore + deliveryScore + warrantyScore
                + haciendaScore + ccssScore + sicopScore + pmeScore;

            scored[q.Quotation.Id] = (total, q.Supplier.Id, priceScore, deliveryScore,
                warrantyScore, haciendaScore, ccssScore, sicopScore, pmeScore);

            if (total > maxTotal)
            {
                maxTotal = total;
            }
        }

        // FR-015 / FR-021 — strict single max is recommended; a top-score tie yields
        // no auto-recommendation, the tied set is flagged IsTiedAtTop.
        var winnerCount = scored.Values.Count(s => s.total == maxTotal);
        var hasStrictWinner = winnerCount == 1;

        var results = new List<(int QuotationId, SupplierScore Score)>(quotations.Count);
        foreach (var q in quotations)
        {
            if (scored.TryGetValue(q.Quotation.Id, out var s))
            {
                var isTop = s.total == maxTotal;
                results.Add((q.Quotation.Id, new SupplierScore(
                    SupplierId: s.supplierId,
                    IsEligible: true,
                    BlockReason: SupplierBlockReason.None,
                    PriceScore: s.price,
                    DeliveryLeadTimeScore: s.delivery,
                    WarrantyTimeScore: s.warranty,
                    HaciendaScore: s.hac,
                    CcssScore: s.ccss,
                    SicopScore: s.sicop,
                    PmeOrPymeScore: s.pme,
                    Total: s.total,
                    IsRecommended: isTop && hasStrictWinner,
                    IsTiedAtTop: isTop && !hasStrictWinner)));
            }
            else
            {
                // Ineligible (CCSS sin inscripción): not scored, never recommended.
                results.Add((q.Quotation.Id, new SupplierScore(
                    SupplierId: q.Supplier.Id,
                    IsEligible: false,
                    BlockReason: SupplierBlockReason.CcssSinInscripcion,
                    PriceScore: 0,
                    DeliveryLeadTimeScore: 0,
                    WarrantyTimeScore: 0,
                    HaciendaScore: 0,
                    CcssScore: 0,
                    SicopScore: 0,
                    PmeOrPymeScore: 0,
                    Total: 0,
                    IsRecommended: false,
                    IsTiedAtTop: false)));
            }
        }

        // Stable ordering: eligible first by descending total, then by quotation id;
        // ineligible (Total 0) sink to the bottom.
        return results
            .OrderByDescending(r => r.Score.IsEligible)
            .ThenByDescending(r => r.Score.Total)
            .ThenBy(r => r.QuotationId)
            .ToList();
    }

    /// <summary>
    /// Spec 015 / research D6 — price comparison key. CRC quotes set
    /// <c>ConvertedCrcAmount = Price</c>; non-CRC quotes carry the snapshotted CRC
    /// amount. Falling back to raw <c>Price</c> keeps a single-currency item correct
    /// even if a snapshot is absent.
    /// </summary>
    private static decimal PriceKey(Quotation q) => q.ConvertedCrcAmount ?? q.Price;
}
