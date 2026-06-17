using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Reviews;

/// <summary>
/// Spec 027 / US5 — DB-backed coverage of the two mechanisms the
/// <c>ReviewController.ApplicantCode</c> POST composes: the
/// <see cref="ApplicationRepository.ApplicantSharesAnyGroupAsync"/> authorization
/// predicate (spec 016) and the <see cref="UserManager{T}"/> write of
/// <c>ApplicationUser.CodigoPersonal</c>. No mocks — real Identity stores over a
/// real <see cref="AppDbContext"/>.
///
/// SCOPE: EF InMemory provider (mirrors the rest of this project). The full
/// controller composition + real SQL is exercised by the E2E suite (T021).
/// </summary>
[TestFixture]
public class ReviewerApplicantCodeTests
{
    private static (AppDbContext ctx, UserManager<ApplicationUser> users, IServiceProvider sp) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase($"applicant-code-{Guid.NewGuid():N}")
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

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp);
    }

    private static async Task<(int appId, int norteId, int surId, string applicantUserId)> SeedAsync(
        AppDbContext ctx, UserManager<ApplicationUser> users)
    {
        var fund = Fund.Create("Fondo de prueba", "Fondo de prueba para tests.");
        ctx.Funds.Add(fund);
        await ctx.SaveChangesAsync();
        var process = Process.Create("Crocus 2025", fund.Id);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();

        var norte = Group.Create("Norte", process.Id);
        var sur = Group.Create("Sur", process.Id);
        ctx.Groups.AddRange(norte, sur);
        await ctx.SaveChangesAsync();

        var applicantUser = new ApplicationUser("owner@example.com", "Ana", "Solicitante", null);
        var createResult = await users.CreateAsync(applicantUser, "Test1!");
        Assert.That(createResult.Succeeded, Is.True);

        var applicant = new Applicant(applicantUser.Id, "L-1", "Ana", "Solicitante", "owner@example.com", null, null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        // Applicant belongs to Norte.
        ctx.UserGroupMemberships.Add(new UserGroupMembership(applicantUser.Id, norte.Id));
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicantId: applicant.Id, norte.Id, null,companyName: "ACME");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.Submitted);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        return (app.Id, norte.Id, sur.Id, applicantUser.Id);
    }

    [Test]
    public async Task OverlappingReviewer_IsAuthorized_AndCanSetCode()
    {
        var (ctx, users, _) = Build();
        var (appId, norteId, surId, applicantUserId) = await SeedAsync(ctx, users);
        var repo = new ApplicationRepository(ctx);

        // A reviewer who shares the applicant's group (Norte) is authorized.
        var allowed = await repo.ApplicantSharesAnyGroupAsync(appId, new[] { norteId }, CancellationToken.None);
        Assert.That(allowed, Is.True);

        // Write the code through the same path the controller uses.
        var user = await users.FindByIdAsync(applicantUserId);
        Assert.That(user, Is.Not.Null);
        user!.CodigoPersonal = "COD-2026-001";
        var update = await users.UpdateAsync(user);
        Assert.That(update.Succeeded, Is.True);

        var reloaded = await users.FindByIdAsync(applicantUserId);
        Assert.That(reloaded!.CodigoPersonal, Is.EqualTo("COD-2026-001"));
    }

    [Test]
    public async Task NonOverlappingReviewer_IsRejected()
    {
        var (ctx, users, _) = Build();
        var (appId, _, surId, _) = await SeedAsync(ctx, users);
        var repo = new ApplicationRepository(ctx);

        // Reviewer only in Sur does not overlap the Norte applicant → rejected.
        var allowed = await repo.ApplicantSharesAnyGroupAsync(appId, new[] { surId }, CancellationToken.None);
        Assert.That(allowed, Is.False);

        // A reviewer with no group memberships is also rejected (fail-closed).
        var none = await repo.ApplicantSharesAnyGroupAsync(appId, Array.Empty<int>(), CancellationToken.None);
        Assert.That(none, Is.False);
    }
}
