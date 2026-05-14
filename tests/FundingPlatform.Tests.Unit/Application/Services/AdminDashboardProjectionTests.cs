using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.DTOs;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FundingPlatform.Tests.Unit.Application.Services;

/// <summary>
/// Spec 017 / US1 / R2, R4 — verifies the dashboard projection composes the four
/// KPIs, three capability sections (with all 9 cards), and the activity feed
/// with the documented degrade-to-zero behavior on sub-projection failure.
/// </summary>
[TestFixture]
public class AdminDashboardProjectionTests
{
    [Test]
    public async Task GetAsync_HappyPath_ReturnsAllKpisAndNineCards()
    {
        var suppliers = Substitute.For<ISupplierRepository>();
        suppliers.ListForAdminAsync(Arg.Any<SupplierAdminFilter>(), 1, 1)
            .Returns(((IReadOnlyList<Supplier>)Array.Empty<Supplier>(), 7));

        var legacy = Substitute.For<IQuotationLegacyRepository>();
        legacy.ListFlaggedAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new LegacyQuotationRow(1, 100, 1, "Item", "Sup", 100m, "USD", DateTime.UtcNow),
                new LegacyQuotationRow(2, 100, 2, "Item", "Sup", 100m, "USD", DateTime.UtcNow),
                new LegacyQuotationRow(3, 100, 3, "Item", "Sup", 100m, "USD", DateTime.UtcNow),
            });

        var reports = Substitute.For<IAdminReportsService>();
        reports.ListAgingApplicationsAsync(Arg.Any<ListAgingApplicationsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListAgingApplicationsResult(
                Array.Empty<AgingApplicationRowDto>(),
                42,
                new ListAgingApplicationsRequest()));

        var users = Substitute.For<IUserStoreReader>();
        users.GetActiveUserCountAsync(Arg.Any<CancellationToken>()).Returns(11);

        var auditReader = Substitute.For<IAdminAuditEventReader>();
        auditReader.GetRecentAsync(5, TimeSpan.FromDays(30), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AdminAuditEvent>());

        var projection = BuildProjection(suppliers, reports, legacy, users, auditReader);

        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Kpis.PendingSuppliers, Is.EqualTo(7));
            Assert.That(dto.Kpis.PendingLegacyQuotations, Is.EqualTo(3));
            Assert.That(dto.Kpis.AgingApplications, Is.EqualTo(42));
            Assert.That(dto.Kpis.ActiveUsers, Is.EqualTo(11));
            Assert.That(dto.Kpis.PendingSuppliersUrl, Is.EqualTo("/Admin/Suppliers?status=PendingReview"));
            Assert.That(dto.Kpis.AgingApplicationsUrl, Is.EqualTo("/Admin/Reports/Aging"));
            Assert.That(dto.Sections, Has.Count.EqualTo(3));
            var totalCards = dto.Sections.Sum(s => s.Cards.Count);
            Assert.That(totalCards, Is.EqualTo(9), "FR-004 demands 9 capability cards.");
            Assert.That(dto.RecentEvents, Is.Empty);
            Assert.That(dto.FeedVisible, Is.False);
        });
    }

    [Test]
    public async Task GetAsync_PendingSupplierFailure_DegradesToZero()
    {
        var suppliers = Substitute.For<ISupplierRepository>();
        suppliers.ListForAdminAsync(Arg.Any<SupplierAdminFilter>(), 1, 1)
            .Returns<(IReadOnlyList<Supplier>, int)>(_ => throw new InvalidOperationException("synthetic"));

        var legacy = Substitute.For<IQuotationLegacyRepository>();
        legacy.ListFlaggedAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LegacyQuotationRow>());

        var reports = Substitute.For<IAdminReportsService>();
        reports.ListAgingApplicationsAsync(Arg.Any<ListAgingApplicationsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListAgingApplicationsResult(Array.Empty<AgingApplicationRowDto>(), 0, new ListAgingApplicationsRequest()));

        var users = Substitute.For<IUserStoreReader>();
        users.GetActiveUserCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        var auditReader = Substitute.For<IAdminAuditEventReader>();
        auditReader.GetRecentAsync(5, TimeSpan.FromDays(30), Arg.Any<CancellationToken>()).Returns(Array.Empty<AdminAuditEvent>());

        var projection = BuildProjection(suppliers, reports, legacy, users, auditReader);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.Kpis.PendingSuppliers, Is.EqualTo(0),
            "R2 — failure in PendingSuppliers sub-projection MUST degrade to 0.");
    }

    [Test]
    public async Task GetAsync_LegacyFailure_DegradesToZero()
    {
        var suppliers = NoPendingSuppliers();
        var legacy = Substitute.For<IQuotationLegacyRepository>();
        legacy.ListFlaggedAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<LegacyQuotationRow>>(_ => throw new InvalidOperationException("synthetic"));
        var reports = NoAging();
        var users = ZeroActiveUsers();
        var audit = NoEvents();

        var projection = BuildProjection(suppliers, reports, legacy, users, audit);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.Kpis.PendingLegacyQuotations, Is.EqualTo(0));
    }

    [Test]
    public async Task GetAsync_AgingFailure_DegradesToZero()
    {
        var suppliers = NoPendingSuppliers();
        var legacy = NoLegacy();
        var reports = Substitute.For<IAdminReportsService>();
        reports.ListAgingApplicationsAsync(Arg.Any<ListAgingApplicationsRequest>(), Arg.Any<CancellationToken>())
            .Returns<ListAgingApplicationsResult>(_ => throw new InvalidOperationException("synthetic"));
        var users = ZeroActiveUsers();
        var audit = NoEvents();

        var projection = BuildProjection(suppliers, reports, legacy, users, audit);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.Kpis.AgingApplications, Is.EqualTo(0));
    }

    [Test]
    public async Task GetAsync_ActiveUsersFailure_DegradesToZero()
    {
        var suppliers = NoPendingSuppliers();
        var legacy = NoLegacy();
        var reports = NoAging();
        var users = Substitute.For<IUserStoreReader>();
        users.GetActiveUserCountAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("synthetic"));
        var audit = NoEvents();

        var projection = BuildProjection(suppliers, reports, legacy, users, audit);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.Kpis.ActiveUsers, Is.EqualTo(0));
    }

    [Test]
    public async Task GetAsync_AuditEventsPresent_FeedVisibleAndCopyApplied()
    {
        var suppliers = NoPendingSuppliers();
        var legacy = NoLegacy();
        var reports = NoAging();
        var users = Substitute.For<IUserStoreReader>();
        users.GetActiveUserCountAsync(Arg.Any<CancellationToken>()).Returns(0);
        users.GetDisplayNameAsync("u1", Arg.Any<CancellationToken>()).Returns("Ana López");

        var audit = Substitute.For<IAdminAuditEventReader>();
        var ev = AdminAuditEvent.Record(
            actorUserId: "u1",
            action: AdminAuditEvent.ActionGroupRename,
            targetType: AdminAuditEvent.TargetTypeGroup,
            targetId: "42",
            payloadJson: null);
        audit.GetRecentAsync(5, TimeSpan.FromDays(30), Arg.Any<CancellationToken>())
            .Returns(new[] { ev });

        var projection = BuildProjection(suppliers, reports, legacy, users, audit);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dto.FeedVisible, Is.True);
            Assert.That(dto.RecentEvents, Has.Count.EqualTo(1));
            var first = dto.RecentEvents[0];
            Assert.That(first.ActorDisplayName, Is.EqualTo("Ana López"));
            Assert.That(first.Copy, Is.EqualTo("renombró el grupo"));
            Assert.That(first.DeepLinkUrl, Is.EqualTo("/Admin/Groups/42/Edit"));
        });
    }

    [Test]
    public async Task GetAsync_GroupDeleteEvent_HasNoDeepLink()
    {
        var users = Substitute.For<IUserStoreReader>();
        users.GetActiveUserCountAsync(Arg.Any<CancellationToken>()).Returns(0);
        users.GetDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Actor");

        var audit = Substitute.For<IAdminAuditEventReader>();
        var ev = AdminAuditEvent.Record("u1", AdminAuditEvent.ActionGroupDelete, AdminAuditEvent.TargetTypeGroup, "9", null);
        audit.GetRecentAsync(5, TimeSpan.FromDays(30), Arg.Any<CancellationToken>()).Returns(new[] { ev });

        var projection = BuildProjection(NoPendingSuppliers(), NoAging(), NoLegacy(), users, audit);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.RecentEvents[0].DeepLinkUrl, Is.Null,
            "Deleted-target events render without a deep-link.");
    }

    [Test]
    public async Task GetAsync_PersonasActivas_AndFondosEntregados_AreSurfaced()
    {
        // Spec 021 / US6 / T135 / FR-032 / SC-010 — the two narrative KPI
        // counters supplied by IAdminDashboardCountersReader must surface on
        // AdminDashboardDto.Kpis without disturbing the four action KPIs.
        var counters = Substitute.For<IAdminDashboardCountersReader>();
        counters.CountPersonasActivasAsync(Arg.Any<CancellationToken>()).Returns(13);
        counters.SumFondosEntregadosAsync(Arg.Any<CancellationToken>()).Returns(5_000_000m);

        var projection = BuildProjection(
            NoPendingSuppliers(), NoAging(), NoLegacy(), ZeroActiveUsers(), NoEvents(), counters);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Kpis.PersonasActivas, Is.EqualTo(13));
            Assert.That(dto.Kpis.FondosEntregados, Is.EqualTo(5_000_000m));
        });
    }

    [Test]
    public async Task GetAsync_PersonasActivasFailure_DegradesToZero()
    {
        // Spec 021 / US6 / R-2 — same degrade-to-zero posture for the new tiles.
        var counters = Substitute.For<IAdminDashboardCountersReader>();
        counters.CountPersonasActivasAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("synthetic"));
        counters.SumFondosEntregadosAsync(Arg.Any<CancellationToken>()).Returns(7m);

        var projection = BuildProjection(
            NoPendingSuppliers(), NoAging(), NoLegacy(), ZeroActiveUsers(), NoEvents(), counters);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.Kpis.PersonasActivas, Is.EqualTo(0));
        Assert.That(dto.Kpis.FondosEntregados, Is.EqualTo(7m));
    }

    [Test]
    public async Task GetAsync_FondosEntregadosFailure_DegradesToZeroDecimal()
    {
        var counters = Substitute.For<IAdminDashboardCountersReader>();
        counters.CountPersonasActivasAsync(Arg.Any<CancellationToken>()).Returns(2);
        counters.SumFondosEntregadosAsync(Arg.Any<CancellationToken>())
            .Returns<decimal>(_ => throw new InvalidOperationException("synthetic"));

        var projection = BuildProjection(
            NoPendingSuppliers(), NoAging(), NoLegacy(), ZeroActiveUsers(), NoEvents(), counters);
        var dto = await projection.GetAsync(CancellationToken.None);

        Assert.That(dto.Kpis.FondosEntregados, Is.EqualTo(0m));
        Assert.That(dto.Kpis.PersonasActivas, Is.EqualTo(2));
    }

    [Test]
    public void BuildSections_ContainsExpectedSlugs()
    {
        var sections = AdminDashboardProjection.BuildSections();
        var slugs = sections.SelectMany(s => s.Cards).Select(c => c.Slug).ToHashSet();
        var expected = new[]
        {
            "users", "groups",
            "suppliers", "currencies", "exchange-rates", "impact-templates",
            "reports", "legacy-quotations", "system-config",
        };
        foreach (var s in expected)
        {
            Assert.That(slugs.Contains(s), Is.True, $"Slug '{s}' must be present.");
        }
    }

    private static AdminDashboardProjection BuildProjection(
        ISupplierRepository suppliers,
        IAdminReportsService reports,
        IQuotationLegacyRepository legacy,
        IUserStoreReader users,
        IAdminAuditEventReader audit,
        IAdminDashboardCountersReader? counters = null)
    {
        return new AdminDashboardProjection(
            suppliers,
            reports,
            legacy,
            users,
            audit,
            new AdminAuditEventCopyProvider(),
            counters ?? ZeroCounters(),
            NullLogger<AdminDashboardProjection>.Instance);
    }

    // Spec 021 / US6 / T135 — default narrative-KPI reader for existing test
    // scenarios that pre-date FR-032. Returns zero for both counters; tests
    // exercising the new tiles inject a configured substitute.
    private static IAdminDashboardCountersReader ZeroCounters()
    {
        var c = Substitute.For<IAdminDashboardCountersReader>();
        c.CountPersonasActivasAsync(Arg.Any<CancellationToken>()).Returns(0);
        c.SumFondosEntregadosAsync(Arg.Any<CancellationToken>()).Returns(0m);
        return c;
    }

    private static ISupplierRepository NoPendingSuppliers()
    {
        var s = Substitute.For<ISupplierRepository>();
        s.ListForAdminAsync(Arg.Any<SupplierAdminFilter>(), 1, 1)
            .Returns(((IReadOnlyList<Supplier>)Array.Empty<Supplier>(), 0));
        return s;
    }

    private static IQuotationLegacyRepository NoLegacy()
    {
        var l = Substitute.For<IQuotationLegacyRepository>();
        l.ListFlaggedAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LegacyQuotationRow>());
        return l;
    }

    private static IAdminReportsService NoAging()
    {
        var r = Substitute.For<IAdminReportsService>();
        r.ListAgingApplicationsAsync(Arg.Any<ListAgingApplicationsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListAgingApplicationsResult(Array.Empty<AgingApplicationRowDto>(), 0, new ListAgingApplicationsRequest()));
        return r;
    }

    private static IUserStoreReader ZeroActiveUsers()
    {
        var u = Substitute.For<IUserStoreReader>();
        u.GetActiveUserCountAsync(Arg.Any<CancellationToken>()).Returns(0);
        u.GetDisplayNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Actor");
        return u;
    }

    private static IAdminAuditEventReader NoEvents()
    {
        var a = Substitute.For<IAdminAuditEventReader>();
        a.GetRecentAsync(5, TimeSpan.FromDays(30), Arg.Any<CancellationToken>()).Returns(Array.Empty<AdminAuditEvent>());
        return a;
    }
}
