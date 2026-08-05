using FundingPlatform.Application.Abstractions.AiComparison;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Infrastructure.AiComparison.Anthropic;

/// <summary>
/// Spec 020 — offline-test stub. Returns canned schema-valid responses loaded
/// from <c>tests/Fixtures/AiComparison/canned-extract.json</c> and
/// <c>canned-compare.json</c> (or any path overridden via the
/// <c>AiComparison:StubFixtures:*</c> keys). Selected when
/// <c>AiComparison:Provider == "Stub"</c>.
///
/// The stub also exposes a static counter so tests can assert "cached path
/// took zero AI calls" (US2). The counter is process-wide; tests reset it via
/// <see cref="ResetCallCounters"/>.
///
/// Fixtures are resolved and read <b>lazily</b>, never in the constructor. This
/// type is a ctor dependency of <c>ComparisonOrchestrator</c>, which is a ctor
/// dependency of <c>ReviewController</c> — a throwing ctor fails controller
/// activation and 500s the whole /Review surface rather than just the
/// comparison region. The published container does not ship
/// <c>tests/Fixtures/AiComparison/</c>, so a deploy that leaves the provider at
/// its <c>Stub</c> default hits exactly that path; it must degrade to a
/// per-item <c>provider_hard:stub_fixture_missing</c> failure instead.
/// </summary>
public class StubAiClient : IAiClient
{
    /// <summary>Surfaced as <c>provider_hard:stub_fixture_missing</c> by the orchestrator.</summary>
    internal const string MissingFixtureCode = "stub_fixture_missing";

    private readonly Lazy<string> _extractJson;
    private readonly Lazy<string> _compareJson;

    public static int ExtractCallCount;
    public static int CompareCallCount;

    public StubAiClient(IConfiguration configuration)
    {
        _extractJson = new Lazy<string>(() => LoadFixture(
            configuration["AiComparison:StubFixtures:Extract"], "canned-extract.json"));
        _compareJson = new Lazy<string>(() => LoadFixture(
            configuration["AiComparison:StubFixtures:Compare"], "canned-compare.json"));
    }

    public Task<ExtractResult> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ExtractCallCount);

        // Bind supplierIdx — the stub's canned-extract.json may be a single
        // object or a per-supplier array; we just echo the same JSON for
        // every extract call. Tests that need per-supplier customization can
        // point AiComparison:StubFixtures:Extract at a richer fixture.
        return Task.FromResult(new ExtractResult(_extractJson.Value, 0, 0, 1));
    }

    public Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CompareCallCount);
        return Task.FromResult(new CompareResult(_compareJson.Value, 0, 0, 1));
    }

    public static void ResetCallCounters()
    {
        Interlocked.Exchange(ref ExtractCallCount, 0);
        Interlocked.Exchange(ref CompareCallCount, 0);
    }

    /// <summary>
    /// Resolves and reads a fixture on first use. Every failure — configured
    /// path missing, discovery exhausted, unreadable file — surfaces as
    /// <see cref="AiProviderHardException"/> so the orchestrator records
    /// <c>provider_hard:stub_fixture_missing</c> and the reviewer sees a failed
    /// generation instead of a broken page.
    /// </summary>
    private static string LoadFixture(string? configuredPath, string name)
    {
        var path = configuredPath ?? ResolveFixture(name);
        if (path is null || !File.Exists(path))
        {
            throw new AiProviderHardException(
                MissingFixtureCode,
                $"Stub fixture '{name}' not found. Set AiComparison:StubFixtures:* or place it under " +
                "tests/Fixtures/AiComparison/. The published container does not ship the tests/ tree — " +
                "set AiComparison:Provider=Anthropic with a valid key for deployed environments.");
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new AiProviderHardException(
                MissingFixtureCode, $"Stub fixture '{name}' could not be read from '{path}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AiProviderHardException(
                MissingFixtureCode, $"Stub fixture '{name}' could not be read from '{path}'.", ex);
        }
    }

    /// <summary>Returns <c>null</c> when discovery finds nothing — the caller raises the typed failure.</summary>
    private static string? ResolveFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Fixtures", "AiComparison", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        // Repo-local fallback for non-test hosts (e.g. running quickstart with
        // AiComparison:Provider=Stub from the AppHost).
        var fallback = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AiComparison", name);
        return File.Exists(fallback) ? fallback : null;
    }
}
