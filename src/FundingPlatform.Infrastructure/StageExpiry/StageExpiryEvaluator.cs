// Spec 021 — see specs/021-feedback-session-may13/tasks.md T115
// and research.md R-2 + OQ-3.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.StageExpiry;

/// <summary>
/// Spec 021 / T115 / FR-006 — EF-backed
/// <see cref="IStageExpiryEvaluator"/>.
///
/// <para><see cref="EvaluateFor"/> maps an Application's <see cref="ApplicationState"/>
/// onto a <see cref="StageKind"/>, then resolves the window-duration in this
/// order (OQ-3 — per-Process only):</para>
/// <list type="number">
///   <item>
///     <description>
///       Per-Process override (<c>Processes.{Stage}WindowDays</c>) joined via
///       the Applicant's group membership → owning Process.
///     </description>
///   </item>
///   <item>
///     <description>
///       Platform default in <c>SystemConfigurations[Stage.{Stage}.WindowDays]</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Hard-coded safety fallback (Solicitud=14, Revision=10, Facturacion=30
///       — matches the values seeded by <c>SeedData.sql</c>) so the evaluator
///       never throws even on a misconfigured row.
///     </description>
///   </item>
/// </list>
///
/// <para><see cref="DetermineBucket"/> is a pure function over
/// <c>(closesAt, sentMask, now)</c> — no DB calls, no side effects.</para>
/// </summary>
public sealed class StageExpiryEvaluator : IStageExpiryEvaluator
{
    private const byte T72hBit = 0x1;
    private const byte T24hBit = 0x2;
    private const byte ExpiredBit = 0x4;

    private static readonly TimeSpan T72hWindow = TimeSpan.FromHours(72);
    private static readonly TimeSpan T24hWindow = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;

    public StageExpiryEvaluator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(StageKind CurrentStage, DateTimeOffset EnteredAt, DateTimeOffset ClosesAt)>
        EvaluateForAsync(AppEntity application, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(application);

        var stage = ResolveStageKind(application.State);
        var windowDays = await ResolveWindowDaysAsync(application, stage, ct).ConfigureAwait(false);
        var enteredAt = application.StageEnteredAt;
        var closesAt = enteredAt.AddDays(windowDays);
        return (stage, enteredAt, closesAt);
    }

    public ReminderBucket DetermineBucket(DateTimeOffset closesAt, byte sentMask, DateTimeOffset now)
    {
        var remaining = closesAt - now;

        if (remaining <= TimeSpan.Zero)
        {
            return (sentMask & ExpiredBit) == 0 ? ReminderBucket.Expired : ReminderBucket.None;
        }
        if (remaining <= T24hWindow)
        {
            return (sentMask & T24hBit) == 0 ? ReminderBucket.T24h : ReminderBucket.None;
        }
        if (remaining <= T72hWindow)
        {
            return (sentMask & T72hBit) == 0 ? ReminderBucket.T72h : ReminderBucket.None;
        }
        return ReminderBucket.None;
    }

    /// <summary>
    /// Spec 021 — maps Application state machine onto the FR-006 stage-window axis.
    /// Draft/SendBack-to-Draft and AppealOpen are treated as the *Solicitud* stage
    /// (applicant editing window). Submitted/UnderReview is *Revision* (reviewer
    /// working window). ResponseFinalized is *Facturacion* (signing/payout window).
    /// AgreementExecuted has no live window — clamped to Facturacion as a no-op
    /// fallthrough so the bucket math doesn't go negative.
    /// </summary>
    public static StageKind ResolveStageKind(ApplicationState state) => state switch
    {
        ApplicationState.Draft => StageKind.Solicitud,
        ApplicationState.AppealOpen => StageKind.Solicitud,
        ApplicationState.Submitted => StageKind.Revision,
        ApplicationState.UnderReview => StageKind.Revision,
        ApplicationState.Resolved => StageKind.Revision,
        ApplicationState.ResponseFinalized => StageKind.Facturacion,
        ApplicationState.AgreementExecuted => StageKind.Facturacion,
        _ => StageKind.Solicitud,
    };

    private async Task<int> ResolveWindowDaysAsync(AppEntity application, StageKind stage, CancellationToken ct)
    {
        // Per-Process override first (OQ-3): join Applicant → UserGroupMembership →
        // Group → Process, then read the stage's nullable override column.
        var userId = await _db.Applicants
            .Where(a => a.Id == application.ApplicantId)
            .Select(a => a.UserId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        int? overrideDays = null;
        if (!string.IsNullOrEmpty(userId))
        {
            // EF cannot translate a switch-expression inside a projection
            // (CS8514). Project the three override columns and pick the
            // relevant one in-memory after the round-trip.
            var processOverrides = await (
                from m in _db.UserGroupMemberships
                where m.UserId == userId
                join g in _db.Groups on m.GroupId equals g.Id
                join p in _db.Processes on g.ProcessId equals p.Id
                select new
                {
                    p.SolicitudWindowDays,
                    p.RevisionWindowDays,
                    p.FacturacionWindowDays,
                }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (processOverrides is not null)
            {
                overrideDays = stage switch
                {
                    StageKind.Solicitud => processOverrides.SolicitudWindowDays,
                    StageKind.Revision => processOverrides.RevisionWindowDays,
                    StageKind.Facturacion => processOverrides.FacturacionWindowDays,
                    _ => null,
                };
            }
        }

        if (overrideDays is int days && days > 0)
        {
            return days;
        }

        // Platform default in SystemConfiguration. Key matches the SeedData.sql rows.
        var key = $"Stage.{stage}.WindowDays";
        var config = await _db.SystemConfigurations
            .FirstOrDefaultAsync(c => c.Key == key, ct).ConfigureAwait(false);

        if (config is not null && int.TryParse(config.Value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        // Safety fallback — matches SeedData.sql values so misconfigured rows
        // don't take down the reminder service.
        return stage switch
        {
            StageKind.Solicitud => 14,
            StageKind.Revision => 10,
            StageKind.Facturacion => 30,
            _ => 14,
        };
    }
}
