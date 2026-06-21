using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.BackgroundServices;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.BackgroundServices;

/// <summary>
/// Spec 043 / US4 (T038) — <see cref="RegulatoryFreshnessDigestService.RunOnceAsync"/>:
/// stale providers on audit-pipeline applications produce one aggregated digest per
/// group-scoped auditor; no stale ⇒ no email; an app outside the auditor's group is excluded.
///
/// SCOPE: EF InMemory provider + a capturing email sender + a dump renderer (the branded
/// HTML render needs the Web view engine; asserted by the E2E mail-capture suite).
/// </summary>
[TestFixture]
public class RegulatoryFreshnessDigestTests
{
    private static ServiceProvider BuildProvider(string dbName, CapturingSender sender)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<IEmailViewRenderer, DumpRenderer>();
        services.AddSingleton<IEmailBaseUrlProvider>(new StubBaseUrlProvider("https://test.example"));
        services.AddScoped<RegulatoryDigestEmailFactory>();
        services.AddSingleton<IEmailSender>(sender);
        services.AddSingleton<IOptions<RegulatoryFreshnessOptions>>(
            Options.Create(new RegulatoryFreshnessOptions { FreshnessWindowDays = 30 }));
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static RegulatoryFreshnessDigestService NewService(ServiceProvider sp) =>
        new(sp, Options.Create(new HaciendaSyncOptions()), NullLogger<RegulatoryFreshnessDigestService>.Instance);

    private static void SetState(AppEntity app, ApplicationState state) =>
        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, state);

    private static void SetSelectedSupplier(Item item, int supplierId) =>
        typeof(Item).GetProperty(nameof(Item.SelectedSupplierId))!.SetValue(item, supplierId);

    /// <summary>Seeds a group with one Auditor member; returns (groupId, auditorEmail).</summary>
    private static async Task<(int GroupId, string AuditorEmail)> SeedGroupWithAuditorAsync(AppDbContext ctx, string tag)
    {
        var fund = Fund.Create($"Fondo {tag}", "d"); ctx.Funds.Add(fund); await ctx.SaveChangesAsync();
        var process = Process.Create($"Proceso {tag}", fund.Id); ctx.Processes.Add(process); await ctx.SaveChangesAsync();
        var group = Group.Create($"Grupo {tag}", process.Id); ctx.Groups.Add(group); await ctx.SaveChangesAsync();

        var role = await ctx.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "AUDITOR");
        if (role is null) { role = new IdentityRole("Auditor") { NormalizedName = "AUDITOR" }; ctx.Roles.Add(role); }
        var auditor = new ApplicationUser($"aud_{tag}@example.com", "Aud", tag, phone: null);
        ctx.Users.Add(auditor);
        await ctx.SaveChangesAsync();
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = auditor.Id, RoleId = role.Id });
        ctx.UserGroupMemberships.Add(new UserGroupMembership(auditor.Id, group.Id));
        await ctx.SaveChangesAsync();
        return (group.Id, auditor.Email!);
    }

    private static async Task<int> SeedAuditPipelineAppAsync(
        AppDbContext ctx, int groupId, Supplier selectedSupplier)
    {
        var applicant = new Applicant($"u-{Guid.NewGuid():N}", $"L-{Guid.NewGuid():N}"[..10],
            "First", "Last", "app@example.com", null, null);
        ctx.Applicants.Add(applicant);
        ctx.Suppliers.Add(selectedSupplier);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, groupId, null, "Empresa");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var item = new Item("Producto", 1);
        app.AddItem(item);
        SetState(app, ApplicationState.PendingAudit);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        SetSelectedSupplier(item, selectedSupplier.Id);
        await ctx.SaveChangesAsync();
        return app.Id;
    }

    private static Supplier MakeSupplier(string tag) => Supplier.CreateDraft(
        $"3-101-{Random.Shared.Next(100000, 999999)}", $"Proveedor {tag}", 1, "Sede",
        null, null, null, null, null, null, null);

    [Test]
    public async Task StaleSupplier_SendsOneDigestToGroupAuditor()
    {
        var dbName = $"digest-stale-{Guid.NewGuid():N}";
        var sender = new CapturingSender();
        using var sp = BuildProvider(dbName, sender);
        int appId;
        string auditorEmail;
        using (var scope = sp.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (var groupId, auditorEmail) = await SeedGroupWithAuditorAsync(ctx, "norte");
            appId = await SeedAuditPipelineAppAsync(ctx, groupId, MakeSupplier("Stale")); // never-reviewed
        }

        var sent = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(sent, Is.EqualTo(1));
        Assert.That(sender.Sent, Has.Count.EqualTo(1));
        Assert.That(sender.Sent[0].ToEmail, Is.EqualTo(auditorEmail));
        Assert.That(sender.Sent[0].HtmlBody + sender.Sent[0].TextBody, Does.Contain("Proveedor Stale"));
        _ = appId;
    }

    [Test]
    public async Task AllFresh_NoEmail()
    {
        var dbName = $"digest-fresh-{Guid.NewGuid():N}";
        var sender = new CapturingSender();
        using var sp = BuildProvider(dbName, sender);
        using (var scope = sp.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (groupId, _) = await SeedGroupWithAuditorAsync(ctx, "fresca");
            var supplier = MakeSupplier("Fresca");
            supplier.ApplyRegulatoryEdit(HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-1));
            await SeedAuditPipelineAppAsync(ctx, groupId, supplier);
        }

        var sent = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(sent, Is.EqualTo(0));
        Assert.That(sender.Sent, Is.Empty);
    }

    [Test]
    public async Task AppOutsideAuditorGroup_NotIncluded()
    {
        var dbName = $"digest-scope-{Guid.NewGuid():N}";
        var sender = new CapturingSender();
        using var sp = BuildProvider(dbName, sender);
        using (var scope = sp.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Auditor belongs to group A; the stale app lives in group B (no auditor there).
            await SeedGroupWithAuditorAsync(ctx, "grupoA");
            var fund = Fund.Create("Fondo B", "d"); ctx.Funds.Add(fund); await ctx.SaveChangesAsync();
            var process = Process.Create("Proceso B", fund.Id); ctx.Processes.Add(process); await ctx.SaveChangesAsync();
            var groupB = Group.Create("Grupo B", process.Id); ctx.Groups.Add(groupB); await ctx.SaveChangesAsync();
            await SeedAuditPipelineAppAsync(ctx, groupB.Id, MakeSupplier("B"));
        }

        var sent = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(sent, Is.EqualTo(0), "the group-A auditor must not be notified about a group-B application");
        Assert.That(sender.Sent, Is.Empty);
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.FromResult(new EmailSendResult(EmailSendOutcome.Sent, "msg-id", null));
        }
    }

    private sealed class StubBaseUrlProvider(string baseUrl) : IEmailBaseUrlProvider
    {
        public string GetBaseUrl() => baseUrl;
    }

    private sealed class DumpRenderer : IEmailViewRenderer
    {
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct)
        {
            var m = (DirectEmailModel)model;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(m.Subject);
            sb.AppendLine(m.HeroTitle);
            foreach (var p in m.Paragraphs) sb.AppendLine(p);
            if (m.CardRows is not null)
                foreach (var row in m.CardRows) sb.AppendLine($"{row.Label}: {row.Value}");
            return Task.FromResult(sb.ToString());
        }
    }
}
