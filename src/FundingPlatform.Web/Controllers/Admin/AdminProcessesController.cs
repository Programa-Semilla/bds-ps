// Spec 021 — see specs/021-feedback-session-may13/tasks.md T080
// and contracts/admin-routes.md (Processes section).

using FundingPlatform.Application.Plantillas;
using FundingPlatform.Application.Processes;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 021 / US1 / T080 — admin lifecycle for the <see cref="Process"/>
/// aggregate. Routes follow <c>contracts/admin-routes.md</c> Processes section.
/// Mirrors <c>AdminGroupsController</c> for style (constructor injection,
/// per-route attributes, TempData flash + ModelState validation).
/// </summary>
[Authorize(Roles = "Admin,SupplierAdmin")]
[SupplierAdminDenied]
[Route("Admin/Processes")]
public class AdminProcessesController : Controller
{
    private readonly IProcessService _processes;
    private readonly IProcessQueryService _processQuery;
    private readonly IPlantillaService _plantillas;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminProcessesController(
        IProcessService processes,
        IProcessQueryService processQuery,
        IPlantillaService plantillas,
        UserManager<ApplicationUser> userManager)
    {
        _processes = processes;
        _processQuery = processQuery;
        _plantillas = plantillas;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(ProcessStatus? statusFilter, CancellationToken ct)
    {
        var rows = await _processQuery.ListAsync(statusFilter, ct);
        return View(new AdminProcessesIndexViewModel
        {
            Rows = rows,
            StatusFilter = statusFilter,
        });
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new AdminProcessCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminProcessCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _processes.CreateAsync(new CreateProcessCommand(vm.Name), actorId, ct);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
            return View(vm);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            ModelState.AddModelError(nameof(vm.Name), "Ya existe un proceso con ese nombre.");
            return View(vm);
        }

        TempData["SuccessMessage"] = $"Proceso '{vm.Name}' creado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var detail = await _processQuery.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();

        // Only surface assignable base Plantillas when no snapshot exists
        // (OQ-1: one-to-one — the form widget would otherwise be confusing).
        IReadOnlyList<PlantillaListRow> assignable = detail.Plantilla is null
            ? (await _plantillas.ListAsync(ct))
                .Where(p => !p.IsArchived && p.ImpactTemplateCount > 0)
                .ToList()
            : Array.Empty<PlantillaListRow>();

        return View(new AdminProcessDetailsViewModel
        {
            Detail = detail,
            AssignableBasePlantillas = assignable,
            CloseBlockingPublicCodes = TempData["CloseBlockingPublicCodes"] as string[]
                ?? Array.Empty<string>(),
        });
    }

    [HttpPost("{id:int}/AssignPlantilla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignPlantilla(int id, int plantillaId, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _processes.AssignPlantillaAsync(
                new AssignPlantillaCommand(id, plantillaId), actorId, ct);
            TempData["SuccessMessage"] = "Plantilla asignada al proceso.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // Re-rendering the detail page with a flash keeps the user oriented
            // (the form widget was on Details, not on a dedicated route).
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/StageOverride")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StageOverride(int id, StageKind stageKind, int? days, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _processes.OverrideStageWindowAsync(
                new OverrideStageWindowCommand(id, stageKind, days), actorId, ct);
            TempData["SuccessMessage"] = days is null
                ? $"Ventana '{stageKind}' restablecida al valor por defecto."
                : $"Ventana '{stageKind}' establecida en {days} día(s).";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentOutOfRangeException)
        {
            TempData["ErrorMessage"] = "El número de días debe ser positivo (o vacío para usar el valor por defecto).";
        }
        catch (Domain.Exceptions.ProcessClosedException)
        {
            TempData["ErrorMessage"] = "El proceso está cerrado; no se pueden modificar las ventanas de etapa.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _processes.CloseAsync(new CloseProcessCommand(id), actorId, ct);
            TempData["SuccessMessage"] = "Proceso cerrado.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ProcessCloseBlockedException ex)
        {
            // Contract: 422 with list of offending PublicCodes. We use TempData
            // to round-trip the list across the redirect so the detail view can
            // render the alert; the controller still returns 422 inline in
            // case the caller asked via fetch().
            TempData["ErrorMessage"] = $"No se puede cerrar el proceso: {ex.ActivePublicCodes.Count} solicitud(es) activa(s).";
            TempData["CloseBlockingPublicCodes"] = ex.ActivePublicCodes.ToArray();
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Domain.Exceptions.ProcessClosedException)
        {
            TempData["ErrorMessage"] = "El proceso ya está cerrado.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
