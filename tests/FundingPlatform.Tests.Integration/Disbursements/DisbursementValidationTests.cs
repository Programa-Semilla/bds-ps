using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using static FundingPlatform.Tests.Integration.Disbursements.DisbursementTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 045 — the money-integrity guards that must never regress silently:
/// FR-003 (zero/negative rejected), FR-004 (non-CRC rejected), and FR-005's
/// authoritative validation-time over-disbursement re-check (the race-proof gate that
/// refuses the second disbursement to validate even when it was individually clean at record).
/// </summary>
[TestFixture]
public class DisbursementValidationTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Test]
    public async Task Record_RejectsZeroAndNegativeAmount()
    {
        var db = $"disb-neg-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        var svc = NewService(ctx, new InMemoryObjectStorage());

        var zero = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 0m, "TX-0", null), Actor, CancellationToken.None);
        var negative = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, -1m, "TX-N", null), Actor, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(zero.Succeeded, Is.False);
            Assert.That(zero.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.AmountInvalid));
            Assert.That(negative.Succeeded, Is.False);
            Assert.That(negative.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.AmountInvalid));
        });
    }

    [Test]
    public async Task AttachEvidence_RejectsNonPositiveAndNonCrc()
    {
        var db = $"disb-ev-guard-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, new InMemoryObjectStorage());
        var rec = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 500_000m, "TX-1", null), Actor, CancellationToken.None);

        var zeroAmount = await svc.AttachEvidenceAsync(
            new AttachDisbursementEvidenceCommand(appId, rec.Value, EvidenceKind.BankReceipt, 0m, "CRC", "R", Today, Pdf(), "f.pdf", "application/pdf", 11),
            Actor, CancellationToken.None);
        var usd = await svc.AttachEvidenceAsync(
            new AttachDisbursementEvidenceCommand(appId, rec.Value, EvidenceKind.Invoice, 500_000m, "USD", "R", Today, Pdf(), "f.pdf", "application/pdf", 11),
            Actor, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(zeroAmount.Succeeded, Is.False);
            Assert.That(zeroAmount.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.AmountInvalid));
            Assert.That(usd.Succeeded, Is.False);
            Assert.That(usd.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.NonCrc));
        });
    }

    [Test]
    public async Task Validate_RefusesWhenCommittedTotalWouldBreachAllocation_EvenIfIndividuallyClean()
    {
        // FR-005 race-proof gate. A is recorded + proven while it alone fits the ceiling, so its
        // stored state stays clean. B is then recorded, pushing the committed Σ over the allocation.
        // A was never re-reconciled, so only the validation-time re-check can catch the breach.
        var db = $"disb-validate-over-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, new InMemoryObjectStorage());

        var a = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 600_000m, "TX-A", null), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, a.Value, EvidenceKind.BankReceipt, 600_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, a.Value, EvidenceKind.Invoice, 600_000m), Actor, CancellationToken.None);

        // B pushes the committed total to ₡1,200,000 > ₡1,000,000.
        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 600_000m, "TX-B", null), Actor, CancellationToken.None);

        var validateA = await svc.ValidateAsync(appId, a.Value, Actor, CancellationToken.None);

        Assert.That(validateA.Succeeded, Is.False);
        Assert.That(validateA.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.OverAllocation));
    }

    [Test]
    public async Task Validate_RefusesWithSpecificReason_WhenAnEvidenceIsMissing()
    {
        var db = $"disb-validate-missing-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, new InMemoryObjectStorage());
        var rec = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 300_000m, "TX-1", null), Actor, CancellationToken.None);

        // Only the invoice present → refusal names the missing bank receipt.
        await svc.AttachEvidenceAsync(Ev(appId, rec.Value, EvidenceKind.Invoice, 300_000m), Actor, CancellationToken.None);
        var missingReceipt = await svc.ValidateAsync(appId, rec.Value, Actor, CancellationToken.None);

        Assert.That(missingReceipt.Succeeded, Is.False);
        Assert.That(missingReceipt.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.MissingEvidence));
        Assert.That(missingReceipt.Errors[0].Message, Does.Contain("comprobante bancario"));
    }

    private static AttachDisbursementEvidenceCommand Ev(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today, Pdf(), $"{kind}.pdf", "application/pdf", 11);
}
