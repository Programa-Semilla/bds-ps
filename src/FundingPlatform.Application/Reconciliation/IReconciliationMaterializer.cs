namespace FundingPlatform.Application.Reconciliation;

/// <summary>
/// Spec 048 / FR-001–FR-004 — recomputes an application's full CURRENT discrepancy set (the blocking
/// legs via the existing pure evaluators + the Warning legs via <c>ReconciliationWarnings</c>) and
/// reconciles it against the persisted <c>Discrepancy</c> rows by stable identity
/// <c>(scope-type, scope-entity-id, comparison)</c>:
/// <list type="bullet">
///   <item>present, already-persisted → <c>Refresh</c> (keep lifecycle state + assignee + history);</item>
///   <item>present, not persisted → <c>Detect</c> (inserted <c>Open</c>);</item>
///   <item>persisted but no longer present → <c>AutoResolve</c> (row retained, never deleted);</item>
///   <item>a <c>Resolved</c>/<c>Waived</c> row that recurs → <c>AutoReopen</c> (a waiver only re-opens on an amount change).</item>
/// </list>
/// Appends <c>DiscrepancyEvent</c>s and owns its <c>SaveChanges</c> (the two-SaveChanges discipline —
/// called AFTER each mutating service's domain <c>SaveChanges</c>). This is the <b>visibility snapshot</b>
/// only: it never throws on discrepancy content, and the money gates keep recomputing fresh at the
/// decision instant and throwing independently (persistence model C, FR-004 / SC-004).
/// </summary>
public interface IReconciliationMaterializer
{
    /// <param name="applicationId">The application whose reconciliation is (re)persisted.</param>
    /// <param name="actorUserId">The user whose mutation triggered this run (for correlation/logging).</param>
    Task MaterializeAsync(int applicationId, string actorUserId, CancellationToken ct);
}
