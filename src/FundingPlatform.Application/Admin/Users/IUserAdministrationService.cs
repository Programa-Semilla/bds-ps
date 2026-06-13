using FundingPlatform.Application.Admin.Users.Batch;
using FundingPlatform.Application.Admin.Users.DTOs;

namespace FundingPlatform.Application.Admin.Users;

public interface IUserAdministrationService
{
    Task<ListUsersResult> ListUsersAsync(ListUsersRequest request, CancellationToken ct);
    Task<UserDetailDto?> GetUserAsync(string userId, CancellationToken ct);
    Task<Result<UserDetailDto>> CreateUserAsync(CreateUserRequest request, string actorUserId, CancellationToken ct);
    Task<Result<UserDetailDto>> UpdateUserAsync(UpdateUserRequest request, string actorUserId, CancellationToken ct);
    Task<Result> DisableUserAsync(string targetUserId, string actorUserId, CancellationToken ct);
    Task<Result> EnableUserAsync(string targetUserId, string actorUserId, CancellationToken ct);
    Task<Result> ResetUserPasswordAsync(ResetPasswordRequest request, string actorUserId, CancellationToken ct);

    /// <summary>
    /// Spec 034 — validate + create up to 200 Solicitante accounts from parsed CSV
    /// rows. Each row is processed independently (per-row atomic via
    /// <see cref="CreateUserAsync"/>); invalid rows are skipped and reported. Does
    /// NOT parse CSV and does NOT send invitations — the controller owns both.
    /// </summary>
    Task<BatchUserCreateResult> CreateUsersBatchAsync(
        IReadOnlyList<BatchUserImportRow> rows,
        string actorUserId,
        CancellationToken ct);
}
