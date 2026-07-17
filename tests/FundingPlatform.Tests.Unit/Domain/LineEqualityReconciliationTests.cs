using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 047 / FR-024 — the pure per-line paid↔accepted equality leg for the closure gate. A mismatch
/// in EITHER direction (paid &lt; accepted or paid &gt; accepted) blocks, at the 0.01 tolerance.
/// </summary>
[TestFixture]
public class LineEqualityReconciliationTests
{
    private static IReadOnlyList<FundingPlatform.Domain.ValueObjects.LineOverpaymentDiscrepancy> Eval(decimal paid, decimal accepted)
        => DisbursementLineReconciliation.EvaluateLineEquality(
            new[] { new LineEqualityInput(1, "L-1", paid, accepted) });

    [Test]
    public void ExactMatch_NoDiscrepancy()
    {
        Assert.That(Eval(100_000m, 100_000m), Is.Empty);
    }

    [Test]
    public void AcceptanceShortfall_Blocks()
    {
        // ₡72 shortfall (paid 100,000 vs accepted 99,928).
        var result = Eval(100_000m, 99_928m);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Severity, Is.EqualTo(DiscrepancySeverity.Blocking));
        Assert.That(result[0].Overage, Is.EqualTo(72m));
    }

    [Test]
    public void AcceptanceExcess_AlsoBlocks()
    {
        var result = Eval(99_928m, 100_000m);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Overage, Is.EqualTo(-72m));
    }

    [Test]
    public void WithinTolerance_Passes()
    {
        Assert.That(Eval(100_000m, 100_000.009m), Is.Empty);
    }
}
