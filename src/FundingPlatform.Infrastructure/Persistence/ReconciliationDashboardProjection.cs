// Spec 048 — see specs/048-full-reconciliation-engine/contracts/interfaces.md (dashboard projection).

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 048 / US3 — EF implementation of the group-scoped reconciliation dashboard. Group-scoping is
/// enforced in-query (admin short-circuit; group-overlap on the applicant's memberships; empty-group
/// non-admin → empty), mirroring <see cref="EvidenceInboxProjection"/>. The base scoped set is capped
/// at <see cref="MaxRows"/>, enriched with per-scope labels (line/tranche/supplier for budget-lines),
/// then the tranche/line/supplier facets are applied in memory (build-then-filter — the
/// <c>ParticipantBalanceProjection</c> pattern). Reads only (no mutation).
/// </summary>
public sealed class ReconciliationDashboardProjection : IReconciliationDashboardProjection
{
    private const int MaxRows = 500;

    private readonly AppDbContext _db;
    private readonly IApplicationQueryFilter _queryFilter;

    public ReconciliationDashboardProjection(AppDbContext db, IApplicationQueryFilter queryFilter)
    {
        _db = db;
        _queryFilter = queryFilter;
    }

    public async Task<ReconciliationSummaryDto> GetSummaryAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct)
    {
        var built = await BuildScopedRowsAsync(scope, filter, ct);

        var blocking = built.Where(b => b.Row.Severity == DiscrepancySeverity.Blocking).ToList();
        var warning = built.Where(b => b.Row.Severity == DiscrepancySeverity.Warning).ToList();

        var byFund = built
            .GroupBy(b => string.IsNullOrWhiteSpace(b.FundName) ? "—" : b.FundName)
            .Select(g => new ReconciliationFundRollup(
                g.Key,
                g.Count(b => b.Row.Severity == DiscrepancySeverity.Blocking),
                g.Count(b => b.Row.Severity == DiscrepancySeverity.Warning)))
            .OrderByDescending(f => f.OpenBlockingCount + f.OpenWarningCount)
            .ThenBy(f => f.FundName)
            .ToList();

        return new ReconciliationSummaryDto(
            blocking.Count, blocking.Sum(b => Math.Abs(b.Row.Difference)),
            warning.Count, warning.Sum(b => Math.Abs(b.Row.Difference)),
            byFund);
    }

    public async Task<IReadOnlyList<DiscrepancyRowDto>> GetDiscrepanciesAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct)
    {
        var built = await BuildScopedRowsAsync(scope, filter, ct);
        return built.Select(b => b.Row).ToList().AsReadOnly();
    }

    public async Task<DiscrepancyDetailDto?> GetDetailAsync(IReviewerScope scope, int discrepancyId, CancellationToken ct)
    {
        var d = await _db.Discrepancies.AsNoTracking()
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == discrepancyId, ct);
        if (d is null)
        {
            return null;
        }

        // Route the owning application through the same soft-delete + archived-fund filter as the list
        // (security review — otherwise a discrepancy on a soft-deleted/archived-fund app, hidden from the
        // index, would still be readable AND mutable via /Reconciliation/{id}, since GuardWriteAsync
        // authorizes solely via this method returning non-null). A filtered-out app → flat 404.
        var apps = _queryFilter.ExcludeArchivedFund(_queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking()));
        var appInfo = await apps
            .Where(a => a.Id == d.ApplicationId)
            .Select(a => new { a.Applicant.FirstName, a.Applicant.LastName, a.Applicant.UserId })
            .FirstOrDefaultAsync(ct);
        if (appInfo is null)
        {
            return null; // soft-deleted / archived-fund → hidden everywhere
        }

        // Scope check: admin short-circuits; else the applicant must share a group with the caller.
        if (!scope.IsAdmin)
        {
            var groupIds = scope.GroupIds.ToList();
            if (groupIds.Count == 0)
            {
                return null;
            }
            var inScope = await _db.UserGroupMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == appInfo.UserId && groupIds.Contains(m.GroupId), ct);
            if (!inScope)
            {
                return null;
            }
        }

        var itemMap = await BuildItemMapAsync([d.ApplicationId], ct);
        var userNames = await BuildUserNameMapAsync(
            d.Events.Select(e => e.ActorUserId).Append(d.AssigneeUserId ?? string.Empty).ToList(), ct);

        var built = BuildRow(
            d.Id, d.ApplicationId, appInfo.FirstName, appInfo.LastName,
            d.ScopeType, d.ScopeEntityId, d.Comparison, d.Severity, d.State, d.Expected, d.Actual, d.Difference,
            d.SourceDocument, d.AssigneeUserId, DateOnly.FromDateTime(d.FirstDetectedAt.UtcDateTime),
            fundName: string.Empty, itemMap, userNames);

        var timeline = d.Events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new DiscrepancyEventDto(
                e.OccurredAt, e.Kind, e.FromState, e.ToState,
                userNames.TryGetValue(e.ActorUserId, out var n) ? n : "Sistema", e.Reason, e.Note))
            .ToList();

        return new DiscrepancyDetailDto(built.Row, RequiredActionFor(d.Severity, d.State), timeline, CanWrite: false);
    }

    // ---------------------------------------------------------------- shared scoped query + enrichment

    private sealed record BuiltRow(DiscrepancyRowDto Row, string FundName, int? ItemId, int? TrancheId, int? SupplierId);

    private async Task<List<BuiltRow>> BuildScopedRowsAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct)
    {
        var isAdmin = scope.IsAdmin;
        var groupIds = scope.GroupIds.ToList();
        if (!isAdmin && groupIds.Count == 0)
        {
            return [];
        }

        var apps = _queryFilter.ExcludeArchivedFund(_queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking()));

        var query =
            from d in _db.Discrepancies.AsNoTracking()
            join app in apps on d.ApplicationId equals app.Id
            where isAdmin || _db.UserGroupMemberships.Any(m => m.UserId == app.Applicant.UserId && groupIds.Contains(m.GroupId))
            select new
            {
                d.Id, d.ApplicationId, d.ScopeType, d.ScopeEntityId, d.Comparison, d.Severity, d.State,
                d.Expected, d.Actual, d.Difference, d.SourceDocument, d.AssigneeUserId, d.FirstDetectedAt,
                app.Applicant.FirstName, app.Applicant.LastName,
                FundName = app.Group!.Process!.Fund!.Name,
            };

        // SQL-translatable pre-filters.
        query = filter.State is { } st
            ? query.Where(r => r.State == st)
            : filter.OpenOnly ? query.Where(r => r.State != DiscrepancyState.Resolved) : query;
        if (filter.Severity is { } sev) query = query.Where(r => r.Severity == sev);
        if (filter.ParticipantApplicationId is { } appId) query = query.Where(r => r.ApplicationId == appId);
        if (!string.IsNullOrWhiteSpace(filter.ResponsibleUserId)) query = query.Where(r => r.AssigneeUserId == filter.ResponsibleUserId);

        var baseRows = await query
            .OrderByDescending(r => r.Severity == DiscrepancySeverity.Blocking)
            .ThenByDescending(r => r.FirstDetectedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        // Date facet (in memory — FirstDetectedAt is a DateTimeOffset).
        if (filter.DateFrom is { } from) baseRows = baseRows.Where(r => DateOnly.FromDateTime(r.FirstDetectedAt.UtcDateTime) >= from).ToList();
        if (filter.DateTo is { } to) baseRows = baseRows.Where(r => DateOnly.FromDateTime(r.FirstDetectedAt.UtcDateTime) <= to).ToList();

        var appIds = baseRows.Select(r => r.ApplicationId).Distinct().ToList();
        var itemMap = await BuildItemMapAsync(appIds, ct);
        var userNames = await BuildUserNameMapAsync(baseRows.Select(r => r.AssigneeUserId ?? string.Empty).ToList(), ct);

        var built = baseRows.Select(r => BuildRow(
            r.Id, r.ApplicationId, r.FirstName, r.LastName, r.ScopeType, r.ScopeEntityId, r.Comparison,
            r.Severity, r.State, r.Expected, r.Actual, r.Difference, r.SourceDocument, r.AssigneeUserId,
            DateOnly.FromDateTime(r.FirstDetectedAt.UtcDateTime), r.FundName ?? string.Empty, itemMap, userNames)).ToList();

        // Post-filters needing enrichment (line/tranche/supplier resolve on budget-line rows).
        if (filter.ItemId is { } fi) built = built.Where(b => b.ItemId == fi).ToList();
        if (filter.TrancheId is { } ft) built = built.Where(b => b.TrancheId == ft).ToList();
        if (filter.SupplierId is { } fs) built = built.Where(b => b.SupplierId == fs).ToList();

        return built;
    }

    private sealed record ItemInfo(string? LineCode, int? TrancheId, string? TrancheName, int? SupplierId, string? SupplierName);

    private async Task<Dictionary<int, ItemInfo>> BuildItemMapAsync(IReadOnlyList<int> appIds, CancellationToken ct)
    {
        if (appIds.Count == 0)
        {
            return [];
        }
        var items = await _db.Items.AsNoTracking()
            .Where(i => appIds.Contains(i.ApplicationId))
            .Select(i => new
            {
                i.Id,
                i.LineCode,
                i.TrancheId,
                TrancheName = i.TrancheId == null ? null : _db.Tranches.Where(t => t.Id == i.TrancheId).Select(t => t.Name).FirstOrDefault(),
                i.SelectedSupplierId,
                SupplierName = i.SelectedSupplier == null ? null : i.SelectedSupplier.Name,
            })
            .ToListAsync(ct);
        return items.ToDictionary(i => i.Id, i => new ItemInfo(i.LineCode, i.TrancheId, i.TrancheName, i.SelectedSupplierId, i.SupplierName));
    }

    private async Task<Dictionary<string, string>> BuildUserNameMapAsync(IReadOnlyList<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var users = await _db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.IsSystemSentinel })
            .ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => u.IsSystemSentinel
            ? "Sistema"
            : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : (u.Email ?? string.Empty)));
    }

    private static BuiltRow BuildRow(
        int id, int applicationId, string firstName, string lastName,
        DiscrepancyScopeType scopeType, int scopeEntityId, ReconciliationComparison comparison,
        DiscrepancySeverity severity, DiscrepancyState state, decimal expected, decimal actual, decimal difference,
        string sourceDocument, string? assigneeUserId, DateOnly firstDetected, string fundName,
        Dictionary<int, ItemInfo> itemMap, Dictionary<string, string> userNames)
    {
        string? lineLabel = null, trancheName = null, supplierName = null;
        int? itemId = null, trancheId = null, supplierId = null;

        if (scopeType == DiscrepancyScopeType.BudgetLine)
        {
            itemId = scopeEntityId;
            if (itemMap.TryGetValue(scopeEntityId, out var info))
            {
                lineLabel = info.LineCode ?? $"L-{scopeEntityId}";
                trancheName = info.TrancheName;
                trancheId = info.TrancheId;
                supplierName = info.SupplierName;
                supplierId = info.SupplierId;
            }
            else
            {
                lineLabel = $"L-{scopeEntityId}";
            }
        }

        var scopeLabel = scopeType switch
        {
            DiscrepancyScopeType.Payment => $"Pago #{scopeEntityId}",
            DiscrepancyScopeType.Document => $"Documento #{scopeEntityId}",
            DiscrepancyScopeType.BudgetLine => $"Línea {lineLabel}",
            DiscrepancyScopeType.Participant => "Participante",
            DiscrepancyScopeType.Tranche => "Tramo",
            _ => scopeType.ToString(),
        };

        var participant = $"{firstName} {lastName}".Trim() is { Length: > 0 } pn ? pn : "Solicitante";
        var assigneeName = !string.IsNullOrEmpty(assigneeUserId) && userNames.TryGetValue(assigneeUserId, out var an) ? an : null;

        var row = new DiscrepancyRowDto(
            id, applicationId, $"APP-{applicationId:D5}", participant, scopeType, scopeLabel, comparison,
            severity, state, expected, actual, difference, sourceDocument, trancheName, lineLabel, supplierName,
            assigneeName, firstDetected);

        return new BuiltRow(row, fundName, itemId, trancheId, supplierId);
    }

    private static string RequiredActionFor(DiscrepancySeverity severity, DiscrepancyState state) => (severity, state) switch
    {
        (_, DiscrepancyState.Resolved) => "Ninguna: la diferencia ya no está presente.",
        (DiscrepancySeverity.Blocking, _) => "Corrija los montos en origen; una diferencia bloqueante no se puede exonerar.",
        (DiscrepancySeverity.Warning, DiscrepancyState.Waived) => "Aceptada. Se reabrirá si el monto cambia.",
        (DiscrepancySeverity.Warning, _) => "Revise y corrija, o exonere con un motivo si es aceptable.",
        _ => "Revise la diferencia.",
    };
}
