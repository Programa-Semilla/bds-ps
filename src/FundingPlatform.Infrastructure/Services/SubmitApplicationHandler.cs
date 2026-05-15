// Spec 021 — see specs/021-feedback-session-may13/tasks.md T091.

using FundingPlatform.Application.Applications;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / T091 / FR-006 / FR-017 — EF-backed
/// <see cref="ISubmitApplicationHandler"/>. Wraps the stage-aware
/// <see cref="AppEntity.Submit(int, StageKind, DateTimeOffset, DateTimeOffset)"/>
/// overload, resolving <c>stageClosesAt</c> from
/// <see cref="Process.OverrideForStage"/> when the Application's group is
/// attached to a Process, falling back to the platform default in
/// <c>SystemConfigurations[Stage.Solicitud.WindowDays]</c>.
/// </summary>
public sealed class SubmitApplicationHandler : ISubmitApplicationHandler
{
    private readonly AppDbContext _db;
    private readonly IStageExpiryClock _clock;

    public SubmitApplicationHandler(AppDbContext db, IStageExpiryClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task SubmitAsync(SubmitApplicationCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var application = await _db.Applications
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
            .Include(a => a.Applicant)
            .FirstOrDefaultAsync(a => a.Id == cmd.ApplicationId, ct)
            ?? throw new InvalidOperationException(
                $"Application {cmd.ApplicationId} not found.");

        var minQuotations = await ResolveMinimumQuotationsAsync(application, ct);
        var stageClosesAt = await ResolveStageClosesAtAsync(application, ct);

        // Spec 013 / FR-024 — every owned Draft supplier referenced by a
        // quotation flips to PendingReview atomically with the submission.
        // This carried over from ApplicationService.SubmitApplicationAsync
        // (the pre-spec-021 submit path); the spec-021 stage-aware handler
        // must preserve it or admin supplier verification has nothing to act
        // on after an applicant submits.
        var referencedSupplierIds = application.Items
            .SelectMany(i => i.Quotations)
            .Select(q => q.SupplierId)
            .Distinct()
            .ToList();
        if (referencedSupplierIds.Count > 0)
        {
            var suppliers = await _db.Suppliers
                .Where(s => referencedSupplierIds.Contains(s.Id))
                .ToListAsync(ct);
            foreach (var supplier in suppliers)
            {
                if (supplier.VerificationStatus == SupplierVerificationStatus.Draft
                    && supplier.CreatedByApplicantId == application.ApplicantId)
                {
                    supplier.SubmitForReview();
                }
            }
        }

        application.Submit(
            minQuotations,
            StageKind.Solicitud,
            stageClosesAt,
            _clock.UtcNow);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> ResolveMinimumQuotationsAsync(AppEntity application, CancellationToken ct)
    {
        var userId = await _db.Applicants
            .Where(a => a.Id == application.ApplicantId)
            .Select(a => a.UserId)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(userId))
        {
            var snapshot = await (
                from m in _db.UserGroupMemberships
                where m.UserId == userId
                join g in _db.Groups on m.GroupId equals g.Id
                join pp in _db.ProcessPlantillas on g.ProcessId equals pp.ProcessId
                select pp).FirstOrDefaultAsync(ct);
            if (snapshot is not null)
            {
                return snapshot.MinimumQuotationsPerItem;
            }
        }

        var config = await _db.SystemConfigurations
            .FirstOrDefaultAsync(c => c.Key == "MinQuotationsPerItem", ct);
        if (config is not null && int.TryParse(config.Value, out var parsed) && parsed > 0)
        {
            return parsed;
        }
        return 2;
    }

    private async Task<DateTimeOffset> ResolveStageClosesAtAsync(AppEntity application, CancellationToken ct)
    {
        int? overrideDays = null;
        var userId = await _db.Applicants
            .Where(a => a.Id == application.ApplicantId)
            .Select(a => a.UserId)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(userId))
        {
            overrideDays = await (
                from m in _db.UserGroupMemberships
                where m.UserId == userId
                join g in _db.Groups on m.GroupId equals g.Id
                join p in _db.Processes on g.ProcessId equals p.Id
                select p.SolicitudWindowDays).FirstOrDefaultAsync(ct);
        }

        int days = overrideDays ?? await ResolvePlatformDefaultAsync(ct);
        return application.StageEnteredAt.AddDays(days);
    }

    private async Task<int> ResolvePlatformDefaultAsync(CancellationToken ct)
    {
        var config = await _db.SystemConfigurations
            .FirstOrDefaultAsync(c => c.Key == "Stage.Solicitud.WindowDays", ct);
        if (config is not null && int.TryParse(config.Value, out var parsed) && parsed > 0)
        {
            return parsed;
        }
        return 14;
    }
}
