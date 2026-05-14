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

    /// <summary>
    /// Spec 021 / FR-001 — every Group belongs to exactly one <see cref="Process"/>.
    /// The "Migración inicial" Process is seeded by PostDeployment so legacy rows
    /// are not orphaned during the cutover.
    /// </summary>
    public int ProcessId { get; private set; }
    public Process? Process { get; private set; }

    public IReadOnlyCollection<UserGroupMembership> Memberships => _memberships.AsReadOnly();

    private Group() { }

    private Group(string name, int processId)
    {
        Name = name;
        ProcessId = processId;
        var now = DateTimeOffset.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>FR-001 — group name MUST be non-empty (after trim) and ≤ 100 chars.
    /// Uniqueness is enforced by the unique index on <c>dbo.Groups.Name</c>.
    /// Pre-021 overload: groups created here are detached from any Process and
    /// must have <see cref="ProcessId"/> assigned by the Application layer
    /// (which knows the active "Migración inicial" Process).</summary>
    public static Group Create(string name)
    {
        var trimmed = ValidateName(name);
        return new Group(trimmed, processId: 0);
    }

    /// <summary>Spec 021 FR-001 — every Group is attached to a Process at creation.</summary>
    public static Group Create(string name, int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentException("ProcessId must be a positive integer.", nameof(processId));
        }
        var trimmed = ValidateName(name);
        return new Group(trimmed, processId);
    }

    /// <summary>Spec 021 FR-001 — admin reparents a Group to a different Process.</summary>
    public void MoveToProcess(int newProcessId)
    {
        if (newProcessId <= 0)
        {
            throw new ArgumentException("ProcessId must be a positive integer.", nameof(newProcessId));
        }
        if (newProcessId == ProcessId)
        {
            return;
        }
        ProcessId = newProcessId;
        UpdatedAt = DateTimeOffset.UtcNow;
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
