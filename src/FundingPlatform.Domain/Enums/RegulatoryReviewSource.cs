namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 038 — provenance of a regulatory-status review. Slice A only ever writes
/// <see cref="Manual"/>; <see cref="Api"/>/<see cref="System"/> are reserved for
/// the deferred Hacienda-API sync (slice D). Stored as TINYINT.
/// </summary>
public enum RegulatoryReviewSource : byte
{
    Manual = 1,
    Api = 2,
    System = 3,
}
