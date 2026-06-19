using FundingPlatform.Application.EvidenceInbox;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Tests.Integration.Helpers;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.EvidenceInbox;

/// <summary>
/// Spec 041 / US1 + US3 — EvidenceInboxProjection matrix over
/// State × Process.Status × group-overlap, plus soft-deleted and archived-fund
/// exclusions. Mirrors the FundsUsageEvidenceServiceTests InMemory pattern
/// (real-DB/SQL-translation coverage is in the E2E suite).
/// </summary>
[TestFixture]
public class EvidenceInboxQueryTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static EvidenceInboxProjection NewProjection(AppDbContext ctx) =>
        new(ctx, new ApplicationQueryFilter());

    /// <summary>
    /// Seeds Fund → Process → Group → Applicant(+membership) → Application and
    /// returns the application id. The applicant is given a membership in its own
    /// group so the spec-016 overlap predicate resolves; reviewers "share" the
    /// group by carrying its id in their scope.
    /// </summary>
    private static async Task<(int AppId, int GroupId)> SeedAsync(
        AppDbContext ctx,
        ApplicationState state,
        bool processClosed = false,
        bool fundArchived = false,
        bool softDeleted = false)
    {
        var fund = Fund.Create("Fondo 041", "desc");
        if (fundArchived) fund.Archive();
        ctx.Funds.Add(fund);
        await ctx.SaveChangesAsync();

        var process = Process.Create($"Proceso {Guid.NewGuid():N}", fund.Id);
        if (processClosed) process.Close();
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();

        var group = Group.Create($"Grupo {Guid.NewGuid():N}", process.Id);
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}", legalId: "L-1", firstName: "Ana", lastName: "Pérez",
            email: "ana@example.com", phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        ctx.UserGroupMemberships.Add(new UserGroupMembership(applicant.UserId, group.Id));

        var app = new AppEntity(applicant.Id, group.Id, null, companyName: "Empresa");
        app.AssignPublicCode(TestPublicCodes.Next());
        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, state);
        if (softDeleted) app.SoftDelete();
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        return (app.Id, group.Id);
    }

    private static IReviewerScope ScopeFor(params int[] groupIds) =>
        new ReviewerScope(false, groupIds);

    [Test]
    public async Task ExecutedActiveInScope_IsReturned()
    {
        using var ctx = CreateContext($"ei-hit-{Guid.NewGuid():N}");
        var (appId, groupId) = await SeedAsync(ctx, ApplicationState.AgreementExecuted);

        var rows = await NewProjection(ctx).GetForUserAsync(ScopeFor(groupId), CancellationToken.None);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ApplicationId, Is.EqualTo(appId));
        Assert.That(rows[0].ApplicationNumber, Is.EqualTo($"APP-{appId:D5}"));
        Assert.That(rows[0].ApplicantName, Is.EqualTo("Ana Pérez"));
    }

    [Test]
    public async Task NonExecutedState_IsExcluded()
    {
        using var ctx = CreateContext($"ei-state-{Guid.NewGuid():N}");
        var (_, groupId) = await SeedAsync(ctx, ApplicationState.ResponseFinalized);

        var rows = await NewProjection(ctx).GetForUserAsync(ScopeFor(groupId), CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task ClosedProcess_IsExcluded()
    {
        using var ctx = CreateContext($"ei-closed-{Guid.NewGuid():N}");
        var (_, groupId) = await SeedAsync(ctx, ApplicationState.AgreementExecuted, processClosed: true);

        var rows = await NewProjection(ctx).GetForUserAsync(ScopeFor(groupId), CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task ArchivedFund_IsExcluded()
    {
        using var ctx = CreateContext($"ei-arch-{Guid.NewGuid():N}");
        var (_, groupId) = await SeedAsync(ctx, ApplicationState.AgreementExecuted, fundArchived: true);

        var rows = await NewProjection(ctx).GetForUserAsync(ScopeFor(groupId), CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task SoftDeleted_IsExcluded()
    {
        using var ctx = CreateContext($"ei-del-{Guid.NewGuid():N}");
        var (_, groupId) = await SeedAsync(ctx, ApplicationState.AgreementExecuted, softDeleted: true);

        var rows = await NewProjection(ctx).GetForUserAsync(ScopeFor(groupId), CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task OutOfGroupReviewer_SeesNothing()
    {
        using var ctx = CreateContext($"ei-oog-{Guid.NewGuid():N}");
        var (_, groupId) = await SeedAsync(ctx, ApplicationState.AgreementExecuted);

        var rows = await NewProjection(ctx).GetForUserAsync(ScopeFor(groupId + 999), CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task ReviewerWithNoGroups_SeesEmpty()
    {
        using var ctx = CreateContext($"ei-empty-{Guid.NewGuid():N}");
        await SeedAsync(ctx, ApplicationState.AgreementExecuted);

        var rows = await NewProjection(ctx).GetForUserAsync(ReviewerScope.Empty, CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task Admin_SeesExecutedActive_RegardlessOfGroup()
    {
        using var ctx = CreateContext($"ei-admin-{Guid.NewGuid():N}");
        var (appId, _) = await SeedAsync(ctx, ApplicationState.AgreementExecuted);

        var rows = await NewProjection(ctx).GetForUserAsync(ReviewerScope.Admin, CancellationToken.None);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ApplicationId, Is.EqualTo(appId));
    }

    [Test]
    public async Task Admin_StillExcludesClosedProcess()
    {
        using var ctx = CreateContext($"ei-admin-closed-{Guid.NewGuid():N}");
        await SeedAsync(ctx, ApplicationState.AgreementExecuted, processClosed: true);

        var rows = await NewProjection(ctx).GetForUserAsync(ReviewerScope.Admin, CancellationToken.None);

        Assert.That(rows, Is.Empty);
    }
}
