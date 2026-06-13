using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly AppDbContext _context;
    // Spec 021 / FR-021 / T152 / R-10 — every list / aggregate read path in this
    // repository routes through ExcludeDeleted so soft-deleted rows never reach
    // a dashboard surface. By-Id lookups + write helpers stay unfiltered (admin
    // detail / write paths can legitimately touch a deleted row); see
    // DashboardQueriesHonorSoftDeleteTests for the structural audit.
    private readonly IApplicationQueryFilter _queryFilter;

    public ApplicationRepository(AppDbContext context, IApplicationQueryFilter queryFilter)
    {
        _context = context;
        _queryFilter = queryFilter;
    }

    /// <summary>
    /// Spec 021 / T152 — back-compat ctor for integration-test setups that
    /// construct the repository directly without going through DI. Equivalent
    /// to passing a fresh <see cref="ApplicationQueryFilter"/> (the same
    /// instance the DI registration would supply).
    /// </summary>
    public ApplicationRepository(AppDbContext context)
        : this(context, new ApplicationQueryFilter())
    {
    }

    // Spec 021 / FR-021 / T152 / R-10 — single-row by-Id lookup. Soft-delete
    // filter intentionally NOT applied: admin "undo-delete" + write paths must
    // still be able to load a soft-deleted aggregate; the dashboard-surface
    // guard is the listing-side filter applied throughout this file.
    public async Task<AppEntity?> GetByIdAsync(int id)
    {
        return await _context.Applications.FindAsync(id);
    }

    // Spec 020 — fast Item→Application lookup for AI comparison orchestrator.
    public async Task<int?> GetApplicationIdForItemAsync(int applicationItemId, CancellationToken ct)
    {
        return await _context.Items
            .AsNoTracking()
            .Where(i => i.Id == applicationItemId)
            .Select(i => (int?)i.ApplicationId)
            .FirstOrDefaultAsync(ct);
    }

    // Spec 021 / FR-021 / T152 / R-10 — single-row by-Id detail load. Same
    // rationale as GetByIdAsync — not a dashboard query; filter intentionally
    // skipped so command handlers can mutate soft-deleted rows.
    public async Task<AppEntity?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Applications
            .Include(a => a.Items)
                .ThenInclude(i => i.Category)
                    .ThenInclude(c => c.Fields)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Supplier)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.SupplierBranch)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Document)
            // Spec 035 / D2 — Impact relocated from Application to Item. Per-item
            // impact template + parameter values + category field values.
            .Include(a => a.Items)
                .ThenInclude(i => i.ImpactTemplate)
            .Include(a => a.Items)
                .ThenInclude(i => i.ImpactParameterValues)
                    .ThenInclude(pv => pv.ImpactTemplateParameter)
            .Include(a => a.Items)
                .ThenInclude(i => i.CategoryFieldValues)
                    .ThenInclude(cfv => cfv.CategoryField)
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
            // Spec 035 / D9 — per-line category fields + impact for the funding-agreement PDF.
            .Include(a => a.Items)
                .ThenInclude(i => i.CategoryFieldValues)
                    .ThenInclude(cfv => cfv.CategoryField)
            .Include(a => a.Items)
                .ThenInclude(i => i.ImpactTemplate)
            .Include(a => a.Items)
                .ThenInclude(i => i.ImpactParameterValues)
                    .ThenInclude(pv => pv.ImpactTemplateParameter)
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
        // Spec 021 / FR-021 / T152 — applicant dashboard list source for
        // Application/Index. Soft-deleted rows MUST NOT surface (SC-011).
        // Spec 029 / FR-020 — archived-Fund applications are also hidden.
        var source = _queryFilter.ExcludeArchivedFund(
            _queryFilter.ExcludeDeleted(_context.Applications));
        return await source
            .Include(a => a.Items)
            .Where(a => a.ApplicantId == applicantId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();
    }

    public async Task<List<AppEntity>> GetForApplicantDashboardAsync(int applicantId)
    {
        // Spec 021 / FR-021 / T152 / T153 — applicant home dashboard
        // (ApplicantDashboardProjection) source. Drives the Solicitudes activas
        // counter + the "borrador listo para enviar" awaiting-action prompt
        // (FR-021 / SC-011 — the meeting-PDF defect path).
        // Spec 029 / FR-020 — archived-Fund applications are also hidden.
        var source = _queryFilter.ExcludeArchivedFund(
            _queryFilter.ExcludeDeleted(_context.Applications));
        return await source
            .Include(a => a.Items)
            .Include(a => a.VersionHistory)
            .Include(a => a.Appeals)
            .Include(a => a.FundingAgreement)
            // Applicant.UserId lets the projection resolve the timeline actor
            // ("usted" vs others) instead of leaking the raw Identity GUID.
            .Include(a => a.Applicant)
            .Where(a => a.ApplicantId == applicantId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();
    }

    public async Task<(List<AppEntity> Items, int TotalCount)> GetByStatePagedAsync(
        Domain.Enums.ApplicationState state, int page, int pageSize)
    {
        // Spec 021 / FR-021 / T152 — admin paged listing (state-keyed).
        var query = _queryFilter.ExcludeDeleted(_context.Applications)
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
        // Spec 021 / FR-021 / T152 — reviewer queue source. Soft-deleted rows
        // must never appear on the reviewer's worklist (SC-011).
        // Spec 029 / FR-020 — archived-Fund applications drop off the reviewer queue.
        IQueryable<AppEntity> query = _queryFilter.ExcludeArchivedFund(
                _queryFilter.ExcludeDeleted(_context.Applications))
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
            // Spec 032 — also match the applicant's User Code and email (FR-013),
            // in addition to the existing name + identification matching.
            query = query.Where(a =>
                a.Applicant != null
                && (EF.Functions.Like(a.Applicant.FirstName, likeTerm)
                 || EF.Functions.Like(a.Applicant.LastName, likeTerm)
                 || EF.Functions.Like(a.Applicant.LegalId, likeTerm)
                 || EF.Functions.Like(a.Applicant.UserCode, likeTerm)
                 || EF.Functions.Like(a.Applicant.Email, likeTerm)));
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
        // Spec 021 / FR-021 / T152 — reviewer detail-page authorization mirrors
        // the listing predicate; a soft-deleted Application is not "shared" with
        // any reviewer because it is no longer reachable via the queue.
        // Spec 029 / FR-020 — an archived-Fund application is not "shared" with
        // any reviewer (it has left the queue).
        var apps = _queryFilter.ExcludeArchivedFund(
            _queryFilter.ExcludeDeleted(_context.Applications.AsNoTracking()));
        return await (
            from a in apps
            where a.Id == applicationId
            from m in _context.UserGroupMemberships
            where m.UserId == a.Applicant!.UserId && groupIds.Contains(m.GroupId)
            select m).AnyAsync(ct);
    }

    public async Task<(List<AppEntity> Items, int TotalCount)> GetPendingAgreementPagedAsync(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        // Spec 021 / FR-021 / T152 / T153 — Generate Agreement queue is a
        // reviewer dashboard surface (Review/GenerateAgreement). Soft-deleted
        // rows must never reach it.
        // Spec 029 / FR-020 — archived-Fund applications drop off the signing inbox.
        var query = _queryFilter.ExcludeArchivedFund(
                _queryFilter.ExcludeDeleted(_context.Applications.AsNoTracking()))
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
