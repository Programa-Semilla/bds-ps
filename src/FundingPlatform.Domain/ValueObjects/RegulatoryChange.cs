using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 038 — what changed on a <c>Supplier</c> regulatory edit. Returned by
/// <c>Supplier.ApplyRegulatoryEdit</c> / <c>ConfirmRegulatoryReviewed</c> so the
/// orchestrating service can write one <c>AdminAuditEvent</c> per change without
/// the entity touching persistence. The audit action is derived from
/// (<see cref="Field"/>, <see cref="Kind"/>).
/// </summary>
public sealed record RegulatoryChange(
    RegulatoryChangeField Field,
    string? OldValue,
    string? NewValue,
    RegulatoryChangeKind Kind,
    RegulatoryReviewSource Source);

/// <summary>
/// Discriminator for what a <see cref="RegulatoryChange"/> targets. Broader than
/// <see cref="RegulatoryField"/> because PME/PYME and the warning flag are also
/// audited via the same change record (but carry no last-reviewed metadata).
/// </summary>
public enum RegulatoryChangeField : byte
{
    Hacienda = 1,
    Ccss = 2,
    Sicop = 3,
    Pme = 4,
    Warning = 5,
}

/// <summary>
/// Whether a <see cref="RegulatoryChange"/> represents an actual value change or
/// a "reviewed — no change" re-authorization that only refreshes the freshness
/// timestamp.
/// </summary>
public enum RegulatoryChangeKind : byte
{
    Changed = 1,
    ReviewedNoChange = 2,
}
