using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 035 (evolved 2026-06-16, US3) — the line-item form: category-first, with
/// dynamic category fields, an attribution multi-select over the application's
/// declared impacts, and a short justification. The line item no longer carries its
/// own impact template/values. TechnicalSpecifications is gone.
/// </summary>
public class AddItemViewModel
{
    public int ApplicationId { get; set; }

    [Required(ErrorMessage = "El nombre del producto es obligatorio.")]
    [Display(Name = "Nombre del producto")]
    [MaxLength(500, ErrorMessage = "El nombre del producto debe tener máximo {1} caracteres.")]
    public string ProductName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La categoría es obligatoria.")]
    [Display(Name = "Categoría")]
    public int CategoryId { get; set; }

    /// <summary>Posted category field values keyed by CategoryFieldId.</summary>
    public Dictionary<int, string?> CategoryFieldValues { get; set; } = new();

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, FR-007) — posted attribution: the ids of the
    /// application impacts this line item supports (multi-select).
    /// </summary>
    [Display(Name = "Impactos relacionados")]
    public List<int> SelectedApplicationImpactIds { get; set; } = new();

    /// <summary>Spec 035 (evolved 2026-06-16, FR-008) — short impact justification (≤300).</summary>
    [Display(Name = "Justificación de impacto")]
    [MaxLength(300, ErrorMessage = "La justificación debe tener máximo {1} caracteres.")]
    public string? ImpactJustification { get; set; }

    public List<SelectListItem> Categories { get; set; } = new();

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D15) — the application's declared impacts, the only
    /// options the attribution multi-select offers (FR-007).
    /// </summary>
    public List<DeclaredImpactOption> DeclaredImpacts { get; set; } = new();

    /// <summary>Pre-rendered category field descriptors (Edit pre-fill + no-JS fallback).</summary>
    public List<DynamicFieldInput> CategoryFields { get; set; } = new();
}

/// <summary>
/// Spec 035 (evolved 2026-06-16, D15) — one declared application impact offered to the
/// per-item attribution multi-select.
/// </summary>
public class DeclaredImpactOption
{
    public int ApplicationImpactId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Spec 035 — one dynamic field descriptor + current value for the category fields.
/// DataType is the <c>ParameterDataType</c> int.
/// </summary>
public class DynamicFieldInput
{
    public int FieldId { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public int DataType { get; set; }
    public bool IsRequired { get; set; }
    public string? Value { get; set; }
}
