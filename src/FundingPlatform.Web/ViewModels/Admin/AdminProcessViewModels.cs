// Spec 021 — see specs/021-feedback-session-may13/tasks.md T080 / T083.

using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Plantillas;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Processes</c> (Index).</summary>
public sealed class AdminProcessesIndexViewModel
{
    public IReadOnlyList<ProcessListRow> Rows { get; init; } = Array.Empty<ProcessListRow>();
    public ProcessStatus? StatusFilter { get; init; }
}

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Processes/Create</c>.</summary>
public sealed class AdminProcessCreateViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Processes/{id}</c> detail.
/// Carries the snapshot, groups, stage-override controls, and the list of base
/// Plantillas available for assignment (only relevant when no snapshot exists).</summary>
public sealed class AdminProcessDetailsViewModel
{
    public ProcessDetail Detail { get; init; } = null!;
    public IReadOnlyList<PlantillaListRow> AssignableBasePlantillas { get; init; } = Array.Empty<PlantillaListRow>();
    public IReadOnlyList<string> CloseBlockingPublicCodes { get; init; } = Array.Empty<string>();
}
