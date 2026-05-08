using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.DTOs;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Localization;
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
    private readonly IUserFacingErrorTranslator _errorTranslator;

    public SupplierController(
        ApplicationService applicationService,
        SupplierCatalogService supplierCatalogService,
        ISupplierRepository supplierRepository,
        AppDbContext dbContext,
        IOptions<AdminReportsOptions> adminReportsOptions,
        IUserFacingErrorTranslator errorTranslator)
    {
        _applicationService = applicationService;
        _supplierCatalogService = supplierCatalogService;
        _supplierRepository = supplierRepository;
        _dbContext = dbContext;
        _adminReportsOptions = adminReportsOptions;
        _errorTranslator = errorTranslator;
    }

    [HttpGet("Add")]
    public async Task<IActionResult> Add(int appId, int itemId, int? supplierId, string? banner)
    {
        await VerifyOwnershipAsync(appId);

        var enabledCurrencies = await LoadEnabledCurrenciesAsync();

        // Spec 015 / FR-014 — default to CRC, the seeded base currency. The previous
        // AdminReports.DefaultCurrency code-path is kept as a fallback for envs that
        // configure a non-CRC default (e.g., legacy COP-only deployments).
        var defaultCurrency = enabledCurrencies.Any(c => c.Code == CurrencyCode.Crc.Value)
            ? CurrencyCode.Crc.Value
            : (_adminReportsOptions.Value.DefaultCurrency ?? string.Empty).ToUpperInvariant();

        var viewModel = new AddSupplierViewModel
        {
            ApplicationId = appId,
            ItemId = itemId,
            Currency = defaultCurrency,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            EnabledCurrencies = enabledCurrencies,
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
            return await RenderAddAsync(model);
        }

        // Spec 015 — server-side defense for tampered POSTs. The form renders a
        // <select> populated only with enabled currencies, but nothing prevents a
        // hand-crafted POST from sending an arbitrary string. Reject anything that
        // is not a valid 3-letter ISO code or that is not in the enabled catalog.
        CurrencyCode parsedCurrency;
        try
        {
            parsedCurrency = CurrencyCode.From(model.Currency);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(model.Currency),
                "La moneda seleccionada no es válida.");
            return await RenderAddAsync(model);
        }

        var currencyIsEnabled = await _dbContext.Currencies
            .AnyAsync(c => c.Code == parsedCurrency && c.IsEnabled);
        if (!currencyIsEnabled)
        {
            ModelState.AddModelError(nameof(model.Currency),
                $"La moneda '{model.Currency}' no está configurada o está deshabilitada.");
            return await RenderAddAsync(model);
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
                    return await RenderAddAsync(model);
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
                    return await RenderAddAsync(model);
                }

                if (model.LookupResult.Supplier.VerificationStatus == SupplierVerificationStatus.Rejected)
                {
                    ModelState.AddModelError(string.Empty, SuppliersResources.LookupRejectedMessage);
                    return await RenderAddAsync(model);
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
                    return await RenderAddAsync(model);
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
            return await RenderAddAsync(model);
        }
        catch (MissingRateException)
        {
            // Spec 015 / FR-018 — the configured currency has no published rate.
            // Surface the literal Spanish message inline on the Currency field so
            // the applicant sees the same copy whether the error came from the
            // AJAX preview or this save POST itself.
            ModelState.AddModelError(
                nameof(model.Currency),
                _errorTranslator.Translate(UserFacingErrorCode.MissingExchangeRate));
            return await RenderAddAsync(model);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RenderAddAsync(model);
        }
        catch (UnauthorizedAccessException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RenderAddAsync(model);
        }
    }

    private async Task<IActionResult> RenderAddAsync(AddSupplierViewModel model)
    {
        // Spec 015 — repopulate the enabled-currencies catalog on every validation
        // re-render so the <select> never collapses to an empty dropdown.
        model.EnabledCurrencies = await LoadEnabledCurrenciesAsync();
        return View(model);
    }

    private async Task<IReadOnlyList<CurrencyOption>> LoadEnabledCurrenciesAsync()
    {
        return await _dbContext.Currencies
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CurrencyOption(c.Code.Value, c.DisplayName, c.Symbol))
            .ToListAsync();
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
