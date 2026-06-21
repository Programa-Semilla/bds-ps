using FundingPlatform.Application.Abstractions.Location;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Application.Suppliers.Compliance;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

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
    private readonly AppDbContext _dbContext;
    private readonly ILocationCatalogReader _locationCatalog;
    private readonly Application.Admin.Filters.IFundHierarchyProvider _fundHierarchy;
    private readonly ISupplierComplianceService _compliance;

    public AdminSuppliersController(
        ISupplierRepository supplierRepository,
        IProcessQueryService processQuery,
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        ILocationCatalogReader locationCatalog,
        Application.Admin.Filters.IFundHierarchyProvider fundHierarchy,
        ISupplierComplianceService compliance)
    {
        _supplierRepository = supplierRepository;
        _processQuery = processQuery;
        _userManager = userManager;
        _dbContext = dbContext;
        _locationCatalog = locationCatalog;
        _fundHierarchy = fundHierarchy;
        _compliance = compliance;
    }

    // ---- Spec 025 — admin branch-edit location cascade ----

    /// <summary>
    /// Builds a branch's Provincia → Cantón → Distrito cascade pre-selected to its
    /// current values (provinces always; cantones for the branch's province; distritos
    /// for its cantón). <c>ElementIdPrefix</c> keeps element ids unique across the
    /// one-form-per-branch edit table.
    /// </summary>
    private async Task<LocationCascadeViewModel> BuildBranchLocationAsync(
        int branchId, int? provinceId, int? cantonId, int? districtId)
    {
        var provinces = await _dbContext.Provinces
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString()))
            .ToListAsync();

        var cantons = provinceId is int pid
            ? await _dbContext.Cantons.Where(c => c.ProvinceId == pid).OrderBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync()
            : new List<SelectListItem>();

        var districts = cantonId is int cid
            ? await _dbContext.Districts.Where(d => d.CantonId == cid).OrderBy(d => d.Name)
                .Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToListAsync()
            : new List<SelectListItem>();

        return new LocationCascadeViewModel
        {
            ElementIdPrefix = $"b{branchId}-",
            SelectedProvinceId = provinceId,
            SelectedCantonId = cantonId,
            SelectedDistrictId = districtId,
            Provinces = provinces,
            Cantons = cantons,
            Districts = districts,
        };
    }

    /// <summary>
    /// Resolves + validates a submitted location chain (FR-005). Returns the chain,
    /// or an aggregated es-CR error message for TempData (admin surface uses the
    /// redirect/TempData pattern rather than ModelState re-render).
    /// </summary>
    private async Task<(DistrictChain? Chain, string? Error)> ResolveBranchLocationAsync(
        int? provinceId, int? cantonId, int? districtId)
    {
        var errors = new List<string>();
        if (provinceId is null) errors.Add("La provincia es obligatoria.");
        if (cantonId is null) errors.Add("El cantón es obligatorio.");
        if (districtId is null) errors.Add("El distrito es obligatorio.");
        if (errors.Count > 0) return (null, string.Join(" ", errors));

        var chain = await _locationCatalog.GetDistrictChainAsync(districtId!.Value);
        if (chain is null) return (null, "El distrito seleccionado no es válido.");
        if (chain.CantonId != cantonId!.Value) return (null, "El distrito no corresponde al cantón.");
        if (chain.ProvinceId != provinceId!.Value) return (null, "El cantón no corresponde a la provincia.");
        return (chain, null);
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
        int? fundId,
        int? processId,
        bool? hasIncompleteCompliance,
        bool? syncFailed,
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
        var supplierAdminPath = User.IsInRole("Auditor") && !User.IsInRole("Admin");
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
            FundId = fundId,
            ProcessId = processId,
            SearchTerm = search,
            HaciendaSyncFailed = syncFailed,
        };

        var (items, total) = await _supplierRepository.ListForSupplierAdminAsync(filter, page, pageSize);

        var fundHierarchy = await _fundHierarchy.GetAsync(includeArchived: false, ct);

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
                s.LastUsedAt,
                s.HaciendaSyncOutcome)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            StatusFilter = effectiveStatus,
            LegalIdFilter = legalId,
            NameFilter = name,
            HasIncompleteCompliance = hasIncompleteCompliance == true,
            SearchTerm = search,
            FundFilter = fundId,
            ProcessIdFilter = processId,
            SyncFailedFilter = syncFailed == true,
            FundHierarchy = fundHierarchy,
        };

        return View(vm);
    }

    [HttpGet("{supplierId:int}")]
    public async Task<IActionResult> Detail(int supplierId)
    {
        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        var refCount = await _supplierRepository.CountReferencingApplicationsAsync(supplierId);

        // Spec 025 — build each branch's pre-selected location cascade (one form per branch).
        var orderedBranches = supplier.Branches
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.BranchName)
            .ToList();
        var branchRows = new List<AdminSupplierBranchRowViewModel>(orderedBranches.Count);
        foreach (var b in orderedBranches)
        {
            var location = await BuildBranchLocationAsync(b.Id, b.ProvinceId, b.CantonId, b.DistrictId);
            branchRows.Add(new AdminSupplierBranchRowViewModel(
                b.Id, b.BranchName, b.ContactName, b.Email, b.Phone,
                b.AddressLine, b.Province, b.ShippingDetails, b.WarrantyInfo, b.IsDefault, location));
        }

        // Spec 038 — resolve the per-field last-reviewer ids to display names for
        // the freshness line.
        var reviewerIds = new[]
            {
                supplier.HaciendaLastReviewedBy,
                supplier.CcssLastReviewedBy,
                supplier.SicopLastReviewedBy,
            }
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        var reviewerNames = reviewerIds.Count == 0
            ? new Dictionary<string, string>()
            : await _dbContext.Users
                .Where(u => reviewerIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => string.IsNullOrWhiteSpace($"{u.FirstName} {u.LastName}".Trim())
                        ? (u.Email ?? u.Id)
                        : $"{u.FirstName} {u.LastName}".Trim());
        string? NameOf(string? id) =>
            !string.IsNullOrEmpty(id) && reviewerNames.TryGetValue(id, out var n) ? n : null;

        var vm = new AdminSupplierDetailViewModel
        {
            Id = supplier.Id,
            LegalId = supplier.LegalId,
            Name = supplier.Name,
            Status = supplier.VerificationStatus,
            HaciendaStatus = supplier.HaciendaStatus,
            HaciendaReviewedAt = supplier.HaciendaLastReviewedAt,
            HaciendaReviewedByName = NameOf(supplier.HaciendaLastReviewedBy),
            HaciendaReviewedSource = supplier.HaciendaLastReviewedSource,
            CcssStatus = supplier.CcssStatus,
            CcssReviewedAt = supplier.CcssLastReviewedAt,
            CcssReviewedByName = NameOf(supplier.CcssLastReviewedBy),
            CcssReviewedSource = supplier.CcssLastReviewedSource,
            SicopStatus = supplier.SicopStatus,
            SicopReviewedAt = supplier.SicopLastReviewedAt,
            SicopReviewedByName = NameOf(supplier.SicopLastReviewedBy),
            SicopReviewedSource = supplier.SicopLastReviewedSource,
            IsPmeOrPyme = supplier.IsPmeOrPyme,
            HasWarning = supplier.HasWarning,
            WarningNote = supplier.WarningNote,
            RowVersion = supplier.RowVersion,
            // Spec 043 (US3) — last Hacienda sync outcome surface.
            HaciendaSyncAttemptAt = supplier.HaciendaSyncAttemptAt,
            HaciendaSyncOutcome = supplier.HaciendaSyncOutcome,
            HaciendaSyncError = supplier.HaciendaSyncError,
            VerifiedByUserId = supplier.VerifiedByUserId,
            VerifiedAt = supplier.VerifiedAt,
            RejectionReason = supplier.RejectionReason,
            CreatedByApplicantId = supplier.CreatedByApplicantId,
            ReferencingApplicationCount = refCount,
            Branches = branchRows,
        };

        return View(vm);
    }

    [HttpPost("{supplierId:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int supplierId, AdminEditSupplierViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Datos inválidos.";
            return RedirectToAction(nameof(Detail), new { supplierId });
        }

        var actorId = _userManager.GetUserId(User)
            ?? throw new InvalidOperationException("Auditor user not found.");

        // Spec 038 — compliance/PME/warning flow through the audited service; name
        // rides along on the same form.
        var cmd = new EditSupplierComplianceCommand(
            supplierId,
            model.Name,
            model.Hacienda,
            model.Ccss,
            model.Sicop,
            model.IsPmeOrPyme,
            model.HasWarning,
            model.WarningNote,
            actorId,
            model.RowVersion);

        var result = await _compliance.EditComplianceAsync(cmd, ct);
        if (!result.Ok)
            TempData["ErrorMessage"] = result.ErrorEsCr;
        else
            TempData["SuccessMessage"] = "Proveedor actualizado.";

        return RedirectToAction(nameof(Detail), new { supplierId });
    }

    /// <summary>
    /// Spec 038 (US2) — "reviewed — no change" re-authorization for one regulatory
    /// field. Refreshes the field's freshness timestamp without changing the value.
    /// </summary>
    [HttpPost("{supplierId:int}/ConfirmReviewed")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReviewed(
        int supplierId, RegulatoryField field, byte[] rowVersion, CancellationToken ct)
    {
        // Default MVC enum binding admits any in-range byte; reject a tampered/
        // out-of-defined-range field with an es-CR message instead of a 500.
        if (!Enum.IsDefined(field))
        {
            TempData["ErrorMessage"] = "Campo regulatorio inválido.";
            return RedirectToAction(nameof(Detail), new { supplierId });
        }

        var actorId = _userManager.GetUserId(User)
            ?? throw new InvalidOperationException("Auditor user not found.");

        var result = await _compliance.ConfirmReviewedAsync(supplierId, field, actorId, rowVersion, ct);
        if (!result.Ok)
            TempData["ErrorMessage"] = result.ErrorEsCr;
        else
            TempData["SuccessMessage"] = "Revisión confirmada.";

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

        // Spec 025 — resolve + validate the submitted Provincia → Cantón → Distrito chain
        // server-side before any write; aggregated message surfaced on the Detail page.
        var (chain, locationError) = await ResolveBranchLocationAsync(
            model.ProvinceId, model.CantonId, model.DistrictId);
        if (chain is null)
        {
            TempData["ErrorMessage"] = locationError;
            return RedirectToAction(nameof(Detail), new { supplierId });
        }

        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId);
        if (supplier is null) return NotFound();

        supplier.EditBranch(branchId,
            model.BranchName, model.ContactName, model.Email, model.Phone,
            model.AddressLine, chain.ComposedDisplay, model.ShippingDetails, model.WarrantyInfo,
            provinceId: chain.ProvinceId, cantonId: chain.CantonId, districtId: chain.DistrictId,
            canton: chain.Canton, district: chain.District);

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
