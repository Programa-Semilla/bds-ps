using System.Reflection;
using FundingPlatform.Application.FundsUsageEvidence;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using FundingPlatform.Tests.Integration.AiComparison;
using FundingPlatform.Tests.Integration.Helpers;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.FundsUsageEvidence;

/// <summary>
/// Spec 036 / US1–US3 — FundsUsageEvidenceService list/upload/edit-note/delete
/// persistence + audit, mirroring the FundService integration pattern
/// (InMemory DB + InMemoryObjectStorage; real-DB coverage is in E2E).
/// </summary>
[TestFixture]
public class FundsUsageEvidenceServiceTests
{
    private const string Actor = "reviewer-1";

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static FundsUsageEvidenceService NewService(AppDbContext ctx, InMemoryObjectStorage storage) =>
        new(ctx, storage, new AdminAuditEventWriter(ctx));

    private static async Task<int> SeedApplicationAsync(AppDbContext ctx, ApplicationState state)
    {
        if (!await ctx.Users.AnyAsync(u => u.Id == Actor))
        {
            ctx.Users.Add(new ApplicationUser { Id = Actor, UserName = "rev", Email = "rev@x.test", FirstName = "Rita", LastName = "Vega" });
        }

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}", legalId: "L-1", firstName: "Ana", lastName: "P",
            email: "ana@example.com", phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, groupId: 1, companyName: "Empresa");
        app.AssignPublicCode(TestPublicCodes.Next());
        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!
            .SetValue(app, state);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();
        return app.Id;
    }

    private static UploadFundsUsageEvidenceCommand UploadCmd(int appId, string name = "evidence.pdf", string? note = null)
        => new(appId, name, "application/pdf", 13, new MemoryStream("%PDF-1.4 body"u8.ToArray()), note);

    // ---------------- US1 ----------------

    [Test]
    public async Task Upload_Then_List_Then_Download_HappyPath_AndAudits()
    {
        var db = $"fue-up-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();
        int appId, evidenceId;

        using (var ctx = CreateContext(db))
        {
            appId = await SeedApplicationAsync(ctx, ApplicationState.AgreementExecuted);
            evidenceId = await NewService(ctx, storage).UploadAsync(UploadCmd(appId, note: "comprobante"), Actor, CancellationToken.None);
        }

        using (var ctx = CreateContext(db))
        {
            var list = await NewService(ctx, storage).ListAsync(appId, CancellationToken.None);
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].OriginalFileName, Is.EqualTo("evidence.pdf"));
            Assert.That(list[0].Note, Is.EqualTo("comprobante"));
            Assert.That(list[0].UploadedByDisplayName, Is.EqualTo("Rita Vega"));

            var audit = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.FundsEvidenceUploaded
                            && a.TargetType == AdminAuditEvent.TargetTypeFundsEvidence)
                .ToListAsync();
            Assert.That(audit, Has.Count.EqualTo(1));
            Assert.That(audit[0].ActorUserId, Is.EqualTo(Actor));

            var download = await NewService(ctx, storage).OpenForDownloadAsync(evidenceId, CancellationToken.None);
            Assert.That(download, Is.Not.Null);
            Assert.That(download!.FileName, Is.EqualTo("evidence.pdf"));
        }
    }

    [Test]
    public async Task Upload_OnNonExecutedApplication_Throws_AndPersistsNoRow()
    {
        var db = $"fue-block-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedApplicationAsync(ctx, ApplicationState.ResponseFinalized);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewService(ctx, storage).UploadAsync(UploadCmd(appId), Actor, CancellationToken.None));

        Assert.That(await ctx.FundsUsageEvidence.AnyAsync(), Is.False);
    }

    // ---------------- US2 ----------------

    [Test]
    public async Task EditNote_SetEditClear_Persists_AndAudits()
    {
        var db = $"fue-note-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();
        int appId, evidenceId;

        using (var ctx = CreateContext(db))
        {
            appId = await SeedApplicationAsync(ctx, ApplicationState.AgreementExecuted);
            evidenceId = await NewService(ctx, storage).UploadAsync(UploadCmd(appId), Actor, CancellationToken.None);
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx, storage).EditNoteAsync(evidenceId, "primera nota", Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
            Assert.That((await ctx.FundsUsageEvidence.FindAsync(evidenceId))!.Note, Is.EqualTo("primera nota"));

        using (var ctx = CreateContext(db))
            await NewService(ctx, storage).EditNoteAsync(evidenceId, "  segunda  ", Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
            Assert.That((await ctx.FundsUsageEvidence.FindAsync(evidenceId))!.Note, Is.EqualTo("segunda"));

        using (var ctx = CreateContext(db))
            await NewService(ctx, storage).EditNoteAsync(evidenceId, "", Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
        {
            Assert.That((await ctx.FundsUsageEvidence.FindAsync(evidenceId))!.Note, Is.Null);
            var audits = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.FundsEvidenceNoteEdited).ToListAsync();
            Assert.That(audits, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task EditNote_Over250_Throws()
    {
        var db = $"fue-note-long-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();
        using var ctx = CreateContext(db);
        var appId = await SeedApplicationAsync(ctx, ApplicationState.AgreementExecuted);
        var evidenceId = await NewService(ctx, storage).UploadAsync(UploadCmd(appId), Actor, CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewService(ctx, storage).EditNoteAsync(evidenceId, new string('a', 251), Actor, CancellationToken.None));
    }

    // ---------------- US3 ----------------

    [Test]
    public async Task Delete_RemovesRowAndBlob_AndAudits_SecondDeleteNotFound()
    {
        var db = $"fue-del-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();
        int appId, evidenceId;
        string blobKey;

        using (var ctx = CreateContext(db))
        {
            appId = await SeedApplicationAsync(ctx, ApplicationState.AgreementExecuted);
            evidenceId = await NewService(ctx, storage).UploadAsync(UploadCmd(appId), Actor, CancellationToken.None);
            blobKey = (await ctx.FundsUsageEvidence.FindAsync(evidenceId))!.BlobKey;
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx, storage).DeleteAsync(evidenceId, Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            Assert.That(await ctx.FundsUsageEvidence.AnyAsync(e => e.Id == evidenceId), Is.False);
            Assert.That(await storage.ExistsAsync(
                FundingPlatform.Application.Abstractions.Storage.FileCategory.FundsUsageEvidence,
                FundingPlatform.Application.Abstractions.Storage.ObjectKey.Parse(blobKey), CancellationToken.None), Is.False);
            var audits = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.FundsEvidenceDeleted).ToListAsync();
            Assert.That(audits, Has.Count.EqualTo(1));
        }

        // Concurrent/second delete resolves to not-found (research D9 edge).
        using (var ctx = CreateContext(db))
            Assert.ThrowsAsync<KeyNotFoundException>(() =>
                NewService(ctx, storage).DeleteAsync(evidenceId, Actor, CancellationToken.None));
    }
}
