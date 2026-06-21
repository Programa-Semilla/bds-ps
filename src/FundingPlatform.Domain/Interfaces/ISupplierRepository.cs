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

    // Spec 013: batch-load helper used by ApplicationService.SubmitAsync to avoid
    // N+1 round-trips when flipping multiple Draft suppliers to PendingReview.
    Task<IReadOnlyList<Supplier>> ListByIdsWithBranchesAsync(IReadOnlyCollection<int> supplierIds);

    // Spec 021 / US3 / T108 / FR-011 — supplier-admin list with default sort
    // by LastUsedAt DESC (derived from MAX(Quotation.CreatedAt) across the
    // supplier's quotations) and an optional Process filter (matches
    // suppliers used by Applications whose Applicant's Group sits under the
    // given Process). Returns the supplier core fields + computed LastUsedAt.
    Task<(IReadOnlyList<SupplierAdminLastUsedRow> Items, int Total)> ListForSupplierAdminAsync(
        SupplierAdminFilter filter, int page, int pageSize);
}

/// <summary>
/// Spec 021 / T108 / FR-011 — flat row used on the supplier-admin list. Carries
/// the computed <see cref="LastUsedAt"/> (most-recent Quotation.CreatedAt) so
/// the view can render and sort by it without further repo calls.
/// </summary>
public sealed record SupplierAdminLastUsedRow(
    int Id,
    string LegalId,
    string Name,
    SupplierVerificationStatus Status,
    int BranchCount,
    bool HasIncompleteCompliance,
    DateTime UpdatedAt,
    DateTime? LastUsedAt,
    // Spec 043 (US3) — last Hacienda sync outcome for the row badge / "verificación fallida" filter.
    HaciendaSyncOutcome? HaciendaSyncOutcome = null);

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

    /// <summary>
    /// Spec 021 / US3 / T108 / FR-011 — restrict the list to suppliers
    /// referenced by Applications whose Applicant's Group belongs to the
    /// given Process. When null, the filter is omitted.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// Restrict the list to suppliers referenced by Applications whose
    /// Applicant's Group belongs to a Process under the given Fund (Fondo). When
    /// null, the filter is omitted. Composes with <see cref="ProcessId"/>.
    /// </summary>
    public int? FundId { get; init; }

    /// <summary>
    /// Spec 021 / FR-009 — single search term applied to both Name and
    /// CédulaJurídica (legalId). When set, supersedes the legacy
    /// <see cref="LegalIdContains"/> + <see cref="NameContains"/> pair.
    /// </summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// Spec 043 / US3 / FR-020 — when true, restrict the list to providers whose last
    /// Hacienda sync attempt failed (<c>HaciendaSyncOutcome == Failure</c>). When null,
    /// the filter is omitted.
    /// </summary>
    public bool? HaciendaSyncFailed { get; init; }
}
