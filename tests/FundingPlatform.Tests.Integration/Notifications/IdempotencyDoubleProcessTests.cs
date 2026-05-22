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
/// Spec 021 / T053 / SC-003 — exercise the worker idempotency guard. A second
/// pass over the same outbox row produces no second NotificationDelivery row
/// and no second provider call.
/// </summary>
[TestFixture]
public class IdempotencyDoubleProcessTests
{
    [Test]
    public async Task Second_pass_is_noop_when_delivery_already_recorded()
    {
        // Arrange in-memory DB + a seeded applicant + an outbox row in Pending.
        var dbName = $"idem-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new AppDbContext(options);

        // Seed applicant + user so the resolver finds the applicant bucket.
        var user = new Domain.Entities.ApplicationUser
        {
            Id = "u-" + Guid.NewGuid().ToString("N"),
            UserName = "applicant@example.com",
            Email = "applicant@example.com",
            FirstName = "Test",
            LastName = "Applicant",
        };
        ctx.Users.Add(user);
        var applicant = new Domain.Entities.Applicant(
            userId: user.Id,
            legalId: "1-2345-6789",
            firstName: "Test",
            lastName: "Applicant",
            email: user.Email,
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, "TestCo");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var vh = new Domain.Entities.VersionHistory(user.Id, "Submitted", null);
        app.AddVersionHistory(vh);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var payload = new NotificationPayload(
            app.Id, user.Id, "Test Applicant", Array.Empty<int>(), null);
        var outbox = NotificationOutbox.Create(
            NotificationEvent.ApplicationSubmittedApplicant, app.Id, vh.Id, payload);
        ctx.NotificationOutbox.Add(outbox);
        await ctx.SaveChangesAsync();

        // Build a worker over a sub-scope that resolves the seeded DB context.
        var sentCount = 0;
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(ci => { sentCount++; return Task.FromResult(new EmailSendResult(EmailSendOutcome.Sent, "id-1", null)); });

        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(Arg.Any<NotificationEvent>(), Arg.Any<NotificationRecipient>(),
                Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RenderedEmail("Subject", "<p>html</p>", "text")));

        // Use NotificationRecipientResolver with a stub UserManager (admin predicate
        // requires UserRoles, which is empty here — returns no admins).
        var userManager = Substitute.For<UserManager<Domain.Entities.ApplicationUser>>(
            Substitute.For<IUserStore<Domain.Entities.ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var adminPredicate = new ParticipatingAdminPredicate(ctx);
        var resolver = new NotificationRecipientResolver(ctx, userManager, adminPredicate);

        // Hand-roll an IServiceScopeFactory whose CreateScope() returns the seeded ctx
        // + the mock sender + renderer. Register the ctx as a singleton so the scope
        // factory's scope disposal does not eagerly dispose our test-owned ctx.
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton<INotificationRecipientResolver>(resolver);
        services.AddSingleton<IEmailTemplateRenderer>(renderer);
        services.AddSingleton<IEmailSender>(sender);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Worker:PollIntervalSeconds"] = "1",
                ["Notifications:Worker:MaxAttempts"] = "3",
                ["Notifications:Worker:BatchSize"] = "10",
            }).Build();

        var worker = new EmailDispatchWorker(scopeFactory, config,
            NullLogger<EmailDispatchWorker>.Instance);

        // Act — first pass.
        await worker.ProcessBatchAsync(CancellationToken.None);

        var afterFirst = await ctx.NotificationDeliveries.CountAsync();
        Assert.That(afterFirst, Is.EqualTo(1), "First pass: one delivery row.");
        Assert.That(sentCount, Is.EqualTo(1), "First pass: one provider call.");

        // Force the row back to Pending so a second pass picks it up.
        // (In production a transient retry would do this; this test simulates
        // the idempotency-check defending against a worker re-process.)
        outbox.MarkTransientFailure("simulated", DateTime.UtcNow.AddSeconds(-1));
        await ctx.SaveChangesAsync();

        // Act — second pass.
        await worker.ProcessBatchAsync(CancellationToken.None);

        var afterSecond = await ctx.NotificationDeliveries.CountAsync();
        Assert.That(afterSecond, Is.EqualTo(1),
            "SC-003: second pass must NOT add a duplicate NotificationDelivery row.");
        Assert.That(sentCount, Is.EqualTo(1),
            "SC-003: second pass must NOT contact the provider again.");

        await ctx.DisposeAsync();
    }
}
