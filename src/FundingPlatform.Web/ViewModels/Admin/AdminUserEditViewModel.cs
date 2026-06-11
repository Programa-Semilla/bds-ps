using System.ComponentModel.DataAnnotations;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Validation;

namespace FundingPlatform.Web.ViewModels.Admin;

public class AdminUserEditViewModel
{
    [Required]
    public string UserId { get; set; } = "";

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Los apellidos son obligatorios.")]
    [StringLength(100, ErrorMessage = "Los apellidos deben tener máximo {1} caracteres.")]
    [Display(Name = "Apellidos")]
    public string LastName { get; set; } = "";

    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
    [StringLength(256, ErrorMessage = "El correo electrónico debe tener máximo {1} caracteres.")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";

    [Phone(ErrorMessage = "El teléfono no es válido.")]
    [Display(Name = "Teléfono")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "El rol es obligatorio.")]
    [Display(Name = "Rol")]
    public string Role { get; set; } = "Applicant";

    // Spec 026 — identification type; required (with the value) only when Role=Applicant.
    [Display(Name = "Tipo de identificación")]
    public IdentificationType? IdentificationType { get; set; }

    [StringLength(50, ErrorMessage = "La identificación debe tener máximo {1} caracteres.")]
    [Display(Name = "Identificación")]
    [IdentificationFormat]
    public string? LegalId { get; set; }

    /// <summary>Spec 016 / FR-008 / FR-010 — selected group ids.</summary>
    public int[] GroupIds { get; set; } = Array.Empty<int>();

    /// <summary>Spec 016 — populated by the controller; resolves selected-group
    /// names for the chips and drives the empty state. Includes archived-Fund
    /// groups so an existing membership is never dropped on re-render. Not posted back.</summary>
    public IReadOnlyList<AdminUserGroupOption> AvailableGroups { get; set; }
        = Array.Empty<AdminUserGroupOption>();

    /// <summary>Drill-down catalog (Fondo → Proceso → Grupo) for the group
    /// selector. Active Funds only. Populated by the controller; not posted back.</summary>
    public IReadOnlyList<AdminUserFundCatalogOption> FundCatalog { get; set; }
        = Array.Empty<AdminUserFundCatalogOption>();

    /// <summary>Spec 016 — round-trips the existing
    /// <c>IdentityUser.ConcurrencyStamp</c> for optimistic concurrency.</summary>
    public string? ConcurrencyStamp { get; set; }
}
