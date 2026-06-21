namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 040 / D6 — the recorded outcome of a single checklist item on an application.
/// A reviewer only ever produces <see cref="Checked"/>; an auditor produces either
/// <see cref="Checked"/> (compliant) or <see cref="NotCompliant"/> (which requires a
/// non-empty reason and routes the application back to the reviewer).
/// </summary>
public enum ChecklistResponseStatus
{
    Checked = 1,
    NotCompliant = 2
}
