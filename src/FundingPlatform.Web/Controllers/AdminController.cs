using System.Security.Claims;
using FundingPlatform.Application.Admin.Commands;
using FundingPlatform.Application.Checklists;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Admin,Auditor")]
[SupplierAdminDenied]
public class AdminController : Controller
{
    private readonly AdminService _adminService;
    private readonly IAdminDashboardProjection _dashboard;
    // Spec 040 / US4 — admin checklist-template CRUD.
    private readonly IChecklistTemplateService _checklists;

    public AdminController(
        AdminService adminService,
        IAdminDashboardProjection dashboard,
        IChecklistTemplateService checklists)
    {
        _adminService = adminService;
        _dashboard = dashboard;
        _checklists = checklists;
    }

    private string ActorUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var dto = await _dashboard.GetAsync(ct);
        return View(new AdminDashboardViewModel(dto));
    }

    [HttpGet]
    public async Task<IActionResult> ImpactTemplates()
    {
        var templates = await _adminService.GetAllImpactTemplatesAsync();

        var viewModel = new ImpactTemplateAdminViewModel
        {
            Templates = templates.Select(t => new ImpactTemplateListItemViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                IsActive = t.IsActive,
                ParameterCount = t.Parameters.Count
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult CreateTemplate()
    {
        var viewModel = new CreateImpactTemplateViewModel();
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTemplate(CreateImpactTemplateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var command = new CreateImpactTemplateCommand(
            model.Name,
            model.Description,
            model.Parameters.Select(p => new ParameterDefinition(
                p.Name,
                p.DisplayLabel,
                p.DataType,
                p.IsRequired,
                p.ValidationRules,
                p.SortOrder)).ToList());

        await _adminService.CreateImpactTemplateAsync(command);

        TempData["SuccessMessage"] = "Plantilla de impacto creada con éxito.";
        return RedirectToAction(nameof(ImpactTemplates));
    }

    [HttpGet]
    public async Task<IActionResult> EditTemplate(int id)
    {
        var template = await _adminService.GetImpactTemplateByIdAsync(id);
        if (template is null)
        {
            return NotFound();
        }

        var viewModel = new EditImpactTemplateViewModel
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description,
            IsActive = template.IsActive,
            Parameters = template.Parameters.Select(p => new ParameterDefinitionViewModel
            {
                Name = p.Name,
                DisplayLabel = p.DisplayLabel,
                DataType = p.DataType,
                IsRequired = p.IsRequired,
                ValidationRules = p.ValidationRules,
                SortOrder = p.SortOrder
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTemplate(EditImpactTemplateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var command = new UpdateImpactTemplateCommand(
            model.Id,
            model.Name,
            model.Description,
            model.IsActive,
            model.Parameters.Select(p => new ParameterDefinition(
                p.Name,
                p.DisplayLabel,
                p.DataType,
                p.IsRequired,
                p.ValidationRules,
                p.SortOrder)).ToList());

        await _adminService.UpdateImpactTemplateAsync(command);

        TempData["SuccessMessage"] = "Plantilla de impacto actualizada con éxito.";
        return RedirectToAction(nameof(ImpactTemplates));
    }

    // ---------------- Spec 035 / US1 — category field configuration ----------------

    [HttpGet]
    public async Task<IActionResult> Categories()
    {
        var categories = await _adminService.GetAllCategoriesAsync();
        var viewModel = new CategoryAdminViewModel
        {
            Categories = categories.Select(c => new CategoryListItemViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                IsActive = c.IsActive,
                FieldCount = c.FieldCount,
            }).ToList()
        };
        return View(viewModel);
    }

    [HttpGet]
    public IActionResult CreateCategory()
    {
        return View(new CreateCategoryViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CreateCategoryViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var command = new CreateCategoryCommand(
            model.Name,
            model.Description,
            model.Fields.Select(f => new CategoryFieldDefinition(
                f.Name, f.DisplayLabel, f.DataType, f.IsRequired, f.SortOrder)).ToList());

        try
        {
            await _adminService.CreateCategoryAsync(command);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException")
        {
            ModelState.AddModelError(nameof(model.Name), "Ya existe una categoría con ese nombre.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Categoría creada con éxito.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpGet]
    public async Task<IActionResult> EditCategory(int id)
    {
        var category = await _adminService.GetCategoryByIdAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        var viewModel = new EditCategoryViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive,
            Fields = category.Fields.Select(f => new CategoryFieldDefinitionViewModel
            {
                Name = f.Name,
                DisplayLabel = f.DisplayLabel,
                DataType = f.DataType,
                IsRequired = f.IsRequired,
                SortOrder = f.SortOrder,
            }).ToList()
        };
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(EditCategoryViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var command = new UpdateCategoryCommand(
            model.Id,
            model.Name,
            model.Description,
            model.IsActive,
            model.Fields.Select(f => new CategoryFieldDefinition(
                f.Name, f.DisplayLabel, f.DataType, f.IsRequired, f.SortOrder)).ToList());

        try
        {
            await _adminService.UpdateCategoryAsync(command);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException")
        {
            ModelState.AddModelError(nameof(model.Name), "Ya existe una categoría con ese nombre.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Categoría actualizada con éxito.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpGet]
    public async Task<IActionResult> Configuration()
    {
        var configs = await _adminService.GetAllSystemConfigurationsAsync();

        var viewModel = new SystemConfigurationViewModel
        {
            Configurations = configs.Select(c => new SystemConfigurationEntryViewModel
            {
                Id = c.Id,
                Key = c.Key,
                Value = c.Value,
                Description = c.Description
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configuration(SystemConfigurationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var command = new UpdateSystemConfigurationCommand(
            model.Configurations.Select(c => new ConfigurationUpdate(c.Id, c.Value ?? string.Empty)).ToList());

        await _adminService.UpdateSystemConfigurationAsync(command);

        TempData["SuccessMessage"] = "Configuración del sistema actualizada con éxito.";
        return RedirectToAction(nameof(Configuration));
    }

    // ---------- Spec 040 / US4 — checklist template admin ----------

    [HttpGet]
    public async Task<IActionResult> Checklists(ChecklistStage? stage, bool? active, CancellationToken ct)
    {
        var rows = await _checklists.ListAsync(stage, active, ct);
        return View(new ChecklistAdminViewModel
        {
            StageFilter = stage,
            ActiveFilter = active,
            Templates = rows.Select(r => new ChecklistListItemViewModel
            {
                Id = r.Id,
                Name = r.Name,
                AppliesToStage = r.AppliesToStage,
                IsActive = r.IsActive,
                ItemCount = r.ItemCount,
            }).ToList()
        });
    }

    [HttpGet]
    public IActionResult CreateChecklist()
    {
        return View(new CreateChecklistViewModel
        {
            Items = [new ChecklistItemViewModel { Text = string.Empty, IsRequired = true }]
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateChecklist(CreateChecklistViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var command = new CreateChecklistTemplateCommand(
            model.Name, model.Description, model.AppliesToStage, model.Activate,
            (model.Items ?? []).Select(i => new ChecklistItemInput(i.Text, i.IsRequired)).ToList());

        await _checklists.CreateAsync(command, ActorUserId, ct);
        TempData["SuccessMessage"] = "Lista de verificación creada con éxito.";
        return RedirectToAction(nameof(Checklists));
    }

    [HttpGet]
    public async Task<IActionResult> EditChecklist(int id, CancellationToken ct)
    {
        var detail = await _checklists.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();

        return View(new EditChecklistViewModel
        {
            Id = detail.Id,
            Name = detail.Name,
            Description = detail.Description,
            AppliesToStage = detail.AppliesToStage,
            IsActive = detail.IsActive,
            Items = detail.Items.Select(i => new ChecklistItemViewModel
            {
                Text = i.Text,
                IsRequired = i.IsRequired,
            }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditChecklist(EditChecklistViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var command = new EditChecklistTemplateCommand(
            model.Id, model.Name, model.Description, model.AppliesToStage,
            (model.Items ?? []).Select(i => new ChecklistItemInput(i.Text, i.IsRequired)).ToList());

        await _checklists.EditAsync(command, ActorUserId, ct);
        TempData["SuccessMessage"] = "Lista de verificación actualizada con éxito.";
        return RedirectToAction(nameof(Checklists));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateChecklist(int id, CancellationToken ct)
    {
        await _checklists.ActivateAsync(id, ActorUserId, ct);
        TempData["SuccessMessage"] = "Lista de verificación activada.";
        return RedirectToAction(nameof(Checklists));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateChecklist(int id, CancellationToken ct)
    {
        await _checklists.DeactivateAsync(id, ActorUserId, ct);
        TempData["SuccessMessage"] = "Lista de verificación desactivada.";
        return RedirectToAction(nameof(Checklists));
    }
}
