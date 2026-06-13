namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — partition of every data row into created vs skipped. Invariant
/// (SC-002): <c>Succeeded.Count + Errored.Count == data row count</c>.
/// </summary>
public sealed record BatchUserCreateResult(
    IReadOnlyList<BatchUserCreateOutcome> Succeeded,
    IReadOnlyList<BatchUserCreateOutcome> Errored)
{
    public int TotalRows => Succeeded.Count + Errored.Count;
}
