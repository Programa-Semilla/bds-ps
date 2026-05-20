using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 023 — view-model for the per-quotation Edit form. Implements
/// <see cref="IQuoteFieldsModel"/> so the shared <c>_QuoteFields.cshtml</c>
/// partial renders Price / Currency / ValidUntil over this model exactly as
/// it does for <see cref="AddSupplierViewModel"/>.
/// </summary>
public class EditQuotationViewModel : IQuoteFieldsModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }
    public int QuotationId { get; set; }

    [Required(ErrorMessage = "El precio es obligatorio.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")]
    [Display(Name = "Precio")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "La moneda es obligatoria.")]
    [Display(Name = "Moneda")]
    public string Currency { get; set; } = string.Empty;

    [Required(ErrorMessage = "La fecha de vigencia es obligatoria.")]
    [Display(Name = "Vigente hasta")]
    public DateOnly ValidUntil { get; set; }

    [Required(ErrorMessage = "La sucursal del proveedor es obligatoria.")]
    [Display(Name = "Sucursal del proveedor")]
    public int SupplierBranchId { get; set; }

    /// <summary>
    /// Spec 015 — enabled-currencies list (CRC + USD by default). Populated
    /// from <c>dbo.Currencies WHERE IsEnabled = 1</c>.
    /// </summary>
    public IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; } = [];

    /// <summary>
    /// Branches of the quotation's current Supplier. Switching to a different
    /// Supplier is not permitted via Edit (FR-004).
    /// </summary>
    public IReadOnlyList<SelectListItem> BranchOptions { get; set; } = [];

    /// <summary>Display copy for the form banner: "Editando cotización de {SupplierName}".</summary>
    public string SupplierName { get; set; } = string.Empty;
}
