using System.Globalization;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.ViewComponents;

/// <summary>
/// Spec 015 / T411 — small ⓘ Tabler-icon indicator carrying a Bootstrap tooltip
/// describing the rate snapshot that produced a non-CRC → CRC conversion.
///
/// The tooltip text is es-CR localized:
/// "Tipo de cambio aplicado: 1 USD = ₡520.00 (Compra, vigente 2026-05-07)".
///
/// Wired with <c>data-bs-toggle="tooltip"</c>; the existing site-wide Bootstrap
/// bundle (Tabler) initialises tooltips via the global <c>data-bs-toggle</c>
/// selector, so no per-page JS is required.
/// </summary>
public class ConversionIndicatorViewComponent : ViewComponent
{
    public sealed record ConversionIndicatorViewModel(string TooltipText);

    public IViewComponentResult Invoke(ExchangeRateSnapshot snapshot, CurrencyCode? originalCurrency = null)
    {
        var crcCulture = CultureInfo.GetCultureInfo("es-CR");
        var sourceCode = originalCurrency?.Value ?? "USD";

        var rateTypeLabel = snapshot.RateType switch
        {
            RateType.Buy  => "Compra",
            RateType.Sell => "Venta",
            _             => snapshot.RateType.ToString(),
        };

        // FR-024 — tooltip surfaces rate value, type, and effective date so reviewers
        // can audit the conversion without leaving the page. Date is rendered in the
        // local presentation calendar (UTC → local) at day-resolution; the snapshot
        // itself stores UTC.
        var effectiveLocal = snapshot.EffectiveAtUtc.ToLocalTime();
        var tooltip = string.Format(
            crcCulture,
            "Tipo de cambio aplicado: 1 {0} = ₡{1:0.######} ({2}, vigente {3:yyyy-MM-dd})",
            sourceCode,
            snapshot.RateValue,
            rateTypeLabel,
            effectiveLocal);

        return View(new ConversionIndicatorViewModel(tooltip));
    }
}
