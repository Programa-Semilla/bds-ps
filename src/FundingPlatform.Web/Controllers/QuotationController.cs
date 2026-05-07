using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Applicant")]
[Route("Application/{appId}/Item/{itemId}/Quotation")]
public class QuotationController : Controller
{
    private readonly ApplicationService _applicationService;
    private readonly ISystemConfigurationRepository _systemConfigurationRepository;
    private readonly AppDbContext _dbContext;
    private readonly IConversionService _conversionService;
    private readonly IUserFacingErrorTranslator _errorTranslator;

    private const string QuotationFileRequiredMessage = "Se requiere el archivo de la cotización.";

    public QuotationController(
        ApplicationService applicationService,
        ISystemConfigurationRepository systemConfigurationRepository,
        AppDbContext dbContext,
        IConversionService conversionService,
        IUserFacingErrorTranslator errorTranslator)
    {
        _applicationService = applicationService;
        _systemConfigurationRepository = systemConfigurationRepository;
        _dbContext = dbContext;
        _conversionService = conversionService;
        _errorTranslator = errorTranslator;
    }

    // Spec 015 — the GET/POST "Add" endpoints that originally lived here were
    // never wired up to any user-facing surface. The applicant journey goes
    // through SupplierController.Add (legal-id lookup → branch picker → quote)
    // which now hosts the multi-currency dropdown and live conversion preview.
    // The Convert AJAX endpoint below is the only piece of QuotationController
    // that the UI calls into; Replace and Delete remain for the existing edit
    // affordances on Application/Details.

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
