using FundingPlatform.Domain.ReceptionWindows;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 044 / D3 / SC-002 — pure submission-availability gate over absolute UTC
/// instants. Boundary semantics are start-inclusive / end-exclusive.
/// </summary>
[TestFixture]
public class ReceptionWindowEvaluationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    private static ReceptionWindowSnapshot Window(int id, DateTimeOffset start, DateTimeOffset end, string? msg = null)
        => new(id, $"W{id}", start, end, msg);

    [Test]
    public void Evaluate_NoWindows_IsUnrestricted()
    {
        var result = ReceptionWindowEvaluation.Evaluate(Array.Empty<ReceptionWindowSnapshot>(), Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.Unrestricted));
        Assert.That(result.CanSubmit, Is.True);
        Assert.That(result.CanCreateDraft, Is.True);
    }

    [Test]
    public void Evaluate_NowInsideWindow_IsOpen()
    {
        var windows = new[] { Window(1, Now.AddDays(-1), Now.AddDays(1)) };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.Open));
        Assert.That(result.ActiveWindow!.Id, Is.EqualTo(1));
        Assert.That(result.CanSubmit, Is.True);
    }

    [Test]
    public void Evaluate_NowExactlyAtStart_IsOpen_StartInclusive()
    {
        var windows = new[] { Window(1, Now, Now.AddDays(1)) };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.Open));
        Assert.That(result.CanSubmit, Is.True);
    }

    [Test]
    public void Evaluate_NowExactlyAtEnd_IsClosed_EndExclusive()
    {
        // now == End ⇒ the window has closed (SC-002). With a single past-bounded
        // window and no upcoming one, the process is AllWindowsClosed.
        var windows = new[] { Window(1, Now.AddDays(-1), Now) };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.AllWindowsClosed));
        Assert.That(result.CanSubmit, Is.False);
        Assert.That(result.LastClosedWindow!.Id, Is.EqualTo(1));
    }

    [Test]
    public void Evaluate_BeforeFirstWindow_NoneClosedYet()
    {
        var windows = new[] { Window(1, Now.AddDays(1), Now.AddDays(2)) };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.BeforeFirstWindow));
        Assert.That(result.NextWindow!.Id, Is.EqualTo(1));
        Assert.That(result.CanSubmit, Is.False);
        Assert.That(result.CanCreateDraft, Is.True); // future window still gives a chance
    }

    [Test]
    public void Evaluate_BetweenWindows_OneClosedOneUpcoming()
    {
        var windows = new[]
        {
            Window(1, Now.AddDays(-3), Now.AddDays(-2)), // closed
            Window(2, Now.AddDays(2), Now.AddDays(3)),   // upcoming
        };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.BetweenWindows));
        Assert.That(result.NextWindow!.Id, Is.EqualTo(2));
        Assert.That(result.CanSubmit, Is.False);
        Assert.That(result.CanCreateDraft, Is.True);
    }

    [Test]
    public void Evaluate_AllClosed_RefusesAndBlocksDraft()
    {
        var windows = new[]
        {
            Window(1, Now.AddDays(-5), Now.AddDays(-4)),
            Window(2, Now.AddDays(-3), Now.AddDays(-1)),
        };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.AllWindowsClosed));
        Assert.That(result.LastClosedWindow!.Id, Is.EqualTo(2)); // latest End
        Assert.That(result.CanSubmit, Is.False);
        Assert.That(result.CanCreateDraft, Is.False); // dead-end guard (FR-014)
    }

    [Test]
    public void Evaluate_OverlappingOpenWindows_PrefersLatestEnd()
    {
        var windows = new[]
        {
            Window(1, Now.AddDays(-1), Now.AddHours(6)),
            Window(2, Now.AddHours(-3), Now.AddDays(2)), // latest End
        };

        var result = ReceptionWindowEvaluation.Evaluate(windows, Now);

        Assert.That(result.Status, Is.EqualTo(SubmissionAvailabilityStatus.Open));
        Assert.That(result.ActiveWindow!.Id, Is.EqualTo(2));
    }
}
