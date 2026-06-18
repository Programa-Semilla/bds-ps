using System.Security.Claims;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Routing;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Queries;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using FundingPlatform.Web.ViewModels.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Reviewer,Admin")]
public class ReviewController : Controller
{
    private readonly ReviewService _reviewService;
    private readonly SignedUploadService _signedUploadService;
    private readonly IReviewerQueueProjection _queueProjection;
    private readonly IUserFacingErrorTranslator _errorTranslator;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _applicationRepository;
    private readonly AppDbContext _dbContext;
    private readonly IStageExpiryEvaluator _stageExpiry;
    private readonly IStageExpiryClock _stageExpiryClock;
    private readonly IComparisonOrchestrator _comparisonOrchestrator;
    private readonly IDecisionSummaryProjection _decisionSummary;
    // Spec 027 / US5 — reviewer/admin write surface for the applicant's CodigoPersonal.
    private readonly Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> _userManager;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public ReviewController(
        ReviewService reviewService,
        SignedUploadService signedUploadService,
        IReviewerQueueProjection queueProjection,
        IUserFacingErrorTranslator errorTranslator,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository applicationRepository,
        AppDbContext dbContext,
        IStageExpiryEvaluator stageExpiry,
        IStageExpiryClock stageExpiryClock,
        IComparisonOrchestrator comparisonOrchestrator,
        IDecisionSummaryProjection decisionSummary,
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _reviewService = reviewService;
        _signedUploadService = signedUploadService;
        _queueProjection = queueProjection;
        _errorTranslator = errorTranslator;
        _scopeProvider = scopeProvider;
        _applicationRepository = applicationRepository;
        _dbContext = dbContext;
        _stageExpiry = stageExpiry;
        _stageExpiryClock = stageExpiryClock;
        _comparisonOrchestrator = comparisonOrchestrator;
        _decisionSummary = decisionSummary;
        _userManager = userManager;
        _configuration = configuration;
    }

    /// <summary>
    /// Spec 021 / T119 / FR-024 — builds a per-row stage countdown banner map
    /// keyed by Application.Id. The reviewer queue + signing inbox views look
    /// up entries by their row's ApplicationId so the partial can render
    /// inline. Rows whose Application is in a terminal state get no entry
    /// (the view conditionally renders).
    /// </summary>
    private async Task<Dictionary<int, StageCountdownBannerViewModel>> BuildStageBannersAsync(
        IEnumerable<int> applicationIds, CancellationToken ct)
    {
        var ids = applicationIds.Distinct().ToList();
        if (ids.Count == 0) return new();

        var entities = await _dbContext.Applications
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(ct);

        var now = _stageExpiryClock.UtcNow;
        var map = new Dictionary<int, StageCountdownBannerViewModel>();
        foreach (var e in entities)
        {
            var (stage, enteredAt, closesAt) = await _stageExpiry.EvaluateForAsync(e, ct);
            map[e.Id] = new StageCountdownBannerViewModel
            {
                StageKind = stage,
                EnteredAt = enteredAt,
                ClosesAt = closesAt,
                Now = now,
                Closed = now >= closesAt,
            };
        }
        return map;
    }

    private static int ParseApplicationIdFromNumber(string applicationNumber)
    {
        if (string.IsNullOrEmpty(applicationNumber)) return 0;
        var idx = applicationNumber.LastIndexOf('-');
        if (idx < 0 || idx == applicationNumber.Length - 1) return 0;
        return int.TryParse(applicationNumber.AsSpan(idx + 1), out var id) ? id : 0;
    }

    /// <summary>
    /// Spec 016 — fetches the request's reviewer scope. Admin callers always
    /// see <c>IsAdmin = true</c>; reviewers get their current group ids fresh
    /// from the DB (NFR-003 — membership changes take effect on next request).
    /// </summary>
    private Task<IReviewerScope> GetScopeAsync(CancellationToken ct) =>
        _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);

    [HttpGet]
    [Route("Review/SigningInbox")]
    public async Task<IActionResult> SigningInbox(int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 25;

        var scope = await GetScopeAsync(ct);
        var query = new GetSigningInboxQuery(
            CurrentUserId: GetUserId(),
            IsAdministrator: scope.IsAdmin,
            ReviewerGroupIds: scope.GroupIds,
            Page: page,
            PageSize: pageSize);

        var result = await _signedUploadService.GetInboxAsync(query);

        var rows = result.Rows
            .Select(r => new SigningInboxRowViewModel
            {
                ApplicationId = r.ApplicationId,
                ApplicantDisplayName = r.ApplicantDisplayName,
                SignedUploadId = r.SignedUploadId,
                UploadedAtUtc = r.UploadedAtUtc,
                GeneratedVersionAtUpload = r.GeneratedVersionAtUpload,
                VersionMatchesCurrent = r.VersionMatchesCurrent
            })
            .ToList();

        ViewData["SigningInbox.Page"] = page;
        ViewData["SigningInbox.PageSize"] = pageSize;
        ViewData["SigningInbox.TotalCount"] = result.TotalCount;

        // Spec 021 / T119 / FR-024 — per-row stage banners keyed by ApplicationId.
        ViewData["StageBanners"] = await BuildStageBannersAsync(
            rows.Select(r => r.ApplicationId), ct);

        return View(rows);
    }

    [HttpGet]
    [Route("Review/GenerateAgreement")]
    public async Task<IActionResult> GenerateAgreement(int page = 1)
    {
        if (page < 1) page = 1;

        var (items, totalCount) = await _reviewService.GetGenerateAgreementQueueAsync(page);

        const int pageSize = 25;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var viewModel = new GenerateAgreementQueueViewModel
        {
            Applications = items.Select(i => new GenerateAgreementQueueItemViewModel
            {
                ApplicationId = i.ApplicationId,
                ApplicantDisplayName = i.ApplicantDisplayName,
                ResponseFinalizedAtUtc = i.ResponseFinalizedAtUtc,
            }).ToList(),
            CurrentPage = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        ReviewerFilter filter = ReviewerFilter.All,
        string? search = null,
        CancellationToken ct = default)
    {
        // Spec 011 US4 (FR-052) — Index renders the new QueueDashboard via the projection.
        // Spec 016 — composes the reviewer scope (FR-011) and a FR-014 search term.
        var firstName = User.Identity?.Name ?? "Reviewer";
        var scope = await GetScopeAsync(ct);
        var dto = await _queueProjection.GetForReviewerAsync(GetUserId(), firstName, filter, scope, search, ct);
        ViewData["ReviewQueue.Search"] = search;

        // Spec 021 / T119 / FR-024 — per-row stage banner map keyed by the
        // numeric Application.Id encoded in ApplicationNumber (APP-{id:D5}).
        ViewData["StageBanners"] = await BuildStageBannersAsync(
            dto.Rows.Select(r => ParseApplicationIdFromNumber(r.ApplicationNumber)).Where(id => id > 0),
            ct);

        return View("QueueDashboard", dto);
    }

    [HttpGet]
    [Route("Review/QueueRows")]
    public async Task<IActionResult> QueueRows(
        ReviewerFilter filter = ReviewerFilter.All,
        string? search = null,
        CancellationToken ct = default)
    {
        // Spec 011 US4 (FR-054) — chip-reflow contract; spec 016 composes the
        // reviewer scope and the FR-014 search term.
        var scope = await GetScopeAsync(ct);
        var rows = await _queueProjection.GetRowsAsync(GetUserId(), filter, scope, search, ct);
        // Spec 021 / T119 / FR-024 — per-row stage banner map for the chip-reflow fragment.
        ViewData["StageBanners"] = await BuildStageBannersAsync(
            rows.Select(r => ParseApplicationIdFromNumber(r.ApplicationNumber)).Where(id => id > 0),
            ct);
        return PartialView("_ReviewerQueueRows", rows);
    }

    [HttpGet]
    [Route(ReviewRoutes.ReviewTemplate)]
    public async Task<IActionResult> Review(int id, CancellationToken ct = default)
    {
        var dto = await _reviewService.GetApplicationForReviewAsync(id);
        if (dto is null)
            return NotFound();

        // Spec 016 / FR-012, NFR-002 — non-admin reviewers may only open an
        // application if their group set intersects the applicant's. Admins
        // are exempt (FR-015). Applicants own their applications via a
        // separate authorization path; this controller is only reachable by
        // Reviewer/Admin (per the [Authorize] attribute), so we only need to
        // gate the reviewer side.
        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(
                id, scope.GroupIds, ct);
            if (!allowed)
            {
                return Forbid();
            }
        }

        var viewModel = MapToViewModel(dto);
        viewModel.IsAdmin = User.IsInRole("Admin");
        viewModel.PollIntervalSeconds = int.TryParse(_configuration["AiComparison:PollIntervalSeconds"], out var ps) ? ps : 3;

        // Spec 027 / US4 — project the shared decision summary from the loaded
        // aggregate so the reviewer sees the same per-line block as every other
        // surface, alongside the unchanged interactive capture controls.
        var aggregate = await _applicationRepository.GetByIdWithDetailsAsync(id);
        if (aggregate is not null)
        {
            viewModel.DecisionSummary = _decisionSummary.Project(aggregate);

            // Spec 027 / US5 — prefill the applicant-code write control with the
            // current value on the application owner's account.
            var applicantUserId = aggregate.Applicant?.UserId;
            if (!string.IsNullOrEmpty(applicantUserId))
            {
                var applicantUser = await _userManager.FindByIdAsync(applicantUserId);
                viewModel.ApplicantCodigoPersonal = applicantUser?.CodigoPersonal;
            }
        }

        // Spec 020 / US2 — hydrate per-item comparison region with the cached
        // artifact + freshness signal. Items with < 2 quotations get a
        // placeholder VM so the view can render the explanatory tooltip.
        foreach (var item in viewModel.Items)
        {
            var cached = await _comparisonOrchestrator.GetCachedComparisonAsync(item.ItemId, ct);
            item.Comparison = new ItemComparisonViewModel
            {
                ApplicationItemId = item.ItemId,
                HasArtifact = cached is not null,
                ArtifactJson = cached?.ArtifactJson,
                LastUpdatedAt = cached?.GeneratedAt,
                Freshness = cached?.Freshness ?? Freshness.None,
                ChangedInputs = cached?.ChangedInputs ?? Array.Empty<ChangedInput>(),
                HasMinimumSuppliers = item.Quotations.Count >= 2,
                IsAdmin = viewModel.IsAdmin,
            };
        }

        return View(viewModel);
    }

    /// <summary>Spec 020 / FR-A1 — sync per-item generation endpoint.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/GenerateComparison/{applicationItemId:int}")]
    public async Task<IActionResult> GenerateComparison(int applicationItemId, [FromBody] GenerateComparisonRequest? body, CancellationToken ct)
    {
        body ??= new GenerateComparisonRequest();

        // Resolve the parent application + group-scope guard.
        var parentApplicationId = await _reviewService.GetApplicationIdForItemAsync(applicationItemId, ct);
        if (parentApplicationId is null) return NotFound();

        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(parentApplicationId.Value, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        var actorRole = User.IsInRole("Admin") ? "Admin" : "Reviewer";
        var bypassRateLimit = body.BypassRateLimit && actorRole == "Admin";
        var bypassTokenCap = body.BypassTokenCap && actorRole == "Admin";

        var hardTimeout = int.TryParse(_configuration["AiComparison:SyncHardTimeoutSeconds"], out var hts) ? hts : 90;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(hardTimeout));

        try
        {
            var result = await _comparisonOrchestrator.GenerateAsync(new GenerateComparisonCommand(
                ApplicationItemId: applicationItemId,
                ActorUserId: GetUserId(),
                ActorRole: actorRole,
                BypassRateLimit: bypassRateLimit,
                BypassTokenCap: bypassTokenCap,
                ForceRegenerate: body.ForceRegenerate), cts.Token);

            return result switch
            {
                GenerateComparisonSuccess s => SuccessResponse(new ItemComparisonViewModel
                {
                    ApplicationItemId = s.ApplicationItemId,
                    HasArtifact = true,
                    ArtifactJson = s.ArtifactJson,
                    LastUpdatedAt = s.GeneratedAt,
                    Freshness = s.Freshness,
                    ChangedInputs = s.ChangedInputs,
                    HasMinimumSuppliers = true,
                    IsAdmin = actorRole == "Admin",
                }),
                GenerateComparisonFailure f => ToErrorEnvelope(f),
                _ => StatusCode(500, new { code = "unknown" }),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { code = "timeout" });
        }
        catch (ConcurrentGenerationException)
        {
            return Conflict(new { code = "concurrent_generation" });
        }
    }

    /// <summary>Spec 020 / FR-F2 — per-item poll endpoint.</summary>
    [HttpGet]
    [Route("Review/ItemStatus/{applicationItemId:int}")]
    public async Task<IActionResult> ItemStatus(int applicationItemId, CancellationToken ct)
    {
        var parentApplicationId = await _reviewService.GetApplicationIdForItemAsync(applicationItemId, ct);
        if (parentApplicationId is null) return NotFound();

        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(parentApplicationId.Value, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        Response.Headers.CacheControl = "no-store";
        var status = await _comparisonOrchestrator.GetStatusAsync(applicationItemId, ct);
        return Ok(new
        {
            applicationItemId = status.ApplicationItemId,
            state = status.State.ToString(),
            freshness = status.Freshness.ToString(),
            changedInputs = status.ChangedInputs.Select(c => c.ToString()).ToArray(),
            lastUpdatedAt = status.LastUpdatedAt,
            failureReason = status.FailureReason,
        });
    }

    /// <summary>
    /// Spec 020 / FINDING-9 — when the caller asks for HTML (Accept: text/html or
    /// X-Requested-With: XMLHttpRequest with text/html in Accept), return the
    /// rendered <c>_ComparisonRegion</c> partial so the JS can do an inline
    /// outerHTML swap rather than a full window.location.reload. JSON callers
    /// (and the default tooling) still get the ItemComparisonViewModel envelope.
    /// </summary>
    private IActionResult SuccessResponse(ItemComparisonViewModel vm)
    {
        var accept = Request.Headers["Accept"].ToString();
        var wantsHtml = !string.IsNullOrEmpty(accept)
            && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        if (wantsHtml)
        {
            return PartialView("_ComparisonRegion", vm);
        }
        return Ok(vm);
    }

    private IActionResult ToErrorEnvelope(GenerateComparisonFailure f) => f.FailureReason switch
    {
        "single_supplier" => BadRequest(new { code = "single_supplier" }),
        "unsupported_format" => BadRequest(new { code = "unsupported_format", offendingInput = f.OffendingInput }),
        "pii_redaction_failed" => BadRequest(new { code = "pii_redaction_failed", offendingInput = f.OffendingInput }),
        "rate_limit_exceeded" => UnprocessableEntity(new { code = "rate_limit_exceeded", remaining = 0, windowResetsAt = f.WindowResetsAt }),
        "token_cap_exceeded" => UnprocessableEntity(new { code = "token_cap_exceeded", estimatedTokens = f.EstimatedTokens, cap = f.Cap, offendingInput = f.OffendingInput }),
        "application_closed" => Conflict(new { code = "application_closed" }),
        "schema_invalid" => StatusCode(500, new { code = "schema_invalid", validatorPath = f.OffendingInput }),
        "provider_transient" => StatusCode(502, new { code = "provider_transient" }),
        var r when r != null && r.StartsWith("provider_hard") => StatusCode(502, new { code = "provider_hard", providerCode = f.ProviderCode }),
        _ => StatusCode(500, new { code = f.FailureReason ?? "unknown" }),
    };

    public class GenerateComparisonRequest
    {
        public bool BypassRateLimit { get; set; }
        public bool BypassTokenCap { get; set; }
        public bool ForceRegenerate { get; set; }
    }

    public class GenerateAllRequest
    {
        public bool ForceAll { get; set; }
        public bool BypassRateLimit { get; set; }
        public bool BypassTokenCap { get; set; }
    }

    /// <summary>
    /// Spec 020 / US5 — resolve a citation source-ref into a signed URL.
    /// Citation IDs are `<applicationItemId>:<documentId>` (the orchestrator
    /// projects supplier blobs through the Document id; we resolve back here).
    /// </summary>
    [HttpGet]
    [Route("Review/Citations/{applicationItemId:int}/{sourceRefId}")]
    public async Task<IActionResult> Citation(
        int applicationItemId,
        string sourceRefId,
        CancellationToken ct)
    {
        var parentApplicationId = await _reviewService.GetApplicationIdForItemAsync(applicationItemId, ct);
        if (parentApplicationId is null) return NotFound();

        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(parentApplicationId.Value, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        // The citation marker is rendered as a relative link to the Document by
        // id; storage handle resolution is delegated to the existing document
        // download endpoint. We 302 to that path so the spec-014 SAS-TTL policy
        // is enforced centrally.
        if (!int.TryParse(sourceRefId, out var documentId))
            return NotFound();

        // Look up the blob key via the Document row and stream it through
        // IObjectStorage (spec 014 / FR-018). The orchestrator wires storage
        // by category; supplier-quotation files live under application-attachments.
        var storage = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Application.Abstractions.Storage.IObjectStorage>();
        var db = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Infrastructure.Persistence.AppDbContext>();
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null || string.IsNullOrEmpty(doc.BlobKey)) return NotFound();

        var key = FundingPlatform.Application.Abstractions.Storage.ObjectKey.Parse(doc.BlobKey);
        var handle = await storage.ResolveServingHandleAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory.ApplicationAttachment,
            key,
            FundingPlatform.Application.Abstractions.Storage.ServingMode.TimeLimitedUrl,
            ct);

        if (handle is FundingPlatform.Application.Abstractions.Storage.TimeLimitedUrlHandle url)
            return Redirect(url.Url.ToString());
        if (handle is FundingPlatform.Application.Abstractions.Storage.BackendStreamHandle stream)
            return File(stream.Content, stream.ContentType ?? "application/octet-stream", doc.OriginalFileName);
        return NotFound();
    }

    /// <summary>
    /// Spec 023 / FR-014 (evolution 2026-05-20) — reviewer (group-scoped) and
    /// Admin download the PDF attached to any quotation on an Application
    /// they're authorized to view. Mirrors the auth + storage rails of the
    /// spec-020 <see cref="Citation"/> endpoint but is keyed by
    /// <c>quotationId</c> directly so the reviewer Review screen can build
    /// the link without an extra DocumentId resolution step.
    /// </summary>
    [HttpGet]
    [Route("Review/Quotation/{quotationId:int}/Download")]
    public async Task<IActionResult> DownloadQuotation(
        int quotationId,
        CancellationToken ct)
    {
        var db = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Infrastructure.Persistence.AppDbContext>();
        var quotation = await db.Quotations
            .Include(q => q.Document)
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct);
        if (quotation is null
            || quotation.Document is null
            || string.IsNullOrEmpty(quotation.Document.BlobKey))
            return NotFound();

        var parentApplicationId = await db.Items
            .Where(i => i.Id == quotation.ItemId)
            .Select(i => (int?)i.ApplicationId)
            .FirstOrDefaultAsync(ct);
        if (parentApplicationId is null) return NotFound();
        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(
                parentApplicationId.Value, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        // Spec 023 / FR-014 (evolution) — same rationale as the applicant
        // download path: force BackendStream so `Content-Disposition: attachment`
        // is set on the response and the browser saves the file. Inline preview
        // is intentionally not exposed on this endpoint.
        var storage = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Application.Abstractions.Storage.IObjectStorage>();
        var key = FundingPlatform.Application.Abstractions.Storage.ObjectKey.Parse(
            quotation.Document.BlobKey);
        var handle = await storage.ResolveServingHandleAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory.ApplicationAttachment,
            key,
            FundingPlatform.Application.Abstractions.Storage.ServingMode.BackendStream,
            ct);

        if (handle is FundingPlatform.Application.Abstractions.Storage.BackendStreamHandle stream)
            return File(
                stream.Content,
                stream.ContentType ?? "application/octet-stream",
                quotation.Document.OriginalFileName);
        return NotFound();
    }

    /// <summary>Spec 020 / FR-A4 — enqueue per-item comparison jobs for the whole application.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/GenerateAll/{applicationId:int}")]
    public async Task<IActionResult> GenerateAll(
        int applicationId,
        [FromBody] GenerateAllRequest? body,
        [FromServices] FundingPlatform.Infrastructure.Persistence.AppDbContext db,
        [FromServices] IComparisonJobRepository jobs,
        CancellationToken ct)
    {
        body ??= new GenerateAllRequest();
        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(applicationId, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        var actorRole = User.IsInRole("Admin") ? "Admin" : "Reviewer";
        var forceAll = body.ForceAll && actorRole == "Admin";
        var bypassRateLimit = body.BypassRateLimit && actorRole == "Admin";
        var bypassTokenCap = body.BypassTokenCap && actorRole == "Admin";

        // Load eligible items (>= 2 quotations).
        var items = await db.Items
            .Where(i => i.ApplicationId == applicationId)
            .Select(i => new { i.Id, QuotationCount = i.Quotations.Count })
            .ToListAsync(ct);

        var eligible = items.Where(x => x.QuotationCount >= 2).ToList();
        if (eligible.Count == 0)
            return UnprocessableEntity(new { code = "no_eligible_items" });

        var enqueued = new List<object>();
        var skippedFresh = new List<int>();

        foreach (var item in eligible)
        {
            if (!forceAll)
            {
                var cached = await _comparisonOrchestrator.GetCachedComparisonAsync(item.Id, ct);
                if (cached is { Freshness: Freshness.Fresh })
                {
                    skippedFresh.Add(item.Id);
                    continue;
                }
            }

            var job = ComparisonJob.Enqueue(
                applicationItemId: item.Id,
                requestedByUserId: GetUserId(),
                actorRole: actorRole,
                bypassedRateLimit: bypassRateLimit,
                bypassedTokenCap: bypassTokenCap,
                now: DateTimeOffset.UtcNow);
            await jobs.EnqueueAsync(job, ct);
            enqueued.Add(new { applicationItemId = item.Id, jobId = job.Id });
        }

        return Accepted(new { enqueued, skippedFresh });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/{id:int}/ReviewItem")]
    public async Task<IActionResult> ReviewItem(
        int id, int ItemId, string Decision, string? Comment, int? SelectedSupplierId, string? LineCode)
    {
        // Spec 018 / FR-012..FR-014 — LineCode threads through the same POST that
        // captures the per-item decision. The service composes
        // `Application.AssignLineCodeToItem` (uniqueness + length) with the decision
        // call inside one transaction; either failure rolls back the other.
        var error = await _reviewService.ReviewItemAsync(
            id, ItemId, Decision, Comment, SelectedSupplierId, LineCode, GetUserId());
        if (error is not null)
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
        else
            TempData["SuccessMessage"] = "Decisión del ítem registrada.";

        return RedirectToAction(nameof(Review), new { id });
    }

    /// <summary>
    /// Spec 027 / US5 — reviewer/admin sets the application owner's
    /// <c>CodigoPersonal</c> from the first review screen. Group-overlap
    /// authorization mirrors the <see cref="Review"/> GET (spec 016). The column
    /// is NVARCHAR(40); input is length-bounded. Last-write-wins (no concurrency
    /// token — a single low-contention scalar, per the spec edge-case note).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/{id:int}/ApplicantCode")]
    public async Task<IActionResult> ApplicantCode(int id, string? CodigoPersonal, CancellationToken ct = default)
    {
        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(id, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        var application = await _applicationRepository.GetByIdWithDetailsAsync(id);
        var applicantUserId = application?.Applicant?.UserId;
        if (string.IsNullOrEmpty(applicantUserId)) return NotFound();

        var code = CodigoPersonal?.Trim();
        if (code is { Length: > 40 })
        {
            TempData["ErrorMessage"] = "El código del solicitante no puede exceder los 40 caracteres.";
            return RedirectToAction(nameof(Review), new { id });
        }

        var user = await _userManager.FindByIdAsync(applicantUserId);
        if (user is null) return NotFound();

        user.CodigoPersonal = string.IsNullOrWhiteSpace(code) ? null : code;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = "No se pudo actualizar el código del solicitante.";
            return RedirectToAction(nameof(Review), new { id });
        }

        TempData["SuccessMessage"] = "Código del solicitante actualizado.";
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/{id:int}/FlagEquivalence")]
    public async Task<IActionResult> FlagEquivalence(int id, int ItemId, bool IsNotEquivalent)
    {
        var error = await _reviewService.FlagTechnicalEquivalenceAsync(id, ItemId, IsNotEquivalent, GetUserId());
        if (error is not null)
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
        else
            TempData["SuccessMessage"] = IsNotEquivalent
                ? "Ítem marcado como no técnicamente equivalente."
                : "Marca de equivalencia técnica eliminada.";

        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/{id:int}/SendBack")]
    public async Task<IActionResult> SendBack(int id)
    {
        var error = await _reviewService.SendBackAsync(id, GetUserId());
        if (error is not null)
        {
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
            return RedirectToAction(nameof(Review), new { id });
        }

        TempData["SuccessMessage"] = "Solicitud devuelta al solicitante.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/{id:int}/Finalize")]
    public async Task<IActionResult> Finalize(int id, bool force = false)
    {
        var (error, unresolvedItems) = await _reviewService.FinalizeReviewAsync(id, force, GetUserId());

        if (error is not null)
        {
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
            return RedirectToAction(nameof(Review), new { id });
        }

        if (unresolvedItems is not null)
        {
            // Show warning with unresolved items — re-render the review page
            var dto = await _reviewService.GetApplicationForReviewAsync(id);
            if (dto is null)
                return NotFound();

            var viewModel = MapToViewModel(dto);
            viewModel.UnresolvedItemWarnings = unresolvedItems;
            return View(nameof(Review), viewModel);
        }

        TempData["SuccessMessage"] = "Revisión finalizada. Solicitud resuelta.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static ReviewApplicationViewModel MapToViewModel(Application.DTOs.ReviewApplicationDto dto)
    {
        var hasUnresolved = dto.Items.Any(i =>
            i.ReviewStatus == Domain.Enums.ItemReviewStatus.Pending ||
            i.ReviewStatus == Domain.Enums.ItemReviewStatus.NeedsInfo);

        return new ReviewApplicationViewModel
        {
            ApplicationId = dto.ApplicationId,
            ApplicantName = dto.ApplicantName,
            ApplicantPerformanceScore = dto.ApplicantPerformanceScore,
            State = dto.State.ToString(),
            SubmittedAt = dto.SubmittedAt,
            HasUnresolvedItems = hasUnresolved,
            RejectedSupplierCount = dto.RejectedSupplierCount,
            Items = dto.Items.Select(item => new ReviewItemViewModel
            {
                ItemId = item.ItemId,
                ProductName = item.ProductName,
                CategoryName = item.CategoryName,
                ReviewStatus = item.ReviewStatus.ToString(),
                ReviewComment = item.ReviewComment,
                SelectedSupplierId = item.SelectedSupplierId,
                IsNotTechnicallyEquivalent = item.IsNotTechnicallyEquivalent,
                LineCode = item.LineCode,
                AttributedImpactNames = item.AttributedImpactNames,
                ImpactJustification = item.ImpactJustification,
                Quotations = item.Quotations.Select(q => new ReviewQuotationViewModel
                {
                    QuotationId = q.QuotationId,
                    SupplierId = q.SupplierId,
                    SupplierName = q.SupplierName,
                    SupplierLegalId = q.SupplierLegalId,
                    Price = q.Price,
                    ValidUntil = q.ValidUntil,
                    DocumentFileName = q.DocumentFileName,
                    IsRecommended = q.IsRecommended,
                    Score = q.Score,
                    ScoreCCSS = q.ScoreCCSS,
                    ScoreHacienda = q.ScoreHacienda,
                    ScoreSICOP = q.ScoreSICOP,
                    ScoreLowestPrice = q.ScoreLowestPrice,
                    IsPreSelected = q.IsPreSelected,
                    IsSupplierVerified = q.IsSupplierVerified,
                    IsSupplierRejected = q.IsSupplierRejected,
                    Currency = q.Currency,
                    ConvertedCrcAmount = q.ConvertedCrcAmount,
                    SnapshotRateValue = q.SnapshotRateValue,
                    SnapshotRateType = q.SnapshotRateType,
                    SnapshotEffectiveAtUtc = q.SnapshotEffectiveAtUtc,
                    Compliance = q.Compliance,
                    LegacyNeedsReview = q.LegacyNeedsReview,
                }).ToList(),
                // Spec 035 / D1 — per-item category field values.
                CategoryFields = item.CategoryFields.Select(cf => new CategoryFieldDisplayViewModel
                {
                    Label = cf.Label,
                    Value = cf.Value,
                }).ToList()
            }).ToList(),
            // Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts.
            Impacts = dto.Impacts.Select(ai => new ApplicationImpactDisplayViewModel
            {
                TemplateName = ai.TemplateName,
                Parameters = ai.Parameters.Select(p => new ImpactParameterDisplayViewModel
                {
                    Name = p.Name,
                    DisplayLabel = p.DisplayLabel,
                    Value = p.Value,
                }).ToList(),
            }).ToList(),
        };
    }
}
