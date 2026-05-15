using System.Net;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Infrastructure.Notifications.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 021 / T069 — error-classification table for the Mailgun HTTP path.
/// Drives the sender through an <see cref="HttpMessageHandler"/> mock per
/// row of the table in contracts/IEmailSender.md.
/// </summary>
[TestFixture]
public class MailgunHttpEmailSenderTests
{
    private static MailgunHttpEmailSender BuildSender(HttpStatusCode status, string body = "")
    {
        var handler = new MockHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
        });
        var http = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Mailgun:ApiKey"] = "test-key",
                ["Notifications:Mailgun:Domain"] = "mg.example.com",
                ["Notifications:Mailgun:BaseUrl"] = "https://api.mailgun.net/v3",
                ["Notifications:Sender:Name"] = "Programa Semilla",
                ["Notifications:Sender:Email"] = "no-reply@programa-semilla.cr",
            }).Build();
        return new MailgunHttpEmailSender(http, config, NullLogger<MailgunHttpEmailSender>.Instance);
    }

    private static EmailMessage SampleMessage() => new(
        ToEmail: "user@example.com",
        ToDisplayName: "Test User",
        Subject: "Subject",
        HtmlBody: "<p>html</p>",
        TextBody: "text",
        ReplyTo: null,
        Headers: null);

    [Test]
    public async Task Http_200_maps_to_Sent_and_extracts_provider_message_id()
    {
        var sender = BuildSender(HttpStatusCode.OK, "{\"id\":\"<abc@mg.example.com>\"}");
        var result = await sender.SendAsync(SampleMessage(), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.Sent));
        Assert.That(result.ProviderMessageId, Is.EqualTo("<abc@mg.example.com>"));
    }

    [Test]
    public async Task Http_400_maps_to_PermanentFailure()
    {
        var sender = BuildSender(HttpStatusCode.BadRequest, "{\"message\":\"bad request\"}");
        var result = await sender.SendAsync(SampleMessage(), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.PermanentFailure));
    }

    [Test]
    public async Task Http_429_maps_to_TransientFailure()
    {
        var sender = BuildSender(HttpStatusCode.TooManyRequests, "{\"message\":\"slow down\"}");
        var result = await sender.SendAsync(SampleMessage(), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.TransientFailure));
    }

    [Test]
    public async Task Http_500_maps_to_TransientFailure()
    {
        var sender = BuildSender(HttpStatusCode.InternalServerError, "");
        var result = await sender.SendAsync(SampleMessage(), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.TransientFailure));
    }

    [Test]
    public async Task Missing_config_maps_to_PermanentFailure()
    {
        var http = new HttpClient(new MockHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var config = new ConfigurationBuilder().Build();  // no Mailgun:* keys
        var sender = new MailgunHttpEmailSender(http, config,
            NullLogger<MailgunHttpEmailSender>.Instance);

        var result = await sender.SendAsync(SampleMessage(), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.PermanentFailure));
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
        public MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _fn(request, ct);
    }
}
