using FundingPlatform.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.Helpers;

/// <summary>
/// Spec 038 — single source of truth for the verbatim Spanish labels of the three
/// regulatory status enums (preserved per client requirement §28.5). The DB stores
/// only numeric codes; this resolver turns them into display text. A <c>null</c>
/// status renders as "sin revisar".
/// </summary>
public static class RegulatoryStatusLabels
{
    public const string Unreviewed = "sin revisar";

    private static readonly IReadOnlyDictionary<HaciendaStatus, string> HaciendaMap = new Dictionary<HaciendaStatus, string>
    {
        [HaciendaStatus.SinInscripcion] = "sin inscripción",
        [HaciendaStatus.AlDia] = "al día",
        [HaciendaStatus.EstadoMoroso] = "estado moroso",
        [HaciendaStatus.CobroAdministrativo] = "cobro administrativo",
        [HaciendaStatus.DesinscritoAlDia] = "desinscrito al día",
        [HaciendaStatus.SinInformacion] = "sin información",
        [HaciendaStatus.DesinscritoMoroso] = "desinscrito moroso",
        [HaciendaStatus.DesinscritoDeOficio] = "desinscrito de oficio",
    };

    private static readonly IReadOnlyDictionary<CcssStatus, string> CcssMap = new Dictionary<CcssStatus, string>
    {
        [CcssStatus.SinInscripcion] = "sin inscripción",
        [CcssStatus.AlDia] = "al día",
        [CcssStatus.EstadoMoroso] = "estado moroso",
        [CcssStatus.CobroAdministrativo] = "cobro administrativo",
        [CcssStatus.EstadoInactivoAlDia] = "estado inactivo / al día",
        [CcssStatus.EstadoInactivoMoroso] = "estado inactivo / moroso",
        [CcssStatus.SinInformacion] = "sin información",
        [CcssStatus.CobroJudicial] = "cobro judicial",
    };

    private static readonly IReadOnlyDictionary<SicopStatus, string> SicopMap = new Dictionary<SicopStatus, string>
    {
        [SicopStatus.Inhabilitacion] = "inhabilitación",
        [SicopStatus.SinSanciones] = "sin sanciones",
        [SicopStatus.SinSuscripcion] = "sin suscripción",
        [SicopStatus.ConSanciones] = "con sanciones",
        [SicopStatus.Suspension] = "suspensión",
    };

    public static string Label(HaciendaStatus? status) =>
        status is { } s && HaciendaMap.TryGetValue(s, out var v) ? v : Unreviewed;

    public static string Label(CcssStatus? status) =>
        status is { } s && CcssMap.TryGetValue(s, out var v) ? v : Unreviewed;

    public static string Label(SicopStatus? status) =>
        status is { } s && SicopMap.TryGetValue(s, out var v) ? v : Unreviewed;

    public static IEnumerable<SelectListItem> HaciendaItems(HaciendaStatus? selected) =>
        BuildItems(HaciendaMap.Select(kv => ((byte)kv.Key, kv.Value)), selected is { } s ? (byte)s : null);

    public static IEnumerable<SelectListItem> CcssItems(CcssStatus? selected) =>
        BuildItems(CcssMap.Select(kv => ((byte)kv.Key, kv.Value)), selected is { } s ? (byte)s : null);

    public static IEnumerable<SelectListItem> SicopItems(SicopStatus? selected) =>
        BuildItems(SicopMap.Select(kv => ((byte)kv.Key, kv.Value)), selected is { } s ? (byte)s : null);

    private static IEnumerable<SelectListItem> BuildItems(IEnumerable<(byte Code, string Text)> entries, byte? selected)
    {
        yield return new SelectListItem(Unreviewed, string.Empty, selected is null);
        foreach (var (code, text) in entries)
        {
            yield return new SelectListItem(text, code.ToString(System.Globalization.CultureInfo.InvariantCulture), selected == code);
        }
    }
}
