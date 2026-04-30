using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
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
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/Suppliers")]
public class AdminSuppliersController : Controller
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminSuppliersController(
        ISupplierRepository supplierRepository,
        UserManager<ApplicationUser> userManager)
    {
        _supplierRepository = supplierRepository;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        SupplierVerificationStatus? status,
        string? legalId,
        string? name,
        bool? hasIncompleteCompliance,
        int page = 1,
        int pageSize = 25)
    {
        // Spec 013 FR-030: default filter on entry is PendingReview.
        var effectiveStatus = status ?? (Request.Query.ContainsKey(nameof(status))
            ? (SupplierVerificationStatus?)null
            : SupplierVerificationStatus.PendingReview);

        var filter = new SupplierAdminFilter
        {
            Status = effectiveStatus,
            LegalIdContains = legalId,
            NameContains = name,
            HasIncompleteCompliance = hasIncompleteCompliance,
        };

        var (items, total) = await _supplierRepository.ListForAdminAsync(filter, page, pageSize);

        var vm = new AdminSupplierListViewModel
        {
            Items = items.Select(s => new AdminSupplierRowViewModel(
                s.Id,
                s.LegalId,
                s.Name,
                s.VerificationStatus,
                s.Branches.Count,
                !s.IsCompliantCCSS || !s.IsCompliantHacienda || !s.IsCompliantSICOP || !s.HasElectronicInvoice,
                s.UpdatedAt)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            StatusFilter = effectiveStatus,
            LegalIdFilter = legalId,
            NameFilter = name,
            HasIncompleteCompliance = hasIncompleteCompliance == true,
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
