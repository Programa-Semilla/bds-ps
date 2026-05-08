using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels.Admin;

public class AdminUserCreateViewModel
{
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

    [Required(ErrorMessage = "La contraseña inicial es obligatoria.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
    [Display(Name = "Contraseña inicial")]
    public string InitialPassword { get; set; } = "";

    [StringLength(50, ErrorMessage = "La cédula debe tener máximo {1} caracteres.")]
    [Display(Name = "Cédula")]
    public string? LegalId { get; set; }

    /// <summary>Spec 016 / FR-007 / FR-010 — selected group ids posted by the
    /// multi-select. Empty when the resulting role is Admin (FR-009).</summary>
    public int[] GroupIds { get; set; } = Array.Empty<int>();

    /// <summary>Spec 016 — populated by the controller from the Group catalog
    /// to render the multi-select. Not posted back.</summary>
    public IReadOnlyList<AdminUserGroupOption> AvailableGroups { get; set; }
        = Array.Empty<AdminUserGroupOption>();
}

public sealed record AdminUserGroupOption(int Id, string Name);
