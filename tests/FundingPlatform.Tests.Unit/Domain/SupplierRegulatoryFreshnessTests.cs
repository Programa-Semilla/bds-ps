using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 043 / FR-001 / FR-005 — pure freshness predicate on <see cref="Supplier"/>.
/// A required field (Hacienda/CCSS/SICOP) is stale when its last-reviewed timestamp
/// is null or strictly older than the window cutoff.
/// </summary>
[TestFixture]
public class SupplierRegulatoryFreshnessTests
{
    private const int Window = 30;
    private static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

    private static Supplier MakeDraft() => Supplier.CreateDraft(
        "3-101-123456", "ACME S.A.", 42, "Sede principal",
        null, null, null, null, null, null, null);

    /// <summary>Reviews all three fields at the given instant so we can isolate one field's staleness.</summary>
    private static Supplier WithAllReviewedAt(DateTime at)
    {
        var s = MakeDraft();
        s.ApplyRegulatoryEdit(
            HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
            false, false, null, "user-1", at);
        return s;
    }

    [Test]
    public void NeverReviewed_AllThreeFieldsStale()
    {
        var s = MakeDraft();

        Assert.That(s.IsRegulatoryStale(Window, Now), Is.True);
        Assert.That(s.StaleRequiredFields(Window, Now),
            Is.EquivalentTo(new[] { RegulatoryField.Hacienda, RegulatoryField.Ccss, RegulatoryField.Sicop }));
    }

    [Test]
    public void AllFresh_EmptyResult()
    {
        var s = WithAllReviewedAt(Now.AddDays(-1));

        Assert.That(s.IsRegulatoryStale(Window, Now), Is.False);
        Assert.That(s.StaleRequiredFields(Window, Now), Is.Empty);
    }

    [Test]
    public void ExactlyAtWindowBoundary_IsFresh()
    {
        // last reviewed exactly window days ago == cutoff; cutoff itself is NOT stale (strict <).
        var s = WithAllReviewedAt(Now.AddDays(-Window));

        Assert.That(s.IsRegulatoryStale(Window, Now), Is.False);
        Assert.That(s.StaleRequiredFields(Window, Now), Is.Empty);
    }

    [Test]
    public void JustInsideWindow_IsFresh()
    {
        var s = WithAllReviewedAt(Now.AddDays(-Window).AddSeconds(1));

        Assert.That(s.StaleRequiredFields(Window, Now), Is.Empty);
    }

    [Test]
    public void JustOutsideWindow_IsStale()
    {
        var s = WithAllReviewedAt(Now.AddDays(-Window).AddSeconds(-1));

        Assert.That(s.IsRegulatoryStale(Window, Now), Is.True);
        Assert.That(s.StaleRequiredFields(Window, Now),
            Is.EquivalentTo(new[] { RegulatoryField.Hacienda, RegulatoryField.Ccss, RegulatoryField.Sicop }));
    }

    [Test]
    public void PerFieldIndependence_OnlyStaleFieldReported()
    {
        // Hacienda + Sicop fresh, CCSS stale.
        var s = MakeDraft();
        s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, SicopStatus.SinSanciones,
            false, false, null, "user-1", Now.AddDays(-1));
        s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
            false, false, null, "user-1", Now.AddDays(-90));

        var stale = s.StaleRequiredFields(Window, Now);

        Assert.That(stale, Does.Contain(RegulatoryField.Ccss));
        Assert.That(stale, Does.Not.Contain(RegulatoryField.Hacienda));
        Assert.That(stale, Does.Not.Contain(RegulatoryField.Sicop));
    }
}
