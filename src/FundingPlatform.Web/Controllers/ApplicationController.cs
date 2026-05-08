using System.Security.Claims;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Services;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Applicant")]
public class ApplicationController : Controller
{
    private readonly ApplicationService _applicationService;
    private readonly AppDbContext _dbContext;

    public ApplicationController(
        ApplicationService applicationService,
        AppDbContext dbContext)
    {
        _applicationService = applicationService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var applications = await _applicationService.GetApplicationsForApplicantAsync(applicantId);

        var viewModel = new ApplicationListViewModel
        {
            Applications = applications.Select(a => new ApplicationListItemViewModel
            {
                Id = a.Id,
                State = a.State.ToString(),
                ItemCount = a.ItemCount,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                SubmittedAt = a.SubmittedAt
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateApplicationViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateApplicationViewModel model)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var command = new CreateApplicationCommand(applicantId);
        var applicationId = await _applicationService.CreateApplicationAsync(command, userId);

        TempData["SuccessMessage"] = "Solicitud creada con éxito.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(id);

        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }

        var viewModel = MapToViewModel(application);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(id);

        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }

        var viewModel = MapToViewModel(application);
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Application/{id}/Submit")]
    public async Task<IActionResult> Submit(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(id);

        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var command = new SubmitApplicationCommand(id);
        var errors = await _applicationService.SubmitApplicationAsync(command, userId);

        if (errors.Count > 0)
        {
            TempData["ValidationErrors"] = System.Text.Json.JsonSerializer.Serialize(errors);
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = "Solicitud enviada con éxito.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<int> GetCurrentApplicantIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var applicant = await _dbContext.Applicants
            .FirstOrDefaultAsync(a => a.UserId == userId);

        return applicant?.Id ?? throw new InvalidOperationException("Applicant not found for current user.");
    }

    private static ApplicationViewModel MapToViewModel(FundingPlatform.Application.DTOs.ApplicationDto dto)
    {
        var vm = new ApplicationViewModel
        {
            Id = dto.Id,
            State = dto.State.ToString(),
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            SubmittedAt = dto.SubmittedAt,
            Items = dto.Items.Select(i => new ItemViewModel
            {
                Id = i.Id,
                ProductName = i.ProductName,
                CategoryName = i.CategoryName,
                QuotationCount = i.Quotations.Count,
                HasImpact = i.Impact is not null,
                ReviewComment = i.ReviewComment,
                SelectedSupplierId = i.SelectedSupplierId,
                Quotations = i.Quotations.Select(q => new QuotationSummaryViewModel
                {
                    Id = q.Id,
                    SupplierName = q.SupplierName,
                    Price = q.Price,
                    Currency = q.Currency,
                    ConvertedCrcAmount = q.ConvertedCrcAmount,
                    SnapshotRateValue = q.SnapshotRateValue,
                    SnapshotRateType = q.SnapshotRateType,
                    SnapshotEffectiveAtUtc = q.SnapshotEffectiveAtUtc,
                    LegacyNeedsReview = q.LegacyNeedsReview
                }).ToList()
            }).ToList()
        };

        // Spec 015 / T413 — application-summary total computed in CRC across all
        // Items by picking the selected-supplier quotation per Item, excluding
        // legacy-flagged rows. Items without a selected supplier are skipped (their
        // total is undetermined until a reviewer decides).
        decimal? total = null;
        var hasLegacy = false;
        foreach (var item in vm.Items)
        {
            if (item.SelectedSupplierId is null) continue;
            var chosen = item.Quotations
                .FirstOrDefault(q => GetSupplierIdForQuotation(dto, item.Id, q.Id) == item.SelectedSupplierId);
            if (chosen is null) continue;
            if (chosen.LegacyNeedsReview)
            {
                hasLegacy = true;
                continue;
            }
            if (chosen.ConvertedCrcAmount.HasValue)
            {
                total = (total ?? 0m) + chosen.ConvertedCrcAmount.Value;
            }
        }
        vm.TotalConvertedCrc = total;
        vm.HasLegacyNeedsReview = hasLegacy;
        return vm;
    }

    /// <summary>
    /// Resolves the underlying supplier id for a quotation summary view-model row.
    /// The summary view-model intentionally does not carry SupplierId (the original
    /// design only needed display fields); when computing the application total
    /// we look it up from the source DTO once.
    /// </summary>
    private static int? GetSupplierIdForQuotation(
        FundingPlatform.Application.DTOs.ApplicationDto dto,
        int itemId,
        int quotationId)
    {
        var item = dto.Items.FirstOrDefault(i => i.Id == itemId);
        var q = item?.Quotations.FirstOrDefault(qq => qq.Id == quotationId);
        return q?.SupplierId;
    }
}
