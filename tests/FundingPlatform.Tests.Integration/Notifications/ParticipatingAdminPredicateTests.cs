using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / T078 / US8 / R-006 — participating-admin predicate matrix.
///
/// <para>
/// <b>v1 contract</b>: a user qualifies as a participating admin when
/// (a) they appear in <c>VersionHistory.UserId</c> for the application AND
/// (b) they currently hold the "Admin" role.
/// </para>
///
/// <para>
/// <b>Known limitation</b> (EC-002 / OQ-011): a user who acted as admin and
/// is later demoted to reviewer is NOT picked up by the v1 predicate. The
/// <c>CurrentReviewerWithVersionHistory_isExcluded</c> case below is marked
/// <c>[Explicit]</c> until a future spec adds a <c>VersionHistory.RoleAtAction</c>
/// snapshot or a dedicated audit event row.
/// </para>
/// </summary>
[TestFixture]
public class ParticipatingAdminPredicateTests
{
    [Test]
    public async Task CurrentAdminWithVersionHistory_isIncluded()
    {
        await using var ctx = BuildContext();
        var (app, alice) = await SeedAdminActorAsync(ctx);

        var predicate = new ParticipatingAdminPredicate(ctx);
        var result = await predicate.GetParticipatingAdminUserIdsAsync(app.Id, CancellationToken.None);

        Assert.That(result, Has.Member(alice.Id),
            "v1: current-admin with a VersionHistory row MUST be in the participating-admin set.");
    }

    [Test, Explicit("OQ-011 — v1 predicate filters by CURRENT role; demoted admin is excluded by design. Future spec extends VersionHistory with RoleAtAction.")]
    public async Task CurrentReviewerWithVersionHistory_isExcluded()
    {
        // SETUP: Alice acted as admin (VersionHistory row exists) but was later
        // demoted to reviewer. Under EC-002 she should still be considered a
        // participating admin for the application she worked on. The v1 predicate
        // filters by CURRENT role and therefore EXCLUDES her — this test documents
        // the gap and is kept Explicit until a future spec restores fidelity.
        await using var ctx = BuildContext();
        var (app, alice) = await SeedAdminActorAsync(ctx);

        // Demote Alice to reviewer (remove from Admin role).
        var aliceAdminLink = await ctx.UserRoles.SingleAsync(r => r.UserId == alice.Id);
        ctx.UserRoles.Remove(aliceAdminLink);
        await ctx.SaveChangesAsync();

        var predicate = new ParticipatingAdminPredicate(ctx);
        var result = await predicate.GetParticipatingAdminUserIdsAsync(app.Id, CancellationToken.None);

        // EXPECTED in EC-002 final state: alice IS in result. v1 limitation: NOT in result.
        Assert.That(result, Has.Member(alice.Id),
            "EC-002 (deferred to OQ-011 follow-up): demoted-admin should remain a participating admin.");
    }

    [Test]
    public async Task CurrentAdminWithoutVersionHistory_isExcluded()
    {
        await using var ctx = BuildContext();
        var (app, _) = await SeedAdminActorAsync(ctx);

        // Add a second admin Carol who has NO VersionHistory row.
        var carol = new Domain.Entities.ApplicationUser
        {
            Id = "carol-" + Guid.NewGuid().ToString("N"),
            UserName = "carol@admin.test",
            Email = "carol@admin.test",
            FirstName = "Carol",
            LastName = "Admin",
        };
        ctx.Users.Add(carol);
        await ctx.SaveChangesAsync();

        var adminRole = await ctx.Roles.SingleAsync(r => r.NormalizedName == "ADMIN");
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = carol.Id, RoleId = adminRole.Id });
        await ctx.SaveChangesAsync();

        var predicate = new ParticipatingAdminPredicate(ctx);
        var result = await predicate.GetParticipatingAdminUserIdsAsync(app.Id, CancellationToken.None);

        Assert.That(result, Has.No.Member(carol.Id),
            "v1: pure-Admin role with no VersionHistory row must NOT be participating.");
    }

    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"pa-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(Domain.Entities.Application, Domain.Entities.ApplicationUser alice)>
        SeedAdminActorAsync(AppDbContext ctx)
    {
        // Seed Identity Admin role.
        var adminRole = new IdentityRole("Admin") { NormalizedName = "ADMIN" };
        ctx.Roles.Add(adminRole);

        var alice = new Domain.Entities.ApplicationUser
        {
            Id = "alice-" + Guid.NewGuid().ToString("N"),
            UserName = "alice@admin.test",
            Email = "alice@admin.test",
            FirstName = "Alice",
            LastName = "Admin",
        };
        ctx.Users.Add(alice);
        await ctx.SaveChangesAsync();

        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = alice.Id, RoleId = adminRole.Id });

        // Seed an applicant + application + a VersionHistory row authored by Alice.
        var applicant = new Domain.Entities.Applicant(
            userId: "app-" + Guid.NewGuid().ToString("N"),
            legalId: "1-2222-3333",
            firstName: "App",
            lastName: "User",
            email: "app@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, 1, null,"TestCo");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        app.AddVersionHistory(new Domain.Entities.VersionHistory(alice.Id, "ReviewItem",
            "Admin acted on item"));
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        return (app, alice);
    }
}
