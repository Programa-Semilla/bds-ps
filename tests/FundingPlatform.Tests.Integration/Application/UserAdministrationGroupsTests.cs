using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 016 / Story 2 — DB-backed coverage for the user-create / user-edit
/// flows with the new GroupIds path. SCOPE LIMITATION: EF InMemory provider
/// (mirrors the rest of this project — see <c>CurrencyConfigServiceTests</c>).
/// The end-to-end SQL behaviour is exercised by the E2E suite (T038).
/// </summary>
[TestFixture]
public class UserAdministrationGroupsTests
{
    private const string ActorAdminId = "actor-admin-1";

    private static (UserAdministrationService service, AppDbContext ctx, IServiceProvider sp) Build()
    {
        var dbName = $"useradmin-groups-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 4;
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<UserAdministrationService>();
        services.AddHttpContextAccessor();

        var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<AppDbContext>();
        var sut = sp.GetRequiredService<UserAdministrationService>();
        return (sut, ctx, sp);
    }

    private static async Task SeedRolesAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        // Spec 021 / FR-007 — SupplierAdmin seeded so role-assignment tests can
        // exercise the standard admin Users form path (closes the impl gap that
        // forced dev-only `Account/AssignRole` use).
        foreach (var r in new[] { "Applicant", "Reviewer", "Auditor", "Admin" })
        {
            if (!await roleMgr.RoleExistsAsync(r))
            {
                await roleMgr.CreateAsync(new IdentityRole(r));
            }
        }
    }

    private static async Task<int[]> SeedGroupsAsync(AppDbContext ctx, params string[] names)
    {
        // Spec 021 / FR-001 — every Group belongs to exactly one Process.
        var process = Process.Create("Crocus 2025", 1);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();
        foreach (var n in names)
        {
            ctx.Groups.Add(Group.Create(n, process.Id));
        }
        await ctx.SaveChangesAsync();
        return ctx.Groups.OrderBy(g => g.Id).Select(g => g.Id).ToArray();
    }

    [Test]
    public async Task Create_Reviewer_WithZeroGroups_IsRejected()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        await SeedGroupsAsync(ctx, "Norte");

        var result = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "rev0@test.com", null, "Reviewer", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors.Any(e => e.Code == "AT_LEAST_ONE_GROUP"), Is.True);
    }

    [Test]
    public async Task Create_Reviewer_WithGroups_PersistsMembershipsAndAudit()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte", "Sur");

        var result = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "rev1@test.com", null, "Reviewer", null,
                GroupIds: ids),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == result.Value!.Id)
            .ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(2));

        var audit = await ctx.AdminAuditEvents
            .Where(e => e.Action == AdminAuditEvent.ActionUserMembershipsUpdate
                     && e.TargetId == result.Value!.Id)
            .SingleAsync();
        Assert.That(audit.PayloadJson, Does.Contain("\"added\""));
    }

    [Test]
    public async Task Create_Applicant_AdminRoleSilentlyDiscardsGroupIds()
    {
        // FR-009 edge case — if a crafted form payload sets role=Admin and posts
        // GroupIds, the service ignores them and creates the user with no
        // memberships, no validation error.
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte");

        var result = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "admin1@test.com", null, "Admin", null,
                GroupIds: ids),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == result.Value!.Id)
            .ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Update_Reviewer_DiffApplied_AndSingleAuditRow()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte", "Sur", "Centro");
        var (norte, sur, centro) = (ids[0], ids[1], ids[2]);

        var created = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "rev2@test.com", null, "Reviewer", null,
                GroupIds: new[] { norte, sur }),
            ActorAdminId, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True);
        var userId = created.Value!.Id;

        // Now edit: keep Norte, drop Sur, add Centro.
        var fresh = await sut.GetUserAsync(userId, CancellationToken.None);
        Assert.That(fresh, Is.Not.Null);
        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(userId, "F", "L", "rev2@test.com", null, "Reviewer", null,
                GroupIds: new[] { norte, centro },
                ConcurrencyStamp: fresh!.ConcurrencyStamp),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.True, string.Join("; ", update.Errors.Select(e => e.Message)));
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.That(memberships, Is.EquivalentTo(new[] { norte, centro }));

        var auditRows = await ctx.AdminAuditEvents
            .Where(e => e.Action == AdminAuditEvent.ActionUserMembershipsUpdate
                     && e.TargetId == userId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();
        // Two audit rows: one from create, one from update diff.
        Assert.That(auditRows, Has.Count.EqualTo(2));
        Assert.That(auditRows[1].PayloadJson, Does.Contain($"\"added\":[{centro}]"));
        Assert.That(auditRows[1].PayloadJson, Does.Contain($"\"removed\":[{sur}]"));
    }

    [Test]
    public async Task Update_PromoteToAdmin_ClearsAllMemberships()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte", "Sur");

        var created = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "rev3@test.com", null, "Reviewer", null,
                GroupIds: ids),
            ActorAdminId, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True);
        var userId = created.Value!.Id;

        var fresh = await sut.GetUserAsync(userId, CancellationToken.None);
        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(userId, "F", "L", "rev3@test.com", null, "Admin", null,
                GroupIds: ids, // crafted payload tries to keep memberships — must be ignored
                ConcurrencyStamp: fresh!.ConcurrencyStamp),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.True, string.Join("; ", update.Errors.Select(e => e.Message)));
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == userId)
            .ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(0), "FR-009: Admin must have zero memberships.");
    }

    [Test]
    public async Task Update_DemoteAdmin_ToReviewer_WithNoGroups_IsRejected()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        await SeedGroupsAsync(ctx, "Norte");

        // Need at least 2 admins so the demotion isn't blocked by LastAdminGuard.
        var first = await sut.CreateUserAsync(
            new CreateUserRequest("A1", "L", "admin-a@test.com", null, "Admin", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);
        Assert.That(first.Succeeded, Is.True);

        var second = await sut.CreateUserAsync(
            new CreateUserRequest("A2", "L", "admin-b@test.com", null, "Admin", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);
        Assert.That(second.Succeeded, Is.True);
        var userId = second.Value!.Id;

        var fresh = await sut.GetUserAsync(userId, CancellationToken.None);
        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(userId, "A2", "L", "admin-b@test.com", null, "Reviewer", null,
                GroupIds: Array.Empty<int>(),
                ConcurrencyStamp: fresh!.ConcurrencyStamp),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.False);
        Assert.That(update.Errors.Any(e => e.Code == "AT_LEAST_ONE_GROUP"), Is.True);
    }

    // ---------------------------------------------------------------------
    // Spec 021 / FR-007 — SupplierAdmin assignment via the standard admin
    // Users form. The role is global-scope (no Process/Group), so the service
    // MUST treat it like Admin for membership purposes: zero rows on create,
    // strip rows on update.
    // ---------------------------------------------------------------------

    [Test]
    public async Task Create_SupplierAdmin_WithoutGroups_Succeeds_AndPersistsNoMemberships()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);

        var result = await sut.CreateUserAsync(
            new CreateUserRequest("Sup", "Admin", "supadmin1@test.com", null, "Auditor", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == result.Value!.Id)
            .ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Create_Auditor_WithGroupIds_PersistsMemberships()
    {
        // Spec 040 / FR-017 (supersedes spec-021 FR-007 for this role) — the Auditor role
        // is now GROUP-SCOPED like Reviewer, so NormalizeGroupIdsForRole keeps its incoming
        // GroupIds instead of stripping them. Only Admin remains groupless.
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte");

        var result = await sut.CreateUserAsync(
            new CreateUserRequest("Sup", "Admin", "supadmin2@test.com", null, "Auditor", null,
                GroupIds: ids),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == result.Value!.Id)
            .ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(1),
            "Spec 040 FR-017: the Auditor role is group-scoped; its memberships are persisted.");
    }

    [Test]
    public async Task Update_Reviewer_To_Auditor_PreservesMemberships()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte", "Sur");

        var created = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "rev-to-sup@test.com", null, "Reviewer", null,
                GroupIds: ids),
            ActorAdminId, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True);
        var userId = created.Value!.Id;

        var fresh = await sut.GetUserAsync(userId, CancellationToken.None);
        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(userId, "F", "L", "rev-to-sup@test.com", null, "Auditor", null,
                GroupIds: ids,
                ConcurrencyStamp: fresh!.ConcurrencyStamp),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.True, string.Join("; ", update.Errors.Select(e => e.Message)));
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == userId)
            .ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(2),
            "Spec 040 FR-017: promoting Reviewer → Auditor preserves Process/Group memberships (both are group-scoped).");

        var refreshed = await sut.GetUserAsync(userId, CancellationToken.None);
        Assert.That(refreshed!.Role, Is.EqualTo("Auditor"));
    }

    [Test]
    public async Task ListUsers_DualRole_AdminWinsOverSupplierAdmin()
    {
        // Guards SelectPrimaryRole priority — Admin must win even when a dual-
        // role user also holds SupplierAdmin (parity with the AccountController
        // profile-screen rank).
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);

        var created = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "dual@test.com", null, "Admin", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True);
        var userId = created.Value!.Id;

        // Pile the SupplierAdmin role on top through Identity directly — the
        // admin form only assigns ONE role at a time, but the existing
        // /Account/AssignRole path can add multiple. The display must still
        // pick Admin.
        var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var target = await userMgr.FindByIdAsync(userId);
        Assert.That(target, Is.Not.Null);
        var addRes = await userMgr.AddToRoleAsync(target!, "Auditor");
        Assert.That(addRes.Succeeded, Is.True);

        var list = await sut.ListUsersAsync(new ListUsersRequest(null, null, null, 1, 20), CancellationToken.None);
        var row = list.Items.Single(i => i.Id == userId);
        Assert.That(row.Role, Is.EqualTo("Admin"));
    }

    [Test]
    public async Task ListUsers_RoleFilter_AcceptsSupplierAdmin()
    {
        // ListUsersAsync's AllowedRoles guard must now accept SupplierAdmin so
        // the Index filter dropdown's new option works.
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);

        var sup = await sut.CreateUserAsync(
            new CreateUserRequest("S", "A", "sup-filt@test.com", null, "Auditor", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);
        Assert.That(sup.Succeeded, Is.True);

        var other = await sut.CreateUserAsync(
            new CreateUserRequest("R", "B", "rev-filt@test.com", null, "Admin", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);
        Assert.That(other.Succeeded, Is.True);

        var filtered = await sut.ListUsersAsync(
            new ListUsersRequest("Auditor", null, null, 1, 20),
            CancellationToken.None);
        Assert.That(filtered.Items.Select(i => i.Id), Contains.Item(sup.Value!.Id));
        Assert.That(filtered.Items.Select(i => i.Id), Does.Not.Contain(other.Value!.Id));
    }

    [Test]
    public async Task Update_ConcurrencyStamp_Mismatch_ReportsConflict()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte");

        var created = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "concurrency@test.com", null, "Reviewer", null,
                GroupIds: ids),
            ActorAdminId, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True);
        var userId = created.Value!.Id;

        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(userId, "F", "L", "concurrency@test.com", null, "Reviewer", null,
                GroupIds: ids,
                ConcurrencyStamp: "stale-stamp-value-xxx"),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.False);
        Assert.That(update.Errors.Any(e => e.Code == "CONCURRENCY_CONFLICT"), Is.True);
    }
}
