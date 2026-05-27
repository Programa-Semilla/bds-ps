using System.Security.Claims;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Applications.Queries;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

[Authorize]
public class ApplicantResponseController : Controller
{
    private readonly ApplicantResponseService _service;
    private readonly AppDbContext _dbContext;
    private readonly IUserFacingErrorTranslator _errorTranslator;

    public ApplicantResponseController(
        ApplicantResponseService service,
        AppDbContext dbContext,
        IUserFacingErrorTranslator errorTranslator)
    {
        _service = service;
        _dbContext = dbContext;
        _errorTranslator = errorTranslator;
    }

    [HttpGet]
    [Authorize(Roles = "Applicant")]
    public async Task<IActionResult> Index(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var userId = GetUserId();

        var dto = await _service.GetResponseAsync(new GetApplicantResponseQuery(id, userId), applicantId);
        if (dto is null) return NotFound();

        return View(BuildViewModel(dto));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Applicant")]
    [Route("ApplicantResponse/Submit/{id:int}")]
    public async Task<IActionResult> Submit(int id, Dictionary<int, ItemResponseDecision>? decisions)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var userId = GetUserId();

        decisions ??= [];
        var command = new SubmitApplicantResponseCommand(id, userId, decisions);
        var (result, error) = await _service.SubmitResponseAsync(command, applicantId);

        if (error is not null)
        {
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
        }
        else
        {
            TempData["SuccessMessage"] = "Respuesta enviada.";
        }

        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Applicant")]
    [Route("ApplicantResponse/OpenAppeal/{id:int}")]
    public async Task<IActionResult> OpenAppeal(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var userId = GetUserId();

        var (result, error) = await _service.OpenAppealAsync(new OpenAppealCommand(id, userId), applicantId);
        if (error is not null)
        {
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
            return RedirectToAction(nameof(Index), new { id });
        }

        TempData["SuccessMessage"] = "Apelación abierta.";
        return RedirectToAction(nameof(Appeal), new { id });
    }

    [HttpGet]
    [Authorize(Roles = "Applicant,Reviewer")]
    [Route("ApplicantResponse/Appeal/{id:int}")]
    public async Task<IActionResult> Appeal(int id)
    {
        var userId = GetUserId();
        var isReviewer = User.IsInRole("Reviewer");
        int? applicantId = isReviewer ? null : await GetCurrentApplicantIdAsync();

        var dto = await _service.GetAppealAsync(new GetAppealQuery(id, userId, isReviewer), applicantId);
        if (dto is null) return NotFound();

        return View(BuildAppealViewModel(dto, id, isReviewer));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Applicant,Reviewer")]
    [Route("ApplicantResponse/PostMessage/{id:int}")]
    public async Task<IActionResult> PostMessage(int id, AppealThreadViewModel model)
    {
        var userId = GetUserId();
        var isReviewer = User.IsInRole("Reviewer");
        int? applicantId = isReviewer ? null : await GetCurrentApplicantIdAsync();

        var text = model?.NewMessageText ?? string.Empty;
        var command = new PostAppealMessageCommand(id, userId, text);
        var (result, error) = await _service.PostMessageAsync(command, applicantId, isReviewer);

        if (error is not null)
        {
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
        }
        else
        {
            TempData["SuccessMessage"] = "Mensaje publicado.";
        }

        return RedirectToAction(nameof(Appeal), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Reviewer")]
    [Route("ApplicantResponse/ResolveAppeal/{id:int}")]
    public async Task<IActionResult> ResolveAppeal(int id, AppealResolution resolution)
    {
        var userId = GetUserId();
        var (result, error) = await _service.ResolveAppealAsync(new ResolveAppealCommand(id, userId, resolution));

        if (error is not null)
        {
            TempData["ErrorMessage"] = _errorTranslator.Translate(error);
            return RedirectToAction(nameof(Appeal), new { id });
        }

        // Spec 012 / FR-010 / FR-014: route the enum through a Spanish display
        // string instead of letting AppealResolution.ToString() reach the UI.
        var resolutionLabel = resolution switch
        {
            AppealResolution.Uphold => "Confirmada",
            AppealResolution.GrantReopenToDraft => "Concedida (reabrir como borrador)",
            AppealResolution.GrantReopenToReview => "Concedida (reabrir en revisión)",
            _ => resolution.ToString(),
        };
        TempData["SuccessMessage"] = $"Apelación resuelta como {resolutionLabel}.";

        return resolution switch
        {
            AppealResolution.GrantReopenToDraft => Redirect($"/Application/Details/{id}"),
            AppealResolution.GrantReopenToReview => Redirect($"/Review/{id}"),
            _ => RedirectToAction(nameof(Appeal), new { id })
        };
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<int> GetCurrentApplicantIdAsync()
    {
        var userId = GetUserId();
        var applicant = await _dbContext.Applicants
            .FirstOrDefaultAsync(a => a.UserId == userId);

        return applicant?.Id ?? throw new InvalidOperationException("Applicant not found for current user.");
    }

    private static ApplicantResponseViewModel BuildViewModel(ApplicantResponseDto dto)
    {
        var hasRejectedItem = dto.Items.Any(i => i.Decision == ItemResponseDecision.Reject);

        return new ApplicantResponseViewModel
        {
            ApplicationId = dto.ApplicationId,
            IsSubmitted = dto.IsSubmitted,
            State = dto.State,
            SubmittedAt = dto.SubmittedAt,
            Items = dto.Items.Select(i => new ItemResponseViewModel
            {
                ItemId = i.ItemId,
                ProductName = i.ProductName,
                ReviewStatus = i.ReviewStatus,
                SelectedSupplierName = i.SelectedSupplierName,
                Amount = i.Amount,
                ReviewComment = i.ReviewComment,
                Decision = i.Decision,
                DecisionLabel = i.Decision.HasValue
                    ? ItemResponseDecisionCopy.Label(i.Decision.Value)
                    : null
            }).ToList(),
            CanOpenAppeal = dto.State == ApplicationState.ResponseFinalized && hasRejectedItem,
            HasOpenAppeal = dto.State == ApplicationState.AppealOpen,
            HasFundingAgreement = dto.HasFundingAgreement
        };
    }

    private static AppealThreadViewModel BuildAppealViewModel(AppealDto dto, int applicationId, bool isReviewer)
    {
        var isOpen = dto.Status == AppealStatus.Open;
        return new AppealThreadViewModel
        {
            ApplicationId = applicationId,
            AppealId = dto.Id,
            Status = dto.Status,
            Resolution = dto.Resolution,
            ResolvedAt = dto.ResolvedAt,
            Messages = dto.Messages.Select(m => new AppealMessageViewModel
            {
                AuthorDisplayName = m.AuthorDisplayName,
                AuthorUserId = m.AuthorUserId,
                Text = m.Text,
                CreatedAt = m.CreatedAt,
                IsByApplicant = m.IsByApplicant
            }).ToList(),
            CanPostMessage = isOpen,
            CanResolve = isOpen && isReviewer
        };
    }
}
