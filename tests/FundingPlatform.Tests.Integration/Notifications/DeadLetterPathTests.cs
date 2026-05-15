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
/// Spec 021 / T070 / FR-022 — a PermanentFailure outcome from the sender
/// transitions the outbox row to DeadLetter immediately (no retry).
/// </summary>
[TestFixture]
public class DeadLetterPathTests
{
    [Test]
    public async Task PermanentFailure_marks_outbox_DeadLetter_with_AttemptCount_1()
    {
        await using var h = await NotificationsTestHarness.CreateAsync(
            senderOutcome: EmailSendOutcome.PermanentFailure);

        await h.SeedApplicationAsync();
        await h.EnqueueAsync(NotificationEvent.ApplicationSubmittedApplicant);

        await h.Worker.ProcessBatchAsync(CancellationToken.None);

        var outbox = await h.Db.NotificationOutbox.SingleAsync();
        Assert.That(outbox.Status, Is.EqualTo(NotificationOutboxStatus.DeadLetter),
            "FR-022: permanent failure must transition outbox row to DeadLetter immediately.");

        var deliveries = await h.Db.NotificationDeliveries.ToListAsync();
        Assert.That(deliveries, Has.Count.EqualTo(1));
        Assert.That(deliveries[0].Status, Is.EqualTo(NotificationDeliveryStatus.DeadLetter));
        Assert.That(deliveries[0].AttemptCount, Is.EqualTo(1),
            "FR-022: permanent failure records AttemptCount=1.");
        Assert.That(deliveries[0].LastError, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// Spec 021 / T084 — render exception → outbox DeadLetter with the exception
    /// message recorded in LastError. The worker MUST not contact the provider.
    /// </summary>
    [Test]
    public async Task Render_exception_marks_outbox_DeadLetter_without_provider_call()
    {
        // Custom harness — the shared one stubs a successful renderer; this test
        // needs a renderer that throws EmailRenderException.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"render-fail-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new AppDbContext(options);

        var user = new Domain.Entities.ApplicationUser
        {
            Id = "u-" + Guid.NewGuid().ToString("N"),
            UserName = "a@a.test",
            Email = "a@a.test",
            FirstName = "Test", LastName = "User",
        };
        db.Users.Add(user);
        var applicant = new Domain.Entities.Applicant(
            user.Id, "1-1-1", "Test", "User", "a@a.test", null, null);
        db.Applicants.Add(applicant);
        await db.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, "C");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var vh = new Domain.Entities.VersionHistory(user.Id, "Submitted", null);
        app.AddVersionHistory(vh);
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var payload = new NotificationPayload(app.Id, user.Id, "Test User", Array.Empty<int>(), null);
        db.NotificationOutbox.Add(NotificationOutbox.Create(
            NotificationEvent.ApplicationSubmittedApplicant, app.Id, vh.Id, payload));
        await db.SaveChangesAsync();

        // Renderer that always throws.
        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(Arg.Any<NotificationEvent>(), Arg.Any<NotificationRecipient>(),
                Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns<Task<RenderedEmail>>(_ =>
                throw new EmailRenderException("simulated render failure"));

        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EmailSendResult(EmailSendOutcome.Sent, "id-x", null)));

        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var resolver = new NotificationRecipientResolver(db, userManager, new ParticipatingAdminPredicate(db));

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<INotificationRecipientResolver>(resolver);
        services.AddSingleton(renderer);
        services.AddSingleton(sender);
        var provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Worker:MaxAttempts"] = "3",
            }).Build();

        var worker = new EmailDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<EmailDispatchWorker>.Instance);

        await worker.ProcessBatchAsync(CancellationToken.None);

        var outbox = await db.NotificationOutbox.SingleAsync();
        Assert.That(outbox.Status, Is.EqualTo(NotificationOutboxStatus.DeadLetter),
            "T084 — render exception must mark the outbox row DeadLetter immediately.");
        Assert.That(outbox.LastError, Does.Contain("simulated render failure"));

        await sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }
}
