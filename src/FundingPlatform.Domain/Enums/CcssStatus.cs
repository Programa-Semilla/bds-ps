namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 038 — provider CCSS (social-security) regulatory status. Stored as
/// TINYINT via <c>HasConversion&lt;byte?&gt;()</c>; <c>null</c> means "sin revisar".
/// Numeric codes follow the source order in spec §13 starting at 1. Verbatim
/// Spanish labels live in <c>RegulatoryStatusLabels</c>.
/// </summary>
public enum CcssStatus : byte
{
    /// <summary>"sin inscripción"</summary>
    SinInscripcion = 1,

    /// <summary>"al día" — the favorable status.</summary>
    AlDia = 2,

    /// <summary>"estado moroso"</summary>
    EstadoMoroso = 3,

    /// <summary>"cobro administrativo"</summary>
    CobroAdministrativo = 4,

    /// <summary>"estado inactivo / al día"</summary>
    EstadoInactivoAlDia = 5,

    /// <summary>"estado inactivo / moroso"</summary>
    EstadoInactivoMoroso = 6,

    /// <summary>"sin información"</summary>
    SinInformacion = 7,

    /// <summary>"cobro judicial"</summary>
    CobroJudicial = 8,
}
