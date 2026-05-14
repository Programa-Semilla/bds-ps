// Spec 021 — see specs/021-feedback-session-may13/tasks.md T081
// and contracts/admin-routes.md (Plantillas section).

using FundingPlatform.Application.Plantillas;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 021 / US1 / T081 — admin catalog management for base
/// <see cref="Plantilla"/> rows. Routes follow <c>contracts/admin-routes.md</c>
/// Plantillas section. Mirrors <c>AdminGroupsController</c> for style.
/// </summary>
[Authorize(Roles = "Admin")]
[SupplierAdminDenied]
[Route("Admin/Plantillas")]
public class AdminPlantillasController : Controller
{
    private readonly IPlantillaService _plantillas;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public AdminPlantillasController(
        IPlantillaService plantillas,
        UserManager<ApplicationUser> userManager,
        AppDbContext db)
    {
        _plantillas = plantillas;
        _userManager = userManager;
        _db = db;
    }

    private async Task<IReadOnlyList<AdminPlantillaImpactTemplateOption>> LoadImpactTemplateOptionsAsync(CancellationToken ct)
    {
        // Only the active ImpactTemplates are offered — applicants can never pick
        // an inactive template when choosing Impact, so attaching one to the
        // base Plantilla would be a dead branch.
        return await _db.ImpactTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new AdminPlantillaImpactTemplateOption(t.Id, t.Name))
            .ToListAsync(ct);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await _plantillas.ListAsync(ct);
        return View(new AdminPlantillasIndexViewModel { Rows = rows });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        return View(new AdminPlantillaCreateViewModel
        {
            AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct),
        });
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminPlantillaCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            vm.AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _plantillas.CreateAsync(
                new CreatePlantillaCommand(
                    vm.Name,
                    vm.MinimumQuotationsPerItem,
                    vm.RequiredFieldFlags,
                    vm.ImpactTemplateIds),
                actorId, ct);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
            vm.AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct);
            return View(vm);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(vm.Name), "Ya existe una plantilla con ese nombre.");
            vm.AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct);
            return View(vm);
        }

        TempData["SuccessMessage"] = $"Plantilla '{vm.Name}' creada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var detail = await _plantillas.GetAsync(id, ct);
        if (detail is null) return NotFound();

        return View(new AdminPlantillaEditViewModel
        {
            Id = detail.Id,
            Name = detail.Name,
            MinimumQuotationsPerItem = detail.MinimumQuotationsPerItem,
            RequiredFieldFlags = detail.RequiredFieldFlags,
            ImpactTemplateIds = detail.ImpactTemplateIds.ToArray(),
            IsArchived = detail.IsArchived,
            AssignedProcessCount = detail.AssignedProcessCount,
            AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct),
        });
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminPlantillaEditViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            vm.AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _plantillas.EditAsync(
                new EditPlantillaCommand(
                    id, vm.Name, vm.MinimumQuotationsPerItem,
                    vm.RequiredFieldFlags, vm.ImpactTemplateIds),
                actorId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
            vm.AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            vm.AvailableImpactTemplates = await LoadImpactTemplateOptionsAsync(ct);
            return View(vm);
        }

        TempData["SuccessMessage"] = "Plantilla actualizada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/Detach/{processId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Detach(int id, int processId, bool force, string? reason, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _plantillas.DetachAsync(
                new DetachPlantillaCommand(id, processId, force, reason), actorId, ct);
            TempData["SuccessMessage"] = "Plantilla desasignada del proceso.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PlantillaDetachBlockedException ex)
        {
            TempData["ErrorMessage"] = $"Plantilla en uso por {ex.ActiveApplicationCount} solicitud(es) activa(s). Use 'forzar' con una razón.";
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction("Details", "AdminProcesses", new { id = processId });
    }

    [HttpPost("{id:int}/Archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _plantillas.ArchiveAsync(new ArchivePlantillaCommand(id), actorId, ct);
            TempData["SuccessMessage"] = "Plantilla archivada.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
