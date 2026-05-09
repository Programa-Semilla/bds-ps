namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 018 / FR-012..FR-014 — reviewer captures a per-Application unique line code
/// at the moment they record their per-item decision. <c>LineCode</c> is required
/// when <c>Decision</c> is <c>Approve</c> or <c>Reject</c>; for <c>RequestMoreInfo</c>
/// it may be blank (the reviewer hasn't decided yet, per R-008).
/// </summary>
public record ReviewItemCommand(
    int ApplicationId,
    int ItemId,
    string Decision,
    string? Comment,
    int? SelectedSupplierId,
    string? LineCode);
