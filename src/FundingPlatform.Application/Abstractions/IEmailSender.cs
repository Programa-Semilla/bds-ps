// Spec 021 — see specs/021-feedback-session-may13/research.md R-11 + contracts/public-routes.md.

namespace FundingPlatform.Application.Abstractions;

/// <summary>
/// Spec 021 / FR-025 / R-11 — single email seam used by the stage-expiry reminder
/// service (and any future spec 021 email path like forgot-password). Production
/// binding is <c>SmtpEmailSender</c>; integration tests replace it with a
/// <c>CapturingEmailSender</c> queue. The interface is intentionally narrow
/// (one method) to keep the test double trivial and to keep the body / subject
/// composition outside the SMTP transport.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends the supplied <paramref name="message"/>. Implementations MUST throw on
    /// transport failure so the caller (reminder service) can apply the
    /// exponential-backoff retry policy mandated by NFR-002.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Spec 021 — single email envelope used across all direct-send senders.
/// <c>HtmlBody</c> is the branded inline-styled HTML body (spec 041 routes these
/// through the shared <c>_EmailLayout</c>). <c>TextBody</c> is the optional
/// plain-text twin (spec 041 / FR-009) — when present the SMTP sender ships a
/// multipart/alternative message.
/// </summary>
public sealed record EmailMessage(
    string ToAddress,
    string Subject,
    string HtmlBody,
    string? TextBody = null);
