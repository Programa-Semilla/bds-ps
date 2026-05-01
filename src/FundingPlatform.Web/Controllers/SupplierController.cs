using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.DTOs;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Applicant")]
[Route("Application/{appId}/Item/{itemId}/Supplier")]
public class SupplierController : Controller
{
    private readonly ApplicationService _applicationService;
    private readonly SupplierCatalogService _supplierCatalogService;
    private readonly ISupplierRepository _supplierRepository;
    private readonly AppDbContext _dbContext;
    private readonly IOptions<AdminReportsOptions> _adminReportsOptions;

    public SupplierController(
        ApplicationService applicationService,
        SupplierCatalogService supplierCatalogService,
        ISupplierRepository supplierRepository,
        AppDbContext dbContext,
        IOptions<AdminReportsOptions> adminReportsOptions)
    {
        _applicationService = applicationService;
        _supplierCatalogService = supplierCatalogService;
        _supplierRepository = supplierRepository;
        _dbContext = dbContext;
        _adminReportsOptions = adminReportsOptions;
    }

    [HttpGet("Add")]
    public async Task<IActionResult> Add(int appId, int itemId, int? supplierId, string? banner)
    {
        await VerifyOwnershipAsync(appId);

        var viewModel = new AddSupplierViewModel
        {
            ApplicationId = appId,
            ItemId = itemId,
            Currency = (_adminReportsOptions.Value.DefaultCurrency ?? string.Empty).ToUpperInvariant(),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
        };

        // R4: redirect-to-existing recovery — pre-load the supplier and show the
        // "concurrent" banner so the applicant can pick a branch or add a new one.
        if (supplierId is int sid)
        {
            var applicantId = await GetCurrentApplicantIdAsync();
            var supplier = await _supplierRepository.GetByIdWithBranchesAsync(sid);
            if (supplier is not null)
            {
                viewModel.SupplierLegalId = supplier.LegalId;
                viewModel.LookupResult = await _supplierCatalogService.SearchByLegalIdAsync(
                    supplier.LegalId, applicantId);
            }
            viewModel.ShowConcurrentBanner = string.Equals(banner, "concurrent", StringComparison.OrdinalIgnoreCase);
        }

        return View(viewModel);
    }

    /// <summary>
    /// Spec 013 (US1): server-rendered HTML partial for the legal-ID lookup. The
    /// Add page's vanilla-JS debounce hook (250ms) fetches this URL and replaces
    /// the lookup-result region in the DOM.
    /// </summary>
    [HttpGet("Search")]
    public async Task<IActionResult> Search(int appId, int itemId, string? legalId)
    {
        await VerifyOwnershipAsync(appId);

        if (string.IsNullOrWhiteSpace(legalId))
        {
            return BadRequest();
        }

        var applicantId = await GetCurrentApplicantIdAsync();
        var result = await _supplierCatalogService.SearchByLegalIdAsync(legalId, applicantId);

        return result.Outcome switch
        {
            SupplierLookupOutcome.Hit => PartialView("_LookupHit", result.Supplier!),
            SupplierLookupOutcome.Rejected => PartialView("_LookupRejected"),
            _ => PartialView("_LookupEmpty",
                new NewSupplierInputViewModel
                {
                    Name = string.Empty,
                    FirstBranch = new AddBranchInputViewModel
                    {
                        BranchName = SuppliersResources.Branch_Default,
                    },
                }),
        };
    }

    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.ApplicationAttachment)]
    public async Task<IActionResult> Add(int appId, int itemId, AddSupplierViewModel model)
    {
        await VerifyOwnershipAsync(appId);
        model.ApplicationId = appId;
        model.ItemId = itemId;

        if (model.QuotationFile is null || model.QuotationFile.Length == 0)
        {
            ModelState.AddModelError(nameof(model.QuotationFile), "Se requiere el archivo de la cotización.");
            return View(model);
        }

        // Re-run lookup so the view can re-render the right partial on validation error.
        var applicantId = await GetCurrentApplicantIdAsync();
        if (!string.IsNullOrWhiteSpace(model.SupplierLegalId))
        {
            model.LookupResult = await _supplierCatalogService.SearchByLegalIdAsync(
                model.SupplierLegalId, applicantId);
        }

        try
        {
            // Branch dispatch (mutually exclusive paths).
            if (model.SelectedBranchId.HasValue && model.LookupResult?.Supplier is not null)
            {
                // US1: existing branch reuse.
                if (model.LookupResult.Supplier.VerificationStatus == SupplierVerificationStatus.Rejected)
                {
                    ModelState.AddModelError(string.Empty, SuppliersResources.LookupRejectedMessage);
                    return View(model);
                }

                using var stream = model.QuotationFile.OpenReadStream();
                await _applicationService.AddQuotationToExistingBranchAsync(
                    appId, itemId,
                    model.LookupResult.Supplier.Id,
                    model.SelectedBranchId.Value,
                    model.Price, model.Currency, model.ValidUntil,
                    stream, model.QuotationFile.FileName,
                    model.QuotationFile.ContentType, model.QuotationFile.Length);

                TempData["SuccessMessage"] = "Cotización agregada con éxito.";
                return RedirectToAction("Details", "Application", new { id = appId });
            }

            if (model.NewBranch is not null && model.LookupResult?.Supplier is not null)
            {
                // US2: add a new branch under the existing supplier, then quote against it.
                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (model.LookupResult.Supplier.VerificationStatus == SupplierVerificationStatus.Rejected)
                {
                    ModelState.AddModelError(string.Empty, SuppliersResources.LookupRejectedMessage);
                    return View(model);
                }

                var newBranchId = await _supplierCatalogService.AddBranchUnderExistingSupplierAsync(
                    model.LookupResult.Supplier.Id,
                    new AddBranchInput
                    {
                        BranchName = model.NewBranch.BranchName,
                        ContactName = model.NewBranch.ContactName,
                        Email = model.NewBranch.Email,
                        Phone = model.NewBranch.Phone,
                        AddressLine = model.NewBranch.AddressLine,
                        Province = model.NewBranch.Province,
                        ShippingDetails = model.NewBranch.ShippingDetails,
                        WarrantyInfo = model.NewBranch.WarrantyInfo,
                    },
                    applicantId);

                using var stream = model.QuotationFile.OpenReadStream();
                await _applicationService.AddQuotationToExistingBranchAsync(
                    appId, itemId,
                    model.LookupResult.Supplier.Id,
                    newBranchId,
                    model.Price, model.Currency, model.ValidUntil,
                    stream, model.QuotationFile.FileName,
                    model.QuotationFile.ContentType, model.QuotationFile.Length);

                TempData["SuccessMessage"] = "Sucursal y cotización agregadas con éxito.";
                return RedirectToAction("Details", "Application", new { id = appId });
            }

            if (model.NewSupplier is not null)
            {
                // US3: brand-new Draft supplier.
                if (string.IsNullOrWhiteSpace(model.NewSupplier.Name)
                    || string.IsNullOrWhiteSpace(model.NewSupplier.FirstBranch?.BranchName))
                {
                    ModelState.AddModelError(string.Empty, "Completa la información del nuevo proveedor.");
                    return View(model);
                }

                var firstBranch = new AddBranchInput
                {
                    BranchName = model.NewSupplier.FirstBranch.BranchName,
                    ContactName = model.NewSupplier.FirstBranch.ContactName,
                    Email = model.NewSupplier.FirstBranch.Email,
                    Phone = model.NewSupplier.FirstBranch.Phone,
                    AddressLine = model.NewSupplier.FirstBranch.AddressLine,
                    Province = model.NewSupplier.FirstBranch.Province,
                    ShippingDetails = model.NewSupplier.FirstBranch.ShippingDetails,
                    WarrantyInfo = model.NewSupplier.FirstBranch.WarrantyInfo,
                };

                var result = await _supplierCatalogService.CreateDraftWithBranchAsync(
                    model.SupplierLegalId, model.NewSupplier.Name, firstBranch, applicantId);

                if (result.Outcome == CreateDraftOutcome.RetryWithExisting)
                {
                    return RedirectToAction(nameof(Add), new { appId, itemId, supplierId = result.SupplierId, banner = "concurrent" });
                }

                // Load the created supplier and pluck its default branch.
                var newSupplier = await _supplierRepository.GetByIdWithBranchesAsync(result.SupplierId)
                    ?? throw new InvalidOperationException($"Newly created supplier {result.SupplierId} not loadable.");
                var defaultBranch = newSupplier.Branches.First(b => b.IsDefault);

                using var stream = model.QuotationFile.OpenReadStream();
                await _applicationService.AddQuotationToExistingBranchAsync(
                    appId, itemId,
                    newSupplier.Id, defaultBranch.Id,
                    model.Price, model.Currency, model.ValidUntil,
                    stream, model.QuotationFile.FileName,
                    model.QuotationFile.ContentType, model.QuotationFile.Length);

                TempData["SuccessMessage"] = "Proveedor y cotización agregados con éxito.";
                return RedirectToAction("Details", "Application", new { id = appId });
            }

            ModelState.AddModelError(string.Empty, "Selecciona una sucursal, agrega una nueva, o crea un proveedor nuevo.");
            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
        catch (UnauthorizedAccessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    // Spec 013 US3 acceptance scenario 3 ("applicant returns to edit own Draft") is
    // delivered today by re-running the Add flow on the application's items page —
    // applicants edit their Draft supplier inline before submission. Dedicated
    // EditDraft/EditBranch endpoints are intentionally NOT exposed in v1: they would
    // require their own GET surfaces, views, and link affordances, none of which are
    // built. The domain methods Supplier.RenameByApplicant and Supplier.EditBranch
    // remain available to drive a future dedicated edit screen without controller
    // changes. (Deep-review fix: removed unreachable POST handlers that crashed on
    // ModelState validation failure due to missing view files.)

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
