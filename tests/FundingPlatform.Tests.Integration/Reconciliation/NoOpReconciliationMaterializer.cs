using FundingPlatform.Application.Reconciliation;

namespace FundingPlatform.Tests.Integration.Reconciliation;

/// <summary>
/// Spec 048 — a no-op <see cref="IReconciliationMaterializer"/> for the P1–P3 service tests, so those
/// suites stay isolated from the visibility snapshot (they assert ledger/state behavior, not
/// discrepancy rows). The real materializer is exercised directly by the spec-048 integration + E2E tests.
/// </summary>
internal sealed class NoOpReconciliationMaterializer : IReconciliationMaterializer
{
    public Task MaterializeAsync(int applicationId, string actorUserId, CancellationToken ct) => Task.CompletedTask;
}
