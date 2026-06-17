using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 037 / FR-002 / FR-012–FR-014 — the applicant selects an admin-assigned
/// company at Application creation (controlled dropdown, replacing the spec-018
/// free-text name). Resolution mirrors the spec-029 Group anchor: 0 companies →
/// blocked; 1 → auto-selected and hidden; ≥2 → required choice. The posted
/// <see cref="CompanyId"/> is validated against the applicant's active companies
/// server-side (FR-018/019).
///
/// Spec 029 / FR-017 / FR-018 — the applicant also anchors the application to an
/// eligible Group (Process/convocatoria) under an Active Fund. Same 0/1/many rules.
/// </summary>
public class CreateApplicationViewModel
{
    /// <summary>
    /// Spec 037 / FR-002 — the chosen company. Required; validated against the
    /// applicant's active companies server-side.
    /// </summary>
    [Required(ErrorMessage = "Debe seleccionar una empresa.")]
    [Display(Name = "Empresa solicitante")]
    public int? CompanyId { get; set; }

    /// <summary>The applicant's active companies, labelled by name.</summary>
    public IReadOnlyList<SelectListItem> Companies { get; set; } = [];

    /// <summary>True when the applicant has no active company (blocks creation, FR-014).</summary>
    public bool HasNoCompanies { get; set; }

    /// <summary>True when exactly one company is active (auto-selected, hidden field, FR-012).</summary>
    public bool IsSingleCompany { get; set; }

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
