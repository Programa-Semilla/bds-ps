namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 046 — a per-application named funding phase grouping the application's
/// <see cref="Item"/>s (budget-lines). The tranche <b>amount is not stored</b>; it is
/// derived at projection time as Σ member-line budgets (research D4/D5), so the
/// "Σ tranche = allocation" guarantee is structural, not a runtime check.
///
/// Name uniqueness within an application and the execution freeze
/// (<c>State != AgreementExecuted</c>) are enforced by the <see cref="Application"/>
/// aggregate root — a <see cref="Tranche"/> never sees its siblings. There is no
/// hard-delete on the entity: deletion goes through the aggregate, which re-parents
/// member lines to <c>TrancheId = null</c> (the synthetic default tranche).
/// </summary>
public sealed class Tranche
{
    /// <summary>Hard cap on the tranche name after trim (matches <c>dbo.Tranches.Name NVARCHAR(60)</c>).</summary>
    public const int NameMaxLength = 60;

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Ordinal { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Tranche() { }

    /// <summary>
    /// Creates a tranche for an application at the given display <paramref name="ordinal"/>.
    /// Trims the name and enforces non-empty + <see cref="NameMaxLength"/>. The aggregate root
    /// assigns the ordinal and guarantees sibling-name uniqueness before calling this.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the trimmed name is empty or too long.</exception>
    public static Tranche Create(int applicationId, string name, int ordinal)
    {
        var trimmed = Normalize(name);
        var now = DateTimeOffset.UtcNow;
        return new Tranche
        {
            ApplicationId = applicationId,
            Name = trimmed,
            Ordinal = ordinal,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    /// <summary>Renames the tranche (same trim/length guards as <see cref="Create"/>).
    /// Sibling-name uniqueness is the aggregate root's responsibility.</summary>
    public void Rename(string name)
    {
        Name = Normalize(name);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string Normalize(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Tranche name is required.", nameof(name));
        }
        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"Tranche name must be {NameMaxLength} characters or fewer.", nameof(name));
        }
        return trimmed;
    }
}
