using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / T054 / EC-001 — two resubmissions without an intermediate
/// SendBack produce two distinct outbox rows with different VersionHistoryIds.
/// Each fans out independently — idempotency does NOT collapse them.
/// </summary>
[TestFixture]
public class SequentialResubmitTests
{
    [Test]
    public async Task Two_resubmits_yield_two_distinct_outbox_rows()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var ctx = new AppDbContext(options);

        var applicant = new Domain.Entities.Applicant(
            userId: "u-" + Guid.NewGuid().ToString("N"),
            legalId: "1-1111-2222",
            firstName: "Resub",
            lastName: "Test",
            email: "rt@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, "TestCo");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        var writer = new NotificationOutboxWriter(ctx);

        // Two sequential resubmits — append a SendBack + a Submitted VersionHistory
        // per cycle, then enqueue a RESUBMITTED_BY_APPLICANT outbox row referencing
        // the latest VersionHistory.Id.
        for (var cycle = 1; cycle <= 2; cycle++)
        {
            var sendBack = new Domain.Entities.VersionHistory("u-1", "SendBack", null);
            app.AddVersionHistory(sendBack);
            var submitted = new Domain.Entities.VersionHistory("u-1", "Submitted", null);
            app.AddVersionHistory(submitted);
            await ctx.SaveChangesAsync();

            var payload = new NotificationPayload(app.Id, applicant.UserId, "Resub Test", Array.Empty<int>(), null);
            await writer.EnqueueAsync(
                NotificationEvent.ResubmittedByApplicant,
                app.Id, submitted.Id, payload, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        var rows = await ctx.NotificationOutbox
            .Where(o => o.ApplicationId == app.Id && o.EventType == "RESUBMITTED_BY_APPLICANT")
            .ToListAsync();

        Assert.That(rows, Has.Count.EqualTo(2),
            "EC-001: two resubmissions must yield two distinct RESUBMITTED_BY_APPLICANT outbox rows.");
        Assert.That(rows.Select(r => r.VersionHistoryId).Distinct().Count(), Is.EqualTo(2),
            "EC-001: each row references its own VersionHistoryId.");
    }
}
