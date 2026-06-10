// Spec 029 — see specs/029-fund-entity/contracts/ui-and-routes.md (Admin Fund management).

using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Funds;
using FundingPlatform.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 029 / US1 — ViewModel for <c>/Admin/Funds</c> (Index).</summary>
public sealed class AdminFundsIndexViewModel
{
    public IReadOnlyList<FundListRow> Rows { get; init; } = Array.Empty<FundListRow>();
    public FundStatus? StatusFilter { get; init; }
}

/// <summary>Spec 029 / US1 — ViewModel for <c>/Admin/Funds/Create</c>.</summary>
public sealed class AdminFundCreateViewModel
{
    [Required(ErrorMessage = "El nombre del fondo es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    [Display(Name = "Nombre del fondo")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(2000, ErrorMessage = "La descripción debe tener 2000 caracteres o menos.")]
    [Display(Name = "Descripción")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Reglamento (PDF, opcional)")]
    public IFormFile? RegulationFile { get; set; }
}

/// <summary>Spec 029 / US1 — ViewModel for <c>/Admin/Funds/{id}/Edit</c>.</summary>
public sealed class AdminFundEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre del fondo es obligatorio.")]
    [StringLength(120, ErrorMessage = "El nombre debe tener 120 caracteres o menos.")]
    [Display(Name = "Nombre del fondo")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(2000, ErrorMessage = "La descripción debe tener 2000 caracteres o menos.")]
    [Display(Name = "Descripción")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>Spec 029 / US1 — ViewModel for <c>/Admin/Funds/{id}</c> (Details).</summary>
public sealed class AdminFundDetailsViewModel
{
    public FundDetail Detail { get; init; } = null!;
}
