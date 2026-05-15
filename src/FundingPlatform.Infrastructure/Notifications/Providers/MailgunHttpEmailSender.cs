using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FundingPlatform.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Notifications.Providers;

/// <summary>
/// Spec 021 / T067 / FR-014 — Mailgun HTTP API path using raw
/// <see cref="HttpClient"/> (no Mailgun-specific NuGet). POSTs to
/// <c>${BaseUrl}/${Domain}/messages</c> with Basic auth <c>api:${ApiKey}</c>
/// and <c>multipart/form-data</c> body.
///
/// <para>
/// Error classification per the contract:
/// HTTP 2xx → Sent. HTTP 4xx (except 429) → PermanentFailure.
/// HTTP 429 / 5xx → TransientFailure. Timeout / DNS / connect → TransientFailure.
/// </para>
/// </summary>
public sealed class MailgunHttpEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<MailgunHttpEmailSender> _logger;

    public MailgunHttpEmailSender(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<MailgunHttpEmailSender> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var apiKey = _config["Notifications:Mailgun:ApiKey"];
        var domain = _config["Notifications:Mailgun:Domain"];
        var baseUrl = _config["Notifications:Mailgun:BaseUrl"] ?? "https://api.mailgun.net/v3";
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(domain))
        {
            return new EmailSendResult(
                EmailSendOutcome.PermanentFailure,
                null,
                "Mailgun ApiKey or Domain not configured");
        }

        var senderName = _config["Notifications:Sender:Name"]
            ?? "Programa Semilla / Sistema de Banca para el Desarrollo";
        var senderEmail = _config["Notifications:Sender:Email"]
            ?? "no-reply@programa-semilla.cr";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent($"{senderName} <{senderEmail}>"), "from");
        content.Add(new StringContent($"{message.ToDisplayName} <{message.ToEmail}>"), "to");
        content.Add(new StringContent(message.Subject), "subject");
        content.Add(new StringContent(message.HtmlBody), "html");
        content.Add(new StringContent(message.TextBody), "text");
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            content.Add(new StringContent(message.ReplyTo), "h:Reply-To");
        }
        if (message.Headers is not null)
        {
            foreach (var (key, value) in message.Headers)
            {
                content.Add(new StringContent(value), $"h:{key}");
            }
        }

        var requestUri = $"{baseUrl.TrimEnd('/')}/{domain}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                string? providerMessageId = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                    {
                        providerMessageId = idEl.GetString();
                    }
                }
                catch (JsonException) { /* ignore — keep id null */ }

                return new EmailSendResult(EmailSendOutcome.Sent, providerMessageId, null);
            }

            // 429 + 5xx → transient.
            var code = (int)response.StatusCode;
            var outcome = response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500
                ? EmailSendOutcome.TransientFailure
                : EmailSendOutcome.PermanentFailure;

            _logger.LogWarning(
                "Mailgun {Status} sending to {Recipient}: {Body}",
                response.StatusCode, message.ToEmail, body);

            return new EmailSendResult(outcome, null, $"Mailgun HTTP {code}: {body}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex,
                "Transient HTTP error sending to {Recipient}", message.ToEmail);
            return new EmailSendResult(EmailSendOutcome.TransientFailure, null, ex.Message);
        }
    }
}
