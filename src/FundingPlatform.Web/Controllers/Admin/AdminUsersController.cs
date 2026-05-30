using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers.Admin;

[Authorize(Roles = "Admin,SupplierAdmin")]
[SupplierAdminDenied]
[Route("Admin/Users")]
public class AdminUsersController : Controller
{
    private readonly IUserAdministrationService _service;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IGroupService _groups;
    private readonly AppDbContext _db;

    public AdminUsersController(
        IUserAdministrationService service,
        UserManager<ApplicationUser> userManager,
        IGroupService groups,
        AppDbContext db)
    {
        _service = service;
        _userManager = userManager;
        _groups = groups;
        _db = db;
    }

    /// <summary>Spec 021 / FR-034 / T082 — loads the Process → Groups catalog
    /// the cascading filter widget on the Index view consumes.</summary>
    private async Task<IReadOnlyList<AdminUsersProcessFilterOption>> LoadProcessFilterCatalogAsync(CancellationToken ct)
    {
        // Two small round-trips: every Process, and every Group. The catalog is
        // small (a handful of Processes × ~3-10 Groups each) so no pagination.
        // FR-034 — the filter MUST list every Process, including ones with zero
        // groups (a freshly-created Process has none until groups are attached).
        // An inner join on Groups would hide those, so we left-join in memory.
        var processes = await _db.Processes.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        var groups = await _db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new { g.ProcessId, g.Id, g.Name })
            .ToListAsync(ct);

        return processes
            .Select(p => new AdminUsersProcessFilterOption(
                p.Id,
                p.Name,
                groups
                    .Where(g => g.ProcessId == p.Id)
                    .Select(g => new AdminUsersGroupFilterOption(g.Id, g.Name))
                    .ToList()))
            .ToList();
    }

    private async Task<IReadOnlyList<AdminUserGroupOption>> LoadGroupOptionsAsync(CancellationToken ct)
    {
        var rows = await _groups.ListAsync(ct);
        return rows.Select(r => new AdminUserGroupOption(r.Id, r.Name)).ToList();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? roleFilter,
        string? statusFilter,
        string? search,
        int? processFilter,
        int? groupFilter,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var actorId = _userManager.GetUserId(User);
        var result = await _service.ListUsersAsync(
            new ListUsersRequest(roleFilter, statusFilter, search, page, pageSize), ct);

        var rows = result.Items.Select(i => new AdminUserSummaryRowViewModel
        {
            Id = i.Id,
            FullName = i.FullName,
            Email = i.Email,
            Role = i.Role,
            Status = i.Status,
            CreatedAt = i.CreatedAt,
            IsSelf = string.Equals(i.Id, actorId, StringComparison.Ordinal),
        }).ToList();

        // Spec 021 / FR-034 — apply the Process → Group cascade filter on top of
        // the existing search/role/status filters. Performed in-memory against
        // the materialized row set to keep the existing ListUsersAsync surface
        // untouched (US1 scope); a future P2/P3 pass can push this into the
        // service-layer query for paging fidelity. We resolve the user-id set
        // for the chosen filter and intersect with the displayed rows.
        if (processFilter is { } pid || groupFilter is { } gid)
        {
            var memberQuery =
                from m in _db.UserGroupMemberships.AsNoTracking()
                join g in _db.Groups.AsNoTracking() on m.GroupId equals g.Id
                where (!groupFilter.HasValue || m.GroupId == groupFilter.Value)
                   && (!processFilter.HasValue || g.ProcessId == processFilter.Value)
                select m.UserId;
            var allowedUserIds = await memberQuery.ToHashSetAsync(ct);
            rows = rows.Where(r => allowedUserIds.Contains(r.Id)).ToList();
        }

        var vm = new AdminUsersListViewModel
        {
            Rows = rows,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            RoleFilter = roleFilter,
            StatusFilter = statusFilter,
            Search = search,
            Processes = await LoadProcessFilterCatalogAsync(ct),
            ProcessFilter = processFilter,
            GroupFilter = groupFilter,
        };
        return View(vm);
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        return View(new AdminUserCreateViewModel
        {
            AvailableGroups = await LoadGroupOptionsAsync(ct),
        });
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminUserCreateViewModel vm, CancellationToken ct)
    {
        if (string.Equals(vm.Role, "Applicant", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(vm.LegalId))
        {
            ModelState.AddModelError(nameof(vm.LegalId), "La identificación es obligatoria para el rol Solicitante.");
        }
        // Spec 026 — type required (with the value) when Role=Applicant.
        if (string.Equals(vm.Role, "Applicant", StringComparison.Ordinal) && vm.IdentificationType is null)
        {
            ModelState.AddModelError(nameof(vm.IdentificationType), "Seleccione el tipo de identificación.");
        }
        // Spec 016 / FR-007 — required-group check at the controller boundary.
        // Admin role bypasses this (FR-009).
        // Spec 021 / FR-007 — SupplierAdmin is global-scope (no Process/Group),
        // same bypass: without this, an invisible ModelState error on the
        // hidden GroupIds field renders "Corrige los campos marcados" with no
        // visible field marker because the group selector is JS-hidden.
        if (!IsGrouplessRole(vm.Role)
            && (vm.GroupIds is null || vm.GroupIds.Length == 0))
        {
            ModelState.AddModelError(nameof(vm.GroupIds), AdminUsersResources.AtLeastOneGroupRequired);
        }
        if (!ModelState.IsValid)
        {
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            var result = await _service.CreateUserAsync(
                new CreateUserRequest(
                    vm.FirstName, vm.LastName, vm.Email, vm.Phone, vm.Role,
                    vm.InitialPassword, vm.LegalId,
                    GroupIds: vm.GroupIds ?? Array.Empty<int>(),
                    IdentificationType: vm.IdentificationType),
                actorId, ct);
            if (!result.Succeeded)
            {
                AddDomainErrors(result.Errors);
                vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
                return View(vm);
            }
            TempData["SuccessMessage"] = $"Usuario '{vm.Email}' creado.";
            return RedirectToAction(nameof(Index));
        }
        catch (SentinelUserModificationException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.SentinelImmutable);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }
        catch (LastAdministratorException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.LastAdminProtected);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }
        catch (SelfModificationException ex)
        {
            ModelState.AddModelError(string.Empty, ResolveSelfMessage(ex.Action));
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }
    }

    [HttpGet("{id}/Edit")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var detail = await _service.GetUserAsync(id, ct);
        if (detail is null) return NotFound();
        var vm = new AdminUserEditViewModel
        {
            UserId = detail.Id,
            FirstName = detail.FirstName,
            LastName = detail.LastName,
            Email = detail.Email,
            Phone = detail.Phone,
            Role = detail.Role,
            LegalId = detail.LegalId,
            IdentificationType = detail.IdentificationType,
            GroupIds = detail.GroupIds.ToArray(),
            ConcurrencyStamp = detail.ConcurrencyStamp,
            AvailableGroups = await LoadGroupOptionsAsync(ct),
        };
        return View(vm);
    }

    [HttpPost("{id}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, AdminUserEditViewModel vm, CancellationToken ct)
    {
        if (!string.Equals(id, vm.UserId, StringComparison.Ordinal))
        {
            return BadRequest();
        }
        if (string.Equals(vm.Role, "Applicant", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(vm.LegalId))
        {
            ModelState.AddModelError(nameof(vm.LegalId), "La identificación es obligatoria para el rol Solicitante.");
        }
        // Spec 026 — type required (with the value) when Role=Applicant.
        if (string.Equals(vm.Role, "Applicant", StringComparison.Ordinal) && vm.IdentificationType is null)
        {
            ModelState.AddModelError(nameof(vm.IdentificationType), "Seleccione el tipo de identificación.");
        }
        // Spec 016 / FR-008 — required-group check at the controller boundary.
        // Spec 021 / FR-007 — SupplierAdmin bypasses too (same rationale as Create).
        if (!IsGrouplessRole(vm.Role)
            && (vm.GroupIds is null || vm.GroupIds.Length == 0))
        {
            ModelState.AddModelError(nameof(vm.GroupIds), AdminUsersResources.AtLeastOneGroupRequired);
        }
        if (!ModelState.IsValid)
        {
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            var result = await _service.UpdateUserAsync(
                new UpdateUserRequest(
                    vm.UserId, vm.FirstName, vm.LastName, vm.Email, vm.Phone, vm.Role, vm.LegalId,
                    GroupIds: vm.GroupIds ?? Array.Empty<int>(),
                    ConcurrencyStamp: vm.ConcurrencyStamp,
                    IdentificationType: vm.IdentificationType),
                actorId, ct);
            if (!result.Succeeded)
            {
                AddDomainErrors(result.Errors, mapping: (code, _) => code switch
                {
                    "CONCURRENCY_CONFLICT" => AdminUsersResources.ConcurrencyConflict,
                    "GROUP_NOT_FOUND" => AdminUsersResources.GroupNotFound,
                    "AT_LEAST_ONE_GROUP" => AdminUsersResources.AtLeastOneGroupRequired,
                    _ => null,
                });
                vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
                // Refresh the concurrency stamp so the user can re-submit cleanly.
                if (result.Errors.Any(e => e.Code == "CONCURRENCY_CONFLICT"))
                {
                    var fresh = await _service.GetUserAsync(vm.UserId, ct);
                    if (fresh is not null)
                    {
                        vm.ConcurrencyStamp = fresh.ConcurrencyStamp;
                    }
                }
                return View(vm);
            }
            TempData["SuccessMessage"] = $"Usuario '{vm.Email}' actualizado.";
            return RedirectToAction(nameof(Index));
        }
        catch (SentinelUserModificationException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.SentinelImmutable);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }
        catch (LastAdministratorException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.LastAdminProtected);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }
        catch (SelfModificationException ex)
        {
            ModelState.AddModelError(string.Empty, ResolveSelfMessage(ex.Action));
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            return View(vm);
        }
    }

    [HttpPost("{id}/Disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(string id, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            var result = await _service.DisableUserAsync(id, actorId, ct);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Errors.FirstOrDefault()?.Message ?? "No se pudo inhabilitar al usuario.";
            }
            else
            {
                TempData["SuccessMessage"] = "Usuario inhabilitado.";
            }
        }
        catch (SentinelUserModificationException)
        {
            TempData["ErrorMessage"] = AdminErrorMessages.SentinelImmutable;
        }
        catch (LastAdministratorException)
        {
            TempData["ErrorMessage"] = AdminErrorMessages.LastAdminProtected;
        }
        catch (SelfModificationException ex)
        {
            TempData["ErrorMessage"] = ResolveSelfMessage(ex.Action);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/Enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(string id, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            var result = await _service.EnableUserAsync(id, actorId, ct);
            TempData["SuccessMessage"] = result.Succeeded
                ? "Usuario habilitado."
                : (result.Errors.FirstOrDefault()?.Message ?? "No se pudo habilitar al usuario.");
        }
        catch (SentinelUserModificationException)
        {
            TempData["ErrorMessage"] = AdminErrorMessages.SentinelImmutable;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/ResetPassword")]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct)
    {
        var detail = await _service.GetUserAsync(id, ct);
        if (detail is null) return NotFound();
        var vm = new AdminUserResetPasswordViewModel
        {
            UserId = detail.Id,
            TargetEmail = detail.Email,
        };
        return View(vm);
    }

    [HttpPost("{id}/ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, AdminUserResetPasswordViewModel vm, CancellationToken ct)
    {
        if (!string.Equals(id, vm.UserId, StringComparison.Ordinal))
        {
            return BadRequest();
        }
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            var result = await _service.ResetUserPasswordAsync(
                new ResetPasswordRequest(vm.UserId, vm.NewTemporaryPassword), actorId, ct);
            if (!result.Succeeded)
            {
                AddDomainErrors(result.Errors);
                return View(vm);
            }
            TempData["SuccessMessage"] = "Contraseña restablecida. El usuario debe cambiarla al iniciar sesión.";
            return RedirectToAction(nameof(Index));
        }
        catch (SentinelUserModificationException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.SentinelImmutable);
            return View(vm);
        }
        catch (SelfModificationException ex)
        {
            ModelState.AddModelError(string.Empty, ResolveSelfMessage(ex.Action));
            return View(vm);
        }
    }

    private void AddDomainErrors(IReadOnlyList<DomainError> errors, Func<string, string?, string?>? mapping = null)
    {
        foreach (var err in errors)
        {
            var key = err.Field ?? string.Empty;
            var translated = mapping?.Invoke(err.Code, err.Field) ?? err.Message;
            ModelState.AddModelError(key, translated);
        }
    }

    /// <summary>
    /// Spec 016 / FR-009 + spec 021 / FR-007 — roles that MUST NOT carry Process/Group
    /// memberships. The controller mirrors the service-layer check
    /// (<c>UserAdministrationService.RoleRequiresGroups</c>) so the form's
    /// required-group ModelState error is suppressed for groupless roles —
    /// without this, the hidden group selector accumulates an invisible error
    /// and the form summary shows "Corrige los campos marcados" with no
    /// visible field marker.
    /// </summary>
    private static bool IsGrouplessRole(string? role)
        => string.Equals(role, "Admin", StringComparison.Ordinal)
        || string.Equals(role, "SupplierAdmin", StringComparison.Ordinal);

    private static string ResolveSelfMessage(SelfModificationAction action) => action switch
    {
        SelfModificationAction.DisableSelf => AdminErrorMessages.SelfDisable,
        SelfModificationAction.ChangeOwnRole => AdminErrorMessages.SelfChangeRole,
        SelfModificationAction.ChangeOwnEmail => AdminErrorMessages.SelfChangeEmail,
        SelfModificationAction.ResetOwnPassword => AdminErrorMessages.SelfResetPassword,
        _ => AdminErrorMessages.SelfDisable,
    };
}
