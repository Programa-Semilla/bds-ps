namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 039 — unit of a <see cref="ValueObjects.TimeDuration"/> (delivery lead
/// time / warranty on a <see cref="Entities.Quotation"/>). Stored as TINYINT via
/// <c>HasConversion&lt;byte&gt;()</c>, mirroring the slice-A regulatory-status
/// enums. Verbatim es-CR labels ("días" / "meses") live in the Web display map
/// (<c>DurationUnitLabels</c>) — no Spanish literals in the domain.
/// </summary>
public enum DurationUnit : byte
{
    /// <summary>"días"</summary>
    Days = 1,

    /// <summary>"meses"</summary>
    Months = 2,
}
