using System.Security.Claims;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using FundingPlatform.Web.ViewModels.Audit;
using FundingPlatform.Web.ViewModels.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 040 / US1 — the auditor workflow surface. Group-scoped exactly like the reviewer
/// surfaces (role gate + group-overlap <see cref="ControllerBase.Forbid()"/>). PDF
/// generation + download reuse the re-gated <c>FundingAgreementController</c> endpoints.
/// </summary>
[Authorize(Roles = "Auditor,Admin")]
[Route("Audit")]
public sealed class AuditController : Controller
{
    private readonly IAuditorQueueProjection _inbox;
    private readonly IAuditWorkflowService _workflow;
    private readonly ReviewService _reviewService;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _applications;
    private readonly IUserFacingErrorTranslator _errorTranslator;
    // Spec 040 / FR-007 — read the application's review history for the audit detail.
    private readonly AppDbContext _dbContext;
    // Spec 040 / FR-007 — reviewer-equivalent read: decision summary + cached AI comparison.
    private readonly IDecisionSummaryProjection _decisionSummary;
    private readonly IComparisonOrchestrator _comparisonOrchestrator;

    public AuditController(
        IAuditorQueueProjection inbox,
        IAuditWorkflowService workflow,
        ReviewService reviewService,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository applications,
        IUserFacingErrorTranslator errorTranslator,
        AppDbContext dbContext,
        IDecisionSummaryProjection decisionSummary,
        IComparisonOrchestrator comparisonOrchestrator)
    {
        _inbox = inbox;
        _workflow = workflow;
        _reviewService = reviewService;
        _scopeProvider = scopeProvider;
        _applications = applications;
        _errorTranslator = errorTranslator;
        _dbContext = dbContext;
        _decisionSummary = decisionSummary;
        _comparisonOrchestrator = comparisonOrchestrator;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private Task<IReviewerScope> GetScopeAsync(CancellationToken ct) =>
        _scopeProvider.GetForUserAsync(UserId, User.IsInRole("Admin"), ct);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var scope = await GetScopeAsync(ct);
        var rows = await _inbox.GetInboxAsync(scope, q, page: 1, pageSize: 200, ct);
        return View(new AuditInboxViewModel { Rows = rows, SearchTerm = q });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var dto = await _reviewService.GetApplicationForReviewAsync(id, UserId);
        if (dto is null) return NotFound();

        // Spec 040 / D8 / D12 — same group-overlap guard as the reviewer detail page.
        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applications.ApplicantSharesAnyGroupAsync(id, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        var checklist = await _workflow.GetAuditChecklistAsync(id, ct);
        if (checklist is null) return NotFound();

        // Spec 040 / FR-007 — the application's review history (reviewer-equivalent read).
        var history = await _dbContext.VersionHistories.AsNoTracking()
            .Where(v => v.ApplicationId == id)
            .OrderByDescending(v => v.Timestamp)
            .Select(v => new AuditHistoryEntryViewModel { Action = v.Action, Details = v.Details, Timestamp = v.Timestamp })
            .ToListAsync(ct);

        // Spec 040 / FR-007 — project the reviewer-equivalent read surface: the same
        // DTO→VM mapping the reviewer uses, plus the shared decision summary and the
        // cached AI comparison (read-only — no generate controls on the audit page).
        var review = ReviewApplicationViewModelMapper.Map(dto);
        review.IsAdmin = User.IsInRole("Admin");

        var aggregate = await _applications.GetByIdWithDetailsAsync(id);
        if (aggregate is not null)
        {
            review.DecisionSummary = _decisionSummary.Project(aggregate);
        }

        foreach (var item in review.Items)
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
                IsAdmin = review.IsAdmin,
                ReadOnly = true,
            };
        }

        return View(new AuditDetailViewModel
        {
            Application = dto,
            Review = review,
            Checklist = checklist,
            IsAdmin = User.IsInRole("Admin"),
            History = history,
        });
    }

    [HttpPost("{id:int}/Checklist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveChecklist(int id, List<AuditMarkInput> marks, CancellationToken ct)
    {
        if (!await EnsureInScopeAsync(id, ct)) return Forbid();

        var result = await PersistMarksAsync(id, marks, ct);
        return RedirectWithResult(id, result, "Lista de verificación de auditoría guardada.");
    }

    // Spec 040 — Approve/Return submit the live checklist marks from the same form so the
    // auditor's current selection is persisted before the action runs (one click instead of
    // "save, then act"). Mirrors SaveChecklist's mapping.
    private Task<AuditActionResult> PersistMarksAsync(int id, List<AuditMarkInput>? marks, CancellationToken ct)
    {
        var domainMarks = (marks ?? new List<AuditMarkInput>())
            .Select(m => new AuditMark(m.TemplateItemId, m.Compliant, m.Reason))
            .ToList();
        return _workflow.SaveAuditChecklistAsync(id, domainMarks, UserId, ct);
    }

    [HttpPost("{id:int}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, List<AuditMarkInput> marks, CancellationToken ct)
    {
        if (!await EnsureInScopeAsync(id, ct)) return Forbid();
        var saved = await PersistMarksAsync(id, marks, ct);
        if (!saved.Success) return RedirectWithResult(id, saved, string.Empty);
        var result = await _workflow.ApproveForAgreementAsync(id, UserId, ct);
        return RedirectWithResult(id, result, "Auditoría aprobada. Ya puede generar el convenio.");
    }

    [HttpPost("{id:int}/Confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        if (!await EnsureInScopeAsync(id, ct)) return Forbid();
        var result = await _workflow.ConfirmPdfAsync(id, UserId, ct);
        return RedirectWithResult(id, result, "PDF confirmado como correcto.");
    }

    [HttpPost("{id:int}/Release")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(int id, CancellationToken ct)
    {
        if (!await EnsureInScopeAsync(id, ct)) return Forbid();
        var result = await _workflow.ReleaseForSignatureAsync(id, UserId, ct);
        if (result.Success)
        {
            TempData["AuditSuccess"] = "Convenio liberado para firma. El solicitante fue notificado.";
            return RedirectToAction(nameof(Index));
        }
        return RedirectWithResult(id, result, string.Empty);
    }

    [HttpPost("{id:int}/Return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, List<AuditMarkInput> marks, CancellationToken ct)
    {
        if (!await EnsureInScopeAsync(id, ct)) return Forbid();
        var saved = await PersistMarksAsync(id, marks, ct);
        if (!saved.Success) return RedirectWithResult(id, saved, string.Empty);
        var result = await _workflow.ReturnToReviewerAsync(id, UserId, ct);
        if (result.Success)
        {
            TempData["AuditSuccess"] = "Solicitud devuelta al revisor con los motivos indicados.";
            return RedirectToAction(nameof(Index));
        }
        return RedirectWithResult(id, result, string.Empty);
    }

    private async Task<bool> EnsureInScopeAsync(int id, CancellationToken ct)
    {
        var scope = await GetScopeAsync(ct);
        if (scope.IsAdmin) return true;
        return await _applications.ApplicantSharesAnyGroupAsync(id, scope.GroupIds, ct);
    }

    private IActionResult RedirectWithResult(int id, AuditActionResult result, string successMessage)
    {
        if (result.Success)
        {
            if (!string.IsNullOrEmpty(successMessage)) TempData["AuditSuccess"] = successMessage;
        }
        else if (result.Error is not null)
        {
            TempData["AuditError"] = _errorTranslator.Translate(result.Error);
        }
        return RedirectToAction(nameof(Detail), new { id });
    }
}
