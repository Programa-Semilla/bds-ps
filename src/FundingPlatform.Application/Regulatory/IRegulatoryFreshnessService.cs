namespace FundingPlatform.Application.Regulatory;

/// <summary>
/// Spec 043 — freshness query over the providers an application relies on. Backs
/// both the auditor-stage hard gate (US1) and the non-blocking warning (US4).
/// </summary>
public interface IRegulatoryFreshnessService
{
    /// <summary>
    /// Returns one finding per stale required regulatory field across the distinct
    /// suppliers selected by the application's approved items
    /// (<c>Item.SelectedSupplierId</c>, research D2). Empty ⇒ all relied-on
    /// providers are fresh.
    /// </summary>
    Task<IReadOnlyList<StaleRegulatoryFinding>> GetStaleFindingsForApplicationAsync(
        int applicationId, CancellationToken ct);
}
