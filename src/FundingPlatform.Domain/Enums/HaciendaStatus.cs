namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 038 — provider Hacienda (tax-authority) regulatory status. Stored as
/// TINYINT via <c>HasConversion&lt;byte?&gt;()</c>; <c>null</c> on the entity means
/// "sin revisar" (not yet reviewed). Numeric codes follow the source order in
/// spec §13 starting at 1. The verbatim Spanish labels live in
/// <c>RegulatoryStatusLabels</c>, never in the DB.
/// </summary>
public enum HaciendaStatus : byte
{
    /// <summary>"sin inscripción"</summary>
    SinInscripcion = 1,

    /// <summary>"al día" — the favorable status.</summary>
    AlDia = 2,

    /// <summary>"estado moroso"</summary>
    EstadoMoroso = 3,

    /// <summary>"cobro administrativo"</summary>
    CobroAdministrativo = 4,

    /// <summary>"desinscrito al día"</summary>
    DesinscritoAlDia = 5,

    /// <summary>"sin información"</summary>
    SinInformacion = 6,

    /// <summary>"desinscrito moroso"</summary>
    DesinscritoMoroso = 7,

    /// <summary>"desinscrito de oficio"</summary>
    DesinscritoDeOficio = 8,
}
