namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 016 — many-to-many join between <see cref="ApplicationUser"/> and
/// <see cref="Group"/>. Composite key (UserId, GroupId). Cascades on either
/// side delete (FR-004 — group delete; standard FK behaviour for user delete).
/// </summary>
public class UserGroupMembership
{
    public string UserId { get; private set; } = string.Empty;
    public int GroupId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public ApplicationUser? User { get; private set; }
    public Group? Group { get; private set; }

    private UserGroupMembership() { }

    public UserGroupMembership(string userId, int groupId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }
        if (groupId <= 0)
        {
            throw new ArgumentException("GroupId must be a positive integer.", nameof(groupId));
        }
        UserId = userId;
        GroupId = groupId;
        AssignedAt = DateTimeOffset.UtcNow;
    }
}
