// Spec 021 — see specs/021-feedback-session-may13/tasks.md T138 and research.md R-12.

using FundingPlatform.Application.ReviewerDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 021 / US6 / T138 / FR-033 / SC-010 — minimal reviewer-dashboard
/// surface that hosts the *Cotizaciones pendientes* KPI tile that moved off
/// the admin dashboard per FR-033 (R-12). Reachable at
/// <c>/Reviewer/Dashboard</c>; the existing <c>/Review</c> queue surface is
/// unchanged. Authorized for the same roles as the queue (Reviewer + Admin)
/// since Admin always inherits Reviewer scope (spec 016).
/// </summary>
[Authorize(Roles = "Reviewer,Admin")]
[Route("Reviewer")]
public class ReviewerDashboardController : Controller
{
    private readonly IReviewerDashboardProjection _projection;

    public ReviewerDashboardController(IReviewerDashboardProjection projection)
    {
        _projection = projection;
    }

    [HttpGet("Dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        // Spec 021 / FR-033 — the only KPI on this surface today. Future
        // additions (reviewer-aging count, signing-inbox backlog, etc.) are
        // welcome but out of scope for US6.
        var pending = await _projection.CountPendingQuotationsAsync(ct);
        ViewData["Title"] = "Panel del revisor";
        return View(pending);
    }
}
