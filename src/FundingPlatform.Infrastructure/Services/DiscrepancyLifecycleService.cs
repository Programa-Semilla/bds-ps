// Spec 048 — see specs/048-full-reconciliation-engine/contracts/interfaces.md (lifecycle service).

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 048 — implements <see cref="IDiscrepancyLifecycleService"/>. Each action loads the tracked
/// <see cref="Discrepancy"/>, applies the guarded aggregate transition (which appends a
/// <see cref="DiscrepancyEvent"/> to the owned timeline), and writes a <c>discrepancy.*</c> audit event
/// via the two-SaveChanges discipline. Optimistic concurrency (<c>RowVersion</c>) surfaces as a
/// retryable refusal. There is no manual Resolve/Reopen — those are the materializer's job (auto).
/// </summary>
public sealed class DiscrepancyLifecycleService : IDiscrepancyLifecycleService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;
    private readonly IEmailSender _emailSender;
    private readonly DiscrepancyAssignmentEmailFactory _assignmentEmail;
    private readonly ILogger<DiscrepancyLifecycleService> _logger;

    public DiscrepancyLifecycleService(
        AppDbContext db,
        IAdminAuditEventWriter audit,
        IEmailSender emailSender,
        DiscrepancyAssignmentEmailFactory assignmentEmail,
        ILogger<DiscrepancyLifecycleService> logger)
    {
        _db = db;
        _audit = audit;
        _emailSender = emailSender;
        _assignmentEmail = assignmentEmail;
        _logger = logger;
    }

    public async Task<DiscrepancyActionResult> AssignAsync(int discrepancyId, string assigneeUserId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Discrepancies.FirstOrDefaultAsync(x => x.Id == discrepancyId, ct);
        if (d is null)
        {
            return DiscrepancyActionResult.NotFound();
        }
        if (string.IsNullOrWhiteSpace(assigneeUserId))
        {
            return DiscrepancyActionResult.Refused(new DomainError(DiscrepancyReasons.Codes.AssigneeRequired, null, DiscrepancyReasons.AssigneeRequired));
        }
        if (d.IsTerminal)
        {
            return DiscrepancyActionResult.Refused(new DomainError(DiscrepancyReasons.Codes.NotActionable, null, DiscrepancyReasons.NotActionable));
        }

        var before = d.State;
        d.Assign(assigneeUserId, actorUserId, DateTimeOffset.UtcNow);
        var result = await CommitWithAuditAsync(d, before, AdminAuditEvent.DiscrepancyAssigned, actorUserId, ct);

        // US4 — best-effort assignment notification (never blocks; only fires on a real assignment).
        if (result.Outcome == DiscrepancyActionOutcome.Ok)
        {
            await SendAssignmentEmailBestEffortAsync(d, assigneeUserId, ct);
        }
        return result;
    }

    private async Task SendAssignmentEmailBestEffortAsync(Discrepancy d, string assigneeUserId, CancellationToken ct)
    {
        try
        {
            var assignee = await _db.Users.AsNoTracking()
                .Where(u => u.Id == assigneeUserId)
                .Select(u => new { u.Email, u.FirstName })
                .FirstOrDefaultAsync(ct);
            if (assignee is null || string.IsNullOrWhiteSpace(assignee.Email))
            {
                return;
            }

            var participant = await _db.Applications.AsNoTracking()
                .Where(a => a.Id == d.ApplicationId)
                .Select(a => new { a.Applicant.FirstName, a.Applicant.LastName })
                .FirstOrDefaultAsync(ct);
            var participantName = participant is null
                ? "Solicitante"
                : ($"{participant.FirstName} {participant.LastName}".Trim() is { Length: > 0 } n ? n : "Solicitante");

            var severityLabel = d.Severity == DiscrepancySeverity.Blocking ? "Bloqueante" : "Advertencia";

            var message = await _assignmentEmail.BuildAsync(
                assignee.Email, assignee.FirstName, d.Id,
                applicationNumber: $"APP-{d.ApplicationId:D5}",
                participantName: participantName,
                comparisonLabel: d.SourceDocument,
                severityLabel: severityLabel,
                difference: d.Difference,
                ct);
            await _emailSender.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort assignment email for discrepancy {DiscrepancyId} failed.", d.Id);
        }
    }

    public async Task<DiscrepancyActionResult> MarkUnderCorrectionAsync(int discrepancyId, string? note, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Discrepancies.FirstOrDefaultAsync(x => x.Id == discrepancyId, ct);
        if (d is null)
        {
            return DiscrepancyActionResult.NotFound();
        }
        if (d.IsTerminal)
        {
            return DiscrepancyActionResult.Refused(new DomainError(DiscrepancyReasons.Codes.NotActionable, null, DiscrepancyReasons.NotActionable));
        }

        var before = d.State;
        d.MarkUnderCorrection(actorUserId, note, DateTimeOffset.UtcNow);
        return await CommitWithAuditAsync(d, before, AdminAuditEvent.DiscrepancyUnderCorrection, actorUserId, ct);
    }

    public async Task<DiscrepancyActionResult> WaiveAsync(int discrepancyId, string reason, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Discrepancies.FirstOrDefaultAsync(x => x.Id == discrepancyId, ct);
        if (d is null)
        {
            return DiscrepancyActionResult.NotFound();
        }
        // Pre-check for clean es-CR messages (the aggregate guards throw as a backstop).
        if (d.Severity == DiscrepancySeverity.Blocking)
        {
            return DiscrepancyActionResult.Refused(new DomainError(DiscrepancyReasons.Codes.CannotWaiveBlocking, null, DiscrepancyReasons.CannotWaiveBlocking));
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            return DiscrepancyActionResult.Refused(new DomainError(DiscrepancyReasons.Codes.ReasonRequired, nameof(reason), DiscrepancyReasons.ReasonRequired));
        }

        var before = d.State;
        d.Waive(reason, actorUserId, DateTimeOffset.UtcNow);
        return await CommitWithAuditAsync(d, before, AdminAuditEvent.DiscrepancyWaived, actorUserId, ct);
    }

    private async Task<DiscrepancyActionResult> CommitWithAuditAsync(
        Discrepancy d, DiscrepancyState before, string action, string actorUserId, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct); // #1 — the state change + the appended timeline event
        }
        catch (DbUpdateConcurrencyException)
        {
            return DiscrepancyActionResult.Refused(new DomainError(DiscrepancyReasons.Codes.Concurrency, null, DiscrepancyReasons.Concurrency));
        }

        await _audit.WriteAsync(
            action, actorUserId,
            JsonSerializer.Serialize(new
            {
                discrepancyId = d.Id,
                applicationId = d.ApplicationId,
                before = before.ToString(),
                after = d.State.ToString(),
            }),
            ct);
        await _db.SaveChangesAsync(ct); // #2 — the audit event

        return DiscrepancyActionResult.Ok();
    }
}
