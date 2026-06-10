using FundingPlatform.Application.Funds;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Services;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Funds;

/// <summary>
/// Spec 029 / US1 (T036) + US4 (T049) — FundService CRUD/lifecycle/regulation
/// persistence + audit, and the archived-Fund freeze read filter against a DB.
/// </summary>
[TestFixture]
public class FundServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static FundService NewService(AppDbContext ctx) =>
        new(ctx, new InMemoryObjectStorage(), new AdminAuditEventWriter(ctx));

    private const string Actor = "admin-user-1";

    [Test]
    public async Task Create_PersistsActiveFund_AndWritesAuditRow()
    {
        var db = $"fund-create-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
        {
            id = await NewService(ctx).CreateAsync(
                new CreateFundCommand("Fondo General", "Descripción.", Regulation: null), Actor, CancellationToken.None);
        }

        using (var ctx = CreateContext(db))
        {
            var fund = await ctx.Funds.FirstOrDefaultAsync(f => f.Id == id);
            Assert.That(fund, Is.Not.Null);
            Assert.That(fund!.Status, Is.EqualTo(FundStatus.Active));
            Assert.That(fund.HasRegulation, Is.False);

            var audit = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.ActionFundCreate && a.TargetType == AdminAuditEvent.TargetTypeFund)
                .ToListAsync();
            Assert.That(audit, Has.Count.EqualTo(1));
            Assert.That(audit[0].ActorUserId, Is.EqualTo(Actor));
        }
    }

    [Test]
    public async Task Create_DuplicateName_Throws()
    {
        var db = $"fund-dup-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var svc = NewService(ctx);
        await svc.CreateAsync(new CreateFundCommand("Fondo X", "d", null), Actor, CancellationToken.None);

        Assert.ThrowsAsync<DuplicateFundNameException>(() =>
            svc.CreateAsync(new CreateFundCommand("Fondo X", "d2", null), Actor, CancellationToken.None));
    }

    [Test]
    public async Task Archive_Then_Reactivate_PersistsStatus_AndAudits()
    {
        var db = $"fund-life-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
            id = await NewService(ctx).CreateAsync(new CreateFundCommand("Fondo L", "d", null), Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
            await NewService(ctx).ArchiveAsync(id, Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
            Assert.That((await ctx.Funds.FindAsync(id))!.Status, Is.EqualTo(FundStatus.Archived));

        using (var ctx = CreateContext(db))
            await NewService(ctx).ReactivateAsync(id, Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
        {
            Assert.That((await ctx.Funds.FindAsync(id))!.Status, Is.EqualTo(FundStatus.Active));
            var actions = await ctx.AdminAuditEvents.Select(a => a.Action).ToListAsync();
            Assert.That(actions, Does.Contain(AdminAuditEvent.ActionFundArchive));
            Assert.That(actions, Does.Contain(AdminAuditEvent.ActionFundReactivate));
        }
    }

    [Test]
    public async Task SetRegulation_Then_RemoveRegulation_SetsThenClearsColumns()
    {
        var db = $"fund-reg-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
            id = await NewService(ctx).CreateAsync(new CreateFundCommand("Fondo R", "d", null), Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            using var pdf = new MemoryStream("%PDF-1.4 body"u8.ToArray());
            await NewService(ctx).SetRegulationAsync(
                new SetFundRegulationCommand(id, new FundRegulationUpload(pdf, "reglamento.pdf", "application/pdf", 13)),
                Actor, CancellationToken.None);
        }
        using (var ctx = CreateContext(db))
        {
            var fund = await ctx.Funds.FindAsync(id);
            Assert.That(fund!.HasRegulation, Is.True);
            Assert.That(fund.RegulationFileName, Is.EqualTo("reglamento.pdf"));
            Assert.That(fund.RegulationContentType, Is.EqualTo("application/pdf"));
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx).RemoveRegulationAsync(id, Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
        {
            var fund = await ctx.Funds.FindAsync(id);
            Assert.That(fund!.HasRegulation, Is.False);
            Assert.That(fund.RegulationBlobKey, Is.Null);
            Assert.That(fund.RegulationFileName, Is.Null);
        }
    }

    [Test]
    public async Task ArchivedFund_Application_IsExcludedFromApplicantList_AndRestoredOnReactivate()
    {
        var db = $"fund-freeze-{Guid.NewGuid():N}";
        int fundId, applicantId, appId;

        using (var ctx = CreateContext(db))
        {
            var fund = Fund.Create("Fondo Congela", "d");
            ctx.Funds.Add(fund);
            await ctx.SaveChangesAsync();
            fundId = fund.Id;

            var process = Process.Create("Proceso", fund.Id);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();

            var group = Group.Create("Norte", process.Id);
            ctx.Groups.Add(group);
            await ctx.SaveChangesAsync();

            var applicant = new Applicant(
                userId: $"u-{Guid.NewGuid():N}", legalId: "L-1", firstName: "Ana", lastName: "P",
                email: "ana@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();
            applicantId = applicant.Id;

            var app = new AppEntity(applicant.Id, group.Id, "Empresa");
            app.AssignPublicCode(Helpers.TestPublicCodes.Next());
            ctx.Applications.Add(app);
            await ctx.SaveChangesAsync();
            appId = app.Id;
        }

        // Active Fund → the applicant sees their application.
        using (var ctx = CreateContext(db))
        {
            var repo = new ApplicationRepository(ctx, new ApplicationQueryFilter());
            var apps = await repo.GetByApplicantIdAsync(applicantId);
            Assert.That(apps.Select(a => a.Id), Does.Contain(appId), "Active-Fund app is visible.");
        }

        // Archive the Fund → the application drops off the applicant list (freeze).
        using (var ctx = CreateContext(db))
            await NewService(ctx).ArchiveAsync(fundId, Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
        {
            var repo = new ApplicationRepository(ctx, new ApplicationQueryFilter());
            var apps = await repo.GetByApplicantIdAsync(applicantId);
            Assert.That(apps.Select(a => a.Id), Does.Not.Contain(appId), "Archived-Fund app is hidden (FR-020).");
        }

        // Reactivate → it reappears.
        using (var ctx = CreateContext(db))
            await NewService(ctx).ReactivateAsync(fundId, Actor, CancellationToken.None);
        using (var ctx = CreateContext(db))
        {
            var repo = new ApplicationRepository(ctx, new ApplicationQueryFilter());
            var apps = await repo.GetByApplicantIdAsync(applicantId);
            Assert.That(apps.Select(a => a.Id), Does.Contain(appId), "Reactivated-Fund app is visible again.");
        }
    }
}
