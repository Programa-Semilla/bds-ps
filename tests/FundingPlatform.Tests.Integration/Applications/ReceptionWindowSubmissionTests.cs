using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ReceptionWindows;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 044 / US2 (T028) — reception-window submission gate. The gate fires in the
/// handler BEFORE the item/quotation validation, so:
///   • blocked cases throw <see cref="ReceptionWindowClosedException"/> (with the
///     correct status + boundary), and
///   • allowed cases (open / no-window / start-boundary) fall through to the
///     item-count validation (<see cref="InvalidOperationException"/> "at least one
///     item") — proving the reception gate did NOT block.
/// Boundary semantics (SC-002) and a full happy-path submit are otherwise covered
/// by the pure evaluator unit tests + the E2E suite.
/// </summary>
[TestFixture]
public class ReceptionWindowSubmissionTests
{
    private AppDbContext _ctx = null!;
    private SubmitApplicationHandler _handler = null!;
    private FakeClock _clock = null!;
    private int _applicantId;
    private int _groupId;
    private int _processId;

    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public async Task Setup()
    {
        _ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rw-submit-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        var fund = Fund.Create("Fondo", "desc");
        _ctx.Funds.Add(fund);
        await _ctx.SaveChangesAsync();
        var process = Process.Create("Proceso", fund.Id);
        _ctx.Processes.Add(process);
        await _ctx.SaveChangesAsync();
        _processId = process.Id;
        var group = Group.Create($"G-{Guid.NewGuid():N}", process.Id);
        _ctx.Groups.Add(group);
        await _ctx.SaveChangesAsync();
        _groupId = group.Id;

        var applicant = new Applicant("u-1", "1-1", "Vivi", "P", "v@example.com", null, null);
        _ctx.Applicants.Add(applicant);
        await _ctx.SaveChangesAsync();
        _applicantId = applicant.Id;

        _clock = new FakeClock(Now);
        _handler = new SubmitApplicationHandler(
            _ctx, _clock,
            new FundingPlatform.Infrastructure.Notifications.Persistence.NotificationOutboxWriter(_ctx),
            new ReceptionWindowQuery(_ctx));
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private async Task AddWindowAsync(DateTimeOffset start, DateTimeOffset end, bool active = true)
    {
        var w = ProcessEvent.CreateReceptionWindow(_processId, "W", start, end, null, null, 0, "admin");
        if (!active) w.Deactivate("admin");
        _ctx.ProcessEvents.Add(w);
        await _ctx.SaveChangesAsync();
    }

    private async Task<int> NewDraftAsync()
    {
        var app = new AppEntity(_applicantId, _groupId, null, "Empresa");
        // Each test uses its own InMemory DB, so a constant valid code is fine.
        app.AssignPublicCode(new PublicCode("ABCD-2345"));
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();
        return app.Id;
    }

    private async Task SubmitAsync(int appId) =>
        await _handler.SubmitAsync(new SubmitApplicationCommand(appId));

    [Test]
    public async Task NoWindows_SubmissionIsAllowed_FallsThroughToItemValidation()
    {
        var appId = await NewDraftAsync();
        Assert.That(async () => await SubmitAsync(appId),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contain("at least one item"));
    }

    [Test]
    public async Task Open_SubmissionIsAllowed_FallsThroughToItemValidation()
    {
        await AddWindowAsync(Now.AddDays(-1), Now.AddDays(1));
        var appId = await NewDraftAsync();
        Assert.That(async () => await SubmitAsync(appId),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contain("at least one item"));
    }

    [Test]
    public async Task StartBoundary_NowEqualsStart_IsAllowed()
    {
        // SC-002 — start-inclusive: now == Start ⇒ open.
        await AddWindowAsync(Now, Now.AddDays(1));
        var appId = await NewDraftAsync();
        Assert.That(async () => await SubmitAsync(appId),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contain("at least one item"));
    }

    [Test]
    public async Task EndBoundary_NowEqualsEnd_IsBlocked()
    {
        // SC-002 — end-exclusive: now == End ⇒ closed.
        await AddWindowAsync(Now.AddDays(-1), Now);
        var appId = await NewDraftAsync();
        var ex = Assert.ThrowsAsync<ReceptionWindowClosedException>(() => SubmitAsync(appId));
        Assert.That(ex!.Status, Is.EqualTo(SubmissionAvailabilityStatus.AllWindowsClosed));
    }

    [Test]
    public async Task BeforeFirstWindow_IsBlocked_WithNextOpenBoundary()
    {
        await AddWindowAsync(Now.AddDays(2), Now.AddDays(5));
        var appId = await NewDraftAsync();
        var ex = Assert.ThrowsAsync<ReceptionWindowClosedException>(() => SubmitAsync(appId));
        Assert.That(ex!.Status, Is.EqualTo(SubmissionAvailabilityStatus.BeforeFirstWindow));
        Assert.That(ex.BoundaryUtc, Is.EqualTo(Now.AddDays(2)));
    }

    [Test]
    public async Task BetweenWindows_IsBlocked_WithNextOpenBoundary()
    {
        await AddWindowAsync(Now.AddDays(-5), Now.AddDays(-3));
        await AddWindowAsync(Now.AddDays(3), Now.AddDays(6));
        var appId = await NewDraftAsync();
        var ex = Assert.ThrowsAsync<ReceptionWindowClosedException>(() => SubmitAsync(appId));
        Assert.That(ex!.Status, Is.EqualTo(SubmissionAvailabilityStatus.BetweenWindows));
        Assert.That(ex.BoundaryUtc, Is.EqualTo(Now.AddDays(3)));
    }

    [Test]
    public async Task AllClosed_IsBlocked_WithLastClosedBoundary()
    {
        await AddWindowAsync(Now.AddDays(-5), Now.AddDays(-4));
        await AddWindowAsync(Now.AddDays(-3), Now.AddDays(-1));
        var appId = await NewDraftAsync();
        var ex = Assert.ThrowsAsync<ReceptionWindowClosedException>(() => SubmitAsync(appId));
        Assert.That(ex!.Status, Is.EqualTo(SubmissionAvailabilityStatus.AllWindowsClosed));
        Assert.That(ex.BoundaryUtc, Is.EqualTo(Now.AddDays(-1))); // latest End
    }

    [Test]
    public async Task InactiveWindow_IsIgnored_TreatedAsNoWindows()
    {
        // An inactive window that would otherwise be open must be ignored ⇒ unrestricted.
        await AddWindowAsync(Now.AddDays(-1), Now.AddDays(1), active: false);
        var appId = await NewDraftAsync();
        Assert.That(async () => await SubmitAsync(appId),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contain("at least one item"));
    }

    [Test]
    public async Task NonRetroactive_DeactivatingWindowAfterSubmit_LeavesSubmittedApplicationUnchanged()
    {
        // FR-017 — gating is point-in-time at submit. A submitted application's state
        // is independent of later window edits. We simulate a prior successful submit
        // (the full happy-path submit is E2E-only) by transitioning the row to
        // Submitted, then deactivate the window and assert the state is untouched and
        // the application is still readable.
        await AddWindowAsync(Now.AddDays(-1), Now.AddDays(1));
        var appId = await NewDraftAsync();
        var window = await _ctx.ProcessEvents.FirstAsync(e => e.ProcessId == _processId);

        var app = await _ctx.Applications.FirstAsync(a => a.Id == appId);
        _ctx.Entry(app).Property(nameof(AppEntity.State)).CurrentValue = ApplicationState.Submitted;
        await _ctx.SaveChangesAsync();

        // Later: admin deactivates (and could delete) the window.
        window.Deactivate("admin");
        await _ctx.SaveChangesAsync();

        var reloaded = await _ctx.Applications.AsNoTracking().FirstAsync(a => a.Id == appId);
        Assert.That(reloaded.State, Is.EqualTo(ApplicationState.Submitted),
            "A submitted application must be unaffected by later reception-window edits (FR-017).");
    }

    private sealed class FakeClock : IStageExpiryClock
    {
        public FakeClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; set; }
    }
}
