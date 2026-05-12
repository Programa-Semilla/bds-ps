using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Notifications.Persistence;

/// <summary>
/// Spec 021 / T022 / FR-001 — appends one outbox row to the current EF
/// unit-of-work. Does NOT call <c>SaveChangesAsync</c>; the Application
/// Service that invoked us is responsible for committing the workflow
/// state change + the version-history row + this outbox row atomically.
///
/// <para>
/// Both the writer and the Application Service must share the same scoped
/// <see cref="AppDbContext"/> instance (default DI scope per request).
/// </para>
/// </summary>
public sealed class NotificationOutboxWriter : INotificationOutboxWriter
{
    private readonly AppDbContext _context;

    public NotificationOutboxWriter(AppDbContext context)
    {
        _context = context;
    }

    public Task EnqueueAsync(
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        NotificationPayload payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var row = NotificationOutbox.Create(eventType, applicationId, versionHistoryId, payload);
        _context.NotificationOutbox.Add(row);
        // No SaveChangesAsync — the caller commits.
        return Task.CompletedTask;
    }

    public Task<bool> HasPriorSendBackAsync(int applicationId, CancellationToken ct)
    {
        return _context.VersionHistories
            .AsNoTracking()
            .AnyAsync(vh => vh.ApplicationId == applicationId && vh.Action == "SendBack", ct);
    }

    public async Task<IReadOnlyList<int>> GetApplicantStageGroupIdsAsync(int applicationId, CancellationToken ct)
    {
        // Applicant's group memberships drive reviewer-bucket resolution per spec-016 read path.
        var query =
            from a in _context.Applications.AsNoTracking()
            where a.Id == applicationId && a.Applicant != null
            from m in _context.UserGroupMemberships
            where m.UserId == a.Applicant!.UserId
            select m.GroupId;

        return await query.Distinct().ToListAsync(ct);
    }
}
