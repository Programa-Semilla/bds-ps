// Spec 021 / US5 / T128 / FR-028.

using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels;

public class ResetPasswordViewModel
{
    public string UserId { get; set; } = "";
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
    [Display(Name = "Nueva contraseña")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Debe confirmar la nueva contraseña.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Confirmar nueva contraseña")]
    public string ConfirmPassword { get; set; } = "";
}
