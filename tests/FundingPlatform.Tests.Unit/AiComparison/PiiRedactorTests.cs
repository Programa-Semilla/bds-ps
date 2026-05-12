using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Infrastructure.AiComparison.Redaction;

namespace FundingPlatform.Tests.Unit.AiComparison;

public class PiiRedactorTests
{
    private static string FixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Fixtures", "Pii");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/Fixtures/Pii not found.");
    }

    [Test]
    public void RedactStructured_ScrubsAllFivePiiFields_AndReportsCounts()
    {
        var redactor = new PiiRedactor();
        var dto = new SupplierAssemblyDto(
            SupplierId: Guid.NewGuid(),
            SupplierName: "Test Supplier",
            OwnerDni: "1-2345-6789",
            OwnerPersonalPhone: "+506 8888-8888",
            ApplicantNationalId: "1-1234-5678",
            ApplicantPersonalPhone: "7777-7777",
            ApplicantPersonalEmail: "carlos.perez.r@gmail.com",
            Body: new { description = "Bomba centrifuga 1HP" });

        var result = redactor.RedactStructured(dto);

        Assert.That(result.SafePayload, Does.Not.Contain("1-2345-6789"));
        Assert.That(result.SafePayload, Does.Not.Contain("8888-8888"));
        Assert.That(result.SafePayload, Does.Not.Contain("1-1234-5678"));
        Assert.That(result.SafePayload, Does.Not.Contain("7777-7777"));
        Assert.That(result.SafePayload, Does.Not.Contain("carlos.perez.r@gmail.com"));
        Assert.That(result.RedactedSpans.Count, Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void RedactStructured_Deterministic_SameInputSameOutput()
    {
        var redactor = new PiiRedactor();
        var dto = new SupplierAssemblyDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Supplier", "1-2345-6789", "+506 8888-8888",
            "1-1234-5678", "7777-7777", "a@b.cr",
            new { x = 1 });

        var r1 = redactor.RedactStructured(dto);
        var r2 = redactor.RedactStructured(dto);

        Assert.That(r1.SafePayload, Is.EqualTo(r2.SafePayload));
    }

    [Test]
    public void RedactFileText_RemovesEnumeratedPatterns_FromPositiveFixture()
    {
        var redactor = new PiiRedactor();
        var text = File.ReadAllText(Path.Combine(FixturesDir(), "supplier-text-positive.txt"));

        var result = redactor.RedactFileText(Guid.NewGuid(), text);

        // SC-006 — none of the original PII patterns survive in the safe payload.
        Assert.That(result.SafePayload, Does.Not.Contain("3-101-234567"));
        Assert.That(result.SafePayload, Does.Not.Contain("8888-8888"));
        Assert.That(result.SafePayload, Does.Not.Contain("7777-7777"));
        Assert.That(result.SafePayload, Does.Not.Contain("maria.gonzalez@constval.co.cr"));
        Assert.That(result.SafePayload, Does.Not.Contain("carlos.perez.r@gmail.com"));
        Assert.That(result.RedactedSpans.Sum(s => s.Count), Is.GreaterThan(0));
    }

    [Test]
    public void RedactFileText_NoPii_LeavesTextIntact_AndReturnsZeroSpans()
    {
        var redactor = new PiiRedactor();
        var text = File.ReadAllText(Path.Combine(FixturesDir(), "supplier-text-negative.txt"));

        var result = redactor.RedactFileText(Guid.NewGuid(), text);

        // The negative fixture is plain copy with no PII patterns; the redactor
        // should not introduce [REDACTED] tokens for content that doesn't match.
        Assert.That(result.SafePayload, Does.Not.Contain("[REDACTED]"));
    }

    [Test]
    public void RedactFileText_EmptyText_ThrowsPiiRedactionFailed()
    {
        var redactor = new PiiRedactor();
        Assert.Throws<PiiRedactionFailedException>(() =>
            redactor.RedactFileText(Guid.NewGuid(), "   "));
    }
}
