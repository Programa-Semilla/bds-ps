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
    public async Task Submission_ReviewButtonDisabledWithTimingReason_WhenUpcoming()
    {
        // A draft can be created during an upcoming window, but the Review submit
        // surface (where submission actually happens) must disable the confirm
        // button and explain the timing block (FR-013 / SC-003).
        var upUnique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(upUnique, ReceptionWindowSeed.WindowState.Upcoming);
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        await GotoReviewAsync();
        await Expect(Notice).ToHaveAttributeAsync("data-reception-state", "upcoming");
        await Expect(Page.Locator("[data-testid=review-confirm-submit]")).ToBeDisabledAsync();
        await Expect(Page.Locator("[data-testid=review-cannot-submit-timing]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Submission_ReviewShowsOpenNotice_NoTimingBlock_WhenOpen()
    {
        // During an open window the reception gate adds no timing block on Review;
        // the open notice shows (any remaining disable is field-completeness only).
        var openUnique = Guid.NewGuid().ToString("N")[..6];
        await SetupAsync(openUnique, ReceptionWindowSeed.WindowState.Open);
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        await GotoReviewAsync();
        await Expect(Notice).ToHaveAttributeAsync("data-reception-state", "open");
        await Expect(Page.Locator("[data-testid=review-cannot-submit-timing]")).ToHaveCountAsync(0);
    }

    /// <summary>From the draft editor, follow the submit button's data-review-url to the
    /// /Applications/{publicCode}/Review surface (where submission actually occurs).</summary>
    private async Task GotoReviewAsync()
    {
        var reviewUrl = await Page.Locator("[data-testid=application-edit-submit]")
            .GetAttributeAsync("data-review-url")
            ?? throw new InvalidOperationException("Edit submit button has no data-review-url.");
        await Page.GotoAsync($"{BaseUrl}{reviewUrl}");
    }
}
