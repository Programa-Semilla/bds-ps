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
