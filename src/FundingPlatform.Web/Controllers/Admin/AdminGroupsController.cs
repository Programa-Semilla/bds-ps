using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 016 / 021 — admin-only catalog management for
/// <see cref="Domain.Entities.Group"/>. `[Authorize(Roles = "Admin,SupplierAdmin")]`
/// covers FR-002 (non-admins → 403). The `[Authorize]` attribute also handles
/// unauthenticated callers (redirect to login / 401).
///
/// Spec 021 / FR-001 — Group <em>creation</em> moved to the Process Details
/// page (<c>AdminProcessesController.CreateGroup</c>) so the owning Process is
/// implied by context. This controller keeps the catalog list, rename, the
/// Process reparenting selector, and delete.
/// </summary>
[Authorize(Roles = "Admin,SupplierAdmin")]
[SupplierAdminDenied]
[Route("Admin/Groups")]
public class AdminGroupsController : Controller
{
    private readonly IGroupService _groups;
    private readonly IProcessQueryService _processQuery;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminGroupsController(
        IGroupService groups,
        IProcessQueryService processQuery,
        UserManager<ApplicationUser> userManager)
    {
        _groups = groups;
        _processQuery = processQuery;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await _groups.ListAsync(ct);
        var vm = new AdminGroupsIndexViewModel
        {
            Groups = rows
                .Select(g => new AdminGroupRow(g.Id, g.Name, g.MemberCount, g.ProcessName))
                .ToList(),
        };
        return View(vm);
    }

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var detail = await _groups.GetAsync(id, ct);
        if (detail is null) return NotFound();
        var rows = await _groups.ListAsync(ct);
        var memberCount = rows.FirstOrDefault(r => r.Id == id)?.MemberCount ?? 0;

        return View(new AdminGroupEditViewModel
        {
            Id = detail.Id,
            Name = detail.Name,
            ProcessId = detail.ProcessId,
            MemberCount = memberCount,
            ProcessOptions = await LoadProcessOptionsAsync(ct),
        });
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminGroupEditViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id)
        {
            return BadRequest();
        }
        if (!ModelState.IsValid)
        {
            vm.ProcessOptions = await LoadProcessOptionsAsync(ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            // Rename + reparent. RenameAsync is idempotent when unchanged, and
            // MoveToProcessAsync is a no-op when the Process did not change, so
            // a plain rename does not write a spurious group.move_process row.
            await _groups.RenameAsync(id, vm.Name, actorId, ct);
            await _groups.MoveToProcessAsync(id, vm.ProcessId, actorId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DuplicateGroupNameException)
        {
            ModelState.AddModelError(nameof(vm.Name), AdminGroupsResources.NameAlreadyInUse);
            vm.ProcessOptions = await LoadProcessOptionsAsync(ct);
            return View(vm);
        }
        catch (ArgumentException)
        {
            // NFR-004 — localized fallback for the defensive domain-validation path.
            ModelState.AddModelError(nameof(vm.Name), AdminGroupsResources.NameRequired);
            vm.ProcessOptions = await LoadProcessOptionsAsync(ct);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            // Reparenting into a closed Process (Spec 021 / FR-001).
            ModelState.AddModelError(nameof(vm.ProcessId), ex.Message);
            vm.ProcessOptions = await LoadProcessOptionsAsync(ct);
            return View(vm);
        }

        TempData["SuccessMessage"] = AdminGroupsResources.FlashRenamed;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            await _groups.DeleteAsync(id, actorId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        TempData["SuccessMessage"] = AdminGroupsResources.FlashDeleted;
        return RedirectToAction(nameof(Index));
    }

    private async Task<IReadOnlyList<AdminGroupProcessOption>> LoadProcessOptionsAsync(CancellationToken ct)
    {
        var processes = await _processQuery.ListAsync(statusFilter: null, ct);
        return processes
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .Select(p => new AdminGroupProcessOption(p.Id, p.Name))
            .ToList();
    }
}
