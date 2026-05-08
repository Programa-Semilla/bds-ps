namespace FundingPlatform.Application.Admin.Users.DTOs;

/// <summary>
/// Spec 016 — adds <c>GroupIds</c> for the multi-select on the admin user form
/// (FR-007, FR-009). When the resulting role is Admin, the service silently
/// ignores any posted GroupIds (FR-009 edge case).
/// </summary>
public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Role,
    string InitialPassword,
    string? LegalId,
    IReadOnlyList<int> GroupIds);
