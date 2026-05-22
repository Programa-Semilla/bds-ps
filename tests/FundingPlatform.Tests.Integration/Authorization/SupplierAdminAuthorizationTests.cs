// Spec 021 — see specs/021-feedback-session-may13/tasks.md T102 and
// spec.md US3 + FR-007 + contracts/admin-routes.md (Denied surfaces) +
// contracts/audit-events.md (SupplierAdminDeniedAccess payload).

using System.Security.Claims;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Controllers;
using FundingPlatform.Web.Controllers.Admin;
using FundingPlatform.Web.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Authorization;

/// <summary>
/// Spec 021 / US3 / T102 / FR-007 — authorization matrix for the
/// <c>SupplierAdmin</c> role. Each test exercises the filter pair
/// (<see cref="SupplierAdminOnlyAttribute"/> on Suppliers,
/// <see cref="SupplierAdminDeniedAttribute"/> on everything else) for a user
/// holding ONLY the SupplierAdmin role, against every admin controller class
/// in the codebase.
///
/// <para>
/// Uses the EF InMemory provider following the existing
/// <see cref="GroupServiceTests"/> pattern (no mocks for the DbContext or the
/// audit writer; real implementations exercised end-to-end). The
/// <c>AuthorizationFilterContext</c> is constructed by hand so the test does
/// not depend on a full HTTP pipeline — each controller class is enumerated
/// via reflection and only the filter attributes on the class are inspected.
/// </para>
///
/// <para>
/// Matrix coverage (FR-007):
/// </para>
/// <list type="bullet">
///   <item>SupplierAdmin × AdminSuppliersController → allowed (no result).</item>
///   <item>SupplierAdmin × every other admin controller → 403 result + audit
///         row of kind <c>supplier_admin.denied_access</c> with payload
///         <c>{ route, method, userAgent }</c>.</item>
///   <item>Admin user × every controller → allowed (no audit row).</item>
/// </list>
/// </summary>
[TestFixture]
public class SupplierAdminAuthorizationTests
{
    private const string SupplierAdminUserId = "supplier-admin-1";
    private const string AdminUserId = "admin-1";

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ClaimsPrincipal BuildPrincipal(string userId, params string[] roles)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        identity.AddClaim(new Claim(ClaimTypes.Name, userId + "@example.com"));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizationFilterContext BuildContext(
        ClaimsPrincipal user,
        string path,
        string method = "GET")
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
        };
        httpContext.Request.Path = path;
        httpContext.Request.Method = method;
        httpContext.Request.Headers.UserAgent = "FundingPlatformTests/1.0";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, Array.Empty<IFilterMetadata>());
    }

    /// <summary>
    /// Spec 021 / T107 — enumerate every controller class the spec routes onto
    /// <c>[SupplierAdminDenied]</c>. Kept explicit (not reflection-discovered)
    /// so a missing attribute on a freshly added controller surfaces as a
    /// failing test, not a silent gap.
    /// </summary>
    private static readonly Type[] DeniedControllerTypes =
    {
        typeof(AdminController),
        typeof(AdminCurrenciesController),
        typeof(AdminExchangeRatesController),
        typeof(AdminGroupsController),
        typeof(AdminLegacyQuotationsController),
        typeof(AdminPlantillasController),
        typeof(AdminProcessesController),
        typeof(AdminReportsController),
        typeof(AdminUsersController),
    };

    [Test]
    public void Every_Denied_Controller_Has_SupplierAdminDenied_Attribute()
    {
        foreach (var t in DeniedControllerTypes)
        {
            var attr = t.GetCustomAttributes(typeof(SupplierAdminDeniedAttribute), inherit: false);
            Assert.That(attr, Is.Not.Empty,
                $"Controller {t.Name} MUST carry [SupplierAdminDenied] per spec 021 FR-007 / T107.");
        }
    }

    [Test]
    public void AdminSuppliersController_Carries_SupplierAdminOnly_And_Not_Denied()
    {
        var onlyAttr = typeof(AdminSuppliersController)
            .GetCustomAttributes(typeof(SupplierAdminOnlyAttribute), inherit: false);
        var deniedAttr = typeof(AdminSuppliersController)
            .GetCustomAttributes(typeof(SupplierAdminDeniedAttribute), inherit: false);

        Assert.That(onlyAttr, Is.Not.Empty,
            "AdminSuppliersController MUST carry [SupplierAdminOnly] (FR-007).");
        Assert.That(deniedAttr, Is.Empty,
            "AdminSuppliersController MUST NOT carry [SupplierAdminDenied] (FR-007).");
    }

    [Test]
    public async Task SupplierAdminOnlyFilter_Allows_SupplierAdmin_Role()
    {
        var filter = new SupplierAdminOnlyAttribute();
        var ctx = BuildContext(BuildPrincipal(SupplierAdminUserId, "SupplierAdmin"),
            "/Admin/Suppliers");

        filter.OnAuthorization(ctx);

        Assert.That(ctx.Result, Is.Null,
            "FR-007: SupplierAdmin role MUST pass [SupplierAdminOnly].");
        await Task.CompletedTask;
    }

    [Test]
    public void SupplierAdminOnlyFilter_Allows_Admin_Role()
    {
        var filter = new SupplierAdminOnlyAttribute();
        var ctx = BuildContext(BuildPrincipal(AdminUserId, "Admin"),
            "/Admin/Suppliers");

        filter.OnAuthorization(ctx);

        Assert.That(ctx.Result, Is.Null,
            "FR-007: Admin role MUST pass [SupplierAdminOnly] (dual-role passthrough).");
    }

    [Test]
    public void SupplierAdminOnlyFilter_Rejects_Other_Authenticated_Role()
    {
        var filter = new SupplierAdminOnlyAttribute();
        var ctx = BuildContext(BuildPrincipal("reviewer-1", "Reviewer"),
            "/Admin/Suppliers");

        filter.OnAuthorization(ctx);

        Assert.That(ctx.Result, Is.InstanceOf<ViewResult>());
        var view = (ViewResult)ctx.Result!;
        Assert.That(view.ViewName, Is.EqualTo("Error403"));
        Assert.That(view.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task SupplierAdminDeniedFilter_Allows_Admin_Without_AuditRow()
    {
        var dbName = $"sad-admin-allow-{Guid.NewGuid():N}";
        await using var db = CreateContext(dbName);
        var writer = new AdminAuditEventWriter(db);
        var filter = new SupplierAdminDeniedFilter(writer, db);

        var ctx = BuildContext(BuildPrincipal(AdminUserId, "Admin"),
            "/Admin/Users");

        await filter.OnAuthorizationAsync(ctx);

        Assert.That(ctx.Result, Is.Null, "Admin role MUST pass [SupplierAdminDenied].");
        var auditRows = await db.AdminAuditEvents.ToListAsync();
        Assert.That(auditRows, Is.Empty,
            "Admin pass-through MUST NOT write an audit row (audit-events.md invariants).");
    }

    [Test]
    public async Task SupplierAdminDeniedFilter_Denies_SupplierAdmin_And_Writes_AuditRow()
    {
        var dbName = $"sad-deny-{Guid.NewGuid():N}";
        await using var db = CreateContext(dbName);
        var writer = new AdminAuditEventWriter(db);
        var filter = new SupplierAdminDeniedFilter(writer, db);

        var ctx = BuildContext(BuildPrincipal(SupplierAdminUserId, "SupplierAdmin"),
            "/Admin/Users",
            "GET");

        await filter.OnAuthorizationAsync(ctx);

        Assert.That(ctx.Result, Is.InstanceOf<ViewResult>());
        var view = (ViewResult)ctx.Result!;
        Assert.That(view.ViewName, Is.EqualTo("Error403"));
        Assert.That(view.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));

        var auditRows = await db.AdminAuditEvents.ToListAsync();
        Assert.That(auditRows.Count, Is.EqualTo(1),
            "FR-007: SupplierAdminDeniedAccess MUST write exactly one audit row.");

        var row = auditRows[0];
        Assert.That(row.Action, Is.EqualTo(AdminAuditEvent.SupplierAdminDeniedAccess));
        Assert.That(row.ActorUserId, Is.EqualTo(SupplierAdminUserId));
        Assert.That(row.TargetType, Is.EqualTo(AdminAuditEvent.TargetTypeAdminRoute));
        Assert.That(row.PayloadJson, Is.Not.Null);
        Assert.That(row.PayloadJson!, Does.Contain("\"route\""));
        Assert.That(row.PayloadJson!, Does.Contain("/Admin/Users"));
        Assert.That(row.PayloadJson!, Does.Contain("\"method\""));
        Assert.That(row.PayloadJson!, Does.Contain("GET"));
        Assert.That(row.PayloadJson!, Does.Contain("\"userAgent\""));
    }

    [Test]
    public async Task SupplierAdminDeniedFilter_Allows_DualRole_AdminAndSupplierAdmin()
    {
        // Dual-role: Admin + SupplierAdmin. Admin role wins — no deny, no audit row.
        var dbName = $"sad-dual-{Guid.NewGuid():N}";
        await using var db = CreateContext(dbName);
        var writer = new AdminAuditEventWriter(db);
        var filter = new SupplierAdminDeniedFilter(writer, db);

        var ctx = BuildContext(BuildPrincipal("dual-1", "Admin", "SupplierAdmin"),
            "/Admin/Processes");

        await filter.OnAuthorizationAsync(ctx);

        Assert.That(ctx.Result, Is.Null,
            "Dual-role Admin+SupplierAdmin MUST pass through (admin role wins).");
        var auditRows = await db.AdminAuditEvents.ToListAsync();
        Assert.That(auditRows, Is.Empty);
    }

    [TestCase("/Admin")]
    [TestCase("/Admin/Currencies")]
    [TestCase("/Admin/ExchangeRates")]
    [TestCase("/Admin/Groups")]
    [TestCase("/Admin/LegacyQuotations")]
    [TestCase("/Admin/Plantillas")]
    [TestCase("/Admin/Processes")]
    [TestCase("/Admin/Reports")]
    [TestCase("/Admin/Users")]
    public async Task SupplierAdminDeniedFilter_DeniesEveryAdminRouteMatrix(string route)
    {
        var dbName = $"sad-matrix-{Guid.NewGuid():N}";
        await using var db = CreateContext(dbName);
        var writer = new AdminAuditEventWriter(db);
        var filter = new SupplierAdminDeniedFilter(writer, db);

        var ctx = BuildContext(BuildPrincipal(SupplierAdminUserId, "SupplierAdmin"),
            route);

        await filter.OnAuthorizationAsync(ctx);

        Assert.That(ctx.Result, Is.InstanceOf<ViewResult>(),
            $"FR-007: SupplierAdmin MUST be denied on {route}.");
        var view = (ViewResult)ctx.Result!;
        Assert.That(view.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));

        var auditRows = await db.AdminAuditEvents.ToListAsync();
        Assert.That(auditRows.Count, Is.EqualTo(1),
            $"FR-007: SupplierAdminDeniedAccess audit row MUST be written on {route}.");
        Assert.That(auditRows[0].PayloadJson!, Does.Contain(route));
    }
}
