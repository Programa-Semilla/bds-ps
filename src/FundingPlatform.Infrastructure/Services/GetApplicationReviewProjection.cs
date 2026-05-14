// Spec 021 — see specs/021-feedback-session-may13/tasks.md T092.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Applications.Queries;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / T092 / FR-017 / FR-022 — EF-backed
/// <see cref="IGetApplicationReviewProjection"/>. Renders the items list,
/// per-item quotation summaries, totals in CRC + (optional) FX disclaimer
/// trigger, and the Application's Impact summary.
/// </summary>
public sealed class GetApplicationReviewProjection : IGetApplicationReviewProjection
{
    private readonly AppDbContext _db;
    // Spec 021 / FR-021 / T152 — applicant /review page is a dashboard surface;
    // a soft-deleted Application must surface as "not found", not as an
    // editable review page.
    private readonly IApplicationQueryFilter _queryFilter;

    public GetApplicationReviewProjection(AppDbContext db, IApplicationQueryFilter queryFilter)
    {
        _db = db;
        _queryFilter = queryFilter;
    }

    public async Task<ApplicationReviewViewModel?> ExecuteAsync(
        string publicCode, int currentApplicantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicCode))
        {
            return null;
        }
        var canonical = publicCode.Trim().ToUpperInvariant();

        // Spec 021 / FR-021 / T152 — exclude soft-deleted Applications so the
        // /review page returns NotFound for a deleted draft.
        var application = await _queryFilter.ExcludeDeleted(_db.Applications)
            .Include(a => a.Items)
                .ThenInclude(i => i.Category)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Supplier)
            .Include(a => a.ImpactTemplate)
                .ThenInclude(t => t!.Parameters)
            .Include(a => a.ImpactParameterValues)
                .ThenInclude(pv => pv.ImpactTemplateParameter)
            .FirstOrDefaultAsync(a => EF.Property<string>(a, "PublicCode") == canonical, ct);

        if (application is null || application.ApplicantId != currentApplicantId)
        {
            return null;
        }

        var items = application.Items.Select(item => new ReviewItemRow(
            item.Id,
            item.ProductName,
            item.Category?.Name ?? string.Empty,
            item.TechnicalSpecifications,
            item.Quotations.Select(q => new ReviewQuotationRow(
                q.Id,
                q.SupplierId,
                q.Supplier?.Name ?? string.Empty,
                q.Price,
                q.Currency,
                q.ConvertedCrcAmount)).ToList())).ToList();

        decimal? totalCrc = null;
        var hasNonCrcQuotation = false;
        foreach (var item in application.Items)
        {
            foreach (var q in item.Quotations)
            {
                if (!string.Equals(q.Currency, "CRC", StringComparison.OrdinalIgnoreCase))
                {
                    hasNonCrcQuotation = true;
                }
                if (q.ConvertedCrcAmount is { } amt)
                {
                    totalCrc = (totalCrc ?? 0m) + amt;
                }
            }
        }

        var impactSummary = application.ImpactTemplate is null
            ? null
            : new ReviewImpactSummary(
                application.ImpactTemplate.Id,
                application.ImpactTemplate.Name,
                application.ImpactParameterValues.Select(pv => new ReviewImpactParameter(
                    pv.ImpactTemplateParameter?.DisplayLabel
                        ?? pv.ImpactTemplateParameter?.Name
                        ?? string.Empty,
                    pv.Value ?? string.Empty)).ToList());

        var minQuotations = await ResolveMinimumQuotationsAsync(application.ApplicantId, ct);
        var canSubmit = application.ImpactTemplateId is not null
                        && application.Items.Count >= 1
                        && application.Items.All(i => i.Quotations.Count >= minQuotations);

        return new ApplicationReviewViewModel(
            application.Id,
            application.PublicCode?.Value ?? string.Empty,
            application.CompanyName,
            application.State,
            items,
            totalCrc,
            hasNonCrcQuotation,
            impactSummary,
            minQuotations,
            canSubmit);
    }

    private async Task<int> ResolveMinimumQuotationsAsync(int applicantId, CancellationToken ct)
    {
        var userId = await _db.Applicants
            .Where(a => a.Id == applicantId)
            .Select(a => a.UserId)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(userId))
        {
            var snapshot = await (
                from m in _db.UserGroupMemberships
                where m.UserId == userId
                join g in _db.Groups on m.GroupId equals g.Id
                join pp in _db.ProcessPlantillas on g.ProcessId equals pp.ProcessId
                select pp).FirstOrDefaultAsync(ct);
            if (snapshot is not null)
            {
                return snapshot.MinimumQuotationsPerItem;
            }
        }

        var config = await _db.SystemConfigurations
            .FirstOrDefaultAsync(c => c.Key == "MinQuotationsPerItem", ct);
        if (config is not null && int.TryParse(config.Value, out var parsed) && parsed > 0)
        {
            return parsed;
        }
        return 2;
    }
}
