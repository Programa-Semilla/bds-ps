using System.Security.Claims;
using System.Text.Json;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.FundingAgreements.Commands;
using FundingPlatform.Application.FundingAgreements.Queries;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Commands;
using FundingPlatform.Application.SignedUploads.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Web.Controllers;

[Authorize]
[Route("Applications/{applicationId:int}/FundingAgreement")]
public class FundingAgreementController : Controller
{
    private readonly FundingAgreementService _service;
    private readonly SignedUploadService _signedUploadService;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IFundingAgreementHtmlRenderer _htmlRenderer;
    private readonly IFundingAgreementPdfRenderer _pdfRenderer;
    // Spec 014 / T028 — migrated from IFileStorageService. The category for the
    // generated PDF is SignedFundingAgreement: it's the artefact the applicant
    // downloads and signs, and it must live alongside the signed copies (FR-013
    // legal-hold candidate per spec/research notes). Generic generator artefacts
    // (e.g. quotation PDFs created by other surfaces) belong to GeneratedArtifact;
    // the funding-agreement document is privileged.
    private readonly IObjectStorage _objectStorage;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOptions<FunderOptions> _funderOptions;
    private readonly IOptions<FundingAgreementOptions> _agreementOptions;
    private readonly IUserFacingErrorTranslator _errorTranslator;
    private readonly ILogger<FundingAgreementController> _logger;

    private const string SignedPdfRequiredMessage = "Se requiere un archivo PDF firmado.";
    private const FileCategory AgreementCategory = FileCategory.SignedFundingAgreement;

    public FundingAgreementController(
        FundingAgreementService service,
        SignedUploadService signedUploadService,
        IApplicationRepository applicationRepository,
        IFundingAgreementHtmlRenderer htmlRenderer,
        IFundingAgreementPdfRenderer pdfRenderer,
        IObjectStorage objectStorage,
        UserManager<ApplicationUser> userManager,
        IOptions<FunderOptions> funderOptions,
        IOptions<FundingAgreementOptions> agreementOptions,
        IUserFacingErrorTranslator errorTranslator,
        ILogger<FundingAgreementController> logger)
    {
        _service = service;
        _signedUploadService = signedUploadService;
        _applicationRepository = applicationRepository;
        _htmlRenderer = htmlRenderer;
        _pdfRenderer = pdfRenderer;
        _objectStorage = objectStorage;
        _userManager = userManager;
        _funderOptions = funderOptions;
        _agreementOptions = agreementOptions;
        _errorTranslator = errorTranslator;
        _logger = logger;
    }

    [HttpGet("Panel")]
    public async Task<IActionResult> Panel(int applicationId)
    {
        var viewModel = await BuildPanelViewModelAsync(applicationId);
        if (viewModel is null)
        {
            LogUnauthorized(applicationId, "Panel", "access-denied-or-missing");
            return NotFound();
        }

        return PartialView("~/Views/Applications/_FundingAgreementPanel.cshtml", viewModel);
    }

    [HttpPost("Generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int applicationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdministrator = User.IsInRole("Admin");
        var isReviewer = User.IsInRole("Reviewer");

        var application = await _service.LoadForGenerationAsync(applicationId);
        if (application is null)
        {
            LogUnauthorized(applicationId, "Generate", "application-missing");
            return NotFound();
        }

        var canUserAccess = application.CanUserAccessFundingAgreement(
            applicantUserId: userId,
            isAdministrator: isAdministrator,
            isReviewerAssignedToThisApplication: isReviewer);

        if (!canUserAccess)
        {
            LogUnauthorized(applicationId, "Generate", "access-denied");
            return NotFound();
        }

        if (!application.CanUserGenerateFundingAgreement(
                isAdministrator: isAdministrator,
                isReviewerAssignedToThisApplication: isReviewer))
        {
            LogUnauthorized(applicationId, "Generate", "role-forbidden");
            return StatusCode(403);
        }

        var isRegeneration = application.FundingAgreement is not null;
        if (isRegeneration)
        {
            if (!application.CanRegenerateFundingAgreement(out var regenErrors))
            {
                // Domain returns English precondition reasons; log them but
                // surface a Spanish summary to the user (FR-014, NFR-001).
                var reasonForLog = regenErrors.FirstOrDefault() ?? "Regeneration preconditions are not met.";
                application.AddVersionHistory(new VersionHistory(
                    userId,
                    SigningAuditActions.FundingAgreementRegenerationBlocked,
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["reason"] = reasonForLog,
                        ["pendingOrTerminalUploadId"] = application.FundingAgreement!.SignedUploads
                            .OrderByDescending(u => u.UploadedAtUtc)
                            .Select(u => (int?)u.Id)
                            .FirstOrDefault()
                    })));

                await _applicationRepository.UpdateAsync(application);
                await _applicationRepository.SaveChangesAsync();

                TempData["FundingAgreementError"] = _errorTranslator.Translate(
                    UserFacingErrorCode.AgreementRegenerationPreconditionsNotMet);
                return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
            }
        }
        else if (!application.CanGenerateFundingAgreement(out var precondErrors))
        {
            // Domain returns English precondition reasons; surface the
            // generic Spanish summary (FR-014, NFR-001).
            TempData["FundingAgreementError"] = _errorTranslator.Translate(
                UserFacingErrorCode.AgreementGenerationPreconditionsNotMet);
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        var documentModel = await BuildDocumentViewModelAsync(application);
        var html = await _htmlRenderer.RenderAsync(
            "~/Views/FundingAgreement/Document.cshtml",
            documentModel);

        byte[] pdfBytes;
        try
        {
            pdfBytes = await _pdfRenderer.RenderAsync(html, baseUrl: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Funding agreement PDF rendering failed. applicationId={ApplicationId}", applicationId);
            TempData["FundingAgreementError"] = _errorTranslator.Translate(
                UserFacingErrorCode.AgreementPdfRenderingFailed);
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        var fileName = $"FundingAgreement-{application.Id}.pdf";
        var priorBlobKey = application.FundingAgreement?.BlobKey;

        // Spec 014 / T028 — build the canonical ObjectKey from the
        // signed-funding-agreement aggregate. EntityId is the application id
        // (the agreement's natural owner); the deterministic suffix is a fresh
        // GUID for every (re)generation so prior versions remain reachable in
        // the manifest while the active key gets recorded on the entity.
        var ownerSegment = application.Applicant?.UserId is { Length: > 0 } applicantUserId
            ? $"applicants/{applicantUserId}"
            : "admin";
        var key = ObjectKey.Build(
            AgreementCategory,
            ownerSegment,
            entityId: application.Id.ToString(),
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: ".pdf");

        StoredObject stored;
        using (var pdfStream = new MemoryStream(pdfBytes))
        {
            stored = await _objectStorage.UploadAsync(
                AgreementCategory,
                key,
                pdfStream,
                "application/pdf",
                pdfBytes.LongLength,
                HttpContext.RequestAborted);
        }
        var storagePath = stored.Key;

        var persist = await _service.PersistGenerationAsync(
            application, userId, fileName, pdfBytes.LongLength, storagePath);

        if (!persist.Success)
        {
            try { await _objectStorage.DeleteAsync(AgreementCategory, key, HttpContext.RequestAborted); }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx,
                    "Failed to clean up orphaned PDF after persistence failure. storagePath={StoragePath}",
                    storagePath);
            }

            var firstError = persist.Errors.FirstOrDefault();
            if (persist.ConflictDetected)
            {
                TempData["FundingAgreementError"] = firstError is null
                    ? _errorTranslator.Translate(UserFacingErrorCode.ConcurrentAgreementModification)
                    : _errorTranslator.Translate(firstError);
                return StatusCode(409);
            }

            TempData["FundingAgreementError"] = firstError is null
                ? _errorTranslator.Translate(UserFacingErrorCode.AgreementGenerationFailed)
                : _errorTranslator.Translate(firstError);
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        // Record the canonical blob key on the agreement entity so downstream
        // consumers (download, regeneration, migration tool) can resolve it.
        if (application.FundingAgreement is not null)
        {
            application.FundingAgreement.RecordBlob(stored.Key);
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
        }

        if (!string.IsNullOrWhiteSpace(priorBlobKey) && priorBlobKey != stored.Key)
        {
            try
            {
                var prior = ObjectKey.Parse(priorBlobKey);
                await _objectStorage.DeleteAsync(AgreementCategory, prior, HttpContext.RequestAborted);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx,
                    "Failed to delete prior funding agreement blob. blobKey={BlobKey}",
                    priorBlobKey);
            }
        }

        TempData["FundingAgreementSuccess"] = "Convenio de financiamiento generado.";
        return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
    }

    [HttpGet("Download")]
    public async Task<IActionResult> Download(int applicationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdministrator = User.IsInRole("Admin");
        var isReviewer = User.IsInRole("Reviewer");

        var application = await _service.LoadForGenerationAsync(applicationId);
        if (application is null)
        {
            LogUnauthorized(applicationId, "Download", "application-missing");
            return NotFound();
        }

        if (!application.CanUserAccessFundingAgreement(
                applicantUserId: userId,
                isAdministrator: isAdministrator,
                isReviewerAssignedToThisApplication: isReviewer))
        {
            LogUnauthorized(applicationId, "Download", "access-denied");
            return NotFound();
        }

        var agreement = application.FundingAgreement;
        if (agreement is null)
        {
            LogUnauthorized(applicationId, "Download", "agreement-missing");
            return NotFound();
        }

        application.AddVersionHistory(new VersionHistory(
            userId,
            SigningAuditActions.AgreementDownloaded,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["generatedVersion"] = agreement.GeneratedVersion
            })));

        await _applicationRepository.UpdateAsync(application);
        await _applicationRepository.SaveChangesAsync();

        // Spec 014 / T028 — resolve via IObjectStorage.ResolveServingHandleAsync.
        // Default ServingMode.BackendStream so the application boundary remains
        // the only authorisation point (FR-018); SAS URLs are never emitted to
        // applicants for the agreement category.
        var rawKey = agreement.BlobKey ?? agreement.StoragePath;
        ObjectKey key;
        try
        {
            key = ObjectKey.Parse(rawKey);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "Funding agreement download could not parse storage key '{RawKey}' as ObjectKey. " +
                "Pre-014 rows are owned by the migration tool (T040).",
                rawKey);
            return NotFound();
        }

        BackendStreamHandle backendHandle;
        try
        {
            var handle = await _objectStorage.ResolveServingHandleAsync(
                AgreementCategory,
                key,
                ServingMode.BackendStream,
                HttpContext.RequestAborted);
            backendHandle = (BackendStreamHandle)handle;
        }
        catch (ObjectNotFoundException)
        {
            LogUnauthorized(applicationId, "Download", "blob-missing");
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-cache";
        return File(backendHandle.Content, agreement.ContentType,
            fileDownloadName: $"FundingAgreement-{application.Id}.pdf");
    }

    [HttpPost("Upload")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 50L * 1024 * 1024)]
    public async Task<IActionResult> Upload(int applicationId, UploadSignedAgreementViewModel model)
    {
        if (!ModelState.IsValid || model.File is null || model.File.Length == 0)
        {
            TempData["FundingAgreementError"] = SignedPdfRequiredMessage;
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        await using var stream = model.File.OpenReadStream();
        var command = new UploadSignedAgreementCommand(
            ApplicationId: applicationId,
            UserId: userId,
            GeneratedVersion: model.GeneratedVersion,
            FileName: model.File.FileName,
            ContentType: model.File.ContentType ?? "",
            Size: model.File.Length,
            Content: stream);

        var result = await _signedUploadService.UploadAsync(command);

        return RenderSignedUploadRedirect(applicationId, result,
            successMessage: "Convenio firmado cargado. A la espera de la decisión del revisor.");
    }

    [HttpPost("ReplaceUpload")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 50L * 1024 * 1024)]
    public async Task<IActionResult> ReplaceUpload(
        int applicationId, int signedUploadId, UploadSignedAgreementViewModel model)
    {
        if (!ModelState.IsValid || model.File is null || model.File.Length == 0)
        {
            TempData["FundingAgreementError"] = SignedPdfRequiredMessage;
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        await using var stream = model.File.OpenReadStream();
        var command = new ReplaceSignedUploadCommand(
            ApplicationId: applicationId,
            UserId: userId,
            SignedUploadId: signedUploadId,
            GeneratedVersion: model.GeneratedVersion,
            FileName: model.File.FileName,
            ContentType: model.File.ContentType ?? "",
            Size: model.File.Length,
            Content: stream);

        var result = await _signedUploadService.ReplaceAsync(command);
        return RenderSignedUploadRedirect(applicationId, result,
            successMessage: "Convenio firmado reemplazado.");
    }

    [HttpPost("WithdrawUpload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WithdrawUpload(int applicationId, int signedUploadId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var command = new WithdrawSignedUploadCommand(applicationId, userId, signedUploadId);
        var result = await _signedUploadService.WithdrawAsync(command);

        return RenderSignedUploadRedirect(applicationId, result,
            successMessage: "Carga firmada retirada.");
    }

    [HttpPost("Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int applicationId, int signedUploadId, string? comment)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");
        var isReviewer = User.IsInRole("Reviewer");

        if (!isAdmin && !isReviewer)
        {
            LogUnauthorized(applicationId, "Approve", "role-forbidden");
            return NotFound();
        }

        var command = new ApproveSignedUploadCommand(
            ApplicationId: applicationId,
            ReviewerUserId: userId,
            IsAdministrator: isAdmin,
            IsReviewerAssigned: isReviewer,
            SignedUploadId: signedUploadId,
            Comment: comment);

        var result = await _signedUploadService.ApproveAsync(command);
        return RenderSignedUploadRedirect(applicationId, result,
            successMessage: "Convenio ejecutado.");
    }

    [HttpPost("Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int applicationId, int signedUploadId, string? comment)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Admin");
        var isReviewer = User.IsInRole("Reviewer");

        if (!isAdmin && !isReviewer)
        {
            LogUnauthorized(applicationId, "Reject", "role-forbidden");
            return NotFound();
        }

        var command = new RejectSignedUploadCommand(
            ApplicationId: applicationId,
            ReviewerUserId: userId,
            IsAdministrator: isAdmin,
            IsReviewerAssigned: isReviewer,
            SignedUploadId: signedUploadId,
            Comment: comment ?? "");

        var result = await _signedUploadService.RejectAsync(command);
        return RenderSignedUploadRedirect(applicationId, result,
            successMessage: "Carga rechazada; el solicitante puede enviar una nueva.");
    }

    [HttpGet("DownloadSigned/{signedUploadId:int}")]
    public async Task<IActionResult> DownloadSigned(int applicationId, int signedUploadId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        var isReviewer = User.IsInRole("Reviewer");

        var query = new GetSignedAgreementDownloadQuery(
            ApplicationId: applicationId,
            SignedUploadId: signedUploadId,
            UserId: userId,
            IsAdministrator: isAdmin,
            IsReviewerAssigned: isReviewer);

        var result = await _signedUploadService.GetDownloadAsync(query);
        if (!result.Authorized || result.Content is null)
        {
            LogUnauthorized(applicationId, "DownloadSigned", "access-denied-or-missing");
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-cache";
        return File(result.Content, result.ContentType ?? "application/pdf",
            fileDownloadName: result.FileName ?? $"SignedAgreement-{applicationId}.pdf");
    }

    [HttpGet("")]
    public async Task<IActionResult> Details(int applicationId)
    {
        var panel = await BuildPanelViewModelAsync(applicationId);
        if (panel is null)
        {
            LogUnauthorized(applicationId, "Details", "access-denied-or-missing");
            return NotFound();
        }

        var application = await _service.LoadForGenerationAsync(applicationId);
        FundingAgreementDocumentViewModel? preview = null;
        var hasApplicantResponse = false;

        if (application is not null)
        {
            preview = await BuildDocumentViewModelAsync(application);
            hasApplicantResponse = application.ApplicantResponses.Any();
        }

        var viewModel = new FundingAgreementDetailsViewModel
        {
            Panel = panel,
            Preview = preview,
            HasApplicantResponse = hasApplicantResponse
        };

        return View(viewModel);
    }

    /// <summary>
    /// Spec 011 US3 — signing ceremony surface (research §6). The action is
    /// bookmark-safe: <c>TempData["CeremonyFresh"]</c> drives the celebratory
    /// motion path; bookmarks see <c>IsFresh = false</c> and render the static
    /// summary state.
    /// </summary>
    [HttpGet("SignCeremony")]
    public async Task<IActionResult> SignCeremony(int applicationId, CancellationToken ct)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null) return NotFound();

        var isFresh = TempData["CeremonyFresh"] is bool b && b;

        var fa = application.FundingAgreement;
        var hasSignedUpload = fa is not null && fa.SignedUploads.Any(s => s.Status == Domain.Enums.SignedUploadStatus.Approved);
        var executed = application.State == Domain.Enums.ApplicationState.AgreementExecuted;

        SigningCeremonyVariant variant;
        if (executed) variant = SigningCeremonyVariant.BothCompleteApplicantLast;
        else if (hasSignedUpload) variant = SigningCeremonyVariant.ApplicantOnlySigned;
        else variant = SigningCeremonyVariant.FunderOnlySigned;

        // Sum the lowest-price quotation per item as a proxy for the funded amount.
        decimal totalAmount = 0m;
        string currencyCode = "USD";
        foreach (var item in application.Items)
        {
            var lowest = item.Quotations.OrderBy(q => q.Price).FirstOrDefault();
            if (lowest is null) continue;
            totalAmount += lowest.Price;
            currencyCode = lowest.Currency;
        }

        var firstName = application.Applicant?.FirstName ?? "there";
        var dashboardHref = "/";
        var detailsHref = Url.Action("Details", "FundingAgreement", new { applicationId }) ?? $"/Applications/{applicationId}/FundingAgreement";

        var vm = new SigningCeremonyViewModel(
            ApplicationId: Guid.Empty,
            Variant: variant,
            IsFresh: isFresh,
            ApplicantFirstName: firstName,
            ProjectName: application.Items.FirstOrDefault()?.ProductName ?? $"Application #{applicationId}",
            FundedAmount: totalAmount,
            CurrencyCode: currencyCode,
            DisbursementDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            ViewFundingDetailsHref: detailsHref,
            DashboardHref: dashboardHref);

        return View("Sign/Ceremony", vm);
    }

    private IActionResult RenderSignedUploadRedirect(
        int applicationId, SignedUploadResult result, string successMessage)
    {
        if (result.Success)
        {
            TempData["FundingAgreementSuccess"] = successMessage;
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        if (result.ConflictDetected)
        {
            TempData["FundingAgreementError"] = result.Error is null
                ? _errorTranslator.Translate(UserFacingErrorCode.ConcurrentSignedUploadModification)
                : _errorTranslator.Translate(result.Error);
            return StatusCode(409);
        }

        // Resource-not-found (or null) — short-circuit to a 404 surface;
        // TempData is set so the "Details" page shows it on the return-to-list
        // side-effect path.
        if (result.Error is null || result.Error.Code == UserFacingErrorCode.SignedUploadResourceNotFound)
        {
            TempData["FundingAgreementError"] = _errorTranslator.Translate(
                UserFacingErrorCode.SignedUploadResourceNotFound);
            return NotFound();
        }

        TempData["FundingAgreementError"] = _errorTranslator.Translate(result.Error);
        return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
    }

    private async Task<SigningStagePanelViewModel?> BuildPanelViewModelAsync(int applicationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdministrator = User.IsInRole("Admin");
        var isReviewer = User.IsInRole("Reviewer");

        var dto = await _signedUploadService.GetPanelAsync(new GetSigningStagePanelQuery(
            ApplicationId: applicationId,
            UserId: userId,
            IsAdministrator: isAdministrator,
            IsReviewerAssigned: isReviewer));

        if (dto is null) return null;

        return MapToViewModel(dto);
    }

    private SigningStagePanelViewModel MapToViewModel(SigningStagePanelDto dto)
    {
        string? downloadUrl = null;
        if (dto.AgreementExists)
        {
            downloadUrl = Url.RouteUrl(new
            {
                controller = "FundingAgreement",
                action = "Download",
                applicationId = dto.ApplicationId
            });
        }

        string? approvedDownloadUrl = null;
        if (dto.ApprovedSignedUploadId.HasValue)
        {
            approvedDownloadUrl = Url.RouteUrl(new
            {
                controller = "FundingAgreement",
                action = "DownloadSigned",
                applicationId = dto.ApplicationId,
                signedUploadId = dto.ApprovedSignedUploadId.Value
            });
        }

        return new SigningStagePanelViewModel
        {
            ApplicationId = dto.ApplicationId,
            AgreementExists = dto.AgreementExists,
            AgreementDownloadUrl = downloadUrl,
            CanGenerate = dto.CanGenerate,
            CanRegenerate = dto.CanRegenerate,
            DisabledReason = dto.DisabledReason,
            GeneratedAtUtc = dto.GeneratedAtUtc,
            GeneratedByDisplayName = dto.GeneratedByDisplayName,
            GeneratedVersion = dto.GeneratedVersion,
            ShowActions = User.IsInRole("Admin") || User.IsInRole("Reviewer"),
            PendingUpload = dto.PendingUpload,
            LastDecision = dto.LastDecision,
            ApprovedSignedUploadId = dto.ApprovedSignedUploadId,
            ApprovedSignedDownloadUrl = approvedDownloadUrl,
            CanApplicantUpload = dto.CanApplicantUpload,
            CanApplicantReplaceOrWithdraw = dto.CanApplicantReplaceOrWithdraw,
            CanReviewerAct = dto.CanReviewerAct,
            IsExecuted = dto.IsExecuted
        };
    }

    private async Task<FundingAgreementDocumentViewModel> BuildDocumentViewModelAsync(AppEntity application)
    {
        var options = _agreementOptions.Value;
        var funder = _funderOptions.Value;

        var applicant = application.Applicant;
        var items = application.Items
            .Where(i => i.ReviewStatus == Domain.Enums.ItemReviewStatus.Approved
                && i.SelectedSupplierId is not null)
            .ToList();

        var latestResponse = application.ApplicantResponses
            .OrderByDescending(r => r.CycleNumber)
            .FirstOrDefault();

        var acceptedItemIds = latestResponse?.ItemResponses
            .Where(ir => ir.Decision == Domain.Enums.ItemResponseDecision.Accept)
            .Select(ir => ir.ItemId)
            .ToHashSet() ?? new HashSet<int>();

        var rows = new List<FundingAgreementItemRowDto>();
        decimal total = 0m;

        foreach (var item in items.Where(i => acceptedItemIds.Contains(i.Id)))
        {
            var quotation = item.Quotations
                .FirstOrDefault(q => q.SupplierId == item.SelectedSupplierId);
            if (quotation is null) continue;

            var supplierName = quotation.Supplier?.Name ?? "(Supplier)";
            var price = quotation.Price;

            rows.Add(new FundingAgreementItemRowDto(
                ItemId: item.Id,
                ProductName: item.ProductName,
                CategoryName: item.Category?.Name ?? string.Empty,
                SupplierName: supplierName,
                UnitPrice: price,
                LineTotal: price,
                Currency: quotation.Currency));

            total += price;
        }

        var totalsByCurrency = rows
            .GroupBy(r => r.Currency)
            .Select(g => new CurrencyTotal(g.Key, g.Sum(r => r.LineTotal)))
            .OrderBy(t => t.Currency)
            .ToList();

        var applicantFullName = applicant is null
            ? string.Empty
            : $"{applicant.FirstName} {applicant.LastName}".Trim();

        return new FundingAgreementDocumentViewModel
        {
            AgreementReference = application.Id.ToString(),
            GeneratedAtUtc = DateTime.UtcNow,
            Funder = funder,
            ApplicantLegalName = applicantFullName,
            ApplicantLegalId = applicant?.LegalId ?? string.Empty,
            ApplicantEmail = applicant?.Email ?? string.Empty,
            ApplicantPhone = applicant?.Phone,
            LocaleCode = string.IsNullOrWhiteSpace(options.LocaleCode) ? "es-CR" : options.LocaleCode,
            CurrencyIsoCode = string.IsNullOrWhiteSpace(options.CurrencyIsoCode) ? "COP" : options.CurrencyIsoCode,
            Items = rows,
            TotalAmount = total,
            TotalsByCurrency = totalsByCurrency
        };
    }

    private void LogUnauthorized(int applicationId, string action, string reasonCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "(anonymous)";
        _logger.LogInformation(
            "Funding agreement authorization rejected. applicationId={ApplicationId} userId={UserId} action={Action} reasonCode={ReasonCode}",
            applicationId, userId, action, reasonCode);
    }
}
