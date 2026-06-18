using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 003 supplier-evaluation score. Spec 038 replaced the four admin booleans
/// with enumerated regulatory statuses: the three compliance points are now
/// awarded when each status is the favorable value
/// (see <see cref="RegulatoryStatusFavorability"/>) and the electronic-invoice
/// point was removed with the field. The scoring algorithm itself is otherwise
/// unchanged; a full redesign against the enum is deferred to slice B.
/// IsRecommended masks Rejected suppliers (FR-043).
/// </summary>
public record SupplierScore(
    int Total,
    bool IsCompliantCCSS,
    bool IsCompliantHacienda,
    bool IsCompliantSICOP,
    bool HasLowestPrice,
    bool IsRecommended,
    bool IsPreSelected,
    bool IsSupplierVerified,
    bool IsSupplierRejected)
{
    /// <summary>
    /// Computes scores for every quotation on an item. The branch is reserved for
    /// reviewer-UI display use and does not affect the score math (research.md R5).
    /// </summary>
    public static List<(int QuotationId, SupplierScore Score)> ComputeForItem(
        List<(Quotation Quotation, Supplier Supplier, SupplierBranch? Branch)> quotations)
    {
        if (quotations.Count == 0)
            return [];

        var minPrice = quotations.Min(q => q.Quotation.Price);

        var scored = quotations.Select(q =>
        {
            bool ccss = q.Supplier.CcssStatus.IsFavorable();
            bool hacienda = q.Supplier.HaciendaStatus.IsFavorable();
            bool sicop = q.Supplier.SicopStatus.IsFavorable();
            bool lowestPrice = q.Quotation.Price == minPrice;
            bool isVerified = q.Supplier.VerificationStatus == SupplierVerificationStatus.Verified;
            bool isRejected = q.Supplier.VerificationStatus == SupplierVerificationStatus.Rejected;

            int total = (ccss ? 1 : 0)
                      + (hacienda ? 1 : 0)
                      + (sicop ? 1 : 0)
                      + (lowestPrice ? 1 : 0);

            return new
            {
                QuotationId = q.Quotation.Id,
                SupplierId = q.Supplier.Id,
                Total = total,
                CCSS = ccss,
                Hacienda = hacienda,
                SICOP = sicop,
                LowestPrice = lowestPrice,
                Verified = isVerified,
                Rejected = isRejected,
            };
        }).ToList();

        int maxScore = scored.Max(s => s.Total);

        // Pre-selected: highest score, tie-break by lowest supplier ID.
        int preSelectedSupplierId = scored
            .Where(s => s.Total == maxScore)
            .OrderBy(s => s.SupplierId)
            .First()
            .SupplierId;

        return scored
            .Select(s => (
                s.QuotationId,
                new SupplierScore(
                    Total: s.Total,
                    IsCompliantCCSS: s.CCSS,
                    IsCompliantHacienda: s.Hacienda,
                    IsCompliantSICOP: s.SICOP,
                    HasLowestPrice: s.LowestPrice,
                    IsRecommended: s.Total == maxScore && !s.Rejected,
                    IsPreSelected: s.SupplierId == preSelectedSupplierId,
                    IsSupplierVerified: s.Verified,
                    IsSupplierRejected: s.Rejected)))
            .OrderByDescending(s => s.Item2.Total)
            .ThenBy(s => s.QuotationId)
            .ToList();
    }
}
