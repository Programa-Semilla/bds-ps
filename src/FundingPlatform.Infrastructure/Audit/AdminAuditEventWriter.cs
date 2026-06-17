// Spec 021 — see specs/021-feedback-session-may13/data-model.md (AdminAuditEvent
// new event-kind discriminators) and research.md OQ-9.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;

namespace FundingPlatform.Infrastructure.Audit;

/// <summary>
/// Spec 021 / NFR-005 — terse-API writer for the new spec-021 admin audit
/// event kinds. Mirrors the spec-016
/// <c>FundingPlatform.Infrastructure.Audit.AdminAuditWriter</c> transaction
/// behavior: stages the entity on the shared <see cref="AppDbContext"/>
/// without calling <c>SaveChanges</c>; the caller owns the transaction
/// boundary so a failed parent mutation does not leave a dangling audit row.
///
/// The <c>TargetType</c> / <c>TargetId</c> columns required by the underlying
/// row are derived from <paramref name="eventKind"/> when known (e.g. the
/// <c>process.*</c> events target <c>process</c>); when the row needs a
/// non-derivable target, callers should instead use
/// <see cref="FundingPlatform.Application.Audit.IAdminAuditWriter"/> with a
/// pre-built <see cref="AdminAuditEvent"/>.
/// </summary>
public sealed class AdminAuditEventWriter : IAdminAuditEventWriter
{
    private readonly AppDbContext _db;

    public AdminAuditEventWriter(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task WriteAsync(string eventKind, string actorUserId, string? payloadJson, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Derive TargetType from the discriminator prefix; for kinds the
        // map does not cover, the caller MUST go through IAdminAuditWriter
        // which accepts an explicit target. We default to a "system" target
        // with id "0" for prefix-unknown kinds so the row is still well-formed
        // (the columns are NOT NULL).
        var (targetType, targetId) = DeriveTarget(eventKind);
        var entity = AdminAuditEvent.Record(actorUserId, eventKind, targetType, targetId, payloadJson);
        _db.AdminAuditEvents.Add(entity);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Maps the spec-021 event-kind discriminator to the
    /// (<c>TargetType</c>, <c>TargetId</c>) tuple used by the audit table. The
    /// real target id is only known to the caller — we use a sentinel "0"
    /// placeholder when none is supplied, matching the existing
    /// <c>AdminAuditEvent.Record</c> guard.
    /// </summary>
    private static (string TargetType, string TargetId) DeriveTarget(string eventKind)
    {
        if (eventKind.StartsWith("process.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeProcess, "0");
        }
        if (eventKind.StartsWith("plantilla.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypePlantilla, "0");
        }
        if (eventKind.StartsWith("supplier_admin.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeAdminRoute, "0");
        }
        if (eventKind.StartsWith("group.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeGroup, "0");
        }
        // Spec 029 — Fund (Fondo) mutations (fund.create/edit/archive/…). The
        // real fund id is carried in the payload JSON (TargetId stays the "0"
        // sentinel, matching the process.* pattern above).
        if (eventKind.StartsWith("fund.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeFund, "0");
        }
        if (eventKind.StartsWith("user.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeUser, "0");
        }
        // Spec 036 — funds-usage evidence mutations (funds_evidence.uploaded/…).
        // The real evidence/application ids ride in the payload JSON; TargetId
        // stays the "0" sentinel, matching the fund.*/process.* patterns above.
        if (eventKind.StartsWith("funds_evidence.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeFundsEvidence, "0");
        }
        return ("system", "0");
    }
}
