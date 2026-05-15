using FundingPlatform.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Notifications;

/// <summary>
/// Spec 021 / T072 / FR-017 / FR-018 / FR-019 — non-prod allowlist guard.
/// Wraps any <see cref="IEmailSender"/>. Drops recipients whose full email
/// AND email-domain is NOT in <c>Notifications:NonProdAllowlist</c>, recording
/// the drop as <see cref="EmailSendOutcome.BlockedByAllowlist"/> so the worker
/// can persist a <c>NotificationDelivery</c> row with
/// <c>Status=BlockedByAllowlist</c> and <c>LastError="NotAllowlisted"</c>.
///
/// <para>
/// Empty allowlist outside Production is fail-closed (FR-018): zero emails
/// reach the wrapped sender. In Production this decorator is never
/// registered (FR-019), so the call site is the bare sender.
/// </para>
/// </summary>
public sealed class RecipientAllowlistFilter : IEmailSender
{
    private readonly IEmailSender _inner;
    private readonly IConfiguration _config;
    private readonly ILogger<RecipientAllowlistFilter> _logger;

    public RecipientAllowlistFilter(
        IEmailSender inner,
        IConfiguration config,
        ILogger<RecipientAllowlistFilter> logger)
    {
        _inner = inner;
        _config = config;
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var allowlist = _config.GetSection("Notifications:NonProdAllowlist").Get<string[]>()
                        ?? Array.Empty<string>();

        if (!IsAllowlisted(message.ToEmail, allowlist))
        {
            _logger.LogInformation(
                "Allowlist dropped recipient {Recipient}. Allowlist count={Count}",
                message.ToEmail, allowlist.Length);

            return Task.FromResult(new EmailSendResult(
                EmailSendOutcome.BlockedByAllowlist,
                ProviderMessageId: null,
                ErrorMessage: "NotAllowlisted"));
        }

        return _inner.SendAsync(message, ct);
    }

    private static bool IsAllowlisted(string email, IReadOnlyList<string> allowlist)
    {
        if (allowlist.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(email)) return false;
        var normalized = email.Trim();
        var atIndex = normalized.LastIndexOf('@');
        var domain = atIndex >= 0 ? normalized[atIndex..] : null;

        foreach (var entry in allowlist)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var trimmed = entry.Trim();
            // Exact email or "@domain" suffix match.
            if (string.Equals(trimmed, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
            if (trimmed.StartsWith('@') && domain is not null &&
                string.Equals(trimmed, domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
