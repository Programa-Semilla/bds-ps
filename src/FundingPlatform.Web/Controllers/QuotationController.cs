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

    /// <summary>
    /// Spec 023 / FR-001 / FR-002 — render the per-quotation Edit form. Pre-populates
    /// Price, Currency, ValidUntil, SupplierBranchId. The branch picker lists only
    /// branches of the quotation's current Supplier (FR-004). The form rejects
    /// editing on out-of-state applications (FR-008) and legacy-flagged quotations
    /// (FR-011) by redirecting back to Application/Edit with TempData["ErrorMessage"].
    /// </summary>
    [HttpGet("{quotationId}/Edit")]
    public async Task<IActionResult> Edit(int appId, int itemId, int quotationId)
    {
        await VerifyOwnershipAsync(appId);

        var dto = await _applicationService.GetQuotationForEditAsync(appId, itemId, quotationId);
        if (dto is null)
        {
            return NotFound();
        }

        // FR-008 — state gate at GET time. Redirect back so the applicant sees
        // the surface that actually owns the affordance (Application/Edit) and
        // an es-CR explanation.
        if (dto.ApplicationState != FundingPlatform.Domain.Enums.ApplicationState.Draft)
        {
            TempData["ErrorMessage"] =
                "El estado de la solicitud cambió, recarga la página.";
            return RedirectToAction("Edit", "Application", new { id = appId });
        }

        // FR-011 — legacy flagged quotations route through the admin-only path.
        if (dto.LegacyNeedsReview)
        {
            TempData["ErrorMessage"] =
                "Esta cotización está marcada para revisión administrativa de tipo de cambio.";
            return RedirectToAction("Edit", "Application", new { id = appId });
        }

        var vm = new EditQuotationViewModel
        {
            ApplicationId = dto.ApplicationId,
            ItemId = dto.ItemId,
            QuotationId = dto.QuotationId,
            Price = dto.Price,
            Currency = dto.Currency,
            ValidUntil = dto.ValidUntil,
            SupplierBranchId = dto.SupplierBranchId,
            SupplierName = dto.SupplierName,
            EnabledCurrencies = await LoadEnabledCurrenciesAsync(),
            BranchOptions = dto.Branches
                .Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.BranchName,
                    Selected = b.Id == dto.SupplierBranchId,
                })
                .ToList(),
        };

        return View(vm);
    }

    /// <summary>
    /// Spec 023 — save edits. Dispatches on <see cref="EditQuotationOutcome"/>
    /// per <c>contracts/quotation-edit-endpoint.md</c>. Field-level errors are
    /// surfaced via <c>ModelState</c> so the partial re-renders with every
    /// error visible on the same round-trip (R0.5).
    /// </summary>
    [HttpPost("{quotationId}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int appId, int itemId, int quotationId,
        EditQuotationViewModel vm,
        CancellationToken ct)
    {
        await VerifyOwnershipAsync(appId);

        // Anchor the route params back onto the VM so re-renders carry them through.
        vm.ApplicationId = appId;
        vm.ItemId = itemId;
        vm.QuotationId = quotationId;

        var applicantId = await GetCurrentApplicantIdAsync();

        var result = await _applicationService.EditQuotationAsync(new EditQuotationCommand
        {
            ApplicationId = appId,
            ItemId = itemId,
            QuotationId = quotationId,
            Price = vm.Price,
            Currency = vm.Currency,
            ValidUntil = vm.ValidUntil,
            SupplierBranchId = vm.SupplierBranchId,
            ApplicantId = applicantId,
        }, ct);

        switch (result.Outcome)
        {
            case EditQuotationOutcome.Success:
                TempData["SuccessMessage"] = "Cotización actualizada con éxito.";
                return RedirectToAction("Edit", "Application", new { id = appId });

            case EditQuotationOutcome.NotFound:
                return NotFound();

            case EditQuotationOutcome.Forbidden:
                return Forbid();

            case EditQuotationOutcome.StateChanged:
                TempData["ErrorMessage"] = result.GlobalError;
                return RedirectToAction("Edit", "Application", new { id = appId });

            case EditQuotationOutcome.LegacyFlagged:
                TempData["ErrorMessage"] = result.GlobalError;
                return RedirectToAction("Edit", "Application", new { id = appId });

            case EditQuotationOutcome.MissingRate:
                ModelState.AddModelError(string.Empty,
                    result.GlobalError ?? _errorTranslator.Translate(UserFacingErrorCode.MissingExchangeRate));
                await PopulateLookupsAsync(vm);
                Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                return View(vm);

            case EditQuotationOutcome.ValidationFailed:
                if (result.FieldErrors is not null)
                {
                    foreach (var kvp in result.FieldErrors)
                    {
                        ModelState.AddModelError(kvp.Key, kvp.Value);
                    }
                }
                await PopulateLookupsAsync(vm);
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return View(vm);

            default:
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task PopulateLookupsAsync(EditQuotationViewModel vm)
    {
        vm.EnabledCurrencies = await LoadEnabledCurrenciesAsync();

        // Reload the supplier name + branch options from the read projection so
        // the re-rendered form keeps the branch <select> populated (the POST
        // payload only carries the SupplierBranchId, not the option list).
        var dto = await _applicationService.GetQuotationForEditAsync(
            vm.ApplicationId, vm.ItemId, vm.QuotationId);
        if (dto is not null)
        {
            vm.SupplierName = dto.SupplierName;
            vm.BranchOptions = dto.Branches
                .Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.BranchName,
                    Selected = b.Id == vm.SupplierBranchId,
                })
                .ToList();
        }
    }

    private async Task<IReadOnlyList<CurrencyOption>> LoadEnabledCurrenciesAsync()
    {
        return await _dbContext.Currencies
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CurrencyOption(c.Code.Value, c.DisplayName, c.Symbol))
            .ToListAsync();
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
            return RedirectToAction("Edit", "Application", new { id = appId });
        }

        var validationError = await ValidateFileAsync(quotationFile);
        if (validationError is not null)
        {
            TempData["ErrorMessage"] = validationError;
            return RedirectToAction("Edit", "Application", new { id = appId });
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
        return RedirectToAction("Edit", "Application", new { id = appId });
    }

    [HttpPost("{quotationId}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int appId, int itemId, int quotationId)
    {
        await VerifyOwnershipAsync(appId);

        await _applicationService.RemoveQuotationAsync(appId, itemId, quotationId);

        TempData["SuccessMessage"] = "Cotización eliminada con éxito.";
        return RedirectToAction("Edit", "Application", new { id = appId });
    }

    /// <summary>
    /// Spec 023 / FR-013 (evolution 2026-05-20) — applicant downloads the PDF
    /// attached to one of their quotations. Available at any time regardless of
    /// Application state. Owner-scoped; non-owners receive HTTP 403 via the
    /// existing <see cref="VerifyOwnershipAsync"/> guard. Storage resolution
    /// reuses the spec-014 <see cref="IObjectStorage"/> rails so signed-URL
    /// hosting and backend-stream fallback behave identically to the reviewer
    /// download path on <c>ReviewController</c>.
    /// </summary>
    [HttpGet("{quotationId}/Download")]
    public async Task<IActionResult> Download(
        int appId, int itemId, int quotationId, CancellationToken ct)
    {
        await VerifyOwnershipAsync(appId);

        var quotation = await _dbContext.Quotations
            .Include(q => q.Document)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.ItemId == itemId, ct);
        if (quotation is null || quotation.Document is null
            || string.IsNullOrEmpty(quotation.Document.BlobKey))
        {
            return NotFound();
        }

        // Spec 023 / FR-013 (evolution) — force BackendStream so the response
        // carries `Content-Disposition: attachment; filename=...` and the
        // browser saves the file to disk instead of rendering the PDF inline.
        // Signed-URL hosting cannot set the attachment disposition on the
        // remote response, so we proxy the stream through the server for the
        // download path. Inline preview is intentionally not exposed.
        var storage = HttpContext.RequestServices.GetRequiredService<IObjectStorage>();
        var key = ObjectKey.Parse(quotation.Document.BlobKey);
        var handle = await storage.ResolveServingHandleAsync(
            FileCategory.ApplicationAttachment,
            key,
            ServingMode.BackendStream,
            ct);

        if (handle is BackendStreamHandle stream)
        {
            return File(
                stream.Content,
                stream.ContentType ?? "application/octet-stream",
                quotation.Document.OriginalFileName);
        }
        return NotFound();
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
