using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 047 / US4 — the append-only evidence version chain: replace appends a new current version and
/// supersedes the prior (one-current invariant), a reason is required, and a reconciliation-critical
/// field edit also appends a version.
/// </summary>
[TestFixture]
public class EvidenceVersionTests
{
    private static Evidence Attach()
        => Evidence.Attach(
            applicationId: 1, type: EvidenceType.Invoice, disbursementId: null, amount: 400_000m, currency: "CRC",
            documentReferenceNumber: "F-001", documentDate: new DateOnly(2026, 7, 16), supplierId: null,
            originalFileName: "invoice.pdf", blobKey: "evidence/application/1/evidence/v1.pdf",
            fileSize: 1024, contentType: "application/pdf", fileHash: new string('a', 64), uploadedByUserId: "user-1");

    private static void Replace(Evidence e, decimal amount, string reason, string blobKey = "evidence/application/1/evidence/v2.pdf")
        => e.ReplaceCurrent(
            amount, "CRC", "F-001", new DateOnly(2026, 7, 16), "invoice-v2.pdf", blobKey,
            2048, "application/pdf", new string('b', 64), reason, "user-2");

    [Test]
    public void Replace_AppendsCurrent_SupersedesPrior()
    {
        var e = Attach();
        Replace(e, 400_000m, "archivo corregido");

        Assert.That(e.Versions, Has.Count.EqualTo(2));
        Assert.That(e.Versions.Count(v => v.IsCurrent), Is.EqualTo(1), "exactly one current");
        var current = e.Versions.Single(v => v.IsCurrent);
        Assert.That(current.VersionNumber, Is.EqualTo(2));
        Assert.That(current.Reason, Is.EqualTo("archivo corregido"));
        Assert.That(e.Versions.Single(v => v.VersionNumber == 1).IsCurrent, Is.False);
    }

    [Test]
    public void Replace_WithoutReason_Throws()
    {
        var e = Attach();
        Assert.That(() => Replace(e, 400_000m, "  "), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Replace_AmountEdit_AppendsVersion_AndUpdatesDenormalizedAmount()
    {
        var e = Attach();
        Replace(e, 350_000m, "ajuste de monto");

        Assert.That(e.Amount, Is.EqualTo(350_000m)); // denormalized current updated
        Assert.That(e.Versions.Single(v => v.IsCurrent).Amount, Is.EqualTo(350_000m));
        Assert.That(e.Versions, Has.Count.EqualTo(2));
    }
}
