using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 025 / FR-002 — Cantón → Distrito cascade API used by the third tier of
/// the applicant + admin supplier-branch location cascade. Mirrors
/// <see cref="CantonsApiController"/>.
///
/// <para>
/// Returns the distritos for a given cantón as <c>[{ id, name }]</c>, ordered by
/// name. Anonymous because the catalog is non-confidential and shared across roles.
/// </para>
///
/// <para>
/// Edge cache (<c>Cache-Control: public, max-age=3600</c>) — the CR distrito
/// catalog is legislatively static, so a 1-hour public cache is the right
/// trade-off (identical rationale to <c>/api/cantons</c>; see
/// contracts/districts-api.md).
/// </para>
/// </summary>
[ApiController]
[Route("api/districts")]
[AllowAnonymous]
public sealed class DistrictsApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public DistrictsApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int cantonId, CancellationToken ct)
    {
        var districts = await _dbContext.Districts
            .Where(d => d.CantonId == cantonId)
            .OrderBy(d => d.Name)
            .Select(d => new { id = d.Id, name = d.Name })
            .ToListAsync(ct);

        // Public, hour-long edge cache. CR distrito catalog is legislatively
        // static; the freshness cost of staleness is effectively zero on the
        // scale of HTTP cache lifetimes. Unknown cantonId → empty array (not an
        // error), matching the cantons endpoint.
        Response.Headers.CacheControl = "public, max-age=3600";

        return new JsonResult(districts);
    }
}
