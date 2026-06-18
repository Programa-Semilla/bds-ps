using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class TimeDurationTests
{
    [Test]
    public void InDays_ForDays_ReturnsValueUnchanged()
    {
        var duration = new TimeDuration(45, DurationUnit.Days);
        Assert.That(duration.InDays, Is.EqualTo(45));
    }

    [Test]
    public void InDays_ForMonths_NormalizesAtThirtyDaysPerMonth()
    {
        var duration = new TimeDuration(2, DurationUnit.Months);
        Assert.That(duration.InDays, Is.EqualTo(60));
    }

    [Test]
    public void InDays_OneMonth_EqualsThirtyDays()
    {
        Assert.That(new TimeDuration(1, DurationUnit.Months).InDays, Is.EqualTo(30));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-30)]
    public void Constructor_RejectsNonPositiveValue(int value)
    {
        Assert.Throws<ArgumentException>(() => new TimeDuration(value, DurationUnit.Days));
    }

    [Test]
    public void Constructor_RejectsUndefinedUnit()
    {
        Assert.Throws<ArgumentException>(() => new TimeDuration(5, (DurationUnit)99));
    }

    [Test]
    public void Equality_SameValueAndUnit_AreEqual()
    {
        Assert.That(new TimeDuration(12, DurationUnit.Months),
            Is.EqualTo(new TimeDuration(12, DurationUnit.Months)));
    }

    [Test]
    public void Equality_DifferentUnit_AreNotEqual()
    {
        // 30 days and 1 month normalize to the same InDays but are distinct values.
        Assert.That(new TimeDuration(30, DurationUnit.Days),
            Is.Not.EqualTo(new TimeDuration(1, DurationUnit.Months)));
    }
}
