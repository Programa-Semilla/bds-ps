using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.ValueObjects;
using Syncfusion.Drawing;
using Syncfusion.HtmlConverter;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;

namespace FundingPlatform.Infrastructure.DocumentGeneration;

public class SyncfusionFundingAgreementPdfRenderer : IFundingAgreementPdfRenderer
{
    // Spec 018 / FR-003 — A4 portrait, 18mm L/R, top/bottom adjusted to reserve
    // room for renderer-drawn header (seedling) and footer (partner-strip).
    // Conversion: 1mm ≈ 2.83465pt.
    //   * Top margin = 90pt (~32mm) — fits the ~80pt seedling band plus a
    //     ~10pt breath before body content.
    //   * Bottom margin = 70pt (~25mm) — fits the ~56pt partner-strip band
    //     plus ~14pt breath after body.
    //   * L/R margin = 51.02pt (~18mm).
    private const float MarginTop = 100f;
    private const float MarginBottom = 75f;
    private const float MarginLR = 51.02f;
    private const float HeaderImageHeight = 80f;
    private const float HeaderImageHeightCover = 130f;
    private const float FooterImageHeight = 50f;
    private const float HeaderTopPadding = 10f;
    private const float FooterBottomPadding = 14f;

    public Task<byte[]> RenderAsync(string html, string? baseUrl)
    {
        return RenderInternalAsync(html, baseUrl, headerImagePath: null, footerImagePath: null);
    }

    /// <summary>
    /// Spec 015 / US5 / T511 — pre-flights the per-line conversion metadata, then
    /// renders the document. The HTML render delegate runs only when the
    /// pre-flight succeeds, so a failed validation never spends Razor work.
    /// Spec 018 — header / footer are drawn at the renderer level (R-001-revised)
    /// because CSS <c>position: fixed</c> does not repeat across pages in
    /// Blink HTML→PDF.
    /// </summary>
    public async Task<byte[]> RenderFromModelAsync(
        IReadOnlyList<FundingAgreementItemRowDto> items,
        Func<Task<string>> renderHtmlAsync,
        string? baseUrl = null,
        string? headerImageAbsolutePath = null,
        string? footerImageAbsolutePath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(renderHtmlAsync);

        EnsureConversionMetadata(items);

        ct.ThrowIfCancellationRequested();

        var html = await renderHtmlAsync().ConfigureAwait(false);
        return await RenderInternalAsync(html, baseUrl, headerImageAbsolutePath, footerImageAbsolutePath)
            .ConfigureAwait(false);
    }

    private static Task<byte[]> RenderInternalAsync(
        string html,
        string? baseUrl,
        string? headerImagePath,
        string? footerImagePath)
    {
        var converter = new HtmlToPdfConverter(HtmlRenderingEngine.Blink);

        var blinkSettings = new BlinkConverterSettings
        {
            PdfPageSize = PdfPageSize.A4,
            Margin = new PdfMargins
            {
                Top = MarginTop,
                Bottom = MarginBottom,
                Left = MarginLR,
                Right = MarginLR,
            },
            // Spec 018 / R-003 — Print media type enables Blink's table-header
            // repeat across page breaks (the <thead> band reappears on the
            // continuation page so the reader never sees a headerless table
            // fragment). Also unblocks @media print rules in the layout.
            MediaType = MediaType.Print,
            // Spec 018 — pin Blink's layout viewport to A4-at-96dpi pixels
            // (794x1123). Default viewport width is ~1200px; HTML laid out
            // at 1200px is then scaled to the 595pt PDF page width, which
            // visually halves CSS font sizes (36pt CSS → ~18pt on page).
            // Matching viewport pixels to PDF dpi-pt makes 1 CSS pt ≈ 1 PDF pt.
            ViewPortSize = new Syncfusion.Drawing.Size(794, 1123),
            EnableJavaScript = false
        };

        converter.ConverterSettings = blinkSettings;

        using var document = converter.Convert(html, baseUrl ?? string.Empty);

        // Spec 018 / FR-001 + FR-002 — repeating brand chrome by drawing the
        // seedling header and partner-strip footer directly on each page's
        // Graphics, in absolute page coordinates, INSIDE the margin reserve
        // computed via Margin.Top (90pt) / Margin.Bottom (70pt). Earlier
        // attempts via the CSS `position: fixed` approach failed (Blink does
        // not repeat fixed elements across pages), the
        // BlinkConverterSettings.PdfHeader path placed the band at body-top
        // rather than page-top, and PdfDocument.Template.Top likewise
        // overlapped body content. Direct page-graphics draw is unambiguous
        // and fully under our control.
        if (headerImagePath is not null)
        {
            DrawImageOnEveryPage(
                document,
                headerImagePath,
                placement: ImagePlacement.HeaderCentered,
                imageHeight: HeaderImageHeight);
        }
        if (footerImagePath is not null)
        {
            DrawImageOnEveryPage(
                document,
                footerImagePath,
                placement: ImagePlacement.FooterStretched,
                imageHeight: FooterImageHeight);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        document.Close(true);

        return Task.FromResult(stream.ToArray());
    }

    private enum ImagePlacement
    {
        /// <summary>
        /// Header band: square seedling logo, centered horizontally inside
        /// the top-margin reserve. Drawn 12pt below the page top edge so the
        /// logo reads as a balanced "above-the-fold" mark on every page.
        /// </summary>
        HeaderCentered,

        /// <summary>
        /// Footer band: wide partner-logo strip, stretched to the inside
        /// content width, sitting in the bottom-margin reserve.
        /// </summary>
        FooterStretched,
    }

    private static void DrawImageOnEveryPage(
        PdfDocument document,
        string imageAbsolutePath,
        ImagePlacement placement,
        float imageHeight)
    {
        var bytes = File.ReadAllBytes(imageAbsolutePath);
        using var imageStream = new MemoryStream(bytes);
        var bitmap = new PdfBitmap(imageStream);

        for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            var page = document.Pages[pageIndex];
            var pageSize = page.Size;

            SizeF imageSize;
            PointF imageOrigin;

            switch (placement)
            {
                case ImagePlacement.HeaderCentered:
                {
                    // Header logo. Aspect-preserving height. Cover page (index
                    // 0) gets a larger seedling matching the seed's prominent
                    // brand mark; continuation pages use the smaller 80pt
                    // version. The cover variant slightly extends into the
                    // body area, but `.cover-page { padding-top: 60pt }` in
                    // the layout keeps the title clear.
                    var targetHeight = pageIndex == 0
                        ? HeaderImageHeightCover
                        : imageHeight;
                    var aspect = (float)bitmap.Width / bitmap.Height;
                    var targetWidth = targetHeight * aspect;
                    imageSize = new SizeF(targetWidth, targetHeight);
                    imageOrigin = new PointF(
                        (pageSize.Width - targetWidth) / 2f,
                        HeaderTopPadding);
                    break;
                }

                case ImagePlacement.FooterStretched:
                {
                    // Footer strip stretched to inner content width.
                    var contentWidth = pageSize.Width - (2f * MarginLR);
                    var aspect = (float)bitmap.Height / bitmap.Width;
                    var targetWidth = contentWidth;
                    var targetHeight = Math.Min(imageHeight, targetWidth * aspect);
                    imageSize = new SizeF(targetWidth, targetHeight);
                    imageOrigin = new PointF(
                        MarginLR,
                        // Anchor the strip inside the bottom-margin reserve.
                        // Body content ends at y=pageHeight-MarginBottom; the
                        // strip lives in the reserve below that boundary.
                        pageSize.Height - FooterBottomPadding - targetHeight);
                    break;
                }

                default:
                    throw new InvalidOperationException("Unknown placement.");
            }

            page.Graphics.DrawImage(bitmap, imageOrigin, imageSize);
        }
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
