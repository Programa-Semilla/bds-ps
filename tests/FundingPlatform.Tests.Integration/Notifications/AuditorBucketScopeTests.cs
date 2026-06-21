using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 040 / US2 (T037) / FR-018 — the SentToAuditAuditor event resolves to the
/// Auditor-role users who share the applicant's stage group (spec-016 group overlap),
/// and excludes auditors in other groups + the applicant + the acting reviewer.
/// </summary>
[TestFixture]
public class AuditorBucketScopeTests
{
    [Test]
    public async Task SentToAuditAuditor_ResolvesToInGroupAuditorsOnly_ExcludesOutOfGroupAndApplicant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"auditor-bucket-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var ctx = new AppDbContext(options);

        ctx.Roles.Add(new IdentityRole("Auditor") { NormalizedName = "AUDITOR" });
        ctx.Roles.Add(new IdentityRole("Applicant") { NormalizedName = "APPLICANT" });
        await ctx.SaveChangesAsync();
        var auditorRoleId = ctx.Roles.Single(r => r.NormalizedName == "AUDITOR").Id;
        var applicantRoleId = ctx.Roles.Single(r => r.NormalizedName == "APPLICANT").Id;

        var process = Domain.Entities.Process.Create("Crocus 2025", 1);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();
        var g1 = Domain.Entities.Group.Create("G1", process.Id);
        var g2 = Domain.Entities.Group.Create("G2", process.Id);
        ctx.Groups.AddRange(g1, g2);
        await ctx.SaveChangesAsync();

        Domain.Entities.ApplicationUser User(string prefix, string first) => new()
        {
            Id = $"{prefix}-{Guid.NewGuid():N}",
            UserName = $"{prefix}@test.local",
            Email = $"{prefix}@test.local",
            FirstName = first,
            LastName = "X",
        };

        var applicantUser = User("applicant", "Sol");
        var inGroupAuditor = User("aud-in", "Audit-In");
        var outGroupAuditor = User("aud-out", "Audit-Out");
        ctx.Users.AddRange(applicantUser, inGroupAuditor, outGroupAuditor);
        await ctx.SaveChangesAsync();

        ctx.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = applicantUser.Id, RoleId = applicantRoleId },
            new IdentityUserRole<string> { UserId = inGroupAuditor.Id, RoleId = auditorRoleId },
            new IdentityUserRole<string> { UserId = outGroupAuditor.Id, RoleId = auditorRoleId });
        ctx.UserGroupMemberships.AddRange(
            new Domain.Entities.UserGroupMembership(applicantUser.Id, g1.Id),
            new Domain.Entities.UserGroupMembership(inGroupAuditor.Id, g1.Id),
            new Domain.Entities.UserGroupMembership(outGroupAuditor.Id, g2.Id));
        await ctx.SaveChangesAsync();

        var applicant = new Domain.Entities.Applicant(
            applicantUser.Id, "1-0000-0001", "Sol", "X", applicantUser.Email!, null, null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();
        var app = new Domain.Entities.Application(applicant.Id, g1.Id, null, "ACME");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var resolver = new NotificationRecipientResolver(ctx, userManager, new ParticipatingAdminPredicate(ctx));

        var payload = new NotificationPayload(
            app.Id, applicantUser.Id, "Sol X",
            StageGroupIds: new[] { g1.Id }, OutcomeCode: null, ActorUserId: "reviewer-actor");

        var recipients = await resolver.ResolveAsync(
            new NotificationOutboxResolveContext(1, NotificationEvent.SentToAuditAuditor, app.Id, 1, payload),
            CancellationToken.None);
        var ids = recipients.Select(r => r.UserId).ToList();

        Assert.That(ids, Does.Contain(inGroupAuditor.Id), "In-group auditor must receive SentToAuditAuditor.");
        Assert.That(ids, Does.Not.Contain(outGroupAuditor.Id), "Out-of-group auditor must NOT receive it.");
        Assert.That(ids, Does.Not.Contain(applicantUser.Id), "Applicant must NOT receive it.");
    }
}
