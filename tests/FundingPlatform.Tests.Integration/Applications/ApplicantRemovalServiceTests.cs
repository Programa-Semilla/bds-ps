using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Tests.Integration.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 021 / US9 / FR-035–FR-041 — DB-backed coverage for
/// <see cref="ApplicationService.RemoveByApplicantAsync"/>: a Draft delete and a
/// Submitted withdrawal enqueue NO reviewer outbox row; an UnderReview withdrawal
/// enqueues exactly one <c>APPLICATION_WITHDRAWN_BY_APPLICANT</c> row keyed to a
/// fresh "Withdrawn" VersionHistory id (distinct idempotency key). Ownership and
/// terminal-state guards reject without mutating.
///
/// SCOPE LIMITATION: EF InMemory + real ApplicationRepository + real
/// NotificationOutboxWriter (mirrors the project's service-test convention). The
/// resolver → recipient pool → smtp4dev delivery contract is the E2E suite's job.
/// </summary>
[TestFixture]
public class ApplicantRemovalServiceTests
{
    private const string WithdrawnEvent = "APPLICATION_WITHDRAWN_BY_APPLICANT";

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ApplicationService BuildService(AppDbContext ctx) =>
        new(
            applicationRepository: new ApplicationRepository(ctx),
            categoryRepository: null!,
            supplierRepository: null!,
            objectStorage: null!,
            impactTemplateRepository: null!,
            systemConfigurationRepository: null!,
            documentRepository: null!,
            supplierCatalogService: null!,
            conversionService: null!,
            outboxWriter: new NotificationOutboxWriter(ctx),
            txScope: null!,
            logger: NullLogger<ApplicationService>.Instance);

    private static async Task<(int appId, string userId)> SeedAppAsync(
        AppDbContext ctx, ApplicationState state, bool withMembership = false)
    {
        var u = new ApplicationUser("app@example.com", "F", "app", null) { Id = Guid.NewGuid().ToString() };
        ctx.Users.Add(u);
        var ap = new Applicant(u.Id, "L-1", "First", "app", "app@example.com", null, null);
        ctx.Applicants.Add(ap);
        await ctx.SaveChangesAsync();

        if (withMembership)
        {
            var process = Process.Create("Crocus 2025");
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();
            var g = Group.Create("Norte", process.Id);
            ctx.Groups.Add(g);
            await ctx.SaveChangesAsync();
            ctx.UserGroupMemberships.Add(new UserGroupMembership(u.Id, g.Id));
            await ctx.SaveChangesAsync();
        }

        var app = new AppEntity(ap.Id, "Test Company");
        app.AssignPublicCode(TestPublicCodes.Next());
        if (state != ApplicationState.Draft)
        {
            typeof(AppEntity).GetProperty("State")!.SetValue(app, state);
        }
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();
        return (app.Id, u.Id);
    }

    private static Task<List<NotificationOutbox>> OutboxRowsAsync(AppDbContext ctx, int appId) =>
        ctx.NotificationOutbox.Where(o => o.ApplicationId == appId).ToListAsync();

    [Test]
    public async Task UnderReview_Withdraw_EnqueuesSingleWithdrawalRow_KeyedToWithdrawnVersionHistory()
    {
        using var ctx = CreateContext($"rm-ur-{Guid.NewGuid():N}");
        var (appId, userId) = await SeedAppAsync(ctx, ApplicationState.UnderReview, withMembership: true);

        var result = await BuildService(ctx).RemoveByApplicantAsync(appId, userId, CancellationToken.None);

        Assert.That(result.Kind, Is.EqualTo(ApplicantRemovalKind.Withdrawn));
        Assert.That(result.Succeeded, Is.True);

        var app = await ctx.Applications.FirstAsync(a => a.Id == appId);
        Assert.That(app.IsDeleted, Is.True);

        var rows = await OutboxRowsAsync(ctx, appId);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].EventType, Is.EqualTo(WithdrawnEvent));

        var withdrawnVh = await ctx.VersionHistories
            .FirstAsync(v => v.ApplicationId == appId && v.Action == "Withdrawn");
        Assert.That(rows[0].VersionHistoryId, Is.EqualTo(withdrawnVh.Id),
            "idempotency key must reference the fresh Withdrawn VersionHistory row (FR-040)");
    }

    [Test]
    public async Task Submitted_Withdraw_SoftDeletes_WithNoReviewerOutboxRow()
    {
        using var ctx = CreateContext($"rm-sub-{Guid.NewGuid():N}");
        var (appId, userId) = await SeedAppAsync(ctx, ApplicationState.Submitted);

        var result = await BuildService(ctx).RemoveByApplicantAsync(appId, userId, CancellationToken.None);

        Assert.That(result.Kind, Is.EqualTo(ApplicantRemovalKind.Withdrawn));
        Assert.That((await ctx.Applications.FirstAsync(a => a.Id == appId)).IsDeleted, Is.True);
        Assert.That(await OutboxRowsAsync(ctx, appId), Is.Empty);
    }

    [Test]
    public async Task Draft_Delete_SoftDeletes_WithNoOutboxRow()
    {
        using var ctx = CreateContext($"rm-draft-{Guid.NewGuid():N}");
        var (appId, userId) = await SeedAppAsync(ctx, ApplicationState.Draft);

        var result = await BuildService(ctx).RemoveByApplicantAsync(appId, userId, CancellationToken.None);

        Assert.That(result.Kind, Is.EqualTo(ApplicantRemovalKind.DraftDeleted));
        Assert.That((await ctx.Applications.FirstAsync(a => a.Id == appId)).IsDeleted, Is.True);
        Assert.That(await OutboxRowsAsync(ctx, appId), Is.Empty);
    }

    [Test]
    public async Task OwnershipMismatch_ReturnsNotFound_AndDoesNotDelete()
    {
        using var ctx = CreateContext($"rm-own-{Guid.NewGuid():N}");
        var (appId, _) = await SeedAppAsync(ctx, ApplicationState.UnderReview);

        var result = await BuildService(ctx)
            .RemoveByApplicantAsync(appId, "a-different-user-id", CancellationToken.None);

        Assert.That(result.NotFound, Is.True);
        Assert.That(result.Succeeded, Is.False);
        Assert.That((await ctx.Applications.FirstAsync(a => a.Id == appId)).IsDeleted, Is.False);
        Assert.That(await OutboxRowsAsync(ctx, appId), Is.Empty);
    }

    [Test]
    public async Task TerminalState_ReturnsRejected_AndDoesNotDelete()
    {
        using var ctx = CreateContext($"rm-term-{Guid.NewGuid():N}");
        var (appId, userId) = await SeedAppAsync(ctx, ApplicationState.Resolved);

        var result = await BuildService(ctx).RemoveByApplicantAsync(appId, userId, CancellationToken.None);

        Assert.That(result.RejectedState, Is.True);
        Assert.That(result.Succeeded, Is.False);
        Assert.That((await ctx.Applications.FirstAsync(a => a.Id == appId)).IsDeleted, Is.False);
    }
}
