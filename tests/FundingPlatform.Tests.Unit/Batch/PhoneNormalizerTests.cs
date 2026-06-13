using FundingPlatform.Application.Admin.Users.Batch;

namespace FundingPlatform.Tests.Unit.Batch;

public class PhoneNormalizerTests
{
    [Test]
    public void Normalize_LocalEightDigits_StripsFormatting()
    {
        Assert.That(PhoneNormalizer.Normalize("8888-1111"), Is.EqualTo("88881111"));
    }

    [Test]
    public void Normalize_WithSpacedCountryCode_DropsLeading506()
    {
        Assert.That(PhoneNormalizer.Normalize("506 8888 1111"), Is.EqualTo("88881111"));
    }

    [Test]
    public void Normalize_WithPlusCountryCode_DropsLeading506()
    {
        Assert.That(PhoneNormalizer.Normalize("+506 88881111"), Is.EqualTo("88881111"));
    }

    [Test]
    public void Normalize_MultipleNumbers_TakesFirst()
    {
        Assert.That(PhoneNormalizer.Normalize("8888-1111 / 7777-2222"), Is.EqualTo("88881111"));
    }

    [Test]
    public void Normalize_ShortNumberStartingWith506_IsKept()
    {
        // Only strip 506 when the result is longer than 8 digits, so a genuine local
        // number that happens to start 506 is preserved.
        Assert.That(PhoneNormalizer.Normalize("5061111"), Is.EqualTo("5061111"));
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("abc")]
    public void Normalize_BlankOrNoDigits_ReturnsNull(string? raw)
    {
        Assert.That(PhoneNormalizer.Normalize(raw), Is.Null);
    }
}
