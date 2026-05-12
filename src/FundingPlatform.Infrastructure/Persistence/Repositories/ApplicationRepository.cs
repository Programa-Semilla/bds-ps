using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly AppDbContext _context;

    public ApplicationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AppEntity?> GetByIdAsync(int id)
    {
        return await _context.Applications.FindAsync(id);
    }

    public async Task<int?> GetApplicationIdForItemAsync(int applicationItemId, CancellationToken ct)
    {
        return await _context.Items
            .AsNoTracking()
            .Where(i => i.Id == applicationItemId)
            .Select(i => (int?)i.ApplicationId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AppEntity?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Applications
            .Include(a => a.Items)
                .ThenInclude(i => i.Category)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Supplier)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.SupplierBranch)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Document)
            .Include(a => a.Items)
                .ThenInclude(i => i.Impact)
                    .ThenInclude(imp => imp!.ImpactTemplate)
            .Include(a => a.Items)
                .ThenInclude(i => i.Impact)
                    .ThenInclude(imp => imp!.ParameterValues)
                        .ThenInclude(pv => pv.ImpactTemplateParameter)
            .Include(a => a.Applicant)
            .Include(a => a.ApplicantResponses)
                .ThenInclude(r => r.ItemResponses)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<AppEntity?> GetByIdWithResponseAndAppealsAsync(int id)
    {
        return await _context.Applications
            .Include(a => a.Items)
                .ThenInclude(i => i.SelectedSupplier)
            .Include(a => a.Items)
                .ThenInclude(i => i.Category)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Supplier)
            .Include(a => a.Applicant)
            .Include(a => a.ApplicantResponses)
                .ThenInclude(r => r.ItemResponses)
            .Include(a => a.Appeals)
                .ThenInclude(ap => ap.Messages)
            .Include(a => a.FundingAgreement)
                .ThenInclude(fa => fa!.SignedUploads)
                    .ThenInclude(u => u.ReviewDecision)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<AppEntity>> GetByApplicantIdAsync(int applicantId)
    {
        return await _context.Applications
            .Include(a => a.Items)
            .Where(a => a.ApplicantId == applicantId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();
    }

    public async Task<List<AppEntity>> GetForApplicantDashboardAsync(int applicantId)
    {
        return await _context.Applications
            .Include(a => a.Items)
            .Include(a => a.VersionHistory)
            .Include(a => a.Appeals)
            .Include(a => a.FundingAgreement)
            .Where(a => a.ApplicantId == applicantId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();
    }

    public async Task<(List<AppEntity> Items, int TotalCount)> GetByStatePagedAsync(
        Domain.Enums.ApplicationState state, int page, int pageSize)
    {
        var query = _context.Applications
            .Include(a => a.Applicant)
            .Include(a => a.Items)
            .Where(a => a.State == state)
            .OrderBy(a => a.SubmittedAt);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary>
    /// Spec 016 / NFR-001 — composes the group-overlap predicate at the EF
    /// query level. Admin scope short-circuits and returns the unscoped result.
    /// FR-014 — the optional <paramref name="searchTerm"/> narrows by
    /// applicant first/last name or legal id (case-insensitive contains).
    /// </summary>
    public async Task<(List<AppEntity> Items, int TotalCount)> GetByStateForReviewerAsync(
        Domain.Enums.ApplicationState state,
        Domain.Interfaces.ReviewerScopeHint scope,
        int page,
        int pageSize,
        string? searchTerm = null)
    {
        IQueryable<AppEntity> query = _context.Applications
            .Include(a => a.Applicant)
            .Include(a => a.Items)
            .Where(a => a.State == state);

        if (!scope.IsAdmin)
        {
            // Non-admin reviewers see only applications whose applicant's
            // ApplicationUser shares at least one group (FR-011).
            var groupIds = scope.GroupIds.ToList();
            if (groupIds.Count == 0)
            {
                // Reviewer has no memberships — empty queue (FR-005).
                return (new List<AppEntity>(), 0);
            }
            query = from a in query
                    where _context.UserGroupMemberships.Any(m =>
                        m.UserId == a.Applicant!.UserId
                        && groupIds.Contains(m.GroupId))
                    select a;
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            var likeTerm = $"%{term}%";
            query = query.Where(a =>
                a.Applicant != null
                && (EF.Functions.Like(a.Applicant.FirstName, likeTerm)
                 || EF.Functions.Like(a.Applicant.LastName, likeTerm)
                 || EF.Functions.Like(a.Applicant.LegalId, likeTerm)));
        }

        query = query.OrderBy(a => a.SubmittedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<bool> ApplicantSharesAnyGroupAsync(
        int applicationId,
        IReadOnlyCollection<int> reviewerGroupIds,
        CancellationToken ct)
    {
        if (reviewerGroupIds.Count == 0) return false;
        var groupIds = reviewerGroupIds.ToList();
        return await (
            from a in _context.Applications.AsNoTracking()
            where a.Id == applicationId
            from m in _context.UserGroupMemberships
            where m.UserId == a.Applicant!.UserId && groupIds.Contains(m.GroupId)
            select m).AnyAsync(ct);
    }

    public async Task<(List<AppEntity> Items, int TotalCount)> GetPendingAgreementPagedAsync(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        var query = _context.Applications
            .AsNoTracking()
            .Include(a => a.Applicant)
            .Include(a => a.ApplicantResponses)
            .Where(a => a.State == Domain.Enums.ApplicationState.ResponseFinalized
                     && a.FundingAgreement == null)
            .OrderBy(a => a.ApplicantResponses.Max(r => r.SubmittedAt));

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task AddAsync(AppEntity application)
    {
        await _context.Applications.AddAsync(application);
    }

    public Task UpdateAsync(AppEntity application)
    {
        _context.Applications.Update(application);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
