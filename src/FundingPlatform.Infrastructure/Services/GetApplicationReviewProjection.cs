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
            // Spec 035 (evolved 2026-06-16, D13/D14) — app-level declared impacts +
            // per-item attribution + category field values.
            .Include(a => a.Impacts)
                .ThenInclude(ai => ai.ImpactTemplate)
            .Include(a => a.Impacts)
                .ThenInclude(ai => ai.ParameterValues)
                    .ThenInclude(pv => pv.ImpactTemplateParameter)
            .Include(a => a.Items)
                .ThenInclude(i => i.ItemImpacts)
                    .ThenInclude(ii => ii.ApplicationImpact)
                        .ThenInclude(ai => ai.ImpactTemplate)
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
            // Spec 035 (evolved 2026-06-16, D14) — attributed impact names + justification.
            item.ItemImpacts
                .Select(ii => ii.ApplicationImpact?.ImpactTemplate?.Name ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList(),
            item.ImpactJustification,
            // Spec 035 / D1 — per-item category field values.
            item.CategoryFieldValues
                .OrderBy(cfv => cfv.CategoryField?.SortOrder ?? 0)
                .Select(cfv => new ReviewCategoryFieldRow(
                    cfv.CategoryField?.DisplayLabel ?? string.Empty,
                    cfv.Value))
                .ToList())).ToList();

        // Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts (app level).
        var impacts = application.Impacts.Select(ai => new ReviewImpactSummary(
            ai.ImpactTemplate?.Id ?? 0,
            ai.ImpactTemplate?.Name ?? string.Empty,
            ai.ParameterValues.Select(pv => new ReviewImpactParameter(
                pv.ImpactTemplateParameter?.DisplayLabel
                    ?? pv.ImpactTemplateParameter?.Name
                    ?? string.Empty,
                pv.Value ?? string.Empty)).ToList())).ToList();

        // The /review page renders before any reviewer selects a supplier, so
        // each item still holds its competing quotations. Reuse the single
        // source of truth for the rollup (ApplicationCurrencyTotal) in its
        // pre-selection "cheapest quote per item" mode — the previous inline
        // loop summed EVERY quote, N-counting each item and producing a total
        // that no eventual purchase could match (spec 021 / FR-022).
        var (totalCrc, hasNonCrcQuotation) =
            ApplicationCurrencyTotal.ComputeCheapestEstimate(application);

        var minQuotations = await ResolveMinimumQuotationsAsync(application.GroupId, ct);
        // Spec 035 (evolved 2026-06-16, D16) — submit requires ≥1 declared impact and
        // every item attributed + min-quotations. The per-item impact justification was
        // relaxed to OPTIONAL (2026-06-18) and no longer gates submission. Required
        // category fields + required impact values are gated at submit by the domain/service.
        var canSubmit = application.Items.Count >= 1
                        && application.Impacts.Count >= 1
                        && application.Items.All(i =>
                            i.ItemImpacts.Count >= 1
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
            canSubmit,
            impacts);
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
