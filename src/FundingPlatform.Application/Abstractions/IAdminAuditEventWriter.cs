// Spec 021 — see specs/021-feedback-session-may13/data-model.md
// (AdminAuditEvent new event-kind discriminators) and research.md OQ-9.

namespace FundingPlatform.Application.Abstractions;

/// <summary>
/// Spec 021 / NFR-005 / OQ-9 — narrow single-seam used by every controller +
/// filter writing the new spec-021 event kinds (<c>ProcessCreated</c>,
/// <c>PlantillaForceDetached</c>, <c>SupplierAdminDeniedAccess</c>, …).
///
/// The seam exists alongside the broader spec-016
/// <c>FundingPlatform.Application.Audit.IAdminAuditWriter</c> (which accepts
/// a pre-built <see cref="FundingPlatform.Domain.Entities.AdminAuditEvent"/>);
/// this surface keeps the common case — <c>WriteAsync(kind, actor, json)</c>
/// — terse enough that every call site can stay one line. Implementations
/// stage the row on the shared <c>AppDbContext</c> without calling
/// <c>SaveChanges</c>; the caller owns the transaction boundary so a failed
/// mutation does not leave a dangling audit row.
/// </summary>
public interface IAdminAuditEventWriter
{
    /// <summary>
    /// Stages an <c>AdminAuditEvent</c> on the shared context. <paramref name="eventKind"/>
    /// MUST be one of the <c>AdminAuditEvent.*</c> constant strings (e.g.
    /// <c>AdminAuditEvent.ProcessCreated</c>); <paramref name="actorUserId"/>
    /// is the authenticated admin's <c>AspNetUsers.Id</c>; <paramref name="payloadJson"/>
    /// carries the action-specific payload (e.g. <c>{"reason":"force"}</c>)
    /// or null when there is no extra context. The caller separately commits
    /// (<c>SaveChangesAsync</c>) in the same UnitOfWork.
    /// </summary>
    Task WriteAsync(string eventKind, string actorUserId, string? payloadJson, CancellationToken ct);
}
