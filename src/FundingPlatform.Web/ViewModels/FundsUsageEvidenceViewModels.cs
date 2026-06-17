using FundingPlatform.Application.FundsUsageEvidence;

namespace FundingPlatform.Web.ViewModels;

/// <summary>Spec 036 — view model for the funds-usage evidence stage (Index).</summary>
public sealed class FundsUsageEvidenceIndexViewModel
{
    public required int ApplicationId { get; init; }
    public required IReadOnlyList<FundsUsageEvidenceListItem> Items { get; init; }

    /// <summary>The comma-joined allowed-extension list for the file input accept hint.</summary>
    public required string AcceptExtensions { get; init; }
}

/// <summary>Spec 036 — per-row model so <c>_EvidenceRow</c> can build action URLs.</summary>
public sealed class FundsUsageEvidenceRowViewModel
{
    public required int ApplicationId { get; init; }
    public required FundsUsageEvidenceListItem Item { get; init; }
}
