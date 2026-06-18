using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 039 — an immutable value + unit duration used for a quotation's delivery
/// lead time and warranty time. <see cref="InDays"/> normalizes to days using the
/// 30-days-per-month constant (research D5) for cross-quote comparison only; the
/// normalized value is never persisted. Construction enforces the
/// <c>Value &gt; 0</c> and "defined unit" invariants so the entity can never carry
/// an invalid duration.
/// </summary>
public record TimeDuration
{
    /// <summary>Spec 039 — month-to-days normalization constant (comparison only).</summary>
    public const int DaysPerMonth = 30;

    public int Value { get; }
    public DurationUnit Unit { get; }

    public TimeDuration(int value, DurationUnit unit)
    {
        if (value <= 0)
        {
            throw new ArgumentException("Duration value must be greater than zero.", nameof(value));
        }
        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentException("Duration unit must be a defined value.", nameof(unit));
        }

        Value = value;
        Unit = unit;
    }

    /// <summary>
    /// The duration normalized to whole days (research D5: 1 month = 30 days).
    /// Used as the comparison key by the recommendation algorithm.
    /// </summary>
    public int InDays => Unit == DurationUnit.Months ? Value * DaysPerMonth : Value;

    // Parameterless ctor for EF owned-type materialization. EF sets the backing
    // fields via the private setters of an OwnsOne mapping; the public ctor stays
    // the only validated construction path for application code.
    private TimeDuration()
    {
        Value = 1;
        Unit = DurationUnit.Days;
    }
}
