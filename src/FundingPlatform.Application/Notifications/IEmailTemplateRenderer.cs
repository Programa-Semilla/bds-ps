using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T023 / FR-023 / FR-024 — Razor renderer abstraction. Implementation
/// lives in the Web project (depends on ASP.NET Core MVC Razor types — same
/// pattern as <c>IFundingAgreementHtmlRenderer</c>).
///
/// <para>
/// The implementation renders every notification variant via
/// <c>Views/Emails/{ViewName}.cshtml</c> for HTML and
/// <c>Views/Emails/{ViewName}.text.cshtml</c> for the plain-text fallback,
/// under the shared <c>_EmailLayout.cshtml</c> layout. A render exception
/// surfaces as <see cref="EmailRenderException"/> so the worker can map it
/// to <see cref="EmailSendOutcome.PermanentFailure"/> (FR-022).
/// </para>
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>Renders one event variant for one recipient bucket.</summary>
    Task<RenderedEmail> RenderAsync(
        NotificationEvent eventType,
        NotificationRecipient recipient,
        NotificationPayload payload,
        CancellationToken ct);
}

/// <summary>Spec 021 / T023 — fully-rendered email body pair.</summary>
public sealed record RenderedEmail(
    string Subject,
    string HtmlBody,
    string TextBody);

/// <summary>
/// Spec 021 / FR-022 — thrown by <see cref="IEmailTemplateRenderer"/> implementations
/// when the underlying Razor render throws. The worker catches it and routes the
/// outbox row to <c>DeadLetter</c> with the message in <c>LastError</c>.
/// </summary>
public sealed class EmailRenderException : Exception
{
    public EmailRenderException(string message) : base(message) { }
    public EmailRenderException(string message, Exception inner) : base(message, inner) { }
}
