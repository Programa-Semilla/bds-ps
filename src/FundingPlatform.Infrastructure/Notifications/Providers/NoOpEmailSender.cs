using FundingPlatform.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Notifications.Providers;

/// <summary>
/// Spec 021 / T038 / FR-015 — emergency-disable / unconfigured-fallback
/// implementation. Logs WARN with the would-be subject + recipient and
/// returns <see cref="EmailSendOutcome.Sent"/> with a null provider id so
/// the worker writes a delivery row but no bytes leave the host.
///
/// <para>
/// Selected automatically in non-Production when no provider config is set
/// (FR-015). In Production the only way to land on NoOp is an explicit
/// <c>Notifications:Provider=NoOp</c> — meant as a break-glass for outages
/// (a CRIT log line MUST be emitted on boot in that case; the worker does
/// not block).
/// </para>
/// </summary>
public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogWarning(
            "NoOp email provider active. Email NOT sent. recipient={Recipient} subject={Subject}",
            message.ToEmail, message.Subject);

        return Task.FromResult(new EmailSendResult(
            EmailSendOutcome.Sent,
            ProviderMessageId: null,
            ErrorMessage: null));
    }
}
