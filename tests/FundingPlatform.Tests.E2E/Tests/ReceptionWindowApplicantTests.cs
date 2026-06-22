using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 044 — applicant-facing reception-window behavior on real SQL:
/// US3 (T034) timing notice (open/upcoming/closed), US4 (T036) new-draft guard,
/// US2 (T029) submission gating (422 + es-CR). Each test uses an isolated
/// Fund→Process→Group + a group-scoped applicant so the shared fixture is unaffected.
/// </summary>
[Category("ReceptionWindow")]
public class ReceptionWindowApplicantTests : ReceptionWindowE2EBase
{
    private static readonly Regex EsCrInstant = new(@"\d{2}/\d{2}/\d{4} \d{2}:\d{2}");

    /// <summary>Admin builds the isolated chain + an applicant, seeds a window, returns the applicant email.</summary>
    private async Task<string> SetupAsync(string unique, ReceptionWindowSeed.WindowState? state)
    {
        await RegisterAdminAndLoginAsync(unique);
        var processId = await AdminCreateProcessWithGroupAsync($"RWAProc-{unique}", $"RWAG-{unique}");
        var applicantEmail = $"rw_app_{unique}@example.com";
        await AdminCreateApplicantInGroupAsync(applicantEmail, $"RWAPP-{unique}", $"RWAG-{unique}");
        if (state is { } s)
        {
            await ReceptionWindowSeed.SeedAsync(ConnectionString, processId, s);
        }
        await Logout();
        await OnboardAndLoginAsync(applicantEmail, ApplicantPwd);
        return applicantEmail;
    }

    private ILocator Notice => Page.Locator("[data-testid=reception-notice]");

    // ---------------- US3 — notice ----------------

    [Test]
    public async Task Notice_Open_ShowsCountdownAndEsCrInstant()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(unique, ReceptionWindowSeed.WindowState.Open);

        await Page.GotoAsync($"{BaseUrl}/Application/Create");
        await Expect(Notice).ToBeVisibleAsync();
        await Expect(Notice).ToHaveAttributeAsync("data-reception-state", "open");
        await Expect(Page.Locator("[data-testid=reception-countdown]")).ToBeVisibleAsync();
        Assert.That(EsCrInstant.IsMatch(await Notice.InnerTextAsync()), Is.True,
            "Open notice must show a dd/MM/yyyy HH:mm close instant.");
    }

    [Test]
    public async Task Notice_Upcoming_ShowsOpenInstantAndDisabledSubmitNote()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(unique, ReceptionWindowSeed.WindowState.Upcoming);

        await Page.GotoAsync($"{BaseUrl}/Application/Create");
        await Expect(Notice).ToBeVisibleAsync();
        await Expect(Notice).ToHaveAttributeAsync("data-reception-state", "upcoming");
        await Expect(Notice).ToContainTextAsync("abre el");
        await Expect(Page.Locator("[data-testid=reception-submit-disabled-note]")).ToBeVisibleAsync();
        Assert.That(EsCrInstant.IsMatch(await Notice.InnerTextAsync()), Is.True);
    }

    [Test]
    public async Task Notice_Closed_ShowsClosedState()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(unique, ReceptionWindowSeed.WindowState.Closed);

        await Page.GotoAsync($"{BaseUrl}/Application/Create");
        await Expect(Notice).ToBeVisibleAsync();
        await Expect(Notice).ToHaveAttributeAsync("data-reception-state", "closed");
    }

    // ---------------- US4 — draft-creation guard ----------------

    [Test]
    public async Task DraftCreation_BlockedWhenAllWindowsClosed_AllowedWhenUpcoming()
    {
        // Closed → creation refused with an es-CR reason; stays on Create.
        var closedUnique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(closedUnique, ReceptionWindowSeed.WindowState.Closed);
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        Assert.That(Page.Url, Does.Contain("/Application/Create"),
            "All-closed reception ⇒ new draft refused (FR-014), re-renders Create.");
        await Expect(Page.Locator("text=ya cerró")).ToBeVisibleAsync();
        await Logout();

        // Upcoming → creation allowed (a future window still gives a submission chance).
        var upUnique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(upUnique, ReceptionWindowSeed.WindowState.Upcoming);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    // ---------------- US2 — submission gating ----------------

    [Test]
    public async Task Submission_BlockedWhenUpcoming_AllowedWhenOpen()
    {
        // Upcoming: a draft can be created but submission is refused (422 + es-CR).
        var upUnique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(upUnique, ReceptionWindowSeed.WindowState.Upcoming);
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var blockedId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var (status, body) = await CraftedSubmitAsync(blockedId);
        Assert.That(status, Is.EqualTo(422), "Upcoming reception ⇒ submit refused with 422.");
        Assert.That(body, Does.Contain("abre el"), "422 body carries the typed es-CR open-instant message.");
        await Logout();

        // Open: the reception gate passes (submission proceeds past it — a now-incomplete
        // draft then trips item validation, NOT the reception 422).
        var openUnique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(openUnique, ReceptionWindowSeed.WindowState.Open);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var openId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var (openStatus, _) = await CraftedSubmitAsync(openId);
        Assert.That(openStatus, Is.Not.EqualTo(422),
            "Open reception ⇒ the gate does not block (submission proceeds past it).");
    }

    /// <summary>Issues an authenticated, antiforgery-valid POST to the Submit endpoint
    /// carrying the live browser session cookies + a token scraped from the draft editor.
    /// Returns (status, body) after following redirects.</summary>
    private async Task<(int Status, string Body)> CraftedSubmitAsync(int appId)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var token = await Page.Locator("input[name=__RequestVerificationToken]").First.GetAttributeAsync("value")
            ?? throw new InvalidOperationException("No antiforgery token on the editor.");

        var baseUri = new Uri(BaseUrl);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = true,
            CookieContainer = new System.Net.CookieContainer(),
        };
        foreach (var c in await Context.CookiesAsync())
        {
            try { handler.CookieContainer.Add(new System.Net.Cookie(c.Name, c.Value, string.IsNullOrEmpty(c.Path) ? "/" : c.Path, baseUri.Host)); }
            catch { /* skip a cookie the container rejects */ }
        }

        using var client = new HttpClient(handler);
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        });
        var resp = await client.PostAsync($"{BaseUrl}/Application/{appId}/Submit", content);
        var body = await resp.Content.ReadAsStringAsync();
        return ((int)resp.StatusCode, body);
    }
}
