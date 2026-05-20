using FundingPlatform.Application.Abstractions.Comparison;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Comparison;

/// <summary>
/// Spec 023 / FR-009 — default <see cref="IComparisonCacheInvalidator"/>.
/// Deletes the cached <c>ComparisonArtifact</c> row for the Item (primary key
/// is <c>ApplicationItemId</c> so there is at most one row per item — see
/// <c>ComparisonArtifactConfiguration</c>). Idempotent: a missing row is a no-op.
/// </summary>
public class ComparisonCacheInvalidator : IComparisonCacheInvalidator
{
    private readonly AppDbContext _db;

    public ComparisonCacheInvalidator(AppDbContext db)
    {
        _db = db;
    }

    public async Task InvalidateForItemAsync(int itemId, CancellationToken ct = default)
    {
        var existing = await _db.ComparisonArtifacts
            .FirstOrDefaultAsync(a => a.ApplicationItemId == itemId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return;
        }

        _db.ComparisonArtifacts.Remove(existing);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
