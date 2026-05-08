namespace FundingPlatform.Application.Admin.Groups;

/// <summary>
/// Spec 016 — admin-only group catalog management. Every mutation accepts an
/// <c>actorUserId</c> because NFR-005 requires the actor on every audit row.
/// </summary>
public interface IGroupService
{
    /// <summary>Returns every group ordered by Name asc, with the current member count.</summary>
    Task<IReadOnlyList<GroupRow>> ListAsync(CancellationToken ct);

    /// <summary>Returns a single group's detail or null if missing.</summary>
    Task<GroupDetail?> GetAsync(int id, CancellationToken ct);

    /// <summary>FR-001 — creates a group. Throws <see cref="DuplicateGroupNameException"/>
    /// when the name collides (case- and accent-insensitive) with an existing group.</summary>
    Task<int> CreateAsync(string name, string actorUserId, CancellationToken ct);

    /// <summary>FR-006 — renames a group; preserves every existing membership row.
    /// Throws <see cref="DuplicateGroupNameException"/> when the new name collides
    /// with another group. No-op renames still write an audit row to keep the
    /// trail honest (per contracts/admin-groups.md).</summary>
    Task RenameAsync(int id, string newName, string actorUserId, CancellationToken ct);

    /// <summary>FR-004, FR-005 — deletes a group; cascades through
    /// <c>UserGroupMemberships</c>. User records are NOT deleted. Returns the
    /// number of memberships removed (for the audit payload).</summary>
    Task<int> DeleteAsync(int id, string actorUserId, CancellationToken ct);
}
