using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 043 (US2/US3) — domain behavior of the Hacienda sync result application +
/// failure recording on <see cref="Supplier"/>.
/// </summary>
[TestFixture]
public class SupplierHaciendaSyncTests
{
    private static Supplier MakeDraft() => Supplier.CreateDraft(
        "3-101-123456", "ACME S.A.", 42, "Sede principal",
        null, null, null, null, null, null, null);

    [Test]
    public void ApplyHaciendaSyncResult_ChangedValue_StampsApiFreshness_AndReturnsChanged()
    {
        var s = MakeDraft();
        var now = DateTime.UtcNow;

        var change = s.ApplyHaciendaSyncResult(HaciendaStatus.AlDia, now, "system");

        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(now));
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("system"));
        Assert.That(s.HaciendaLastReviewedSource, Is.EqualTo(RegulatoryReviewSource.Api));
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Success));
        Assert.That(s.HaciendaSyncAttemptAt, Is.EqualTo(now));
        Assert.That(s.HaciendaSyncError, Is.Null);

        Assert.That(change.Kind, Is.EqualTo(RegulatoryChangeKind.Changed));
        Assert.That(change.Field, Is.EqualTo(RegulatoryChangeField.Hacienda));
        Assert.That(change.Source, Is.EqualTo(RegulatoryReviewSource.Api));
        Assert.That(change.NewValue, Is.EqualTo(((byte)HaciendaStatus.AlDia).ToString()));
    }

    [Test]
    public void ApplyHaciendaSyncResult_SameValue_ReturnsReviewedNoChange_ButRefreshesTimestamp()
    {
        var s = MakeDraft();
        s.ApplyHaciendaSyncResult(HaciendaStatus.AlDia, DateTime.UtcNow.AddDays(-10), "system");
        var now = DateTime.UtcNow;

        var change = s.ApplyHaciendaSyncResult(HaciendaStatus.AlDia, now, "system");

        Assert.That(change.Kind, Is.EqualTo(RegulatoryChangeKind.ReviewedNoChange));
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(now), "an unchanged sync still refreshes freshness");
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Success));
    }

    [Test]
    public void RecordHaciendaSyncFailure_SetsMetadataOnly_NeverTouchesStatusOrFreshness()
    {
        var s = MakeDraft();
        var reviewedAt = DateTime.UtcNow.AddDays(-5);
        s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, null, false, false, null, "auditor-1", reviewedAt);

        var now = DateTime.UtcNow;
        s.RecordHaciendaSyncFailure(now, "timeout");

        // FR-018 — status + last-reviewed are untouched.
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(reviewedAt));
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("auditor-1"));
        // Only the sync metadata changes.
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Failure));
        Assert.That(s.HaciendaSyncAttemptAt, Is.EqualTo(now));
        Assert.That(s.HaciendaSyncError, Is.EqualTo("timeout"));
    }

    [Test]
    public void RecordHaciendaSyncFailure_TruncatesReasonTo500()
    {
        var s = MakeDraft();
        s.RecordHaciendaSyncFailure(DateTime.UtcNow, new string('x', 600));
        Assert.That(s.HaciendaSyncError!.Length, Is.EqualTo(500));
    }

    [Test]
    public void Failure_ThenSuccess_ClearsError()
    {
        var s = MakeDraft();
        s.RecordHaciendaSyncFailure(DateTime.UtcNow, "boom");
        s.ApplyHaciendaSyncResult(HaciendaStatus.AlDia, DateTime.UtcNow, "system");

        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Success));
        Assert.That(s.HaciendaSyncError, Is.Null);
    }
}
