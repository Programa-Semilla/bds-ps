namespace FundingPlatform.Application.Interfaces;

/// <summary>
/// Spec 020 — explicit commit boundary for use cases that stage entities via
/// shared writers (e.g. <see cref="Audit.IAdminAuditWriter"/>) without owning
/// their own SaveChanges. The orchestrator's failure paths emit audit rows
/// but have no follow-up repository call to flush them; this seam closes that gap.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
