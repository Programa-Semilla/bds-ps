using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Audit;

/// <summary>
/// Spec 040 / T032 — DB-backed coverage of the auditor workflow: send-to-audit
/// transition + reviewer-checklist snapshot + SentToAuditAuditor enqueue; the full
/// audit→generate→confirm→release path with the re-pointed AgreementGeneratedApplicant
/// enqueue; the return path; and group-scoping of the auditor inbox projection.
/// SCOPE: EF InMemory provider (mirrors the rest of this project's service tests).
/// </summary>
[TestFixture]
public class AuditWorkflowServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AuditWorkflowService NewService(AppDbContext ctx) =>
        new(new ApplicationRepository(ctx),
            new ChecklistTemplateRepository(ctx),
            new NotificationOutboxWriter(ctx),
            ctx,
            NullLogger<AuditWorkflowService>.Instance);

    private static async Task<int> SeedActiveChecklistAsync(AppDbContext ctx, ChecklistStage stage)
    {
        var template = new ChecklistTemplate("Plantilla", null, stage, isActive: true, createdByUserId: "admin");
        template.AddItem("Verificación 1", 1, isRequired: true);
        ctx.ChecklistTemplates.Add(template);
        await ctx.SaveChangesAsync();
        return template.Items[0].Id;
    }

    private static async Task<int> SeedFinalizedApplicationAsync(AppDbContext ctx, int groupId, string emailBase)
    {
        var user = new ApplicationUser($"{emailBase}@example.com", "F", emailBase, null) { Id = Guid.NewGuid().ToString() };
        ctx.Users.Add(user);
        var applicant = new Applicant(user.Id, $"L-{emailBase}", "First", emailBase, $"{emailBase}@example.com", null, null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();
        ctx.UserGroupMemberships.Add(new UserGroupMembership(user.Id, groupId));
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, groupId, null, "Empresa");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var item = new Item("Producto", 1);
        app.AddItem(item);
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.Resolved);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        app.SubmitResponse(new Dictionary<int, ItemResponseDecision> { [item.Id] = ItemResponseDecision.Accept }, user.Id);
        await ctx.SaveChangesAsync();
        return app.Id;
    }

    private static async Task<(int groupId, int appId)> SeedGroupAndFinalizedAsync(AppDbContext ctx, string name)
    {
        var fund = Fund.Create($"Fondo {name}", "d");
        ctx.Funds.Add(fund); await ctx.SaveChangesAsync();
        var process = Process.Create($"Proceso {name}", fund.Id);
        ctx.Processes.Add(process); await ctx.SaveChangesAsync();
        var group = Group.Create(name, process.Id);
        ctx.Groups.Add(group); await ctx.SaveChangesAsync();
        var appId = await SeedFinalizedApplicationAsync(ctx, group.Id, name.ToLowerInvariant());
        return (group.Id, appId);
    }

    [Test]
    public async Task FullPath_Audit_Generate_Confirm_Release_TransitionsAndEnqueues()
    {
        var db = $"audit-full-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var (_, appId) = await SeedGroupAndFinalizedAsync(ctx, "Norte");
        var itemId = await SeedActiveChecklistAsync(ctx, ChecklistStage.Both);
        var svc = NewService(ctx);

        // Reviewer sends to audit.
        var send = await svc.SubmitReviewerChecklistAndSendToAuditAsync(
            appId, new[] { new ReviewerCheck(itemId, true) }, "reviewer-1", CancellationToken.None);
        Assert.That(send.Success, Is.True);

        var app = await ctx.Applications.FirstAsync(a => a.Id == appId);
        Assert.That(app.State, Is.EqualTo(ApplicationState.PendingAudit));
        Assert.That(await ctx.ApplicationChecklistResponses.CountAsync(
            r => r.ApplicationId == appId && r.Stage == ChecklistStage.Reviewer), Is.EqualTo(1));
        Assert.That(await ctx.NotificationOutbox.CountAsync(o => o.EventType == "SENT_TO_AUDIT_AUDITOR"), Is.EqualTo(1));

        // Auditor marks compliant + approves.
        Assert.That((await svc.SaveAuditChecklistAsync(
            appId, new[] { new AuditMark(itemId, true, null) }, "auditor-1", CancellationToken.None)).Success, Is.True);
        Assert.That(await svc.IsAuditChecklistCompleteAsync(appId, CancellationToken.None), Is.True);
        Assert.That((await svc.ApproveForAgreementAsync(appId, "auditor-1", CancellationToken.None)).Success, Is.True);

        // Simulate the PDF generation that the controller drives (domain directly).
        var forGen = await new ApplicationRepository(ctx).GetByIdWithResponseAndAppealsAsync(appId);
        forGen!.GenerateFundingAgreement("a.pdf", "application/pdf", 10, "/blob/a", "auditor-1");
        await ctx.SaveChangesAsync();

        Assert.That((await svc.ConfirmPdfAsync(appId, "auditor-1", CancellationToken.None)).Success, Is.True);
        Assert.That((await svc.ReleaseForSignatureAsync(appId, "auditor-1", CancellationToken.None)).Success, Is.True);

        var released = await new ApplicationRepository(ctx).GetByIdWithResponseAndAppealsAsync(appId);
        Assert.That(released!.State, Is.EqualTo(ApplicationState.ResponseFinalized));
        Assert.That(released.FundingAgreement!.AuditorConfirmedAtUtc, Is.Not.Null);
        Assert.That(await ctx.NotificationOutbox.CountAsync(o => o.EventType == "AGREEMENT_GENERATED_APPLICANT"), Is.EqualTo(1));
    }

    [Test]
    public async Task ReturnPath_MarksNonCompliant_TransitionsToReturnedFromAudit_AndEnqueues()
    {
        var db = $"audit-return-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var (_, appId) = await SeedGroupAndFinalizedAsync(ctx, "Sur");
        var itemId = await SeedActiveChecklistAsync(ctx, ChecklistStage.Both);
        var svc = NewService(ctx);

        await svc.SubmitReviewerChecklistAndSendToAuditAsync(
            appId, new[] { new ReviewerCheck(itemId, true) }, "reviewer-1", CancellationToken.None);
        await svc.SaveAuditChecklistAsync(
            appId, new[] { new AuditMark(itemId, false, "Falta documentación") }, "auditor-1", CancellationToken.None);

        var result = await svc.ReturnToReviewerAsync(appId, "auditor-1", CancellationToken.None);
        Assert.That(result.Success, Is.True);

        var app = await ctx.Applications.FirstAsync(a => a.Id == appId);
        Assert.That(app.State, Is.EqualTo(ApplicationState.ReturnedFromAudit));
        Assert.That(await ctx.NotificationOutbox.CountAsync(o => o.EventType == "RETURNED_TO_REVIEWER_FROM_AUDIT"), Is.EqualTo(1));
    }

    [Test]
    public async Task Inbox_IsGroupScoped_InGroupSees_OutOfGroupDoesNot()
    {
        var db = $"audit-inbox-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var itemId = await SeedActiveChecklistAsync(ctx, ChecklistStage.Both);
        var (norteId, norteApp) = await SeedGroupAndFinalizedAsync(ctx, "Norte");
        var (surId, surApp) = await SeedGroupAndFinalizedAsync(ctx, "Sur");
        var svc = NewService(ctx);

        await svc.SubmitReviewerChecklistAndSendToAuditAsync(norteApp, new[] { new ReviewerCheck(itemId, true) }, "r", CancellationToken.None);
        await svc.SubmitReviewerChecklistAndSendToAuditAsync(surApp, new[] { new ReviewerCheck(itemId, true) }, "r", CancellationToken.None);

        var projection = new FundingPlatform.Application.Services.AuditorQueueProjection(new ApplicationRepository(ctx));

        var norteRows = await projection.GetInboxAsync(
            new ReviewerScope(false, new[] { norteId }), null, 1, 50, CancellationToken.None);
        Assert.That(norteRows.Select(r => r.ApplicationId), Is.EquivalentTo(new[] { norteApp }));

        var adminRows = await projection.GetInboxAsync(ReviewerScope.Admin, null, 1, 50, CancellationToken.None);
        Assert.That(adminRows.Select(r => r.ApplicationId), Is.EquivalentTo(new[] { norteApp, surApp }));
    }
}
