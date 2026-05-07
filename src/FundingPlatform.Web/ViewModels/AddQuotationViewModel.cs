using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace FundingPlatform.Web.ViewModels;

public class AddQuotationViewModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }
    public int SupplierId { get; set; }

    [Display(Name = "Proveedor")]
    public string SupplierName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El precio es obligatorio.")]
    [Display(Name = "Precio")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")]
    public decimal Price { get; set; }

    // Spec 015 / T113 — the form binds Currency from a <select> populated with the
    // enabled-currencies catalog (CRC + USD by default). The value is already
    // constrained to a 3-char ISO code by the select itself, so the legacy
    // [StringLength(3, MinimumLength = 3)] attribute is gone. Server-side, the
    // controller passes Currency through CurrencyCode.From(...) which throws on
    // anything that is not exactly three letters. The legacy [StringLength] also
    // tripped jQuery Unobtrusive Validation against <select> elements (which it
    // applies as a rangelength rule that reads the option's text content rather
    // than its value attribute) and blocked every POST in spec 015 E2E tests.
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
    /// Spec 015 / T113 — list of enabled currencies (CRC + USD by default) used to
    /// populate the currency &lt;select&gt; on the Add form. Bound by the controller
    /// from <c>dbo.Currencies WHERE IsEnabled = 1</c> ordered by <c>DisplayOrder</c>.
    /// </summary>
    public IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; } = [];
}

public sealed record CurrencyOption(string Code, string DisplayName, string Symbol);
