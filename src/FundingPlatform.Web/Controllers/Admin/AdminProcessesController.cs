// Spec 021 — see specs/021-feedback-session-may13/tasks.md T080
// and contracts/admin-routes.md (Processes section).

using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Plantillas;
using FundingPlatform.Application.Processes;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 021 / US1 / T080 — admin lifecycle for the <see cref="Process"/>
/// aggregate. Routes follow <c>contracts/admin-routes.md</c> Processes section.
/// Mirrors <c>AdminGroupsController</c> for style (constructor injection,
/// per-route attributes, TempData flash + ModelState validation).
/// </summary>
[Authorize(Roles = "Admin,Auditor")]
[SupplierAdminDenied]
[Route("Admin/Processes")]
public class AdminProcessesController : Controller
{
    private readonly IProcessService _processes;
    private readonly IProcessQueryService _processQuery;
    private readonly IPlantillaService _plantillas;
    private readonly IGroupService _groups;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly Application.Admin.Filters.IFundHierarchyProvider _fundHierarchy;
    // Spec 044 — reception-window admin CRUD + CR-local input/display.
    private readonly Application.Processes.ReceptionWindows.IReceptionWindowService _receptionWindows;
    private readonly Application.Processes.ReceptionWindows.IReceptionWindowQuery _receptionWindowQuery;
    private readonly Application.Time.IBusinessTimeZone _businessTime;
    private readonly Domain.Interfaces.IStageExpiryClock _clock;

    public AdminProcessesController(
        IProcessService processes,
        IProcessQueryService processQuery,
        IPlantillaService plantillas,
        IGroupService groups,
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Application.Admin.Filters.IFundHierarchyProvider fundHierarchy,
        Application.Processes.ReceptionWindows.IReceptionWindowService receptionWindows,
        Application.Processes.ReceptionWindows.IReceptionWindowQuery receptionWindowQuery,
        Application.Time.IBusinessTimeZone businessTime,
        Domain.Interfaces.IStageExpiryClock clock)
    {
        _processes = processes;
        _processQuery = processQuery;
        _plantillas = plantillas;
        _groups = groups;
        _userManager = userManager;
        _db = db;
        _fundHierarchy = fundHierarchy;
        _receptionWindows = receptionWindows;
        _receptionWindowQuery = receptionWindowQuery;
        _businessTime = businessTime;
        _clock = clock;
    }

    /// <summary>Spec 029 / FR-002 — Active Funds for the Process Fund selector.</summary>
    private async Task PopulateFundOptionsAsync(AdminProcessCreateViewModel vm, CancellationToken ct)
    {
        vm.FundOptions = await _db.Funds
            .Where(f => f.Status == FundStatus.Active)
            .OrderBy(f => f.Name)
            .Select(f => new SelectListItem(
                f.Name, f.Id.ToString()))
            .ToListAsync(ct);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(ProcessStatus? statusFilter, int? fundId, CancellationToken ct)
    {
        var rows = await _processQuery.ListAsync(statusFilter, fundId, ct);
        // Spec 029 / FR-011 — Fund filter lists every Fund (incl. Archived) so an
        // admin can still find Processes under an archived Fund.
        return View(new AdminProcessesIndexViewModel
        {
            Rows = rows,
            StatusFilter = statusFilter,
            FundFilter = fundId,
            FundHierarchy = await _fundHierarchy.GetAsync(includeArchived: true, ct),
        });
    }

    [HttpPost("{id:int}/ChangeFund")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeFund(int id, int fundId, CancellationToken ct)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _processes.ReassignFundAsync(new ReassignProcessFundCommand(id, fundId), actorId, ct);
            TempData["SuccessMessage"] = "Fondo del proceso actualizado.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (Domain.Exceptions.ProcessClosedException)
        {
            TempData["ErrorMessage"] = "El proceso está cerrado; no se puede cambiar el fondo.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new AdminProcessCreateViewModel();
        await PopulateFundOptionsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminProcessCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateFundOptionsAsync(vm, ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _processes.CreateAsync(new CreateProcessCommand(vm.Name, vm.FundId ?? 0), actorId, ct);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
            await PopulateFundOptionsAsync(vm, ct);
            return View(vm);
        }
        catch (KeyNotFoundException)
        {
            ModelState.AddModelError(nameof(vm.FundId), "Debe seleccionar un fondo activo.");
            await PopulateFundOptionsAsync(vm, ct);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            // Spec 029 — Fund missing/Archived.
            ModelState.AddModelError(nameof(vm.FundId), ex.Message);
            await PopulateFundOptionsAsync(vm, ct);
            return View(vm);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            ModelState.AddModelError(nameof(vm.Name), "Ya existe un proceso con ese nombre.");
            await PopulateFundOptionsAsync(vm, ct);
            return View(vm);
        }

        TempData["SuccessMessage"] = $"Proceso '{vm.Name}' creado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var vm = await BuildDetailsViewModelAsync(id, ct);
        if (vm is null) return NotFound();
        return View(vm);
    }

    /// <summary>Builds the <see cref="AdminProcessDetailsViewModel"/> the Details
    /// view renders. Shared by the GET and by the spec-030 Rename error
    /// re-render so the inline-error path shows an identical page.</summary>
    private async Task<AdminProcessDetailsViewModel?> BuildDetailsViewModelAsync(int id, CancellationToken ct)
    {
        var detail = await _processQuery.GetDetailAsync(id, ct);
        if (detail is null) return null;

        // Only surface assignable base Plantillas when no snapshot exists
        // (OQ-1: one-to-one — the form widget would otherwise be confusing).
        IReadOnlyList<PlantillaListRow> assignable = detail.Plantilla is null
            ? (await _plantillas.ListAsync(ct))
                .Where(p => !p.IsArchived)
                .ToList()
            : Array.Empty<PlantillaListRow>();

        var fundOptions = await _db.Funds
            .Where(f => f.Status == FundStatus.Active)
            .OrderBy(f => f.Name)
            .Select(f => new SelectListItem(f.Name, f.Id.ToString()))
            .ToListAsync(ct);

        // Spec 044 / US1 — reception windows projected into CR local time for the card.
        var windowRows = await _receptionWindowQuery.GetForProcessAsync(id, _clock.UtcNow, ct);
        var windows = windowRows
            .Select(w => new ReceptionWindowDisplayRow(
                w.Id,
                w.Name,
                _businessTime.ToBusinessLocal(w.StartUtc).DateTime,
                _businessTime.ToBusinessLocal(w.EndUtc).DateTime,
                w.ApplicantFacingMessage,
                w.Description,
                w.IsActive,
                w.DisplayOrder,
                w.State))
            .ToList();

        return new AdminProcessDetailsViewModel
        {
            Detail = detail,
            AssignableBasePlantillas = assignable,
            CloseBlockingPublicCodes = TempData["CloseBlockingPublicCodes"] as string[]
                ?? Array.Empty<string>(),
            FundOptions = fundOptions,
            ReceptionWindows = windows,
        };
    }

    [HttpPost("{id:int}/Rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, string? newName, CancellationToken ct)
    {
        // Nullable param: avoid the implicit [Required] on a non-nullable string,
        // whose English message ("The newName field is required.") would override
        // the es-CR validation below (FR-008).
        // Spec 030 / FR-004 / FR-008 — validate required/≤120 with es-CR copy
        // (mirrors the Create ViewModel messages) so the inline message is
        // Spanish; the domain Rename() remains the backstop.
        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            ModelState.AddModelError(nameof(newName), "El nombre es obligatorio.");
        }
        else if (trimmed.Length > Process.MaxNameLength)
        {
            ModelState.AddModelError(
                nameof(newName), $"El nombre debe tener {Process.MaxNameLength} caracteres o menos.");
        }

        if (ModelState.IsValid)
        {
            var actorId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                // newName is non-null here: pre-validation rejected null/empty.
                await _processes.RenameAsync(new RenameProcessCommand(id, newName!), actorId, ct);
                TempData["SuccessMessage"] = "Nombre del proceso actualizado.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException)
            {
                // Defensive backstop (es-CR) — pre-validation already covers the
                // required/≤120 cases the domain rejects.
                ModelState.AddModelError(nameof(newName), "El nombre es obligatorio.");
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another admin modified this Process concurrently (RowVersion).
                // Caught BEFORE DbUpdateException (its base type) so the collision
                // is not mislabeled as a duplicate-name error.
                ModelState.AddModelError(
                    nameof(newName), "El proceso fue modificado por otra persona; vuelva a intentarlo.");
            }
            catch (DbUpdateException)
            {
                // Spec 030 / FR-005 — duplicate name (UX_Processes_Name); reuse the
                // same es-CR message the Create flow surfaces.
                ModelState.AddModelError(nameof(newName), "Ya existe un proceso con ese nombre.");
            }
        }

        var vm = await BuildDetailsViewModelAsync(id, ct);
        if (vm is null) return NotFound();
        return View(nameof(Details), vm);
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

    // ===== Spec 044 / US1 — reception-window CRUD =====================================
    // Admin-only (FR-001 "Administrators" — overrides the class-level Admin,Auditor).
    // Window-scoped actions verify the window belongs to the route Process (no
    // cross-process mutation). Errors surface as TempData toasts (mirrors the
    // sibling AssignPlantilla/StageOverride actions); success → redirect to Details.
    // datetime-local inputs are CR-local wall-clock values → UTC via IBusinessTimeZone.

    [HttpPost("{id:int}/ReceptionWindows")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReceptionWindow(
        int id, string? name, DateTime? start, DateTime? end,
        string? applicantMessage, string? description, int displayOrder, CancellationToken ct)
    {
        if (WindowInputError(name, start, end) is { } inputError)
        {
            TempData["ErrorMessage"] = inputError;
            return RedirectToAction(nameof(Details), new { id });
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _receptionWindows.CreateAsync(
                new Application.Processes.ReceptionWindows.CreateReceptionWindowCommand(
                    id, name!.Trim(), _businessTime.ToUtc(start!.Value), _businessTime.ToUtc(end!.Value),
                    applicantMessage, description, displayOrder),
                actorId, ct);
            TempData["SuccessMessage"] = Resources.AdminReceptionWindowsResources.CreatedToast;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = MapWindowError(ex);
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/ReceptionWindows/{windowId:int}/Update")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateReceptionWindow(
        int id, int windowId, string? name, DateTime? start, DateTime? end,
        string? applicantMessage, string? description, int displayOrder, CancellationToken ct)
    {
        if (!await WindowBelongsToProcessAsync(id, windowId, ct)) return NotFound();

        if (WindowInputError(name, start, end) is { } inputError)
        {
            TempData["ErrorMessage"] = inputError;
            return RedirectToAction(nameof(Details), new { id });
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _receptionWindows.UpdateAsync(
                new Application.Processes.ReceptionWindows.UpdateReceptionWindowCommand(
                    windowId, name!.Trim(), _businessTime.ToUtc(start!.Value), _businessTime.ToUtc(end!.Value),
                    applicantMessage, description, displayOrder),
                actorId, ct);
            TempData["SuccessMessage"] = Resources.AdminReceptionWindowsResources.UpdatedToast;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "La ventana fue modificada por otra persona; vuelva a intentarlo.";
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = MapWindowError(ex);
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/ReceptionWindows/{windowId:int}/SetActive")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetReceptionWindowActive(
        int id, int windowId, bool isActive, CancellationToken ct)
    {
        if (!await WindowBelongsToProcessAsync(id, windowId, ct)) return NotFound();

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _receptionWindows.SetActiveAsync(windowId, isActive, actorId, ct);
            TempData["SuccessMessage"] = isActive
                ? Resources.AdminReceptionWindowsResources.ActivatedToast
                : Resources.AdminReceptionWindowsResources.DeactivatedToast;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "La ventana fue modificada por otra persona; vuelva a intentarlo.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/ReceptionWindows/{windowId:int}/Delete")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReceptionWindow(int id, int windowId, CancellationToken ct)
    {
        if (!await WindowBelongsToProcessAsync(id, windowId, ct)) return NotFound();

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _receptionWindows.DeleteAsync(windowId, actorId, ct);
            TempData["SuccessMessage"] = Resources.AdminReceptionWindowsResources.DeletedToast;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "La ventana fue modificada por otra persona; vuelva a intentarlo.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Spec 044 — guards against cross-process mutation: the window in the
    /// route must belong to the route Process (no IDOR via a mismatched windowId).</summary>
    private Task<bool> WindowBelongsToProcessAsync(int processId, int windowId, CancellationToken ct)
        => _db.ProcessEvents.AnyAsync(e => e.Id == windowId && e.ProcessId == processId, ct);

    /// <summary>Shared es-CR pre-validation for window create/update. Returns the
    /// first es-CR message (surfaced as a TempData error toast), or null when valid.</summary>
    private static string? WindowInputError(string? name, DateTime? start, DateTime? end)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Resources.AdminReceptionWindowsResources.NameRequired;
        }
        if (start is null || end is null)
        {
            return Resources.AdminReceptionWindowsResources.DatesRequired;
        }
        if (end.Value <= start.Value)
        {
            return Resources.AdminReceptionWindowsResources.EndAfterStart;
        }
        return null;
    }

    private static string MapWindowError(ArgumentException ex)
        => ex.ParamName switch
        {
            "endUtc" => Resources.AdminReceptionWindowsResources.EndAfterStart,
            "name" => Resources.AdminReceptionWindowsResources.NameRequired,
            _ => ex.Message,
        };

    [HttpPost("{id:int}/Groups")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGroup(int id, string groupName, CancellationToken ct)
    {
        // Spec 021 / FR-001 — Groups are created *under* a Process. The owning
        // Process is the route id; the form on Process Details posts only the
        // name. Mirrors the AssignPlantilla / StageOverride flash pattern.
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _groups.CreateAsync(groupName ?? string.Empty, id, actorId, ct);
            TempData["SuccessMessage"] = $"Grupo '{groupName}' creado.";
        }
        catch (KeyNotFoundException)
        {
            // The Process in the route does not exist.
            return NotFound();
        }
        catch (DuplicateGroupNameException)
        {
            TempData["ErrorMessage"] = "Ya existe un grupo con ese nombre.";
        }
        catch (ArgumentException)
        {
            TempData["ErrorMessage"] = "El nombre del grupo es obligatorio.";
        }
        catch (InvalidOperationException ex)
        {
            // Process is closed.
            TempData["ErrorMessage"] = ex.Message;
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
