using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Regulatory;

/// <summary>
/// Spec 043 — one stale (provider, required-field) pair backing the hard gate and
/// the non-blocking warning. <see cref="LastReviewedAt"/> is null when the field
/// was never reviewed.
/// </summary>
public sealed record StaleRegulatoryFinding(
    int SupplierId,
    string SupplierName,
    RegulatoryField Field,
    DateTime? LastReviewedAt);
