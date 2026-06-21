using FundingPlatform.Application.Abstractions.Hacienda;
using FundingPlatform.Infrastructure.BackgroundServices;
using FundingPlatform.Infrastructure.Hacienda;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 043 — Development-only triggers for the daily regulatory workers, mirroring the
/// existing <c>/Account/SeedUser</c> dev seam (404 outside Development; no UI). They let
/// E2E run the Hacienda sync / freshness digest deterministically on demand and stage
/// <see cref="FakeHaciendaApiClient"/> outcomes (the live API is never called in tests).
/// </summary>
[AllowAnonymous]
[Route("Dev")]
public sealed class DevController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly HaciendaSyncService _haciendaSync;
    private readonly RegulatoryFreshnessDigestService _freshnessDigest;

    public DevController(
        IWebHostEnvironment env,
        HaciendaSyncService haciendaSync,
        RegulatoryFreshnessDigestService freshnessDigest)
    {
        _env = env;
        _haciendaSync = haciendaSync;
        _freshnessDigest = freshnessDigest;
    }

    [HttpGet("RunHaciendaSync")]
    public async Task<IActionResult> RunHaciendaSync(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var summary = await _haciendaSync.RunOnceAsync(ct);
        return Json(summary);
    }

    [HttpGet("RunFreshnessDigest")]
    public async Task<IActionResult> RunFreshnessDigest(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var sent = await _freshnessDigest.RunOnceAsync(ct);
        return Json(new { sent });
    }

    /// <summary>
    /// Stages a <see cref="FakeHaciendaApiClient"/> outcome for an identification (or the
    /// default when none is given) so E2E can drive changed/unchanged/404/failure paths.
    /// </summary>
    [HttpGet("StageHaciendaOutcome")]
    public IActionResult StageHaciendaOutcome(string? identificacion, string kind)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var result = BuildResult(kind);
        if (string.IsNullOrWhiteSpace(identificacion))
        {
            FakeHaciendaApiClient.StageDefault(result);
        }
        else
        {
            FakeHaciendaApiClient.StageOutcome(identificacion, result);
        }
        return Ok($"staged {kind}");
    }

    private static HaciendaLookupResult BuildResult(string kind) => (kind ?? string.Empty).ToLowerInvariant() switch
    {
        "aldia" => HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, false)),
        "moroso" => HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", true, false)),
        "cobroadmin" => HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, true)),
        "desinscrito" => HaciendaLookupResult.Found(null, new HaciendaSituacion("Desinscrito", false, false)),
        "desinscritomoroso" => HaciendaLookupResult.Found(null, new HaciendaSituacion("Desinscrito", true, false)),
        "noinscrito" => HaciendaLookupResult.Found(null, new HaciendaSituacion("No inscrito", false, false)),
        "notregistered" => HaciendaLookupResult.NotRegistered(),
        _ => HaciendaLookupResult.Failed("Error simulado de verificación."),
    };
}
