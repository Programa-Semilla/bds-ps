using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Users.DTOs;

/// <summary>
/// Spec 016 — adds <c>GroupIds</c> + <c>ConcurrencyStamp</c>. The stamp comes
/// from the existing <c>IdentityUser.ConcurrencyStamp</c> rendered on the edit
/// form. <c>GroupIds</c> drives the membership diff (FR-008..FR-010).
/// Spec 026 — adds <c>IdentificationType</c> for the Applicant legal-ID kind.
/// Spec 032 — adds <c>UserCode</c>, the admin-assigned unique code (required for Solicitante).
/// </summary>
public record UpdateUserRequest(
    string UserId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Role,
    string? LegalId,
    IReadOnlyList<int> GroupIds,
    string? ConcurrencyStamp,
    IdentificationType? IdentificationType = null,
    string? UserCode = null);
