// Spec 021 — see specs/021-feedback-session-may13/tasks.md T110
// and contracts/admin-routes.md (API: GET /api/suppliers/search).

namespace FundingPlatform.Application.Suppliers.Queries;

/// <summary>
/// Spec 021 / US3 / T110 / FR-009 — admin-side supplier autocomplete query.
/// Distinct from <see cref="SearchSuppliersQuery"/>: this surface is gated to
/// <c>Admin</c> OR <c>SupplierAdmin</c> role holders, so visibility is the
/// full catalog (every <see cref="FundingPlatform.Domain.Enums.SupplierVerificationStatus"/>
/// row except <c>Rejected</c>) rather than the applicant-scoped slice.
///
/// <para>
/// Result cap: 25 rows. Performance target: P95 ≤ 300 ms at 200+ supplier seed
/// scale (FR-009 / NFR-006 / SC-007). Implementation uses an EF
/// <c>EF.Functions.Like</c> on the <c>Name</c> and <c>LegalId</c> columns; both
/// columns are indexed by the existing dacpac.
/// </para>
/// </summary>
public sealed record SearchSuppliersForAdminQuery(string Query);

/// <summary>
/// Spec 021 / T110 — flat row used on the admin autocomplete drop-down. Carries
/// the supplier's verification status so the UI can disambiguate Verified vs
/// PendingReview entries when both match the same term.
/// </summary>
public sealed record AdminSupplierSearchResultRow(
    int Id,
    string Name,
    string CedulaJuridica,
    string VerificationStatus);

/// <summary>
/// Spec 021 / T110 — handler seam (impl in
/// <c>FundingPlatform.Infrastructure</c>). Keeps the controller layer one EF
/// call thick and lets integration tests inject a stub.
/// </summary>
public interface ISearchSuppliersForAdminHandler
{
    Task<IReadOnlyList<AdminSupplierSearchResultRow>> HandleAsync(
        SearchSuppliersForAdminQuery query,
        CancellationToken ct = default);
}
