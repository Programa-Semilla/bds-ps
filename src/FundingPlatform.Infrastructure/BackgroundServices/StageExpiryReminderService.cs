// Spec 021 — see specs/021-feedback-session-may13/tasks.md T117
// and research.md R-2 + NFR-002.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.BackgroundServices;

/// <summary>
/// Spec 021 / T117 / FR-024 / FR-025 / NFR-002 — hourly hosted service that
/// scans Active Applications, classifies each into a
/// <see cref="ReminderBucket"/> via <see cref="IStageExpiryEvaluator"/>, and
/// emits T-72h / T-24h / expiry reminder emails via <see cref="IEmailSender"/>.
///
/// <para>Idempotency: <c>Applications.RemindersSentMask</c> bitfield tracks
/// which reminders have already fired for the current stage entry; a bit is
/// set atomically AFTER a successful send so retries never double-send. The
/// mask resets to 0 on every stage transition (<c>Application.ResetStageState</c>).</para>
///
/// <para>Backoff: send failures retry with exponential backoff (1s, 2s, 4s,
/// 8s, 16s) up to 5 attempts (NFR-002). Final failure is logged and the bit
/// is NOT set so the next hourly cycle will retry.</para>
///
/// <para>Test seam: <see cref="ExecuteOneCycleAsync"/> is public so integration
/// tests can invoke a single tick deterministically — they don't need to wait
/// for the hourly timer.</para>
/// </summary>
public sealed class StageExpiryReminderService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<StageExpiryReminderService> _logger;

    public StageExpiryReminderService(
        IServiceProvider services,
        ILogger<StageExpiryReminderService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StageExpiryReminderService starting — tick interval {Interval}.", TickInterval);

        // Fire one cycle immediately on startup so the platform reaches steady
        // state without waiting up to an hour for the first tick.
        try
        {
            await ExecuteOneCycleAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "StageExpiryReminderService initial cycle threw.");
        }

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await ExecuteOneCycleAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "StageExpiryReminderService cycle threw.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on host shutdown
        }
    }

    /// <summary>
    /// Spec 021 / T113 — public test seam. Runs exactly one evaluation pass:
    /// pulls every active (non-deleted) Application, evaluates each, and
    /// emits at most one reminder per Application per cycle. Integration tests
    /// call this directly so the bucket-classification + dedupe-mask paths are
    /// exercised without needing the hourly timer.
    /// </summary>
    public async Task<int> ExecuteOneCycleAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IStageExpiryEvaluator>();
        var clock = scope.ServiceProvider.GetRequiredService<IStageExpiryClock>();
        var emailFactory = scope.ServiceProvider.GetRequiredService<StageReminderEmailFactory>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = clock.UtcNow;
        var sent = 0;

        // Spec 021 / FR-021 / T152 / R-10 — route through IApplicationQueryFilter
        // so the soft-delete predicate stays centralised (no inline
        // `a.DeletedAt == null` here — the structural test pins this).
        // Pull every active (non-soft-deleted, non-terminal) Application + its
        // Applicant. Terminal states (AgreementExecuted) have no live window.
        var queryFilter = scope.ServiceProvider.GetRequiredService<IApplicationQueryFilter>();
        // Spec 029 / FR-020 — archived-Fund applications no longer receive
        // stage-expiry reminders (their work is frozen).
        var candidates = await queryFilter
            .ExcludeArchivedFund(queryFilter.ExcludeDeleted(db.Applications))
            .Include(a => a.Applicant)
            .Where(a => a.State != ApplicationState.AgreementExecuted)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var app in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var (stage, _, closesAt) = await evaluator
                .EvaluateForAsync(app, ct).ConfigureAwait(false);

            var bucket = evaluator.DetermineBucket(closesAt, app.RemindersSentMask, now);
            if (bucket == ReminderBucket.None)
            {
                continue;
            }

            var to = app.Applicant?.Email;
            var publicCode = app.PublicCode?.Value ?? $"#{app.Id}";
            if (string.IsNullOrWhiteSpace(to))
            {
                _logger.LogWarning(
                    "Skipping reminder for Application {Code}: applicant email is unset.",
                    publicCode);
                continue;
            }

            var firstName = app.Applicant?.FirstName ?? string.Empty;
            var envelope = emailFactory.Build(bucket, to, firstName, publicCode, stage, closesAt);

            var sentOk = await SendWithBackoffAsync(sender, envelope, ct).ConfigureAwait(false);
            if (!sentOk)
            {
                // Final failure logged inside SendWithBackoffAsync; bit not set
                // so next hourly cycle retries.
                continue;
            }

            app.MarkReminderSent(BitFor(bucket));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            sent++;

            _logger.LogInformation(
                "Sent {Bucket} reminder for Application {Code} (stage={Stage}, closesAt={ClosesAt:o}).",
                bucket, publicCode, stage, closesAt);
        }

        return sent;
    }

    /// <summary>
    /// NFR-002 — exponential-backoff retry (1s, 2s, 4s, 8s, 16s) up to 5
    /// attempts. Returns true on success; false when every attempt threw.
    /// </summary>
    private async Task<bool> SendWithBackoffAsync(IEmailSender sender, EmailMessage envelope, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await sender.SendAsync(envelope, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogError(
                        ex,
                        "Reminder email send failed after {MaxAttempts} attempts (to={To}, subject={Subject}).",
                        maxAttempts, envelope.ToAddress, envelope.Subject);
                    return false;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Reminder email send attempt {Attempt}/{MaxAttempts} failed; retrying in {Delay}.",
                    attempt, maxAttempts, delay);
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }
        return false;
    }

    private static byte BitFor(ReminderBucket bucket) => bucket switch
    {
        ReminderBucket.T72h => 0x1,
        ReminderBucket.T24h => 0x2,
        ReminderBucket.Expired => 0x4,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };
}
