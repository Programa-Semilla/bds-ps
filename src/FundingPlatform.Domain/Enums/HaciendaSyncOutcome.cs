namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 043 — outcome of the last daily Hacienda sync attempt for a provider.
/// Stored as TINYINT via <c>HasConversion&lt;byte?&gt;()</c>; <c>null</c> on the entity
/// means the provider was never synced. es-CR labels live in the Web resources.
/// </summary>
public enum HaciendaSyncOutcome : byte
{
    Success = 1,
    Failure = 2,
}
