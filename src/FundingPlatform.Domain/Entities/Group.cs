namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 016 — a named partition used to scope reviewer access. Admins manage the
/// catalog; non-admin users (Applicants, Reviewers) inherit visibility through
/// their <see cref="UserGroupMembership"/> rows. The Admin role MUST never carry
/// memberships — that invariant is enforced at the Web/Service boundary.
/// </summary>
public class Group
{
    /// <summary>Maximum length of <see cref="Name"/> after trimming. Mirrors
    /// `dbo.Groups.Name NVARCHAR(100)`.</summary>
    public const int MaxNameLength = 100;

    private readonly List<UserGroupMembership> _memberships = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<UserGroupMembership> Memberships => _memberships.AsReadOnly();

    private Group() { }

    private Group(string name)
    {
        Name = name;
        var now = DateTimeOffset.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>FR-001 — group name MUST be non-empty (after trim) and ≤ 100 chars.
    /// Uniqueness is enforced by the unique index on <c>dbo.Groups.Name</c>.</summary>
    public static Group Create(string name)
    {
        var trimmed = ValidateName(name);
        return new Group(trimmed);
    }

    /// <summary>FR-006 — rename preserves the row identity (and therefore every
    /// existing membership row). Idempotent if the trimmed name equals the
    /// current name.</summary>
    public void Rename(string newName)
    {
        var trimmed = ValidateName(newName);
        if (string.Equals(trimmed, Name, StringComparison.Ordinal))
        {
            return;
        }
        Name = trimmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ValidateName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("Group name is required.", nameof(name));
        }
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Group name is required.", nameof(name));
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Group name must be {MaxNameLength} characters or fewer.", nameof(name));
        }
        return trimmed;
    }
}
