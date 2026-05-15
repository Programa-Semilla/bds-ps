using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T014 / FR-006..FR-013 — resolves the recipient list for one
/// outbox row at dispatch time (NOT at outbox-write time — EC-003 / EC-004).
/// Per-event resolution rules match the §Recipient Rules table in spec.md.
/// Dedup keeps one recipient per UserId with the lowest-ordinal bucket
/// (FR-012, priority Applicant &gt; Reviewer &gt; Admin).
///
/// <para>
/// The resolver MUST NOT mutate state. Pure read over Applications,
/// AspNetUsers, AspNetUserRoles, Groups, UserGroupMemberships,
/// VersionHistory.
/// </para>
/// </summary>
public interface INotificationRecipientResolver
{
    Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationOutboxResolveContext context,
        CancellationToken ct);
}

/// <summary>
/// Spec 021 / T014 — Application-layer abstraction of the outbox row fields
/// the resolver needs to do its work. Decouples the resolver interface from
/// the Infrastructure-mapped <c>NotificationOutbox</c> entity (Clean
/// Architecture §I — Application layer cannot reference Infrastructure).
/// </summary>
public sealed record NotificationOutboxResolveContext(
    long OutboxId,
    NotificationEvent EventType,
    int ApplicationId,
    int VersionHistoryId,
    NotificationPayload Payload);
