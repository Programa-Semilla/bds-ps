// Spec 021 — see specs/021-feedback-session-may13/tasks.md T078.

namespace FundingPlatform.Application.Plantillas;

/// <summary>
/// Spec 021 / US1 / T078 — admin commands and queries for the base
/// <c>Plantilla</c> catalog. Each mutation writes an audit row via
/// <c>IAdminAuditEventWriter</c> (NFR-005). Mirrors the spec-016
/// <c>IGroupService</c> shape: one method per command record.
/// </summary>
public interface IPlantillaService
{
    /// <summary>FR-003 — lists every base Plantilla (active + archived) for the admin
    /// catalog page. Carries a snapshot-usage counter so the admin can see which
    /// Plantillas are referenced by a <c>ProcessPlantilla</c>.</summary>
    Task<IReadOnlyList<PlantillaListRow>> ListAsync(CancellationToken ct);

    /// <summary>Returns a single Plantilla for the edit form.</summary>
    Task<PlantillaDetail?> GetAsync(int id, CancellationToken ct);

    /// <summary>FR-003 — creates a base Plantilla. Writes no audit event (this is the
    /// catalog seed-side, not a Process-level mutation).</summary>
    Task<int> CreateAsync(CreatePlantillaCommand command, string actorUserId, CancellationToken ct);

    /// <summary>FR-004 — edits a base Plantilla. Snapshot independence is preserved
    /// because <c>ProcessPlantilla</c> rows hold their own column values; the EF
    /// configuration never propagates these mutations.</summary>
    Task EditAsync(EditPlantillaCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Force-detach a Plantilla from a Process. Writes audit event
    /// <c>PlantillaForceDetached</c> with the supplied reason.</summary>
    Task DetachAsync(DetachPlantillaCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Soft-archive a base Plantilla. Blocks when any ProcessPlantilla
    /// snapshot still references it (force-archive deferred — spec 021 carve-out).</summary>
    Task ArchiveAsync(ArchivePlantillaCommand command, string actorUserId, CancellationToken ct);
}

/// <summary>Spec 021 / T078 — record carrying the Create payload.
/// Spec 035 / D4 — ImpactTemplateIds removed (impact gating gone).</summary>
public sealed record CreatePlantillaCommand(
    string Name,
    int MinimumQuotationsPerItem,
    long RequiredFieldFlags);

/// <summary>Spec 021 / T078 — record carrying the Edit payload.</summary>
public sealed record EditPlantillaCommand(
    int PlantillaId,
    string Name,
    int MinimumQuotationsPerItem,
    long RequiredFieldFlags);

/// <summary>Spec 021 / T078 — record carrying the Force-detach payload.</summary>
public sealed record DetachPlantillaCommand(int PlantillaId, int ProcessId, bool Force, string? Reason);

/// <summary>Spec 021 / T078 — record carrying the Archive payload.</summary>
public sealed record ArchivePlantillaCommand(int PlantillaId);

/// <summary>Spec 021 / T078 — flat row for the catalog list.</summary>
public sealed record PlantillaListRow(
    int Id,
    string Name,
    int MinimumQuotationsPerItem,
    int AssignedProcessCount,
    bool IsArchived,
    DateTimeOffset CreatedAt);

/// <summary>Spec 021 / T078 — projection used for the edit form pre-fill.</summary>
public sealed record PlantillaDetail(
    int Id,
    string Name,
    int MinimumQuotationsPerItem,
    long RequiredFieldFlags,
    bool IsArchived,
    int AssignedProcessCount);
