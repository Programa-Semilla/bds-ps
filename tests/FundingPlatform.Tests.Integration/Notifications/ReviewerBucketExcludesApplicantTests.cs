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
        // visibility gate).
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
}
