using System.Linq;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Helpers;

namespace FundingPlatform.Tests.Unit.Web;

/// <summary>
/// Spec 038 (US1/US2) — verbatim status labels + es-CR review-freshness phrasing.
/// </summary>
[TestFixture]
public class RegulatoryDisplayTests
{
    [Test]
    public void Label_ReturnsVerbatimSpanish_OrSinRevisar()
    {
        Assert.That(RegulatoryStatusLabels.Label(HaciendaStatus.AlDia), Is.EqualTo("al día"));
        Assert.That(RegulatoryStatusLabels.Label(HaciendaStatus.DesinscritoDeOficio), Is.EqualTo("desinscrito de oficio"));
        Assert.That(RegulatoryStatusLabels.Label(CcssStatus.EstadoInactivoAlDia), Is.EqualTo("estado inactivo / al día"));
        Assert.That(RegulatoryStatusLabels.Label(SicopStatus.SinSanciones), Is.EqualTo("sin sanciones"));
        Assert.That(RegulatoryStatusLabels.Label((HaciendaStatus?)null), Is.EqualTo("sin revisar"));
        Assert.That(RegulatoryStatusLabels.Label((CcssStatus?)null), Is.EqualTo("sin revisar"));
        Assert.That(RegulatoryStatusLabels.Label((SicopStatus?)null), Is.EqualTo("sin revisar"));
    }

    [Test]
    public void HaciendaItems_LeadsWithSinRevisarBlankOption()
    {
        var items = RegulatoryStatusLabels.HaciendaItems(null).ToList();
        Assert.That(items.First().Value, Is.EqualTo(string.Empty));
        Assert.That(items.First().Text, Is.EqualTo("sin revisar"));
        Assert.That(items.First().Selected, Is.True);
        // Numeric codes are the enum byte values.
        Assert.That(items.Any(i => i.Value == "2" && i.Text == "al día"), Is.True);
    }

    [Test]
    public void HaciendaItems_MarksSelected()
    {
        var items = RegulatoryStatusLabels.HaciendaItems(HaciendaStatus.AlDia).ToList();
        Assert.That(items.Single(i => i.Value == "2").Selected, Is.True);
        Assert.That(items.First(i => i.Value == string.Empty).Selected, Is.False);
    }

    [Test]
    public void Describe_Unreviewed_WhenNoTimestamp()
    {
        Assert.That(ReviewFreshness.Describe(null, "Ana", RegulatoryReviewSource.Manual),
            Is.EqualTo("sin revisar"));
    }

    [Test]
    public void Describe_Today_WithReviewerAndSource()
    {
        var text = ReviewFreshness.Describe(DateTime.UtcNow, "Ana Mora", RegulatoryReviewSource.Manual);
        Assert.That(text, Is.EqualTo("revisado hoy por Ana Mora (manual)"));
    }

    [Test]
    public void Describe_NDaysAgo_WithoutReviewer()
    {
        var text = ReviewFreshness.Describe(DateTime.UtcNow.Date.AddDays(-3), null, null);
        Assert.That(text, Is.EqualTo("revisado hace 3 días"));
    }

    [Test]
    public void Describe_OneDayAgo_UsesSingular()
    {
        var text = ReviewFreshness.Describe(DateTime.UtcNow.Date.AddDays(-1), null, null);
        Assert.That(text, Is.EqualTo("revisado hace 1 día"));
    }

    [Test]
    public void Describe_SourceSuffixes_ForApiAndSystem()
    {
        Assert.That(ReviewFreshness.Describe(DateTime.UtcNow, null, RegulatoryReviewSource.Api),
            Is.EqualTo("revisado hoy (API)"));
        Assert.That(ReviewFreshness.Describe(DateTime.UtcNow, null, RegulatoryReviewSource.System),
            Is.EqualTo("revisado hoy (sistema)"));
    }
}
