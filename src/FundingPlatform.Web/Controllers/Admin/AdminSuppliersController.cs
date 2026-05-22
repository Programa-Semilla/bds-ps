using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Admin Suppliers area (spec 013).
/// FR-036 / FR-037: this controller intentionally exposes no Delete or Create
/// actions in v1. Admin-only edit, verify, and reject flows on applicant-initiated
/// suppliers only.
///
/// Spec 021 / US3 / T107 / FR-007 — opens this surface to the new
/// <c>SupplierAdmin</c> role via <see cref="SupplierAdminOnlyAttribute"/>.
/// The base <c>[Authorize]</c> still enforces authentication; the attribute
/// substitutes role gating so Admin OR SupplierAdmin passes. Every other
/// <c>/Admin/*</c> controller carries <see cref="SupplierAdminDeniedAttribute"/>
/// — together they implement the FR-007 matrix.
/// </summary>
[Authorize]
[SupplierAdminOnly]
[Route("Admin/Suppliers")]
public class AdminSuppliersController : Controller
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IProcessQueryService _processQuery;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminSuppliersController(
        ISupplierRepository supplierRepository,
        IProcessQueryService processQuery,
        UserManager<ApplicationUser> userManager)
    {
        _supplierRepository = supplierRepository;
        _processQuery = processQuery;
        _userManager = userManager;
    }

    /// <summary>
    /// Spec 021 / US3 / T108 / FR-009 / FR-011 — supplier-admin list. Default
    /// sort by <c>LastUsedAt DESC</c>; Process filter (FR-011); single search
    /// term across Name + CédulaJurídica (FR-009). The legacy spec-013
    /// status/legalId/name/incomplete filters remain functional for the Admin
    /// caller path but are absent from the SupplierAdmin UI.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(
        SupplierVerificationStatus? status,
        string? legalId,
        string? name,
        string? search,
        int? processId,
        bool? hasIncompleteCompliance,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        // Spec 013 FR-030: legacy default filter on entry is PendingReview.
        // Spec 021 / FR-011: SupplierAdmin path defaults to *no* status filter
        // (all suppliers, sorted by LastUsedAt DESC). The default flips when
        // either the search box, Process filter, or pageSize > default are
        // present — both behaviours coexist via the explicit `status` query
        // parameter test.
        var supplierAdminPath = User.IsInRole("SupplierAdmin") && !User.IsInRole("Admin");
        var effectiveStatus = status ?? (Request.Query.ContainsKey(nameof(status))
            ? (SupplierVerificationStatus?)null
            : supplierAdminPath
                ? null
                : SupplierVerificationStatus.PendingReview);

        var filter = new SupplierAdminFilter
        {
            Status = effectiveStatus,
            LegalIdContains = legalId,
            NameContains = name,
            HasIncompleteCompliance = hasIncompleteCompliance,
            ProcessId = processId,
            SearchTerm = search,
        };

        var (items, total) = await _supplierRepository.ListForSupplierAdminAsync(filter, page, pageSize);

        var processOptions = await _processQuery.ListAsync(null, ct);

        var vm = new AdminSupplierListViewModel
        {
            Items = items.Select(s => new AdminSupplierRowViewModel(
                s.Id,
                s.LegalId,
                s.Name,
                s.Status,
                s.BranchCount,
                s.HasIncompleteCompliance,
                s.UpdatedAt,
                s.LastUsedAt)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            StatusFilter = effectiveStatus,
            LegalIdFilter = legalId,
            NameFilter = name,
            HasIncompleteCompliance = hasIncompleteCompliance == true,
            SearchTerm = search,
            ProcessIdFilter = processId,
            ProcessOptions = processOptions
                .Select(p => (p.Id, p.Name))
                .ToList(),
        };

        return View(vm);
    }

    [HttpGet("{supplierId:int}")]
    public async Task<IActionResult> Detail(int supplierId)
    {
        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        var refCount = await _supplierRepository.CountReferencingApplicationsAsync(supplierId);

        var vm = new AdminSupplierDetailViewModel
        {
            Id = supplier.Id,
            LegalId = supplier.LegalId,
            Name = supplier.Name,
            Status = supplier.VerificationStatus,
            HasElectronicInvoice = supplier.HasElectronicInvoice,
            IsCompliantCCSS = supplier.IsCompliantCCSS,
            IsCompliantHacienda = supplier.IsCompliantHacienda,
            IsCompliantSICOP = supplier.IsCompliantSICOP,
            VerifiedByUserId = supplier.VerifiedByUserId,
            VerifiedAt = supplier.VerifiedAt,
            RejectionReason = supplier.RejectionReason,
            CreatedByApplicantId = supplier.CreatedByApplicantId,
            ReferencingApplicationCount = refCount,
            Branches = supplier.Branches
                .OrderByDescending(b => b.IsDefault)
                .ThenBy(b => b.BranchName)
                .Select(b => new AdminSupplierBranchRowViewModel(
                    b.Id, b.BranchName, b.ContactName, b.Email, b.Phone,
                    b.AddressLine, b.Province, b.ShippingDetails, b.WarrantyInfo, b.IsDefault))
                .ToList(),
        };

        return View(vm);
    }

    [HttpPost("{supplierId:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int supplierId, AdminEditSupplierViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Datos inválidos.";
            return RedirectToAction(nameof(Detail), new { supplierId });
        }

        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        supplier.EditByAdmin(
            model.Name,
            model.HasElectronicInvoice,
            model.IsCompliantCCSS,
            model.IsCompliantHacienda,
            model.IsCompliantSICOP);

        await _supplierRepository.UpdateAsync(supplier);
        await _supplierRepository.SaveChangesAsync();

        TempData["SuccessMessage"] = "Proveedor actualizado.";
        return RedirectToAction(nameof(Detail), new { supplierId });
    }

    [HttpPost("{supplierId:int}/Branch/{branchId:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBranch(int supplierId, int branchId, AdminEditBranchViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Datos inválidos.";
            return RedirectToAction(nameof(Detail), new { supplierId });
        }

        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        supplier.EditBranch(branchId,
            model.BranchName, model.ContactName, model.Email, model.Phone,
            model.AddressLine, model.Province, model.ShippingDetails, model.WarrantyInfo);

        await _supplierRepository.UpdateAsync(supplier);
        await _supplierRepository.SaveChangesAsync();

        TempData["SuccessMessage"] = "Sucursal actualizada.";
        return RedirectToAction(nameof(Detail), new { supplierId });
    }

    [HttpPost("{supplierId:int}/Verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(int supplierId)
    {
        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        try
        {
            var actorId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Admin user not found.");
            supplier.Verify(actorId);
            await _supplierRepository.UpdateAsync(supplier);
            await _supplierRepository.SaveChangesAsync();
            TempData["SuccessMessage"] = "Proveedor verificado.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { supplierId });
    }

    [HttpPost("{supplierId:int}/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int supplierId, AdminRejectSupplierViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Indica la razón de rechazo.";
            return RedirectToAction(nameof(Detail), new { supplierId });
        }

        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        try
        {
            var actorId = _userManager.GetUserId(User) ?? throw new InvalidOperationException("Admin user not found.");
            supplier.Reject(actorId, model.Reason);
            await _supplierRepository.UpdateAsync(supplier);
            await _supplierRepository.SaveChangesAsync();
            TempData["SuccessMessage"] = "Proveedor rechazado.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { supplierId });
    }
}
