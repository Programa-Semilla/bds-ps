namespace FundingPlatform.Application.Regulatory;

/// <summary>
/// Spec 043 / FR-002 — configuration for the regulatory-freshness gate. A
/// provider's required regulatory field (Hacienda/CCSS/SICOP) is "stale" when its
/// last-reviewed timestamp is null or older than <see cref="FreshnessWindowDays"/>.
/// Bound from the <c>Regulatory</c> configuration section.
/// </summary>
public sealed class RegulatoryFreshnessOptions
{
    public const string SectionName = "Regulatory";

    /// <summary>FR-002 — staleness window in days (default 30).</summary>
    public int FreshnessWindowDays { get; set; } = 30;
}
