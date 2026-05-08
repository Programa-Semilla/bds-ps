using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 016 — admin-only catalog management for <see cref="Domain.Entities.Group"/>.
/// `[Authorize(Roles = "Admin")]` covers FR-002 (non-admins → 403). The
/// `[Authorize]` attribute also handles unauthenticated callers (redirect to
/// login / 401), so no extra code path is needed for that case.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/Groups")]
public class AdminGroupsController : Controller
{
    private readonly IGroupService _groups;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminGroupsController(IGroupService groups, UserManager<ApplicationUser> userManager)
    {
        _groups = groups;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await _groups.ListAsync(ct);
        var vm = new AdminGroupsIndexViewModel
        {
            Groups = rows.Select(g => new AdminGroupRow(g.Id, g.Name, g.MemberCount)).ToList(),
        };
        return View(vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new AdminGroupCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminGroupCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            await _groups.CreateAsync(vm.Name, actorId, ct);
        }
        catch (DuplicateGroupNameException)
        {
            ModelState.AddModelError(nameof(vm.Name), AdminGroupsResources.NameAlreadyInUse);
            return View(vm);
        }
        catch (ArgumentException ex)
        {
            // Domain validation surfaced from Group.Create — defensive, the model
            // attributes already cover the empty/over-length cases.
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
            return View(vm);
        }

        TempData["SuccessMessage"] = AdminGroupsResources.FlashCreated;
        return RedirectToAction(nameof(Index));
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
            MemberCount = memberCount,
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
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? "";
        try
        {
            await _groups.RenameAsync(id, vm.Name, actorId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DuplicateGroupNameException)
        {
            ModelState.AddModelError(nameof(vm.Name), AdminGroupsResources.NameAlreadyInUse);
            return View(vm);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
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
}
