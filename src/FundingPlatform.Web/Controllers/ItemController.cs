using System.Security.Claims;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Applicant")]
[Route("Application/{appId}/Item")]
public class ItemController : Controller
{
    private readonly ApplicationService _applicationService;
    private readonly ICategoryRepository _categoryRepository;
    private readonly AppDbContext _dbContext;

    public ItemController(
        ApplicationService applicationService,
        ICategoryRepository categoryRepository,
        AppDbContext dbContext)
    {
        _applicationService = applicationService;
        _categoryRepository = categoryRepository;
        _dbContext = dbContext;
    }

    [HttpGet("Add")]
    public async Task<IActionResult> Add(int appId)
    {
        await VerifyOwnershipAsync(appId);

        var viewModel = new AddItemViewModel { ApplicationId = appId };
        await PopulateOptionsAsync(viewModel);
        return View(viewModel);
    }

    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int appId, AddItemViewModel model)
    {
        await VerifyOwnershipAsync(appId);

        if (!ModelState.IsValid)
        {
            model.ApplicationId = appId;
            await PopulateOptionsAsync(model);
            return View(model);
        }

        try
        {
            var command = new AddItemCommand(
                appId,
                model.ProductName,
                model.CategoryId,
                model.CategoryFieldValues ?? new(),
                model.SelectedApplicationImpactIds ?? new(),
                model.ImpactJustification);

            await _applicationService.AddItemAsync(command);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.ApplicationId = appId;
            await PopulateOptionsAsync(model);
            return View(model);
        }

        TempData["SuccessMessage"] = "Ítem agregado con éxito.";
        return RedirectToAction("Edit", "Application", new { id = appId });
    }

    [HttpGet("{itemId}/Edit")]
    public async Task<IActionResult> Edit(int appId, int itemId)
    {
        await VerifyOwnershipAsync(appId);

        var item = await _dbContext.Items
            .AsNoTracking()
            .Include(i => i.CategoryFieldValues)
            .Include(i => i.ItemImpacts)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ApplicationId == appId);
        if (item is null)
        {
            return NotFound();
        }

        var categoryValues = item.CategoryFieldValues.ToDictionary(v => v.CategoryFieldId, v => v.Value);

        var viewModel = new EditItemViewModel
        {
            Id = item.Id,
            ApplicationId = appId,
            ProductName = item.ProductName,
            CategoryId = item.CategoryId,
            ImpactJustification = item.ImpactJustification,
            SelectedApplicationImpactIds = item.ItemImpacts.Select(ii => ii.ApplicationImpactId).ToList(),
        };
        await PopulateOptionsAsync(viewModel);
        viewModel.CategoryFields = await LoadCategoryFieldInputsAsync(item.CategoryId, categoryValues);

        return View(viewModel);
    }

    [HttpPost("{itemId}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int appId, int itemId, EditItemViewModel model)
    {
        await VerifyOwnershipAsync(appId);

        if (!ModelState.IsValid)
        {
            model.ApplicationId = appId;
            model.Id = itemId;
            await PopulateOptionsAsync(model);
            return View(model);
        }

        try
        {
            var command = new UpdateItemCommand(
                itemId,
                appId,
                model.ProductName,
                model.CategoryId,
                model.CategoryFieldValues ?? new(),
                model.SelectedApplicationImpactIds ?? new(),
                model.ImpactJustification);

            await _applicationService.UpdateItemAsync(command);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.ApplicationId = appId;
            model.Id = itemId;
            await PopulateOptionsAsync(model);
            return View(model);
        }

        TempData["SuccessMessage"] = "Ítem actualizado con éxito.";
        return RedirectToAction("Edit", "Application", new { id = appId });
    }

    [HttpPost("{itemId}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int appId, int itemId)
    {
        await VerifyOwnershipAsync(appId);

        var command = new RemoveItemCommand(itemId, appId);
        await _applicationService.RemoveItemAsync(command);

        TempData["SuccessMessage"] = "Ítem eliminado con éxito.";
        return RedirectToAction("Edit", "Application", new { id = appId });
    }

    /// <summary>
    /// Spec 035 / US2 / T041 — category field descriptors for the dynamic form,
    /// fetched when the applicant picks a category.
    /// </summary>
    [HttpGet("Category/{categoryId}/Fields")]
    public async Task<IActionResult> CategoryFields(int appId, int categoryId)
    {
        await VerifyOwnershipAsync(appId);

        var category = await _categoryRepository.GetByIdWithFieldsAsync(categoryId);
        if (category is null)
        {
            return NotFound();
        }

        return Json(category.Fields
            .OrderBy(f => f.SortOrder)
            .Select(f => new
            {
                id = f.Id,
                name = f.Name,
                displayLabel = f.DisplayLabel,
                dataType = (int)f.DataType,
                isRequired = f.IsRequired,
            }));
    }

    private async Task PopulateOptionsAsync(AddItemViewModel model)
    {
        var categories = await _categoryRepository.GetAllActiveAsync();
        model.Categories = categories
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToList();

        // Spec 035 (evolved 2026-06-16, D15 / FR-007) — the attribution multi-select offers
        // ONLY the impacts the application has declared (never raw templates).
        model.DeclaredImpacts = await _dbContext.ApplicationImpacts
            .AsNoTracking()
            .Where(ai => ai.ApplicationId == model.ApplicationId)
            .Select(ai => new DeclaredImpactOption
            {
                ApplicationImpactId = ai.Id,
                Name = ai.ImpactTemplate.Name,
            })
            .ToListAsync();
    }

    private async Task<List<DynamicFieldInput>> LoadCategoryFieldInputsAsync(
        int categoryId, IReadOnlyDictionary<int, string?> values)
    {
        var category = await _categoryRepository.GetByIdWithFieldsAsync(categoryId);
        if (category is null) return new();
        return category.Fields
            .OrderBy(f => f.SortOrder)
            .Select(f => new DynamicFieldInput
            {
                FieldId = f.Id,
                DisplayLabel = f.DisplayLabel,
                DataType = (int)f.DataType,
                IsRequired = f.IsRequired,
                Value = values.TryGetValue(f.Id, out var v) ? v : null,
            })
            .ToList();
    }

    private async Task<int> GetCurrentApplicantIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var applicant = await _dbContext.Applicants
            .FirstOrDefaultAsync(a => a.UserId == userId);

        return applicant?.Id ?? throw new InvalidOperationException("Applicant not found for current user.");
    }

    private async Task VerifyOwnershipAsync(int appId)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(appId);

        if (application is null || application.ApplicantId != applicantId)
        {
            throw new UnauthorizedAccessException("You do not own this application.");
        }
    }
}
