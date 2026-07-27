// Spec 048 — see specs/048-full-reconciliation-engine/contracts/interfaces.md (HTTP surface).

using System.Globalization;
using System.Security.Claims;
using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Reconciliation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 048 / US3 — the group→agency reconciliation dashboard. Financial Operator + Auditor are
/// group-scoped; Admin is agency-wide. Only the Financial Operator may act on a discrepancy
/// (assign/under-correction/waive); Auditor + Admin are read-only (mirrors
/// <c>DisbursementController</c>: per-discrepancy <see cref="GuardWriteAsync"/> → flat-404 out-of-scope,
/// then 403 read-only). Discrepancy status/severity are never conveyed by colour alone (FR-025).
/// </summary>
[Authorize(Roles = "Financial Operator,Admin,Auditor")]
[Route("Reconciliation")]
public sealed class ReconciliationDashboardController : Controller
{
    private const string OperatorRole = "Financial Operator";

    private readonly IReconciliationDashboardProjection _dashboard;
    private readonly IDiscrepancyLifecycleService _lifecycle;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public ReconciliationDashboardController(
        IReconciliationDashboardProjection dashboard,
        IDiscrepancyLifecycleService lifecycle,
        IReviewerScopeProvider scopeProvider,
        UserManager<ApplicationUser> userManager,
        AppDbContext db)
    {
        _dashboard = dashboard;
        _lifecycle = lifecycle;
        _scopeProvider = scopeProvider;
        _userManager = userManager;
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ReconciliationFilterForm filter, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(ct);
        var appFilter = filter.ToFilter();

        var summary = await _dashboard.GetSummaryAsync(scope, appFilter, ct);
        var rows = await _dashboard.GetDiscrepanciesAsync(scope, appFilter, ct);

        return View(new ReconciliationIndexViewModel
        {
            Summary = summary,
            Rows = rows,
            Filter = filter,
            SupplierOptions = await SupplierOptionsAsync(scope, ct),
            TrancheOptions = await TrancheOptionsAsync(scope, ct),
            ResponsibleOptions = await OperatorOptionsAsync(),
            CanWrite = CanWrite(),
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(ct);
        var detail = await _dashboard.GetDetailAsync(scope, id, ct);
        if (detail is null)
        {
            return NotFound(); // out of scope / missing — no disclosure
        }

        return View(new ReconciliationDetailViewModel
        {
            Detail = detail with { CanWrite = CanWrite() },
            AssigneeOptions = CanWrite() ? await OperatorOptionsAsync() : [],
            CanWrite = CanWrite(),
        });
    }

    [HttpPost("{id:int}/Assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, string assigneeUserId, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(id, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _lifecycle.AssignAsync(id, assigneeUserId, GetUserId(), ct);
        return LifecycleRedirect(id, result, ReconciliationResources.Flash_Assigned);
    }

    [HttpPost("{id:int}/UnderCorrection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnderCorrection(int id, string? note, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(id, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _lifecycle.MarkUnderCorrectionAsync(id, note, GetUserId(), ct);
        return LifecycleRedirect(id, result, ReconciliationResources.Flash_UnderCorrection);
    }

    [HttpPost("{id:int}/Waive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Waive(int id, string reason, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(id, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _lifecycle.WaiveAsync(id, reason, GetUserId(), ct);
        return LifecycleRedirect(id, result, ReconciliationResources.Flash_Waived);
    }

    // ---------------------------------------------------------------- helpers

    private IActionResult LifecycleRedirect(int id, DiscrepancyActionResult result, string successMessage)
    {
        switch (result.Outcome)
        {
            case DiscrepancyActionOutcome.NotFound:
                return NotFound();
            case DiscrepancyActionOutcome.Refused:
                TempData["ErrorMessage"] = result.Error?.Message;
                break;
            default:
                TempData["SuccessMessage"] = successMessage;
                break;
        }
        return RedirectToAction(nameof(Detail), new { id });
    }

    private async Task<IReviewerScope> ResolveScopeAsync(CancellationToken ct)
        => await _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);

    /// <summary>Per-discrepancy write authorization: out-of-scope → flat 404 (no disclosure), then an
    /// in-scope Auditor/Admin → 403 read-only. Returns null when the caller may write.</summary>
    private async Task<IActionResult?> GuardWriteAsync(int discrepancyId, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(ct);
        var detail = await _dashboard.GetDetailAsync(scope, discrepancyId, ct);
        if (detail is null)
        {
            return NotFound();
        }
        if (!CanWrite())
        {
            return Forbid();
        }
        return null;
    }

    private async Task<IReadOnlyList<(int Id, string Name)>> SupplierOptionsAsync(IReviewerScope scope, CancellationToken ct)
    {
        var groupIds = scope.GroupIds.ToList();
        var q = _db.Items.AsNoTracking()
            .Where(i => i.SelectedSupplierId != null && i.SelectedSupplier != null);
        if (!scope.IsAdmin)
        {
            q = q.Where(i => _db.Applications.Any(a => a.Id == i.ApplicationId
                && _db.UserGroupMemberships.Any(m => m.UserId == a.Applicant.UserId && groupIds.Contains(m.GroupId))));
        }
        var rows = await q
            .Select(i => new { Id = i.SelectedSupplierId!.Value, i.SelectedSupplier!.Name })
            .Distinct().OrderBy(x => x.Name).Take(500).ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    private async Task<IReadOnlyList<(int Id, string Name)>> TrancheOptionsAsync(IReviewerScope scope, CancellationToken ct)
    {
        var groupIds = scope.GroupIds.ToList();
        var q = _db.Tranches.AsNoTracking().AsQueryable();
        if (!scope.IsAdmin)
        {
            q = q.Where(t => _db.Applications.Any(a => a.Id == t.ApplicationId
                && _db.UserGroupMemberships.Any(m => m.UserId == a.Applicant.UserId && groupIds.Contains(m.GroupId))));
        }
        var rows = await q.Select(t => new { t.Id, t.Name }).Distinct().OrderBy(x => x.Name).Take(500).ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    private async Task<IReadOnlyList<(string UserId, string Name)>> OperatorOptionsAsync()
    {
        var operators = await _userManager.GetUsersInRoleAsync(OperatorRole);
        return operators
            .Select(u => (u.Id, ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : (u.Email ?? u.Id))))
            .OrderBy(x => x.Item2, StringComparer.Create(new CultureInfo("es-CR"), ignoreCase: true))
            .ToList();
    }

    private bool CanWrite() => User.IsInRole(OperatorRole);

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
