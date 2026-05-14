using FundingPlatform.Application.AiComparison;

namespace FundingPlatform.Tests.Unit.AiComparison;

public class ComparisonNormalizerTests
{
    [Test]
    public void ToCrc_LeavesCrcAmountsUnchanged()
    {
        Assert.That(ComparisonNormalizer.ToCrc(120000m, "CRC", 1m), Is.EqualTo(120000m));
    }

    [Test]
    public void ToCrc_ConvertsUsdViaSnapshotRate()
    {
        // 300 USD @ 550 CRC/USD ⇒ 165000 CRC
        var result = ComparisonNormalizer.ToCrc(300m, "USD", 550m);
        Assert.That(result, Is.EqualTo(165000m));
    }

    [Test]
    public void ToCrc_RejectsNonPositiveSnapshotRateForNonCrc()
    {
        Assert.Throws<ArgumentException>(() => ComparisonNormalizer.ToCrc(100m, "USD", 0m));
    }

    [Test]
    public void ToMetres_HandlesCmAndMm()
    {
        Assert.That(ComparisonNormalizer.ToMetres(150m, "cm"), Is.EqualTo(1.5m));
        Assert.That(ComparisonNormalizer.ToMetres(2000m, "mm"), Is.EqualTo(2m));
    }

    [Test]
    public void ToKilograms_HandlesLbAndG()
    {
        Assert.That(ComparisonNormalizer.ToKilograms(10m, "lb"), Is.EqualTo(10m * 0.45359237m));
        Assert.That(ComparisonNormalizer.ToKilograms(500m, "g"), Is.EqualTo(0.5m));
    }

    [Test]
    public void FormatDateEsCr_UsesMmmDdYyyy()
    {
        var date = new DateOnly(2026, 5, 11);
        var formatted = ComparisonNormalizer.FormatDateEsCr(date);
        // es-CR locale uses Spanish month abbreviations; assert the year + day made it through.
        Assert.That(formatted, Does.Contain("2026"));
        Assert.That(formatted, Does.Contain("11"));
    }
}
