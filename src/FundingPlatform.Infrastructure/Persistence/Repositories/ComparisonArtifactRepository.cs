using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>Spec 020 / data-model.md — EF-backed comparison-artifact persistence.</summary>
public class ComparisonArtifactRepository : IComparisonArtifactRepository
{
    private readonly AppDbContext _context;

    public ComparisonArtifactRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<ComparisonArtifact?> GetByItemIdAsync(int applicationItemId, CancellationToken ct)
        => _context.ComparisonArtifacts
            .FirstOrDefaultAsync(a => a.ApplicationItemId == applicationItemId, ct);

    public async Task UpsertAsync(ComparisonArtifact artifact, CancellationToken ct)
    {
        var existing = await _context.ComparisonArtifacts
            .FirstOrDefaultAsync(a => a.ApplicationItemId == artifact.ApplicationItemId, ct);

        if (existing is null)
        {
            _context.ComparisonArtifacts.Add(artifact);
        }
        else if (!ReferenceEquals(existing, artifact))
        {
            // The factory built a fresh instance; copy state into the tracked
            // row via ReplaceWith so EF's change tracker picks up the update.
            existing.ReplaceWith(
                artifact.JsonContent,
                artifact.InputHash,
                artifact.PromptVersion,
                artifact.SchemaVersion,
                artifact.AiModel,
                artifact.GeneratedByUserId,
                artifact.TokenCostInput,
                artifact.TokenCostOutput,
                artifact.LatencyMs,
                artifact.GeneratedAt);
        }

        await _context.SaveChangesAsync(ct);
    }
}
