namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 038 — provider SICOP (public-procurement) regulatory status. Stored as
/// TINYINT via <c>HasConversion&lt;byte?&gt;()</c>; <c>null</c> means "sin revisar".
/// Numeric codes follow the source order in spec §13 starting at 1. Verbatim
/// Spanish labels live in <c>RegulatoryStatusLabels</c>.
/// </summary>
public enum SicopStatus : byte
{
    /// <summary>"inhabilitación"</summary>
    Inhabilitacion = 1,

    /// <summary>"sin sanciones" — the favorable status.</summary>
    SinSanciones = 2,

    /// <summary>"sin suscripción"</summary>
    SinSuscripcion = 3,

    /// <summary>"con sanciones"</summary>
    ConSanciones = 4,

    /// <summary>"suspensión"</summary>
    Suspension = 5,
}
