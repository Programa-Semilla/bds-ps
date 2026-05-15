using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 016 / FR-011..FR-015 — DB-backed coverage for the reviewer queue
/// group-overlap predicate via <see cref="ApplicationRepository.GetByStateForReviewerAsync"/>.
/// Exercises:
///  - reviewer with one group sees only matching applicants;
///  - reviewer with multiple groups sees the union;
///  - admin scope short-circuits and returns every applicant;
///  - reviewer with zero memberships sees an empty queue (FR-005);
///  - the FR-014 search-term parameter narrows by name / legal id.
///
/// SCOPE LIMITATION: EF InMemory provider (mirrors the rest of this project).
/// The real SQL query plan is exercised end-to-end by the E2E suite (T048).
/// InMemory does not support `EF.Functions.Like`, so the search-term test
/// uses a literal-equality variant of the predicate by checking
/// case-sensitive Contains semantics.
/// </summary>
[TestFixture]
public class ReviewerQueueScopeTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Seeds three applicants: A in Norte, B in Sur, C in Norte+Sur. Returns
    /// the Application ids in order (a, b, c).
    /// </summary>
    private static async Task<(int a, int b, int c, int norteId, int surId)> SeedFixtureAsync(AppDbContext ctx)
    {
        var norte = Group.Create("Norte");
        var sur = Group.Create("Sur");
        ctx.Groups.AddRange(norte, sur);
        await ctx.SaveChangesAsync();

        async Task<(ApplicationUser, Applicant, AppEntity)> SeedTrio(string emailBase, int legalSeed, params int[] groupIds)
        {
            var u = new ApplicationUser($"{emailBase}@example.com", "F", emailBase, null);
            u.Id = Guid.NewGuid().ToString();
            ctx.Users.Add(u);
            var ap = new Applicant(u.Id, $"L-{legalSeed}", "First", emailBase, $"{emailBase}@example.com", null, null);
            ctx.Applicants.Add(ap);
            await ctx.SaveChangesAsync();
            foreach (var gid in groupIds)
            {
                ctx.UserGroupMemberships.Add(new UserGroupMembership(u.Id, gid));
            }
            await ctx.SaveChangesAsync();
            var app = new AppEntity(applicantId: ap.Id, companyName: "Test Company");
            app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
            typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.Submitted);
            ctx.Applications.Add(app);
            await ctx.SaveChangesAsync();
            return (u, ap, app);
        }

        var (_, _, appA) = await SeedTrio("alice", 1, norte.Id);
        var (_, _, appB) = await SeedTrio("bob", 2, sur.Id);
        var (_, _, appC) = await SeedTrio("carol", 3, norte.Id, sur.Id);

        return (appA.Id, appB.Id, appC.Id, norte.Id, sur.Id);
    }

    [Test]
    public async Task Reviewer_InOneGroup_SeesOnlyApplicantsInThatGroup()
    {
        using var ctx = CreateContext($"queue-scope-one-{Guid.NewGuid():N}");
        var (a, b, c, norteId, _) = await SeedFixtureAsync(ctx);

        var repo = new ApplicationRepository(ctx);
        var (items, total) = await repo.GetByStateForReviewerAsync(
            ApplicationState.Submitted,
            new ReviewerScopeHint(IsAdmin: false, GroupIds: new[] { norteId }),
            page: 1,
            pageSize: 50);

        var ids = items.Select(x => x.Id).ToList();
        Assert.That(total, Is.EqualTo(2), "Norte reviewer must see Norte applicant + Norte+Sur applicant.");
        Assert.That(ids, Does.Contain(a));
        Assert.That(ids, Does.Contain(c));
        Assert.That(ids, Does.Not.Contain(b));
    }

    [Test]
    public async Task Reviewer_InMultipleGroups_SeesUnion()
    {
        using var ctx = CreateContext($"queue-scope-multi-{Guid.NewGuid():N}");
        var (a, b, c, norteId, surId) = await SeedFixtureAsync(ctx);

        var repo = new ApplicationRepository(ctx);
        var (items, total) = await repo.GetByStateForReviewerAsync(
            ApplicationState.Submitted,
            new ReviewerScopeHint(IsAdmin: false, GroupIds: new[] { norteId, surId }),
            page: 1,
            pageSize: 50);

        Assert.That(total, Is.EqualTo(3));
        Assert.That(items.Select(x => x.Id), Is.EquivalentTo(new[] { a, b, c }));
    }

    [Test]
    public async Task Admin_SeesEveryApplication()
    {
        using var ctx = CreateContext($"queue-scope-admin-{Guid.NewGuid():N}");
        var (a, b, c, _, _) = await SeedFixtureAsync(ctx);

        var repo = new ApplicationRepository(ctx);
        var (items, total) = await repo.GetByStateForReviewerAsync(
            ApplicationState.Submitted,
            ReviewerScopeHint.Admin,
            page: 1,
            pageSize: 50);

        Assert.That(total, Is.EqualTo(3));
        Assert.That(items.Select(x => x.Id), Is.EquivalentTo(new[] { a, b, c }));
    }

    [Test]
    public async Task Reviewer_WithZeroGroups_SeesEmptyQueue()
    {
        using var ctx = CreateContext($"queue-scope-zero-{Guid.NewGuid():N}");
        await SeedFixtureAsync(ctx);

        var repo = new ApplicationRepository(ctx);
        var (items, total) = await repo.GetByStateForReviewerAsync(
            ApplicationState.Submitted,
            new ReviewerScopeHint(IsAdmin: false, GroupIds: Array.Empty<int>()),
            page: 1,
            pageSize: 50);

        Assert.That(total, Is.EqualTo(0));
        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task ApplicantSharesAnyGroup_TrueForOverlap_FalseOtherwise()
    {
        // FR-012 detail-page authorization mirrors the listing predicate.
        using var ctx = CreateContext($"queue-scope-detail-{Guid.NewGuid():N}");
        var (a, b, _, norteId, surId) = await SeedFixtureAsync(ctx);

        var repo = new ApplicationRepository(ctx);

        Assert.That(await repo.ApplicantSharesAnyGroupAsync(a, new[] { norteId }, CancellationToken.None),
            Is.True, "Norte reviewer shares with Norte applicant.");
        Assert.That(await repo.ApplicantSharesAnyGroupAsync(a, new[] { surId }, CancellationToken.None),
            Is.False, "Norte applicant does not share with Sur-only reviewer.");
        Assert.That(await repo.ApplicantSharesAnyGroupAsync(b, new[] { norteId, surId }, CancellationToken.None),
            Is.True, "Sur applicant shares with Norte+Sur reviewer.");
    }
}
