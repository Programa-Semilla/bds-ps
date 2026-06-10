// Spec 029 / US3 — see specs/029-fund-entity/contracts/ui-and-routes.md
// (Applicant regulation download).

using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 029 / US3 / FR-013 — streams a Fund's regulation PDF. The application
/// boundary is the single auth point: any authenticated user may download a
/// present regulation while the Fund is Active; admins may also download an
/// archived Fund's regulation (for catalog curation). Everything else is a flat
/// 404 (no disclosure of whether the Fund/regulation exists). Serving uses
/// <see cref="ServingMode.BackendStream"/> like the public-landing slots.
/// </summary>
[Authorize]
[Route("Funds")]
public class FundRegulationController : Controller
{
    private const FileCategory Category = FileCategory.FundRegulation;

    private readonly AppDbContext _db;
    private readonly IObjectStorage _storage;

    public FundRegulationController(AppDbContext db, IObjectStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpGet("{fundId:int}/Regulation/Download")]
    public async Task<IActionResult> Download(int fundId, CancellationToken ct)
    {
        var fund = await _db.Funds.AsNoTracking()
            .Where(f => f.Id == fundId)
            .Select(f => new { f.Status, f.RegulationBlobKey, f.RegulationFileName })
            .FirstOrDefaultAsync(ct);

        if (fund is null || string.IsNullOrWhiteSpace(fund.RegulationBlobKey))
        {
            return NotFound();
        }

        // Non-admins only reach an Active Fund's regulation (FR-020 freeze posture).
        if (fund.Status == FundStatus.Archived && !User.IsInRole("Admin"))
        {
            return NotFound();
        }

        ObjectKey key;
        try
        {
            key = ObjectKey.Parse(fund.RegulationBlobKey);
        }
        catch
        {
            return NotFound();
        }

        BackendStreamHandle handle;
        try
        {
            var resolved = await _storage.ResolveServingHandleAsync(
                Category, key, ServingMode.BackendStream, ct);
            handle = (BackendStreamHandle)resolved;
        }
        catch (ObjectNotFoundException)
        {
            return NotFound();
        }

        var fileName = string.IsNullOrWhiteSpace(fund.RegulationFileName)
            ? "Reglamento.pdf"
            : fund.RegulationFileName;

        return File(handle.Content, handle.ContentType ?? "application/pdf", fileDownloadName: fileName);
    }
}
