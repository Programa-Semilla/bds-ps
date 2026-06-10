using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / T042 / FR-001 — exercises the writer's transactional behaviour
/// directly. The full SubmitApplicationAsync flow is covered by the E2E test
/// in T043; this test isolates the FR-001 contract: a successful enqueue
/// writes one row; a failed transaction leaves zero rows.
/// </summary>
[TestFixture]
public class OutboxTransactionalEnqueueTests
{
    private AppDbContext _ctx = null!;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"outbox-tx-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _ctx = new AppDbContext(options);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task EnqueueAsync_writes_outbox_row_on_SaveChanges()
    {
        var writer = new NotificationOutboxWriter(_ctx);
        var payload = new NotificationPayload(
            ApplicationId: 1,
            ApplicantUserId: "user-1",
            ApplicantDisplayName: "Juana Pérez",
            StageGroupIds: new[] { 1, 2 },
            OutcomeCode: null);

        await writer.EnqueueAsync(
            NotificationEvent.ApplicationSubmittedApplicant,
            applicationId: 1,
            versionHistoryId: 1,
            payload,
            CancellationToken.None);

        // Writer alone does not SaveChanges — caller commits.
        var beforeSave = await _ctx.NotificationOutbox.CountAsync();
        Assert.That(beforeSave, Is.EqualTo(0),
            "FR-001: writer must defer persistence to the caller's SaveChanges.");

        await _ctx.SaveChangesAsync();

        var afterSave = await _ctx.NotificationOutbox.CountAsync();
        Assert.That(afterSave, Is.EqualTo(1));

        var row = await _ctx.NotificationOutbox.SingleAsync();
        Assert.That(row.Status, Is.EqualTo(NotificationOutboxStatus.Pending));
        Assert.That(row.AttemptCount, Is.EqualTo(0));
        Assert.That(row.EventType, Is.EqualTo("APPLICATION_SUBMITTED_APPLICANT"));
        Assert.That(row.PayloadJson, Does.Contain("Juana Pérez"));
    }

    [Test]
    public async Task Submit_fails_writes_zero_outbox_rows()
    {
        // FR-001: a workflow-transition failure must not leave orphan outbox rows.
        // The writer's Add+defer pattern means the row is only persisted when the
        // caller's SaveChanges succeeds. We simulate that here by enqueueing then
        // NOT calling SaveChanges — equivalent to a rollback path on the caller side.
        var writer = new NotificationOutboxWriter(_ctx);
        var payload = new NotificationPayload(1, "user-1", "Juana", new[] { 1 }, null);

        await writer.EnqueueAsync(
            NotificationEvent.ApplicationSubmittedReviewer, 1, 1, payload, CancellationToken.None);

        // Caller decides to roll back — clears change tracker before any persistence.
        _ctx.ChangeTracker.Clear();
        await _ctx.SaveChangesAsync();

        var count = await _ctx.NotificationOutbox.CountAsync();
        Assert.That(count, Is.EqualTo(0),
            "FR-001: rolled-back transaction must yield zero outbox rows.");
    }

    [Test]
    public async Task HasPriorSendBackAsync_detects_resubmit_path()
    {
        // R-003 — the writer's resubmit detection reads VersionHistory for an Action="SendBack".
        // Seed via an Applicant + Application so the navigation chain is intact and the
        // VersionHistory row picks up its ApplicationId from the parent aggregate.
        var applicant = new Domain.Entities.Applicant(
            userId: "u-" + Guid.NewGuid().ToString("N"),
            legalId: "1-1234-5678",
            firstName: "Test",
            lastName: "Applicant",
            email: "test@example.com",
            phone: null,
            performanceScore: null);
        _ctx.Applicants.Add(applicant);
        await _ctx.SaveChangesAsync();

        var app = new Domain.Entities.Application(applicant.Id, 1, "TestCo");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        app.AddVersionHistory(new Domain.Entities.VersionHistory("u-1", "SendBack", null));
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();

        var writer = new NotificationOutboxWriter(_ctx);
        Assert.That(await writer.HasPriorSendBackAsync(app.Id, CancellationToken.None), Is.True);
        Assert.That(await writer.HasPriorSendBackAsync(app.Id + 9999, CancellationToken.None), Is.False);
    }
}
