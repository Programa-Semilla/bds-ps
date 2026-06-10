using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 018 / FR-015 / FR-016 — applicant captures the commercial entity name
/// (`Empresa solicitante`) at Application creation. The Spanish error messages
/// here are surfaced via ModelState; the entity-level invariants in
/// <c>Application.SetCompanyName</c> are the canonical source per Constitution II.
///
/// Spec 029 / FR-017 / FR-018 — the applicant also anchors the application to an
/// eligible Group (Process/convocatoria) under an Active Fund. Resolution rules:
/// 0 eligible → blocked; 1 eligible → auto-selected and hidden; ≥2 → required choice.
/// </summary>
public class CreateApplicationViewModel
{
    [Required(ErrorMessage = "Debe ingresar el nombre de la empresa.")]
    [StringLength(200, ErrorMessage = "El nombre de la empresa no puede exceder 200 caracteres.")]
    [Display(Name = "Empresa solicitante (nombre comercial)")]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Spec 029 / FR-018 — the chosen Group anchor. Required; validated against
    /// the applicant's eligible set server-side.
    /// </summary>
    [Required(ErrorMessage = "Debe seleccionar el proceso al que desea postular.")]
    [Display(Name = "Proceso (convocatoria)")]
    public int? GroupId { get; set; }

    /// <summary>The applicant's eligible Groups, labelled by Process name.</summary>
    public IReadOnlyList<SelectListItem> EligibleGroups { get; set; } = [];

    /// <summary>True when the applicant has no eligible Group (blocks creation).</summary>
    public bool HasNoEligibleGroups { get; set; }

    /// <summary>True when exactly one Group is eligible (auto-selected, hidden field).</summary>
    public bool IsSingleEligibleGroup { get; set; }
}
