// Spec 045 — the group-scoped Desembolsos inbox landing (sidebar entry target).

using System.Security.Claims;
using FundingPlatform.Application.EvidenceInbox;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Web.ViewModels.Disbursements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 045 — the persistent disbursement inbox for a Financial Operator (and Admin):
/// executed applications whose governing Process is still <c>Active</c>, scoped to the
/// caller's groups. Reuses the spec-041 <see cref="IEvidenceInboxProjection"/> — the row
/// set is identical (executed apps in active processes, group-overlap in-query) — so no
/// new projection is introduced. Auditors do not receive the sidebar entry; the per-application
/// financial surface (with its read-only Auditor view) remains <c>DisbursementController</c>.
/// </summary>
[Authorize(Roles = "Financial Operator,Admin")]
[Route("Disbursements")]
public sealed class DisbursementInboxController : Controller
{
    private readonly IEvidenceInboxProjection _projection;
    private readonly IReviewerScopeProvider _scopeProvider;

    public DisbursementInboxController(
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

        return View(new DisbursementInboxViewModel
        {
            Rows = rows.Select(r => new DisbursementInboxRowViewModel
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
