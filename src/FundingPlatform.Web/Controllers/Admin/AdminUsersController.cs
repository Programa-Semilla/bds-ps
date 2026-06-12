using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Identity;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Controllers;
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
    private readonly Application.Admin.Filters.IFundHierarchyProvider _fundHierarchy;
    private readonly IIssuePasswordResetTokenHandler _issueInvite;
    private readonly IEmailSender _emailSender;
    private readonly InvitationEmailFactory _invitationEmailFactory;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IUserAdministrationService service,
        UserManager<ApplicationUser> userManager,
        IGroupService groups,
        AppDbContext db,
        Application.Admin.Filters.IFundHierarchyProvider fundHierarchy,
        IIssuePasswordResetTokenHandler issueInvite,
        IEmailSender emailSender,
        InvitationEmailFactory invitationEmailFactory,
        ILogger<AdminUsersController> logger)
    {
        _service = service;
        _userManager = userManager;
        _groups = groups;
        _db = db;
        _fundHierarchy = fundHierarchy;
        _issueInvite = issueInvite;
        _emailSender = emailSender;
        _invitationEmailFactory = invitationEmailFactory;
        _logger = logger;
    }

    /// <summary>
    /// Spec 033 / FR-001 / FR-007 / C4 — issues a fresh 72h single-use
    /// set-password invitation for <paramref name="email"/> (superseding any
    /// prior unused link), composes the absolute <c>/Account/ResetPassword</c>
    /// link, and best-effort sends the es-CR invitation email. Returns the raw
    /// link so the caller can render the FR-008 admin-visible fallback, or
    /// <c>null</c> when the user could not be resolved / the link could not be
    /// composed. Email transport failures are swallowed (logged) — the
    /// admin-visible link is the resilience mechanism, not delivery retry (D5).
    /// </summary>
    private async Task<string?> IssueAndSendInvitationAsync(string email, CancellationToken ct)
    {
        var result = await _issueInvite.HandleAsync(
            new IssuePasswordResetTokenCommand(
                email,
                Ttl: PasswordResetToken.InvitationLifetime,
                InvalidatePriorUnused: true),
            ct);

        if (!result.UserFound || string.IsNullOrEmpty(result.RawToken) || string.IsNullOrEmpty(result.UserId))
        {
            return null;
        }

        var inviteLink = Url.Action(
            action: nameof(AccountController.ResetPassword),
            controller: "Account",
            values: new { userId = result.UserId, token = result.RawToken },
            protocol: Request.Scheme,
            host: Request.Host.Value);

        if (string.IsNullOrEmpty(inviteLink))
        {
            return null;
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(PasswordResetToken.InvitationLifetime);
        var envelope = _invitationEmailFactory.Build(
            toAddress: result.Email!,
            firstName: result.FirstName,
            inviteLink: inviteLink,
            expiresAt: expiresAt);
        try
        {
            await _emailSender.SendAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            // D5 — best-effort delivery; the admin-visible copyable link (FR-008)
            // is the fallback, so a transport failure must not block onboarding.
            _logger.LogWarning(ex,
                "Failed to send set-password invitation email to {Email}; the admin-visible link remains the onboarding fallback.",
                email);
        }

        return inviteLink;
    }

    private async Task<IReadOnlyList<AdminUserGroupOption>> LoadGroupOptionsAsync(CancellationToken ct)
    {
        var rows = await _groups.ListAsync(ct);
        return rows.Select(r => new AdminUserGroupOption(r.Id, r.Name)).ToList();
    }

    /// <summary>
    /// Builds the Fondo → Proceso → Grupo drill-down catalog for the user-form
    /// group selector. Active Funds only (FR — archived Funds are excluded from
    /// the picker, mirroring spec 029's freeze philosophy). Three small
    /// round-trips assembled in memory so a Fund/Process with zero children still
    /// appears (an inner join would hide those). Any existing membership in an
    /// archived Fund is preserved separately by <see cref="LoadGroupOptionsAsync"/>
    /// (which is Fund-status-agnostic), so the selected chips never silently drop.
    /// </summary>
    private async Task<IReadOnlyList<AdminUserFundCatalogOption>> LoadFundCatalogAsync(CancellationToken ct)
    {
        var funds = await _db.Funds.AsNoTracking()
            .Where(f => f.Status == FundStatus.Active)
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var processes = await _db.Processes.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.FundId })
            .ToListAsync(ct);

        var groups = await _db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.ProcessId })
            .ToListAsync(ct);

        return funds
            .Select(f => new AdminUserFundCatalogOption(
                f.Id,
                f.Name,
                processes
                    .Where(p => p.FundId == f.Id)
                    .Select(p => new AdminUserFundProcessOption(
                        p.Id,
                        p.Name,
                        groups
                            .Where(g => g.ProcessId == p.Id)
                            .Select(g => new AdminUserGroupOption(g.Id, g.Name))
                            .ToList()))
                    .ToList()))
            .ToList();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? roleFilter,
        string? statusFilter,
        string? search,
        int? fundFilter,
        int? processFilter,
        int? groupFilter,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // The status filter defaults to "Active" on first load (statusFilter
        // absent from the query string → null). An explicit empty value
        // (?statusFilter=) is the "Todos los estados" choice and is honored.
        var effectiveStatus = statusFilter ?? "Active";

        var actorId = _userManager.GetUserId(User);
        var result = await _service.ListUsersAsync(
            new ListUsersRequest(roleFilter, effectiveStatus, search, page, pageSize), ct);

        var rows = result.Items.Select(i => new AdminUserSummaryRowViewModel
        {
            Id = i.Id,
            FullName = i.FullName,
            Email = i.Email,
            Role = i.Role,
            Status = i.Status,
            CreatedAt = i.CreatedAt,
            IsSelf = string.Equals(i.Id, actorId, StringComparison.Ordinal),
            UserCode = i.UserCode,
        }).ToList();

        // Spec 021 / FR-034 — apply the Process → Group cascade filter on top of
        // the existing search/role/status filters. Performed in-memory against
        // the materialized row set to keep the existing ListUsersAsync surface
        // untouched (US1 scope); a future P2/P3 pass can push this into the
        // service-layer query for paging fidelity. We resolve the user-id set
        // for the chosen filter and intersect with the displayed rows.
        if (fundFilter is { } || processFilter is { } || groupFilter is { })
        {
            var memberQuery =
                from m in _db.UserGroupMemberships.AsNoTracking()
                join g in _db.Groups.AsNoTracking() on m.GroupId equals g.Id
                join p in _db.Processes.AsNoTracking() on g.ProcessId equals p.Id
                where (!groupFilter.HasValue || m.GroupId == groupFilter.Value)
                   && (!processFilter.HasValue || g.ProcessId == processFilter.Value)
                   && (!fundFilter.HasValue || p.FundId == fundFilter.Value)
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
            StatusFilter = effectiveStatus,
            Search = search,
            // Active Funds only on the assignment-adjacent Users filter (exclude archived).
            FundHierarchy = await _fundHierarchy.GetAsync(includeArchived: false, ct),
            FundFilter = fundFilter,
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
            FundCatalog = await LoadFundCatalogAsync(ct),
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
        // Spec 032 — User Code required for Solicitante (not asked for other roles).
        if (string.Equals(vm.Role, "Applicant", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(vm.UserCode))
        {
            ModelState.AddModelError(nameof(vm.UserCode), AdminUsersResources.UserCodeRequired);
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
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            var result = await _service.CreateUserAsync(
                new CreateUserRequest(
                    vm.FirstName, vm.LastName, vm.Email, vm.Phone, vm.Role,
                    vm.LegalId,
                    GroupIds: vm.GroupIds ?? Array.Empty<int>(),
                    IdentificationType: vm.IdentificationType,
                    UserCode: vm.UserCode),
                actorId, ct);
            if (!result.Succeeded)
            {
                // Spec 032 — surface USER_CODE_IN_USE in es-CR on the UserCode field.
                AddDomainErrors(result.Errors, mapping: (code, _) =>
                    code == "USER_CODE_IN_USE" ? AdminUsersResources.UserCodeInUse : null);
                vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
                return View(vm);
            }

            // Spec 033 / FR-001 / FR-008 — the account was created with no password.
            // Issue + send the set-password invitation and render the confirmation
            // with the copyable admin-visible link (the onboarding-resilience fallback).
            var inviteLink = await IssueAndSendInvitationAsync(vm.Email, ct);
            if (inviteLink is null)
            {
                // Defensive: the user was just created so this is highly unlikely.
                // Fall back to the list with a generic success rather than 500.
                TempData["SuccessMessage"] = $"Usuario '{vm.Email}' creado.";
                return RedirectToAction(nameof(Index));
            }
            return View("InvitationSent", new AdminUserInvitationSentViewModel(vm.Email, inviteLink));
        }
        catch (DbUpdateException ex) when (ex.GetBaseException().Message.Contains("UX_Applicants_UserCode"))
        {
            // Spec 032 — concurrency backstop: a racing duplicate slips past the
            // service pre-check and trips the filtered unique index.
            ModelState.AddModelError(nameof(vm.UserCode), AdminUsersResources.UserCodeInUse);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }
        catch (SentinelUserModificationException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.SentinelImmutable);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }
        catch (LastAdministratorException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.LastAdminProtected);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }
        catch (SelfModificationException ex)
        {
            ModelState.AddModelError(string.Empty, ResolveSelfMessage(ex.Action));
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
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
            UserCode = detail.UserCode,
            GroupIds = detail.GroupIds.ToArray(),
            ConcurrencyStamp = detail.ConcurrencyStamp,
            AvailableGroups = await LoadGroupOptionsAsync(ct),
            FundCatalog = await LoadFundCatalogAsync(ct),
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
        // Spec 032 — User Code required for Solicitante (not asked for other roles).
        if (string.Equals(vm.Role, "Applicant", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(vm.UserCode))
        {
            ModelState.AddModelError(nameof(vm.UserCode), AdminUsersResources.UserCodeRequired);
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
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
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
                    IdentificationType: vm.IdentificationType,
                    UserCode: vm.UserCode),
                actorId, ct);
            if (!result.Succeeded)
            {
                AddDomainErrors(result.Errors, mapping: (code, _) => code switch
                {
                    "CONCURRENCY_CONFLICT" => AdminUsersResources.ConcurrencyConflict,
                    "GROUP_NOT_FOUND" => AdminUsersResources.GroupNotFound,
                    "AT_LEAST_ONE_GROUP" => AdminUsersResources.AtLeastOneGroupRequired,
                    "USER_CODE_IN_USE" => AdminUsersResources.UserCodeInUse,
                    _ => null,
                });
                vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
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
        catch (DbUpdateException ex) when (ex.GetBaseException().Message.Contains("UX_Applicants_UserCode"))
        {
            // Spec 032 — concurrency backstop on the filtered unique index.
            ModelState.AddModelError(nameof(vm.UserCode), AdminUsersResources.UserCodeInUse);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }
        catch (SentinelUserModificationException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.SentinelImmutable);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }
        catch (LastAdministratorException)
        {
            ModelState.AddModelError(string.Empty, AdminErrorMessages.LastAdminProtected);
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
            return View(vm);
        }
        catch (SelfModificationException ex)
        {
            ModelState.AddModelError(string.Empty, ResolveSelfMessage(ex.Action));
            vm.AvailableGroups = await LoadGroupOptionsAsync(ct);
            vm.FundCatalog = await LoadFundCatalogAsync(ct);
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

    [HttpPost("{id}/ResendInvitation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvitation(string id, CancellationToken ct)
    {
        // Spec 033 / US2 / C3 — issue a fresh 72h invite (superseding the prior
        // unused link) and re-render the confirmation with the new copyable link.
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var inviteLink = await IssueAndSendInvitationAsync(user.Email!, ct);
        if (inviteLink is null)
        {
            TempData["ErrorMessage"] = "No se pudo generar la invitación. Intente de nuevo.";
            return RedirectToAction(nameof(Index));
        }
        return View("InvitationSent", new AdminUserInvitationSentViewModel(user.Email!, inviteLink));
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
