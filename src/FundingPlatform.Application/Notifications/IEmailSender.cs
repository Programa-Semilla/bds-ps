namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T010 / contracts/IEmailSender.md — provider abstraction for
/// outbound transactional mail. Implementations: <c>MailtrapSmtpEmailSender</c>
/// (MailKit v3 SMTP path), <c>MailgunHttpEmailSender</c> (raw HttpClient),
/// <c>NoOpEmailSender</c> (logs + returns Sent). Wrapped by
/// <c>RecipientAllowlistFilter</c> decorator outside Production (FR-017).
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Send one fully-rendered email to one recipient.
    /// MUST NOT throw on provider-side failures — return a classified
    /// <see cref="EmailSendResult"/> instead. MAY throw on programmer errors.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Spec 021 / contracts/IEmailSender.md — fully-rendered email payload handed
/// to <see cref="IEmailSender"/>. Both HTML and plain-text are always set
/// (FR-024 ships text fallback for every variant). Headers passes through
/// verbatim (Mailgun X-Mailgun-Variables, etc.).
/// </summary>
public sealed record EmailMessage(
    string ToEmail,
    string ToDisplayName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyTo,
    IReadOnlyDictionary<string, string>? Headers);

/// <summary>
/// Spec 021 / contracts/IEmailSender.md — classified outcome of a single send
/// attempt. The worker reads <see cref="Outcome"/> to decide retry vs
/// dead-letter (FR-021, FR-022).
/// </summary>
public sealed record EmailSendResult(
    EmailSendOutcome Outcome,
    string? ProviderMessageId,
    string? ErrorMessage);

/// <summary>
/// Spec 021 / FR-021 / FR-022 — error-classification axis returned by every
/// <see cref="IEmailSender"/> implementation. Mapped from provider signals
/// per the table in contracts/IEmailSender.md.
/// </summary>
public enum EmailSendOutcome
{
    /// <summary>Provider accepted the message (HTTP 2xx / SMTP 2xx).</summary>
    Sent              = 1,

    /// <summary>Retry per backoff (timeout, 5xx, 429, DNS/connect refused).</summary>
    TransientFailure  = 2,

    /// <summary>Dead-letter immediately — no retry (4xx, hard bounce, render exception).</summary>
    PermanentFailure  = 3,

    /// <summary>Dropped by <c>RecipientAllowlistFilter</c>. The wrapped sender is not invoked.</summary>
    BlockedByAllowlist = 4,
}
