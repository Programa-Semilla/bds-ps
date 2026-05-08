using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 015 / US3 — single row of the admin rate-history list.
/// Maps to the contract shape in <c>contracts/exchange-rate-api.md</c>.
/// </summary>
public class AdminExchangeRateRowViewModel
{
    public Guid Id { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public string TargetCurrencyCode { get; set; } = string.Empty;
    public decimal BuyRate { get; set; }
    public decimal SellRate { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? CreatedByUserName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public bool IsActive { get; set; }
}

public class AdminExchangeRatesListViewModel
{
    public List<AdminExchangeRateRowViewModel> Rows { get; set; } = new();
}

public class AdminExchangeRateCreateViewModel
{
    [Required(ErrorMessage = "Seleccione la moneda de origen.")]
    public string SourceCurrencyCode { get; set; } = "USD";

    [Required(ErrorMessage = "Seleccione la moneda de destino.")]
    public string TargetCurrencyCode { get; set; } = "CRC";

    [Required(ErrorMessage = "Ingrese el tipo de cambio de compra.")]
    [Range(typeof(decimal), "0.000001", "999999999", ErrorMessage = "El tipo de cambio de compra debe ser mayor a cero.")]
    public decimal BuyRate { get; set; }

    [Required(ErrorMessage = "Ingrese el tipo de cambio de venta.")]
    [Range(typeof(decimal), "0.000001", "999999999", ErrorMessage = "El tipo de cambio de venta debe ser mayor a cero.")]
    public decimal SellRate { get; set; }

    [Required(ErrorMessage = "Ingrese la fecha de vigencia.")]
    public DateTime EffectiveAtLocal { get; set; } = DateTime.Now;

    public List<AdminCurrencyRowViewModel> AvailableCurrencies { get; set; } = new();
}
