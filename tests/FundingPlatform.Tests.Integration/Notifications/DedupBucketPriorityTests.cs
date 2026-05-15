using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / T079 / US8 acceptance scenario 3 / FR-012 — a user qualifying
/// via Applicant AND Admin buckets gets ONE recipient row with
/// <c>Bucket=Applicant</c> and the applicant-variant template key.
/// </summary>
[TestFixture]
public class DedupBucketPriorityTests
{
    [Test]
    public async Task User_qualifying_as_applicant_and_admin_gets_one_applicant_row()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"dedup-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var ctx = new AppDbContext(options);

        // Seed Admin role + a user who is both the applicant AND a current Admin.
        ctx.Roles.Add(new IdentityRole("Admin") { NormalizedName = "ADMIN" });

        var dualUser = new Domain.Entities.ApplicationUser
        {
            Id = "dual-" + Guid.NewGuid().ToString("N"),
            UserName = "dual@test.local",
            Email = "dual@test.local",
            FirstName = "Dual",
            LastName = "Role",
        };
        ctx.Users.Add(dualUser);
        await ctx.SaveChangesAsync();

        var adminRole = await ctx.Roles.SingleAsync(r => r.NormalizedName == "ADMIN");
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = dualUser.Id, RoleId = adminRole.Id });

        var applicant = new Domain.Entities.Applicant(
            userId: dualUser.Id,                       // applicant linked to the same Identity user.
            legalId: "1-9999-9999",
            firstName: "Dual",
            lastName: "Role",
            email: dualUser.Email!,
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, "Dual-Co");
        // Add a VersionHistory row authored by the same user — qualifies them
        // for the participating-admin bucket simultaneously.
        app.AddVersionHistory(new Domain.Entities.VersionHistory(dualUser.Id, "ReviewItem", null));
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var resolver = new NotificationRecipientResolver(
            ctx, userManager, new ParticipatingAdminPredicate(ctx));

        // RETURNED_TO_APPLICANT includes BOTH applicant + admin buckets.
        var payload = new NotificationPayload(
            app.Id, dualUser.Id, "Dual Role", Array.Empty<int>(), null);
        var resolveContext = new NotificationOutboxResolveContext(
            OutboxId: 1,
            EventType: NotificationEvent.ReturnedToApplicant,
            ApplicationId: app.Id,
            VersionHistoryId: 1,
            Payload: payload);
        var recipients = await resolver.ResolveAsync(resolveContext, CancellationToken.None);

        Assert.That(recipients, Has.Count.EqualTo(1),
            "FR-012: dedup yields exactly one recipient per UserId.");
        Assert.That(recipients[0].Bucket, Is.EqualTo(RecipientBucket.Applicant),
            "FR-012 bucket priority: Applicant > Admin must keep the Applicant bucket.");
    }
}
