using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.DTOs;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US1, US7 / R2, R4, R5 — assembles the admin dashboard view model.
/// Public surface is a single <see cref="GetAsync(CancellationToken)"/> method;
/// the four KPI sub-projections are private. Each KPI degrades to <c>0</c> with a
/// WARN log on failure (R2) so a single bad source never blocks the dashboard.
/// </summary>
public sealed class AdminDashboardProjection : IAdminDashboardProjection
{
    /// <summary>Default aging threshold matches <c>ListAgingApplicationsRequest.Threshold</c>.</summary>
    public const int DefaultAgingThresholdDays = 14;

    /// <summary>Activity feed window per FR-024 — 30 days.</summary>
    public static readonly TimeSpan ActivityFeedWindow = TimeSpan.FromDays(30);

    /// <summary>Activity feed cap per FR-024 — top 5.</summary>
    public const int ActivityFeedTake = 5;

    private readonly ISupplierRepository _suppliers;
    private readonly IAdminReportsService _reports;
    private readonly IQuotationLegacyRepository _legacyQuotations;
    private readonly IUserStoreReader _users;
    private readonly IAdminAuditEventReader _auditReader;
    private readonly IAdminAuditEventCopyProvider _copy;
    private readonly ILogger<AdminDashboardProjection> _logger;

    public AdminDashboardProjection(
        ISupplierRepository suppliers,
        IAdminReportsService reports,
        IQuotationLegacyRepository legacyQuotations,
        IUserStoreReader users,
        IAdminAuditEventReader auditReader,
        IAdminAuditEventCopyProvider copy,
        ILogger<AdminDashboardProjection> logger)
    {
        _suppliers = suppliers;
        _reports = reports;
        _legacyQuotations = legacyQuotations;
        _users = users;
        _auditReader = auditReader;
        _copy = copy;
        _logger = logger;
    }

    public async Task<AdminDashboardDto> GetAsync(CancellationToken ct)
    {
        var pendingSuppliers = await SafeAsync("PendingSuppliers", GetPendingSupplierCountAsync, ct);
        var pendingLegacy = await SafeAsync("PendingLegacyQuotations", GetPendingLegacyQuotationCountAsync, ct);
        var aging = await SafeAsync("AgingApplications", GetAgingApplicationCountAsync, ct);
        var activeUsers = await SafeAsync("ActiveUsers", _users.GetActiveUserCountAsync, ct);

        var kpis = new AdminDashboardKpis(
            PendingSuppliers: pendingSuppliers,
            PendingSuppliersUrl: "/Admin/Suppliers?status=PendingReview",
            PendingLegacyQuotations: pendingLegacy,
            PendingLegacyQuotationsUrl: "/Admin/LegacyQuotations",
            AgingApplications: aging,
            AgingApplicationsUrl: "/Admin/Reports/Aging",
            ActiveUsers: activeUsers,
            ActiveUsersUrl: "/Admin/Users?status=Active");

        var sections = BuildSections();
        var events = await BuildRecentEventsAsync(ct);
        return new AdminDashboardDto(
            Kpis: kpis,
            Sections: sections,
            RecentEvents: events,
            FeedVisible: events.Count > 0);
    }

    /// <summary>Spec 017 / FR-004 — three template-time sections, nine cards.</summary>
    public static IReadOnlyList<CapabilitySection> BuildSections() => new List<CapabilitySection>
    {
        new(
            HeaderLabel: "Usuarios y acceso",
            HeaderSlug: "users-access",
            Cards: new List<CapabilityCard>
            {
                new("users", "Usuarios", "Cuentas, roles y estado de acceso.",
                    "ti ti-users", "/Admin/Users", "Administrar usuarios"),
                new("groups", "Grupos", "Defina cohortes y asigne membresías.",
                    "ti ti-users-group", "/Admin/Groups", "Administrar grupos"),
            }),
        new(
            HeaderLabel: "Catálogo",
            HeaderSlug: "catalog",
            Cards: new List<CapabilityCard>
            {
                new("suppliers", "Proveedores", "Verifique, edite o rechace proveedores.",
                    "ti ti-building-store", "/Admin/Suppliers", "Administrar proveedores"),
                new("currencies", "Monedas", "Habilite o deshabilite las monedas del catálogo.",
                    "ti ti-coin", "/Admin/Currencies", "Administrar monedas"),
                new("exchange-rates", "Tipos de cambio", "Registre tasas de referencia para conversiones.",
                    "ti ti-arrows-exchange", "/Admin/ExchangeRates", "Administrar tipos de cambio"),
                new("impact-templates", "Plantillas de impacto", "Defina parámetros de evaluación.",
                    "ti ti-template", "/Admin/ImpactTemplates", "Administrar plantillas"),
            }),
        new(
            HeaderLabel: "Operaciones",
            HeaderSlug: "operations",
            Cards: new List<CapabilityCard>
            {
                new("reports", "Reportes", "Indicadores y exportes operativos.",
                    "ti ti-chart-line", "/Admin/Reports", "Ver reportes"),
                new("legacy-quotations", "Cotizaciones legadas", "Adjunte tipos de cambio históricos.",
                    "ti ti-history", "/Admin/LegacyQuotations", "Revisar cola"),
                new("system-config", "Configuración del sistema", "Parámetros de la plataforma.",
                    "ti ti-settings", "/Admin/Configuration", "Administrar configuración"),
            }),
    };

    private async Task<int> GetPendingSupplierCountAsync(CancellationToken ct)
    {
        var (_, total) = await _suppliers.ListForAdminAsync(
            new SupplierAdminFilter { Status = SupplierVerificationStatus.PendingReview },
            page: 1,
            pageSize: 1);
        return total;
    }

    private async Task<int> GetPendingLegacyQuotationCountAsync(CancellationToken ct)
    {
        var rows = await _legacyQuotations.ListFlaggedAsync(ct);
        return rows.Count;
    }

    private async Task<int> GetAgingApplicationCountAsync(CancellationToken ct)
    {
        var req = new ListAgingApplicationsRequest
        {
            States = Array.Empty<ApplicationState>(),
            Threshold = DefaultAgingThresholdDays,
            Search = null,
            Page = 1,
            PageSize = 1,
            Sort = "days-desc",
        };
        var result = await _reports.ListAgingApplicationsAsync(req, ct);
        return result.TotalCount;
    }

    private async Task<IReadOnlyList<AdminEvent>> BuildRecentEventsAsync(CancellationToken ct)
    {
        IReadOnlyList<AdminAuditEvent> rows;
        try
        {
            rows = await _auditReader.GetRecentAsync(ActivityFeedTake, ActivityFeedWindow, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AdminDashboardActivityFeedFailed");
            return Array.Empty<AdminEvent>();
        }

        var projected = new List<AdminEvent>(rows.Count);
        foreach (var ev in rows)
        {
            var actor = await SafeDisplayNameAsync(ev.ActorUserId, ct);
            var phrase = _copy.Format(ev.Action, ev.TargetType, ev.PayloadJson);
            var deepLink = ResolveDeepLink(ev);
            projected.Add(new AdminEvent(
                OccurredAt: ev.OccurredAt,
                ActorDisplayName: actor,
                Copy: phrase,
                DeepLinkUrl: deepLink));
        }
        return projected;
    }

    /// <summary>
    /// FR-024 + R5 — group-target events deep-link to the group-edit surface,
    /// user-target events to the user-edit surface, except <c>group.delete</c>
    /// where the target is gone (returns null so the row renders without a link).
    /// </summary>
    private static string? ResolveDeepLink(AdminAuditEvent ev)
    {
        if (string.Equals(ev.Action, AdminAuditEvent.ActionGroupDelete, StringComparison.Ordinal))
        {
            return null;
        }
        return ev.TargetType switch
        {
            AdminAuditEvent.TargetTypeGroup => $"/Admin/Groups/{ev.TargetId}/Edit",
            AdminAuditEvent.TargetTypeUser => $"/Admin/Users/{ev.TargetId}/Edit",
            _ => null,
        };
    }

    private async Task<string> SafeDisplayNameAsync(string userId, CancellationToken ct)
    {
        try
        {
            return await _users.GetDisplayNameAsync(userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AdminDashboardActorNameLookupFailed for {UserId}",
                userId);
            return userId;
        }
    }

    /// <summary>
    /// R2 degrade-to-zero wrapper — swallows exceptions, emits the structured
    /// WARN log <c>AdminDashboardKpiProjectionFailed</c>, and returns 0. The
    /// dashboard never breaks because a single source faulted.
    /// </summary>
    private async Task<int> SafeAsync(string kpi, Func<CancellationToken, Task<int>> work, CancellationToken ct)
    {
        try
        {
            return await work(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AdminDashboardKpiProjectionFailed Kpi={Kpi} Reason={Reason}",
                kpi, ex.Message);
            return 0;
        }
    }
}
