namespace FundingPlatform.Application.Abstractions.Hacienda;

/// <summary>Spec 043 — discriminator for a Hacienda <c>fe/ae</c> lookup outcome.</summary>
public enum HaciendaLookupKind
{
    /// <summary>HTTP 200 with a parseable body.</summary>
    Found,
    /// <summary>HTTP 404 (<c>{code:404, …}</c>) — "information not available".</summary>
    NotRegistered,
    /// <summary>Transport error / non-200-non-404 / timeout / unparseable body /
    /// unrecognized estado / malformed-or-missing local id.</summary>
    Failed,
}

/// <summary>Spec 043 — parsed <c>situacion</c> block (<c>"SI"</c>/<c>"NO"</c> → bool).</summary>
public sealed record HaciendaSituacion(string Estado, bool Moroso, bool Omiso);

/// <summary>
/// Spec 043 — result of one <see cref="IHaciendaApiClient.LookupAsync"/>. A
/// discriminated result: <see cref="Found"/> (200 parsed) / <see cref="NotRegistered"/>
/// (404) / <see cref="Failed"/> (everything else). The mapper yields a non-null
/// <c>HaciendaStatus</c> for Found/NotRegistered and is never called for Failed.
/// </summary>
public sealed record HaciendaLookupResult(
    HaciendaLookupKind Kind,
    string? Nombre = null,
    HaciendaSituacion? Situacion = null,
    string? Reason = null)
{
    public static HaciendaLookupResult Found(string? nombre, HaciendaSituacion situacion)
        => new(HaciendaLookupKind.Found, nombre, situacion);

    public static HaciendaLookupResult NotRegistered()
        => new(HaciendaLookupKind.NotRegistered);

    public static HaciendaLookupResult Failed(string reason)
        => new(HaciendaLookupKind.Failed, Reason: reason);
}
