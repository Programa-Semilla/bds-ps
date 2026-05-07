using System.Globalization;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.ViewComponents;

/// <summary>
/// Spec 015 / T410 — renders a monetary value with explicit currency context.
///
/// Behaviour:
///   - <c>originalCurrency == null</c> or equals CRC: renders the CRC string only
///     (e.g. "₡750,000.00") with no conversion indicator. Used for application
///     totals (always summed in CRC) and for native CRC quotation rows.
///   - <c>originalCurrency</c> is non-CRC: renders the original (e.g. "$1,000.00 USD")
///     followed by the converted CRC value in parentheses, plus the conversion
///     indicator tooltip when a snapshot is supplied.
///
/// Locale-aware: CRC uses the es-CR culture (₡ symbol, U+00A0 thousand separator),
/// USD uses en-US so the rendered "$" matches the western convention applicants
/// expect in supplier quotation documents.
/// </summary>
public class MoneyDisplayViewComponent : ViewComponent
{
    public sealed record MoneyDisplayViewModel(
        decimal? Original,
        CurrencyCode? OriginalCurrency,
        decimal? ConvertedCrc,
        ExchangeRateSnapshot? Snapshot,
        string OriginalText,
        string? ConvertedCrcText,
        bool ShowConversion);

    public IViewComponentResult Invoke(
        decimal? original,
        CurrencyCode? originalCurrency,
        decimal? convertedCrc,
        ExchangeRateSnapshot? snapshot)
    {
        var crcCulture = CultureInfo.GetCultureInfo("es-CR");
        var usdCulture = CultureInfo.GetCultureInfo("en-US");

        // Treat null/empty originalCurrency as CRC (used for the request-total surface
        // — always native CRC, no conversion indicator). FR-013 / FR-024.
        var isCrc = originalCurrency is null || originalCurrency.IsBase;

        if (isCrc)
        {
            var amount = original ?? convertedCrc ?? 0m;
            var crcText = "₡" + amount.ToString("N2", crcCulture) + " CRC";
            var vm = new MoneyDisplayViewModel(
                Original: amount,
                OriginalCurrency: CurrencyCode.Crc,
                ConvertedCrc: amount,
                Snapshot: null,
                OriginalText: crcText,
                ConvertedCrcText: null,
                ShowConversion: false);
            return View(vm);
        }

        // Non-CRC: render the original + converted CRC. Both must be present at the
        // call site for a non-CRC quotation; missing converted-crc is a domain bug
        // surfaced as a literal "(–)" so the page does not crash.
        var originalAmount = original ?? 0m;
        var originalText = originalCurrency!.Value switch
        {
            "USD" => "$" + originalAmount.ToString("N2", usdCulture) + " USD",
            _     => originalAmount.ToString("N2", usdCulture) + " " + originalCurrency.Value,
        };

        var convertedText = convertedCrc.HasValue
            ? "₡" + convertedCrc.Value.ToString("N2", crcCulture) + " CRC"
            : null;

        var model = new MoneyDisplayViewModel(
            Original: originalAmount,
            OriginalCurrency: originalCurrency,
            ConvertedCrc: convertedCrc,
            Snapshot: snapshot,
            OriginalText: originalText,
            ConvertedCrcText: convertedText,
            ShowConversion: snapshot is not null);

        return View(model);
    }
}
