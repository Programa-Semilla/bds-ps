using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.BackgroundServices;

/// <summary>
/// Spec 043 / US4 (research D3) — daily digest emailed DIRECTLY (not via the per-application
/// outbox → no new <c>NotificationEvent</c>) to group-scoped auditors, listing the
/// audit-pipeline applications (<c>PendingAudit</c>/<c>ReturnedFromAudit</c>) whose selected
/// providers have stale/never-reviewed required regulatory fields. Modeled on
/// <c>StageExpiryReminderService</c>: a startup-resilient daily loop + a public
/// <see cref="RunOnceAsync"/> seam. Reuses the daily run time from the Hacienda sync config.
/// </summary>
public sealed class RegulatoryFreshnessDigestService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<HaciendaSyncOptions> _syncOptions;
    private readonly ILogger<RegulatoryFreshnessDigestService> _logger;

    public RegulatoryFreshnessDigestService(
        IServiceProvider services,
        IOptions<HaciendaSyncOptions> syncOptions,
        ILogger<RegulatoryFreshnessDigestService> logger)
    {
        _services = services;
        _syncOptions = syncOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runAt = _syncOptions.Value.RunAtLocalTime;
        _logger.LogInformation("RegulatoryFreshnessDigestService starting — daily at {RunAt} (America/Costa_Rica).", runAt);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DailyRunSchedule.TimeUntilNextRun(runAt, DateTime.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var sent = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Regulatory freshness digest cycle done: {Sent} email(s) sent.", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Regulatory freshness digest cycle threw; will retry on the next schedule.");
            }
        }
    }

    /// <summary>Public test/dev seam — runs one digest pass; returns the number of emails sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var factory = sp.GetRequiredService<RegulatoryDigestEmailFactory>();
        var sender = sp.GetRequiredService<IEmailSender>();
        var window = sp.GetRequiredService<IOptions<RegulatoryFreshnessOptions>>().Value.FreshnessWindowDays;
        var now = DateTime.UtcNow;

        var apps = await db.Applications
            .Include(a => a.Items).ThenInclude(i => i.SelectedSupplier)
            .Where(a => a.State == ApplicationState.PendingAudit || a.State == ApplicationState.ReturnedFromAudit)
            .ToListAsync(ct).ConfigureAwait(false);

        // Per audit-pipeline app: the stale findings across its distinct selected suppliers.
        var appStale = new List<(int GroupId, List<RegulatoryDigestLine> Lines)>();
        foreach (var app in apps)
        {
            var code = app.PublicCode?.Value ?? $"APP-{app.Id:D5}";
            var suppliers = app.Items
                .Where(i => i.SelectedSupplierId != null && i.SelectedSupplier != null)
                .Select(i => i.SelectedSupplier!)
                .GroupBy(s => s.Id).Select(g => g.First());

            var lines = new List<RegulatoryDigestLine>();
            foreach (var s in suppliers)
            {
                foreach (var field in s.StaleRequiredFields(window, now))
                {
                    lines.Add(new RegulatoryDigestLine(code, s.Name, field));
                }
            }
            if (lines.Count > 0)
            {
                appStale.Add((app.GroupId, lines));
            }
        }

        if (appStale.Count == 0)
        {
            return 0;
        }

        // Resolve the Auditor-role members of every affected group (spec-016/040 group scope).
        var groupIds = appStale.Select(a => a.GroupId).Distinct().ToList();
        var auditorRows = await (
            from m in db.UserGroupMemberships
            where groupIds.Contains(m.GroupId)
            join u in db.Users on m.UserId equals u.Id
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where r.NormalizedName == "AUDITOR" && u.Email != null
            select new { m.GroupId, u.Id, u.Email, u.FirstName })
            .ToListAsync(ct).ConfigureAwait(false);

        // Aggregate one digest per auditor across all groups they belong to.
        var byAuditor = new Dictionary<string, (string Email, string First, List<RegulatoryDigestLine> Lines)>();
        foreach (var app in appStale)
        {
            foreach (var aud in auditorRows.Where(x => x.GroupId == app.GroupId))
            {
                if (!byAuditor.TryGetValue(aud.Id, out var entry))
                {
                    entry = (aud.Email!, aud.FirstName ?? string.Empty, new List<RegulatoryDigestLine>());
                    byAuditor[aud.Id] = entry;
                }
                entry.Lines.AddRange(app.Lines);
            }
        }

        var sent = 0;
        foreach (var entry in byAuditor.Values)
        {
            var message = await factory.BuildAsync(entry.Email, entry.First, entry.Lines, ct).ConfigureAwait(false);
            if (await SendWithBackoffAsync(sender, message, ct).ConfigureAwait(false) == EmailSendOutcome.Sent)
            {
                sent++;
            }
        }
        return sent;
    }

    private static async Task<EmailSendOutcome> SendWithBackoffAsync(
        IEmailSender sender, EmailMessage message, CancellationToken ct)
    {
        var outcome = EmailSendOutcome.TransientFailure;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            outcome = (await sender.SendAsync(message, ct).ConfigureAwait(false)).Outcome;
            if (outcome != EmailSendOutcome.TransientFailure) break;
            try { await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
        }
        return outcome;
    }
}
