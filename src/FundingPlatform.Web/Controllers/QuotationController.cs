using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Applicant")]
[Route("Application/{appId}/Item/{itemId}/Quotation")]
public class QuotationController : Controller
{
    private readonly ApplicationService _applicationService;
    private readonly SupplierCatalogService _supplierCatalogService;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ISystemConfigurationRepository _systemConfigurationRepository;
    private readonly AppDbContext _dbContext;
    private readonly IOptions<AdminReportsOptions> _adminReportsOptions;
    private readonly IConversionService _conversionService;
    private readonly IUserFacingErrorTranslator _errorTranslator;

    private const string QuotationFileRequiredMessage = "Se requiere el archivo de la cotización.";

    public QuotationController(
        ApplicationService applicationService,
        SupplierCatalogService supplierCatalogService,
        ISupplierRepository supplierRepository,
        ISystemConfigurationRepository systemConfigurationRepository,
        AppDbContext dbContext,
        IOptions<AdminReportsOptions> adminReportsOptions,
        IConversionService conversionService,
        IUserFacingErrorTranslator errorTranslator)
    {
        _applicationService = applicationService;
        _supplierCatalogService = supplierCatalogService;
        _supplierRepository = supplierRepository;
        _systemConfigurationRepository = systemConfigurationRepository;
        _dbContext = dbContext;
        _adminReportsOptions = adminReportsOptions;
        _conversionService = conversionService;
        _errorTranslator = errorTranslator;
    }

    [HttpGet("Add")]
    public async Task<IActionResult> Add(int appId, int itemId, int supplierId, string supplierName)
    {
        await VerifyOwnershipAsync(appId);

        var enabled = await LoadEnabledCurrenciesAsync();

        var viewModel = new AddQuotationViewModel
        {
            ApplicationId = appId,
            ItemId = itemId,
            SupplierId = supplierId,
            SupplierName = supplierName,
            Currency = CurrencyCode.Crc.Value,  // Spec 015 / FR-014: default to CRC.
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            EnabledCurrencies = enabled
        };

        return View(viewModel);
    }

    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.ApplicationAttachment)]
    public async Task<IActionResult> Add(int appId, int itemId, AddQuotationViewModel model)
    {
        await VerifyOwnershipAsync(appId);

        if (!ModelState.IsValid)
        {
            return await RenderAddFormAsync(appId, itemId, model);
        }

        if (model.QuotationFile is null || model.QuotationFile.Length == 0)
        {
            ModelState.AddModelError(nameof(model.QuotationFile), QuotationFileRequiredMessage);
            return await RenderAddFormAsync(appId, itemId, model);
        }

        var validationError = await ValidateFileAsync(model.QuotationFile);
        if (validationError is not null)
        {
            ModelState.AddModelError(nameof(model.QuotationFile), validationError);
            return await RenderAddFormAsync(appId, itemId, model);
        }

        try
        {
            // Spec 013: existing-supplier quotation. Use the supplier's default branch
            // since this entry-point doesn't expose branch selection (callers wanting
            // branch choice go through SupplierController.Add).
            var supplier = await _supplierRepository.GetByIdWithBranchesAsync(model.SupplierId)
                ?? throw new InvalidOperationException($"Supplier {model.SupplierId} not found.");

            if (supplier.VerificationStatus == SupplierVerificationStatus.Rejected)
            {
                ModelState.AddModelError(string.Empty, "El proveedor está rechazado y no puede recibir nuevas cotizaciones.");
                return await RenderAddFormAsync(appId, itemId, model);
            }

            var defaultBranch = supplier.Branches.FirstOrDefault(b => b.IsDefault)
                ?? supplier.Branches.FirstOrDefault()
                ?? throw new InvalidOperationException($"Supplier {model.SupplierId} has no branches.");

            using var stream = model.QuotationFile.OpenReadStream();
            await _applicationService.AddQuotationToExistingBranchAsync(
                appId, itemId, supplier.Id, defaultBranch.Id,
                model.Price, model.Currency, model.ValidUntil,
                stream, model.QuotationFile.FileName,
                model.QuotationFile.ContentType, model.QuotationFile.Length);

            TempData["SuccessMessage"] = "Cotización agregada con éxito.";
            return RedirectToAction("Details", "Application", new { id = appId });
        }
        catch (MissingRateException)
        {
            // Spec 015 / FR-018 — surface the literal Spanish message inline so the
            // applicant sees the same copy whether the error came from the AJAX
            // preview or the save POST itself.
            ModelState.AddModelError(
                nameof(model.Currency),
                _errorTranslator.Translate(UserFacingErrorCode.MissingExchangeRate));
            return await RenderAddFormAsync(appId, itemId, model);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RenderAddFormAsync(appId, itemId, model);
        }
    }

    /// <summary>
    /// Spec 015 / contract <c>conversion-preview-api.md</c> — server-computed
    /// conversion preview. Called from <c>quote-conversion-preview.js</c> on
    /// currency-or-amount blur. The client never multiplies locally (FR-019).
    /// </summary>
    [HttpPost("Convert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(
        int appId, int itemId, [FromBody] ConversionPreviewRequestModel req,
        CancellationToken ct)
    {
        await VerifyOwnershipAsync(appId);

        if (req is null || string.IsNullOrWhiteSpace(req.CurrencyCode))
        {
            return BadRequest(new { error = "Falta el código de moneda." });
        }
        if (req.Amount <= 0m)
        {
            return BadRequest(new { error = "El monto debe ser mayor a cero." });
        }

        CurrencyCode parsed;
        try
        {
            parsed = CurrencyCode.From(req.CurrencyCode);
        }
        catch (ArgumentException)
        {
            return NotFound(new { error = $"La moneda '{req.CurrencyCode}' no está configurada." });
        }

        // CRC short-circuit per the contract: { isCrc: true, amount: <input> }.
        if (parsed.IsBase)
        {
            return Ok(new ConversionPreviewDto(
                IsCrc: true,
                Amount: req.Amount,
                OriginalCurrencyCode: null,
                OriginalAmount: null,
                ConvertedCrcAmount: null,
                Rate: null));
        }

        var currency = await _dbContext.Currencies
            .FirstOrDefaultAsync(c => c.Code == parsed, ct);
        if (currency is null)
        {
            return NotFound(new { error = $"La moneda '{parsed}' no está configurada." });
        }
        if (!currency.IsEnabled)
        {
            return BadRequest(new { error = $"La moneda '{parsed}' está deshabilitada." });
        }

        try
        {
            var result = await _conversionService.ConvertAsync(
                parsed, CurrencyCode.Crc, req.Amount, ct);

            return Ok(new ConversionPreviewDto(
                IsCrc: false,
                Amount: req.Amount,
                OriginalCurrencyCode: parsed.Value,
                OriginalAmount: req.Amount,
                ConvertedCrcAmount: result.Converted,
                Rate: new ConversionPreviewRateDto(
                    RateRecordId: result.Snapshot.RateRecordId,
                    RateValue: result.Snapshot.RateValue,
                    RateType: result.Snapshot.RateType.ToString(),
                    EffectiveAtUtc: result.Snapshot.EffectiveAtUtc)));
        }
        catch (MissingRateException)
        {
            return Conflict(new
            {
                error = _errorTranslator.Translate(UserFacingErrorCode.MissingExchangeRate)
            });
        }
    }

    private async Task<IActionResult> RenderAddFormAsync(int appId, int itemId, AddQuotationViewModel model)
    {
        model.ApplicationId = appId;
        model.ItemId = itemId;
        model.EnabledCurrencies = await LoadEnabledCurrenciesAsync();
        return View("Add", model);
    }

    private async Task<IReadOnlyList<CurrencyOption>> LoadEnabledCurrenciesAsync()
    {
        // Spec 015 — currencies dropdown source. Phase 5 will introduce
        // ICurrencyConfigService.ListEnabledAsync; for the US1 slice we read the
        // catalog directly so the form works as soon as the seed inserts CRC + USD.
        var enabled = await _dbContext.Currencies
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CurrencyOption(c.Code.Value, c.DisplayName, c.Symbol))
            .ToListAsync();
        return enabled;
    }

    [HttpPost("{quotationId}/Replace")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.ApplicationAttachment)]
    public async Task<IActionResult> Replace(int appId, int itemId, int quotationId, IFormFile quotationFile)
    {
        await VerifyOwnershipAsync(appId);

        if (quotationFile is null || quotationFile.Length == 0)
        {
            TempData["ErrorMessage"] = QuotationFileRequiredMessage;
            return RedirectToAction("Details", "Application", new { id = appId });
        }

        var validationError = await ValidateFileAsync(quotationFile);
        if (validationError is not null)
        {
            TempData["ErrorMessage"] = validationError;
            return RedirectToAction("Details", "Application", new { id = appId });
        }

        var command = new ReplaceQuotationDocumentCommand
        {
            ApplicationId = appId,
            ItemId = itemId,
            QuotationId = quotationId,
            FileName = quotationFile.FileName,
            FileContentType = quotationFile.ContentType,
            FileSize = quotationFile.Length
        };

        using var stream = quotationFile.OpenReadStream();
        await _applicationService.ReplaceQuotationDocumentAsync(command, stream);

        TempData["SuccessMessage"] = "Documento de cotización reemplazado con éxito.";
        return RedirectToAction("Details", "Application", new { id = appId });
    }

    [HttpPost("{quotationId}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int appId, int itemId, int quotationId)
    {
        await VerifyOwnershipAsync(appId);

        await _applicationService.RemoveQuotationAsync(appId, itemId, quotationId);

        TempData["SuccessMessage"] = "Cotización eliminada con éxito.";
        return RedirectToAction("Details", "Application", new { id = appId });
    }

    private async Task<string?> ValidateFileAsync(IFormFile file)
    {
        var allowedTypesConfig = await _systemConfigurationRepository.GetByKeyAsync("AllowedFileTypes");
        var maxSizeConfig = await _systemConfigurationRepository.GetByKeyAsync("MaxFileSizeMB");

        if (allowedTypesConfig is not null)
        {
            var allowedTypes = allowedTypesConfig.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (allowedTypes.Length > 0 && !allowedTypes.Contains(extension))
            {
                return $"El tipo de archivo '{extension}' no está permitido. Tipos permitidos: {string.Join(", ", allowedTypes)}.";
            }
        }

        if (maxSizeConfig is not null && decimal.TryParse(maxSizeConfig.Value, out var maxSizeMb))
        {
            var maxSizeBytes = (long)(maxSizeMb * 1024 * 1024);
            if (file.Length > maxSizeBytes)
            {
                return $"El tamaño del archivo excede el máximo permitido de {maxSizeMb} MB.";
            }
        }

        return null;
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
