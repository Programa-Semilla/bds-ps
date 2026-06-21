namespace FundingPlatform.Domain.Enums;

public enum ApplicationState
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    Resolved = 3,
    AppealOpen = 4,
    ResponseFinalized = 5,
    AgreementExecuted = 6,

    /// <summary>
    /// Spec 040 — the reviewer has completed the reviewer checklist and sent the
    /// application to audit; it now sits in the (group-scoped) auditor inbox awaiting
    /// audit. Brackets the generate-agreement step together with
    /// <see cref="ReturnedFromAudit"/>.
    /// </summary>
    PendingAudit = 7,

    /// <summary>
    /// Spec 040 — the auditor found the application non-compliant and bounced it back
    /// to the reviewer with per-item findings. The reviewer reworks, re-completes the
    /// reviewer checklist, and re-sends to audit (loop). Applicant is never contacted.
    /// </summary>
    ReturnedFromAudit = 8
}
