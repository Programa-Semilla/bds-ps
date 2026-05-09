using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 018 — the create flow now returns either the newly-created Application id
/// or a <see cref="UserFacingError"/> when the entity rejects the supplied company
/// name. <see cref="ApplicationId"/> is 0 when <see cref="Error"/> is non-null.
/// </summary>
public sealed record CreateApplicationResult(int ApplicationId, UserFacingError? Error);

public class ApplicationService
{
    // Spec 014 / T052 — quotation files stream through IObjectStorage with the
    // ApplicationAttachment category. Owner segment is the applicant's user id;
    // entity id is the application id (the natural parent aggregate for the
    // quotation document). The legacy IFileStorageService dependency is gone.
    private const FileCategory QuotationCategory = FileCategory.ApplicationAttachment;

    private readonly IApplicationRepository _applicationRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IObjectStorage _objectStorage;
    private readonly IImpactTemplateRepository _impactTemplateRepository;
    private readonly ISystemConfigurationRepository _systemConfigurationRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly SupplierCatalogService _supplierCatalogService;
    private readonly IConversionService _conversionService;
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(
        IApplicationRepository applicationRepository,
        ICategoryRepository categoryRepository,
        ISupplierRepository supplierRepository,
        IObjectStorage objectStorage,
        IImpactTemplateRepository impactTemplateRepository,
        ISystemConfigurationRepository systemConfigurationRepository,
        IDocumentRepository documentRepository,
        SupplierCatalogService supplierCatalogService,
        IConversionService conversionService,
        ILogger<ApplicationService> logger)
    {
        _applicationRepository = applicationRepository;
        _categoryRepository = categoryRepository;
        _supplierRepository = supplierRepository;
        _objectStorage = objectStorage;
        _impactTemplateRepository = impactTemplateRepository;
        _systemConfigurationRepository = systemConfigurationRepository;
        _documentRepository = documentRepository;
        _supplierCatalogService = supplierCatalogService;
        _conversionService = conversionService;
        _logger = logger;
    }

    /// <summary>
    /// Spec 018 / FR-015 / FR-016 — creates a new draft Application with the
    /// applicant-supplied company name. Domain-level validation (required, ≤200,
    /// trim semantics) happens inside the entity constructor; ArgumentException
    /// from the entity is mapped to a user-facing code via <see cref="UserFacingError"/>.
    /// </summary>
    public async Task<CreateApplicationResult> CreateApplicationAsync(
        CreateApplicationCommand cmd, string? userId = null)
    {
        AppEntity application;
        try
        {
            application = new AppEntity(cmd.ApplicantId, cmd.CompanyName);
        }
        catch (ArgumentException ex)
        {
            // Map entity-level validation failures to user-facing codes.
            // The trim-then-check ordering in SetCompanyName means an over-length
            // input always trips the length branch; null/blank trips the required
            // branch. Distinguishing them lets the Web layer pick the right
            // Spanish message per FR-014 / NFR-001.
            var code = ex.Message.Contains("200", StringComparison.Ordinal)
                ? UserFacingErrorCode.CompanyNameTooLong
                : UserFacingErrorCode.CompanyNameRequired;
            return new CreateApplicationResult(0, UserFacingError.From(code, ex.Message));
        }

        if (userId is not null)
        {
            application.AddVersionHistory(new VersionHistory(userId, "Created", "Application created"));
        }

        await _applicationRepository.AddAsync(application);
        await _applicationRepository.SaveChangesAsync();
        return new CreateApplicationResult(application.Id, null);
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

        // Spec 014 / T052 — build the canonical ObjectKey from the application
        // aggregate. Owner segment uses the applicant's user id when available
        // (matches the SignedUploadService convention) and falls back to the
        // numeric applicant id; the deterministic suffix is a fresh GUID so
        // multiple quotations under the same item remain reachable.
        var key = BuildQuotationKey(application, fileName);
        var stored = await _objectStorage.UploadAsync(
            QuotationCategory, key, fileStream, contentType, fileSize, CancellationToken.None);

        var document = new Document(fileName, stored.Key, fileSize, contentType);
        await _documentRepository.AddAsync(document);
        await _applicationRepository.SaveChangesAsync();

        try
        {
            // Spec 015 — route through Quotation.SetCurrencyAndAmountAsync so the
            // (Snapshot, ConvertedCrcAmount) fields are populated atomically with
            // the row. CRC short-circuits to (Snapshot=null, ConvertedCrcAmount=Price);
            // non-CRC reads the latest published rate, embeds the snapshot, and marks
            // the source rate row used (FR-008). MissingRateException bubbles up to
            // the controller for inline FR-018 messaging.
            var quotation = new Quotation(
                supplierId: supplier.Id,
                supplierBranchId: branch.Id,
                documentId: document.Id,
                price: price,
                validUntil: validUntil,
                currency: currency);

            await quotation.SetCurrencyAndAmountAsync(
                CurrencyCode.From(currency), price, _conversionService);

            item.AttachQuotation(supplier, branch, quotation);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
        }
        catch
        {
            // Spec 013: best-effort cleanup of the just-saved Document row and the
            // file in object storage if the Quotation save fails. Avoids orphaned
            // Document rows and orphaned blobs when the (item, supplier) UNIQUE
            // constraint or any other invariant rejects the Quotation insert.
            try { await _objectStorage.DeleteAsync(QuotationCategory, key, CancellationToken.None); }
            catch { /* best-effort */ }
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

        // Spec 014 / T052 — replacement upload via IObjectStorage.
        var newKey = BuildQuotationKey(application, cmd.FileName);
        var stored = await _objectStorage.UploadAsync(
            QuotationCategory, newKey, fileStream, cmd.FileContentType, cmd.FileSize, CancellationToken.None);

        var newDocument = new Document(cmd.FileName, stored.Key, cmd.FileSize, cmd.FileContentType);
        await _documentRepository.AddAsync(newDocument);
        await _applicationRepository.SaveChangesAsync();

        if (quotation.Document is not null)
        {
            await TryDeleteQuotationBlobAsync(quotation.Document);
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
            await TryDeleteQuotationBlobAsync(quotation.Document);
        }

        item.RemoveQuotation(quotationId);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    /// <summary>
    /// Spec 014 / T052 — build the canonical ObjectKey for an application-attachment
    /// quotation document. Owner segment is the applicant's user id when available
    /// (matches SignedUploadService convention) and falls back to a numeric
    /// applicants/{id} segment for legacy rows where the user link is missing.
    /// </summary>
    private static ObjectKey BuildQuotationKey(AppEntity application, string fileName)
    {
        var ownerSegment = application.Applicant?.UserId is { Length: > 0 } applicantUserId
            ? $"applicants/{applicantUserId}"
            : $"applicants/{application.ApplicantId}";
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
        return ObjectKey.Build(
            FileCategory.ApplicationAttachment,
            ownerSegment,
            entityId: application.Id.ToString(),
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: ext);
    }

    /// <summary>
    /// Spec 014 — best-effort delete of a quotation document's blob.
    /// Every Document row has a canonical <c>BlobKey</c> populated on insert.
    /// </summary>
    private async Task TryDeleteQuotationBlobAsync(Document document)
    {
        var key = ObjectKey.Parse(document.BlobKey);

        var category = key.Container switch
        {
            "application-attachments" => FileCategory.ApplicationAttachment,
            "signed-funding-agreements" => FileCategory.SignedFundingAgreement,
            "supplier-catalog-imports" => FileCategory.SupplierCatalogImport,
            _ => FileCategory.GeneratedArtifact,
        };

        try
        {
            await _objectStorage.DeleteAsync(category, key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Best-effort delete of quotation blob failed. documentId={DocumentId} blobKey={BlobKey}",
                document.Id, key.Value);
        }
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
                q.Document?.OriginalFileName ?? string.Empty,
                ConvertedCrcAmount: q.ConvertedCrcAmount,
                SnapshotRateValue: q.Snapshot?.RateValue,
                SnapshotRateType: q.Snapshot?.RateType.ToString(),
                SnapshotEffectiveAtUtc: q.Snapshot?.EffectiveAtUtc,
                LegacyNeedsReview: q.LegacyNeedsReview)).ToList(),
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
            item.ReviewComment,
            item.SelectedSupplierId)).ToList();

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
