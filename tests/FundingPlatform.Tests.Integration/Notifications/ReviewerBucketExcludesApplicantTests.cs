using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / FR-007 / §Recipient Rules — regression coverage for the
/// applicant-also-in-reviewer-group case.
///
/// <para>Spec 016 groups contain BOTH applicants and reviewers — that's how
/// <c>ApplicationRepository.ApplicantSharesAnyGroupAsync</c> works (a reviewer
/// can see an application iff they share a group with the applicant). Without
/// an explicit applicant-exclusion filter on the reviewer-bucket query, the
/// applicant is returned alongside the real reviewers and ends up receiving
/// the <c>Nueva solicitud para revisar</c> email on their own submission.</para>
///
/// <para>Bug surfaced in local dev 2026-05-12: an applicant submitted and
/// received the reviewer-variant email in addition to the applicant-variant
/// confirmation. FR-012 intra-row dedup does not save this case because the
/// applicant only appears in the reviewer bucket on the <c>_REVIEWER</c>
/// outbox row (the <c>_APPLICANT</c> row is a separate dispatch).</para>
/// </summary>
[TestFixture]
public class ReviewerBucketExcludesApplicantTests
{
    [Test]
    public async Task ApplicationSubmittedReviewer_excludes_the_applicant_even_if_applicant_is_in_the_group()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"resolver-excl-applicant-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var ctx = new AppDbContext(options);

        // Seed: one group, one applicant user, one reviewer user. Both users
        // are members of the same group — modeling the spec 016 production
        // shape (the reviewer's group overlap with the applicant is the
        // visibility gate). Role tagging matters: the resolver filters the
        // reviewer bucket by the "Reviewer" ASP.NET Identity role.
        ctx.Roles.Add(new IdentityRole("Reviewer") { NormalizedName = "REVIEWER" });
        ctx.Roles.Add(new IdentityRole("Applicant") { NormalizedName = "APPLICANT" });
        await ctx.SaveChangesAsync();

        var reviewerRoleId = ctx.Roles.Single(r => r.NormalizedName == "REVIEWER").Id;
        var applicantRoleId = ctx.Roles.Single(r => r.NormalizedName == "APPLICANT").Id;

        var group = Domain.Entities.Group.Create("Reviewers G1");
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var applicantUser = new Domain.Entities.ApplicationUser
        {
            Id = "applicant-" + Guid.NewGuid().ToString("N"),
            UserName = "applicant@test.local",
            Email = "applicant@test.local",
            FirstName = "Solicitante",
            LastName = "Uno",
        };
        var reviewerUser = new Domain.Entities.ApplicationUser
        {
            Id = "reviewer-" + Guid.NewGuid().ToString("N"),
            UserName = "reviewer@test.local",
            Email = "reviewer@test.local",
            FirstName = "Revisor",
            LastName = "Uno",
        };
        ctx.Users.Add(applicantUser);
        ctx.Users.Add(reviewerUser);
        await ctx.SaveChangesAsync();

        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = applicantUser.Id, RoleId = applicantRoleId });
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = reviewerUser.Id, RoleId = reviewerRoleId });
        ctx.UserGroupMemberships.Add(new Domain.Entities.UserGroupMembership(applicantUser.Id, group.Id));
        ctx.UserGroupMemberships.Add(new Domain.Entities.UserGroupMembership(reviewerUser.Id, group.Id));
        await ctx.SaveChangesAsync();

        var applicant = new Domain.Entities.Applicant(
            userId: applicantUser.Id,
            legalId: "1-0000-0001",
            firstName: applicantUser.FirstName,
            lastName: applicantUser.LastName,
            email: applicantUser.Email!,
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, "ACME");
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var resolver = new NotificationRecipientResolver(
            ctx, userManager, new ParticipatingAdminPredicate(ctx));

        var payload = new NotificationPayload(
            ApplicationId: app.Id,
            ApplicantUserId: applicantUser.Id,
            ApplicantDisplayName: "Solicitante Uno",
            StageGroupIds: new[] { group.Id },
            OutcomeCode: null);

        var resolveContext = new NotificationOutboxResolveContext(
            OutboxId: 1,
            EventType: NotificationEvent.ApplicationSubmittedReviewer,
            ApplicationId: app.Id,
            VersionHistoryId: 1,
            Payload: payload);

        var recipients = await resolver.ResolveAsync(resolveContext, CancellationToken.None);

        Assert.That(recipients.Any(r => r.UserId == applicantUser.Id), Is.False,
            "FR-007 / §Recipient Rules — the applicant MUST NOT receive the "
            + "APPLICATION_SUBMITTED_REVIEWER variant on their own application, even "
            + "if they share the reviewer group via spec 016 UserGroupMemberships.");

        Assert.That(recipients.Any(r => r.UserId == reviewerUser.Id), Is.True,
            "The real reviewer MUST still receive the APPLICATION_SUBMITTED_REVIEWER variant.");
    }

    [Test]
    public async Task ApplicationSubmittedReviewer_excludes_other_applicants_in_the_same_group()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"resolver-excl-otherapplicants-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var ctx = new AppDbContext(options);

        // Seed roles (Reviewer is the gate for the reviewer bucket; Applicant
        // is present so the other applicants in the group are role-tagged
        // correctly, even though the resolver filters by Reviewer-positive).
        ctx.Roles.Add(new IdentityRole("Reviewer") { NormalizedName = "REVIEWER" });
        ctx.Roles.Add(new IdentityRole("Applicant") { NormalizedName = "APPLICANT" });
        await ctx.SaveChangesAsync();

        var reviewerRoleId = ctx.Roles.Single(r => r.NormalizedName == "REVIEWER").Id;
        var applicantRoleId = ctx.Roles.Single(r => r.NormalizedName == "APPLICANT").Id;

        // Group G1 contains: A1 (submitting applicant), A2 (other applicant),
        // A3 (other applicant), R1 (real reviewer). This mirrors the spec 016
        // production shape where applicants and reviewers share groups.
        var group = Domain.Entities.Group.Create("Reviewers G1");
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var a1 = new Domain.Entities.ApplicationUser
        {
            Id = "a1-" + Guid.NewGuid().ToString("N"),
            UserName = "a1@test.local", Email = "a1@test.local",
            FirstName = "A", LastName = "One",
        };
        var a2 = new Domain.Entities.ApplicationUser
        {
            Id = "a2-" + Guid.NewGuid().ToString("N"),
            UserName = "a2@test.local", Email = "a2@test.local",
            FirstName = "A", LastName = "Two",
        };
        var a3 = new Domain.Entities.ApplicationUser
        {
            Id = "a3-" + Guid.NewGuid().ToString("N"),
            UserName = "a3@test.local", Email = "a3@test.local",
            FirstName = "A", LastName = "Three",
        };
        var r1 = new Domain.Entities.ApplicationUser
        {
            Id = "r1-" + Guid.NewGuid().ToString("N"),
            UserName = "r1@test.local", Email = "r1@test.local",
            FirstName = "R", LastName = "One",
        };
        ctx.Users.AddRange(a1, a2, a3, r1);
        await ctx.SaveChangesAsync();

        // Role tagging — A1/A2/A3 are Applicants, R1 is a Reviewer.
        ctx.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = a1.Id, RoleId = applicantRoleId },
            new IdentityUserRole<string> { UserId = a2.Id, RoleId = applicantRoleId },
            new IdentityUserRole<string> { UserId = a3.Id, RoleId = applicantRoleId },
            new IdentityUserRole<string> { UserId = r1.Id, RoleId = reviewerRoleId });

        // Group memberships — all four users are in G1.
        ctx.UserGroupMemberships.AddRange(
            new Domain.Entities.UserGroupMembership(a1.Id, group.Id),
            new Domain.Entities.UserGroupMembership(a2.Id, group.Id),
            new Domain.Entities.UserGroupMembership(a3.Id, group.Id),
            new Domain.Entities.UserGroupMembership(r1.Id, group.Id));
        await ctx.SaveChangesAsync();

        var applicant = new Domain.Entities.Applicant(
            userId: a1.Id, legalId: "1-0000-0001",
            firstName: a1.FirstName, lastName: a1.LastName,
            email: a1.Email!, phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, "ACME");
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var resolver = new NotificationRecipientResolver(
            ctx, userManager, new ParticipatingAdminPredicate(ctx));

        var payload = new NotificationPayload(
            ApplicationId: app.Id,
            ApplicantUserId: a1.Id,
            ApplicantDisplayName: "A One",
            StageGroupIds: new[] { group.Id },
            OutcomeCode: null);

        var resolveContext = new NotificationOutboxResolveContext(
            OutboxId: 1,
            EventType: NotificationEvent.ApplicationSubmittedReviewer,
            ApplicationId: app.Id,
            VersionHistoryId: 1,
            Payload: payload);

        var recipients = await resolver.ResolveAsync(resolveContext, CancellationToken.None);

        var ids = recipients.Select(r => r.UserId).ToList();

        Assert.That(ids, Does.Not.Contain(a1.Id),
            "Submitting applicant A1 MUST NOT receive the reviewer variant on their own application.");
        Assert.That(ids, Does.Not.Contain(a2.Id),
            "Other applicant A2 (different applicant in same group) MUST NOT receive the "
            + "reviewer variant — they hold the Applicant role, not Reviewer, and would be "
            + "leaked data about A1's submission otherwise.");
        Assert.That(ids, Does.Not.Contain(a3.Id),
            "Other applicant A3 MUST NOT receive the reviewer variant.");
        Assert.That(ids, Does.Contain(r1.Id),
            "Real reviewer R1 (Reviewer role + group membership) MUST receive the reviewer variant.");
    }
}
