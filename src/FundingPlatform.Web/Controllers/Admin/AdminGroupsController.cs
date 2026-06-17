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
    private readonly Application.Admin.Filters.IFundHierarchyProvider _fundHierarchy;

    public AdminGroupsController(
        IGroupService groups,
        IProcessQueryService processQuery,
        UserManager<ApplicationUser> userManager,
        Application.Admin.Filters.IFundHierarchyProvider fundHierarchy)
    {
        _groups = groups;
        _processQuery = processQuery;
        _userManager = userManager;
        _fundHierarchy = fundHierarchy;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? fundId, int? processId, string? search, CancellationToken ct)
    {
        var rows = await _groups.ListAsync(ct);

        var filtered = rows.Where(g =>
            (!fundId.HasValue || g.FundId == fundId.Value)
            && (!processId.HasValue || g.ProcessId == processId.Value)
            && (string.IsNullOrWhiteSpace(search)
                || g.Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase)));

        var vm = new AdminGroupsIndexViewModel
        {
            Groups = filtered
                .Select(g => new AdminGroupRow(
                    g.Id, g.Name, g.MemberCount, g.ProcessId, g.ProcessName, g.FundId, g.FundName))
                .ToList(),
            HasAnyGroups = rows.Count > 0,
            Search = search,
            // Include archived Funds so an admin can still find groups under one.
            FundHierarchy = await _fundHierarchy.GetAsync(includeArchived: true, ct),
            FundFilter = fundId,
            ProcessFilter = processId,
        };
        return View(vm);
    }

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var detail = await _groups.GetAsync(id, ct);
        if (detail is null) return NotFound();
        var rows = await _groups.ListAsync(ct);
        var row = rows.FirstOrDefault(r => r.Id == id);

        var vm = new AdminGroupEditViewModel
        {
            Id = detail.Id,
            Name = detail.Name,
            ProcessId = detail.ProcessId,
            MemberCount = row?.MemberCount ?? 0,
            ProcessName = row?.ProcessName ?? "",
            FundName = row?.FundName ?? "",
        };
        await PopulateReparentCatalogAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminGroupEditViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id)
        {
            return BadRequest();
        }
        // The drill-down posts an empty Process as 0; a Group must belong to a Process.
        if (vm.ProcessId <= 0)
        {
            ModelState.AddModelError(nameof(vm.ProcessId), AdminGroupsResources.ProcessRequired);
        }
        if (!ModelState.IsValid)
        {
            await PopulateReparentCatalogAsync(vm, ct);
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
            await PopulateReparentCatalogAsync(vm, ct);
            return View(vm);
        }
        catch (ArgumentException)
        {
            // NFR-004 — localized fallback for the defensive domain-validation path.
            ModelState.AddModelError(nameof(vm.Name), AdminGroupsResources.NameRequired);
            await PopulateReparentCatalogAsync(vm, ct);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            // Reparenting into a closed Process (Spec 021 / FR-001).
            ModelState.AddModelError(nameof(vm.ProcessId), ex.Message);
            await PopulateReparentCatalogAsync(vm, ct);
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

    /// <summary>Populates the Fondo → Proceso reparent drill-down: the Fund
    /// hierarchy (incl. Archived so the current Process is always reachable) plus
    /// the Fund of the currently-selected Process (pre-selecting the Fondo level).</summary>
    private async Task PopulateReparentCatalogAsync(AdminGroupEditViewModel vm, CancellationToken ct)
    {
        var hierarchy = await _fundHierarchy.GetAsync(includeArchived: true, ct);
        vm.FundHierarchy = hierarchy;
        vm.SelectedFundId = hierarchy
            .FirstOrDefault(f => f.Processes.Any(p => p.Id == vm.ProcessId))?.Id;
    }
}
