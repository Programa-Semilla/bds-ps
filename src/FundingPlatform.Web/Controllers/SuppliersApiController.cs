// Spec 021 — see specs/021-feedback-session-may13/tasks.md T109
// and contracts/admin-routes.md (API: GET /api/suppliers/search).

using FundingPlatform.Application.Suppliers.Queries;
using FundingPlatform.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 021 / US3 / T109 / FR-009 — admin-side supplier autocomplete API.
/// Companion endpoint to the applicant-side
/// <c>GET /api/applications/suppliers/search</c> on
/// <see cref="ApplicationController"/>; this surface widens visibility to the
/// full catalog and is gated to <c>Admin</c> OR <c>SupplierAdmin</c> role
/// holders via <see cref="SupplierAdminOnlyAttribute"/>.
///
/// <para>
/// Response shape matches the applicant endpoint
/// (<c>{ id, name, cedulaJuridica }</c>) so the existing
/// <c>supplier-autocomplete.js</c> module can consume both. Capped at 25
/// results; performance target P95 ≤ 300 ms at 200+ supplier seed scale
/// (FR-009 / NFR-006 / SC-007).
/// </para>
/// </summary>
[Authorize]
[SupplierAdminOnly]
[Route("api/suppliers")]
public sealed class SuppliersApiController : Controller
{
    private readonly ISearchSuppliersForAdminHandler _handler;

    public SuppliersApiController(ISearchSuppliersForAdminHandler handler)
    {
        _handler = handler;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        var results = await _handler.HandleAsync(
            new SearchSuppliersForAdminQuery(q ?? string.Empty),
            ct);

        return Ok(results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            cedulaJuridica = r.CedulaJuridica,
            verificationStatus = r.VerificationStatus,
        }));
    }
}
