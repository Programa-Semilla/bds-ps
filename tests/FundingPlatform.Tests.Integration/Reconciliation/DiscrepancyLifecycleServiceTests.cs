using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Reconciliation;

/// <summary>
/// Spec 048 / T034 — the lifecycle service: assign writes a timeline event + a discrepancy.* audit
/// event; waiving a Blocking discrepancy is refused; a not-found id returns NotFound. RowVersion
/// concurrency is proven on real SQL by the E2E suite (InMemory does not generate the token).
/// </summary>
[TestFixture]
public class DiscrepancyLifecycleServiceTests
{
    private const string Actor = "finop-1";
    private const string Assignee = "finop-2";
    private const string System = "system-sentinel";
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DiscrepancyLifecycleService NewService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx), new NoOpEmailSender(),
            new DiscrepancyAssignmentEmailFactory(new StubEmailViewRenderer(), new StubBaseUrlProvider(),
                NullLogger<DiscrepancyAssignmentEmailFactory>.Instance),
            NullLogger<DiscrepancyLifecycleService>.Instance);

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubEmailViewRenderer : IEmailViewRenderer
    {
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct) => Task.FromResult(string.Empty);
    }

    private sealed class StubBaseUrlProvider : IEmailBaseUrlProvider
    {
        public string GetBaseUrl() => "http://localhost";
    }

    private static async Task<int> SeedAsync(AppDbContext ctx, DiscrepancySeverity severity)
    {
        var d = Discrepancy.Detect(
            applicationId: 10, DiscrepancyScopeType.Payment, scopeEntityId: 3,
            severity == DiscrepancySeverity.Blocking
                ? ReconciliationComparison.DisbursementVsInvoice
                : ReconciliationComparison.PossibleDuplicatePayment,
            severity, 100m, severity == DiscrepancySeverity.Blocking ? 101m : 100m, 0m, "doc", System, Now);
        ctx.Discrepancies.Add(d);
        await ctx.SaveChangesAsync();
        return d.Id;
    }

    [Test]
    public async Task Assign_WritesTimelineEventAndAudit()
    {
        var db = $"disc-assign-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var id = await SeedAsync(ctx, DiscrepancySeverity.Warning);

        var result = await NewService(ctx).AssignAsync(id, Assignee, Actor, CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(DiscrepancyActionOutcome.Ok));

        var stored = await ctx.Discrepancies.AsNoTracking().Include(x => x.Events).SingleAsync(x => x.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.State, Is.EqualTo(DiscrepancyState.Assigned));
            Assert.That(stored.AssigneeUserId, Is.EqualTo(Assignee));
            Assert.That(stored.Events.Any(e => e.Kind == DiscrepancyEvent.KindAssigned), Is.True);
        });

        var audit = await ctx.AdminAuditEvents.AsNoTracking()
            .Where(a => a.Action == AdminAuditEvent.DiscrepancyAssigned).ToListAsync();
        Assert.That(audit, Has.Count.EqualTo(1));
        Assert.That(audit[0].TargetType, Is.EqualTo(AdminAuditEvent.TargetTypeDiscrepancy));
        Assert.That(audit[0].TargetId, Is.EqualTo(id.ToString()));
    }

    [Test]
    public async Task Waive_OnBlocking_IsRefused()
    {
        var db = $"disc-waive-block-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var id = await SeedAsync(ctx, DiscrepancySeverity.Blocking);

        var result = await NewService(ctx).WaiveAsync(id, "motivo", Actor, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(DiscrepancyActionOutcome.Refused));
            Assert.That(result.Error!.Code, Is.EqualTo(DiscrepancyReasons.Codes.CannotWaiveBlocking));
        });
    }

    [Test]
    public async Task Waive_OnWarning_Succeeds()
    {
        var db = $"disc-waive-ok-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var id = await SeedAsync(ctx, DiscrepancySeverity.Warning);

        var result = await NewService(ctx).WaiveAsync(id, "Aceptada.", Actor, CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(DiscrepancyActionOutcome.Ok));

        var stored = await ctx.Discrepancies.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.That(stored.State, Is.EqualTo(DiscrepancyState.Waived));
    }

    [Test]
    public async Task Action_OnMissingId_ReturnsNotFound()
    {
        var db = $"disc-missing-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);

        var result = await NewService(ctx).AssignAsync(9999, Assignee, Actor, CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(DiscrepancyActionOutcome.NotFound));
    }
}
