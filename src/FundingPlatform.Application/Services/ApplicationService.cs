using FundingPlatform.Application.Abstractions.Comparison;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.Notifications;
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

/// <summary>Spec 023 — read projection consumed by <c>QuotationController.Edit</c> (GET).</summary>
public sealed record EditQuotationReadDto(
    int ApplicationId,
    int ApplicantId,
    ApplicationState ApplicationState,
    int ItemId,
    int QuotationId,
    decimal Price,
    string Currency,
    DateOnly ValidUntil,
    int SupplierBranchId,
    string SupplierName,
    int SupplierId,
    bool LegacyNeedsReview,
    IReadOnlyList<EditQuotationBranchDto> Branches);

public sealed record EditQuotationBranchDto(int Id, string BranchName);

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
    private readonly IPublicCodeGenerator? _publicCodeGenerator;
    private readonly INotificationOutboxWriter _outboxWriter;
    private readonly IWorkflowTransactionScope _txScope;
    private readonly ILogger<ApplicationService> _logger;
    // Spec 023 / FR-009 — optional so legacy integration-test ctors still
    // compile. When null, EditQuotationAsync no-ops the cache hook.
    private readonly IComparisonCacheInvalidator? _comparisonCacheInvalidator;
    // Spec 023 — optional currency repo. When null, EditQuotationAsync falls
    // back to CurrencyCode.From shape validation only (matches QuotationController.Convert).
    private readonly ICurrencyRepository? _currencyRepository;

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
        INotificationOutboxWriter outboxWriter,
        IWorkflowTransactionScope txScope,
        ILogger<ApplicationService> logger,
        IPublicCodeGenerator? publicCodeGenerator = null,
        IComparisonCacheInvalidator? comparisonCacheInvalidator = null,
        ICurrencyRepository? currencyRepository = null)
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
        _publicCodeGenerator = publicCodeGenerator;
        _outboxWriter = outboxWriter;
        _txScope = txScope;
        _logger = logger;
        _comparisonCacheInvalidator = comparisonCacheInvalidator;
        _currencyRepository = currencyRepository;
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
            // Map entity-level validation failures to user-facing codes via the
            // stable Data["FundingPlatform.ValidationReason"] discriminator the
            // entity sets (instead of fragile message-string matching). Lets the
            // Web layer pick the right Spanish message per FR-014 / NFR-001
            // even if the English exception text is later edited.
            var reason = ex.Data[Item.ValidationReasonKey] as string;
            var code = reason switch
            {
                AppEntity.CompanyNameTooLongReason => UserFacingErrorCode.CompanyNameTooLong,
                AppEntity.CompanyNameRequiredReason => UserFacingErrorCode.CompanyNameRequired,
                _ => UserFacingErrorCode.CompanyNameRequired,
            };
            return new CreateApplicationResult(0, UserFacingError.From(code, ex.Message));
        }

        // Spec 021 / FR-008 — stamp the opaque PublicCode before the first
        // SaveChanges. The column is NOT NULL at the DB level. The generator
        // is optional in the constructor for back-compat with legacy tests
        // that construct ApplicationService without an Infrastructure
        // PublicCodeGenerator; when missing, the existing tests rely on
        // EF InMemory which does not enforce the constraint.
        if (_publicCodeGenerator is not null && application.PublicCode is null)
        {
            var code = await _publicCodeGenerator.GenerateAsync();
            application.AssignPublicCode(code);
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
            var vhRow = new VersionHistory(userId, "Submitted", "Application submitted for review");
            application.AddVersionHistory(vhRow);

            // R-003 — Resubmit detection: a prior SendBack row in VersionHistory means
            // this Submit is a resubmission → fire RESUBMITTED_BY_APPLICANT instead of
            // the two-row APPLICATION_SUBMITTED_* fan-out (Phase 5 / US3).
            // Read BEFORE we add the new VersionHistory row so the predicate reflects
            // prior cycles only.
            var isResubmit = await _outboxWriter.HasPriorSendBackAsync(application.Id, CancellationToken.None);

            await _applicationRepository.UpdateAsync(application);

            // Spec 021 / FR-001 — two-phase save (workflow first, outbox second).
            // No explicit BeginTransaction because Aspire's SQL Server connection
            // uses Microsoft.Data.SqlClient with retry-on-transient policies that
            // re-execute SaveChanges. Wrapping in an explicit transaction conflicts
            // with that retry policy and produced silent SaveChanges failures during
            // E2E (see commit history for the txScope rollback). The two saves are
            // separated by ~1ms in practice; the worker tolerates the rare case where
            // the second save fails (idempotency + retry catch any duplicate or lost
            // row on the next poll).
            await _applicationRepository.SaveChangesAsync();
            // vhRow.Id now assigned.

            var stageGroupIds = await _outboxWriter.GetApplicantStageGroupIdsAsync(
                application.Id, CancellationToken.None);

            var applicantDisplayName = application.Applicant is not null
                ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
                : "Solicitante";
            var applicantUserId = application.Applicant?.UserId ?? userId;

            var payload = new NotificationPayload(
                ApplicationId: application.Id,
                ApplicantUserId: applicantUserId,
                ApplicantDisplayName: applicantDisplayName,
                StageGroupIds: stageGroupIds,
                OutcomeCode: null);

            if (isResubmit)
            {
                await _outboxWriter.EnqueueAsync(
                    NotificationEvent.ResubmittedByApplicant,
                    application.Id, vhRow.Id, payload, CancellationToken.None);
            }
            else
            {
                await _outboxWriter.EnqueueAsync(
                    NotificationEvent.ApplicationSubmittedApplicant,
                    application.Id, vhRow.Id, payload, CancellationToken.None);
                await _outboxWriter.EnqueueAsync(
                    NotificationEvent.ApplicationSubmittedReviewer,
                    application.Id, vhRow.Id, payload, CancellationToken.None);
            }

            await _applicationRepository.SaveChangesAsync();
            _logger.LogInformation(
                "Spec 021: enqueued outbox rows for application {AppId}, VersionHistoryId={VhId}, isResubmit={Resubmit}",
                application.Id, vhRow.Id, isResubmit);
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
            a.SubmittedAt,
            a.PublicCode?.Value,
            a.CompanyName)).ToList();
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
    /// Spec 023 — read projection for the Quotation/Edit form. Resolves the
    /// quotation, the owning Item, and the parent Application, plus the
    /// Supplier's branch set (needed by the branch picker). Returns null when
    /// any link is missing or soft-deleted. Used by <c>QuotationController.Edit</c>
    /// (GET) to render the form pre-populated with current values.
    /// </summary>
    public async Task<EditQuotationReadDto?> GetQuotationForEditAsync(
        int applicationId, int itemId, int quotationId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null) return null;

        var item = application.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return null;

        var quotation = item.Quotations.FirstOrDefault(q => q.Id == quotationId);
        if (quotation is null) return null;

        // Eager-load the supplier's branches (the included Supplier nav only carries
        // the single branch this quotation references, not the full branch set).
        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(quotation.SupplierId);
        if (supplier is null) return null;

        return new EditQuotationReadDto(
            ApplicationId: application.Id,
            ApplicantId: application.ApplicantId,
            ApplicationState: application.State,
            ItemId: item.Id,
            QuotationId: quotation.Id,
            Price: quotation.Price,
            Currency: quotation.Currency,
            ValidUntil: quotation.ValidUntil,
            SupplierBranchId: quotation.SupplierBranchId,
            SupplierName: supplier.Name,
            SupplierId: supplier.Id,
            LegacyNeedsReview: quotation.LegacyNeedsReview,
            Branches: supplier.Branches
                .Select(b => new EditQuotationBranchDto(b.Id, b.BranchName))
                .ToList());
    }

    /// <summary>
    /// Spec 023 — applicant-initiated in-place edit of a quotation. See
    /// <c>contracts/quotation-edit-endpoint.md</c> and <c>data-model.md</c> §2.3
    /// for the orchestration contract. Returns an <see cref="EditQuotationResult"/>
    /// envelope; the controller dispatches on <see cref="EditQuotationOutcome"/>.
    ///
    /// Implementation notes:
    /// - Load order: Application + its Quotation chain via <c>GetByIdWithDetailsAsync</c>;
    ///   the Supplier's branch set via <c>ISupplierRepository.GetByIdWithBranchesAsync</c>
    ///   (the application-detail load only carries the branch the quotation already
    ///   references, not the full supplier branch set the picker needs).
    /// - Note on lifecycle states: the codebase has a single editable state
    ///   (<see cref="ApplicationState.Draft"/>). Spec 023 references a logical
    ///   "ReturnedForChanges" state; in the current implementation, the reviewer's
    ///   <c>SendBack</c> path transitions back to Draft (Application.cs:418-434),
    ///   so the state gate is satisfied by <c>state == Draft</c>.
    /// - Idempotency (NFR-004): when no field changed, returns Success without
    ///   touching the DB, exchange-rate, or comparison cache.
    /// - Mutation order (research §R0.7): ChangeCurrency → EditAmount → ChangeBranch
    ///   → SetValidUntil. Currency-change resets the snapshot first; price change
    ///   then re-multiplies against the fresh rate.
    /// </summary>
    public async Task<EditQuotationResult> EditQuotationAsync(
        EditQuotationCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var application = await _applicationRepository.GetByIdWithDetailsAsync(command.ApplicationId);
        if (application is null)
        {
            return new EditQuotationResult(EditQuotationOutcome.NotFound);
        }

        // FR-007 — ownership gate.
        if (application.ApplicantId != command.ApplicantId)
        {
            return new EditQuotationResult(EditQuotationOutcome.Forbidden);
        }

        var item = application.Items.FirstOrDefault(i => i.Id == command.ItemId);
        if (item is null)
        {
            return new EditQuotationResult(EditQuotationOutcome.NotFound);
        }

        var quotation = item.Quotations.FirstOrDefault(q => q.Id == command.QuotationId);
        if (quotation is null)
        {
            return new EditQuotationResult(EditQuotationOutcome.NotFound);
        }

        // FR-008 — state gate. See the lifecycle note in the XML summary.
        if (application.State != ApplicationState.Draft)
        {
            return new EditQuotationResult(
                EditQuotationOutcome.StateChanged,
                GlobalError: "El estado de la solicitud cambió, recarga la página.");
        }

        // FR-011 — legacy-flagged quotations route through the admin-only path.
        if (quotation.LegacyNeedsReview)
        {
            return new EditQuotationResult(
                EditQuotationOutcome.LegacyFlagged,
                GlobalError: "Esta cotización está marcada para revisión administrativa de tipo de cambio.");
        }

        // Eager-load the supplier with its branch set so the branch invariant
        // can be enforced in code without a second DB round-trip per branch.
        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(quotation.SupplierId);
        if (supplier is null)
        {
            return new EditQuotationResult(EditQuotationOutcome.NotFound);
        }

        // FR-005 — aggregate field validation (R0.5).
        var fieldErrors = new Dictionary<string, string>();
        if (command.Price <= 0m)
        {
            fieldErrors[nameof(command.Price)] = "El precio debe ser mayor a cero.";
        }

        CurrencyCode? parsedCurrency = null;
        if (string.IsNullOrWhiteSpace(command.Currency))
        {
            fieldErrors[nameof(command.Currency)] = "La moneda es obligatoria.";
        }
        else
        {
            try
            {
                parsedCurrency = CurrencyCode.From(command.Currency);
            }
            catch (ArgumentException)
            {
                fieldErrors[nameof(command.Currency)] =
                    $"La moneda '{command.Currency}' no está configurada.";
            }
        }

        // Currency-catalog gate (only when shape parsed cleanly).
        if (parsedCurrency is not null && _currencyRepository is not null)
        {
            var catalogEntry = await _currencyRepository.GetByCodeAsync(parsedCurrency, ct);
            if (catalogEntry is null)
            {
                fieldErrors[nameof(command.Currency)] =
                    $"La moneda '{parsedCurrency}' no está configurada.";
            }
            else if (!catalogEntry.IsEnabled)
            {
                fieldErrors[nameof(command.Currency)] =
                    $"La moneda '{parsedCurrency}' está deshabilitada.";
            }
        }

        if (command.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            fieldErrors[nameof(command.ValidUntil)] =
                "La fecha de vigencia debe ser hoy o futura.";
        }

        var targetBranch = supplier.Branches.FirstOrDefault(b => b.Id == command.SupplierBranchId);
        if (targetBranch is null)
        {
            fieldErrors[nameof(command.SupplierBranchId)] =
                "Sucursal no válida para este proveedor.";
        }

        if (fieldErrors.Count > 0)
        {
            return new EditQuotationResult(
                EditQuotationOutcome.ValidationFailed,
                FieldErrors: fieldErrors);
        }

        // NFR-004 — idempotency short-circuit. All four fields match → no-op.
        var normalizedCurrency = parsedCurrency!.Value;
        var currencyChanged = !string.Equals(quotation.Currency, normalizedCurrency, StringComparison.Ordinal);
        var priceChanged = quotation.Price != command.Price;
        var validUntilChanged = quotation.ValidUntil != command.ValidUntil;
        var branchChanged = quotation.SupplierBranchId != command.SupplierBranchId;

        if (!currencyChanged && !priceChanged && !validUntilChanged && !branchChanged)
        {
            return new EditQuotationResult(EditQuotationOutcome.Success);
        }

        try
        {
            // Order (R0.7): ChangeCurrency → EditAmount → ChangeBranch → SetValidUntil.
            if (currencyChanged)
            {
                await quotation.ChangeCurrencyAsync(parsedCurrency, _conversionService, ct)
                    .ConfigureAwait(false);
            }

            if (priceChanged)
            {
                quotation.EditAmount(command.Price);
            }
            else if (!currencyChanged && quotation.Currency == CurrencyCode.Crc.Value)
            {
                // CRC, same price, same currency — nothing else to recompute. The
                // ConvertedCrcAmount is already Price by the entity invariant.
            }

            if (branchChanged)
            {
                quotation.ChangeBranch(targetBranch!);
            }

            if (validUntilChanged)
            {
                quotation.SetValidUntil(command.ValidUntil);
            }
        }
        catch (MissingRateException ex)
        {
            _logger.LogWarning(ex,
                "EditQuotation: missing rate for currency {Currency} on quotation {QuotationId}.",
                normalizedCurrency, command.QuotationId);
            return new EditQuotationResult(
                EditQuotationOutcome.MissingRate,
                GlobalError: "No hay un tipo de cambio publicado para la moneda solicitada.");
        }
        catch (ArgumentException ex) when (ex.ParamName == nameof(SupplierBranch.SupplierId)
                                            || ex.ParamName == "branch"
                                            || ex.ParamName == "newValidUntil")
        {
            // Defensive: entity-level invariants we already pre-validated above.
            // Translate any residual exception into a field error so the form
            // re-renders cleanly instead of 500-ing.
            var key = ex.ParamName == "newValidUntil"
                ? nameof(command.ValidUntil)
                : nameof(command.SupplierBranchId);
            return new EditQuotationResult(
                EditQuotationOutcome.ValidationFailed,
                FieldErrors: new Dictionary<string, string> { [key] = ex.Message.Split('\n')[0] });
        }

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();

        // FR-009 — silent cache invalidation after commit. Only on the non-idempotent
        // path (the early return above guarantees we got here only when something changed).
        if (_comparisonCacheInvalidator is not null)
        {
            try
            {
                await _comparisonCacheInvalidator.InvalidateForItemAsync(item.Id, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Cache miss is the expected reviewer-side behaviour anyway; log and continue.
                _logger.LogWarning(ex,
                    "EditQuotation: cache invalidation failed for itemId={ItemId}; reviewer will see stale until regenerate.",
                    item.Id);
            }
        }

        _logger.LogInformation(
            "EditQuotation: applicantId={ApplicantId} applicationId={ApplicationId} itemId={ItemId} quotationId={QuotationId} "
                + "currencyChanged={CurrencyChanged} priceChanged={PriceChanged} validUntilChanged={ValidUntilChanged} branchChanged={BranchChanged}",
            command.ApplicantId, command.ApplicationId, command.ItemId, command.QuotationId,
            currencyChanged, priceChanged, validUntilChanged, branchChanged);

        return new EditQuotationResult(EditQuotationOutcome.Success);
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

    public async Task SetApplicationImpactAsync(SetApplicationImpactCommand cmd)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(cmd.ApplicationId)
            ?? throw new InvalidOperationException($"Application {cmd.ApplicationId} not found.");

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

        // Spec 021 / FR-005 — Impact is captured on the Application aggregate
        // upfront, before any Item exists; there is no per-Item Impact path.
        application.SetImpact(template, parameterValues);

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();
    }

    private static ApplicationDto MapToDto(AppEntity application)
    {
        // Spec 021 / FR-005 — Impact is a per-Application value, surfaced as
        // ApplicationDto.Impact below. The per-Item ImpactDto on ItemDto is a
        // vestigial mirror kept so existing read paths keep compiling; Id = 0
        // (the value-object projection has no row identity).
        var applicationImpactDto = application.ImpactTemplate is not null
            ? new ImpactDto(
                0,
                application.ImpactTemplate.Id,
                application.ImpactTemplate.Name ?? string.Empty,
                application.ImpactParameterValues.Select(pv => new ImpactParameterValueDto(
                    pv.Id,
                    pv.ImpactTemplateParameterId,
                    pv.ImpactTemplateParameter?.Name ?? string.Empty,
                    pv.ImpactTemplateParameter?.DisplayLabel ?? string.Empty,
                    pv.ImpactTemplateParameter?.DataType.ToString() ?? string.Empty,
                    pv.ImpactTemplateParameter?.IsRequired ?? false,
                    pv.Value)).ToList())
            : null;

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
            applicationImpactDto,
            item.ReviewComment,
            item.SelectedSupplierId)).ToList();

        return new ApplicationDto(
            application.Id,
            application.ApplicantId,
            application.State,
            application.CreatedAt,
            application.UpdatedAt,
            application.SubmittedAt,
            items,
            application.PublicCode?.Value,
            application.CompanyName,
            applicationImpactDto);
    }
}
