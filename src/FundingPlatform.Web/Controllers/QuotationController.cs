using System.Security.Claims;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
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

    private const string QuotationFileRequiredMessage = "Se requiere el archivo de la cotización.";

    public QuotationController(
        ApplicationService applicationService,
        SupplierCatalogService supplierCatalogService,
        ISupplierRepository supplierRepository,
        ISystemConfigurationRepository systemConfigurationRepository,
        AppDbContext dbContext,
        IOptions<AdminReportsOptions> adminReportsOptions)
    {
        _applicationService = applicationService;
        _supplierCatalogService = supplierCatalogService;
        _supplierRepository = supplierRepository;
        _systemConfigurationRepository = systemConfigurationRepository;
        _dbContext = dbContext;
        _adminReportsOptions = adminReportsOptions;
    }

    [HttpGet("Add")]
    public async Task<IActionResult> Add(int appId, int itemId, int supplierId, string supplierName)
    {
        await VerifyOwnershipAsync(appId);

        var viewModel = new AddQuotationViewModel
        {
            ApplicationId = appId,
            ItemId = itemId,
            SupplierId = supplierId,
            SupplierName = supplierName,
            Currency = (_adminReportsOptions.Value.DefaultCurrency ?? string.Empty).ToUpperInvariant(),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3))
        };

        return View(viewModel);
    }

    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int appId, int itemId, AddQuotationViewModel model)
    {
        await VerifyOwnershipAsync(appId);

        if (!ModelState.IsValid)
        {
            model.ApplicationId = appId;
            model.ItemId = itemId;
            return View(model);
        }

        if (model.QuotationFile is null || model.QuotationFile.Length == 0)
        {
            ModelState.AddModelError(nameof(model.QuotationFile), QuotationFileRequiredMessage);
            model.ApplicationId = appId;
            model.ItemId = itemId;
            return View(model);
        }

        var validationError = await ValidateFileAsync(model.QuotationFile);
        if (validationError is not null)
        {
            ModelState.AddModelError(nameof(model.QuotationFile), validationError);
            model.ApplicationId = appId;
            model.ItemId = itemId;
            return View(model);
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
                model.ApplicationId = appId;
                model.ItemId = itemId;
                return View(model);
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
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.ApplicationId = appId;
            model.ItemId = itemId;
            return View(model);
        }
    }

    [HttpPost("{quotationId}/Replace")]
    [ValidateAntiForgeryToken]
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
