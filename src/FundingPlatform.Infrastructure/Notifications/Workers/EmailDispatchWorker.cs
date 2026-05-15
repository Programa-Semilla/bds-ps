using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Notifications.Workers;

/// <summary>
/// Spec 021 / T037 / FR-003..FR-005 — hosted <see cref="BackgroundService"/>
/// that polls <c>dbo.NotificationOutbox</c> for work, claims each row via
/// the <c>RowVersion</c> optimistic update, resolves recipients, renders
/// templates, sends via <see cref="IEmailSender"/>, and persists per-recipient
/// delivery rows.
///
/// <para>
/// Retry / backoff per FR-021 — transient failures retry on schedule
/// <c>(1s, 5s, 30s)</c> across <c>MaxAttempts=3</c>. Permanent failures
/// transition the outbox row to <c>DeadLetter</c> immediately (FR-022).
/// </para>
/// <para>
/// Worker exceptions MUST NOT crash the Web host (NFR-004): every exception
/// is caught, logged, and the loop continues on the next poll.
/// </para>
/// </summary>
public sealed class EmailDispatchWorker : BackgroundService
{
    /// <summary>FR-021 backoff schedule — index 0 used after first failure.</summary>
    public static readonly TimeSpan[] BackoffSchedule =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailDispatchWorker> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxAttempts;
    private readonly int _batchSize;

    public EmailDispatchWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<EmailDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;

        var pollSeconds = int.TryParse(_config["Notifications:Worker:PollIntervalSeconds"], out var p) ? p : 5;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, pollSeconds));
        _maxAttempts = int.TryParse(_config["Notifications:Worker:MaxAttempts"], out var m) ? Math.Max(1, m) : 3;
        _batchSize = int.TryParse(_config["Notifications:Worker:BatchSize"], out var b) ? Math.Max(1, b) : 25;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "EmailDispatchWorker started. PollInterval={Poll}s MaxAttempts={Max} BatchSize={Batch}",
            _pollInterval.TotalSeconds, _maxAttempts, _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // NFR-004 — log + continue.
                _logger.LogError(ex, "EmailDispatchWorker batch exception; will retry next poll");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Public for unit / integration tests that drive the loop synchronously
    /// (e.g., IdempotencyDoubleProcessTests).
    /// </summary>
    public async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<INotificationRecipientResolver>();
        var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();
        var sender   = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = DateTime.UtcNow;
        var batch = await db.NotificationOutbox
            .Where(o => o.Status == NotificationOutboxStatus.Pending ||
                        (o.Status == NotificationOutboxStatus.Dispatching &&
                         (o.NextAttemptAt == null || o.NextAttemptAt <= now)))
            .OrderBy(o => o.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        foreach (var outbox in batch)
        {
            ct.ThrowIfCancellationRequested();
            await DispatchOneAsync(outbox, scope.ServiceProvider, db, resolver, renderer, sender, ct);
        }
    }

    private async Task DispatchOneAsync(
        NotificationOutbox outbox,
        IServiceProvider services,
        AppDbContext db,
        INotificationRecipientResolver resolver,
        IEmailTemplateRenderer renderer,
        IEmailSender sender,
        CancellationToken ct)
    {
        // FR-004 — claim with RowVersion guard.
        outbox.ClaimForDispatch();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogDebug(
                "Lost claim on outbox row {Id} (RowVersion conflict). Skipping.", outbox.Id);
            return;
        }

        var payload = NotificationPayload.Deserialize(outbox.PayloadJson);
        var context = new NotificationOutboxResolveContext(
            outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId, payload);

        IReadOnlyList<NotificationRecipient> recipients;
        try
        {
            recipients = await resolver.ResolveAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resolver failed for outbox row {Id}", outbox.Id);
            outbox.MarkDeadLetter($"Resolver: {ex.Message}");
            await db.SaveChangesAsync(ct);
            return;
        }

        // Provider name for delivery audit.
        var providerName = ResolveProviderName(sender);

        // Track outcome across all recipients on this row. Any transient triggers a retry.
        // Any permanent (or all transient with attempts exhausted) triggers DeadLetter.
        var sawTransient = false;
        var sawPermanent = false;
        string? lastError = null;

        foreach (var recipient in recipients)
        {
            ct.ThrowIfCancellationRequested();

            // FR-029 — missing email is recorded as Skipped, no provider call.
            if (string.IsNullOrWhiteSpace(recipient.Email))
            {
                var skipped = NotificationDelivery.RecordSkipped(
                    outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId,
                    recipient.UserId, recipient.Email, providerName, "MissingEmail");
                db.NotificationDeliveries.Add(skipped);
                continue;
            }

            // FR-020 — dedup check. If a Sent / BlockedByAllowlist / Skipped row already exists
            // for this (EventType, ApplicationId, VersionHistoryId, RecipientUserId), no-op.
            var alreadyDelivered = await db.NotificationDeliveries.AnyAsync(d =>
                d.EventType == outbox.EventType &&
                d.ApplicationId == outbox.ApplicationId &&
                d.VersionHistoryId == outbox.VersionHistoryId &&
                d.RecipientUserId == recipient.UserId &&
                (d.Status == NotificationDeliveryStatus.Sent ||
                 d.Status == NotificationDeliveryStatus.BlockedByAllowlist ||
                 d.Status == NotificationDeliveryStatus.Skipped), ct);
            if (alreadyDelivered)
            {
                _logger.LogDebug(
                    "Idempotency hit on outbox {Id} recipient {Recipient}; not contacting provider.",
                    outbox.Id, recipient.Email);
                continue;
            }

            // FR-023 — render the variant + bucket flavour.
            RenderedEmail rendered;
            try
            {
                rendered = await renderer.RenderAsync(outbox.EventTypeEnum, recipient, payload, ct);
            }
            catch (EmailRenderException ex)
            {
                _logger.LogError(ex, "Render exception for outbox {Id} recipient {Recipient}", outbox.Id, recipient.Email);
                sawPermanent = true;
                lastError = ex.Message;
                var failure = NotificationDelivery.RecordPermanentFailure(
                    outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId,
                    recipient.UserId, recipient.Email, providerName, outbox.AttemptCount + 1, ex.Message);
                db.NotificationDeliveries.Add(failure);
                continue;
            }

            var message = new EmailMessage(
                ToEmail: recipient.Email,
                ToDisplayName: recipient.DisplayName,
                Subject: rendered.Subject,
                HtmlBody: rendered.HtmlBody,
                TextBody: rendered.TextBody,
                ReplyTo: null,
                Headers: null);

            EmailSendResult result;
            try
            {
                result = await sender.SendAsync(message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sender threw for outbox {Id} recipient {Recipient}", outbox.Id, recipient.Email);
                sawTransient = true;
                lastError = ex.Message;
                continue;
            }

            switch (result.Outcome)
            {
                case EmailSendOutcome.Sent:
                {
                    var sent = NotificationDelivery.RecordSend(
                        outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId,
                        recipient.UserId, recipient.Email, providerName, result.ProviderMessageId,
                        outbox.AttemptCount + 1, DateTime.UtcNow);
                    db.NotificationDeliveries.Add(sent);
                    break;
                }
                case EmailSendOutcome.BlockedByAllowlist:
                {
                    var blocked = NotificationDelivery.RecordBlockedByAllowlist(
                        outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId,
                        recipient.UserId, recipient.Email, providerName);
                    db.NotificationDeliveries.Add(blocked);
                    break;
                }
                case EmailSendOutcome.PermanentFailure:
                {
                    sawPermanent = true;
                    lastError = result.ErrorMessage ?? "PermanentFailure";
                    var failure = NotificationDelivery.RecordPermanentFailure(
                        outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId,
                        recipient.UserId, recipient.Email, providerName,
                        outbox.AttemptCount + 1, lastError);
                    db.NotificationDeliveries.Add(failure);
                    break;
                }
                case EmailSendOutcome.TransientFailure:
                default:
                {
                    sawTransient = true;
                    lastError = result.ErrorMessage ?? "TransientFailure";
                    var failure = NotificationDelivery.RecordTransientFailure(
                        outbox.Id, outbox.EventTypeEnum, outbox.ApplicationId, outbox.VersionHistoryId,
                        recipient.UserId, recipient.Email, providerName,
                        outbox.AttemptCount + 1, lastError);
                    db.NotificationDeliveries.Add(failure);
                    break;
                }
            }
        }

        // Decide outbox row terminal state.
        if (sawPermanent)
        {
            outbox.MarkDeadLetter(lastError ?? "PermanentFailure");
        }
        else if (sawTransient)
        {
            var nextAttempt = outbox.AttemptCount + 1;
            if (nextAttempt >= _maxAttempts)
            {
                outbox.MarkDeadLetter(lastError ?? "Max attempts reached");
            }
            else
            {
                var backoff = BackoffSchedule[Math.Min(outbox.AttemptCount, BackoffSchedule.Length - 1)];
                outbox.MarkTransientFailure(lastError ?? "TransientFailure", DateTime.UtcNow + backoff);
            }
        }
        else
        {
            outbox.MarkDone();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Most common: dedup unique-index violation from a concurrent worker.
            // Treat as benign (the row was already delivered) and log.
            _logger.LogWarning(ex,
                "Persist failed for outbox row {Id} delivery rows; idempotency guard may have rejected.",
                outbox.Id);
        }
    }

    private static string ResolveProviderName(IEmailSender sender)
    {
        // Unwrap one layer of decorator (RecipientAllowlistFilter wraps the real sender).
        var type = sender.GetType().Name;
        return type switch
        {
            nameof(Providers.MailtrapSmtpEmailSender) => NotificationDeliveryProvider.MailtrapSmtp,
            nameof(Providers.MailgunHttpEmailSender) => NotificationDeliveryProvider.Mailgun,
            nameof(Providers.NoOpEmailSender) => NotificationDeliveryProvider.NoOp,
            // Decorator → look at wrapped instance via private field if needed; for
            // delivery-audit purposes "MailtrapSmtp" is a safe default in dev because
            // RecipientAllowlistFilter primarily fronts MailtrapSmtpEmailSender outside Production.
            // The choice does not affect any business logic.
            _ => NotificationDeliveryProvider.MailtrapSmtp,
        };
    }
}
