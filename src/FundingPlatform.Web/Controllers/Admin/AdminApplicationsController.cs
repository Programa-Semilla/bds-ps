// Spec 021 — see specs/021-feedback-session-may13/tasks.md T154 and FR-021.

using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 021 / US8 / T154 / FR-021 — admin-only Application soft-delete endpoint.
/// The single call site for <see cref="Domain.Entities.Application.SoftDelete"/>
/// outside of the domain layer; routes the mutation through
/// <see cref="IApplicationRepository"/> so EF tracks the <c>DeletedAt</c> column
/// update. Once persisted, every dashboard surface filters the row out via
/// <see cref="Application.Abstractions.IApplicationQueryFilter.ExcludeDeleted"/>
/// (the audit lives in <c>DashboardQueriesHonorSoftDeleteTests</c>).
/// </summary>
[Authorize(Roles = "Admin")]
[SupplierAdminDenied]
[Route("Admin/Applications")]
public sealed class AdminApplicationsController : Controller
{
    private readonly IApplicationRepository _applications;

    public AdminApplicationsController(IApplicationRepository applications)
    {
        _applications = applications;
    }

    /// <summary>
    /// Spec 021 / FR-021 / T154 — soft-deletes the Application by setting
    /// <c>DeletedAt = UTC now</c> via the domain <c>SoftDelete()</c> method.
    /// Idempotent (the domain guards a no-op on already-deleted rows). Returns
    /// 200 on success, 404 when the Application does not exist.
    /// </summary>
    [HttpPost("{id:int}/SoftDelete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SoftDelete(int id)
    {
        var application = await _applications.GetByIdAsync(id);
        if (application is null)
        {
            return NotFound();
        }

        application.SoftDelete();
        await _applications.UpdateAsync(application);
        await _applications.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Solicitud {id} eliminada.";
        return Ok(new { id, deletedAt = application.DeletedAt });
    }
}
