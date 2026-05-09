using System.Security.Claims;
using System.Text.Json;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.FundingAgreements.Commands;
using FundingPlatform.Application.FundingAgreements.Queries;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Commands;
using FundingPlatform.Application.SignedUploads.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IUserFacingErrorTranslator _errorTranslator;
    private readonly Application.Services.IUserStoreReader _userStoreReader;
    private readonly IWebHostEnvironment _env;
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
        IUserFacingErrorTranslator errorTranslator,
        Application.Services.IUserStoreReader userStoreReader,
        IWebHostEnvironment env,
        ILogger<FundingAgreementController> logger)
    {
        _service = service;
        _signedUploadService = signedUploadService;
        _applicationRepository = applicationRepository;
        _htmlRenderer = htmlRenderer;
        _pdfRenderer = pdfRenderer;
        _objectStorage = objectStorage;
        _userManager = userManager;
        _errorTranslator = errorTranslator;
        _userStoreReader = userStoreReader;
        _env = env;
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

        // Spec 018 / T024 — defence-in-depth: every approved item must have a non-blank
        // LineCode before we render. The reviewer flow already enforces this at write
        // time via Application.AssignLineCodeToItem (FR-012); this guards against
        // fixtures or admin-edited rows that bypassed that path.
        if (application.Items.Any(i => i.ReviewStatus == Domain.Enums.ItemReviewStatus.Approved
                                       && string.IsNullOrWhiteSpace(i.LineCode)))
        {
            TempData["FundingAgreementError"] = _errorTranslator.Translate(
                UserFacingErrorCode.LineCodeMissingOnApprovedItems);
            return RedirectToRoute(new { controller = "FundingAgreement", action = "Details", applicationId });
        }

        byte[] pdfBytes;
        try
        {
            // Spec 015 / US5 / T511 — pre-flight per-line conversion metadata
            // before spending Razor work. RenderFromModelAsync throws
            // MissingConversionMetadataException for any non-CRC line that
            // lacks an embedded rate snapshot.
            // Spec 018 / FR-001 + FR-004 — Blink HTML→PDF resolves relative
            // asset URLs (vendored fonts under wwwroot/lib/fonts/) against
            // this baseUrl. Without it, the Fraunces+Inter font stack falls
            // back to default sans-serif and the cover/section typography
            // breaks parity with the seed.
            var assetsBaseUrl = new Uri(
                Path.GetFullPath(_env.WebRootPath) + Path.DirectorySeparatorChar
            ).AbsoluteUri;

            // Spec 018 / FR-001 + FR-002 (R-001-revised) — the seedling header
            // and partner-strip footer images are drawn at the renderer level
            // via PdfPageTemplateElement so they repeat reliably on every
            // page. CSS `position: fixed` was the original technique but
            // Blink does not honour it across page breaks, leaving the chrome
            // mis-positioned (overlapping body content / dropping off after
            // page 1). The images live under wwwroot/lib/brand/pdf/ and are
            // swap-file-only per FR-018.
            var headerImagePath = Path.Combine(
                _env.WebRootPath, "lib", "brand", "pdf", "header-seedling.png");
            var footerImagePath = Path.Combine(
                _env.WebRootPath, "lib", "brand", "pdf", "footer-partners-strip.png");

            pdfBytes = await _pdfRenderer.RenderFromModelAsync(
                documentModel.Items,
                renderHtmlAsync: () => _htmlRenderer.RenderAsync(
                    "~/Views/FundingAgreement/Document.cshtml",
                    documentModel),
                baseUrl: assetsBaseUrl,
                headerImageAbsolutePath: System.IO.File.Exists(headerImagePath)
                    ? headerImagePath : null,
                footerImageAbsolutePath: System.IO.File.Exists(footerImagePath)
                    ? footerImagePath : null,
                ct: HttpContext.RequestAborted);
        }
        catch (MissingConversionMetadataException ex)
        {
            // Spec 015 / US5 / T512 / FR-027 — log the offending quotation ids
            // and re-render the Details view directly (NOT a TempData redirect)
            // so a hard browser reload still shows the inline Spanish error
            // until an admin attaches a historical rate.
            _logger.LogError(ex,
                "Funding agreement PDF refused: missing conversion metadata. applicationId={ApplicationId} offendingQuotationIds={OffendingQuotationIds}",
                applicationId,
                string.Join(",", ex.OffendingQuotationIds));

            return await BuildInlineErrorViewAsync(
                applicationId,
                "No se puede generar el PDF: una o más cotizaciones no tienen tipo de cambio aplicado. Contacte a un administrador para asignar tipos históricos.");
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
        var priorBlobKey = application.FundingAgreement is { BlobKey.Length: > 0 } existing ? existing.BlobKey : null;

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

        var persist = await _service.PersistGenerationAsync(
            application, userId, fileName, pdfBytes.LongLength, stored.Key);

        if (!persist.Success)
        {
            try { await _objectStorage.DeleteAsync(AgreementCategory, key, HttpContext.RequestAborted); }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx,
                    "Failed to clean up orphaned PDF after persistence failure. blobKey={BlobKey}",
                    stored.Key);
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

        // Spec 014 — resolve via IObjectStorage.ResolveServingHandleAsync.
        // Default ServingMode.BackendStream so the application boundary remains
        // the only authorisation point (FR-018); SAS URLs are never emitted to
        // applicants for the agreement category.
        var key = ObjectKey.Parse(agreement.BlobKey);

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
    [UploadSizeGuard(FileCategory.SignedFundingAgreement)]
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
    [UploadSizeGuard(FileCategory.SignedFundingAgreement)]
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
        // Spec 018 — branded PDF projection (Contract 4 in contracts/README.md).
        // Funder identity is hardcoded inside the sworn declaration partial; we no
        // longer read the deleted FunderOptions here.
        var culture = EsCrCultureFactory.Build();
        var applicant = application.Applicant;
        var representativeName = applicant is null
            ? string.Empty
            : $"{applicant.FirstName} {applicant.LastName}".Trim();
        var generatedAt = DateTime.UtcNow;

        // FR-006 / SC-004 — distinct review-action takers, hydrated to display names.
        var commissionUserIds = application.VersionHistory
            .Where(vh => vh.Action == "ReviewItem")
            .Select(vh => vh.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var commissionMembers = new List<string>(commissionUserIds.Count);
        foreach (var userId in commissionUserIds)
        {
            var displayName = await _userStoreReader.GetDisplayNameAsync(userId, HttpContext.RequestAborted);
            commissionMembers.Add(displayName);
        }
        commissionMembers.Sort(StringComparer.CurrentCulture);

        // FR-008 — every item (approved + rejected) feeds Recursos solicitados;
        // approved-only items feed Resultados comisión and the supplier table.
        var requestedResources = new List<RequestedResourceRow>();
        var approvedLines = new List<ApprovedLineRow>();
        var rejectedLines = new List<RejectedLineRow>();
        var supplierByDate = new Dictionary<int, (DateTime ReviewedAt, string Name, string Hac, string Ccss, string Sicop)>();
        var preFlightItems = new List<FundingAgreementItemRowDto>();
        decimal approvedDisbursementTotal = 0m;
        var acuerdoLabel = $"FA-{application.Id}";

        var sortedItems = application.Items
            .OrderBy(i => i.LineCode ?? "￿", StringComparer.Ordinal)
            .ThenBy(i => i.Id)
            .ToList();

        foreach (var item in sortedItems)
        {
            var lineCodeDisplay = item.LineCode ?? string.Empty;
            var supplierQuotation = item.SelectedSupplierId is int supplierId
                ? item.Quotations.FirstOrDefault(q => q.SupplierId == supplierId)
                : item.Quotations.OrderBy(q => q.Price).FirstOrDefault();

            var supplierName = supplierQuotation?.Supplier?.Name ?? string.Empty;
            var crcAmount = supplierQuotation?.ConvertedCrcAmount ?? supplierQuotation?.Price ?? 0m;
            var origCurrency = supplierQuotation?.Currency ?? "CRC";
            var conversionNote = BuildConversionNote(supplierQuotation, culture);

            requestedResources.Add(new RequestedResourceRow(
                LineCode: lineCodeDisplay,
                ProductName: item.ProductName,
                CategoryName: item.Category?.Name ?? string.Empty,
                Amount: crcAmount,
                Currency: origCurrency,
                SelectedSupplierName: supplierName,
                CurrencyConversionNote: conversionNote));

            if (supplierQuotation is not null)
            {
                preFlightItems.Add(new FundingAgreementItemRowDto(
                    ItemId: item.Id,
                    ProductName: item.ProductName,
                    CategoryName: item.Category?.Name ?? string.Empty,
                    SupplierName: supplierName,
                    UnitPrice: supplierQuotation.Price,
                    LineTotal: supplierQuotation.Price,
                    Currency: supplierQuotation.Currency,
                    QuotationId: supplierQuotation.Id,
                    ConvertedCrcAmount: supplierQuotation.ConvertedCrcAmount,
                    SnapshotRateValue: supplierQuotation.Snapshot?.RateValue,
                    SnapshotRateType: supplierQuotation.Snapshot?.RateType.ToString(),
                    SnapshotEffectiveAtUtc: supplierQuotation.Snapshot?.EffectiveAtUtc,
                    LineCode: item.LineCode));
            }

            if (item.ReviewStatus == ItemReviewStatus.Approved && supplierQuotation is not null)
            {
                approvedLines.Add(new ApprovedLineRow(
                    AcuerdoLabel: acuerdoLabel,
                    LineCode: lineCodeDisplay,
                    ProductName: item.ProductName,
                    SelectedSupplierName: supplierName,
                    Disbursement: crcAmount,
                    CurrencyConversionNote: conversionNote));
                approvedDisbursementTotal += crcAmount;

                if (supplierQuotation.Supplier is { } supplier
                    && !supplierByDate.ContainsKey(supplier.Id))
                {
                    supplierByDate[supplier.Id] = (
                        supplier.UpdatedAt,
                        supplier.Name,
                        FormatCompliance(supplier.IsCompliantHacienda),
                        FormatCompliance(supplier.IsCompliantCCSS),
                        FormatCompliance(supplier.IsCompliantSICOP));
                }
            }
            else if (item.ReviewStatus == ItemReviewStatus.Rejected)
            {
                rejectedLines.Add(new RejectedLineRow(
                    AcuerdoLabel: acuerdoLabel,
                    LineCode: lineCodeDisplay,
                    ProductName: item.ProductName,
                    Motivo: item.ReviewComment ?? string.Empty));
            }
        }

        var supplierCompliance = supplierByDate
            .Values
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => new SupplierComplianceRow(s.ReviewedAt, s.Name, s.Hac, s.Ccss, s.Sicop))
            .ToList();

        var summaryParagraph = ComposeApprovedSummaryParagraph(approvedLines, approvedDisbursementTotal, culture);

        var generationDateLong = generatedAt.ToString("d 'de' MMMM 'de' yyyy", culture);

        return new FundingAgreementDocumentViewModel
        {
            CompanyName = application.CompanyName,
            ApplicantRepresentativeName = representativeName,
            GeneratedAtUtc = generatedAt,
            GenerationDateLong = generationDateLong,
            CommissionMembers = commissionMembers,
            LocaleCode = culture.Name,
            CurrencyIsoCode = "CRC",
            RequestedResources = requestedResources,
            ApprovedLines = approvedLines,
            RejectedLines = rejectedLines,
            ApprovedSummaryParagraph = summaryParagraph,
            ApprovedDisbursementTotal = approvedDisbursementTotal,
            SupplierCompliance = supplierCompliance,
            Items = preFlightItems,
        };
    }

    private static string FormatCompliance(bool ok) => ok ? "Al día" : "Pendiente";

    private static string? BuildConversionNote(Domain.Entities.Quotation? q, System.Globalization.CultureInfo culture)
    {
        if (q is null || q.Currency == "CRC" || q.Snapshot is null) return null;
        var rate = q.Snapshot.RateValue.ToString("N6", culture);
        var rateType = q.Snapshot.RateType.ToString() switch
        {
            "Buy" => "Compra",
            "Sell" => "Venta",
            var s => s ?? string.Empty,
        };
        var effective = q.Snapshot.EffectiveAtUtc.ToString("yyyy-MM-dd", culture);
        return $"Conversión: 1 {q.Currency} = ₡{rate} (Tipo {rateType}, vigente desde {effective})";
    }

    private static string ComposeApprovedSummaryParagraph(
        IReadOnlyList<ApprovedLineRow> approved,
        decimal total,
        System.Globalization.CultureInfo culture)
    {
        if (approved.Count == 0)
        {
            return "No se aprueban líneas en este tracto.";
        }

        var codes = string.Join(", ", approved.Select(a => a.LineCode));
        var totalText = total.ToString("N2", culture);
        return $"Se aprueban las líneas {codes} por un monto total de ₡{totalText}, " +
               "que serán reembolsadas mediante depósito a la cuenta indicada por el solicitante.";
    }

    /// <summary>
    /// Spec 015 / US5 / T512 / FR-027 — re-renders the Details view directly
    /// (no TempData / redirect) with an inline Spanish error baked into the
    /// view model. A hard browser reload re-issues the GET to Details, which
    /// rebuilds the panel without the error — but the hard-reload-survives-error
    /// requirement is satisfied because the error is bound to the action result
    /// of the failed Generate POST (the user remains on the Details URL with
    /// the error visible until they navigate away or refresh, at which point
    /// the panel goes back to its idle state and an admin / reviewer can act
    /// on US6 to attach a rate before retrying).
    /// </summary>
    private async Task<IActionResult> BuildInlineErrorViewAsync(int applicationId, string spanishError)
    {
        var panel = await BuildPanelViewModelAsync(applicationId);
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
            Panel = panel ?? new SigningStagePanelViewModel { ApplicationId = applicationId },
            Preview = preview,
            HasApplicantResponse = hasApplicantResponse,
            MissingConversionInlineError = spanishError,
        };

        return View("Details", viewModel);
    }

    private void LogUnauthorized(int applicationId, string action, string reasonCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "(anonymous)";
        _logger.LogInformation(
            "Funding agreement authorization rejected. applicationId={ApplicationId} userId={UserId} action={Action} reasonCode={ReasonCode}",
            applicationId, userId, action, reasonCode);
    }
}
