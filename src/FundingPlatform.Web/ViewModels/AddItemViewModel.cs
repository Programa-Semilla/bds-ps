using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 035 / US2 — the line-item form: category-first, with dynamic category
/// fields + per-item impact (any active template). TechnicalSpecifications is gone.
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

    [Display(Name = "Plantilla de impacto")]
    public int? ImpactTemplateId { get; set; }

    /// <summary>Posted impact parameter values keyed by ImpactTemplateParameterId.</summary>
    public Dictionary<int, string?> ImpactParameterValues { get; set; } = new();

    public List<SelectListItem> Categories { get; set; } = new();
    public List<ItemImpactTemplateOption> ImpactTemplates { get; set; } = new();

    /// <summary>Pre-rendered category field descriptors (Edit pre-fill + no-JS fallback).</summary>
    public List<DynamicFieldInput> CategoryFields { get; set; } = new();

    /// <summary>Pre-rendered impact parameter descriptors (Edit pre-fill).</summary>
    public List<DynamicFieldInput> ImpactParameters { get; set; } = new();
}

/// <summary>Spec 035 — active impact-template option for the per-item picker.</summary>
public class ItemImpactTemplateOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Spec 035 — one dynamic field descriptor + current value, shared by category
/// fields and impact parameters. DataType is the <c>ParameterDataType</c> int.
/// </summary>
public class DynamicFieldInput
{
    public int FieldId { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public int DataType { get; set; }
    public bool IsRequired { get; set; }
    public string? Value { get; set; }
}
