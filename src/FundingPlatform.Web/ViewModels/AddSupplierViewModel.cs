using System.ComponentModel.DataAnnotations;
using FundingPlatform.Application.Suppliers.DTOs;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Validation;
using Microsoft.AspNetCore.Http;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 013: rewritten to a step-flow shape. Compliance / e-invoice fields are
/// gone (admin-only per FR-040). The single POST endpoint dispatches on which
/// of SelectedBranchId / NewBranch / NewSupplier is populated.
/// </summary>
public class AddSupplierViewModel : IQuoteFieldsModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }

    // Spec 026 — supplier identification kind (Cédula jurídica or NITE). The
    // sibling-property name is passed explicitly to [IdentificationFormat] since it
    // differs from the attribute's default "IdentificationType".
    [Display(Name = "Tipo de identificación")]
    public IdentificationType? SupplierIdentificationType { get; set; } = IdentificationType.CedulaJuridica;

    [Required(ErrorMessage = "La identificación del proveedor es obligatoria.")]
    [Display(Name = "Identificación del proveedor")]
    [MaxLength(50, ErrorMessage = "La identificación debe tener máximo {1} caracteres.")]
    [IdentificationFormat(nameof(SupplierIdentificationType))]
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

    // Spec 035 / US3 — optional: required only on the add-new path (the controller
    // enforces it). Omitted when reusing an existing quotation's document.
    [Display(Name = "Archivo de la cotización")]
    public IFormFile? QuotationFile { get; set; }

    /// <summary>Spec 035 / US3 — when set, reuse this sibling quotation's supplier + document.</summary>
    public int? SourceQuotationId { get; set; }

    /// <summary>Spec 035 / US3 — reuse candidates (quotations on the application's other items).</summary>
    public IReadOnlyList<ReusableQuotationOption> ReusableQuotations { get; set; } = [];

    /// <summary>
    /// Spec 015 — enabled-currencies list used to populate the currency &lt;select&gt;.
    /// Bound by the controller from <c>dbo.Currencies WHERE IsEnabled = 1</c>
    /// ordered by <c>DisplayOrder</c>.
    /// </summary>
    public IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; } = [];
}

/// <summary>Spec 035 / US3 — one reuse candidate shown in the picker.</summary>
public class ReusableQuotationOption
{
    public int SourceQuotationId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string DocumentFileName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
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

    // Spec 025 — Provincia → Cantón → Distrito cascade FK ids (replace the spec-013
    // free-text Province input). All three required server-side on this surface; the
    // composed "Distrito, Cantón, Provincia" display string is set by the controller,
    // not posted.
    [Display(Name = "Provincia")]
    public int? ProvinceId { get; set; }

    [Display(Name = "Cantón")]
    public int? CantonId { get; set; }

    [Display(Name = "Distrito")]
    public int? DistrictId { get; set; }

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
