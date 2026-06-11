// Spec 021 — see specs/021-feedback-session-may13/tasks.md T080 / T083.

using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Plantillas;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Processes</c> (Index).
/// Spec 029 / FR-011 adds a Fund filter alongside the status filter.</summary>
public sealed class AdminProcessesIndexViewModel
{
    public IReadOnlyList<ProcessListRow> Rows { get; init; } = Array.Empty<ProcessListRow>();
    public ProcessStatus? StatusFilter { get; init; }

    /// <summary>Spec 029 / FR-011 — selected Fund filter (null = all Funds).</summary>
    public int? FundFilter { get; init; }

    /// <summary>Spec 029 / FR-011 — Fund hierarchy (all Funds incl. Archived) for
    /// the cascading filter component, rendered Fund-only on the process list.</summary>
    public IReadOnlyList<FundingPlatform.Application.Admin.Filters.FundHierarchyNode> FundHierarchy { get; init; }
        = Array.Empty<FundingPlatform.Application.Admin.Filters.FundHierarchyNode>();
}

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Processes/Create</c>.
/// Spec 029 / FR-002 adds the required Fund anchor.</summary>
public sealed class AdminProcessCreateViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Spec 029 / FR-002 — the Active Fund the new Process is anchored to.</summary>
    [Required(ErrorMessage = "Debe seleccionar un fondo activo.")]
    public int? FundId { get; set; }

    /// <summary>Active Funds available for selection.</summary>
    public IReadOnlyList<SelectListItem> FundOptions { get; set; } = Array.Empty<SelectListItem>();
}

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Processes/{id}</c> detail.
/// Carries the snapshot, groups, stage-override controls, and the list of base
/// Plantillas available for assignment (only relevant when no snapshot exists).</summary>
public sealed class AdminProcessDetailsViewModel
{
    public ProcessDetail Detail { get; init; } = null!;
    public IReadOnlyList<PlantillaListRow> AssignableBasePlantillas { get; init; } = Array.Empty<PlantillaListRow>();
    public IReadOnlyList<string> CloseBlockingPublicCodes { get; init; } = Array.Empty<string>();

    /// <summary>Spec 029 / FR-009 — Active Funds available as reassignment targets.</summary>
    public IReadOnlyList<SelectListItem> FundOptions { get; init; } = Array.Empty<SelectListItem>();
}
