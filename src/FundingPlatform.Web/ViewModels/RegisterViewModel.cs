using System.ComponentModel.DataAnnotations;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Validation;

namespace FundingPlatform.Web.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    [MinLength(6, ErrorMessage = "La contraseña debe tener al menos {1} caracteres.")]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debe confirmar la contraseña.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar contraseña")]
    [Compare("Password", ErrorMessage = "Las contraseñas no coinciden.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [Display(Name = "Nombre")]
    [MaxLength(100, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Los apellidos son obligatorios.")]
    [Display(Name = "Apellidos")]
    [MaxLength(100, ErrorMessage = "Los apellidos deben tener máximo {1} caracteres.")]
    public string LastName { get; set; } = string.Empty;

    // Spec 026 — type-aware identification. The selector drives the client mask
    // and the server [IdentificationFormat] validation below.
    [Required(ErrorMessage = "Seleccione el tipo de identificación.")]
    [Display(Name = "Tipo de identificación")]
    public IdentificationType? IdentificationType { get; set; }

    [Required(ErrorMessage = "La identificación es obligatoria.")]
    [Display(Name = "Identificación")]
    [MaxLength(50, ErrorMessage = "La identificación debe tener máximo {1} caracteres.")]
    [IdentificationFormat]
    public string LegalId { get; set; } = string.Empty;
}
