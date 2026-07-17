using FundingPlatform.Application.Admin.Users.DTOs;

namespace FundingPlatform.Application.Reconciliation;

/// <summary>
/// Spec 048 / FR-007/FR-008 — the operator-driven discrepancy lifecycle: assign, mark under
/// correction, and waive (Warning-only). There is <b>no manual Resolve/Reopen</b> — resolution
/// happens by fixing the numbers, and the materializer auto-resolves/auto-reopens. Each success
/// appends a <c>DiscrepancyEvent</c> to the timeline and writes a <c>discrepancy.*</c> audit event
/// (two-SaveChanges discipline); <see cref="AssignAsync"/> also fires the best-effort assignment email (US4).
/// </summary>
public interface IDiscrepancyLifecycleService
{
    Task<DiscrepancyActionResult> AssignAsync(int discrepancyId, string assigneeUserId, string actorUserId, CancellationToken ct);
    Task<DiscrepancyActionResult> MarkUnderCorrectionAsync(int discrepancyId, string? note, string actorUserId, CancellationToken ct);
    /// <summary>Waive a non-blocking Warning discrepancy (reason required). Refuses a Blocking one.</summary>
    Task<DiscrepancyActionResult> WaiveAsync(int discrepancyId, string reason, string actorUserId, CancellationToken ct);
}

/// <summary>Spec 048 — the outcome of a lifecycle action: <see cref="DiscrepancyActionOutcome.Ok"/>,
/// <see cref="DiscrepancyActionOutcome.NotFound"/> (the controller returns a flat 404), or
/// <see cref="DiscrepancyActionOutcome.Refused"/> with the es-CR <see cref="DomainError"/>.</summary>
public sealed record DiscrepancyActionResult(DiscrepancyActionOutcome Outcome, DomainError? Error)
{
    public static DiscrepancyActionResult Ok() => new(DiscrepancyActionOutcome.Ok, null);
    public static DiscrepancyActionResult NotFound() => new(DiscrepancyActionOutcome.NotFound, null);
    public static DiscrepancyActionResult Refused(DomainError error) => new(DiscrepancyActionOutcome.Refused, error);
}

public enum DiscrepancyActionOutcome
{
    Ok,
    NotFound,
    Refused,
}
