using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Application.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 032 / US2 — DB-backed coverage for the admin-assigned, unique applicant
/// User Code. SCOPE: EF InMemory (mirrors <see cref="UserAdministrationGroupsTests"/>).
/// The service-level uniqueness pre-check is exercised here; the DB filtered
/// unique index race + the schema constraint are E2E-only (the in-memory
/// provider does not enforce the index).
/// </summary>
[TestFixture]
public class UserAdministrationUserCodeTests
{
    private const string ActorAdminId = "actor-admin-1";

    private static (UserAdministrationService service, AppDbContext ctx, IServiceProvider sp) Build()
    {
        var dbName = $"useradmin-usercode-{Guid.NewGuid():N}";
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
        foreach (var r in new[] { "Applicant", "Reviewer", "SupplierAdmin", "Admin" })
        {
            if (!await roleMgr.RoleExistsAsync(r))
            {
                await roleMgr.CreateAsync(new IdentityRole(r));
            }
        }
    }

    private static async Task<int[]> SeedGroupsAsync(AppDbContext ctx, params string[] names)
    {
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

    private static CreateUserRequest Applicant(string email, string legalId, int[] groups, string? userCode) =>
        new("F", "L", email, null, "Applicant", "Test1!", legalId, GroupIds: groups,
            IdentificationType: null, UserCode: userCode);

    [Test]
    public async Task Create_Applicant_WithUserCode_PersistsTrimmedCode()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var groups = await SeedGroupsAsync(ctx, "Norte");

        var result = await sut.CreateUserAsync(
            Applicant("app1@test.com", "APP-1", groups, "  UC-1  "),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
        var applicant = await ctx.Applicants.SingleAsync(a => a.UserId == result.Value!.Id);
        Assert.That(applicant.UserCode, Is.EqualTo("UC-1"));
        Assert.That(result.Value!.UserCode, Is.EqualTo("UC-1"), "Detail DTO must surface the code.");
    }

    [Test]
    public async Task Create_Applicant_DuplicateUserCode_IsRejected()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var groups = await SeedGroupsAsync(ctx, "Norte");

        var first = await sut.CreateUserAsync(
            Applicant("a@test.com", "APP-A", groups, "DUP"),
            ActorAdminId, CancellationToken.None);
        Assert.That(first.Succeeded, Is.True);

        var second = await sut.CreateUserAsync(
            Applicant("b@test.com", "APP-B", groups, "DUP"),
            ActorAdminId, CancellationToken.None);

        Assert.That(second.Succeeded, Is.False);
        Assert.That(second.Errors.Any(e => e.Code == "USER_CODE_IN_USE"), Is.True);
        // The rejected create must not leave an orphan account.
        Assert.That(await ctx.Users.AnyAsync(u => u.Email == "b@test.com"), Is.False);
    }

    [Test]
    public async Task Create_Applicants_WithNoUserCode_DoNotCollide()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var groups = await SeedGroupsAsync(ctx, "Norte");

        var first = await sut.CreateUserAsync(
            Applicant("n1@test.com", "APP-N1", groups, "   "),
            ActorAdminId, CancellationToken.None);
        var second = await sut.CreateUserAsync(
            Applicant("n2@test.com", "APP-N2", groups, null),
            ActorAdminId, CancellationToken.None);

        Assert.That(first.Succeeded, Is.True, string.Join("; ", first.Errors.Select(e => e.Message)));
        Assert.That(second.Succeeded, Is.True, "Multiple code-less applicants must not collide.");
        Assert.That((await ctx.Applicants.SingleAsync(a => a.UserId == first.Value!.Id)).UserCode, Is.Null);
        Assert.That((await ctx.Applicants.SingleAsync(a => a.UserId == second.Value!.Id)).UserCode, Is.Null);
    }

    [Test]
    public async Task Update_Applicant_ToAnotherApplicantsCode_IsRejected()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var groups = await SeedGroupsAsync(ctx, "Norte");

        var a = await sut.CreateUserAsync(Applicant("a@test.com", "APP-A", groups, "CODE-A"), ActorAdminId, CancellationToken.None);
        var b = await sut.CreateUserAsync(Applicant("b@test.com", "APP-B", groups, "CODE-B"), ActorAdminId, CancellationToken.None);
        Assert.That(a.Succeeded && b.Succeeded, Is.True);

        var freshB = await sut.GetUserAsync(b.Value!.Id, CancellationToken.None);
        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(b.Value!.Id, "F", "L", "b@test.com", null, "Applicant", "APP-B",
                GroupIds: groups, ConcurrencyStamp: freshB!.ConcurrencyStamp,
                IdentificationType: null, UserCode: "CODE-A"),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.False);
        Assert.That(update.Errors.Any(e => e.Code == "USER_CODE_IN_USE"), Is.True);
    }

    [Test]
    public async Task Update_Applicant_KeepingOwnCode_Succeeds()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var groups = await SeedGroupsAsync(ctx, "Norte");

        var a = await sut.CreateUserAsync(Applicant("a@test.com", "APP-A", groups, "CODE-A"), ActorAdminId, CancellationToken.None);
        Assert.That(a.Succeeded, Is.True);

        var fresh = await sut.GetUserAsync(a.Value!.Id, CancellationToken.None);
        var update = await sut.UpdateUserAsync(
            new UpdateUserRequest(a.Value!.Id, "Fx", "L", "a@test.com", null, "Applicant", "APP-A",
                GroupIds: groups, ConcurrencyStamp: fresh!.ConcurrencyStamp,
                IdentificationType: null, UserCode: "CODE-A"),
            ActorAdminId, CancellationToken.None);

        Assert.That(update.Succeeded, Is.True, string.Join("; ", update.Errors.Select(e => e.Message)));
        Assert.That((await ctx.Applicants.SingleAsync(x => x.UserId == a.Value!.Id)).UserCode, Is.EqualTo("CODE-A"));
    }

    [Test]
    public async Task Create_NonApplicant_DoesNotRequireOrStoreUserCode()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        var groups = await SeedGroupsAsync(ctx, "Norte");

        // A crafted payload sets a UserCode on a Reviewer; there is no Applicant
        // row, so the code is simply ignored and no uniqueness check applies.
        var result = await sut.CreateUserAsync(
            new CreateUserRequest("F", "L", "rev@test.com", null, "Reviewer", "Test1!", null,
                GroupIds: groups, IdentificationType: null, UserCode: "WHATEVER"),
            ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.That(await ctx.Applicants.AnyAsync(a => a.UserId == result.Value!.Id), Is.False);
        Assert.That(result.Value!.UserCode, Is.Null);
    }
}
