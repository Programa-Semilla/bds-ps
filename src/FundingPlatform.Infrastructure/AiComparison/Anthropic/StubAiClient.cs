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
/// </summary>
public class StubAiClient : IAiClient
{
    private readonly string _extractJson;
    private readonly string _compareJson;

    public static int ExtractCallCount;
    public static int CompareCallCount;

    public StubAiClient(IConfiguration configuration)
    {
        var extractPath = configuration["AiComparison:StubFixtures:Extract"]
            ?? ResolveFixture("canned-extract.json");
        var comparePath = configuration["AiComparison:StubFixtures:Compare"]
            ?? ResolveFixture("canned-compare.json");

        _extractJson = File.ReadAllText(extractPath);
        _compareJson = File.ReadAllText(comparePath);
    }

    public Task<ExtractResult> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ExtractCallCount);

        // Bind supplierIdx — the stub's canned-extract.json may be a single
        // object or a per-supplier array; we just echo the same JSON for
        // every extract call. Tests that need per-supplier customization can
        // point AiComparison:StubFixtures:Extract at a richer fixture.
        return Task.FromResult(new ExtractResult(_extractJson, 0, 0, 1));
    }

    public Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CompareCallCount);
        return Task.FromResult(new CompareResult(_compareJson, 0, 0, 1));
    }

    public static void ResetCallCounters()
    {
        Interlocked.Exchange(ref ExtractCallCount, 0);
        Interlocked.Exchange(ref CompareCallCount, 0);
    }

    private static string ResolveFixture(string name)
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
        if (File.Exists(fallback)) return fallback;

        throw new FileNotFoundException(
            $"Stub fixture '{name}' not found. Set AiComparison:StubFixtures:* or place it under tests/Fixtures/AiComparison/.");
    }
}
