using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Infrastructure.AiComparison.Anthropic;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Tests.Unit.AiComparison;

/// <summary>
/// Spec 020 — the stub provider must never take down the DI graph. It is a
/// constructor dependency of <c>ComparisonOrchestrator</c>, which is in turn a
/// constructor dependency of <c>ReviewController</c>: a throwing ctor fails
/// controller activation and 500s the entire /Review surface, not just the
/// comparison region. The published container does not ship
/// <c>tests/Fixtures/AiComparison/</c>, so a keyless deploy hits exactly that
/// path — the missing fixture must degrade to a per-item generation failure.
/// </summary>
[TestFixture]
public class StubAiClientTests
{
    private static IConfiguration ConfigWith(string? extractPath, string? comparePath)
    {
        var values = new Dictionary<string, string?>
        {
            ["AiComparison:StubFixtures:Extract"] = extractPath,
            ["AiComparison:StubFixtures:Compare"] = comparePath,
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string MissingPath(string name) =>
        Path.Combine(Path.GetTempPath(), $"fp-missing-{Guid.NewGuid():N}", name);

    [Test]
    public void Constructor_DoesNotThrow_WhenFixturesAreMissing()
    {
        var config = ConfigWith(MissingPath("canned-extract.json"), MissingPath("canned-compare.json"));

        Assert.DoesNotThrow(() => _ = new StubAiClient(config));
    }

    [Test]
    public void ExtractAsync_ThrowsProviderHard_WhenFixtureIsMissing()
    {
        var client = new StubAiClient(ConfigWith(MissingPath("canned-extract.json"), MissingPath("canned-compare.json")));

        var ex = Assert.ThrowsAsync<AiProviderHardException>(() =>
            client.ExtractAsync(new ExtractRequest("m", "p", "{}", Array.Empty<AiInputBlock>()), CancellationToken.None));

        Assert.That(ex!.ProviderCode, Is.EqualTo("stub_fixture_missing"));
    }

    [Test]
    public void CompareAsync_ThrowsProviderHard_WhenFixtureIsMissing()
    {
        var client = new StubAiClient(ConfigWith(MissingPath("canned-extract.json"), MissingPath("canned-compare.json")));

        var ex = Assert.ThrowsAsync<AiProviderHardException>(() =>
            client.CompareAsync(new CompareRequest("m", "p", "{}", "{}"), CancellationToken.None));

        Assert.That(ex!.ProviderCode, Is.EqualTo("stub_fixture_missing"));
    }

    [Test]
    public async Task ExtractAsync_ReturnsFixtureContents_WhenConfigured()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fp-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var extractPath = Path.Combine(dir, "canned-extract.json");
        await File.WriteAllTextAsync(extractPath, "{\"supplier\":\"stub\"}");

        try
        {
            var client = new StubAiClient(ConfigWith(extractPath, MissingPath("canned-compare.json")));

            var result = await client.ExtractAsync(
                new ExtractRequest("m", "p", "{}", Array.Empty<AiInputBlock>()), CancellationToken.None);

            Assert.That(result.Json, Is.EqualTo("{\"supplier\":\"stub\"}"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
