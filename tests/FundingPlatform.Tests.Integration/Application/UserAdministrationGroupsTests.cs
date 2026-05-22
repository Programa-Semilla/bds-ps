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
        foreach (var r in new[] { "Applicant", "Reviewer", "Admin" })
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
        var process = Process.Create("Crocus 2025");
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
            new CreateUserRequest("F", "L", "rev0@test.com", null, "Reviewer", "Test1!", null,
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
            new CreateUserRequest("F", "L", "rev1@test.com", null, "Reviewer", "Test1!", null,
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
            new CreateUserRequest("F", "L", "admin1@test.com", null, "Admin", "Test1!", null,
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
            new CreateUserRequest("F", "L", "rev2@test.com", null, "Reviewer", "Test1!", null,
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
            new CreateUserRequest("F", "L", "rev3@test.com", null, "Reviewer", "Test1!", null,
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
            new CreateUserRequest("A1", "L", "admin-a@test.com", null, "Admin", "Test1!", null,
                GroupIds: Array.Empty<int>()),
            ActorAdminId, CancellationToken.None);
        Assert.That(first.Succeeded, Is.True);

        var second = await sut.CreateUserAsync(
            new CreateUserRequest("A2", "L", "admin-b@test.com", null, "Admin", "Test1!", null,
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

    [Test]
    public async Task Update_ConcurrencyStamp_Mismatch_ReportsConflict()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var ids = await SeedGroupsAsync(ctx, "Norte");

        var created = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "concurrency@test.com", null, "Reviewer", "Test1!", null,
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
