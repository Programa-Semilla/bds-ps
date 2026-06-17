using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Users.DTOs;

/// <summary>
/// Spec 016 — adds <c>GroupIds</c> for the multi-select on the admin user form
/// (FR-007, FR-009). When the resulting role is Admin, the service silently
/// ignores any posted GroupIds (FR-009 edge case).
/// Spec 026 — adds <c>IdentificationType</c> for the Applicant legal-ID kind.
/// Spec 032 — adds <c>UserCode</c>, the admin-assigned unique code (required for Solicitante).
/// Spec 033 — <c>InitialPassword</c> removed: accounts are created with no password and
/// the user sets their own via an emailed 72h invitation (the controller issues + sends it).
/// Spec 037 — adds <c>CompanyNames</c>: the at-creation companies for a Solicitante
/// (≥1 required). The service attaches a Company row per name in the same SaveChanges
/// as the Applicant (D4); ignored for non-Applicant roles.
/// </summary>
public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Role,
    string? LegalId,
    IReadOnlyList<int> GroupIds,
    IdentificationType? IdentificationType = null,
    string? UserCode = null,
    IReadOnlyList<string>? CompanyNames = null);
