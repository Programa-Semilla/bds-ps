using System.Text.Json;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Identity;

public class UserAdministrationService : IUserAdministrationService
{
    private const string ApplicantRole = "Applicant";
    private const string ReviewerRole = "Reviewer";
    // Spec 021 / FR-007 — SupplierAdmin is a global-scope role (no Process/Group),
    // assignable from the standard admin Users form (parity with Admin in terms of
    // group handling — see RoleRequiresGroups + NormalizeGroupIdsForRole below).
    private const string SupplierAdminRole = "SupplierAdmin";
    private const string AdminRole = "Admin";

    private static readonly string[] AllowedRoles = [ApplicantRole, ReviewerRole, SupplierAdminRole, AdminRole];

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _dbContext;
    private readonly IAdminAuditWriter _audit;

    public UserAdministrationService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        AppDbContext dbContext,
        IAdminAuditWriter audit)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<ListUsersResult> ListUsersAsync(ListUsersRequest request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u =>
                (u.Email != null && u.Email.Contains(term)) ||
                (u.FirstName != null && u.FirstName.Contains(term)) ||
                (u.LastName != null && u.LastName.Contains(term)));
        }

        if (string.Equals(request.StatusFilter, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var nowUtc = DateTimeOffset.UtcNow;
            query = query.Where(u => u.LockoutEnd == null || u.LockoutEnd <= nowUtc);
        }
        else if (string.Equals(request.StatusFilter, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            var nowUtc = DateTimeOffset.UtcNow;
            query = query.Where(u => u.LockoutEnd != null && u.LockoutEnd > nowUtc);
        }

        if (!string.IsNullOrWhiteSpace(request.RoleFilter) && AllowedRoles.Contains(request.RoleFilter))
        {
            var roleId = await _dbContext.Roles
                .Where(r => r.Name == request.RoleFilter)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(ct);
            if (roleId is null)
            {
                return new ListUsersResult(Array.Empty<UserSummaryDto>(), 0, page, pageSize);
            }
            var userIds = _dbContext.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
            query = query.Where(u => userIds.Contains(u.Id));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var userIdList = users.Select(u => u.Id).ToList();
        var userRolePairs = await _dbContext.UserRoles
            .Where(ur => userIdList.Contains(ur.UserId))
            .Join(_dbContext.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, RoleName = r.Name ?? "" })
            .ToListAsync(ct);
        var rolesByUser = userRolePairs
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => SelectPrimaryRole(g.Select(x => x.RoleName)));

        var nowUtc2 = DateTimeOffset.UtcNow;
        var items = users.Select(u => new UserSummaryDto(
            Id: u.Id,
            FullName: ComposeFullName(u),
            Email: u.Email ?? "",
            Role: rolesByUser.GetValueOrDefault(u.Id, ""),
            Status: IsDisabled(u, nowUtc2) ? "Disabled" : "Active",
            CreatedAt: DateTimeOffset.MinValue)).ToList();

        return new ListUsersResult(items, total, page, pageSize);
    }

    public async Task<UserDetailDto?> GetUserAsync(string userId, CancellationToken ct)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        return await MapToDetailAsync(user, ct);
    }

    public async Task<Result<UserDetailDto>> CreateUserAsync(CreateUserRequest request, string actorUserId, CancellationToken ct)
    {
        var validation = ValidateRoleAndLegalId(request.Role, request.LegalId, isCreate: true);
        if (validation.Count > 0) return Result<UserDetailDto>.Failure(validation);

        // Spec 016 / FR-007: at least one group when role is Applicant or Reviewer.
        // FR-009: Admin role MUST never carry memberships — silently clear.
        var requestedGroupIds = NormalizeGroupIdsForRole(request.Role, request.GroupIds ?? Array.Empty<int>());
        if (RoleRequiresGroups(request.Role) && requestedGroupIds.Count == 0)
        {
            return Result<UserDetailDto>.Failure(
                new DomainError("AT_LEAST_ONE_GROUP", "GroupIds",
                    "At least one group is required for Applicant and Reviewer roles."));
        }
        if (requestedGroupIds.Count > 0)
        {
            var existingGroupIds = await _dbContext.Groups
                .Where(g => requestedGroupIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(ct);
            if (existingGroupIds.Count != requestedGroupIds.Count)
            {
                return Result<UserDetailDto>.Failure(
                    new DomainError("GROUP_NOT_FOUND", "GroupIds",
                        "One or more selected groups do not exist."));
            }
        }

        if (string.Equals(request.Email, IdentityConfiguration.SentinelEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result<UserDetailDto>.Failure(
                new DomainError("EMAIL_IN_USE", nameof(CreateUserRequest.Email),
                    "Email already in use by another account."));
        }

        var user = new ApplicationUser(request.Email, request.FirstName, request.LastName, request.Phone)
        {
            MustChangePassword = true,
        };

        var createResult = await _userManager.CreateAsync(user, request.InitialPassword);
        if (!createResult.Succeeded)
        {
            return Result<UserDetailDto>.Failure(MapIdentityErrors(createResult.Errors));
        }

        var roleResult = await _userManager.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return Result<UserDetailDto>.Failure(MapIdentityErrors(roleResult.Errors));
        }

        // Spec 016 — insert membership rows (skipped for Admin role). FR-007/008
        // require non-admin users to have at least one membership when create
        // succeeds; if persistence of the rows fails after the user+role were
        // saved, roll back the user so we never leave behind a non-admin with
        // zero memberships.
        if (requestedGroupIds.Count > 0)
        {
            foreach (var gid in requestedGroupIds)
            {
                _dbContext.UserGroupMemberships.Add(new UserGroupMembership(user.Id, gid));
            }
            await _audit.WriteAsync(
                AdminAuditEvent.Record(
                    actorUserId,
                    AdminAuditEvent.ActionUserMembershipsUpdate,
                    AdminAuditEvent.TargetTypeUser,
                    user.Id,
                    JsonSerializer.Serialize(new { added = requestedGroupIds, removed = Array.Empty<int>() })),
                ct);
            try
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            catch
            {
                // Compensating action: undo the user creation so the partial state
                // (user with role but no memberships) cannot exist.
                try { await _userManager.DeleteAsync(user); } catch { /* best effort */ }
                throw;
            }
        }

        if (string.Equals(request.Role, ApplicantRole, StringComparison.Ordinal))
        {
            // Spec 032 — normalize the admin-assigned User Code (whitespace → null) so
            // the uniqueness comparison matches what the entity stores.
            var normalizedUserCode = string.IsNullOrWhiteSpace(request.UserCode) ? null : request.UserCode.Trim();
            var existingApplicant = await _dbContext.Applicants
                .FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
            if (existingApplicant is null)
            {
                if (await _dbContext.Applicants.AnyAsync(a => a.LegalId == request.LegalId, ct))
                {
                    await _userManager.DeleteAsync(user);
                    return Result<UserDetailDto>.Failure(
                        new DomainError("LEGAL_ID_IN_USE", nameof(CreateUserRequest.LegalId),
                            "Legal ID already in use by another applicant."));
                }
                // Spec 032 — User Code unique among assigned values (FR-009).
                if (normalizedUserCode is not null
                    && await _dbContext.Applicants.AnyAsync(a => a.UserCode == normalizedUserCode, ct))
                {
                    await _userManager.DeleteAsync(user);
                    return Result<UserDetailDto>.Failure(
                        new DomainError("USER_CODE_IN_USE", nameof(CreateUserRequest.UserCode),
                            "User code already in use by another applicant."));
                }
                _dbContext.Applicants.Add(new Applicant(
                    userId: user.Id,
                    legalId: request.LegalId!,
                    firstName: request.FirstName,
                    lastName: request.LastName,
                    email: request.Email,
                    phone: request.Phone,
                    performanceScore: null,
                    identificationType: request.IdentificationType,
                    userCode: normalizedUserCode));
            }
            else
            {
                existingApplicant.UpdateProfile(request.LegalId!, request.FirstName, request.LastName, request.Email, request.Phone, request.IdentificationType, normalizedUserCode);
            }
            await _dbContext.SaveChangesAsync(ct);
        }

        var detail = await MapToDetailAsync(user, ct);
        return Result<UserDetailDto>.Success(detail!);
    }

    public async Task<Result<UserDetailDto>> UpdateUserAsync(UpdateUserRequest request, string actorUserId, CancellationToken ct)
    {
        var target = await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (target is null)
        {
            return Result<UserDetailDto>.Failure(
                new DomainError("NOT_FOUND", null, "User not found."));
        }

        if (target.IsSystemSentinel)
        {
            throw new SentinelUserModificationException();
        }

        // Spec 016 — optimistic concurrency: reject the save when the posted
        // ConcurrencyStamp does not match the current row.
        if (!string.IsNullOrEmpty(request.ConcurrencyStamp)
            && !string.Equals(target.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return Result<UserDetailDto>.Failure(
                new DomainError("CONCURRENCY_CONFLICT", null,
                    "The user record was modified by another administrator. Please reload."));
        }

        var validation = ValidateRoleAndLegalId(request.Role, request.LegalId, isCreate: false);
        if (validation.Count > 0) return Result<UserDetailDto>.Failure(validation);

        // Spec 016 — wrap the user-row mutations + applicant upsert + membership
        // diff in a single explicit transaction so a partial failure on a later
        // SaveChanges cannot leave a half-applied edit (REVIEW-CODE F-3).
        // EF InMemory does not support relational transactions; guard via
        // Database.IsRelational(). Under SQL Server we go through
        // CreateExecutionStrategy().ExecuteAsync(...) so the configured retry
        // strategy can wrap retries around the user-initiated transaction.
        if (!_dbContext.Database.IsRelational())
        {
            return await UpdateUserCoreAsync(target, request, actorUserId, ct);
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
            var inner = await UpdateUserCoreAsync(target, request, actorUserId, ct);
            if (!inner.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return inner;
            }
            await tx.CommitAsync(ct);
            return inner;
        });
    }

    private async Task<Result<UserDetailDto>> UpdateUserCoreAsync(
        ApplicationUser target,
        UpdateUserRequest request,
        string actorUserId,
        CancellationToken ct)
    {
        // Spec 016 / FR-008: at least one group when resulting role is non-Admin.
        // FR-009: clear all memberships if resulting role is Admin (silently).
        var requestedGroupIds = NormalizeGroupIdsForRole(request.Role, request.GroupIds ?? Array.Empty<int>());
        if (RoleRequiresGroups(request.Role) && requestedGroupIds.Count == 0)
        {
            return Result<UserDetailDto>.Failure(
                new DomainError("AT_LEAST_ONE_GROUP", "GroupIds",
                    "At least one group is required for Applicant and Reviewer roles."));
        }
        if (requestedGroupIds.Count > 0)
        {
            var existingGroupIds = await _dbContext.Groups
                .Where(g => requestedGroupIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(ct);
            if (existingGroupIds.Count != requestedGroupIds.Count)
            {
                return Result<UserDetailDto>.Failure(
                    new DomainError("GROUP_NOT_FOUND", "GroupIds",
                        "One or more selected groups do not exist."));
            }
        }

        var currentRoles = await _userManager.GetRolesAsync(target);
        var currentRole = SelectPrimaryRole(currentRoles);
        var emailChanged = !string.Equals(target.Email, request.Email, StringComparison.OrdinalIgnoreCase);
        var roleChanged = !string.Equals(currentRole, request.Role, StringComparison.Ordinal);

        if (string.Equals(actorUserId, target.Id, StringComparison.Ordinal))
        {
            if (roleChanged)
            {
                throw new SelfModificationException(SelfModificationAction.ChangeOwnRole);
            }
            if (emailChanged)
            {
                throw new SelfModificationException(SelfModificationAction.ChangeOwnEmail);
            }
        }

        if (roleChanged
            && string.Equals(currentRole, AdminRole, StringComparison.Ordinal)
            && !string.Equals(request.Role, AdminRole, StringComparison.Ordinal))
        {
            var activeAdmins = await CountActiveNonSentinelAdminsAsync(ct);
            // The current target is an active admin counted above; demoting them subtracts 1.
            // Only block if the target is currently active (not disabled).
            var nowUtc = DateTimeOffset.UtcNow;
            var targetIsActive = target.LockoutEnd == null || target.LockoutEnd <= nowUtc;
            if (targetIsActive && activeAdmins - 1 <= 0)
            {
                throw new LastAdministratorException();
            }
        }

        target.FirstName = request.FirstName;
        target.LastName = request.LastName;
        target.PhoneNumber = request.Phone;

        if (emailChanged)
        {
            target.Email = request.Email;
            target.NormalizedEmail = request.Email.ToUpperInvariant();
            target.UserName = request.Email;
            target.NormalizedUserName = request.Email.ToUpperInvariant();
        }

        var update = await _userManager.UpdateAsync(target);
        if (!update.Succeeded)
        {
            return Result<UserDetailDto>.Failure(MapIdentityErrors(update.Errors));
        }

        if (emailChanged || roleChanged)
        {
            await _userManager.UpdateSecurityStampAsync(target);
        }

        if (roleChanged)
        {
            if (currentRoles.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(target, currentRoles);
                if (!removeResult.Succeeded)
                {
                    return Result<UserDetailDto>.Failure(MapIdentityErrors(removeResult.Errors));
                }
            }
            var addResult = await _userManager.AddToRoleAsync(target, request.Role);
            if (!addResult.Succeeded)
            {
                return Result<UserDetailDto>.Failure(MapIdentityErrors(addResult.Errors));
            }
        }

        if (string.Equals(request.Role, ApplicantRole, StringComparison.Ordinal))
        {
            // Spec 032 — normalize the User Code (whitespace → null) for the uniqueness compare.
            var normalizedUserCode = string.IsNullOrWhiteSpace(request.UserCode) ? null : request.UserCode.Trim();
            var applicant = await _dbContext.Applicants.FirstOrDefaultAsync(a => a.UserId == target.Id, ct);
            if (applicant is null)
            {
                if (await _dbContext.Applicants.AnyAsync(a => a.LegalId == request.LegalId, ct))
                {
                    return Result<UserDetailDto>.Failure(
                        new DomainError("LEGAL_ID_IN_USE", nameof(UpdateUserRequest.LegalId),
                            "Legal ID already in use by another applicant."));
                }
                // Spec 032 — User Code unique among assigned values (FR-009).
                if (normalizedUserCode is not null
                    && await _dbContext.Applicants.AnyAsync(a => a.UserCode == normalizedUserCode, ct))
                {
                    return Result<UserDetailDto>.Failure(
                        new DomainError("USER_CODE_IN_USE", nameof(UpdateUserRequest.UserCode),
                            "User code already in use by another applicant."));
                }
                _dbContext.Applicants.Add(new Applicant(
                    userId: target.Id,
                    legalId: request.LegalId!,
                    firstName: request.FirstName,
                    lastName: request.LastName,
                    email: request.Email,
                    phone: request.Phone,
                    performanceScore: null,
                    identificationType: request.IdentificationType,
                    userCode: normalizedUserCode));
            }
            else
            {
                // Compare against the canonical form so a re-typed hyphenation variant
                // does not register as a change (spec 026 canonical legal ID).
                var canonicalNew = request.IdentificationType is { } t && !string.IsNullOrWhiteSpace(request.LegalId)
                    ? Domain.ValueObjects.Identification.From(t, request.LegalId).Value
                    : request.LegalId;
                if (!string.Equals(applicant.LegalId, canonicalNew, StringComparison.Ordinal)
                    && await _dbContext.Applicants.AnyAsync(a => a.LegalId == canonicalNew && a.UserId != target.Id, ct))
                {
                    return Result<UserDetailDto>.Failure(
                        new DomainError("LEGAL_ID_IN_USE", nameof(UpdateUserRequest.LegalId),
                            "Legal ID already in use by another applicant."));
                }
                // Spec 032 — block only when the code actually changes to one another applicant owns.
                if (normalizedUserCode is not null
                    && !string.Equals(applicant.UserCode, normalizedUserCode, StringComparison.Ordinal)
                    && await _dbContext.Applicants.AnyAsync(a => a.UserCode == normalizedUserCode && a.UserId != target.Id, ct))
                {
                    return Result<UserDetailDto>.Failure(
                        new DomainError("USER_CODE_IN_USE", nameof(UpdateUserRequest.UserCode),
                            "User code already in use by another applicant."));
                }
                applicant.UpdateProfile(request.LegalId!, request.FirstName, request.LastName, request.Email, request.Phone, request.IdentificationType, normalizedUserCode);
            }
            await _dbContext.SaveChangesAsync(ct);
        }

        // Spec 016 — apply group-membership diff (FR-008..FR-010).
        await ApplyMembershipDiffAsync(target, requestedGroupIds, actorUserId, ct);

        var detail = await MapToDetailAsync(target, ct);
        return Result<UserDetailDto>.Success(detail!);
    }

    public async Task<Result> DisableUserAsync(string targetUserId, string actorUserId, CancellationToken ct)
    {
        var target = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target is null)
        {
            return Result.Failure(new DomainError("NOT_FOUND", null, "User not found."));
        }
        if (target.IsSystemSentinel)
        {
            throw new SentinelUserModificationException();
        }

        if (string.Equals(actorUserId, target.Id, StringComparison.Ordinal))
        {
            throw new SelfModificationException(SelfModificationAction.DisableSelf);
        }

        var roles = await _userManager.GetRolesAsync(target);
        if (roles.Contains(AdminRole))
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var targetIsActive = target.LockoutEnd == null || target.LockoutEnd <= nowUtc;
            if (targetIsActive)
            {
                var activeAdmins = await CountActiveNonSentinelAdminsAsync(ct);
                if (activeAdmins - 1 <= 0)
                {
                    throw new LastAdministratorException();
                }
            }
        }

        await _userManager.SetLockoutEnabledAsync(target, true);
        await _userManager.SetLockoutEndDateAsync(target, DateTimeOffset.MaxValue);
        await _userManager.UpdateSecurityStampAsync(target);
        return Result.Success();
    }

    public async Task<Result> EnableUserAsync(string targetUserId, string actorUserId, CancellationToken ct)
    {
        var target = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target is null)
        {
            return Result.Failure(new DomainError("NOT_FOUND", null, "User not found."));
        }
        if (target.IsSystemSentinel)
        {
            throw new SentinelUserModificationException();
        }

        await _userManager.SetLockoutEndDateAsync(target, null);
        return Result.Success();
    }

    public async Task<Result> ResetUserPasswordAsync(ResetPasswordRequest request, string actorUserId, CancellationToken ct)
    {
        var target = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (target is null)
        {
            return Result.Failure(new DomainError("NOT_FOUND", null, "User not found."));
        }
        if (target.IsSystemSentinel)
        {
            throw new SentinelUserModificationException();
        }

        if (string.Equals(actorUserId, target.Id, StringComparison.Ordinal))
        {
            throw new SelfModificationException(SelfModificationAction.ResetOwnPassword);
        }

        // Validate the new password BEFORE removing the old one. RemovePassword
        // followed by a rejected AddPassword would leave the user with no
        // password at all — locked out by a failed reset. Validate first so a
        // weak password aborts the operation with the old password intact.
        foreach (var validator in _userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(
                _userManager, target, request.NewTemporaryPassword);
            if (!validation.Succeeded)
            {
                return Result.Failure(MapIdentityErrors(validation.Errors));
            }
        }

        var hasPassword = await _userManager.HasPasswordAsync(target);
        if (hasPassword)
        {
            var remove = await _userManager.RemovePasswordAsync(target);
            if (!remove.Succeeded)
            {
                return Result.Failure(MapIdentityErrors(remove.Errors));
            }
        }
        var add = await _userManager.AddPasswordAsync(target, request.NewTemporaryPassword);
        if (!add.Succeeded)
        {
            return Result.Failure(MapIdentityErrors(add.Errors));
        }

        target.MustChangePassword = true;
        await _userManager.UpdateAsync(target);
        await _userManager.UpdateSecurityStampAsync(target);
        return Result.Success();
    }

    private async Task<UserDetailDto?> MapToDetailAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var role = SelectPrimaryRole(roles);
        string? legalId = null;
        Domain.Enums.IdentificationType? identificationType = null;
        string? userCode = null;
        if (string.Equals(role, ApplicantRole, StringComparison.Ordinal))
        {
            var applicantRow = await _dbContext.Applicants
                .Where(a => a.UserId == user.Id)
                .Select(a => new { a.LegalId, a.IdentificationType, a.UserCode })
                .FirstOrDefaultAsync(ct);
            legalId = applicantRow?.LegalId;
            identificationType = applicantRow?.IdentificationType;
            userCode = applicantRow?.UserCode;
        }
        var status = IsDisabled(user, DateTimeOffset.UtcNow) ? "Disabled" : "Active";
        // Spec 016 — surface current memberships so the edit form pre-selects them.
        var groupIds = await _dbContext.UserGroupMemberships
            .Where(m => m.UserId == user.Id)
            .Select(m => m.GroupId)
            .OrderBy(g => g)
            .ToListAsync(ct);
        return new UserDetailDto(
            Id: user.Id,
            FirstName: user.FirstName ?? "",
            LastName: user.LastName ?? "",
            Email: user.Email ?? "",
            Phone: user.PhoneNumber,
            Role: role,
            Status: status,
            LegalId: legalId,
            MustChangePassword: user.MustChangePassword,
            GroupIds: groupIds,
            ConcurrencyStamp: user.ConcurrencyStamp,
            IdentificationType: identificationType,
            UserCode: userCode);
    }

    /// <summary>
    /// Spec 016 / FR-008..FR-010 — apply the (added, removed) diff for a user's
    /// group memberships, write a single audit row when there is a real change,
    /// and persist atomically.
    /// </summary>
    private async Task ApplyMembershipDiffAsync(
        ApplicationUser user,
        IReadOnlyList<int> requestedGroupIds,
        string actorUserId,
        CancellationToken ct)
    {
        var currentRows = await _dbContext.UserGroupMemberships
            .Where(m => m.UserId == user.Id)
            .ToListAsync(ct);
        var current = currentRows.Select(r => r.GroupId).ToHashSet();
        var requested = requestedGroupIds.ToHashSet();

        var added = requested.Except(current).OrderBy(x => x).ToList();
        var removed = current.Except(requested).OrderBy(x => x).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            // No-op — do not write an audit row (per contracts/admin-users-form.md).
            return;
        }

        if (removed.Count > 0)
        {
            var rowsToRemove = currentRows.Where(r => removed.Contains(r.GroupId)).ToList();
            _dbContext.UserGroupMemberships.RemoveRange(rowsToRemove);
        }
        foreach (var gid in added)
        {
            _dbContext.UserGroupMemberships.Add(new UserGroupMembership(user.Id, gid));
        }

        await _audit.WriteAsync(
            AdminAuditEvent.Record(
                actorUserId,
                AdminAuditEvent.ActionUserMembershipsUpdate,
                AdminAuditEvent.TargetTypeUser,
                user.Id,
                JsonSerializer.Serialize(new { added, removed })),
            ct);

        await _dbContext.SaveChangesAsync(ct);
    }

    private static bool RoleRequiresGroups(string? role) =>
        string.Equals(role, ApplicantRole, StringComparison.Ordinal)
        || string.Equals(role, ReviewerRole, StringComparison.Ordinal);

    /// <summary>
    /// Spec 016 / FR-009 — Admin role MUST never carry memberships.
    /// Spec 021 / FR-007 — SupplierAdmin is global-scope (no Process/Group); same
    /// strip-on-write rule applies so the membership table can never accumulate
    /// orphan rows for these two roles.
    /// </summary>
    private static IReadOnlyList<int> NormalizeGroupIdsForRole(string? role, IReadOnlyList<int> ids)
    {
        if (string.Equals(role, AdminRole, StringComparison.Ordinal)
            || string.Equals(role, SupplierAdminRole, StringComparison.Ordinal))
        {
            return Array.Empty<int>();
        }
        return ids.Distinct().OrderBy(x => x).ToList();
    }

    private static IReadOnlyList<DomainError> ValidateRoleAndLegalId(string? role, string? legalId, bool isCreate)
    {
        var errors = new List<DomainError>();
        if (string.IsNullOrWhiteSpace(role) || !AllowedRoles.Contains(role))
        {
            errors.Add(new DomainError("INVALID_INPUT", "Role", "Role must be Applicant, Reviewer, SupplierAdmin, or Admin."));
            return errors;
        }
        if (string.Equals(role, ApplicantRole, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(legalId))
        {
            errors.Add(new DomainError("INVALID_INPUT", "LegalId", "Legal ID is required for Applicant role."));
        }
        return errors;
    }

    private static IReadOnlyList<DomainError> MapIdentityErrors(IEnumerable<IdentityError> errors)
    {
        return errors.Select(e =>
        {
            var code = e.Code ?? "";
            if (code.StartsWith("Duplicate", StringComparison.Ordinal))
            {
                return new DomainError("EMAIL_IN_USE", code.Contains("Email") ? "Email" : null, e.Description);
            }
            if (code.StartsWith("Password", StringComparison.Ordinal))
            {
                // No field name — the infrastructure layer cannot know which
                // form field the caller used (Create-user form: "InitialPassword";
                // admin reset form: "NewTemporaryPassword"). A wrong key renders
                // nowhere and silently swallows the error. Null routes it to the
                // model-level validation summary, which every caller's form has.
                return new DomainError("WEAK_PASSWORD", null, e.Description);
            }
            return new DomainError("INVALID_INPUT", null, e.Description);
        }).ToList();
    }

    private static bool IsDisabled(ApplicationUser user, DateTimeOffset nowUtc) =>
        user.LockoutEnd != null && user.LockoutEnd > nowUtc;

    // FR-001 says one role per user, but the existing /Account/Register + /Account/AssignRole
    // flow assigns multiple (Applicant + Reviewer/Admin). The admin area surfaces ONE role,
    // picked Admin > Reviewer > SupplierAdmin > Applicant — matching the priority list in
    // AccountController.BuildProfileViewModelAsync so a dual-role user reads the same way
    // on both the admin Users list and their own profile screen (spec 021 / FR-007).
    private static string SelectPrimaryRole(IEnumerable<string> roles)
    {
        var set = roles.ToHashSet(StringComparer.Ordinal);
        if (set.Contains(AdminRole)) return AdminRole;
        if (set.Contains(ReviewerRole)) return ReviewerRole;
        if (set.Contains(SupplierAdminRole)) return SupplierAdminRole;
        if (set.Contains(ApplicantRole)) return ApplicantRole;
        return set.FirstOrDefault() ?? "";
    }

    private async Task<int> CountActiveNonSentinelAdminsAsync(CancellationToken ct)
    {
        var adminRoleId = await _dbContext.Roles
            .Where(r => r.Name == AdminRole)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (adminRoleId is null) return 0;

        var nowUtc = DateTimeOffset.UtcNow;
        return await _dbContext.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.RoleId == adminRoleId)
            .Join(_dbContext.Users.IgnoreQueryFilters(),
                ur => ur.UserId,
                u => u.Id,
                (ur, u) => u)
            .CountAsync(u => !u.IsSystemSentinel && (u.LockoutEnd == null || u.LockoutEnd <= nowUtc), ct);
    }

    private static string ComposeFullName(ApplicationUser user)
    {
        var first = user.FirstName ?? "";
        var last = user.LastName ?? "";
        var full = $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(full) ? (user.Email ?? user.Id) : full;
    }
}
