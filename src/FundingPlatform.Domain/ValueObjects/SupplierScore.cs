using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 003 supplier-evaluation score, extended in spec 013 with two read-only
/// verification flags (FR-042). Math is unchanged: one point each for the four
/// admin-only flags (CCSS, Hacienda, SICOP, e-invoice) plus one for lowest price.
/// IsRecommended now masks Rejected suppliers (FR-043).
/// </summary>
public record SupplierScore(
    int Total,
    bool IsCompliantCCSS,
    bool IsCompliantHacienda,
    bool IsCompliantSICOP,
    bool HasElectronicInvoice,
    bool HasLowestPrice,
    bool IsRecommended,
    bool IsPreSelected,
    bool IsSupplierVerified,
    bool IsSupplierRejected)
{
    /// <summary>
    /// Computes scores for every quotation on an item. Spec 013 changed the
    /// signature from (Quotation, Supplier) to (Quotation, Supplier, SupplierBranch);
    /// the branch is reserved for reviewer-UI display use and does not affect the
    /// score math (research.md R5).
    /// </summary>
    public static List<(int QuotationId, SupplierScore Score)> ComputeForItem(
        List<(Quotation Quotation, Supplier Supplier, SupplierBranch? Branch)> quotations)
    {
        if (quotations.Count == 0)
            return [];

        var minPrice = quotations.Min(q => q.Quotation.Price);

        var scored = quotations.Select(q =>
        {
            bool ccss = q.Supplier.IsCompliantCCSS;
            bool hacienda = q.Supplier.IsCompliantHacienda;
            bool sicop = q.Supplier.IsCompliantSICOP;
            bool eInvoice = q.Supplier.HasElectronicInvoice;
            bool lowestPrice = q.Quotation.Price == minPrice;
            bool isVerified = q.Supplier.VerificationStatus == SupplierVerificationStatus.Verified;
            bool isRejected = q.Supplier.VerificationStatus == SupplierVerificationStatus.Rejected;

            int total = (ccss ? 1 : 0)
                      + (hacienda ? 1 : 0)
                      + (sicop ? 1 : 0)
                      + (eInvoice ? 1 : 0)
                      + (lowestPrice ? 1 : 0);

            return new
            {
                QuotationId = q.Quotation.Id,
                SupplierId = q.Supplier.Id,
                Total = total,
                CCSS = ccss,
                Hacienda = hacienda,
                SICOP = sicop,
                EInvoice = eInvoice,
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
                    HasElectronicInvoice: s.EInvoice,
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
