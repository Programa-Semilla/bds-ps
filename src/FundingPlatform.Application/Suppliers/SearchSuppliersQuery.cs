// Spec 021 — see specs/021-feedback-session-may13/tasks.md T093
// and contracts/applicant-routes.md (GET /api/applications/suppliers/search).

namespace FundingPlatform.Application.Suppliers;

/// <summary>
/// Spec 021 / T093 / FR-009 — applicant-side supplier autocomplete query.
/// Returns up to 25 suppliers whose <c>Name</c> or <c>LegalId</c> contains
/// the supplied term (case-insensitive). Visibility mirrors
/// <c>SupplierCatalogService.SearchByLegalIdAsync</c>: Verified → visible to
/// all; Draft / PendingReview → visible only to the creator; Rejected hidden.
/// </summary>
public sealed record SearchSuppliersQuery(string Query, int CurrentApplicantId);

public sealed record SupplierSearchResultRow(int Id, string Name, string CedulaJuridica);

public interface ISearchSuppliersHandler
{
    Task<IReadOnlyList<SupplierSearchResultRow>> HandleAsync(
        SearchSuppliersQuery query, CancellationToken ct = default);
}
