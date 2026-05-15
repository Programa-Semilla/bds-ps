namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / FR-001 — thin abstraction over an explicit database transaction
/// the Application Service opens to wrap a workflow-state-change save AND the
/// outbox-row enqueue save in one atomic boundary. Implemented in Infrastructure
/// via <c>AppDbContext.Database.BeginTransactionAsync</c>.
///
/// <para>
/// Pattern in <c>ApplicationService.SubmitApplicationAsync</c>:
/// <code>
/// await using var tx = await _txScope.BeginAsync(ct);
/// application.Submit(...);
/// application.AddVersionHistory(new VersionHistory(...));
/// await _applicationRepository.SaveChangesAsync();   // VersionHistory.Id assigned
/// await _outboxWriter.EnqueueAsync(eventType, app.Id, lastVh.Id, payload, ct);
/// await _applicationRepository.SaveChangesAsync();
/// await tx.CommitAsync(ct);
/// </code>
/// Failure path: <c>tx</c>'s disposal rolls back both saves (FR-001).
/// </para>
/// </summary>
public interface IWorkflowTransactionScope
{
    Task<IWorkflowTransaction> BeginAsync(CancellationToken ct);
}

/// <summary>Spec 021 / FR-001 — opaque transaction handle.</summary>
public interface IWorkflowTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
