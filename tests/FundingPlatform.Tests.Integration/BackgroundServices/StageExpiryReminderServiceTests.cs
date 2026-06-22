// Spec 021 / T113 / FR-024 / FR-025 / NFR-002 — integration test for the
// hourly stage-expiry reminder hosted service. Verifies:
//
//   - Applications at the T-72h, T-24h, and expired boundaries each receive
//     exactly one reminder per cycle (captured by CapturingEmailSender)
//   - RemindersSentMask bitfield prevents a second cycle from double-sending
//   - The fake clock controls cycle classification deterministically
//   - Per-Process override (Crocus 2025 facturación = 1 day) takes precedence
//     over the platform default
//
// Test DB strategy: EF in-memory + FakeStageExpiryClock + CapturingEmailSender.
// We swap the DI registration of IEmailSender + IStageExpiryClock manually
// (no real SMTP transport, no real clock).

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using Microsoft.Extensions.Configuration;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.BackgroundServices;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.StageExpiry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.BackgroundServices;

[TestFixture]
public class StageExpiryReminderServiceTests
{
    private AppDbContext _ctx = null!;
    private FakeStageExpiryClock _clock = null!;
    private CapturingEmailSender _capture = null!;
    private StageExpiryReminderService _service = null!;
    private IServiceProvider _services = null!;
    private DateTimeOffset _baseInstant;
    private int _defaultGroupId;

    [SetUp]
    public async Task Setup()
    {
        var dbName = $"reminders-{Guid.NewGuid():N}";

        // Anchor the clock at a fixed instant so every "T-Nh" calculation in
        // the assertions is deterministic. UTC; the templates render in CR
        // local time but the bucket math is UTC-agnostic.
        _baseInstant = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        _clock = new FakeStageExpiryClock(_baseInstant);
        _capture = new CapturingEmailSender();

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<IStageExpiryClock>(_clock);
        services.AddScoped<IStageExpiryEvaluator, StageExpiryEvaluator>();
        // Spec 021 stamp fix: match production DI (Infrastructure/DependencyInjection.cs:96).
        // StageExpiryReminderService routes its application sweep through IApplicationQueryFilter
        // (see StageExpiryReminderService.cs:111). Without this registration the hosted service
        // throws when resolving the scoped dependency.
        services.AddSingleton<IApplicationQueryFilter, ApplicationQueryFilter>();
        services.AddSingleton<IEmailSender>(_capture);
        // Spec 041 — the factory now renders Razor via IEmailViewRenderer (+ reads
        // Notifications:BaseUrl). These tests assert on subject + recipient + count
        // (the subject is computed in BuildAsync, not the body), so a stub renderer
        // suffices.
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Notifications:BaseUrl"] = "https://test.example",
                }).Build());
        services.AddSingleton<IEmailViewRenderer, StubEmailViewRenderer>();
        // Spec 041 bugfix — the factory now resolves its base URL through
        // IEmailBaseUrlProvider (request-aware in production; config-backed in this
        // worker context). A stub returning the test base URL keeps the subject/recipient
        // assertions unchanged.
        services.AddSingleton<IEmailBaseUrlProvider>(new StubBaseUrlProvider("https://test.example"));
        services.AddSingleton<StageReminderEmailFactory>();

        _services = services.BuildServiceProvider();
        _ctx = _services.GetRequiredService<AppDbContext>();
        _service = new StageExpiryReminderService(_services, NullLogger<StageExpiryReminderService>.Instance);

        // Platform default — used when the Application's owning Process has no override.
        // Spec 044 — Stage.Solicitud.WindowDays removed (reception windows replace it).
        _ctx.SystemConfigurations.Add(
            new SystemConfiguration("Stage.Revision.WindowDays", "10", description: null));
        _ctx.SystemConfigurations.Add(
            new SystemConfiguration("Stage.Facturacion.WindowDays", "30", description: null));
        await _ctx.SaveChangesAsync();

        // Spec 029 — a default Active Fund→Process→Group chain so applications
        // seeded by SeedApplicantAndApplicationAsync survive the freeze filter.
        var fund = Fund.Create("Fondo de prueba", "Fondo de prueba para tests.");
        _ctx.Funds.Add(fund);
        await _ctx.SaveChangesAsync();
        var defaultProcess = Process.Create("Proceso de prueba", fund.Id);
        _ctx.Processes.Add(defaultProcess);
        await _ctx.SaveChangesAsync();
        var defaultGroup = Group.Create("Grupo de prueba", defaultProcess.Id);
        _ctx.Groups.Add(defaultGroup);
        await _ctx.SaveChangesAsync();
        _defaultGroupId = defaultGroup.Id;
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
        _ctx.Dispose();
        (_services as IDisposable)?.Dispose();
    }

    [Test]
    public async Task ThreeApplications_AtT72hT24hAndExpired_EachReceiveExactlyOneReminder()
    {
        // Spec 044 — Revisión window default = 10 days (Submitted stage). We want
        // three Applications whose close-at instant lands inside the T-72h, T-24h,
        // and expired buckets relative to _baseInstant.
        //
        //   T-72h Applicant: StageEnteredAt = baseInstant - (10d - 70h)  → 70h remaining
        //   T-24h Applicant: StageEnteredAt = baseInstant - (10d - 23h)  → 23h remaining
        //   Expired Applicant: StageEnteredAt = baseInstant - 11d        → -1d remaining

        var t72 = await SeedApplicantAndApplicationAsync(
            "u-72", "t72@example.com", "Vivi", "T72",
            stageEnteredAt: _baseInstant - TimeSpan.FromDays(10) + TimeSpan.FromHours(70),
            publicCode: "AAAA-2222");
        var t24 = await SeedApplicantAndApplicationAsync(
            "u-24", "t24@example.com", "Vivi", "T24",
            stageEnteredAt: _baseInstant - TimeSpan.FromDays(10) + TimeSpan.FromHours(23),
            publicCode: "BBBB-3333");
        var exp = await SeedApplicantAndApplicationAsync(
            "u-exp", "exp@example.com", "Vivi", "Exp",
            stageEnteredAt: _baseInstant - TimeSpan.FromDays(11),
            publicCode: "CCCC-4444");

        var sent = await _service.ExecuteOneCycleAsync(CancellationToken.None);

        Assert.That(sent, Is.EqualTo(3), "One email per boundary should fire in a single cycle.");
        Assert.That(_capture.Sent, Has.Count.EqualTo(3));
        Assert.That(_capture.Sent.Any(m => m.ToAddress == "t72@example.com"
            && m.Subject.Contains("72 horas")), Is.True, "T-72h envelope must hit t72@example.com.");
        Assert.That(_capture.Sent.Any(m => m.ToAddress == "t24@example.com"
            && m.Subject.Contains("24 horas")), Is.True, "T-24h envelope must hit t24@example.com.");
        Assert.That(_capture.Sent.Any(m => m.ToAddress == "exp@example.com"
            && m.Subject.Contains("cerró")), Is.True, "Expired envelope must hit exp@example.com.");

        // RemindersSentMask is set per Application.
        var reloadedT72 = await _ctx.Applications.AsNoTracking().FirstAsync(a => a.Id == t72);
        var reloadedT24 = await _ctx.Applications.AsNoTracking().FirstAsync(a => a.Id == t24);
        var reloadedExp = await _ctx.Applications.AsNoTracking().FirstAsync(a => a.Id == exp);
        Assert.That(reloadedT72.RemindersSentMask, Is.EqualTo((byte)0x1), "T-72h bit must be set.");
        Assert.That(reloadedT24.RemindersSentMask, Is.EqualTo((byte)0x2), "T-24h bit must be set.");
        Assert.That(reloadedExp.RemindersSentMask, Is.EqualTo((byte)0x4), "Expired bit must be set.");
    }

    [Test]
    public async Task SecondCycle_DoesNotResendBucketsWhoseBitIsAlreadySet()
    {
        // Seed one T-72h Applicant, run twice without advancing the clock.
        await SeedApplicantAndApplicationAsync(
            "u-dup", "dup@example.com", "Vivi", "Dup",
            stageEnteredAt: _baseInstant - TimeSpan.FromDays(10) + TimeSpan.FromHours(70),
            publicCode: "DDDD-5555");

        var firstCycle = await _service.ExecuteOneCycleAsync(CancellationToken.None);
        var secondCycle = await _service.ExecuteOneCycleAsync(CancellationToken.None);

        Assert.That(firstCycle, Is.EqualTo(1), "First cycle must send the T-72h reminder once.");
        Assert.That(secondCycle, Is.EqualTo(0),
            "Second cycle must NOT resend — the 0x1 bit on RemindersSentMask short-circuits the bucket.");
        Assert.That(_capture.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PerProcessOverride_ShortensFacturacionWindowToOneDay()
    {
        // FR-006 — admin overrides facturacion to 1 day on Crocus 2025.
        // Build the Crocus 2025 Process + a Group attached to it + the
        // Applicant's UserGroupMembership pointing at that group. The
        // Application sits in ResponseFinalized (facturacion stage); with the
        // 1-day override, StageEnteredAt > 24h ago lands in the expired bucket
        // even though the platform default is 30 days.

        var fund = Fund.Create("Fondo Crocus", "Fondo de prueba para tests.");
        _ctx.Funds.Add(fund);
        await _ctx.SaveChangesAsync();
        var process = Process.Create("Crocus 2025", fund.Id);
        process.OverrideStageWindow(StageKind.Facturacion, 1);
        _ctx.Processes.Add(process);
        await _ctx.SaveChangesAsync();

        var group = Group.Create($"Norte-{Guid.NewGuid():N}", process.Id);
        _ctx.Groups.Add(group);
        await _ctx.SaveChangesAsync();

        var applicant = new Applicant(
            userId: "u-fact",
            legalId: $"L-{Guid.NewGuid():N}",
            firstName: "Vivi",
            lastName: "Fact",
            email: "fact@example.com",
            phone: null,
            performanceScore: null);
        _ctx.Applicants.Add(applicant);
        await _ctx.SaveChangesAsync();

        _ctx.UserGroupMemberships.Add(new UserGroupMembership("u-fact", group.Id));
        await _ctx.SaveChangesAsync();

        // Application in ResponseFinalized state with StageEnteredAt = 2 days ago.
        var app = new AppEntity(applicant.Id, group.Id, null,"Sazón Crocus");
        app.AssignPublicCode(new PublicCode("EEEE-6666"));
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();

        // Reach into the EF row to set the state + stage-entered timestamp
        // (Application.Submit goes through guard chains we don't want to fire
        // here — the test exercises the evaluator + reminder service, not the
        // full state machine).
        _ctx.Entry(app).Property(nameof(AppEntity.StageEnteredAt)).CurrentValue =
            _baseInstant - TimeSpan.FromDays(2);
        _ctx.Entry(app).Property(nameof(AppEntity.State)).CurrentValue =
            ApplicationState.ResponseFinalized;
        await _ctx.SaveChangesAsync();

        var sent = await _service.ExecuteOneCycleAsync(CancellationToken.None);

        Assert.That(sent, Is.EqualTo(1));
        Assert.That(_capture.Sent[0].Subject, Does.Contain("cerró"),
            "FR-006 — with facturacion override = 1 day and StageEnteredAt = 2 days ago, the Application must be classified Expired.");
        Assert.That(_capture.Sent[0].ToAddress, Is.EqualTo("fact@example.com"));
    }

    private async Task<int> SeedApplicantAndApplicationAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        DateTimeOffset stageEnteredAt,
        string publicCode)
    {
        var applicant = new Applicant(
            userId: userId,
            legalId: $"L-{Guid.NewGuid():N}",
            firstName: firstName,
            lastName: lastName,
            email: email,
            phone: null,
            performanceScore: null);
        _ctx.Applicants.Add(applicant);
        await _ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, _defaultGroupId, null,$"Co-{userId}");
        app.AssignPublicCode(new PublicCode(publicCode));
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();

        // Spec 044 — Draft/AppealOpen no longer receive stage-expiry reminders
        // (the Solicitud duration gate was removed). Seed in Submitted (Revisión)
        // stage so the reviewer-window reminder buckets are exercised.
        _ctx.Entry(app).Property(nameof(AppEntity.StageEnteredAt)).CurrentValue = stageEnteredAt;
        _ctx.Entry(app).Property(nameof(AppEntity.State)).CurrentValue = ApplicationState.Submitted;
        await _ctx.SaveChangesAsync();

        return app.Id;
    }

    private sealed class FakeStageExpiryClock : IStageExpiryClock
    {
        public FakeStageExpiryClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "FundingPlatform.Tests.Integration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>Spec 041 — no-op renderer; these tests assert subject/recipient/count, not body.</summary>
    private sealed class StubEmailViewRenderer : IEmailViewRenderer
    {
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct)
            => Task.FromResult(string.Empty);
    }

    private sealed class StubBaseUrlProvider(string baseUrl) : IEmailBaseUrlProvider
    {
        public string GetBaseUrl() => baseUrl;
    }
}
