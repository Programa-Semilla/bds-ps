// Spec 021 — see specs/021-feedback-session-may13/tasks.md T092.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Applications.Queries;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.ValueObjects;
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
        // PublicCode is a value-object column (HasConversion); compare the VO
        // directly so EF applies the converter — never EF.Property&lt;string&gt;.
        PublicCode canonical;
        try { canonical = new PublicCode(publicCode); }
        catch (ArgumentException) { return null; }

        // Spec 021 / FR-021 / T152 — exclude soft-deleted Applications so the
        // /review page returns NotFound for a deleted draft.
        var application = await _queryFilter.ExcludeDeleted(_db.Applications)
            .Include(a => a.Items)
                .ThenInclude(i => i.Category)
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
                    .ThenInclude(q => q.Supplier)
            // Spec 035 / D2 / D1 — per-item impact + category field values.
            .Include(a => a.Items)
                .ThenInclude(i => i.ImpactTemplate)
            .Include(a => a.Items)
                .ThenInclude(i => i.ImpactParameterValues)
                    .ThenInclude(pv => pv.ImpactTemplateParameter)
            .Include(a => a.Items)
                .ThenInclude(i => i.CategoryFieldValues)
                    .ThenInclude(cfv => cfv.CategoryField)
            .FirstOrDefaultAsync(a => a.PublicCode == canonical, ct);

        if (application is null || application.ApplicantId != currentApplicantId)
        {
            return null;
        }

        var items = application.Items.Select(item => new ReviewItemRow(
            item.Id,
            item.ProductName,
            item.Category?.Name ?? string.Empty,
            item.Quotations.Select(q => new ReviewQuotationRow(
                q.Id,
                q.SupplierId,
                q.Supplier?.Name ?? string.Empty,
                q.Price,
                q.Currency,
                q.ConvertedCrcAmount)).ToList(),
            // Spec 035 / D2 — per-item impact summary.
            item.ImpactTemplate is null
                ? null
                : new ReviewImpactSummary(
                    item.ImpactTemplate.Id,
                    item.ImpactTemplate.Name,
                    item.ImpactParameterValues.Select(pv => new ReviewImpactParameter(
                        pv.ImpactTemplateParameter?.DisplayLabel
                            ?? pv.ImpactTemplateParameter?.Name
                            ?? string.Empty,
                        pv.Value ?? string.Empty)).ToList()),
            // Spec 035 / D1 — per-item category field values.
            item.CategoryFieldValues
                .OrderBy(cfv => cfv.CategoryField?.SortOrder ?? 0)
                .Select(cfv => new ReviewCategoryFieldRow(
                    cfv.CategoryField?.DisplayLabel ?? string.Empty,
                    cfv.Value))
                .ToList())).ToList();

        // The /review page renders before any reviewer selects a supplier, so
        // each item still holds its competing quotations. Reuse the single
        // source of truth for the rollup (ApplicationCurrencyTotal) in its
        // pre-selection "cheapest quote per item" mode — the previous inline
        // loop summed EVERY quote, N-counting each item and producing a total
        // that no eventual purchase could match (spec 021 / FR-022).
        var (totalCrc, hasNonCrcQuotation) =
            ApplicationCurrencyTotal.ComputeCheapestEstimate(application);

        var minQuotations = await ResolveMinimumQuotationsAsync(application.GroupId, ct);
        // Spec 035 / D2 — every item must have an impact template + the minimum
        // quotations. Required category fields are gated at submit by the domain.
        var canSubmit = application.Items.Count >= 1
                        && application.Items.All(i =>
                            i.ImpactTemplateId is not null
                            && i.Quotations.Count >= minQuotations);

        return new ApplicationReviewViewModel(
            application.Id,
            application.PublicCode?.Value ?? string.Empty,
            application.CompanyName,
            application.State,
            items,
            totalCrc,
            hasNonCrcQuotation,
            minQuotations,
            canSubmit);
    }

    private async Task<int> ResolveMinimumQuotationsAsync(int groupId, CancellationToken ct)
    {
        // Spec 029 / FR-017 — resolve the Plantilla deterministically through the
        // application's Group anchor (Group → Process → ProcessPlantilla),
        // replacing the prior nondeterministic membership FirstOrDefault.
        var snapshot = await (
            from g in _db.Groups
            where g.Id == groupId
            join pp in _db.ProcessPlantillas on g.ProcessId equals pp.ProcessId
            select pp).FirstOrDefaultAsync(ct);
        if (snapshot is not null)
        {
            return snapshot.MinimumQuotationsPerItem;
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
