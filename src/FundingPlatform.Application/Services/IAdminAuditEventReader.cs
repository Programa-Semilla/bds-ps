using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US7 — reads recent <see cref="AdminAuditEvent"/> rows for the
/// dashboard activity feed. Implementation lives in Infrastructure
/// (<c>AdminAuditEventReader</c> over <c>AppDbContext.AdminAuditEvents</c>).
/// </summary>
public interface IAdminAuditEventReader
{
    Task<IReadOnlyList<AdminAuditEvent>> GetRecentAsync(int take, TimeSpan window, CancellationToken ct);
}
