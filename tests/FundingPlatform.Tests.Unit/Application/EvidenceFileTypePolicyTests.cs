using System.Text;
using FundingPlatform.Application.FundsUsageEvidence;

namespace FundingPlatform.Tests.Unit.Application;

[TestFixture]
public class EvidenceFileTypePolicyTests
{
    private static byte[] Head(params byte[] prefix)
    {
        var buf = new byte[EvidenceFileTypePolicy.HeadByteCount];
        Array.Copy(prefix, buf, prefix.Length);
        return buf;
    }

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0];

    // RIFF????WEBP
    private static byte[] Webp() => Head(0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50);
    // ????ftyp (ftyp box marker at offset 4)
    private static byte[] Heif() => Head(0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63);

    [Test]
    public void Accepts_Pdf()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("doc.pdf", "application/pdf", Head(Pdf)), Is.True);

    [Test]
    public void Accepts_Png()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("img.png", "image/png", Head(Png)), Is.True);

    [TestCase("photo.jpg")]
    [TestCase("photo.jpeg")]
    public void Accepts_Jpeg(string name)
        => Assert.That(EvidenceFileTypePolicy.IsAllowed(name, "image/jpeg", Head(Jpeg)), Is.True);

    [Test]
    public void Accepts_Webp()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("img.webp", "image/webp", Webp()), Is.True);

    [TestCase("img.heic", "image/heic")]
    [TestCase("img.heif", "image/heif")]
    public void Accepts_Heif(string name, string ct)
        => Assert.That(EvidenceFileTypePolicy.IsAllowed(name, ct, Heif()), Is.True);

    [Test]
    public void Accepts_Docx()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed(
            "f.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Head(Zip)), Is.True);

    [Test]
    public void Accepts_Xlsx()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed(
            "f.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Head(Zip)), Is.True);

    [Test]
    public void Accepts_LegacyDoc()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("f.doc", "application/msword", Head(Ole)), Is.True);

    [Test]
    public void Accepts_LegacyXls()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("f.xls", "application/vnd.ms-excel", Head(Ole)), Is.True);

    [Test]
    public void Accepts_MissingContentType_WhenMagicMatches()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("doc.pdf", null, Head(Pdf)), Is.True);

    [Test]
    public void Rejects_DisallowedExtension_Txt()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("notes.txt", "text/plain", Head(Encoding.ASCII.GetBytes("hello"))), Is.False);

    [Test]
    public void Rejects_DisallowedExtension_Zip()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("archive.zip", "application/zip", Head(Zip)), Is.False);

    [Test]
    public void Rejects_SpoofedMagic_PdfNameButPngBytes()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("fake.pdf", "application/pdf", Head(Png)), Is.False);

    [Test]
    public void Rejects_MismatchedContentType()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("doc.pdf", "image/png", Head(Pdf)), Is.False);

    [Test]
    public void Rejects_EmptyFileName()
        => Assert.That(EvidenceFileTypePolicy.IsAllowed("", "application/pdf", Head(Pdf)), Is.False);
}
