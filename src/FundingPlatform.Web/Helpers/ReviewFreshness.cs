using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.Helpers;

/// <summary>
/// Spec 038 (US2 / D12) — formats a regulatory field's last-reviewed metadata as
/// an es-CR relative-recency phrase, read directly from the per-field columns on
/// <c>Supplier</c> (never from the audit trail). Pure; unit-tested.
/// </summary>
public static class ReviewFreshness
{
    public static string Describe(DateTime? lastReviewedAt, string? byName, RegulatoryReviewSource? source)
    {
        if (lastReviewedAt is null)
            return RegulatoryStatusLabels.Unreviewed;

        var days = (DateTime.UtcNow.Date - lastReviewedAt.Value.Date).Days;
        var when = days <= 0
            ? "revisado hoy"
            : days == 1
                ? "revisado hace 1 día"
                : $"revisado hace {days} días";

        var by = string.IsNullOrWhiteSpace(byName) ? string.Empty : $" por {byName}";

        var src = source switch
        {
            RegulatoryReviewSource.Manual => " (manual)",
            RegulatoryReviewSource.Api => " (API)",
            RegulatoryReviewSource.System => " (sistema)",
            _ => string.Empty,
        };

        return $"{when}{by}{src}";
    }
}
