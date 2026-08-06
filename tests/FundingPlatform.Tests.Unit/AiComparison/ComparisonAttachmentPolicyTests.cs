using FundingPlatform.Application.AiComparison;
using static FundingPlatform.Application.AiComparison.ComparisonAttachmentPolicy;

namespace FundingPlatform.Tests.Unit.AiComparison;

/// <summary>
/// Spec 020 — regression cover for the dev-environment failure where a supplier
/// quotation stored as <c>application/vnd.ms-excel</c> was sent to the provider
/// declared as <c>application/pdf</c>, producing
/// "messages.0.content.1.pdf.source.base64.data: The PDF specified was not valid"
/// and failing the whole comparison with an opaque hard error.
/// </summary>
[TestFixture]
public class ComparisonAttachmentPolicyTests
{
    [Test]
    public void Classify_DetectsPdf()
    {
        var pdf = "%PDF-1.7\n%âãÏÓ"u8.ToArray();

        var result = Classify(pdf);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(AttachmentKind.Pdf));
            Assert.That(result.MediaType, Is.EqualTo(PdfMediaType));
        });
    }

    [Test]
    public void Classify_DetectsPng()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

        var result = Classify(png);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(AttachmentKind.Image));
            Assert.That(result.MediaType, Is.EqualTo(PngMediaType));
        });
    }

    [Test]
    public void Classify_DetectsJpeg()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

        var result = Classify(jpeg);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(AttachmentKind.Image));
            Assert.That(result.MediaType, Is.EqualTo(JpegMediaType));
        });
    }

    /// <summary>The exact shape that broke dev: a legacy .xls (OLE2 compound file).</summary>
    [Test]
    public void Classify_RejectsLegacyExcel()
    {
        byte[] ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        var result = Classify(ole2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(AttachmentKind.Unsupported));
            Assert.That(result.MediaType, Is.Null);
        });
    }

    /// <summary>.xlsx / .docx are ZIP containers — equally unreadable as a PDF.</summary>
    [Test]
    public void Classify_RejectsOpenXmlZipContainer()
    {
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];

        Assert.That(Classify(zip).Kind, Is.EqualTo(AttachmentKind.Unsupported));
    }

    [Test]
    public void Classify_RejectsEmptyAndTruncatedContent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Classify(ReadOnlySpan<byte>.Empty).Kind, Is.EqualTo(AttachmentKind.Unsupported));
            // Shorter than the PDF signature — must not read past the buffer.
            Assert.That(Classify("%PD"u8).Kind, Is.EqualTo(AttachmentKind.Unsupported));
        });
    }

    /// <summary>Content wins over naming — a renamed spreadsheet is still refused.</summary>
    [Test]
    public void Classify_IgnoresFileNamingAndTrustsContent()
    {
        byte[] excelBytesNamedPdf = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        Assert.That(Classify(excelBytesNamedPdf).Kind, Is.EqualTo(AttachmentKind.Unsupported));
    }
}
