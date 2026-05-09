using System.Globalization;
using System.Text;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Infrastructure.DocumentGeneration;
using Syncfusion.Licensing;
using Syncfusion.Pdf.Parsing;

namespace FundingPlatform.Tests.Integration.PdfGeneration;

/// <summary>
/// Spec 015 / US5 / T500 — PDF rendering golden tests for the funding-agreement
/// document. Exercises <see cref="SyncfusionFundingAgreementPdfRenderer"/>
/// directly, including the Spec-015 conversion-metadata pre-flight.
///
/// The test feeds the renderer a hand-built HTML stub that mirrors the markup
/// our Razor partial produces — a thin enough harness that we can run the
/// checks without spinning up a Razor view engine inside the integration test
/// project (the real partial gets E2E coverage in
/// <c>AgreementPdfMultiCurrencyE2E</c>). The PDF artefact and the extracted
/// text baseline live under <c>tests/Fixtures/pdfs/</c> per spec 015 / T513.
/// </summary>
[TestFixture]
[Category("PdfRendering")]
public class PdfRenderingTests
{
    private static readonly CultureInfo EsCr = BuildEsCrCulture();

    [OneTimeSetUp]
    public void RegisterSyncfusionLicense()
    {
        // Mirrors AppHost.cs's dev-fallback license. Real environments override
        // via configuration; here we just need to silence Syncfusion's
        // evaluation watermark behaviour in test runs.
        const string DevLicense = "Ngo9BigBOggjHTQxAR8/V1JHaF1cXmhMYVJpR2NbeU5xdF9DZVZURGY/P1ZhSXxVdkFhXX1cdXFQRmJVU019XEE=";
        SyncfusionLicenseProvider.RegisterLicense(DevLicense);
    }

    /// <summary>
    /// Returns the absolute path of <c>tests/Fixtures/pdfs/</c>. The Integration
    /// test binary lives at <c>tests/FundingPlatform.Tests.Integration/bin/...</c>
    /// — five levels up gets us back to the repo root, so we can resolve
    /// <c>tests/Fixtures/pdfs/</c> deterministically across machines.
    /// </summary>
    private static string FixturesDir()
    {
        var asmDir = Path.GetDirectoryName(typeof(PdfRenderingTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        // bin/Debug/net10.0 → bin/Debug → bin → FundingPlatform.Tests.Integration → tests → repo root
        for (var i = 0; i < 5 && dir.Parent is not null; i++)
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir.FullName, "tests", "Fixtures", "pdfs");
    }

    private static CultureInfo BuildEsCrCulture()
    {
        var ci = (CultureInfo)CultureInfo.GetCultureInfo("es-CR").Clone();
        ci.NumberFormat.NumberDecimalSeparator = ".";
        ci.NumberFormat.NumberGroupSeparator = ",";
        ci.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        return CultureInfo.ReadOnly(ci);
    }

    /// <summary>
    /// Spec 015 / T513 — one-shot helper: regenerates the committed
    /// <c>tests/Fixtures/pdfs/crc-only-baseline.pdf</c> artefact. Marked
    /// [Explicit] so it never runs in CI and only emits when a contributor
    /// invokes it manually (e.g. when the partial markup shifts and the
    /// committed baseline becomes stale).
    /// </summary>
    [Test]
    [Explicit("Run manually to refresh tests/Fixtures/pdfs/crc-only-baseline.pdf.")]
    public async Task RegenerateCrcOnlyBaselinePdf()
    {
        var items = new List<FundingAgreementItemRowDto>
        {
            new(
                ItemId: 1,
                ProductName: "Servidor",
                CategoryName: "Equipo",
                SupplierName: "Proveedor Alfa",
                UnitPrice: 750_000m,
                LineTotal: 750_000m,
                Currency: "CRC",
                QuotationId: 100,
                ConvertedCrcAmount: 750_000m),
        };

        var html = BuildItemsHtml(items);
        var renderer = new SyncfusionFundingAgreementPdfRenderer();
        var pdfBytes = await renderer.RenderFromModelAsync(
            items, renderHtmlAsync: () => Task.FromResult(html));

        var dir = FixturesDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "crc-only-baseline.pdf");
        await File.WriteAllBytesAsync(path, pdfBytes);

        await TestContext.Out.WriteLineAsync($"Wrote baseline: {path} ({pdfBytes.Length} bytes)");
    }

    [Test]
    public async Task CrcOnly_Rendering_ProducesValidPdf_WithNoConversionNote()
    {
        var items = new List<FundingAgreementItemRowDto>
        {
            new(
                ItemId: 1,
                ProductName: "Servidor",
                CategoryName: "Equipo",
                SupplierName: "Proveedor Alfa",
                UnitPrice: 750_000m,
                LineTotal: 750_000m,
                Currency: "CRC",
                QuotationId: 100,
                ConvertedCrcAmount: 750_000m),
        };

        var html = BuildItemsHtml(items);
        var renderer = new SyncfusionFundingAgreementPdfRenderer();
        var pdfBytes = await renderer.RenderFromModelAsync(
            items,
            renderHtmlAsync: () => Task.FromResult(html));

        Assert.That(LooksLikePdf(pdfBytes), Is.True, "PDF magic bytes %PDF- expected.");

        var text = ExtractText(pdfBytes);
        Assert.That(text, Does.Contain("Servidor"), "Item name must be in the PDF text.");
        Assert.That(text, Does.Contain("CRC"), "CRC currency code must be on the line.");
        Assert.That(text, Does.Not.Contain("Conversión:"),
            "CRC-only requests must not render a conversion note (FR-027 inverse).");
        Assert.That(text, Does.Not.Contain("Tipo Compra"),
            "CRC-only requests must not mention rate type.");

        // T513 baseline cross-check — when the committed baseline exists, its
        // text extract should also lack the conversion note. The baseline
        // artefact itself is committed for visual review; the assertion here
        // is the structural one.
        var baselineDir = FixturesDir();
        var baselinePdf = Path.Combine(baselineDir, "crc-only-baseline.pdf");
        if (File.Exists(baselinePdf))
        {
            var baselineText = ExtractText(await File.ReadAllBytesAsync(baselinePdf));
            Assert.That(baselineText, Does.Not.Contain("Conversión:"),
                "Committed CRC-only baseline must not contain a conversion note row.");
        }
    }

    [Test]
    public async Task Mixed_Rendering_IncludesConversionNote_WithRateBuyAndDate()
    {
        var effective = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var items = new List<FundingAgreementItemRowDto>
        {
            new(
                ItemId: 1,
                ProductName: "Insumos locales",
                CategoryName: "Suministros",
                SupplierName: "Proveedor CRC",
                UnitPrice: 300_000m,
                LineTotal: 300_000m,
                Currency: "CRC",
                QuotationId: 200,
                ConvertedCrcAmount: 300_000m),
            new(
                ItemId: 2,
                ProductName: "Servidor importado",
                CategoryName: "Equipo",
                SupplierName: "Proveedor USD",
                UnitPrice: 1000m,
                LineTotal: 1000m,
                Currency: "USD",
                QuotationId: 201,
                ConvertedCrcAmount: 520_000m,
                SnapshotRateValue: 520.123456m,
                SnapshotRateType: "Buy",
                SnapshotEffectiveAtUtc: effective),
        };

        var html = BuildItemsHtml(items);
        var renderer = new SyncfusionFundingAgreementPdfRenderer();
        var pdfBytes = await renderer.RenderFromModelAsync(
            items,
            renderHtmlAsync: () => Task.FromResult(html));

        Assert.That(LooksLikePdf(pdfBytes), Is.True);

        var text = ExtractText(pdfBytes);
        Assert.That(text, Does.Contain("Conversión:"),
            "Mixed-currency requests must render the conversion note (FR-027).");
        Assert.That(text, Does.Contain("Tipo Compra"),
            "Conversion note must include the localised RateType (Compra).");
        // Spec 018 — narrower Blink viewport may wrap "vigente desde" onto
        // a separate line from the date; assert the two tokens independently
        // rather than as a fixed-spaced substring.
        Assert.That(text, Does.Contain("vigente desde"),
            "Conversion note must include the 'vigente desde' phrase.");
        Assert.That(text, Does.Contain("2026-05-01"),
            "Conversion note must include the effective-since date.");
        Assert.That(text, Does.Contain("520"),
            "Conversion note must include the rate value.");
        Assert.That(text, Does.Contain("USD"),
            "Original USD currency must still appear on the line.");
        Assert.That(text, Does.Contain("CRC"),
            "Converted CRC currency must appear on the conversion-note row.");

        // T513 — baseline expected.txt cross-check. The committed baseline is
        // a normalised text extract of the mixed-currency PDF that downstream
        // contributors can diff against if rendering shifts.
        var baselineExpected = Path.Combine(FixturesDir(), "mixed-baseline.expected.txt");
        if (File.Exists(baselineExpected))
        {
            var expected = await File.ReadAllTextAsync(baselineExpected);
            foreach (var line in expected.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                Assert.That(text, Does.Contain(trimmed),
                    $"mixed-baseline.expected.txt line not found in extracted PDF text: '{trimmed}'");
            }
        }
    }

    [Test]
    public void MissingSnapshot_NonCrcRow_ThrowsMissingConversionMetadataException()
    {
        var items = new List<FundingAgreementItemRowDto>
        {
            new(
                ItemId: 1,
                ProductName: "Legacy USD",
                CategoryName: "Equipo",
                SupplierName: "Legacy Supplier",
                UnitPrice: 500m,
                LineTotal: 500m,
                Currency: "USD",
                QuotationId: 999,
                ConvertedCrcAmount: null,
                SnapshotRateValue: null),
        };

        var renderer = new SyncfusionFundingAgreementPdfRenderer();

        var ex = Assert.ThrowsAsync<MissingConversionMetadataException>(async () =>
            await renderer.RenderFromModelAsync(
                items,
                renderHtmlAsync: () => Task.FromResult("<html><body>should not render</body></html>")));

        Assert.That(ex!.OffendingQuotationIds, Is.EquivalentTo(new[] { 999 }),
            "OffendingQuotationIds must surface the legacy quotation id for the controller's structured log.");
    }

    [Test]
    public async Task MissingSnapshot_HtmlCallbackIsNotInvoked_WhenPreflightFails()
    {
        var items = new List<FundingAgreementItemRowDto>
        {
            new(
                ItemId: 1,
                ProductName: "Legacy USD",
                CategoryName: "X",
                SupplierName: "X",
                UnitPrice: 100m,
                LineTotal: 100m,
                Currency: "USD",
                QuotationId: 5),
        };

        var callbackInvoked = false;
        var renderer = new SyncfusionFundingAgreementPdfRenderer();

        try
        {
            await renderer.RenderFromModelAsync(
                items,
                renderHtmlAsync: () =>
                {
                    callbackInvoked = true;
                    return Task.FromResult("<html/>");
                });
        }
        catch (MissingConversionMetadataException)
        {
            // expected
        }

        Assert.That(callbackInvoked, Is.False,
            "HTML render callback must not be invoked when the conversion-metadata pre-flight fails (saves Razor work).");
    }

    /// <summary>
    /// Builds a minimal HTML body that mirrors the markup
    /// <c>_FundingAgreementItemsTable.cshtml</c> emits for each row plus the
    /// per-line conversion note for non-CRC rows. Kept in sync with the partial
    /// by convention; structural shifts in the partial that change visible text
    /// will surface here as failed assertions.
    /// </summary>
    private static string BuildItemsHtml(IReadOnlyList<FundingAgreementItemRowDto> items)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'/></head><body>");
        sb.Append("<h2>Ítems financiados</h2>");
        sb.Append("<table><thead><tr>");
        sb.Append("<th>Producto</th><th>Categoría</th><th>Proveedor</th>");
        sb.Append("<th>Precio unitario</th><th>Total de la línea</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in items)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{item.ProductName}</td>");
            sb.Append($"<td>{item.CategoryName}</td>");
            sb.Append($"<td>{item.SupplierName}</td>");
            sb.Append($"<td>{Money(item.Currency, item.UnitPrice)}</td>");
            sb.Append($"<td>{Money(item.Currency, item.LineTotal)}</td>");
            sb.Append("</tr>");

            if (item.Currency != "CRC" && item.SnapshotRateValue.HasValue)
            {
                var rate = item.SnapshotRateValue.Value.ToString("N6", EsCr);
                var rateType = item.SnapshotRateType switch
                {
                    "Buy" => "Compra",
                    "Sell" => "Venta",
                    _ => item.SnapshotRateType ?? string.Empty,
                };
                var effective = (item.SnapshotEffectiveAtUtc ?? DateTime.MinValue)
                    .ToString("yyyy-MM-dd", EsCr);
                var note = $"Conversión: 1 {item.Currency} = ₡{rate} (Tipo {rateType}, vigente desde {effective})";

                sb.Append("<tr>");
                sb.Append($"<td colspan='3'>{note}</td>");
                if (item.ConvertedCrcAmount.HasValue)
                {
                    sb.Append($"<td colspan='2'>{Money("CRC", item.ConvertedCrcAmount.Value)}</td>");
                }
                else
                {
                    sb.Append("<td colspan='2'></td>");
                }
                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    private static string Money(string currency, decimal value) =>
        $"{currency} {value.ToString("N2", EsCr)}";

    private static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';

    private static string ExtractText(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var doc = new PdfLoadedDocument(stream);
        var sb = new StringBuilder();
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            sb.Append(doc.Pages[i].ExtractText());
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
