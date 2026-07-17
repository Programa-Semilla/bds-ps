using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 047 / US1 — the pure evidence-graph domain invariants: the positive-amount allocation
/// factory, the CRC + positive-amount attach guards, and the initial version (v1) shape. The
/// service-level allocation-integrity (Σ ≤ amount) + orphan guard are exercised in the integration
/// suite (they span the DB); this covers the entity contracts.
/// </summary>
[TestFixture]
public class EvidenceAllocationTests
{
    private static Evidence Attach(EvidenceType type = EvidenceType.Invoice, decimal amount = 400_000m, string currency = "CRC")
        => Evidence.Attach(
            applicationId: 1, type: type, disbursementId: null, amount: amount, currency: currency,
            documentReferenceNumber: "F-001", documentDate: new DateOnly(2026, 7, 16), supplierId: null,
            originalFileName: "invoice.pdf", blobKey: "evidence/application/1/evidence/abc.pdf",
            fileSize: 1024, contentType: "application/pdf", fileHash: new string('a', 64), uploadedByUserId: "user-1");

    [Test]
    public void For_PositiveAmount_Creates()
    {
        var alloc = EvidenceLineAllocation.For(evidenceId: 5, itemId: 9, amount: 100_000m);
        Assert.That(alloc.EvidenceId, Is.EqualTo(5));
        Assert.That(alloc.ItemId, Is.EqualTo(9));
        Assert.That(alloc.Amount, Is.EqualTo(100_000m));
    }

    [Test]
    public void For_ZeroOrNegative_Throws()
    {
        Assert.That(() => EvidenceLineAllocation.For(1, 1, 0m), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => EvidenceLineAllocation.For(1, 1, -1m), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Attach_NonCrc_Throws()
    {
        Assert.That(() => Attach(currency: "USD"), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Attach_NonPositiveAmount_Throws()
    {
        Assert.That(() => Attach(amount: 0m), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Attach_CreatesInitialCurrentVersion()
    {
        var e = Attach();

        Assert.That(e.Versions, Has.Count.EqualTo(1));
        var v1 = e.Versions[0];
        Assert.That(v1.VersionNumber, Is.EqualTo(1));
        Assert.That(v1.IsCurrent, Is.True);
        Assert.That(v1.Reason, Is.Null); // no reason on the initial version
        Assert.That(v1.FileHash, Is.EqualTo(new string('a', 64)));
        Assert.That(e.Currency, Is.EqualTo("CRC"));
        Assert.That(e.Type, Is.EqualTo(EvidenceType.Invoice));
    }

    [Test]
    public void Attach_PaymentIndependentAcceptance_Stored()
    {
        // AC — a signed acceptance with no disbursement anchor is valid at the domain level
        // (the orphan guard — requiring a line OR disbursement — is a service concern).
        var e = Attach(type: EvidenceType.SignedAcceptance);
        Assert.That(e.Type, Is.EqualTo(EvidenceType.SignedAcceptance));
        Assert.That(e.DisbursementId, Is.Null);
    }
}
