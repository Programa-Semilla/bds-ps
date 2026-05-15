using FundingPlatform.Application.Admin.Commands;
using FundingPlatform.Application.Services;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Admin,SupplierAdmin")]
[SupplierAdminDenied]
public class AdminController : Controller
{
    private readonly AdminService _adminService;
    private readonly IAdminDashboardProjection _dashboard;

    public AdminController(AdminService adminService, IAdminDashboardProjection dashboard)
    {
        _adminService = adminService;
        _dashboard = dashboard;
    }

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
}
