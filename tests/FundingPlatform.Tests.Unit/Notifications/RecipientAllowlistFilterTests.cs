using FundingPlatform.Application.Notifications;
using FundingPlatform.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 021 / T075 / FR-017 / FR-018 / FR-019 — allowlist decorator behavior.
/// Drop / pass-through paths. Production bypass is enforced at DI registration
/// (not inside the filter), so the unit test only validates the per-recipient
/// match logic; the production-bypass invariant is covered by
/// <c>NotificationsServiceCollectionExtensions.AddNotifications</c>.
/// </summary>
[TestFixture]
public class RecipientAllowlistFilterTests
{
    private static IConfiguration BuildConfig(params string[] allowlist)
    {
        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < allowlist.Length; i++)
        {
            dict[$"Notifications:NonProdAllowlist:{i}"] = allowlist[i];
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static EmailMessage Msg(string to) =>
        new(to, "Test User", "Subject", "<p>html</p>", "text", null, null);

    [Test]
    public async Task Empty_allowlist_drops_every_recipient()
    {
        var inner = Substitute.For<IEmailSender>();
        var filter = new RecipientAllowlistFilter(inner, BuildConfig(),
            NullLogger<RecipientAllowlistFilter>.Instance);

        var result = await filter.SendAsync(Msg("real-user@gmail.com"), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.BlockedByAllowlist));
        Assert.That(result.ErrorMessage, Is.EqualTo("NotAllowlisted"));
        await inner.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Exact_email_match_passes_through_to_wrapped_sender()
    {
        var inner = Substitute.For<IEmailSender>();
        inner.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new EmailSendResult(EmailSendOutcome.Sent, "id-1", null));

        var filter = new RecipientAllowlistFilter(
            inner, BuildConfig("qa@programa-semilla.test"),
            NullLogger<RecipientAllowlistFilter>.Instance);

        var result = await filter.SendAsync(Msg("qa@programa-semilla.test"), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.Sent));
        await inner.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Domain_suffix_match_passes_through()
    {
        var inner = Substitute.For<IEmailSender>();
        inner.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new EmailSendResult(EmailSendOutcome.Sent, "id-2", null));

        var filter = new RecipientAllowlistFilter(
            inner, BuildConfig("@programa-semilla.test"),
            NullLogger<RecipientAllowlistFilter>.Instance);

        var result = await filter.SendAsync(Msg("anyone@programa-semilla.test"), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.Sent));
    }

    [Test]
    public async Task Non_matching_recipient_drops_even_when_others_are_allowlisted()
    {
        var inner = Substitute.For<IEmailSender>();
        var filter = new RecipientAllowlistFilter(
            inner, BuildConfig("qa@programa-semilla.test", "@programa-semilla.test"),
            NullLogger<RecipientAllowlistFilter>.Instance);

        var result = await filter.SendAsync(Msg("real-user@gmail.com"), CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.BlockedByAllowlist));
        await inner.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }
}
