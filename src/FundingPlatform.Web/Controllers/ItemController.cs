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

        var categories = await _categoryRepository.GetAllActiveAsync();
        var viewModel = new AddItemViewModel
        {
            ApplicationId = appId,
            Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int appId, AddItemViewModel model)
    {
        await VerifyOwnershipAsync(appId);

        if (!ModelState.IsValid)
        {
            var categories = await _categoryRepository.GetAllActiveAsync();
            model.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            }).ToList();
            model.ApplicationId = appId;
            return View(model);
        }

        var command = new AddItemCommand(
            appId,
            model.ProductName,
            model.CategoryId,
            model.TechnicalSpecifications);

        await _applicationService.AddItemAsync(command);

        TempData["SuccessMessage"] = "Ítem agregado con éxito.";
        return RedirectToAction("Edit", "Application", new { id = appId });
    }

    [HttpGet("{itemId}/Edit")]
    public async Task<IActionResult> Edit(int appId, int itemId)
    {
        await VerifyOwnershipAsync(appId);

        var application = await _applicationService.GetApplicationAsync(appId);
        if (application is null)
        {
            return NotFound();
        }

        var item = application.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return NotFound();
        }

        var categories = await _categoryRepository.GetAllActiveAsync();
        var viewModel = new EditItemViewModel
        {
            Id = item.Id,
            ApplicationId = appId,
            ProductName = item.ProductName,
            CategoryId = item.CategoryId,
            TechnicalSpecifications = item.TechnicalSpecifications,
            Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost("{itemId}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int appId, int itemId, EditItemViewModel model)
    {
        await VerifyOwnershipAsync(appId);

        if (!ModelState.IsValid)
        {
            var categories = await _categoryRepository.GetAllActiveAsync();
            model.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            }).ToList();
            model.ApplicationId = appId;
            model.Id = itemId;
            return View(model);
        }

        var command = new UpdateItemCommand(
            itemId,
            appId,
            model.ProductName,
            model.CategoryId,
            model.TechnicalSpecifications);

        await _applicationService.UpdateItemAsync(command);

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
