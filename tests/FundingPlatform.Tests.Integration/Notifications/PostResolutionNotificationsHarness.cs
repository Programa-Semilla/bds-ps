using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
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
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 028 — shared harness for the post-resolution integration tests
/// (US1/US2/US3). Seeds a single application with the four recipient archetypes:
/// the applicant, a stage-group reviewer, a participating admin (authored a
/// VersionHistory row), and a non-participating admin (Admin role but no activity
/// on this application). Exposes both the real resolver (recipient-matrix
/// assertions, SC-002) and a real <see cref="EmailDispatchWorker"/> over a stub
/// renderer + counting sender (idempotency / dual-fire, SC-003).
/// </summary>
internal sealed class PostResolutionNotificationsHarness : IAsyncDisposable
{
    public AppDbContext Db { get; private init; } = default!;
    public int AppId { get; private init; }
    public string ApplicantUserId { get; private init; } = default!;
    public string ReviewerUserId { get; private init; } = default!;
    public string ParticipatingAdminUserId { get; private init; } = default!;
    public string NonParticipatingAdminUserId { get; private init; } = default!;
    public int GroupId { get; private init; }

    public NotificationRecipientResolver Resolver { get; private init; } = default!;
    private EmailDispatchWorker Worker { get; init; } = default!;
    private Func<int> ReadSentCount { get; init; } = default!;

    public int SentCount => ReadSentCount();

    public static async Task<PostResolutionNotificationsHarness> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"pr-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new AppDbContext(options);

        ctx.Roles.Add(new IdentityRole("Reviewer") { NormalizedName = "REVIEWER" });
        ctx.Roles.Add(new IdentityRole("Admin") { NormalizedName = "ADMIN" });

        var uniq = Guid.NewGuid().ToString("N");
        ApplicationUser MakeUser(string prefix, string first, string last) => new()
        {
            Id = $"{prefix}-{uniq}",
            UserName = $"{prefix}-{uniq}@programa-semilla.test",
            Email = $"{prefix}-{uniq}@programa-semilla.test",
            FirstName = first,
            LastName = last,
        };

        var applicantUser = MakeUser("app", "Tina", "Solicitante");
        var reviewerUser = MakeUser("rev", "Rita", "Revisora");
        var participatingAdmin = MakeUser("padm", "Pablo", "AdminActivo");
        var nonParticipatingAdmin = MakeUser("nadm", "Nora", "AdminInactivo");
        ctx.Users.AddRange(applicantUser, reviewerUser, participatingAdmin, nonParticipatingAdmin);
        await ctx.SaveChangesAsync();

        var reviewerRole = await ctx.Roles.SingleAsync(r => r.NormalizedName == "REVIEWER");
        var adminRole = await ctx.Roles.SingleAsync(r => r.NormalizedName == "ADMIN");
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = reviewerUser.Id, RoleId = reviewerRole.Id });
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = participatingAdmin.Id, RoleId = adminRole.Id });
        ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = nonParticipatingAdmin.Id, RoleId = adminRole.Id });

        var applicant = new Applicant(
            userId: applicantUser.Id, legalId: "1-1111-2222",
            firstName: "Tina", lastName: "Solicitante",
            email: applicantUser.Email!, phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var group = Group.Create("G-postres", processId: 1);
        ctx.Groups.Add(group);
        await ctx.SaveChangesAsync();

        ctx.UserGroupMemberships.Add(new UserGroupMembership(applicantUser.Id, group.Id));
        ctx.UserGroupMemberships.Add(new UserGroupMembership(reviewerUser.Id, group.Id));
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "PostRes-Co");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        // The participating admin authored an action on THIS application → matches
        // the participating-admin predicate. The non-participating admin does not.
        app.AddVersionHistory(new VersionHistory(participatingAdmin.Id, "ReviewItem", null));
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var resolver = new NotificationRecipientResolver(ctx, userManager, new ParticipatingAdminPredicate(ctx));

        // Worker over a stub renderer (never fails) + a counting sender.
        var sentCount = 0;
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => { sentCount++; return Task.FromResult(new EmailSendResult(EmailSendOutcome.Sent, "id", null)); });

        var renderer = Substitute.For<IEmailTemplateRenderer>();
        renderer.RenderAsync(Arg.Any<NotificationEvent>(), Arg.Any<NotificationRecipient>(),
                Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RenderedEmail("Subject", "<p>html</p>", "text")));

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton<INotificationRecipientResolver>(resolver);
        services.AddSingleton<IEmailTemplateRenderer>(renderer);
        services.AddSingleton<IEmailSender>(sender);
        var provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Worker:PollIntervalSeconds"] = "1",
            ["Notifications:Worker:MaxAttempts"] = "3",
            ["Notifications:Worker:BatchSize"] = "25",
        }).Build();

        var worker = new EmailDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<EmailDispatchWorker>.Instance);

        return new PostResolutionNotificationsHarness
        {
            Db = ctx,
            AppId = app.Id,
            ApplicantUserId = applicantUser.Id,
            ReviewerUserId = reviewerUser.Id,
            ParticipatingAdminUserId = participatingAdmin.Id,
            NonParticipatingAdminUserId = nonParticipatingAdmin.Id,
            GroupId = group.Id,
            Resolver = resolver,
            Worker = worker,
            ReadSentCount = () => sentCount,
        };
    }

    /// <summary>Resolve recipients for one event with a freshly-built payload.</summary>
    public async Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationEvent ev, bool reviewerBucket, string? actorUserId = null, string? outcomeCode = null)
    {
        var payload = BuildPayload(reviewerBucket, actorUserId, outcomeCode);
        var context = new NotificationOutboxResolveContext(
            OutboxId: 1, EventType: ev, ApplicationId: AppId, VersionHistoryId: 1, Payload: payload);
        return await Resolver.ResolveAsync(context, CancellationToken.None);
    }

    /// <summary>
    /// Append a fresh VersionHistory row and enqueue one outbox row anchored on it.
    /// Returns the VersionHistoryId so the caller can craft dual-fire / distinctness.
    /// </summary>
    public async Task<int> EnqueueAsync(
        NotificationEvent ev, bool reviewerBucket, string? actorUserId = null,
        string? outcomeCode = null, int? versionHistoryId = null)
    {
        int vhId;
        if (versionHistoryId is int existing)
        {
            vhId = existing;
        }
        else
        {
            var app = await Db.Applications.FirstAsync(a => a.Id == AppId);
            var vh = new VersionHistory(actorUserId ?? ApplicantUserId, "PostResEvent", null);
            app.AddVersionHistory(vh);
            await Db.SaveChangesAsync();
            vhId = vh.Id;
        }

        var payload = BuildPayload(reviewerBucket, actorUserId, outcomeCode);
        var writer = new NotificationOutboxWriter(Db);
        await writer.EnqueueAsync(ev, AppId, vhId, payload, CancellationToken.None);
        await Db.SaveChangesAsync();
        return vhId;
    }

    private NotificationPayload BuildPayload(bool reviewerBucket, string? actorUserId, string? outcomeCode) =>
        new(AppId, ApplicantUserId, "Tina Solicitante",
            reviewerBucket ? new[] { GroupId } : Array.Empty<int>(),
            outcomeCode, ActorUserId: actorUserId);

    public Task RunWorkerAsync() => Worker.ProcessBatchAsync(CancellationToken.None);

    public async Task<IReadOnlyList<string?>> SentRecipientsAsync(NotificationEvent ev) =>
        await Db.NotificationDeliveries
            .Where(d => d.EventType == ev.ToStorageString()
                     && d.Status == NotificationDeliveryStatus.Sent)
            .Select(d => d.RecipientUserId)
            .ToListAsync();

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
