// Spec 021 — see specs/021-feedback-session-may13/tasks.md T081 / T083.

using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Plantillas;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Plantillas</c> (Index).</summary>
public sealed class AdminPlantillasIndexViewModel
{
    public IReadOnlyList<PlantillaListRow> Rows { get; init; } = Array.Empty<PlantillaListRow>();
}

/// <summary>Spec 021 / US1 — ViewModel for <c>/Admin/Plantillas/Create</c>.</summary>
public sealed class AdminPlantillaCreateViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 10, ErrorMessage = "Mínimo de cotizaciones por ítem debe estar entre 1 y 10.")]
    public int MinimumQuotationsPerItem { get; set; } = 3;

    /// <summary>Bound from the "Campos requeridos" multi-checkbox group — one
    /// single-bit value per checked box. The group cannot bind to the scalar
    /// <see cref="RequiredFieldFlags"/> directly: repeated form keys collapse to
    /// the first value, silently dropping every flag but the lowest bit.</summary>
    public long[] RequiredFieldFlagBits { get; set; } = Array.Empty<long>();

    /// <summary>FR-003 bit-mask of <c>RequiredFieldKind</c> values, OR-folded
    /// from the checked <see cref="RequiredFieldFlagBits"/>.</summary>
    public long RequiredFieldFlags =>
        RequiredFieldFlagBits.Aggregate(0L, (mask, bit) => mask | bit);
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

    /// <summary>Bound from the "Campos requeridos" multi-checkbox group — one
    /// single-bit value per checked box. The group cannot bind to the scalar
    /// <see cref="RequiredFieldFlags"/> directly: repeated form keys collapse to
    /// the first value, silently dropping every flag but the lowest bit.</summary>
    public long[] RequiredFieldFlagBits { get; set; } = Array.Empty<long>();

    /// <summary>FR-003 bit-mask of <c>RequiredFieldKind</c> values, OR-folded
    /// from the checked <see cref="RequiredFieldFlagBits"/>.</summary>
    public long RequiredFieldFlags =>
        RequiredFieldFlagBits.Aggregate(0L, (mask, bit) => mask | bit);

    public bool IsArchived { get; set; }
    public int AssignedProcessCount { get; set; }
}
