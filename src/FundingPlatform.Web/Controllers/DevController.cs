using FundingPlatform.Application.Abstractions.Hacienda;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.BackgroundServices;
using FundingPlatform.Infrastructure.Hacienda;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly AppDbContext _db;

    public DevController(
        IWebHostEnvironment env,
        HaciendaSyncService haciendaSync,
        RegulatoryFreshnessDigestService freshnessDigest,
        AppDbContext db)
    {
        _env = env;
        _haciendaSync = haciendaSync;
        _freshnessDigest = freshnessDigest;
        _db = db;
    }

    /// <summary>
    /// Spec 048 — seeds a persisted reconciliation <see cref="Discrepancy"/> for an application so E2E
    /// can drive the lifecycle/dashboard/notification surfaces deterministically without constructing
    /// the (complex) underlying warning conditions through the UI. The seeded row is engine-managed like
    /// any other: it would auto-resolve on the next materialization if it is not backed by live data, so
    /// tests act on it before triggering a mutation. <c>severity</c> = "Warning" | "Blocking".
    /// </summary>
    [HttpGet("SeedDiscrepancy")]
    public async Task<IActionResult> SeedDiscrepancy(int applicationId, string severity, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var sentinel = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsSystemSentinel).Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(sentinel)) return BadRequest("no sentinel");

        var isWarning = string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase);
        var sev = isWarning ? DiscrepancySeverity.Warning : DiscrepancySeverity.Blocking;
        var comparison = isWarning
            ? ReconciliationComparison.PossibleDuplicatePayment
            : ReconciliationComparison.DisbursementVsInvoice;
        // Unique scope-entity id per call so repeated seeds never collide on UX_Discrepancies_Identity.
        var scopeEntityId = (int)(DateTimeOffset.UtcNow.Ticks % 1_000_000_0);
        var expected = 100_000m;
        var actual = isWarning ? 100_000m : 100_072m;

        var d = Discrepancy.Detect(
            applicationId, DiscrepancyScopeType.Payment, scopeEntityId, comparison, sev,
            expected, actual, 0m, isWarning ? "posible pago duplicado" : "factura", sentinel, DateTimeOffset.UtcNow);
        _db.Discrepancies.Add(d);
        await _db.SaveChangesAsync(ct);
        return Json(new { id = d.Id });
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
