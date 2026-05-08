using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Suppliers.DTOs;
using Microsoft.AspNetCore.Http;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 013: rewritten to a step-flow shape. Compliance / e-invoice fields are
/// gone (admin-only per FR-040). The single POST endpoint dispatches on which
/// of SelectedBranchId / NewBranch / NewSupplier is populated.
/// </summary>
public class AddSupplierViewModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }

    [Required(ErrorMessage = "La cédula jurídica del proveedor es obligatoria.")]
    [Display(Name = "Cédula jurídica del proveedor")]
    [MaxLength(50, ErrorMessage = "La cédula jurídica debe tener máximo {1} caracteres.")]
    public string SupplierLegalId { get; set; } = string.Empty;

    /// <summary>
    /// Populated by the GET handler after the legal-ID lookup runs (or by the JS
    /// debounce hook fetching /Search). Drives which sub-form renders.
    /// </summary>
    public SupplierLookupResultDto? LookupResult { get; set; }

    /// <summary>R4 banner message ("...acaba de ser registrado por otro postulante…").</summary>
    public bool ShowConcurrentBanner { get; set; }

    /// <summary>Path 1: existing-branch selection.</summary>
    public int? SelectedBranchId { get; set; }

    /// <summary>Path 2: new branch under existing supplier.</summary>
    public AddBranchInputViewModel? NewBranch { get; set; }

    /// <summary>Path 3: brand-new Draft supplier.</summary>
    public NewSupplierInputViewModel? NewSupplier { get; set; }

    [Required(ErrorMessage = "El precio es obligatorio.")]
    [Display(Name = "Precio")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")]
    public decimal Price { get; set; }

    // Spec 015 — bound by a <select> populated with the enabled-currencies catalog
    // (CRC + USD by default). Server-side, the controller passes Currency through
    // CurrencyCode.From(...) which already enforces the 3-letter ISO shape.
    // [StringLength(3, MinimumLength = 3)] is intentionally absent: jQuery
    // Unobtrusive Validation applies it as a rangelength rule against the
    // <option>'s text content rather than its value, breaking every POST.
    [Required(ErrorMessage = "La moneda es obligatoria.")]
    [Display(Name = "Moneda")]
    public string Currency { get; set; } = string.Empty;

    [Required(ErrorMessage = "La fecha de vigencia es obligatoria.")]
    [Display(Name = "Vigente hasta")]
    public DateOnly ValidUntil { get; set; }

    [Required(ErrorMessage = "El archivo de la cotización es obligatorio.")]
    [Display(Name = "Archivo de la cotización")]
    public IFormFile? QuotationFile { get; set; }

    /// <summary>
    /// Spec 015 — enabled-currencies list used to populate the currency &lt;select&gt;.
    /// Bound by the controller from <c>dbo.Currencies WHERE IsEnabled = 1</c>
    /// ordered by <c>DisplayOrder</c>.
    /// </summary>
    public IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; } = [];
}

public class AddBranchInputViewModel
{
    [Required(ErrorMessage = "El nombre de la sucursal es obligatorio.")]
    [Display(Name = "Nombre de la sucursal")]
    [MaxLength(200, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    public string BranchName { get; set; } = string.Empty;

    [Display(Name = "Persona de contacto")]
    [MaxLength(200)]
    public string? ContactName { get; set; }

    [Display(Name = "Correo electrónico")]
    [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
    [MaxLength(256)]
    public string? Email { get; set; }

    [Display(Name = "Teléfono")]
    [Phone(ErrorMessage = "El teléfono no es válido.")]
    [MaxLength(20)]
    public string? Phone { get; set; }

    [Display(Name = "Dirección")]
    [MaxLength(500)]
    public string? AddressLine { get; set; }

    [Display(Name = "Provincia")]
    [MaxLength(100)]
    public string? Province { get; set; }

    [Display(Name = "Detalles de envío")]
    [MaxLength(500)]
    public string? ShippingDetails { get; set; }

    [Display(Name = "Información de garantía")]
    [MaxLength(500)]
    public string? WarrantyInfo { get; set; }
}

public class NewSupplierInputViewModel
{
    [Required(ErrorMessage = "La razón social es obligatoria.")]
    [Display(Name = "Razón social del proveedor")]
    [MaxLength(300, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;

    public AddBranchInputViewModel FirstBranch { get; set; } = new();
}

/// <summary>
/// Spec 015 — single enabled-currency option used to populate the multi-currency
/// dropdown on the supplier-quote Add form. Bound by the controller from
/// <c>dbo.Currencies WHERE IsEnabled = 1</c> ordered by <c>DisplayOrder</c>.
/// </summary>
public sealed record CurrencyOption(string Code, string DisplayName, string Symbol);
