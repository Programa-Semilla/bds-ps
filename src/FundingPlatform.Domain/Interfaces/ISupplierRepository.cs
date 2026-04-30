using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Interfaces;

public interface ISupplierRepository
{
    // Existing API — preserved for backwards-compat callers.
    Task<Supplier?> GetByLegalIdAsync(string legalId);
    Task AddAsync(Supplier supplier);
    Task<Supplier?> GetByIdAsync(int id);

    // Spec 013 additions: branch-aware queries used by SupplierCatalogService and the admin queue.
    Task<Supplier?> GetByLegalIdWithBranchesAsync(string legalId);
    Task<Supplier?> GetByIdWithBranchesAsync(int id);
    Task<(IReadOnlyList<Supplier> Items, int Total)> ListForAdminAsync(
        SupplierAdminFilter filter, int page, int pageSize);
    Task<int> CountReferencingApplicationsAsync(int supplierId);
    Task UpdateAsync(Supplier supplier);
    Task SaveChangesAsync();
}

/// <summary>
/// Filter for the admin Suppliers queue. All fields are optional; when null/blank
/// the corresponding predicate is omitted.
/// </summary>
public sealed class SupplierAdminFilter
{
    public SupplierVerificationStatus? Status { get; init; }
    public string? LegalIdContains { get; init; }
    public string? NameContains { get; init; }
    public bool? HasIncompleteCompliance { get; init; }
}
