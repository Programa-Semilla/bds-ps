using FundingPlatform.Application.Errors;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 037 / US2 — <see cref="CompanyAdministrationService"/> add / rename (+no-op) /
/// archive / unarchive with the last-active floor (FR-008), per-applicant active-name
/// uniqueness pre-check (D3), unarchive-collision block, and audit rows. Uses the real
/// EF stack (InMemory) so the service + audit-writer paths are exercised end-to-end.
/// </summary>
[TestFixture]
public class CompanyAdministrationTests
{
    private const string Actor = "admin-user-1";

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static CompanyAdministrationService NewService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx));

    private static async Task<int> SeedApplicantAsync(AppDbContext ctx)
    {
        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: $"1-{Guid.NewGuid().GetHashCode() & 0x7fffffff}",
            firstName: "Ana",
            lastName: "Pérez",
            email: $"a-{Guid.NewGuid():N}@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();
        return applicant.Id;
    }

    [Test]
    public async Task Add_PersistsActiveCompany_AndWritesAuditRow()
    {
        var db = $"co-add-{Guid.NewGuid():N}";
        int applicantId;
        using (var ctx = CreateContext(db))
        {
            applicantId = await SeedApplicantAsync(ctx);
            var result = await NewService(ctx).AddAsync(applicantId, "  Acme S.A.  ", Actor, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Company!.Name, Is.EqualTo("Acme S.A."));
        }

        using (var ctx = CreateContext(db))
        {
            var company = await ctx.Companies.FirstOrDefaultAsync(c => c.ApplicantId == applicantId);
            Assert.That(company, Is.Not.Null);
            Assert.That(company!.IsActive, Is.True);
            var audit = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.ActionCompanyCreate && a.TargetType == AdminAuditEvent.TargetTypeCompany)
                .ToListAsync();
            Assert.That(audit, Has.Count.EqualTo(1));
            Assert.That(audit[0].ActorUserId, Is.EqualTo(Actor));
        }
    }

    [Test]
    public async Task Add_DuplicateActiveName_AccentInsensitive_IsRejected()
    {
        var db = $"co-dup-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        await svc.AddAsync(applicantId, "Construcción S.A.", Actor, CancellationToken.None);

        var result = await svc.AddAsync(applicantId, "construccion s.a.", Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(UserFacingErrorCode.CompanyNameDuplicate));
    }

    [Test]
    public async Task Rename_EqualAfterTrim_IsNoOp_WithoutAudit()
    {
        var db = $"co-rename-noop-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        var added = await svc.AddAsync(applicantId, "Same Name", Actor, CancellationToken.None);

        var result = await svc.RenameAsync(added.Company!.Id, "  Same Name  ", Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        var renameAudits = await ctx.AdminAuditEvents
            .Where(a => a.Action == AdminAuditEvent.ActionCompanyRename)
            .CountAsync();
        Assert.That(renameAudits, Is.EqualTo(0), "An equal-after-trim rename writes no audit.");
    }

    [Test]
    public async Task Rename_ChangesName_AndWritesAudit()
    {
        var db = $"co-rename-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        var added = await svc.AddAsync(applicantId, "Old Name", Actor, CancellationToken.None);

        var result = await svc.RenameAsync(added.Company!.Id, "New Name", Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Company!.Name, Is.EqualTo("New Name"));
        var renameAudit = await ctx.AdminAuditEvents.SingleAsync(a => a.Action == AdminAuditEvent.ActionCompanyRename);
        Assert.That(renameAudit.ActorUserId, Is.EqualTo(Actor));
        Assert.That(renameAudit.TargetType, Is.EqualTo(AdminAuditEvent.TargetTypeCompany));
    }

    [Test]
    public async Task Archive_LastActiveCompany_IsRejected()
    {
        var db = $"co-floor-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        var only = await svc.AddAsync(applicantId, "Única", Actor, CancellationToken.None);

        var result = await svc.ArchiveAsync(only.Company!.Id, Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(UserFacingErrorCode.CompanyArchiveLastActive));
        // FR-008 — the company MUST remain active after a rejected archive.
        var reloaded = await ctx.Companies.AsNoTracking().FirstAsync(c => c.Id == only.Company!.Id);
        Assert.That(reloaded.IsActive, Is.True);
        Assert.That(await ctx.AdminAuditEvents.AnyAsync(a => a.Action == AdminAuditEvent.ActionCompanyArchive), Is.False);
    }

    [Test]
    public async Task Archive_NonLast_Succeeds_AndWritesAudit()
    {
        var db = $"co-archive-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        var first = await svc.AddAsync(applicantId, "Primera", Actor, CancellationToken.None);
        await svc.AddAsync(applicantId, "Segunda", Actor, CancellationToken.None);

        var result = await svc.ArchiveAsync(first.Company!.Id, Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Company!.IsArchived, Is.True);
        var archiveAudit = await ctx.AdminAuditEvents.SingleAsync(a => a.Action == AdminAuditEvent.ActionCompanyArchive);
        Assert.That(archiveAudit.ActorUserId, Is.EqualTo(Actor));
        // The DB row is actually archived (the guarded ExecuteUpdate fired).
        var reloaded = await ctx.Companies.AsNoTracking().FirstAsync(c => c.Id == first.Company!.Id);
        Assert.That(reloaded.IsActive, Is.False);
    }

    [Test]
    public async Task Unarchive_NameCollision_IsRejected()
    {
        var db = $"co-unarchive-collide-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        var a = await svc.AddAsync(applicantId, "Repetida", Actor, CancellationToken.None);
        await svc.AddAsync(applicantId, "Otra", Actor, CancellationToken.None); // keeps the floor when we archive 'Repetida'
        await svc.ArchiveAsync(a.Company!.Id, Actor, CancellationToken.None);
        // Now an active company takes the same name; unarchiving the archived one collides.
        await svc.AddAsync(applicantId, "Repetida", Actor, CancellationToken.None);

        var result = await svc.UnarchiveAsync(a.Company!.Id, Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(UserFacingErrorCode.CompanyUnarchiveNameCollision));
    }

    [Test]
    public async Task Unarchive_NoCollision_Succeeds()
    {
        var db = $"co-unarchive-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var applicantId = await SeedApplicantAsync(ctx);
        var svc = NewService(ctx);
        var a = await svc.AddAsync(applicantId, "Reactivable", Actor, CancellationToken.None);
        await svc.AddAsync(applicantId, "Activa", Actor, CancellationToken.None);
        await svc.ArchiveAsync(a.Company!.Id, Actor, CancellationToken.None);

        var result = await svc.UnarchiveAsync(a.Company!.Id, Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Company!.IsArchived, Is.False);
        Assert.That(await ctx.AdminAuditEvents.CountAsync(a => a.Action == AdminAuditEvent.ActionCompanyUnarchive), Is.EqualTo(1));
    }
}
