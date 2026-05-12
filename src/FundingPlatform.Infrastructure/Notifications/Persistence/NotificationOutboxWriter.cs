using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Persistence;

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
}
