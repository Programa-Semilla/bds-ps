using System.ComponentModel.DataAnnotations;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Validation;

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

    // Spec 026 — identification type; required (with the value) only when Role=Applicant,
    // enforced at the controller. Format validated server-side via [IdentificationFormat].
    [Display(Name = "Tipo de identificación")]
    public IdentificationType? IdentificationType { get; set; }

    [StringLength(50, ErrorMessage = "La identificación debe tener máximo {1} caracteres.")]
    [Display(Name = "Identificación")]
    [IdentificationFormat]
    public string? LegalId { get; set; }

    /// <summary>Spec 016 / FR-007 / FR-010 — selected group ids posted by the
    /// multi-select. Empty when the resulting role is Admin (FR-009).</summary>
    public int[] GroupIds { get; set; } = Array.Empty<int>();

    /// <summary>Spec 016 — populated by the controller from the Group catalog.
    /// Used to resolve the names of the currently-selected groups (for the chips)
    /// and to detect the empty state. Includes groups under archived Funds so an
    /// existing membership is never silently dropped on re-render. Not posted back.</summary>
    public IReadOnlyList<AdminUserGroupOption> AvailableGroups { get; set; }
        = Array.Empty<AdminUserGroupOption>();

    /// <summary>Drill-down catalog for the Fondo → Proceso → Grupo group selector.
    /// Active Funds only (archived Funds are excluded from the picker). Populated
    /// by the controller; not posted back — the posted value is still
    /// <see cref="GroupIds"/>.</summary>
    public IReadOnlyList<AdminUserFundCatalogOption> FundCatalog { get; set; }
        = Array.Empty<AdminUserFundCatalogOption>();
}

public sealed record AdminUserGroupOption(int Id, string Name);

/// <summary>One Fund (Fondo) node of the group-selector drill-down, carrying its
/// Processes (which in turn carry their Groups).</summary>
public sealed record AdminUserFundCatalogOption(
    int Id,
    string Name,
    IReadOnlyList<AdminUserFundProcessOption> Processes);

/// <summary>One Process (Proceso) node under a Fund, carrying its Groups.</summary>
public sealed record AdminUserFundProcessOption(
    int Id,
    string Name,
    IReadOnlyList<AdminUserGroupOption> Groups);
