using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.ValueObjects;
using Syncfusion.HtmlConverter;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;

namespace FundingPlatform.Infrastructure.DocumentGeneration;

public class SyncfusionFundingAgreementPdfRenderer : IFundingAgreementPdfRenderer
{
    public Task<byte[]> RenderAsync(string html, string? baseUrl)
    {
        var converter = new HtmlToPdfConverter(HtmlRenderingEngine.Blink);

        // Spec 018 / FR-003 — A4 portrait, 20mm top/bottom + 18mm left/right.
        // Conversion: 1mm ≈ 2.83465pt → 20mm ≈ 56.69pt, 18mm ≈ 51.02pt. The
        // CSS @page rule in _FundingAgreementLayout.cshtml controls layout;
        // these renderer-side values mirror it so Blink reserves the same
        // strip for the position: fixed header / footer (R-001).
        var blinkSettings = new BlinkConverterSettings
        {
            PdfPageSize = PdfPageSize.A4,
            Margin = new PdfMargins
            {
                Top = 56.69f,
                Bottom = 56.69f,
                Left = 51.02f,
                Right = 51.02f,
            },
            EnableJavaScript = false
        };

        converter.ConverterSettings = blinkSettings;

        using var document = converter.Convert(html, baseUrl ?? string.Empty);
        using var stream = new MemoryStream();
        document.Save(stream);
        document.Close(true);

        return Task.FromResult(stream.ToArray());
    }

    /// <summary>
    /// Spec 015 / US5 / T511 — pre-flights the per-line conversion metadata, then
    /// renders the document. The HTML render delegate runs only when the
    /// pre-flight succeeds, so a failed validation never spends Razor work.
    /// </summary>
    public async Task<byte[]> RenderFromModelAsync(
        IReadOnlyList<FundingAgreementItemRowDto> items,
        Func<Task<string>> renderHtmlAsync,
        string? baseUrl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(renderHtmlAsync);

        EnsureConversionMetadata(items);

        ct.ThrowIfCancellationRequested();

        var html = await renderHtmlAsync().ConfigureAwait(false);
        return await RenderAsync(html, baseUrl).ConfigureAwait(false);
    }

    /// <summary>
    /// Pre-flight check — every non-CRC line must carry an embedded rate
    /// snapshot. Exposed as <c>internal</c> for unit/integration assertions.
    /// </summary>
    internal static void EnsureConversionMetadata(IReadOnlyList<FundingAgreementItemRowDto> items)
    {
        var offending = new List<int>();
        var crc = CurrencyCode.Crc.Value;

        foreach (var row in items)
        {
            if (string.Equals(row.Currency, crc, StringComparison.Ordinal)) continue;
            if (row.SnapshotRateValue is null)
            {
                offending.Add(row.QuotationId ?? row.ItemId);
            }
        }

        if (offending.Count > 0)
        {
            throw new MissingConversionMetadataException(offending);
        }
    }
}
