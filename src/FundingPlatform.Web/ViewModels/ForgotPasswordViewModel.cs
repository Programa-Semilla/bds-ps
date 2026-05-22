// Spec 021 / US5 / T128 / FR-028.

using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido.")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";
}
