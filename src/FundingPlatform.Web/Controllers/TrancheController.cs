// Spec 046 — see specs/046-tranches-budget-lines/contracts/interfaces.md §4 (Web routes).

using System.Security.Claims;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Routing;
using FundingPlatform.Application.Tranches;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 046 / US1 / FR-021 — reviewer tranche (funding-phase) setup, mounted on the review surface.
/// <b>Reviewer-only</b>: FR-021 requires that only reviewers define tranches and assign lines, with
/// Auditors AND Admins read-only for these actions (the sibling <c>DisbursementController</c> applies
/// the same segregation to money movement — write = Financial Operator only). Gated by group overlap
/// with the applicant. The execution freeze (edits refused once the agreement executes) is enforced
/// by <see cref="ITrancheService"/> and surfaced as a flash; the editor renders only pre-audit and
/// only for reviewers (<c>ReviewController</c>). All POSTs are antiforgery-guarded and redirect back
/// to the review page.
/// </summary>
[Authorize(Roles = "Reviewer")]
[Route("Review/{applicationId:int}/Tranches")]
public sealed class TrancheController : Controller
{
    private readonly ITrancheService _service;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _appRepo;

    public TrancheController(
        ITrancheService service,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository appRepo)
    {
        _service = service;
        _scopeProvider = scopeProvider;
        _appRepo = appRepo;
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int applicationId, string? name, CancellationToken ct)
    {
        var guard = await GuardAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _service.CreateAsync(applicationId, name ?? string.Empty, GetUserId(), ct);
        return Flash(result, TrancheResources.Flash_TrancheCreated, applicationId);
    }

    [HttpPost("{trancheId:int}/Rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int applicationId, int trancheId, string? name, CancellationToken ct)
    {
        var guard = await GuardAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _service.RenameAsync(applicationId, trancheId, name ?? string.Empty, GetUserId(), ct);
        return Flash(result, TrancheResources.Flash_TrancheRenamed, applicationId);
    }

    [HttpPost("{trancheId:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int applicationId, int trancheId, CancellationToken ct)
    {
        var guard = await GuardAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _service.DeleteAsync(applicationId, trancheId, GetUserId(), ct);
        return Flash(result, TrancheResources.Flash_TrancheDeleted, applicationId);
    }

    [HttpPost("Assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int applicationId, int itemId, int? trancheId, CancellationToken ct)
    {
        var guard = await GuardAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _service.AssignItemAsync(applicationId, itemId, trancheId, GetUserId(), ct);
        return Flash(result, TrancheResources.Flash_LineAssigned, applicationId);
    }

    // ---------------------------------------------------------------------

    /// <summary>Group-overlap gate: admin short-circuit, else the applicant must share a group with
    /// the caller (mirrors <c>ReviewController</c>). Returns a <see cref="ForbidResult"/> when not
    /// in scope, else null.</summary>
    private async Task<IActionResult?> GuardAsync(int applicationId, CancellationToken ct)
    {
        var scope = await _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);
        if (!scope.IsAdmin && !await _appRepo.ApplicantSharesAnyGroupAsync(applicationId, scope.GroupIds, ct))
        {
            return Forbid();
        }
        return null;
    }

    private IActionResult Flash(Result result, string successMessage, int applicationId)
    {
        if (result.Succeeded)
        {
            TempData["SuccessMessage"] = successMessage;
        }
        else
        {
            TempData["ErrorMessage"] = result.Errors.Count > 0
                ? result.Errors[0].Message
                : DisbursementResources.Error_InvalidInput;
        }
        return Redirect(ReviewRoutes.PathFor(applicationId) + "#tranche-editor");
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
