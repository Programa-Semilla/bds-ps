namespace FundingPlatform.Application.Suppliers.Notifications;

/// <summary>
/// Spec 041 / US4 / FR-013 — emails the review pool when a new applicant
/// <c>Company</c> is entered for review. Because the event is NOT application-keyed,
/// it cannot use the application outbox; this mirrors the spec-038
/// <see cref="IProviderCreatedNotifier"/> notifier pattern instead.
///
/// <para><b>DEFERRED (OQ-1).</b> The live trigger (which lifecycle event enters a
/// company "for review") and the recipient pool (reviewers vs auditors) are not yet
/// confirmed. The branded template + this seam exist and are render-tested only —
/// <b>no call site invokes this method</b>. Activating it later is a one-call-site
/// change once OQ-1 lands. Best-effort: implementations MUST NOT throw to the caller.</para>
/// </summary>
public interface ICompanyForReviewNotifier
{
    /// <summary>Render + (once OQ-1 lands) deliver the "nueva empresa para revisión" email.</summary>
    Task NotifyAsync(int companyId, CancellationToken ct);
}
