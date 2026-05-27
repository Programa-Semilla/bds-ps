using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 028 / T007 — regression guard for the foundational refactor (T003 event-aware
/// <c>CtaRouteTemplate</c> on <c>Binding</c>, T005 actor exclusion in the resolver).
/// The seven shipped spec-021 events MUST resolve the exact same recipient buckets as
/// before. Because legacy outbox rows carry a null <c>ActorUserId</c>, the new
/// actor-exclusion filter is a no-op for them — proven below alongside an active-filter
/// case. (CTA-URL preservation is covered by <c>RazorEmailCtaUrlTests</c>.)
/// </summary>
[TestFixture]
public class Spec021RegressionAfterCtaAndActorRefactorTests
{
    private sealed class Scenario : IAsyncDisposable
    {
        public required AppDbContext Ctx { get; init; }
        public required int AppId { get; init; }
        public required string ApplicantUserId { get; init; }
        public required string ReviewerUserId { get; init; }
        public required string AdminUserId { get; init; }
        public required int GroupId { get; init; }

        public ValueTask DisposeAsync() => Ctx.DisposeAsync();
    }

    private static UserManager<ApplicationUser> FakeUserManager() =>
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

    private static async Task<Scenario> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"reg-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new AppDbContext(options);

        ctx.Roles.Add(new IdentityRole("Reviewer") { NormalizedName = "REVIEWER" });
        ctx.Roles.Add(new IdentityRole("Admin") { NormalizedName = "ADMIN" });

        var uniq = Guid.NewGuid().ToString("N");
        var applicantUser = new ApplicationUser
        {
            Id = $"app-{uniq}",
            UserName = $"applicant-{uniq}@test.local",
            Email = $"applicant-{uniq}@test.local",
            FirstName = "Test",
            LastName = "Applicant",
        };
        var reviewerUser = new ApplicationUser
        {
            Id = $"rev-{uniq}",
            UserName = $"reviewer-{uniq}@test.local",
            Email = $"reviewer-{uniq}@test.local",
            FirstName = "Rita",
            LastName = "Reviewer",
        };
        var adminUser = new ApplicationUser
        {
            Id = $"adm-{uniq}",
            UserName = $"admin-{uniq}@test.local",
            Email = $"admin-{uniq}@test.local",
            FirstName = "Ada",
            LastName = "Admin",
        };
        ctx.Users.AddRange(applicantUser, reviewerUser, adminUser);
        await ctx.SaveChangesAsync();

        var reviewerRole = await ctx.Roles.SingleAsync(r => r.NormalizedName == "REVIEWER");
        var adminRole = await ctx.Roles.SingleAsync(r => r.NormalizedName == "ADMIN");
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = reviewerUser.Id, RoleId = reviewerRole.Id });
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = adminUser.Id, RoleId = adminRole.Id });

        var applicant = new Applicant(
            userId: applicantUser.Id,
            legalId: "1-1111-2222",
            firstName: "Test",
            lastName: "Applicant",
            email: applicantUser.Email!,
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var group = Group.Create("G-reg", processId: 1);
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        // Spec 016 — both applicant and reviewer hold the group membership; the
        // resolver excludes the applicant from the reviewer query by UserId.
        ctx.UserGroupMemberships.Add(new UserGroupMembership(applicantUser.Id, group.Id));
        ctx.UserGroupMemberships.Add(new UserGroupMembership(reviewerUser.Id, group.Id));
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "Reg-Co");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        // Admin authored a VersionHistory row → qualifies as a participating admin.
        app.AddVersionHistory(new VersionHistory(adminUser.Id, "ReviewItem", null));
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        return new Scenario
        {
            Ctx = ctx,
            AppId = app.Id,
            ApplicantUserId = applicantUser.Id,
            ReviewerUserId = reviewerUser.Id,
            AdminUserId = adminUser.Id,
            GroupId = group.Id,
        };
    }

    public static IEnumerable<TestCaseData> EventBucketCases()
    {
        yield return new TestCaseData(
            NotificationEvent.ApplicationSubmittedReviewer,
            new[] { RecipientBucket.Reviewer, RecipientBucket.Admin });
        yield return new TestCaseData(
            NotificationEvent.ApplicationSubmittedApplicant,
            new[] { RecipientBucket.Applicant });
        yield return new TestCaseData(
            NotificationEvent.ReturnedToApplicant,
            new[] { RecipientBucket.Applicant, RecipientBucket.Admin });
        yield return new TestCaseData(
            NotificationEvent.ResubmittedByApplicant,
            new[] { RecipientBucket.Reviewer, RecipientBucket.Admin });
        yield return new TestCaseData(
            NotificationEvent.ApplicationApproved,
            new[] { RecipientBucket.Applicant, RecipientBucket.Admin });
        yield return new TestCaseData(
            NotificationEvent.ApplicationRejected,
            new[] { RecipientBucket.Applicant, RecipientBucket.Admin });
        yield return new TestCaseData(
            NotificationEvent.WithdrawnByApplicant,
            new[] { RecipientBucket.Reviewer, RecipientBucket.Admin });
    }

    [TestCaseSource(nameof(EventBucketCases))]
    public async Task Spec021_event_resolves_original_buckets(
        NotificationEvent ev, RecipientBucket[] expectedBuckets)
    {
        await using var s = await SeedAsync();
        var resolver = new NotificationRecipientResolver(
            s.Ctx, FakeUserManager(), new ParticipatingAdminPredicate(s.Ctx));

        // Legacy-shaped payload: ActorUserId omitted (null) — actor exclusion is a no-op.
        var payload = new NotificationPayload(
            s.AppId, s.ApplicantUserId, "Test Applicant",
            new[] { s.GroupId }, OutcomeCode: null);
        var resolveContext = new NotificationOutboxResolveContext(
            OutboxId: 1, EventType: ev, ApplicationId: s.AppId, VersionHistoryId: 1, Payload: payload);

        var recipients = await resolver.ResolveAsync(resolveContext, CancellationToken.None);

        var buckets = recipients.Select(r => r.Bucket).Distinct().ToArray();
        Assert.That(buckets, Is.EquivalentTo(expectedBuckets),
            $"{ev.ToStorageString()} bucket composition must be unchanged after the spec-028 refactor.");
    }

    [Test]
    public async Task Actor_user_is_dropped_from_recipients()
    {
        await using var s = await SeedAsync();
        var resolver = new NotificationRecipientResolver(
            s.Ctx, FakeUserManager(), new ParticipatingAdminPredicate(s.Ctx));

        // The reviewer is the actor → excluded; the admin remains.
        var payload = new NotificationPayload(
            s.AppId, s.ApplicantUserId, "Test Applicant",
            new[] { s.GroupId }, OutcomeCode: null, ActorUserId: s.ReviewerUserId);
        var resolveContext = new NotificationOutboxResolveContext(
            OutboxId: 1, EventType: NotificationEvent.ApplicationSubmittedReviewer,
            ApplicationId: s.AppId, VersionHistoryId: 1, Payload: payload);

        var recipients = await resolver.ResolveAsync(resolveContext, CancellationToken.None);

        var userIds = recipients.Select(r => r.UserId).ToArray();
        Assert.That(userIds, Does.Not.Contain(s.ReviewerUserId), "the actor must be excluded (FR-013a).");
        Assert.That(userIds, Does.Contain(s.AdminUserId), "non-actor recipients are unaffected.");
    }

    [Test]
    public async Task Null_actor_excludes_nobody()
    {
        await using var s = await SeedAsync();
        var resolver = new NotificationRecipientResolver(
            s.Ctx, FakeUserManager(), new ParticipatingAdminPredicate(s.Ctx));

        var payload = new NotificationPayload(
            s.AppId, s.ApplicantUserId, "Test Applicant",
            new[] { s.GroupId }, OutcomeCode: null, ActorUserId: null);
        var resolveContext = new NotificationOutboxResolveContext(
            OutboxId: 1, EventType: NotificationEvent.ApplicationSubmittedReviewer,
            ApplicationId: s.AppId, VersionHistoryId: 1, Payload: payload);

        var recipients = await resolver.ResolveAsync(resolveContext, CancellationToken.None);

        var userIds = recipients.Select(r => r.UserId).ToArray();
        Assert.That(userIds, Does.Contain(s.ReviewerUserId));
        Assert.That(userIds, Does.Contain(s.AdminUserId));
    }
}
