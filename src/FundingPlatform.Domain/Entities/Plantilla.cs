// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Plantilla aggregate)
// and research.md OQ-1 (one ProcessPlantilla per Process).

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / FR-003 — base catalogue entity owned by *Administración*. Holds the
/// minimum-quotations-per-item rule, a bitfield of required-field flags, and a
/// many-to-many list of <see cref="ImpactTemplate"/> rows that the assigned
/// <see cref="Process"/> may pick from when applicants set their Impact.
///
/// Assignment is a copy-on-write operation: <see cref="AssignTo"/> returns a
/// frozen <see cref="ProcessPlantilla"/> snapshot. Subsequent <see cref="Edit"/>
/// calls mutate the base catalog only — already-assigned snapshots are
/// independent (FR-004).
/// </summary>
public class Plantilla
{
    public const int MaxNameLength = 120;

    private readonly List<ImpactTemplate> _impactTemplates = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int MinimumQuotationsPerItem { get; private set; }
    public long RequiredFieldFlags { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Many-to-many: candidate ImpactTemplates available when a Process built from
    /// this Plantilla collects an applicant's Impact pick. EF-mapped via the
    /// <c>PlantillaImpactTemplates</c> join table.
    /// </summary>
    public ICollection<ImpactTemplate> ImpactTemplates => _impactTemplates;

    private Plantilla() { }

    private Plantilla(string name, int minimumQuotationsPerItem, long requiredFieldFlags, DateTimeOffset now)
    {
        Name = name;
        MinimumQuotationsPerItem = minimumQuotationsPerItem;
        RequiredFieldFlags = requiredFieldFlags;
        CreatedAt = now;
    }

    /// <summary>
    /// Factory: produces a new base Plantilla in the unarchived state.
    /// </summary>
    public static Plantilla Create(string name, int minimumQuotationsPerItem, long requiredFieldFlags)
    {
        var trimmedName = ValidateName(name);
        if (minimumQuotationsPerItem <= 0)
        {
            throw new ArgumentException(
                "MinimumQuotationsPerItem must be positive.", nameof(minimumQuotationsPerItem));
        }
        return new Plantilla(trimmedName, minimumQuotationsPerItem, requiredFieldFlags, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Mutates the base Plantilla. Per FR-004 these mutations MUST NOT propagate
    /// into already-assigned <see cref="ProcessPlantilla"/> snapshots; that
    /// independence is preserved because the snapshot copied the relevant fields
    /// at <see cref="AssignTo"/> time and the snapshot's setters are not exposed.
    /// </summary>
    public void Edit(string newName, int minimumQuotationsPerItem, long requiredFieldFlags)
    {
        if (IsArchived)
        {
            throw new InvalidOperationException("Cannot edit an archived Plantilla.");
        }
        Name = ValidateName(newName);
        if (minimumQuotationsPerItem <= 0)
        {
            throw new ArgumentException(
                "MinimumQuotationsPerItem must be positive.", nameof(minimumQuotationsPerItem));
        }
        MinimumQuotationsPerItem = minimumQuotationsPerItem;
        RequiredFieldFlags = requiredFieldFlags;
    }

    /// <summary>
    /// Attaches an ImpactTemplate to the base Plantilla. Idempotent.
    /// </summary>
    public void AttachImpactTemplate(ImpactTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (_impactTemplates.Any(t => t.Id == template.Id))
        {
            return;
        }
        _impactTemplates.Add(template);
    }

    /// <summary>
    /// Detaches an ImpactTemplate from the base Plantilla. Idempotent.
    /// </summary>
    public void DetachImpactTemplate(int impactTemplateId)
    {
        var existing = _impactTemplates.FirstOrDefault(t => t.Id == impactTemplateId);
        if (existing is not null)
        {
            _impactTemplates.Remove(existing);
        }
    }

    /// <summary>
    /// Soft-deletes the base Plantilla. Already-assigned Process snapshots are
    /// unaffected (FR-004).
    /// </summary>
    public void Archive()
    {
        if (IsArchived) return;
        IsArchived = true;
    }

    /// <summary>
    /// Spec 021 / FR-003 / FR-004 / OQ-1 — produces a frozen
    /// <see cref="ProcessPlantilla"/> snapshot and binds it to <paramref name="target"/>.
    /// Validates: target Process is Active, has no existing ProcessPlantilla
    /// (one-to-one per OQ-1), and this Plantilla has ≥ 1 attached ImpactTemplate.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the target Process already has a Plantilla, the source
    /// Plantilla is archived, or there are no attached ImpactTemplates.
    /// </exception>
    public ProcessPlantilla AssignTo(Process target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsArchived)
        {
            throw new InvalidOperationException("Cannot assign an archived Plantilla.");
        }
        if (target.Plantilla is not null)
        {
            throw new InvalidOperationException(
                $"Process {target.Id} already has a Plantilla snapshot (OQ-1: one-to-one).");
        }
        if (_impactTemplates.Count == 0)
        {
            throw new InvalidOperationException(
                "Plantilla must have ≥ 1 ImpactTemplate attached before assignment.");
        }

        var snapshot = new ProcessPlantilla(
            processId: target.Id,
            sourcePlantillaId: Id,
            minimumQuotationsPerItem: MinimumQuotationsPerItem,
            requiredFieldFlags: RequiredFieldFlags,
            impactTemplateIds: _impactTemplates.Select(t => t.Id),
            assignedAt: DateTimeOffset.UtcNow);

        // Bind to the parent Process so the EF nav property reflects the new
        // snapshot immediately. The Application layer is responsible for
        // raising the PlantillaAssignedToProcess audit event.
        target.Plantilla = snapshot;

        return snapshot;
    }

    /// <summary>
    /// Force-detaches the assigned snapshot from <paramref name="target"/>.
    /// Without <paramref name="force"/>, blocked when Applications already depend
    /// on the snapshot — that check lives at the Application layer (Domain has no
    /// repository access). With <paramref name="force"/> + <paramref name="reason"/>,
    /// the caller is also responsible for writing a <c>PlantillaForceDetached</c>
    /// audit event with the reason payload.
    /// </summary>
    /// <exception cref="ArgumentException">force=true with a null/empty reason.</exception>
    /// <exception cref="InvalidOperationException">No snapshot is attached to target.</exception>
    public void Detach(Process target, bool force, string? reason)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Plantilla is null)
        {
            throw new InvalidOperationException(
                $"Process {target.Id} has no Plantilla snapshot to detach.");
        }
        if (target.Plantilla.SourcePlantillaId != Id)
        {
            throw new InvalidOperationException(
                $"Process {target.Id} snapshot is sourced from a different Plantilla.");
        }
        if (force && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required for force-detach.", nameof(reason));
        }

        target.Plantilla = null;
    }

    private static string ValidateName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("Plantilla name is required.", nameof(name));
        }
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Plantilla name is required.", nameof(name));
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Plantilla name must be {MaxNameLength} characters or fewer.", nameof(name));
        }
        return trimmed;
    }
}
