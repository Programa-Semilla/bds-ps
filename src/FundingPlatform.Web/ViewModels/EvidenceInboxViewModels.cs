namespace FundingPlatform.Web.ViewModels;

/// <summary>Spec 041 — view model for the funds-usage evidence inbox (Index).</summary>
public sealed class EvidenceInboxViewModel
{
    public required IReadOnlyList<EvidenceInboxRowViewModel> Rows { get; init; }
}

/// <summary>
/// Spec 041 — one inbox row. Pre-scoped by the projection (NFR-001); the view
/// does no filtering. Links to the existing per-application evidence page.
/// </summary>
public sealed class EvidenceInboxRowViewModel
{
    public required int ApplicationId { get; init; }
    public required string ApplicationNumber { get; init; }
    public required string ApplicantName { get; init; }
    public required string FundName { get; init; }
    public required string ProcessName { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }
}
