using System.Collections.Concurrent;
using FundingPlatform.Application.Abstractions.Hacienda;

namespace FundingPlatform.Infrastructure.Hacienda;

/// <summary>
/// Spec 043 — offline test double for <see cref="IHaciendaApiClient"/> (mirrors
/// <c>StubAiClient</c>). Selected in dev/E2E via <c>Regulatory:HaciendaSync:Provider=Fake</c>
/// so the live API is never called in tests. Outcomes are staged by identification
/// (digits-only normalized) with a configurable default; static counters + Reset()
/// give test isolation. Staging is also reachable in E2E via the Development-only
/// <c>/Dev/StageHaciendaOutcome</c> endpoint.
/// </summary>
public sealed class FakeHaciendaApiClient : IHaciendaApiClient
{
    public static int LookupCallCount;

    private static readonly ConcurrentDictionary<string, HaciendaLookupResult> Staged = new();

    // Default favorable result so a plain sync run sets every provider to "al día".
    private static volatile HaciendaLookupResult _default =
        HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", Moroso: false, Omiso: false));

    public static void Reset()
    {
        Interlocked.Exchange(ref LookupCallCount, 0);
        Staged.Clear();
        _default = HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", Moroso: false, Omiso: false));
    }

    /// <summary>Stage a specific outcome for one identification (matched digits-only).</summary>
    public static void StageOutcome(string identificacion, HaciendaLookupResult result)
        => Staged[Normalize(identificacion)] = result;

    /// <summary>Stage the fallback outcome returned for any non-staged identification.</summary>
    public static void StageDefault(HaciendaLookupResult result) => _default = result;

    public Task<HaciendaLookupResult> LookupAsync(string identificacion, CancellationToken ct)
    {
        Interlocked.Increment(ref LookupCallCount);
        var key = Normalize(identificacion);
        return Task.FromResult(Staged.TryGetValue(key, out var r) ? r : _default);
    }

    /// <summary>Digits-only normalization so a canonical "3-101-700001" and "3101700001" match.</summary>
    private static string Normalize(string? identificacion)
        => new(((identificacion ?? string.Empty)).Where(char.IsDigit).ToArray());
}
