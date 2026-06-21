using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Spec 040 / D4 — resolves the active checklist template for a workflow stage. The
/// gate-relevant rule: an active template whose <c>AppliesToStage</c> is the requested
/// stage wins over an active <c>Both</c> template; otherwise the active <c>Both</c>
/// template applies. Admin CRUD lives in <c>ChecklistTemplateService</c>.
/// </summary>
public class ChecklistTemplateRepository : IChecklistTemplateRepository
{
    private readonly AppDbContext _context;

    public ChecklistTemplateRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ChecklistTemplate?> GetActiveForStageAsync(ChecklistStage stage, CancellationToken ct)
    {
        var candidates = await _context.ChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Items.OrderBy(i => i.DisplayOrder))
            .Where(t => t.IsActive
                && (t.AppliesToStage == stage || t.AppliesToStage == ChecklistStage.Both))
            .ToListAsync(ct);

        // Stage-specific active template beats the Both fallback.
        return candidates.FirstOrDefault(t => t.AppliesToStage == stage)
            ?? candidates.FirstOrDefault(t => t.AppliesToStage == ChecklistStage.Both);
    }
}
