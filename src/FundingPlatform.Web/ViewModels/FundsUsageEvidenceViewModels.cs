using FundingPlatform.Application.FundsUsageEvidence;

namespace FundingPlatform.Web.ViewModels;

/// <summary>Spec 036 — view model for the funds-usage evidence stage (Index).</summary>
public sealed class FundsUsageEvidenceIndexViewModel
{
    public required int ApplicationId { get; init; }
    public required IReadOnlyList<FundsUsageEvidenceListItem> Items { get; init; }

    /// <summary>The comma-joined allowed-extension list for the file input accept hint.</summary>
    public required string AcceptExtensions { get; init; }

    /// <summary>
    /// Spec 041 — true when the governing Process is <c>Closed</c>: the page is
    /// view + download only (upload/edit/delete hidden in the UI and rejected
    /// server-side). Defaults to read-write so spec 036 behavior is unchanged
    /// while the process is active (FR-005).
    /// </summary>
    public bool IsReadOnly { get; init; }
}

/// <summary>Spec 036 — per-row model so <c>_EvidenceRow</c> can build action URLs.</summary>
public sealed class FundsUsageEvidenceRowViewModel
{
    public required int ApplicationId { get; init; }
    public required FundsUsageEvidenceListItem Item { get; init; }

    /// <summary>Spec 041 — hide the edit-note save + delete controls when the process is closed (download stays).</summary>
    public bool IsReadOnly { get; init; }
}
