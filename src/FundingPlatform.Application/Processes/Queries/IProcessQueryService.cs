// Spec 021 — see specs/021-feedback-session-may13/tasks.md T079.

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Processes.Queries;

/// <summary>
/// Spec 021 / US1 / T079 — read-only projections that drive the admin
/// <c>/Admin/Processes</c> surfaces. Backed by EF directly in
/// <c>FundingPlatform.Infrastructure</c>; mirrors the spec-016
/// <c>IGroupService.ListAsync</c> projection shape.
/// </summary>
public interface IProcessQueryService
{
    /// <summary>FR-001 — lists every Process for the admin index, optionally
    /// filtered by <see cref="ProcessStatus"/>.</summary>
    Task<IReadOnlyList<ProcessListRow>> ListAsync(ProcessStatus? statusFilter, CancellationToken ct);

    /// <summary>Returns the Process detail used by <c>/Admin/Processes/{id}</c> —
    /// includes the attached <c>ProcessPlantilla</c> snapshot, groups, and
    /// per-stage overrides. Null if the process is not found.</summary>
    Task<ProcessDetail?> GetDetailAsync(int processId, CancellationToken ct);
}

/// <summary>Spec 021 / T079 — flat row used on the admin Process index.</summary>
public sealed record ProcessListRow(
    int Id,
    string Name,
    ProcessStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int GroupCount,
    string? PlantillaName);

/// <summary>Spec 021 / T079 — projection used on the admin Process detail view.</summary>
public sealed record ProcessDetail(
    int Id,
    string Name,
    ProcessStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int? SolicitudWindowDays,
    int? RevisionWindowDays,
    int? FacturacionWindowDays,
    ProcessPlantillaSnapshotDto? Plantilla,
    IReadOnlyList<ProcessGroupRow> Groups);

/// <summary>Spec 021 / T079 — group-row helper for the detail view.</summary>
public sealed record ProcessGroupRow(int Id, string Name, int MemberCount);

/// <summary>Spec 021 / T079 — denormalized snapshot DTO; carries the resolved
/// names of the <c>ImpactTemplateIdsCsv</c> for display.</summary>
public sealed record ProcessPlantillaSnapshotDto(
    int Id,
    int SourcePlantillaId,
    string SourcePlantillaName,
    int MinimumQuotationsPerItem,
    long RequiredFieldFlags,
    IReadOnlyList<int> ImpactTemplateIds,
    IReadOnlyList<string> ImpactTemplateNames,
    DateTimeOffset AssignedAt);
