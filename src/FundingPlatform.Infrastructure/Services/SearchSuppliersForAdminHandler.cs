// Spec 021 — see specs/021-feedback-session-may13/tasks.md T110.

using FundingPlatform.Application.Suppliers.Queries;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / US3 / T110 / FR-009 — admin-side supplier autocomplete handler.
/// Returns up to 25 rows whose <c>Name</c> or <c>LegalId</c> matches the
/// supplied term (case-insensitive via <c>EF.Functions.Like</c>). Hides only
/// the <c>Rejected</c> verification status; everything else (Draft,
/// PendingReview, Verified) is visible to admins + supplier-admins.
/// </summary>
public sealed class SearchSuppliersForAdminHandler : ISearchSuppliersForAdminHandler
{
    private const int MaxResults = 25;

    private readonly AppDbContext _db;

    public SearchSuppliersForAdminHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AdminSupplierSearchResultRow>> HandleAsync(
        SearchSuppliersForAdminQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Query) || query.Query.Trim().Length < 2)
        {
            return Array.Empty<AdminSupplierSearchResultRow>();
        }

        var needle = query.Query.Trim();
        var pattern = $"%{needle}%";

        var results = await _db.Suppliers
            .Where(s => s.VerificationStatus != SupplierVerificationStatus.Rejected)
            .Where(s => EF.Functions.Like(s.Name, pattern)
                     || EF.Functions.Like(s.LegalId, pattern))
            .OrderBy(s => s.Name)
            .Take(MaxResults)
            .Select(s => new AdminSupplierSearchResultRow(
                s.Id,
                s.Name,
                s.LegalId,
                s.VerificationStatus.ToString()))
            .ToListAsync(ct);

        return results;
    }
}
