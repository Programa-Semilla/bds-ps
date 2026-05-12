using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Notifications.Workers;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 — shared test harness that boots an in-memory DB + a worker over
/// a mocked sender. Used by DeadLetterPathTests, AllowlistFailClosedTests,
/// ParticipatingAdminPredicateTests, DedupBucketPriorityTests, and
/// MissingEmailSkipTests so each test focuses on the surface under test.
/// </summary>
internal sealed class NotificationsTestHarness : IAsyncDisposable
{
    public AppDbContext Db { get; }
    public IEmailSender Sender { get; }
    public IEmailTemplateRenderer Renderer { get; }
    public EmailDispatchWorker Worker { get; }
    public Domain.Entities.ApplicationUser ApplicantUser { get; }
    public Domain.Entities.Applicant Applicant { get; }
    public Domain.Entities.Application Application { get; private set; } = default!;
    public Domain.Entities.VersionHistory LatestVersionHistory { get; private set; } = default!;
    public int SendCount { get; private set; }

    private NotificationsTestHarness(
        AppDbContext db,
        IEmailSender sender,
        IEmailTemplateRenderer renderer,
        EmailDispatchWorker worker,
        Domain.Entities.ApplicationUser applicantUser,
        Domain.Entities.Applicant applicant)
    {
        Db = db;
        Sender = sender;
        Renderer = renderer;
        Worker = worker;
        ApplicantUser = applicantUser;
        Applicant = applicant;
    }

    public static async Task<NotificationsTestHarness> CreateAsync(
        EmailSendOutcome senderOutcome = EmailSendOutcome.Sent,
        string? allowlistEntry = null,
        bool simulateMissingEmail = false,
        Action<HarnessSendInvocation>? onSend = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"nh-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        var user = new Domain.Entities.ApplicationUser
        {
            Id = "u-" + Guid.NewGuid().ToString("N"),
            UserName = "applicant@test.local",
            Email = simulateMissingEmail ? null : "applicant@test.local",
            FirstName = "Test",
            LastName = "Applicant",
        };
        db.Users.Add(user);
        var applicant = new Domain.Entities.Applicant(
            userId: user.Id,
            legalId: "1-1111-2222",
            firstName: "Test",
            lastName: "Applicant",
            email: user.Email ?? "applicant@test.local",
            phone: null,
            performanceScore: null);
        db.Applicants.Add(applicant);
        await db.SaveChangesAsync();

        // Sender mock.
        var sendCount = 0;
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                sendCount++;
                var msg = ci.Arg<EmailMessage>();
                onSend?.Invoke(new HarnessSendInvocation(msg));
                return Task.FromResult(new EmailSendResult(senderOutcome,
                    senderOutcome == EmailSendOutcome.Sent ? "id-" + sendCount : null,
                    senderOutcome == EmailSendOutcome.Sent ? null : "simulated"));
            });

        // Wrap with allowlist filter when an entry is supplied (covers fail-closed
        // SC-004 scenario when entry == "").
        IEmailSender effectiveSender = sender;
        if (allowlistEntry is not null)
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Notifications:NonProdAllowlist:0"] = allowlistEntry,
                }).Build();
            effectiveSender = new FundingPlatform.Infrastructure.Notifications
                .RecipientAllowlistFilter(sender, cfg,
                    NullLogger<FundingPlatform.Infrastructure.Notifications.RecipientAllowlistFilter>.Instance);
        }

        // Stub renderer returns a benign body so render never fails.
        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(Arg.Any<NotificationEvent>(), Arg.Any<NotificationRecipient>(),
                Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RenderedEmail("Subject", "<p>html</p>", "text")));

        // UserManager stub (admin predicate hits UserRoles directly via DbContext;
        // the resolver only needs a valid instance to satisfy the ctor).
        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

        var adminPredicate = new ParticipatingAdminPredicate(db);
        var resolver = new NotificationRecipientResolver(db, userManager, adminPredicate);

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<INotificationRecipientResolver>(resolver);
        services.AddSingleton<IEmailTemplateRenderer>(renderer);
        services.AddSingleton(effectiveSender);
        var provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Worker:PollIntervalSeconds"] = "1",
                ["Notifications:Worker:MaxAttempts"] = "3",
                ["Notifications:Worker:BatchSize"] = "10",
            }).Build();

        var worker = new EmailDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<EmailDispatchWorker>.Instance);

        var harness = new NotificationsTestHarness(db, effectiveSender, renderer, worker, user, applicant);
        harness.UpdateSendCounter(() => sendCount);
        return harness;
    }

    private Func<int>? _readSendCount;
    private void UpdateSendCounter(Func<int> reader) => _readSendCount = reader;
    public int ObservedSendCount => _readSendCount?.Invoke() ?? SendCount;

    public async Task<Domain.Entities.Application> SeedApplicationAsync(string companyName = "Test Co")
    {
        var app = new Domain.Entities.Application(Applicant.Id, companyName);
        var vh = new Domain.Entities.VersionHistory(ApplicantUser.Id, "Submitted", null);
        app.AddVersionHistory(vh);
        Db.Applications.Add(app);
        await Db.SaveChangesAsync();
        Application = app;
        LatestVersionHistory = vh;
        return app;
    }

    public async Task EnqueueAsync(NotificationEvent ev)
    {
        var writer = new NotificationOutboxWriter(Db);
        var payload = new NotificationPayload(
            Application.Id,
            ApplicantUser.Id,
            $"{Applicant.FirstName} {Applicant.LastName}",
            Array.Empty<int>(),
            null);
        await writer.EnqueueAsync(ev, Application.Id, LatestVersionHistory.Id, payload, CancellationToken.None);
        await Db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

internal sealed record HarnessSendInvocation(EmailMessage Message);
