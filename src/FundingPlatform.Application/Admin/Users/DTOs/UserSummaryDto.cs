namespace FundingPlatform.Application.Admin.Users.DTOs;

public record UserSummaryDto(
    string Id,
    string FullName,
    string Email,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    string? UserCode = null);
