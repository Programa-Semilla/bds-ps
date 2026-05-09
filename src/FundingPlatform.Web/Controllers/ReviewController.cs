using System.Security.Claims;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Routing;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Queries;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
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

    public ReviewController(
        ReviewService reviewService,
        SignedUploadService signedUploadService,
        IReviewerQueueProjection queueProjection,
        IUserFacingErrorTranslator errorTranslator,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository applicationRepository)
    {
        _reviewService = reviewService;
        _signedUploadService = signedUploadService;
        _queueProjection = queueProjection;
        _errorTranslator = errorTranslator;
        _scopeProvider = scopeProvider;
        _applicationRepository = applicationRepository;
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
        return View(viewModel);
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
                TechnicalSpecifications = item.TechnicalSpecifications,
                ReviewStatus = item.ReviewStatus.ToString(),
                ReviewComment = item.ReviewComment,
                SelectedSupplierId = item.SelectedSupplierId,
                IsNotTechnicallyEquivalent = item.IsNotTechnicallyEquivalent,
                LineCode = item.LineCode,
                ImpactTemplateName = item.ImpactTemplateName,
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
                    ScoreElectronicInvoice = q.ScoreElectronicInvoice,
                    ScoreLowestPrice = q.ScoreLowestPrice,
                    IsPreSelected = q.IsPreSelected,
                    IsSupplierVerified = q.IsSupplierVerified,
                    IsSupplierRejected = q.IsSupplierRejected,
                    Currency = q.Currency,
                    ConvertedCrcAmount = q.ConvertedCrcAmount,
                    SnapshotRateValue = q.SnapshotRateValue,
                    SnapshotRateType = q.SnapshotRateType,
                    SnapshotEffectiveAtUtc = q.SnapshotEffectiveAtUtc,
                    LegacyNeedsReview = q.LegacyNeedsReview,
                }).ToList(),
                ImpactParameters = item.ImpactParameters.Select(p => new ImpactParameterDisplayViewModel
                {
                    Name = p.Name,
                    DisplayLabel = p.DisplayLabel,
                    Value = p.Value
                }).ToList()
            }).ToList()
        };
    }
}
