using FundingPlatform.Application.Admin.Users.Batch;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Batch;

public class IdentificationInferenceTests
{
    [Test]
    public void NineDigits_InfersCedulaFisica()
    {
        Assert.That(IdentificationInference.TryInfer("117740469", out var id), Is.True);
        Assert.That(id!.Type, Is.EqualTo(IdentificationType.CedulaFisica));
        Assert.That(id.Value, Is.EqualTo("1-1774-0469")); // canonical 1-4-4
    }

    [Test]
    public void NineDigitsAlreadyHyphenated_InfersCedulaFisica()
    {
        Assert.That(IdentificationInference.TryInfer("1-1774-0469", out var id), Is.True);
        Assert.That(id!.Type, Is.EqualTo(IdentificationType.CedulaFisica));
    }

    [Test]
    [TestCase("155822492214")] // geoflores — the real-world failing value
    [TestCase("122201638122")]
    [TestCase("12345678901")]  // 11 digits
    public void ElevenOrTwelveDigits_InfersDimex(string raw)
    {
        Assert.That(IdentificationInference.TryInfer(raw, out var id), Is.True);
        Assert.That(id!.Type, Is.EqualTo(IdentificationType.Dimex));
        Assert.That(id.Value, Is.EqualTo(raw));
    }

    [Test]
    public void ValueWithLetters_InfersPasaporte()
    {
        Assert.That(IdentificationInference.TryInfer("PA123456", out var id), Is.True);
        Assert.That(id!.Type, Is.EqualTo(IdentificationType.Pasaporte));
    }

    [Test]
    [TestCase("1234567890")] // 10 digits — entity shape, not an individual; errored
    [TestCase("1234")]       // too short
    [TestCase("")]
    [TestCase(null)]
    public void Unrecognized_ReturnsFalse(string? raw)
    {
        Assert.That(IdentificationInference.TryInfer(raw, out var id), Is.False);
        Assert.That(id, Is.Null);
    }
}
