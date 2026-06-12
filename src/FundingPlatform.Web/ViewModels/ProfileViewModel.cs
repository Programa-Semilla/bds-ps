// Spec 021 / US5 / T128 / FR-018 — /Profile view-model.
//
// FirstName / LastName / Phone / Address are self-editable. Email / Role /
// Group / CodigoPersonal are read-only ("administrado" badge per FR-018).
// The read-only fields render but are NOT bindable on POST: the controller
// rebuilds them from the authenticated user on every request, so a
// smuggled form field can't reach UpdateProfileCommand.

using System.ComponentModel.DataAnnotations;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

public class ProfileViewModel
{
    // Self-editable (FR-018).
    [Display(Name = "Nombre")]
    [StringLength(100)]
    public string? FirstName { get; set; }

    [Display(Name = "Apellido")]
    [StringLength(100)]
    public string? LastName { get; set; }

    [Display(Name = "Teléfono")]
    [Phone(ErrorMessage = "Ingrese un número de teléfono válido.")]
    [StringLength(40)]
    public string? Phone { get; set; }

    [Display(Name = "Dirección")]
    [StringLength(200)]
    public string? Address { get; set; }

    // Read-only ("administrado" badge) — rebuilt server-side per request.
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
    public string Group { get; init; } = "";
    public string? CodigoPersonal { get; init; }

    // Spec 026 — identification is admin-managed: shown read-only on /Profile.
    public IdentificationType? IdentificationType { get; init; }
    public string? LegalId { get; init; }

    // Spec 032 — admin-assigned User Code (applicants only): shown read-only on /Profile.
    public string? UserCode { get; init; }

    // Companion form-model for the change-password panel on /Profile.
    public ChangePasswordViewModel ChangePassword { get; set; } = new();
}
