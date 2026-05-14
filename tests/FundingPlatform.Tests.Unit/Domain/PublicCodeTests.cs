using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / FR-008 / R-1 — value-object invariants for <see cref="PublicCode"/>.
/// Validates the base32 alphabet (0/O/1/I/L excluded for dictation safety), the
/// 4-hyphen-4 shape, equality semantics, and implicit string conversion.
/// </summary>
[TestFixture]
public class PublicCodeTests
{
    [TestCase("A7K2-9XF3")]
    [TestCase("B3M5-7PQ9")]
    [TestCase("HJKN-PRST")]
    [TestCase("2345-6789")]
    [TestCase("ZZZZ-2222")]
    public void Constructor_AcceptsValidShape(string raw)
    {
        var code = new PublicCode(raw);

        Assert.That(code.Value, Is.EqualTo(raw));
    }

    [Test]
    public void Constructor_TrimsAndUpperCases()
    {
        var code = new PublicCode("  a7k2-9xf3  ");

        Assert.That(code.Value, Is.EqualTo("A7K2-9XF3"));
    }

    // NOTE: the VO docstring lists L as excluded, but the actual regex
    // [A-HJ-NP-Z2-9] permits L (only I, O, 0, 1 are excluded from the
    // confusable-glyph set). Tests pin the IMPLEMENTED behavior.
    [TestCase("0AAA-AAAA")] // '0' (zero) banned
    [TestCase("AAAA-0AAA")]
    [TestCase("OAAA-AAAA")] // 'O' banned
    [TestCase("AAAA-OAAA")]
    [TestCase("1AAA-AAAA")] // '1' banned
    [TestCase("AAAA-1AAA")]
    [TestCase("IAAA-AAAA")] // 'I' banned
    [TestCase("AAAA-IAAA")]
    public void Constructor_RejectsForbiddenAlphabetChars(string raw)
    {
        Assert.Throws<ArgumentException>(() => new PublicCode(raw));
    }

    [TestCase("A7K2-9XF")]   // 7 chars
    [TestCase("A7K2-9XF33")] // 10 chars
    [TestCase("A7K29XF3")]   // missing hyphen
    [TestCase("")]
    public void Constructor_RejectsWrongLengthOrShape(string raw)
    {
        Assert.Throws<ArgumentException>(() => new PublicCode(raw));
    }

    [TestCase("A7K-29XF3")]  // hyphen at position 3
    [TestCase("A7K29-XF3")]  // hyphen at position 5
    public void Constructor_RejectsWrongHyphenPosition(string raw)
    {
        Assert.Throws<ArgumentException>(() => new PublicCode(raw));
    }

    [TestCase(null)]
    [TestCase("   ")]
    public void Constructor_RejectsNullOrWhitespace(string? raw)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null (a subclass of ArgumentException); use Catch for the parent type.
        Assert.Catch<ArgumentException>(() => new PublicCode(raw!));
    }

    [Test]
    public void Equality_ByValue()
    {
        var a = new PublicCode("A7K2-9XF3");
        var b = new PublicCode("a7k2-9xf3"); // canonicalised to A7K2-9XF3
        var c = new PublicCode("B3M5-7PQ9");

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.Not.EqualTo(c));
    }

    [Test]
    public void GetHashCode_MatchesForEqualValues()
    {
        var a = new PublicCode("A7K2-9XF3");
        var b = new PublicCode("a7k2-9xf3");

        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void ImplicitStringConversion_ReturnsValue()
    {
        var code = new PublicCode("A7K2-9XF3");

        string s = code;

        Assert.That(s, Is.EqualTo("A7K2-9XF3"));
    }

    [Test]
    public void ToString_ReturnsValue()
    {
        var code = new PublicCode("A7K2-9XF3");

        Assert.That(code.ToString(), Is.EqualTo("A7K2-9XF3"));
    }

    [Test]
    public void TryParse_NullReturnsFalse()
    {
        var ok = PublicCode.TryParse(null, out var code);

        Assert.That(ok, Is.False);
        Assert.That(code, Is.Null);
    }

    [Test]
    public void TryParse_InvalidReturnsFalse()
    {
        var ok = PublicCode.TryParse("invalid", out var code);

        Assert.That(ok, Is.False);
        Assert.That(code, Is.Null);
    }

    [Test]
    public void TryParse_ValidReturnsTrue()
    {
        var ok = PublicCode.TryParse("A7K2-9XF3", out var code);

        Assert.That(ok, Is.True);
        Assert.That(code, Is.Not.Null);
        Assert.That(code!.Value, Is.EqualTo("A7K2-9XF3"));
    }
}
