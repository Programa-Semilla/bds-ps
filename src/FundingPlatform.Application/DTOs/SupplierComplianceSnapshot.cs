using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DTOs;

/// <summary>
/// Spec 038 (US3 / FR-016/019) — read-only snapshot of a provider's regulatory
/// compliance, warning, and per-field review freshness, surfaced to reviewers
/// during application review. Carries raw enums/dates; the Web layer formats the
/// verbatim labels + es-CR freshness (reviewer name omitted on this surface).
/// </summary>
public sealed record SupplierComplianceSnapshot(
    bool HasWarning,
    string? WarningNote,
    HaciendaStatus? Hacienda,
    DateTime? HaciendaReviewedAt,
    RegulatoryReviewSource? HaciendaSource,
    CcssStatus? Ccss,
    DateTime? CcssReviewedAt,
    RegulatoryReviewSource? CcssSource,
    SicopStatus? Sicop,
    DateTime? SicopReviewedAt,
    RegulatoryReviewSource? SicopSource);
