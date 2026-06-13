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
    // Spec 021 / FR-008 — opaque PublicCode surfaced on every applicant
    // identity rendering. Nullable for legacy rows seeded before the
    // schema cutover; new rows always carry it.
    string? PublicCode = null,
    string? CompanyName = null);
    // Spec 035 / D2 — application-level Impact removed; impact is now per-Item
    // (see ItemDto.Impact).

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
