namespace FundingPlatform.Application.Abstractions.Hacienda;

/// <summary>
/// Spec 043 — the replaceable seam over the Ministerio de Hacienda <c>fe/ae</c>
/// taxpayer-status endpoint. The live API is never called in tests (a fake is
/// config-selected, mirroring <c>StubAiClient</c>).
/// </summary>
public interface IHaciendaApiClient
{
    /// <summary>
    /// Looks up one taxpayer by identification number. MUST NOT throw for HTTP /
    /// transport errors — those map to <see cref="HaciendaLookupResult.Failed"/>.
    /// </summary>
    Task<HaciendaLookupResult> LookupAsync(string identificacion, CancellationToken ct);
}
