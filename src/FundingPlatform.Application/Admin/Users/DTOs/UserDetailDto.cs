namespace FundingPlatform.Application.Admin.Users.DTOs;

/// <summary>
/// Spec 016 — adds <c>GroupIds</c> + <c>ConcurrencyStamp</c> so the admin edit
/// form can pre-select current memberships and round-trip the optimistic stamp.
/// </summary>
public record UserDetailDto(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Role,
    string Status,
    string? LegalId,
    bool MustChangePassword,
    IReadOnlyList<int> GroupIds,
    string? ConcurrencyStamp);
