// Spec 041 — see specs/041-evidence-inbox/contracts/interfaces.md.

using System.Security.Claims;
using FundingPlatform.Application.EvidenceInbox;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 041 — the persistent funds-usage evidence inbox. A reviewer/admin reaches
/// executed applications (whose governing Process is still <c>Active</c>) to add
/// evidence over time, long after the per-application link on the agreement panel
/// has scrolled out of reach. Group-scoped exactly like the reviewer queue via
/// <see cref="IReviewerScopeProvider"/>; rows are pre-scoped by the projection
/// (NFR-001), so the view does no filtering. Applicants never receive the sidebar
/// entry and are refused here by the role attribute (FR-001/FR-008).
/// </summary>
[Authorize(Roles = "Reviewer,Admin")]
[Route("Evidence")]
public sealed class EvidenceInboxController : Controller
{
    private readonly IEvidenceInboxProjection _projection;
    private readonly IReviewerScopeProvider _scopeProvider;

    public EvidenceInboxController(
        IEvidenceInboxProjection projection,
        IReviewerScopeProvider scopeProvider)
    {
        _projection = projection;
        _scopeProvider = scopeProvider;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var scope = await _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);
        var rows = await _projection.GetForUserAsync(scope, ct);

        return View(new EvidenceInboxViewModel
        {
            Rows = rows.Select(r => new EvidenceInboxRowViewModel
            {
                ApplicationId = r.ApplicationId,
                ApplicationNumber = r.ApplicationNumber,
                ApplicantName = r.ApplicantName,
                FundName = r.FundName,
                ProcessName = r.ProcessName,
                ExecutedAtUtc = r.ExecutedAtUtc,
            }).ToList(),
        });
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
