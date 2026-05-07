using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 015 / T413 + T414 — single source of truth for the application's CRC
/// rollup logic so the applicant Details view, the dashboard card projection,
/// and the reviewer-queue row projection compute the total identically.
///
/// Rule: sum each <see cref="FundingPlatform.Domain.Entities.Item"/>'s
/// selected-supplier <c>Quotation.ConvertedCrcAmount</c>. Items without a
/// selected supplier or without a matching quotation are skipped. Quotations
/// flagged <c>LegacyNeedsReview = true</c> are explicitly excluded
/// (FR-026 — legacy-flagged rows are quarantined out of cross-currency totals
/// until an admin attaches a rate).
/// </summary>
public static class ApplicationCurrencyTotal
{
    /// <param name="application">Loaded application aggregate (Items + Quotations).</param>
    /// <returns>
    /// (total, hasNonCrc): total in CRC summed across all eligible items
    /// (null when no item has a selected supplier yet); hasNonCrc is true when
    /// at least one quotation on the application is non-CRC, regardless of
    /// whether it is the selected one — used as a UI hint that conversion was
    /// involved somewhere on the application.
    /// </returns>
    public static (decimal? Total, bool HasNonCrc) Compute(AppEntity application)
    {
        decimal? total = null;
        var hasNonCrc = false;
        foreach (var item in application.Items)
        {
            foreach (var q in item.Quotations)
            {
                if (!string.IsNullOrEmpty(q.Currency)
                    && !string.Equals(q.Currency, "CRC", StringComparison.OrdinalIgnoreCase))
                {
                    hasNonCrc = true;
                }
            }

            if (item.SelectedSupplierId is null) continue;
            var chosen = item.Quotations.FirstOrDefault(qq => qq.SupplierId == item.SelectedSupplierId);
            if (chosen is null) continue;
            if (chosen.LegacyNeedsReview) continue;
            if (chosen.ConvertedCrcAmount.HasValue)
            {
                total = (total ?? 0m) + chosen.ConvertedCrcAmount.Value;
            }
        }
        return (total, hasNonCrc);
    }
}
