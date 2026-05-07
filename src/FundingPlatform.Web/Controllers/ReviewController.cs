using System.Security.Claims;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Routing;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Queries;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Reviewer,Admin")]
public class ReviewController : Controller
{
    private readonly ReviewService _reviewService;
    private readonly SignedUploadService _signedUploadService;
    private readonly IReviewerQueueProjection _queueProjection;
    private readonly IUserFacingErrorTranslator _errorTranslator;

    public ReviewController(
        ReviewService reviewService,
        SignedUploadService signedUploadService,
        IReviewerQueueProjection queueProjection,
        IUserFacingErrorTranslator errorTranslator)
    {
        _reviewService = reviewService;
        _signedUploadService = signedUploadService;
        _queueProjection = queueProjection;
        _errorTranslator = errorTranslator;
    }

    [HttpGet]
    [Route("Review/SigningInbox")]
    public async Task<IActionResult> SigningInbox(int page = 1, int pageSize = 25)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 25;

        var query = new GetSigningInboxQuery(
            CurrentUserId: GetUserId(),
            IsAdministrator: User.IsInRole("Admin"),
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
    public async Task<IActionResult> Index(ReviewerFilter filter = ReviewerFilter.All, CancellationToken ct = default)
    {
        // Spec 011 US4 (FR-052) — Index renders the new QueueDashboard via the projection.
        var firstName = User.Identity?.Name ?? "Reviewer";
        var dto = await _queueProjection.GetForReviewerAsync(GetUserId(), firstName, filter, ct);
        return View("QueueDashboard", dto);
    }

    [HttpGet]
    [Route("Review/QueueRows")]
    public async Task<IActionResult> QueueRows(ReviewerFilter filter = ReviewerFilter.All, CancellationToken ct = default)
    {
        // Spec 011 US4 (FR-054) — chip-reflow contract.
        var rows = await _queueProjection.GetRowsAsync(GetUserId(), filter, ct);
        return PartialView("_ReviewerQueueRows", rows);
    }

    [HttpGet]
    [Route(ReviewRoutes.ReviewTemplate)]
    public async Task<IActionResult> Review(int id)
    {
        var dto = await _reviewService.GetApplicationForReviewAsync(id);
        if (dto is null)
            return NotFound();

        var viewModel = MapToViewModel(dto);
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Review/{id:int}/ReviewItem")]
    public async Task<IActionResult> ReviewItem(int id, int ItemId, string Decision, string? Comment, int? SelectedSupplierId)
    {
        var error = await _reviewService.ReviewItemAsync(id, ItemId, Decision, Comment, SelectedSupplierId, GetUserId());
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
