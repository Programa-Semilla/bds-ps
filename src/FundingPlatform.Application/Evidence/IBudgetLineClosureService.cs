using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.DocRules;

namespace FundingPlatform.Application.Evidence;

/// <summary>
/// Spec 047 / US3 — the off-ledger budget-line closure gate. A Financial Operator closes a line only
/// when, re-checked against FRESH reads: every required document is present (both sources, D1), every
/// attributed payment is <c>Validated</c>, <c>LinePaid == LineAccepted</c> to the colón, and each
/// required graph evidence is fully allocated. Closing writes NO ledger entry (FR-018) and is
/// reversible with a reason (audited). Reuses the P1/P2 group-scope + Financial-Operator write posture
/// at the controller.
/// </summary>
public interface IBudgetLineClosureService
{
    /// <summary>FR-016 — close a budget-line when the gate is satisfied. Refusals name the failing leg.</summary>
    Task<Result> CloseAsync(int applicationId, int itemId, string? reason, string actorUserId, CancellationToken ct);

    /// <summary>FR-017 — reopen a closed line with a required reason. Off-ledger — no balance change.</summary>
    Task<Result> ReopenAsync(int applicationId, int itemId, string reason, string actorUserId, CancellationToken ct);

    /// <summary>The line's required-vs-present completeness (used by the closure UI), or null when the
    /// line is not part of the application.</summary>
    Task<LineCompleteness?> GetCompletenessAsync(int applicationId, int itemId, CancellationToken ct);
}
