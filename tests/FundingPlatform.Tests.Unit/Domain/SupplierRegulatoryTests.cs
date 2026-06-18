using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 038 (US1/US2) — domain behavior of the auditor regulatory edit +
/// "reviewed — no change" re-authorization on <see cref="Supplier"/>.
/// </summary>
[TestFixture]
public class SupplierRegulatoryTests
{
    private static Supplier MakeDraft() => Supplier.CreateDraft(
        "3-101-123456", "ACME S.A.", 42, "Sede principal",
        null, null, null, null, null, null, null);

    [Test]
    public void ApplyRegulatoryEdit_ChangedField_SetsValueAndStampsFreshness()
    {
        var s = MakeDraft();
        var now = DateTime.UtcNow;

        var changes = s.ApplyRegulatoryEdit(
            HaciendaStatus.AlDia, null, null, false, false, null, "user-1", now);

        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(now));
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("user-1"));
        Assert.That(s.HaciendaLastReviewedSource, Is.EqualTo(RegulatoryReviewSource.Manual));
        // Unchanged fields are left untouched.
        Assert.That(s.CcssStatus, Is.Null);
        Assert.That(s.CcssLastReviewedAt, Is.Null);

        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].Field, Is.EqualTo(RegulatoryChangeField.Hacienda));
        Assert.That(changes[0].Kind, Is.EqualTo(RegulatoryChangeKind.Changed));
    }

    [Test]
    public void ApplyRegulatoryEdit_NoChange_ReturnsEmptyAndKeepsTimestamp()
    {
        var s = MakeDraft();
        var firstStamp = DateTime.UtcNow.AddDays(-1);
        s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, null, false, false, null, "user-1", firstStamp);

        var changes = s.ApplyRegulatoryEdit(
            HaciendaStatus.AlDia, null, null, false, false, null, "user-2", DateTime.UtcNow);

        Assert.That(changes, Is.Empty);
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(firstStamp), "no-op must not refresh freshness");
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("user-1"));
    }

    [Test]
    public void ApplyRegulatoryEdit_PmeAndWarning_AreAuditedAndNormalized()
    {
        var s = MakeDraft();
        var now = DateTime.UtcNow;

        var changes = s.ApplyRegulatoryEdit(null, null, null, true, true, "  ojo con este  ", "user-1", now);

        Assert.That(s.IsPmeOrPyme, Is.True);
        Assert.That(s.HasWarning, Is.True);
        Assert.That(s.WarningNote, Is.EqualTo("ojo con este"), "note is trimmed");
        Assert.That(changes.Select(c => c.Field),
            Is.EquivalentTo(new[] { RegulatoryChangeField.Pme, RegulatoryChangeField.Warning }));

        // Flag off clears the note.
        s.ApplyRegulatoryEdit(null, null, null, true, false, "ignored", "user-1", now);
        Assert.That(s.HasWarning, Is.False);
        Assert.That(s.WarningNote, Is.Null);
    }

    [Test]
    public void ApplyRegulatoryEdit_WarningNoOp_ReturnsEmpty()
    {
        var s = MakeDraft();
        s.ApplyRegulatoryEdit(null, null, null, false, true, "nota", "user-1", DateTime.UtcNow);

        // Re-apply the identical warning flag + note → no change.
        var changes = s.ApplyRegulatoryEdit(null, null, null, false, true, "nota", "user-1", DateTime.UtcNow);

        Assert.That(changes, Is.Empty);
        Assert.That(s.HasWarning, Is.True);
        Assert.That(s.WarningNote, Is.EqualTo("nota"));
    }

    [Test]
    public void ApplyRegulatoryEdit_WarningNoteTooLong_Throws()
    {
        var s = MakeDraft();
        var tooLong = new string('x', Supplier.WarningNoteMaxLength + 1);

        Assert.Throws<ArgumentException>(() =>
            s.ApplyRegulatoryEdit(null, null, null, false, true, tooLong, "user-1", DateTime.UtcNow));
    }

    [Test]
    public void ApplyRegulatoryEdit_MixedChange_ReturnsOnlyChangedField()
    {
        var s = MakeDraft();
        s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, null, false, false, null, "user-1", DateTime.UtcNow);

        // Re-supply Hacienda unchanged, change CCSS → exactly one change record.
        var changes = s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, CcssStatus.AlDia, null, false, false, null, "user-1", DateTime.UtcNow);

        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].Field, Is.EqualTo(RegulatoryChangeField.Ccss));
    }

    [Test]
    public void ConfirmRegulatoryReviewed_RefreshesTimestampWithoutChangingValue()
    {
        var s = MakeDraft();
        s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, null, false, false, null, "user-1",
            DateTime.UtcNow.AddDays(-5));

        var now = DateTime.UtcNow;
        var change = s.ConfirmRegulatoryReviewed(RegulatoryField.Hacienda, "user-2", now);

        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia), "value unchanged");
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(now), "timestamp refreshed");
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("user-2"));
        Assert.That(change.Kind, Is.EqualTo(RegulatoryChangeKind.ReviewedNoChange));
        Assert.That(change.Field, Is.EqualTo(RegulatoryChangeField.Hacienda));
    }

    [Test]
    public void ConfirmRegulatoryReviewed_ThrowsWhenStatusUnset()
    {
        var s = MakeDraft();
        Assert.Throws<InvalidOperationException>(
            () => s.ConfirmRegulatoryReviewed(RegulatoryField.Ccss, "user-1", DateTime.UtcNow));
    }
}
