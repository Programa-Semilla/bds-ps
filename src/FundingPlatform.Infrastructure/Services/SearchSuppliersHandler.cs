// Spec 021 — see specs/021-feedback-session-may13/tasks.md T093.

using FundingPlatform.Application.Suppliers;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / T093 / FR-009 — applicant-side supplier autocomplete handler.
/// Visibility mirrors <c>SupplierCatalogService.SearchByLegalIdAsync</c>:
/// Verified → all; Draft / PendingReview → creator only; Rejected hidden.
/// </summary>
public sealed class SearchSuppliersHandler : ISearchSuppliersHandler
{
    private const int MaxResults = 25;

    private readonly AppDbContext _db;

    public SearchSuppliersHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SupplierSearchResultRow>> HandleAsync(
        SearchSuppliersQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Query) || query.Query.Trim().Length < 2)
        {
            return Array.Empty<SupplierSearchResultRow>();
        }
        var lowered = query.Query.Trim().ToLowerInvariant();

        var results = await _db.Suppliers
            .Where(s => s.VerificationStatus == SupplierVerificationStatus.Verified
                     || ((s.VerificationStatus == SupplierVerificationStatus.Draft
                          || s.VerificationStatus == SupplierVerificationStatus.PendingReview)
                         && s.CreatedByApplicantId == query.CurrentApplicantId))
            .Where(s => s.Name.ToLower().Contains(lowered)
                     || s.LegalId.ToLower().Contains(lowered))
            .OrderBy(s => s.Name)
            .Take(MaxResults)
            .Select(s => new SupplierSearchResultRow(s.Id, s.Name, s.LegalId))
            .ToListAsync(ct);

        return results;
    }
}
