using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 041 / US3 / T029 / FR-010/FR-012 — completing a password reset fires
/// exactly one branded "Tu contraseña fue actualizada" confirmation to that user,
/// with NO CTA button (FR-005 — no link variable) and the support phone present.
/// </summary>
public class PasswordChangedNotificationE2ETests : AuthenticatedTestBase
{
    [TearDown]
    public async Task TearDown()
    {
        if (MailCapture is not null) await MailCapture.DrainAsync();
    }

    [Test]
    public async Task PasswordReset_fires_one_branded_password_changed_email()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 021 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }
        await MailCapture.DrainAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var email = $"pwch_{uniqueId}@programa-semilla.test";

        await RegisterUserAsync(Page, email, password, "Pw", "Changer", $"P-{uniqueId}");
        await MailCapture.DrainAsync();

        // Obtain a fresh set-password link via the dev seam and complete the reset
        // (ConsumePasswordResetTokenHandler → password-changed confirmation).
        string link;
        using (var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        using (var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) })
        {
            var response = await client.GetAsync(
                $"/Account/LatestPasswordResetLink?email={Uri.EscapeDataString(email)}");
            response.EnsureSuccessStatusCode();
            link = (await response.Content.ReadAsStringAsync()).Trim();
        }
        await SetPasswordViaInviteAsync(link, "NewPass123!");

        var messages = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Tu contraseña fue actualizada"));

        var toUser = messages.Where(m =>
            m.ToAddresses.Any(t => t.Contains(email, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.That(toUser, Has.Count.EqualTo(1),
            "FR-012: exactly one password-changed confirmation to the affected user.");

        var msg = toUser[0];
        Assert.Multiple(() =>
        {
            // Branded shell present.
            Assert.That(msg.HtmlBody, Does.Contain("<img"),
                "Spec 041 / FR-002: branded email carries the hosted logo + partner strip.");
            // The footer phone renders correctly in mail clients; in the HTML source
            // Razor encodes "+" as "&#x2B;", so assert on the (unencoded) digits.
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Contain("4600-1234"),
                "FR-006: support phone present.");
            // NO CTA (FR-005): the fallback-link copy only appears when a CTA is rendered.
            Assert.That(msg.HtmlBody, Does.Not.Contain("Si el botón no funciona"),
                "FR-005: no CTA button + no fallback link (no link variable).");
            // No legacy brand leakage.
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Capital Semilla"));
        });
    }
}
