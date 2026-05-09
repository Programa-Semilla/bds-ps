using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 018 / FR-015 / FR-016 — applicant captures the commercial entity name
/// (`Empresa solicitante`) at Application creation. The Spanish error messages
/// here are surfaced via ModelState; the entity-level invariants in
/// <c>Application.SetCompanyName</c> are the canonical source per Constitution II.
/// </summary>
public class CreateApplicationViewModel
{
    [Required(ErrorMessage = "Debe ingresar el nombre de la empresa.")]
    [StringLength(200, ErrorMessage = "El nombre de la empresa no puede exceder 200 caracteres.")]
    [Display(Name = "Empresa solicitante (nombre comercial)")]
    public string CompanyName { get; set; } = string.Empty;
}
