using FundingPlatform.Application.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace FundingPlatform.Infrastructure.Notifications.Providers;

/// <summary>
/// Spec 021 / T039 / FR-014 — SMTP path backed by MailKit v3 (MIT). Builds
/// a <c>multipart/alternative</c> <see cref="MimeMessage"/> with HTML +
/// plain-text and sends via <see cref="SmtpClient"/>. Maps SMTP outcomes
/// to <see cref="EmailSendOutcome"/> per the table in contracts/IEmailSender.md.
///
/// <para>
/// In Local the smtp4dev sidecar resolves the host/port via Aspire service
/// discovery (env var <c>services__smtp4dev__smtp__0</c>). When that env var
/// is set it overrides <c>Notifications:Mailtrap:Host</c>/<c>Port</c>; when
/// it is not, the configured fallback is used (real Mailtrap or a custom
/// SMTP host).
/// </para>
/// </summary>
public sealed class MailtrapSmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<MailtrapSmtpEmailSender> _logger;

    public MailtrapSmtpEmailSender(IConfiguration config, ILogger<MailtrapSmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var (host, port) = ResolveEndpoint();
        var username = _config["Notifications:Mailtrap:Username"];
        var password = _config["Notifications:Mailtrap:Password"];

        var senderName = _config["Notifications:Sender:Name"]
            ?? "Programa Semilla / Sistema de Banca para el Desarrollo";
        var senderEmail = _config["Notifications:Sender:Email"]
            ?? "no-reply@programa-semilla.cr";

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(senderName, senderEmail));
        mime.To.Add(new MailboxAddress(message.ToDisplayName, message.ToEmail));
        mime.Subject = message.Subject;
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }
        if (message.Headers is not null)
        {
            foreach (var (key, value) in message.Headers)
            {
                mime.Headers.Add(key, value);
            }
        }

        var alternative = new MultipartAlternative
        {
            new TextPart("plain") { Text = message.TextBody },
            new TextPart("html")  { Text = message.HtmlBody },
        };
        mime.Body = alternative;

        try
        {
            using var client = new SmtpClient();
            // smtp4dev sidecar (Local) speaks plain SMTP on port 25 — no TLS
            // negotiation. Mailtrap (cloud) supports STARTTLS on alt ports.
            // Pick the socket-options policy per the port: 25 → None,
            // 465 → SslOnConnect, anything else → StartTlsWhenAvailable.
            var socketOptions = port switch
            {
                25 => SecureSocketOptions.None,
                465 => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTlsWhenAvailable,
            };
            await client.ConnectAsync(host, port, socketOptions, ct);
            if (!string.IsNullOrWhiteSpace(username))
            {
                await client.AuthenticateAsync(username, password ?? string.Empty, ct);
            }
            var providerMessageId = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            return new EmailSendResult(
                EmailSendOutcome.Sent,
                ProviderMessageId: string.IsNullOrEmpty(providerMessageId) ? mime.MessageId : providerMessageId,
                ErrorMessage: null);
        }
        catch (SmtpCommandException ex)
        {
            // 5xx permanent codes (RFC 5321): 550 MailboxUnavailable, 552 ExceededStorageAllocation, 521 (server does not accept mail).
            var outcome = ex.StatusCode is SmtpStatusCode.MailboxUnavailable
                or SmtpStatusCode.ExceededStorageAllocation
                ? EmailSendOutcome.PermanentFailure
                : EmailSendOutcome.TransientFailure;
            _logger.LogWarning(ex,
                "SMTP command error sending to {Recipient}: {StatusCode}",
                message.ToEmail, ex.StatusCode);
            return new EmailSendResult(outcome, null, $"SMTP {ex.StatusCode}: {ex.Message}");
        }
        catch (Exception ex) when (ex is SmtpProtocolException or TimeoutException or System.Net.Sockets.SocketException)
        {
            _logger.LogWarning(ex,
                "Transient SMTP error sending to {Recipient}", message.ToEmail);
            return new EmailSendResult(EmailSendOutcome.TransientFailure, null, ex.Message);
        }
    }

    private (string Host, int Port) ResolveEndpoint()
    {
        // Aspire propagates the smtp4dev sidecar's host:port via env vars
        // matching its WithReference convention. The smtp endpoint surfaces as
        // services__smtp4dev__smtp__0 = tcp://host:port (or just host:port).
        var aspireRaw = Environment.GetEnvironmentVariable("services__smtp4dev__smtp__0")
                     ?? _config["services:smtp4dev:smtp:0"];
        if (!string.IsNullOrWhiteSpace(aspireRaw))
        {
            // tcp://host:port form
            if (Uri.TryCreate(aspireRaw, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                _logger.LogDebug("Resolved smtp4dev endpoint from Aspire URI: {Host}:{Port}", uri.Host, uri.Port);
                return (uri.Host, uri.Port);
            }
            // host:port form
            var trimmed = aspireRaw.Trim();
            var colonIdx = trimmed.LastIndexOf(':');
            if (colonIdx > 0 && int.TryParse(trimmed[(colonIdx + 1)..], out var aspirePort) && aspirePort > 0)
            {
                var hostPart = trimmed[..colonIdx];
                _logger.LogDebug("Resolved smtp4dev endpoint from Aspire host:port: {Host}:{Port}", hostPart, aspirePort);
                return (hostPart, aspirePort);
            }
        }

        var host = _config["Notifications:Mailtrap:Host"] ?? "localhost";
        var portStr = _config["Notifications:Mailtrap:Port"];
        var port = int.TryParse(portStr, out var p) ? p : 25;
        _logger.LogDebug("Resolved smtp4dev endpoint from config fallback: {Host}:{Port}", host, port);
        return (host, port);
    }
}
