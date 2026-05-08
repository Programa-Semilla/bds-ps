using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Audit;

/// <summary>
/// Spec 016 / NFR-005 — single sink for admin-mutation audit rows. Implementations
/// MUST persist the row in the same transaction as the originating mutation when
/// the caller arranges for it (i.e. they call SaveChanges once after the writer
/// has staged the entity). The writer itself does not call SaveChanges.
/// </summary>
public interface IAdminAuditWriter
{
    Task WriteAsync(AdminAuditEvent ev, CancellationToken ct);
}
