// Spec 021 — see specs/021-feedback-session-may13/tasks.md T081 / T083.

using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Plantillas;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Plantillas</c> (Index).</summary>
public sealed class AdminPlantillasIndexViewModel
{
    public IReadOnlyList<PlantillaListRow> Rows { get; init; } = Array.Empty<PlantillaListRow>();
}

/// <summary>Spec 021 / US1 — option row used to populate the multi-select on the
/// Plantilla create / edit forms.</summary>
public sealed record AdminPlantillaImpactTemplateOption(int Id, string Name);

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Plantillas/Create</c>.</summary>
public sealed class AdminPlantillaCreateViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 10, ErrorMessage = "Mínimo de cotizaciones por ítem debe estar entre 1 y 10.")]
    public int MinimumQuotationsPerItem { get; set; } = 3;

    /// <summary>Encoded as a bit-mask of <c>RequiredFieldKind</c> values (FR-003).
    /// Bound to a multi-checkbox group in the view.</summary>
    public long RequiredFieldFlags { get; set; }

    public int[] ImpactTemplateIds { get; set; } = Array.Empty<int>();

    public IReadOnlyList<AdminPlantillaImpactTemplateOption> AvailableImpactTemplates { get; set; }
        = Array.Empty<AdminPlantillaImpactTemplateOption>();
}

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Plantillas/{id}/Edit</c>.</summary>
public sealed class AdminPlantillaEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 10, ErrorMessage = "Mínimo de cotizaciones por ítem debe estar entre 1 y 10.")]
    public int MinimumQuotationsPerItem { get; set; } = 3;

    public long RequiredFieldFlags { get; set; }
    public int[] ImpactTemplateIds { get; set; } = Array.Empty<int>();
    public bool IsArchived { get; set; }
    public int AssignedProcessCount { get; set; }

    public IReadOnlyList<AdminPlantillaImpactTemplateOption> AvailableImpactTemplates { get; set; }
        = Array.Empty<AdminPlantillaImpactTemplateOption>();
}
