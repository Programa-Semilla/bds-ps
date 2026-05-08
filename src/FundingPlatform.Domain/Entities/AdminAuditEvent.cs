namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 016 / NFR-005 — append-only record of admin mutations on the group
/// catalog and on a user's group memberships. One row per mutation, written by
/// <c>IAdminAuditWriter</c>. No purge or retention policy in this spec.
/// </summary>
public class AdminAuditEvent
{
    /// <summary>Group catalog action keys.</summary>
    public const string ActionGroupCreate = "group.create";
    public const string ActionGroupRename = "group.rename";
    public const string ActionGroupDelete = "group.delete";
    /// <summary>User membership-update action key.</summary>
    public const string ActionUserMembershipsUpdate = "user.memberships.update";

    public const string TargetTypeGroup = "group";
    public const string TargetTypeUser = "user";

    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string TargetId { get; private set; } = string.Empty;
    public string? PayloadJson { get; private set; }

    private AdminAuditEvent() { }

    private AdminAuditEvent(string actorUserId, string action, string targetType, string targetId, string? payloadJson)
    {
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        PayloadJson = payloadJson;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>NFR-005 — every audit row carries an actor + timestamp. The
    /// factory validates non-empty fields so callers cannot persist a
    /// malformed row.</summary>
    public static AdminAuditEvent Record(
        string actorUserId,
        string action,
        string targetType,
        string targetId,
        string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action is required.", nameof(action));
        }
        if (string.IsNullOrWhiteSpace(targetType))
        {
            throw new ArgumentException("TargetType is required.", nameof(targetType));
        }
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("TargetId is required.", nameof(targetId));
        }
        return new AdminAuditEvent(actorUserId, action, targetType, targetId, payloadJson);
    }
}
