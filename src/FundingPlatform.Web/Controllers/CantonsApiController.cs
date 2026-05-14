using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 021 / T053 / R-4 / FR-013 — Province → Cantón cascade API used by the
/// applicant supplier-branch form and the admin supplier-branch form.
///
/// <para>
/// Returns the cantones for a given province as <c>[{ id, name }]</c>.
/// Anonymous because the catalog is non-confidential and shared across roles.
/// </para>
///
/// <para>
/// Edge cache (<c>Cache-Control: public, max-age=3600</c>) — the CR Province/Cantón
/// catalog is effectively static (only changes via legislative redistricting), so
/// a 1-hour public cache is the right trade-off per the contract in
/// <c>contracts/admin-routes.md</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/cantons")]
[AllowAnonymous]
public sealed class CantonsApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public CantonsApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int provinceId, CancellationToken ct)
    {
        var cantons = await _dbContext.Cantons
            .Where(c => c.ProvinceId == provinceId)
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.Id, name = c.Name })
            .ToListAsync(ct);

        // R-4 — public, hour-long edge cache. CR Province/Cantón catalog is
        // legislatively static; the freshness cost of staleness is effectively
        // zero on the scale of HTTP cache lifetimes.
        Response.Headers.CacheControl = "public, max-age=3600";

        return new JsonResult(cantons);
    }
}
