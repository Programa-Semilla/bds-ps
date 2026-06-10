// Spec 021 — see specs/021-feedback-session-may13/tasks.md T077.

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Processes;

/// <summary>
/// Spec 021 / US1 — admin commands for the Process aggregate. Each mutation
/// stages an <see cref="FundingPlatform.Domain.Entities.AdminAuditEvent"/>
/// via the spec-021 <c>IAdminAuditEventWriter</c> seam and commits in the
/// same UnitOfWork. Mirrors the spec-016 <c>IGroupService</c> shape: one
/// method per command record under <c>Processes/</c>.
/// </summary>
public interface IProcessService
{
    /// <summary>FR-001 — creates a new Process; writes audit event <c>ProcessCreated</c>.</summary>
    Task<int> CreateAsync(CreateProcessCommand command, string actorUserId, CancellationToken ct);

    /// <summary>OQ-2 — transitions Status to Closed. Guards: no Active Applications
    /// attached via Groups. Writes audit event <c>ProcessClosed</c>.</summary>
    Task CloseAsync(CloseProcessCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Spec 029 / FR-009 — reassigns the Process to another Active Fund.
    /// Rejects a missing/Archived target Fund and a Closed Process.</summary>
    Task ReassignFundAsync(ReassignProcessFundCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Spec 030 / FR-003 — renames the Process. Writes audit event
    /// <c>ProcessRenamed</c>. A no-op write (the trimmed new name equals the
    /// current name) persists nothing and writes no audit row (FR-006). Allowed
    /// at any status, including Closed (FR-002).</summary>
    Task RenameAsync(RenameProcessCommand command, string actorUserId, CancellationToken ct);

    /// <summary>FR-006 / OQ-3 — sets or clears a per-Process stage-window override.
    /// Writes audit event <c>ProcessStageWindowOverridden</c>.</summary>
    Task OverrideStageWindowAsync(OverrideStageWindowCommand command, string actorUserId, CancellationToken ct);

    /// <summary>FR-003 / FR-004 — attaches a base Plantilla to the Process, producing a
    /// <c>ProcessPlantilla</c> snapshot. Writes audit event
    /// <c>PlantillaAssignedToProcess</c>. Returns the snapshot id.</summary>
    Task<int> AssignPlantillaAsync(AssignPlantillaCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Returns active Applications by PublicCode for the given Process — used
    /// when <see cref="CloseAsync"/> needs to enumerate offenders on the 422 path.</summary>
    Task<IReadOnlyList<string>> ListBlockingActiveApplicationPublicCodesAsync(int processId, CancellationToken ct);
}

/// <summary>Spec 021 / T077 — record carrying the Create payload. Spec 029 /
/// FR-002 adds the required <paramref name="FundId"/> (the Active Fund the new
/// Process is anchored to).</summary>
public sealed record CreateProcessCommand(string Name, int FundId);

/// <summary>Spec 021 / T077 — record carrying the Close payload.</summary>
public sealed record CloseProcessCommand(int ProcessId);

/// <summary>Spec 029 / FR-009 — record carrying the Fund-reassignment payload.</summary>
public sealed record ReassignProcessFundCommand(int ProcessId, int FundId);

/// <summary>Spec 030 / FR-003 — record carrying the rename payload. Mirrors
/// <see cref="ReassignProcessFundCommand"/>. No-op writes no audit; allowed at
/// any status.</summary>
public sealed record RenameProcessCommand(int ProcessId, string NewName);

/// <summary>Spec 021 / T077 — record carrying the stage-window override payload.
/// <paramref name="Days"/> null = revert to platform default.</summary>
public sealed record OverrideStageWindowCommand(int ProcessId, StageKind StageKind, int? Days);

/// <summary>Spec 021 / T077 — record carrying the Plantilla-assignment payload.</summary>
public sealed record AssignPlantillaCommand(int ProcessId, int PlantillaId);
