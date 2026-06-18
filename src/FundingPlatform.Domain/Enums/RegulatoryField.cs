namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 038 — the three regulatory status fields that carry per-field
/// last-reviewed metadata and support the "reviewed — no change" re-authorize
/// action.
/// </summary>
public enum RegulatoryField : byte
{
    Hacienda = 1,
    Ccss = 2,
    Sicop = 3,
}
