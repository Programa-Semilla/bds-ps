using FundingPlatform.Application.Abstractions.Hacienda;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Infrastructure.Hacienda;

/// <summary>
/// Spec 043 / research D1 — pure mapping from a Hacienda <c>fe/ae</c> lookup result to
/// the <see cref="HaciendaStatus"/> enum. Returns null ONLY for
/// <see cref="HaciendaLookupKind.Failed"/> (the caller records a sync failure instead);
/// an unrecognized <c>estado</c> is treated as a failure (null) rather than guessed.
/// </summary>
public static class HaciendaStatusMapper
{
    public static HaciendaStatus? Map(HaciendaLookupResult result)
    {
        switch (result.Kind)
        {
            case HaciendaLookupKind.NotRegistered:
                // 404 = "information not available" → distinct from a 200 "No inscrito".
                return HaciendaStatus.SinInformacion;

            case HaciendaLookupKind.Failed:
                return null;

            case HaciendaLookupKind.Found:
                if (result.Situacion is not { } s) return null;
                var estado = (s.Estado ?? string.Empty).Trim().ToLowerInvariant();
                return estado switch
                {
                    "inscrito" => s.Moroso
                        ? HaciendaStatus.EstadoMoroso
                        : s.Omiso ? HaciendaStatus.CobroAdministrativo : HaciendaStatus.AlDia,
                    // The fe/ae `estado` vocabulary does not distinguish "de oficio", so
                    // HaciendaStatus.DesinscritoDeOficio is unreachable via sync (manual-only).
                    "desinscrito" => s.Moroso
                        ? HaciendaStatus.DesinscritoMoroso
                        : HaciendaStatus.DesinscritoAlDia,
                    "no inscrito" => HaciendaStatus.SinInscripcion,
                    // Unrecognized estado → surface as a failure rather than mismap.
                    _ => null,
                };

            default:
                return null;
        }
    }
}
