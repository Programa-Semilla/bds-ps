namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T011 / FR-012 — recipient classification. Bucket priority is
/// <c>Applicant &gt; Reviewer &gt; Admin</c> (lowest ordinal wins on collision).
/// The dedup pass keeps one recipient per <c>UserId</c> with the
/// lowest-ordinal bucket and its corresponding template variant.
/// </summary>
public enum RecipientBucket
{
    Applicant = 1,
    Reviewer  = 2,
    Admin     = 3,
}
