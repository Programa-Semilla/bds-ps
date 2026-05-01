using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

public class ApplicationService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IImpactTemplateRepository _impactTemplateRepository;
    private readonly ISystemConfigurationRepository _systemConfigurationRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly SupplierCatalogService _supplierCatalogService;
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(
        IApplicationRepository applicationRepository,
        ICategoryRepository categoryRepository,
        ISupplierRepository supplierRepository,
        IFileStorageService fileStorageService,
        IImpactTemplateRepository impactTemplateRepository,
        ISystemConfigurationRepository systemConfigurationRepository,
        IDocumentRepository documentRepository,
        SupplierCatalogService supplierCatalogService,
        ILogger<ApplicationService> logger)
    {
        _applicationRepository = applicationRepository;
        _categoryRepository = categoryRepository;
        _supplierRepository = supplierRepository;
        _fileStorageService = fileStorageService;
        _impactTemplateRepository = impactTemplateRepository;
        _systemConfigurationRepository = systemConfigurationRepository;
        _documentRepository = documentRepository;
        _supplierCatalogService = supplierCatalogService;
        _logger = logger;
    }

    public async Task<int> CreateApplicationAsync(CreateApplicationCommand cmd, string? userId = null)
    {
        var application = new AppEntity(cmd.ApplicantId);

        if (userId is not null)
        {
            application.AddVersionHistory(new VersionHistory(userId, "Created", "Application created"));
        }

        await _applicationRepository.AddAsync(application);
        await _applicationRepository.SaveChangesAsync();
        return application.Id;
    }

    /// <summary>
    /// Submits an application after validating it against business rules.
    /// Spec 013: also flips every owned Draft supplier (referenced via the application's
    /// quotations) to PendingReview atomically with the submission (FR-024).
    /// Returns a list of validation errors, or an empty list on success.
    /// </summary>
    public async Task<List<string>> SubmitApplicationAsync(SubmitApplicationCommand cmd, string userId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

        var config = await _systemConfigurationRepository.GetByKeyAsync("MinQuotationsPerItem");
        int minQuotations;
        if (config is not null)
        {
            minQuotations = int.Parse(config.Value);
        }
        else
        {
            minQuotations = 2;
            _logger.LogWarning("SystemConfiguration key 'MinQuotationsPerItem' not found. Using default value of {Default}.", minQuotations);
        }

        try
        {
            // Spec 013 FR-024: collect every distinct supplier referenced by a quotation
            // on this application, batch-load them in a single round-trip, and flip the
            // ones that are owned Drafts to PendingReview inside the same submit
            // transaction. The actual ownership-and-status filter is applied in-memory
            // below — the variable name reflects the load set, not the flip set.
            var referencedSupplierIds = application.Items
                .SelectMany(i => i.Quotations)
                .Select(q => q.SupplierId)
                .Distinct()
                .ToList();

            if (referencedSupplierIds.Count > 0)
            {
                var suppliers = await _supplierRepository.ListByIdsWithBranchesAsync(referencedSupplierIds);
                foreach (var supplier in suppliers)
                {
                    if (supplier.VerificationStatus == SupplierVerificationStatus.Draft
                        && supplier.CreatedByApplicantId == application.ApplicantId)
                    {
                        supplier.SubmitForReview();
                        await _supplierRepository.UpdateAsync(supplier);
                    }
                }
            }

            application.Submit(minQuotations);
            application.AddVersionHistory(new VersionHistory(userId, "Submitted", "Application submitted for review"));

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            return [];
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.Message;
            var prefix = "Cannot submit application: ";
            if (message.StartsWith(prefix))
            {
                return message[prefix.Length..].Split("; ").ToList();
            }

            return [message];
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return ["This application has been modified by another user. Please refresh and try again."];
        }
    }

    public async Task<ApplicationDto?> GetApplicationAsync(int id)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(id);
        if (application is null)
        {
            return null;
        }

        return MapToDto(application);
    }

    public async Task<List<ApplicationSummaryDto>> GetApplicationsForApplicantAsync(int applicantId)
    {
        var applications = await _applicationRepository.GetByApplicantIdAsync(applicantId);

        return applications.Select(a => new ApplicationSummaryDto(
            a.Id,
            a.State,
            a.Items.Count,
            a.CreatedAt,
            a.UpdatedAt,
            a.SubmittedAt)).ToList();
    }

    public async Task AddItemAsync(AddItemCommand cmd)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

        var category = await _categoryRepository.GetByIdAsync(cmd.CategoryId)
            ?? throw new InvalidOperationException($"Category {cmd.CategoryId} not found.");

        var item = new Item(cmd.ProductName, category.Id, cmd.TechnicalSpecifications);
        application.AddItem(item);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    public async Task UpdateItemAsync(UpdateItemCommand cmd)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

        var category = await _categoryRepository.GetByIdAsync(cmd.CategoryId)
            ?? throw new InvalidOperationException($"Category {cmd.CategoryId} not found.");

        var item = application.Items.FirstOrDefault(i => i.Id == cmd.ItemId)
            ?? throw new InvalidOperationException($"Item {cmd.ItemId} not found in application {cmd.ApplicationId}.");

        item.Update(cmd.ProductName, category.Id, cmd.TechnicalSpecifications);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    public async Task RemoveItemAsync(RemoveItemCommand cmd)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

        application.RemoveItem(cmd.ItemId);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    /// <summary>
    /// Spec 013 (US1): adds a quotation against an existing Verified or
    /// applicant-owned PendingReview/Draft supplier branch. Writes both
    /// SupplierId and SupplierBranchId on the new Quotation atomically from the
    /// same loaded SupplierBranch (preserves the FK invariant).
    /// </summary>
    public async Task AddQuotationToExistingBranchAsync(
        int appId,
        int itemId,
        int supplierId,
        int branchId,
        decimal price,
        string currency,
        DateOnly validUntil,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize)
    {
        var (supplier, branch) = await _supplierCatalogService
            .LoadSupplierAndBranchAsync(supplierId, branchId);

        var application = await _applicationRepository.GetByIdWithDetailsAsync(appId)
            ?? throw new InvalidOperationException($"Application {appId} not found.");

        var item = application.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} not found in application {appId}.");

        var storagePath = await _fileStorageService.SaveFileAsync(fileStream, fileName, contentType);

        var document = new Document(fileName, storagePath, fileSize, contentType);
        await _documentRepository.AddAsync(document);
        await _applicationRepository.SaveChangesAsync();

        try
        {
            item.AddQuotation(supplier, branch, document, price, validUntil, currency);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
        }
        catch
        {
            // Spec 013: best-effort cleanup of the just-saved Document row and the
            // file on disk if the Quotation save fails. Avoids orphaned Document
            // rows and orphaned files when the (item, supplier) UNIQUE constraint
            // or any other invariant rejects the Quotation insert.
            try { await _fileStorageService.DeleteFileAsync(storagePath); } catch { /* best-effort */ }
            throw;
        }
    }

    public async Task ReplaceQuotationDocumentAsync(ReplaceQuotationDocumentCommand cmd, Stream fileStream)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

        var item = application.Items.FirstOrDefault(i => i.Id == cmd.ItemId)
            ?? throw new InvalidOperationException($"Item {cmd.ItemId} not found in application {cmd.ApplicationId}.");

        var quotation = item.Quotations.FirstOrDefault(q => q.Id == cmd.QuotationId)
            ?? throw new InvalidOperationException($"Quotation {cmd.QuotationId} not found in item {cmd.ItemId}.");

        var storagePath = await _fileStorageService.SaveFileAsync(fileStream, cmd.FileName, cmd.FileContentType);
        var newDocument = new Document(cmd.FileName, storagePath, cmd.FileSize, cmd.FileContentType);
        await _documentRepository.AddAsync(newDocument);
        await _applicationRepository.SaveChangesAsync();

        if (quotation.Document is not null)
        {
            await _fileStorageService.DeleteFileAsync(quotation.Document.StoragePath);
        }

        quotation.ReplaceDocument(newDocument.Id);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    public async Task RemoveQuotationAsync(int applicationId, int itemId, int quotationId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId)
            ?? throw new InvalidOperationException($"Application {applicationId} not found.");

        var item = application.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} not found in application {applicationId}.");

        var quotation = item.Quotations.FirstOrDefault(q => q.Id == quotationId);
        if (quotation?.Document is not null)
        {
            await _fileStorageService.DeleteFileAsync(quotation.Document.StoragePath);
        }

        item.RemoveQuotation(quotationId);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    public async Task<List<ImpactTemplateDto>> GetImpactTemplatesAsync()
    {
        var templates = await _impactTemplateRepository.GetAllActiveAsync();

        return templates.Select(t => new ImpactTemplateDto(
            t.Id,
            t.Name,
            t.Description,
            t.IsActive,
            t.Parameters.Select(p => new ImpactTemplateParameterDto(
                p.Id,
                p.Name,
                p.DisplayLabel,
                p.DataType.ToString(),
                p.IsRequired,
                p.ValidationRules,
                p.SortOrder)).ToList())).ToList();
    }

    public async Task SetItemImpactAsync(SetItemImpactCommand cmd)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

        var item = application.Items.FirstOrDefault(i => i.Id == cmd.ItemId)
            ?? throw new InvalidOperationException($"Item {cmd.ItemId} not found in application {cmd.ApplicationId}.");

        var template = await _impactTemplateRepository.GetByIdWithParametersAsync(cmd.ImpactTemplateId)
            ?? throw new InvalidOperationException($"Impact template {cmd.ImpactTemplateId} not found.");

        foreach (var param in template.Parameters.Where(p => p.IsRequired))
        {
            if (!cmd.ParameterValues.TryGetValue(param.Id, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Parameter '{param.DisplayLabel}' is required.");
            }
        }

        var parameterValues = cmd.ParameterValues
            .Select(kvp => new ImpactParameterValue(kvp.Key, kvp.Value))
            .ToList();

        item.SetImpact(template, parameterValues);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    private static ApplicationDto MapToDto(AppEntity application)
    {
        var items = application.Items.Select(item => new ItemDto(
            item.Id,
            item.ProductName,
            item.CategoryId,
            item.Category?.Name ?? string.Empty,
            item.TechnicalSpecifications,
            item.Quotations.Select(q => new QuotationDto(
                q.Id,
                q.SupplierId,
                q.Supplier?.Name ?? string.Empty,
                q.Supplier?.LegalId ?? string.Empty,
                q.Price,
                q.Currency,
                q.ValidUntil,
                q.DocumentId,
                q.Document?.OriginalFileName ?? string.Empty)).ToList(),
            item.Impact is not null
                ? new ImpactDto(
                    item.Impact.Id,
                    item.Impact.ImpactTemplateId,
                    item.Impact.ImpactTemplate?.Name ?? string.Empty,
                    item.Impact.ParameterValues.Select(pv => new ImpactParameterValueDto(
                        pv.Id,
                        pv.ImpactTemplateParameterId,
                        pv.ImpactTemplateParameter?.Name ?? string.Empty,
                        pv.ImpactTemplateParameter?.DisplayLabel ?? string.Empty,
                        pv.ImpactTemplateParameter?.DataType.ToString() ?? string.Empty,
                        pv.ImpactTemplateParameter?.IsRequired ?? false,
                        pv.Value)).ToList())
                : null,
            item.ReviewComment)).ToList();

        return new ApplicationDto(
            application.Id,
            application.ApplicantId,
            application.State,
            application.CreatedAt,
            application.UpdatedAt,
            application.SubmittedAt,
            items);
    }
}
