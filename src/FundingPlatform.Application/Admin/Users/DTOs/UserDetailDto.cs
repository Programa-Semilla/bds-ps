using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Users.DTOs;

/// <summary>
/// Spec 016 — adds <c>GroupIds</c> + <c>ConcurrencyStamp</c> so the admin edit
/// form can pre-select current memberships and round-trip the optimistic stamp.
/// Spec 026 — adds <c>IdentificationType</c> so the edit form pre-selects the kind.
/// Spec 032 — adds <c>UserCode</c> so the edit form pre-fills it and lists/detail can show it.
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
    string? ConcurrencyStamp,
    IdentificationType? IdentificationType = null,
    string? UserCode = null);
