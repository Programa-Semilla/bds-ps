using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Suppliers.DTOs;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.Suppliers.Services;

/// <summary>
/// Application-layer orchestration for the centralized supplier catalog (spec 013).
/// Owns the visibility-filtered legal-ID lookup, branch addition under existing
/// suppliers, and Draft creation with concurrent-insert recovery (R4).
/// </summary>
public class SupplierCatalogService
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<SupplierCatalogService> _logger;

    public SupplierCatalogService(
        ISupplierRepository supplierRepository,
        IApplicationRepository applicationRepository,
        ILogger<SupplierCatalogService> logger)
    {
        _supplierRepository = supplierRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    /// <summary>
    /// Looks up a supplier by legal ID and applies the visibility filter:
    ///   - Verified                  -> visible to all
    ///   - PendingReview / Draft     -> visible only to creator
    ///   - Rejected                  -> returns Rejected outcome (no Hit)
    ///   - other / nothing           -> Empty
    /// </summary>
    public async Task<SupplierLookupResultDto> SearchByLegalIdAsync(
        string legalId, int currentApplicantId)
    {
        if (string.IsNullOrWhiteSpace(legalId))
            return new SupplierLookupResultDto(SupplierLookupOutcome.Empty, null);

        var supplier = await _supplierRepository.GetByLegalIdWithBranchesAsync(legalId);
        if (supplier is null)
            return new SupplierLookupResultDto(SupplierLookupOutcome.Empty, null);

        return supplier.VerificationStatus switch
        {
            SupplierVerificationStatus.Verified =>
                new SupplierLookupResultDto(SupplierLookupOutcome.Hit, MapToDetail(supplier)),

            SupplierVerificationStatus.PendingReview when supplier.CreatedByApplicantId == currentApplicantId =>
                new SupplierLookupResultDto(SupplierLookupOutcome.Hit, MapToDetail(supplier)),

            SupplierVerificationStatus.Draft when supplier.CreatedByApplicantId == currentApplicantId =>
                new SupplierLookupResultDto(SupplierLookupOutcome.Hit, MapToDetail(supplier)),

            SupplierVerificationStatus.Rejected =>
                new SupplierLookupResultDto(SupplierLookupOutcome.Rejected, null),

            // Other applicants' Draft / PendingReview are indistinguishable from "nothing exists".
            _ => new SupplierLookupResultDto(SupplierLookupOutcome.Empty, null),
        };
    }

    /// <summary>
    /// Loads the supplier (with branches) by Id and asserts the requested branch
    /// belongs to it. Returns the (Supplier, SupplierBranch) pair to the caller
    /// who then writes the Quotation.
    /// </summary>
    public async Task<(Supplier Supplier, SupplierBranch Branch)> LoadSupplierAndBranchAsync(
        int supplierId, int branchId)
    {
        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId)
            ?? throw new InvalidOperationException($"Supplier {supplierId} not found.");

        if (supplier.VerificationStatus == SupplierVerificationStatus.Rejected)
            throw new InvalidOperationException($"Supplier {supplierId} is Rejected and cannot accept new quotations.");

        var branch = supplier.Branches.FirstOrDefault(b => b.Id == branchId)
            ?? throw new InvalidOperationException($"Branch {branchId} does not belong to supplier {supplierId}.");

        return (supplier, branch);
    }

    /// <summary>
    /// Adds a new branch under an existing supplier (US2, FR-012).
    /// Returns the new branch's ID after persistence.
    /// </summary>
    public async Task<int> AddBranchUnderExistingSupplierAsync(
        int supplierId, AddBranchInput input, int createdByApplicantId)
    {
        ArgumentNullException.ThrowIfNull(input);

        var supplier = await _supplierRepository.GetByIdWithBranchesAsync(supplierId)
            ?? throw new InvalidOperationException($"Supplier {supplierId} not found.");

        if (supplier.VerificationStatus == SupplierVerificationStatus.Rejected)
            throw new InvalidOperationException($"Supplier {supplierId} is Rejected; cannot add a branch.");

        var branch = supplier.AddBranch(
            input.BranchName,
            input.ContactName,
            input.Email,
            input.Phone,
            input.AddressLine,
            input.Province,
            input.ShippingDetails,
            input.WarrantyInfo,
            createdByApplicantId,
            isDefault: false,
            provinceId: input.ProvinceId,
            cantonId: input.CantonId,
            districtId: input.DistrictId,
            canton: input.Canton,
            district: input.District);

        await _supplierRepository.UpdateAsync(supplier);
        await _supplierRepository.SaveChangesAsync();

        return branch.Id;
    }

    /// <summary>
    /// Creates a brand-new Draft supplier with one default branch (US3, FR-021).
    /// Catches SqlException 2627 (UNIQUE on LegalId) and returns RetryWithExisting
    /// pointing at the now-existing supplier (R4 concurrent-insert recovery).
    /// </summary>
    public async Task<CreateDraftResult> CreateDraftWithBranchAsync(
        string legalId, string name, AddBranchInput firstBranch, int createdByApplicantId,
        IdentificationType? identificationType = null)
    {
        ArgumentNullException.ThrowIfNull(firstBranch);

        var canonical = Supplier.NormalizeLegalId(legalId);

        var supplier = Supplier.CreateDraft(
            canonical,
            name,
            createdByApplicantId,
            firstBranch.BranchName,
            firstBranch.ContactName,
            firstBranch.Email,
            firstBranch.Phone,
            firstBranch.AddressLine,
            firstBranch.Province,
            firstBranch.ShippingDetails,
            firstBranch.WarrantyInfo,
            firstBranchProvinceId: firstBranch.ProvinceId,
            firstBranchCantonId: firstBranch.CantonId,
            firstBranchDistrictId: firstBranch.DistrictId,
            firstBranchCanton: firstBranch.Canton,
            firstBranchDistrict: firstBranch.District,
            // Spec 026 — persist the supplier identification kind. The legal ID is
            // already canonical (NormalizeLegalId == VO canonical for the 10-digit
            // jurídica/NITE shape), so the VO inside CreateDraft is idempotent.
            identificationType: identificationType);

        try
        {
            await _supplierRepository.AddAsync(supplier);
            await _supplierRepository.SaveChangesAsync();
            return CreateDraftResult.Success(supplier.Id);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogInformation(
                "Concurrent insert collision on Suppliers.LegalId='{LegalId}' by applicant {Applicant}; redirecting to existing supplier.",
                canonical, createdByApplicantId);
            var existing = await _supplierRepository.GetByLegalIdWithBranchesAsync(canonical)
                ?? throw new InvalidOperationException("UNIQUE collision recovery: existing supplier not found.");
            return CreateDraftResult.RetryWithExisting(existing.Id);
        }
    }

    /// <summary>
    /// Asserts the applicant is allowed to edit a Draft supplier they created on
    /// a Draft application. Throws UnauthorizedAccessException otherwise (FR-013).
    /// </summary>
    public async Task AssertEditableByApplicantAsync(
        int supplierId, int currentApplicantId, int currentApplicationId)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId)
            ?? throw new InvalidOperationException($"Supplier {supplierId} not found.");

        if (supplier.VerificationStatus != SupplierVerificationStatus.Draft)
            throw new UnauthorizedAccessException("Cannot edit a non-Draft supplier.");

        if (supplier.CreatedByApplicantId != currentApplicantId)
            throw new UnauthorizedAccessException("Cannot edit a supplier you did not create.");

        var application = await _applicationRepository.GetByIdAsync(currentApplicationId)
            ?? throw new InvalidOperationException($"Application {currentApplicationId} not found.");

        if (application.State != Domain.Enums.ApplicationState.Draft)
            throw new UnauthorizedAccessException("Cannot edit suppliers attached to a non-Draft application.");
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        // Avoid a SqlClient project reference in the Application layer by detecting
        // the SQL Server unique-constraint exception via its type name and the
        // dynamic Number property. Numbers: 2627 (UNIQUE constraint), 2601 (duplicate
        // key in unique index). Walk the entire exception chain starting at ex itself
        // so we catch both EF-wrapped (DbUpdateException -> SqlException) and direct
        // SqlException paths.
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur.GetType().Name == "SqlException")
            {
                var numberProp = cur.GetType().GetProperty("Number");
                if (numberProp?.GetValue(cur) is int number && (number == 2627 || number == 2601))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static SupplierDetailViewDto MapToDetail(Supplier s) => new(
        Id: s.Id,
        LegalId: s.LegalId,
        Name: s.Name,
        HasElectronicInvoice: s.HasElectronicInvoice,
        IsCompliantCCSS: s.IsCompliantCCSS,
        IsCompliantHacienda: s.IsCompliantHacienda,
        IsCompliantSICOP: s.IsCompliantSICOP,
        VerificationStatus: s.VerificationStatus,
        CreatedByApplicantId: s.CreatedByApplicantId,
        Branches: s.Branches
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.BranchName)
            .Select(b => new SupplierBranchDto(
                b.Id, b.SupplierId, b.BranchName, b.ContactName, b.Email, b.Phone,
                b.AddressLine, b.Province, b.ShippingDetails, b.WarrantyInfo,
                b.IsDefault, b.CreatedByApplicantId))
            .ToList());
}

