using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Suppliers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 038 (US4 / FR-021/022/024) — <see cref="ProviderCreatedNotifier"/> sends one
/// message per Auditor with the required body fields, and a sender failure is
/// swallowed (best-effort, never throws to the caller).
/// </summary>
[TestFixture]
public class ProviderCreatedNotifierTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<int> SeedSupplierAndAuditorsAsync(string dbName, int auditorCount)
    {
        using var ctx = CreateContext(dbName);
        var supplier = Supplier.CreateDraft("3-101-900900", "Proveedor Notif", 1, "Sede principal",
            null, null, null, null, null, null, null);
        ctx.Suppliers.Add(supplier);

        var role = new IdentityRole("Auditor") { NormalizedName = "AUDITOR" };
        ctx.Roles.Add(role);
        for (var i = 0; i < auditorCount; i++)
        {
            var u = new ApplicationUser($"aud{i}@example.com", $"Aud{i}", "Itor", phone: null);
            ctx.Users.Add(u);
            ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = u.Id, RoleId = role.Id });
        }
        await ctx.SaveChangesAsync();
        return supplier.Id;
    }

    private static IEmailBaseUrlProvider BaseUrl() => new StubBaseUrlProvider("https://test.example");

    private sealed class StubBaseUrlProvider(string baseUrl) : IEmailBaseUrlProvider
    {
        public string GetBaseUrl() => baseUrl;
    }

    [Test]
    public async Task NotifyAuditorsAsync_SendsOnePerAuditor_WithRequiredBody()
    {
        var dbName = $"notifier-multi-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAndAuditorsAsync(dbName, auditorCount: 3);
        var sender = new CapturingSender();

        using var ctx = CreateContext(dbName);
        var notifier = new ProviderCreatedNotifier(ctx, sender, new DumpRenderer(), BaseUrl(),
            NullLogger<ProviderCreatedNotifier>.Instance);

        await notifier.NotifyAuditorsAsync(supplierId, CancellationToken.None);

        Assert.That(sender.Sent, Has.Count.EqualTo(3), "one message per auditor");
        var msg = sender.Sent[0];
        Assert.That(msg.Subject, Does.Contain("Proveedor Notif"));
        var body = msg.HtmlBody + msg.TextBody;
        Assert.That(body, Does.Contain("Proveedor Notif"));
        Assert.That(body, Does.Contain("3-101-900900"));
        Assert.That(body, Does.Contain($"/Admin/Suppliers/{supplierId}"));
    }

    [Test]
    public async Task NotifyAuditorsAsync_SenderThrows_DoesNotPropagate()
    {
        var dbName = $"notifier-throw-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAndAuditorsAsync(dbName, auditorCount: 1);

        using var ctx = CreateContext(dbName);
        var notifier = new ProviderCreatedNotifier(ctx, new ThrowingSender(), new DumpRenderer(), BaseUrl(),
            NullLogger<ProviderCreatedNotifier>.Instance);

        // FR-024 — best-effort: must not throw to the caller.
        Assert.DoesNotThrowAsync(() => notifier.NotifyAuditorsAsync(supplierId, CancellationToken.None));
    }

    [Test]
    public async Task NotifyAuditorsAsync_NoAuditors_NoOp()
    {
        var dbName = $"notifier-none-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAndAuditorsAsync(dbName, auditorCount: 0);
        var sender = new CapturingSender();

        using var ctx = CreateContext(dbName);
        var notifier = new ProviderCreatedNotifier(ctx, sender, new DumpRenderer(), BaseUrl(),
            NullLogger<ProviderCreatedNotifier>.Instance);

        await notifier.NotifyAuditorsAsync(supplierId, CancellationToken.None);

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

    private sealed class ThrowingSender : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
            => throw new InvalidOperationException("simulated provider failure");
    }

    /// <summary>
    /// Spec 041 — the real Razor render needs the Web view engine (not available in
    /// an integration test), so this double serializes the <see cref="DirectEmailModel"/>
    /// the notifier builds into the body. It lets the test assert that the notifier
    /// passes the provider name / cédula / review link into the model (its real
    /// responsibility); the branded HTML is asserted by the E2E mail-capture suite.
    /// </summary>
    private sealed class DumpRenderer : IEmailViewRenderer
    {
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct)
        {
            var m = (DirectEmailModel)model;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(m.Subject);
            sb.AppendLine(m.HeroTitle);
            sb.AppendLine(m.DisplayName);
            foreach (var p in m.Paragraphs) sb.AppendLine(p);
            if (m.CardRows is not null)
                foreach (var row in m.CardRows) sb.AppendLine($"{row.Label}: {row.Value}");
            if (m.CtaUrl is not null) sb.AppendLine(m.CtaUrl);
            return Task.FromResult(sb.ToString());
        }
    }
}
