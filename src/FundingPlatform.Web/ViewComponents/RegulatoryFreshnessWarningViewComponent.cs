using FundingPlatform.Application.Regulatory;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.ViewComponents;

/// <summary>
/// Spec 043 / US4 (FR-010) — renders a non-blocking warning naming the stale/never-reviewed
/// providers + fields an application relies on. Surfaced on the reviewer send-to-audit
/// screen and the auditor detail screen as an early heads-up before the hard gate (US1).
/// Renders nothing when all relied-on providers are fresh.
/// </summary>
public sealed class RegulatoryFreshnessWarningViewComponent : ViewComponent
{
    private readonly IRegulatoryFreshnessService _freshness;

    public RegulatoryFreshnessWarningViewComponent(IRegulatoryFreshnessService freshness)
        => _freshness = freshness;

    public async Task<IViewComponentResult> InvokeAsync(int applicationId)
    {
        var findings = await _freshness.GetStaleFindingsForApplicationAsync(
            applicationId, HttpContext.RequestAborted);
        return View(findings);
    }
}
