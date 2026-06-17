using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DTOs;

public record ApplicationDto(
    int Id,
    int ApplicantId,
    ApplicationState State,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    List<ItemDto> Items,
    // Spec 035 (evolved 2026-06-16, D13) — the application's declared impacts (one or more).
    List<ImpactDto> Impacts,
    // Spec 021 / FR-008 — opaque PublicCode surfaced on every applicant
    // identity rendering. Nullable for legacy rows seeded before the
    // schema cutover; new rows always carry it.
    string? PublicCode = null,
    string? CompanyName = null);

public record ApplicationSummaryDto(
    int Id,
    ApplicationState State,
    int ItemCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    // Spec 021 / FR-008 — surfaced on the applicant dashboard in place of
    // the legacy numeric Id (`Solicitud N.º N`). Nullable while we transition.
    string? PublicCode = null,
    string? CompanyName = null);
