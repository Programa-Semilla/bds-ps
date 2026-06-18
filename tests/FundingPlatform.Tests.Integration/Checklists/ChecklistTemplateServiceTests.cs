using FundingPlatform.Application.Checklists;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Checklists;

/// <summary>
/// Spec 040 / US4 (T051) — ChecklistTemplateService CRUD/activation + audit, the
/// one-active-per-stage rule, and FR-003 (editing items leaves recorded
/// ApplicationChecklistResponse snapshots unchanged). SCOPE: EF InMemory provider.
/// </summary>
[TestFixture]
public class ChecklistTemplateServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ChecklistTemplateService NewService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx));

    private const string Actor = "admin-1";

    [Test]
    public async Task Create_Activate_PersistsAndWritesAudit()
    {
        var db = $"checklist-create-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var svc = NewService(ctx);

        var id = await svc.CreateAsync(new CreateChecklistTemplateCommand(
            "Plantilla A", "desc", ChecklistStage.Auditor, Activate: true,
            new[] { new ChecklistItemInput("Verificación 1", true), new ChecklistItemInput("Verificación 2", false) }),
            Actor, CancellationToken.None);

        var template = await ctx.ChecklistTemplates.Include(t => t.Items).FirstAsync(t => t.Id == id);
        Assert.That(template.IsActive, Is.True);
        Assert.That(template.Items.Count, Is.EqualTo(2));
        Assert.That(await ctx.AdminAuditEvents.CountAsync(a => a.Action == AdminAuditEvent.ActionChecklistCreate), Is.EqualTo(1));
        Assert.That(await ctx.AdminAuditEvents.CountAsync(a => a.Action == AdminAuditEvent.ActionChecklistActivate), Is.EqualTo(1));
    }

    [Test]
    public async Task Activate_DeactivatesOtherActiveTemplateOfSameStage()
    {
        var db = $"checklist-oneactive-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var svc = NewService(ctx);

        var a = await svc.CreateAsync(new CreateChecklistTemplateCommand(
            "A", null, ChecklistStage.Reviewer, true, new[] { new ChecklistItemInput("x", true) }), Actor, CancellationToken.None);
        var b = await svc.CreateAsync(new CreateChecklistTemplateCommand(
            "B", null, ChecklistStage.Reviewer, false, new[] { new ChecklistItemInput("y", true) }), Actor, CancellationToken.None);

        await svc.ActivateAsync(b, Actor, CancellationToken.None);

        Assert.That((await ctx.ChecklistTemplates.FirstAsync(t => t.Id == a)).IsActive, Is.False);
        Assert.That((await ctx.ChecklistTemplates.FirstAsync(t => t.Id == b)).IsActive, Is.True);
    }

    [Test]
    public async Task GetActiveForStage_StageSpecificBeatsBoth()
    {
        var db = $"checklist-resolve-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var svc = NewService(ctx);

        await svc.CreateAsync(new CreateChecklistTemplateCommand(
            "Both", null, ChecklistStage.Both, true, new[] { new ChecklistItemInput("both-item", true) }), Actor, CancellationToken.None);
        var reviewerId = await svc.CreateAsync(new CreateChecklistTemplateCommand(
            "Rev", null, ChecklistStage.Reviewer, true, new[] { new ChecklistItemInput("rev-item", true) }), Actor, CancellationToken.None);

        var active = await svc.GetActiveForStageAsync(ChecklistStage.Reviewer, CancellationToken.None);
        Assert.That(active, Is.Not.Null);
        Assert.That(active!.Id, Is.EqualTo(reviewerId), "Stage-specific Reviewer template wins over the Both template.");

        var auditorActive = await svc.GetActiveForStageAsync(ChecklistStage.Auditor, CancellationToken.None);
        Assert.That(auditorActive!.Items.Single().Text, Is.EqualTo("both-item"), "Auditor falls back to the Both template.");
    }

    [Test]
    public async Task Edit_DeactivatesOldItems_LeavesRecordedResponsesUnchanged()
    {
        var db = $"checklist-fr003-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var svc = NewService(ctx);

        var id = await svc.CreateAsync(new CreateChecklistTemplateCommand(
            "T", null, ChecklistStage.Auditor, true, new[] { new ChecklistItemInput("Texto original", true) }),
            Actor, CancellationToken.None);
        var originalItem = await ctx.ChecklistTemplateItems.FirstAsync(i => i.ChecklistTemplateId == id);

        // A recorded response snapshots the original item text and references the item (NO ACTION FK).
        ctx.ApplicationChecklistResponses.Add(new ApplicationChecklistResponse(
            applicationId: 999, ChecklistStage.Auditor, originalItem.Id, "Texto original",
            ChecklistResponseStatus.Checked, null, "auditor-1"));
        await ctx.SaveChangesAsync();

        // Edit full-replaces the items.
        await svc.EditAsync(new EditChecklistTemplateCommand(
            id, "T", null, ChecklistStage.Auditor,
            new[] { new ChecklistItemInput("Texto NUEVO", true) }), Actor, CancellationToken.None);

        // The recorded response is unchanged (frozen snapshot + the original item still exists, now inactive).
        var response = await ctx.ApplicationChecklistResponses.FirstAsync(r => r.ApplicationId == 999);
        Assert.That(response.ItemTextSnapshot, Is.EqualTo("Texto original"));
        Assert.That(response.ChecklistTemplateItemId, Is.EqualTo(originalItem.Id));

        var oldItem = await ctx.ChecklistTemplateItems.FirstAsync(i => i.Id == originalItem.Id);
        Assert.That(oldItem.IsActive, Is.False, "Old item is deactivated, not hard-deleted (FR-003 / NO ACTION FK).");

        var active = await svc.GetActiveForStageAsync(ChecklistStage.Auditor, CancellationToken.None);
        Assert.That(active!.Items.Single().Text, Is.EqualTo("Texto NUEVO"));
    }
}
