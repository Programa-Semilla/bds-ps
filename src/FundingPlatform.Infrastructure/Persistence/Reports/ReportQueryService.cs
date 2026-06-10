using System.Runtime.CompilerServices;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Reports.DTOs;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

// Disambiguate the Application entity from the FundingPlatform.Application namespace
// (the latter is pulled in by Application.Admin.Reports.DTOs/Interfaces above).
using ApplicationEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Infrastructure.Persistence.Reports;

public sealed class ReportQueryService : IReportQueryService
{
    private readonly AppDbContext _db;
    // Spec 021 / FR-021 / T152 / R-10 — every Applications read in this service
    // is an admin Reports / dashboard surface. Source the table through this
    // helper so soft-deleted rows never inflate counters or appear on a list.
    private readonly IApplicationQueryFilter _queryFilter;

    public ReportQueryService(AppDbContext db, IApplicationQueryFilter queryFilter)
    {
        _db = db;
        _queryFilter = queryFilter;
    }

    /// <summary>
    /// Spec 021 / FR-021 / T152 — base Applications source for every admin
    /// Reports read. Equivalent to <c>_db.Applications.AsNoTracking()</c> with
    /// the soft-delete predicate applied.
    /// </summary>
    private IQueryable<ApplicationEntity> ActiveApplications() =>
        _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking());

    public Task<int> CountApplicationsAsync(ListApplicationsRequest req, CancellationToken ct = default)
        => BuildApplicationsBaseQuery(req).CountAsync(ct);

    public async Task<IReadOnlyList<ApplicationProjection>> ListApplicationsPageAsync(
        ListApplicationsRequest req, int page, int pageSize, CancellationToken ct = default)
    {
        var q = ApplyApplicationsSort(BuildApplicationsBaseQuery(req), req.Sort);
        return await ProjectApplications(q.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
    }

    public async IAsyncEnumerable<ApplicationProjection> StreamApplicationsAsync(
        ListApplicationsRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var q = ApplyApplicationsSort(BuildApplicationsBaseQuery(req), req.Sort);
        await foreach (var row in ProjectApplications(q).AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return row;
        }
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<CurrencyAmount>>>
        ApplicationsApprovedTotalsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default)
    {
        if (appIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<CurrencyAmount>>();
        }

        var rows = await (
            from i in _db.Items.AsNoTracking()
            where appIds.Contains(i.ApplicationId)
                  && i.ReviewStatus == ItemReviewStatus.Approved
                  && i.SelectedSupplierId != null
            join q in _db.Quotations.AsNoTracking()
                on new { ItemId = i.Id, SupplierId = i.SelectedSupplierId!.Value }
                equals new { ItemId = q.ItemId, SupplierId = q.SupplierId }
            select new { i.ApplicationId, q.Currency, q.Price, q.ConvertedCrcAmount }
        ).ToListAsync(ct);

        return rows
            .GroupBy(r => r.ApplicationId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CurrencyAmount>)g
                    .GroupBy(r => r.Currency)
                    .Select(cg => new CurrencyAmount(
                        cg.Key,
                        cg.Sum(x => x.Price),
                        // Spec 015 / T416 — sum the converted-CRC values per (app, currency) so
                        // CSV/dashboards can display a CRC-comparable total alongside the
                        // original-currency stack. Null ConvertedCrcAmount (legacy rows) → 0.
                        cg.Sum(x => x.ConvertedCrcAmount ?? 0m)))
                    .OrderBy(c => c.Currency)
                    .ToList());
    }

    public Task<int> CountApplicantsAsync(ListApplicantsRequest req, CancellationToken ct = default)
        => BuildApplicantsBaseQuery(req).CountAsync(ct);

    public async Task<IReadOnlyList<ApplicantProjection>> ListApplicantsPageAsync(
        ListApplicantsRequest req, int page, int pageSize, CancellationToken ct = default)
    {
        var q = ApplyApplicantsSort(BuildApplicantsBaseQuery(req), req.Sort);
        return await ProjectApplicants(q.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<CurrencyAmount>>>
        ApplicantsApprovedTotalsAsync(IReadOnlyCollection<int> applicantIds, CancellationToken ct = default)
    {
        if (applicantIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<CurrencyAmount>>();
        }

        var rows = await (
            from i in _db.Items.AsNoTracking()
            where i.ReviewStatus == ItemReviewStatus.Approved && i.SelectedSupplierId != null
            join a in ActiveApplications() on i.ApplicationId equals a.Id
            where applicantIds.Contains(a.ApplicantId)
            join q in _db.Quotations.AsNoTracking()
                on new { ItemId = i.Id, SupplierId = i.SelectedSupplierId!.Value }
                equals new { ItemId = q.ItemId, SupplierId = q.SupplierId }
            select new { a.ApplicantId, q.Currency, q.Price, q.ConvertedCrcAmount }
        ).ToListAsync(ct);

        return rows
            .GroupBy(r => r.ApplicantId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CurrencyAmount>)g
                    .GroupBy(r => r.Currency)
                    .Select(cg => new CurrencyAmount(
                        cg.Key,
                        cg.Sum(x => x.Price),
                        cg.Sum(x => x.ConvertedCrcAmount ?? 0m)))
                    .OrderBy(c => c.Currency)
                    .ToList());
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<CurrencyAmount>>>
        ApplicantsExecutedTotalsAsync(IReadOnlyCollection<int> applicantIds, CancellationToken ct = default)
    {
        if (applicantIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<CurrencyAmount>>();
        }

        var rows = await (
            from i in _db.Items.AsNoTracking()
            where i.ReviewStatus == ItemReviewStatus.Approved && i.SelectedSupplierId != null
            join a in ActiveApplications() on i.ApplicationId equals a.Id
            where applicantIds.Contains(a.ApplicantId)
                  && a.State == ApplicationState.AgreementExecuted
            join q in _db.Quotations.AsNoTracking()
                on new { ItemId = i.Id, SupplierId = i.SelectedSupplierId!.Value }
                equals new { ItemId = q.ItemId, SupplierId = q.SupplierId }
            select new { a.ApplicantId, q.Currency, q.Price, q.ConvertedCrcAmount }
        ).ToListAsync(ct);

        return rows
            .GroupBy(r => r.ApplicantId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CurrencyAmount>)g
                    .GroupBy(r => r.Currency)
                    .Select(cg => new CurrencyAmount(
                        cg.Key,
                        cg.Sum(x => x.Price),
                        cg.Sum(x => x.ConvertedCrcAmount ?? 0m)))
                    .OrderBy(c => c.Currency)
                    .ToList());
    }

    public Task<int> CountFundedItemsAsync(ListFundedItemsRequest req, CancellationToken ct = default)
        => BuildFilteredFundedItemRows(req).CountAsync(ct);

    public async Task<IReadOnlyList<FundedItemRowDto>> ListFundedItemsPageAsync(
        ListFundedItemsRequest req, int page, int pageSize, CancellationToken ct = default)
    {
        // The pipeline must stay inline (anonymous-typed): EF Core treats anonymous-type
        // join carriers as transparent identifiers and inlines property access into SQL,
        // but treats named records as opaque constructor calls — `OrderBy(x => x.A.Id)`
        // on a named-record carrier blows up with "could not be translated". Keep the
        // base+sort+page+project chain in this single method.
        var rows = BuildFilteredFundedItemRows(req);

        rows = req.Sort switch
        {
            "approvedAt-asc"  => rows.OrderBy(x => x.A.UpdatedAt).ThenBy(x => x.A.Id),
            "price-desc"      => rows.OrderByDescending(x => x.Q.Price).ThenBy(x => x.A.Id),
            "price-asc"       => rows.OrderBy(x => x.Q.Price).ThenBy(x => x.A.Id),
            "applicant-asc"   => rows.OrderBy(x => x.A.Applicant.FirstName).ThenBy(x => x.A.Applicant.LastName).ThenBy(x => x.A.Id),
            "applicant-desc"  => rows.OrderByDescending(x => x.A.Applicant.FirstName).ThenByDescending(x => x.A.Applicant.LastName).ThenByDescending(x => x.A.Id),
            "supplier-asc"    => rows.OrderBy(x => x.S.Name).ThenBy(x => x.A.Id),
            "supplier-desc"   => rows.OrderByDescending(x => x.S.Name).ThenByDescending(x => x.A.Id),
            _                 => rows.OrderByDescending(x => x.A.UpdatedAt).ThenByDescending(x => x.A.Id),
        };

        return await rows
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FundedItemRowDto(
                x.A.Id,
                (x.A.Applicant.FirstName + " " + x.A.Applicant.LastName).Trim(),
                x.I.ProductName,
                x.C.Name,
                x.S.Name,
                x.S.LegalId,
                x.Q.Price,
                x.Q.Currency,
                x.A.State,
                x.A.SubmittedAt,
                (DateTime?)x.A.UpdatedAt, // approximation — research §5 fallback uses application timestamp
                x.A.FundingAgreement != null,
                x.A.State == ApplicationState.AgreementExecuted,
                x.A.Group!.Process!.Fund!.Name, // Spec 029 / FR-012
                x.Q.ConvertedCrcAmount))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns the filtered Items×Application×Quotation×Supplier×Category join, carrier
    /// is an anonymous type so EF inlines property access through transparent identifiers
    /// when the caller adds OrderBy/Skip/Take/Select. Default app states per FR-026:
    /// ResponseFinalized + AgreementExecuted.
    /// </summary>
    private IQueryable<FundedItemsCarrier> BuildFilteredFundedItemRows(ListFundedItemsRequest req)
    {
        var allowed = (req.AppStates is { Count: > 0 } provided)
            ? provided.Where(s =>
                s == ApplicationState.ResponseFinalized
                || s == ApplicationState.AgreementExecuted).ToArray()
            : new[] { ApplicationState.ResponseFinalized, ApplicationState.AgreementExecuted };
        var allowedSet = allowed.ToHashSet();

        var executedOnly = req.ExecutedOnly;

        var items = _db.Items.AsNoTracking()
            .Where(i => i.ReviewStatus == ItemReviewStatus.Approved && i.SelectedSupplierId != null);

        var rows =
            from i in items
            join a in ActiveApplications() on i.ApplicationId equals a.Id
            join q in _db.Quotations.AsNoTracking()
                on new { ItemId = i.Id, SupplierId = i.SelectedSupplierId!.Value }
                equals new { ItemId = q.ItemId, SupplierId = q.SupplierId }
            join s in _db.Suppliers.AsNoTracking() on q.SupplierId equals s.Id
            join c in _db.Categories.AsNoTracking() on i.CategoryId equals c.Id
            where allowedSet.Contains(a.State)
            select new FundedItemsCarrier { I = i, A = a, Q = q, S = s, C = c };

        if (executedOnly)
        {
            rows = rows.Where(x => x.A.State == ApplicationState.AgreementExecuted);
        }

        if (req.CategoryIds is { Count: > 0 } catIds)
        {
            rows = rows.Where(x => catIds.Contains(x.C.Id));
        }

        if (req.SupplierIds is { Count: > 0 } supIds)
        {
            rows = rows.Where(x => supIds.Contains(x.S.Id));
        }

        if (req.ApprovedFrom.HasValue)
        {
            var fromUtc = req.ApprovedFrom.Value.ToDateTime(TimeOnly.MinValue);
            rows = rows.Where(x => x.A.UpdatedAt >= fromUtc);
        }
        if (req.ApprovedTo.HasValue)
        {
            var toUtc = req.ApprovedTo.Value.ToDateTime(TimeOnly.MaxValue);
            rows = rows.Where(x => x.A.UpdatedAt <= toUtc);
        }

        // Spec 029 / FR-012 — exact Fund filter via the anchor.
        if (req.FundId is { } fundId)
        {
            rows = rows.Where(x => x.A.Group!.Process!.FundId == fundId);
        }

        return rows;
    }

    public Task<int> CountAgingApplicationsAsync(ListAgingApplicationsRequest req, CancellationToken ct = default)
        => BuildAgingApplicationsBaseQuery(req).CountAsync(ct);

    public async Task<IReadOnlyList<AgingApplicationRowDto>> ListAgingApplicationsPageAsync(
        ListAgingApplicationsRequest req, int page, int pageSize, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var q = ApplyAgingSort(BuildAgingApplicationsBaseQuery(req), req.Sort);
        var rows = await ProjectAging(q.Skip((page - 1) * pageSize).Take(pageSize), nowUtc).ToListAsync(ct);

        // Per-(application, currency) Approved totals for the visible page only.
        var ids = rows.Select(r => r.AppId).ToList();
        var totals = await ApplicationsApprovedTotalsAsync(ids, ct);

        return rows.Select(r => r with
        {
            TotalApproved = totals.TryGetValue(r.AppId, out var s) ? s : Array.Empty<CurrencyAmount>()
        }).ToList();
    }

    /// <summary>
    /// Builds the base aging-applications query (entities only, sortable). LastTransitionAt
    /// is approximated as Application.UpdatedAt (research.md §5 fallback); LastActor is
    /// left as null pending VersionHistory join wiring in a follow-up. Sort and projection
    /// are applied separately in <see cref="ApplyAgingSort"/> and <see cref="ProjectAging"/>
    /// so that EF can translate ORDER BY against entity columns rather than against a
    /// projected expression that contains correlated subqueries.
    /// </summary>
    private IQueryable<ApplicationEntity> BuildAgingApplicationsBaseQuery(ListAgingApplicationsRequest req)
    {
        var defaultStates = new[]
        {
            ApplicationState.Submitted,
            ApplicationState.UnderReview,
            ApplicationState.Resolved,
            ApplicationState.ResponseFinalized,
        };
        var states = (req.States is { Count: > 0 } provided ? provided.ToArray() : defaultStates).ToHashSet();

        var threshold = Math.Clamp(req.Threshold, 1, 365);
        var nowUtc = DateTime.UtcNow;
        var thresholdCutoff = nowUtc.AddDays(-threshold);

        var q = ActiveApplications()
            .Where(a => states.Contains(a.State))
            .Where(a => a.UpdatedAt <= thresholdCutoff);

        // Spec 029 / FR-012 — exact Fund filter via the anchor.
        if (req.FundId is { } fundId)
        {
            q = q.Where(a => a.Group!.Process!.FundId == fundId);
        }

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var pattern = $"%{req.Search.Trim()}%";
            q = q.Where(a =>
                EF.Functions.Like(a.Applicant.FirstName + " " + a.Applicant.LastName, pattern)
                || EF.Functions.Like(a.Applicant.LegalId, pattern)
                || EF.Functions.Like(a.Applicant.Email, pattern));
        }

        return q;
    }

    private static IQueryable<AgingApplicationRowDto> ProjectAging(IQueryable<ApplicationEntity> q, DateTime nowUtc) =>
        q.Select(a => new AgingApplicationRowDto(
            a.Id,
            (a.Applicant.FirstName + " " + a.Applicant.LastName).Trim(),
            a.Applicant.Email,
            a.Applicant.LegalId,
            a.State,
            (int)EF.Functions.DateDiffDay(a.UpdatedAt, nowUtc),
            (DateTime?)a.UpdatedAt,
            (string?)null,
            a.Items.Count,
            (IReadOnlyList<CurrencyAmount>)Array.Empty<CurrencyAmount>(),
            a.Group!.Process!.Fund!.Name // Spec 029 / FR-012
        ));

    private static IQueryable<ApplicationEntity> ApplyAgingSort(IQueryable<ApplicationEntity> q, string? sort)
    {
        // DaysInCurrentState = DateDiff(UpdatedAt, now) — older UpdatedAt = larger days.
        // So days-asc <-> UpdatedAt-desc; default days-desc <-> UpdatedAt-asc.
        return sort switch
        {
            "days-asc"       => q.OrderByDescending(a => a.UpdatedAt).ThenBy(a => a.Id),
            "applicant-asc"  => q.OrderBy(a => a.Applicant.FirstName).ThenBy(a => a.Applicant.LastName).ThenBy(a => a.Id),
            "applicant-desc" => q.OrderByDescending(a => a.Applicant.FirstName).ThenByDescending(a => a.Applicant.LastName).ThenByDescending(a => a.Id),
            "state-asc"      => q.OrderBy(a => a.State).ThenBy(a => a.Id),
            _                => q.OrderBy(a => a.UpdatedAt).ThenByDescending(a => a.Id),
        };
    }

    public async Task<DashboardResult> DashboardSnapshotAsync(DateRange range, CancellationToken ct = default)
    {
        var fromUtc = range.From.ToDateTime(TimeOnly.MinValue);
        var toUtc = range.To.ToDateTime(TimeOnly.MaxValue);

        var pipelineRaw = await ActiveApplications()
            .Where(a => a.State != ApplicationState.Draft)
            .GroupBy(a => a.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pipeline = Enum.GetValues<ApplicationState>()
            .Where(s => s != ApplicationState.Draft)
            .Select(s => new PipelineCount(s, pipelineRaw.FirstOrDefault(p => p.State == s)?.Count ?? 0))
            .ToList();

        var approvedRaw = await _db.Items
            .AsNoTracking()
            .Where(i => i.ReviewStatus == ItemReviewStatus.Approved
                     && i.SelectedSupplierId != null)
            .Join(ActiveApplications(),
                i => i.ApplicationId,
                a => a.Id,
                (i, a) => new { Item = i, App = a })
            .Where(x => x.App.State != ApplicationState.Draft
                     && x.App.State != ApplicationState.Submitted
                     && x.App.State != ApplicationState.UnderReview
                     && x.App.UpdatedAt >= fromUtc && x.App.UpdatedAt <= toUtc)
            .Join(_db.Quotations.AsNoTracking(),
                x => new { ItemId = x.Item.Id, SupplierId = x.Item.SelectedSupplierId!.Value },
                q => new { ItemId = q.ItemId, SupplierId = q.SupplierId },
                (x, q) => new { x.App.State, q.Currency, q.Price, q.ConvertedCrcAmount })
            .ToListAsync(ct);

        var approvedStack = approvedRaw
            .GroupBy(r => r.Currency)
            .Select(g => new CurrencyAmount(g.Key, g.Sum(r => r.Price), g.Sum(r => r.ConvertedCrcAmount ?? 0m)))
            .OrderBy(c => c.Currency)
            .ToList();

        var executedStack = approvedRaw
            .Where(r => r.State == ApplicationState.AgreementExecuted)
            .GroupBy(r => r.Currency)
            .Select(g => new CurrencyAmount(g.Key, g.Sum(r => r.Price), g.Sum(r => r.ConvertedCrcAmount ?? 0m)))
            .OrderBy(c => c.Currency)
            .ToList();

        var pendingStack = approvedStack
            .Select(a =>
            {
                var executed = executedStack.FirstOrDefault(e => e.Currency == a.Currency);
                var executedAmount = executed?.Amount ?? 0m;
                var executedCrc = executed?.ConvertedCrcAmount ?? 0m;
                return new CurrencyAmount(a.Currency, a.Amount - executedAmount, a.ConvertedCrcAmount - executedCrc);
            })
            .Where(c => c.Amount > 0)
            .ToList();

        var financial = new List<FinancialKpi>
        {
            new("Approved this period",  approvedStack),
            new("Executed this period",  executedStack),
            new("Pending execution",     pendingStack),
        };

        var nonTerminalStates = new[]
        {
            ApplicationState.Draft,
            ApplicationState.Submitted,
            ApplicationState.UnderReview,
            ApplicationState.Resolved,
            ApplicationState.ResponseFinalized,
            ApplicationState.AppealOpen,
        };

        var activeApplicants = await _db.Applicants
            .AsNoTracking()
            .CountAsync(a => a.Applications.Any(app => nonTerminalStates.Contains(app.State)), ct);

        var repeatApplicants = await _db.Applicants
            .AsNoTracking()
            .CountAsync(a => a.Applications.Count(app => app.SubmittedAt != null) >= 2, ct);

        var newThisPeriod = await _db.Applicants
            .AsNoTracking()
            .Where(a => a.Applications.Any(app => app.SubmittedAt != null))
            .Select(a => new
            {
                MinSubmitted = a.Applications
                    .Where(app => app.SubmittedAt != null)
                    .Min(app => app.SubmittedAt)
            })
            .CountAsync(x => x.MinSubmitted >= fromUtc && x.MinSubmitted <= toUtc, ct);

        var applicants = new List<ApplicantKpi>
        {
            new("Active applicants",   activeApplicants),
            new("Repeat applicants",   repeatApplicants),
            new("New this period",     newThisPeriod),
        };

        return new DashboardResult(range, pipeline, financial, applicants);
    }

    private IQueryable<ApplicationEntity> BuildApplicationsBaseQuery(ListApplicationsRequest req)
    {
        var q = ActiveApplications().AsQueryable();

        if (req.States is { Count: > 0 } states)
        {
            q = q.Where(a => states.Contains(a.State));
        }

        if (req.From.HasValue)
        {
            var fromUtc = req.From.Value.ToDateTime(TimeOnly.MinValue);
            q = q.Where(a => a.UpdatedAt >= fromUtc);
        }

        if (req.To.HasValue)
        {
            var toUtc = req.To.Value.ToDateTime(TimeOnly.MaxValue);
            q = q.Where(a => a.UpdatedAt <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var pattern = $"%{req.Search.Trim()}%";
            q = q.Where(a =>
                EF.Functions.Like(a.Applicant.FirstName + " " + a.Applicant.LastName, pattern)
                || EF.Functions.Like(a.Applicant.LegalId, pattern)
                || EF.Functions.Like(a.Applicant.Email, pattern));
        }

        if (req.HasAgreement.HasValue)
        {
            q = req.HasAgreement.Value
                ? q.Where(a => a.FundingAgreement != null)
                : q.Where(a => a.FundingAgreement == null);
        }

        if (req.HasActiveAppeal.HasValue)
        {
            q = req.HasActiveAppeal.Value
                ? q.Where(a => a.Appeals.Any(ap => ap.Status == AppealStatus.Open))
                : q.Where(a => !a.Appeals.Any(ap => ap.Status == AppealStatus.Open));
        }

        // Spec 029 / FR-012 — exact Fund filter via the anchor.
        if (req.FundId is { } fundId)
        {
            q = q.Where(a => a.Group!.Process!.FundId == fundId);
        }

        return q;
    }

    private static IQueryable<ApplicationProjection> ProjectApplications(IQueryable<ApplicationEntity> q) =>
        q.Select(a => new ApplicationProjection(
            a.Id,
            (a.Applicant.FirstName + " " + a.Applicant.LastName).Trim(),
            a.Applicant.LegalId,
            a.State,
            a.CreatedAt,
            a.SubmittedAt,
            a.UpdatedAt,
            a.Items.Count,
            a.FundingAgreement != null,
            a.Appeals.Any(ap => ap.Status == AppealStatus.Open),
            a.Group!.Process!.Fund!.Name // Spec 029 / FR-012 — exact Fund via the anchor.
        ));

    private IQueryable<Applicant> BuildApplicantsBaseQuery(ListApplicantsRequest req)
    {
        var q = _db.Applicants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var pattern = $"%{req.Search.Trim()}%";
            q = q.Where(a =>
                EF.Functions.Like(a.FirstName + " " + a.LastName, pattern)
                || EF.Functions.Like(a.LegalId, pattern)
                || EF.Functions.Like(a.Email, pattern));
        }

        if (req.HasExecutedAgreement.HasValue)
        {
            q = req.HasExecutedAgreement.Value
                ? q.Where(a => a.Applications.Any(app => app.State == ApplicationState.AgreementExecuted))
                : q.Where(a => !a.Applications.Any(app => app.State == ApplicationState.AgreementExecuted));
        }

        if (req.LastActivityFrom.HasValue)
        {
            var fromUtc = req.LastActivityFrom.Value.ToDateTime(TimeOnly.MinValue);
            q = q.Where(a => a.Applications.Any() && a.Applications.Max(app => app.UpdatedAt) >= fromUtc);
        }

        if (req.LastActivityTo.HasValue)
        {
            var toUtc = req.LastActivityTo.Value.ToDateTime(TimeOnly.MaxValue);
            q = q.Where(a => a.Applications.Any() && a.Applications.Max(app => app.UpdatedAt) <= toUtc);
        }

        return q;
    }

    private static IQueryable<ApplicantProjection> ProjectApplicants(IQueryable<Applicant> q) =>
        q.Select(a => new ApplicantProjection(
            a.Id,
            (a.FirstName + " " + a.LastName).Trim(),
            a.LegalId,
            a.Email,
            a.Applications.Count(app => app.SubmittedAt != null),
            a.Applications.Count(app => app.State == ApplicationState.Resolved),
            a.Applications.Count(app => app.State == ApplicationState.ResponseFinalized),
            a.Applications.Count(app => app.State == ApplicationState.AgreementExecuted),
            a.Applications.Sum(app => app.Items.Count),
            a.Applications.Sum(app => app.Items.Count(i => i.ReviewStatus == ItemReviewStatus.Approved)),
            a.Applications.Any() ? (DateTime?)a.Applications.Max(app => app.UpdatedAt) : null
        ));

    private static IQueryable<Applicant> ApplyApplicantsSort(IQueryable<Applicant> q, string? sort)
    {
        return sort switch
        {
            "executed-asc"      => q.OrderBy(a => a.Applications.Count(app => app.State == ApplicationState.AgreementExecuted)).ThenBy(a => a.Id),
            "approved-desc"     => q.OrderByDescending(a => a.Applications.Sum(app => app.Items.Count(i => i.ReviewStatus == ItemReviewStatus.Approved))).ThenByDescending(a => a.Id),
            "approved-asc"      => q.OrderBy(a => a.Applications.Sum(app => app.Items.Count(i => i.ReviewStatus == ItemReviewStatus.Approved))).ThenBy(a => a.Id),
            "applicant-asc"     => q.OrderBy(a => a.FirstName).ThenBy(a => a.LastName).ThenBy(a => a.Id),
            "applicant-desc"    => q.OrderByDescending(a => a.FirstName).ThenByDescending(a => a.LastName).ThenByDescending(a => a.Id),
            "lastActivity-desc" => q.OrderByDescending(a => a.Applications.Max(app => (DateTime?)app.UpdatedAt)).ThenByDescending(a => a.Id),
            "lastActivity-asc"  => q.OrderBy(a => a.Applications.Max(app => (DateTime?)app.UpdatedAt)).ThenBy(a => a.Id),
            _                   => q.OrderByDescending(a => a.Applications.Count(app => app.State == ApplicationState.AgreementExecuted)).ThenByDescending(a => a.Id),
        };
    }

    private static IQueryable<ApplicationEntity> ApplyApplicationsSort(IQueryable<ApplicationEntity> q, string? sort)
    {
        return sort switch
        {
            "updated-asc"     => q.OrderBy(a => a.UpdatedAt).ThenBy(a => a.Id),
            "submitted-desc"  => q.OrderByDescending(a => a.SubmittedAt).ThenByDescending(a => a.Id),
            "submitted-asc"   => q.OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id),
            "applicant-asc"   => q.OrderBy(a => a.Applicant.FirstName).ThenBy(a => a.Applicant.LastName).ThenBy(a => a.Id),
            "applicant-desc"  => q.OrderByDescending(a => a.Applicant.FirstName).ThenByDescending(a => a.Applicant.LastName).ThenByDescending(a => a.Id),
            _                 => q.OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id),
        };
    }

    /// <summary>
    /// Anonymous-style carrier for the FundedItems join graph. Holding entity references
    /// (rather than projected primitives) lets EF apply OrderBy/Skip/Take/Project as a
    /// single SQL plan; the alternative — sorting an already-projected DTO that contains
    /// computed expressions — fails with EF Core's "could not be translated" diagnostic.
    /// </summary>
    /// <summary>
    /// Plain class (not record) with member-initializer construction — EF Core's
    /// expression translator handles member-init projections by inlining property
    /// access through the join graph, whereas a record's positional constructor blocks
    /// translation when subsequent OrderBy/Select reference its members.
    /// </summary>
    private sealed class FundedItemsCarrier
    {
        public Item I { get; init; } = null!;
        public ApplicationEntity A { get; init; } = null!;
        public Quotation Q { get; init; } = null!;
        public Supplier S { get; init; } = null!;
        public Category C { get; init; } = null!;
    }
}
