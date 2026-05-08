using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;

namespace FundingPlatform.Infrastructure.Audit;

/// <summary>
/// Spec 016 / NFR-005 — persists audit rows via the shared <see cref="AppDbContext"/>.
/// Adds the entity to the context but does NOT call SaveChanges; the caller
/// owns the transaction boundary so a failed mutation does not leave a
/// dangling audit row.
/// </summary>
public sealed class AdminAuditWriter : IAdminAuditWriter
{
    private readonly AppDbContext _db;

    public AdminAuditWriter(AppDbContext db)
    {
        _db = db;
    }

    public Task WriteAsync(AdminAuditEvent ev, CancellationToken ct)
    {
        _db.AdminAuditEvents.Add(ev);
        return Task.CompletedTask;
    }
}
