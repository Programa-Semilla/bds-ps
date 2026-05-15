using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 016 / NFR-003 — membership changes MUST take effect on the next
/// request from the affected user, without requiring sign-out. The reviewer
/// scope is request-scoped (re-resolved fresh from the DB), so a membership
/// removal between request N and N+1 is reflected in the queue and in
/// detail-page authorization.
///
/// SCOPE LIMITATION: simulates two consecutive requests by re-creating the
/// <see cref="ReviewerScopeProvider"/> against the same in-memory DB. The
/// E2E suite covers the full HTTP path (no sign-out) end-to-end.
/// </summary>
[TestFixture]
public class ReviewerScopeNextRequestTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task Reviewer_LosesGroup_NextRequest_SeesEmptyQueue_AndDetail403()
    {
        var dbName = $"next-request-{Guid.NewGuid():N}";

        // Seed: one applicant + one reviewer both in group "Norte".
        Group norte;
        ApplicationUser reviewer;
        ApplicationUser applicantUser;
        Applicant applicant;
        AppEntity app;
        using (var ctx = CreateContext(dbName))
        {
            norte = Group.Create("Norte");
            ctx.Groups.Add(norte);
            await ctx.SaveChangesAsync();

            reviewer = new ApplicationUser("rev@test.com", "Rev", "Iewer", null) { Id = Guid.NewGuid().ToString() };
            applicantUser = new ApplicationUser("app@test.com", "App", "Licant", null) { Id = Guid.NewGuid().ToString() };
            ctx.Users.AddRange(reviewer, applicantUser);
            await ctx.SaveChangesAsync();

            applicant = new Applicant(applicantUser.Id, "L-100", "App", "Licant", "app@test.com", null, null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            ctx.UserGroupMemberships.Add(new UserGroupMembership(reviewer.Id, norte.Id));
            ctx.UserGroupMemberships.Add(new UserGroupMembership(applicantUser.Id, norte.Id));
            await ctx.SaveChangesAsync();

            app = new AppEntity(applicantId: applicant.Id, companyName: "Test Company");
            app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
            typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.Submitted);
            ctx.Applications.Add(app);
            await ctx.SaveChangesAsync();
        }

        // Request 1: reviewer queries the queue and sees the application.
        using (var ctx = CreateContext(dbName))
        {
            IReviewerScopeProvider provider = new ReviewerScopeProvider(ctx);
            var scope = await provider.GetForUserAsync(reviewer.Id, isAdmin: false, CancellationToken.None);
            var repo = new ApplicationRepository(ctx);
            var (items, _) = await repo.GetByStateForReviewerAsync(
                ApplicationState.Submitted,
                new ReviewerScopeHint(scope.IsAdmin, scope.GroupIds),
                1, 50);
            Assert.That(items, Has.Count.EqualTo(1), "Reviewer must see the application before group removal.");
            // Detail-page check
            Assert.That(await repo.ApplicantSharesAnyGroupAsync(app.Id, scope.GroupIds, CancellationToken.None),
                Is.True);
        }

        // Admin removes the reviewer's only group.
        using (var ctx = CreateContext(dbName))
        {
            var rows = await ctx.UserGroupMemberships
                .Where(m => m.UserId == reviewer.Id)
                .ToListAsync();
            ctx.UserGroupMemberships.RemoveRange(rows);
            await ctx.SaveChangesAsync();
        }

        // Request 2: reviewer queries again (no sign-out, no token refresh).
        using (var ctx = CreateContext(dbName))
        {
            IReviewerScopeProvider provider = new ReviewerScopeProvider(ctx);
            var scope = await provider.GetForUserAsync(reviewer.Id, isAdmin: false, CancellationToken.None);
            Assert.That(scope.IsAdmin, Is.False);
            Assert.That(scope.GroupIds, Is.Empty,
                "NFR-003: scope reflects the current DB state on the next request.");

            var repo = new ApplicationRepository(ctx);
            var (items, total) = await repo.GetByStateForReviewerAsync(
                ApplicationState.Submitted,
                new ReviewerScopeHint(scope.IsAdmin, scope.GroupIds),
                1, 50);
            Assert.That(total, Is.EqualTo(0));
            Assert.That(items, Is.Empty);

            Assert.That(await repo.ApplicantSharesAnyGroupAsync(app.Id, scope.GroupIds, CancellationToken.None),
                Is.False, "FR-012: detail-page authorization denies after group removal.");
        }
    }
}
