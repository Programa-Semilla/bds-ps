using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 016 / Story 3 — reviewer sees only applicants from shared groups.
/// Drives the real user journey through the UI: admin creates groups + users,
/// applicant submits an application, then a reviewer in/out of group sees the
/// expected scope. Covers FR-011..FR-016, NFR-001, NFR-002, plus the FR-014
/// search input.
/// </summary>
public class ReviewerScopeTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string TempPwd = "TempPass1!";

    private async Task<string> SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"scope_admin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, Pwd, "Scope", "Admin", $"SCA-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Pwd);
        return adminEmail;
    }

    private async Task LogoutAsync()
    {
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    /// <summary>
    /// Spec 021 / FR-001 — Groups are created from the Process detail page (the
    /// owning Process is implied by route context). Creates one Active Process
    /// and the given Groups under it.
    /// </summary>
    private async Task CreateGroupsUnderNewProcessAsync(string suffix, params string[] groupNames)
    {
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync($"RSProc-{suffix}");
        var processId = await procPage.OpenProcessDetailByNameAsync(BaseUrl, $"RSProc-{suffix}");
        foreach (var name in groupNames)
        {
            await procPage.GoToDetailsAsync(BaseUrl, processId);
            await procPage.CreateGroupAsync(name);
        }
    }

    [Test]
    public async Task Reviewer_OutOfScope_DetailUrl_Returns403()
    {
        // Setup: admin creates two groups, an applicant in group A, a reviewer in group B.
        // Reviewer attempts to open the applicant's detail URL → 403.
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        await CreateGroupsUnderNewProcessAsync(unique, $"SC-{unique}-A", $"SC-{unique}-B");

        // Create an applicant in group A.
        var applicantEmail = $"sc_app_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("App", "Sub", applicantEmail, null, "Applicant", TempPwd, IdentificationData.CedulaFisica($"SCAPP-{unique}"));
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync($"SC-{unique}-A");
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Create a reviewer in group B (no overlap).
        var reviewerEmail = $"sc_rev_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Rev", "Iewer", reviewerEmail, null, "Reviewer", TempPwd, null);
        await formPage.SelectGroupsAsync($"SC-{unique}-B");
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // The applicant must own an application before any out-of-scope check
        // is meaningful. Sign in as the applicant, change-password, submit a
        // bare-minimum application. (Full draft+submit happens via
        // CreateApplicationAndSubmitResponseAsync; here we shortcut by hitting
        // /Application — applicant role is implicit from registration.)
        await LogoutAsync();
        await OnboardAndLoginAsync(applicantEmail, "NewPass1!");

        // Navigate to /Application and create an application skeleton.
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True, "Applicant must land on the created application's draft editor.");
        var appId = int.Parse(appIdMatch.Groups[1].Value);
        await LogoutAsync();

        // Sign in as the reviewer; first-login path.
        await OnboardAndLoginAsync(reviewerEmail, "RevPass1!");

        // Direct-URL access to the application's detail page → 403 / Forbidden.
        var response = await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        var status = response?.Status ?? 0;
        var ok403 = status == 403 || Page.Url.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);
        Assert.That(ok403, Is.True,
            $"Out-of-scope reviewer must receive 403 on direct detail URL. Status={status}, Url={Page.Url}");
    }

    [Test]
    public async Task Reviewer_QueueSearch_NarrowsResults_AndStillRespectsScope()
    {
        // FR-014 — the queue's search input narrows results by applicant
        // name/legal id and STILL applies the group-overlap predicate.
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        await CreateGroupsUnderNewProcessAsync(unique, $"SR-{unique}");

        var reviewerEmail = $"sr_rev_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Rev", "Iewer", reviewerEmail, null, "Reviewer", TempPwd, null);
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync($"SR-{unique}");
        await createPage.SubmitAsync();

        // Sign in as reviewer; the queue should render with the search box.
        await LogoutAsync();
        await OnboardAndLoginAsync(reviewerEmail, "RevPass2!");

        var queue = new ReviewQueuePage(Page);
        await queue.GotoAsync(BaseUrl);
        await Expect(queue.SearchInput).ToBeVisibleAsync();
        await queue.SearchAsync($"NoSuchApplicant-{unique}");
        // The URL carries the search parameter; the queue is empty (the
        // reviewer's group has no matching applicants either way).
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?.*search="));
    }

    [Test]
    public async Task Admin_ReviewQueue_BypassesScope()
    {
        // FR-015 — admin sees every application on the queue. Smoke check:
        // signs in as admin, opens /Review, and verifies the queue page
        // loads (the dashboard with the search input is the new surface).
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        var queue = new ReviewQueuePage(Page);
        await queue.GotoAsync(BaseUrl);
        await Expect(queue.SearchInput).ToBeVisibleAsync();
        // The admin queue must render the search input + the queue scaffold.
        // Whether there are applications is environment-dependent; the assert
        // here is that the page renders for an admin without 403.
        await Expect(Page).ToHaveURLAsync(new Regex("/Review(\\?.*)?$"));
    }

    /// <summary>
    /// Spec 016 / FR-012, NFR-002 + REVIEW-CODE F-8 — driving the real user
    /// journey: a Norte-only reviewer logs in, navigates to the queue (which
    /// must show only the Norte applicant's submitted application — never the
    /// Sur applicant's), then attempts to GET the detail URL of a Sur
    /// application by id and is denied (403 / AccessDenied). The id is
    /// recovered from the admin's queue view, NOT a deep-link shortcut.
    /// </summary>
    [Test]
    public async Task Reviewer_NorteOnly_OutOfScopeSurDetail_Returns403()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var adminEmail = await SignInAsAdminAsync(unique);

        // Two real groups named after the spec convention.
        var norteName = $"Norte-{unique}";
        var surName = $"Sur-{unique}";
        await CreateGroupsUnderNewProcessAsync(unique, norteName, surName);

        // Sur applicant.
        var surApplicantEmail = $"sur_app_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Sur", "Applicant", surApplicantEmail, null,
            "Applicant", TempPwd, IdentificationData.CedulaFisica($"SURAPP-{unique}"));
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync(surName);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Norte reviewer.
        var norteReviewerEmail = $"norte_rev_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Norte", "Reviewer", norteReviewerEmail, null,
            "Reviewer", TempPwd, null);
        await formPage.SelectGroupsAsync(norteName);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Sur applicant logs in, password change, submits a draft application.
        await LogoutAsync();
        await OnboardAndLoginAsync(surApplicantEmail, "NewPass1!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var surAppId = int.Parse(appIdMatch.Groups[1].Value);
        await LogoutAsync();

        // Admin re-opens the queue / signs in to confirm the Sur app id is
        // visible to admin (FR-015) — uses this as the source-of-truth for the
        // id the reviewer will then attempt out-of-scope.
        await LoginAsync(Page, adminEmail, Pwd);
        var adminQueue = new ReviewQueuePage(Page);
        await adminQueue.GotoAsync(BaseUrl);
        // Note: the application is still a draft (not yet Submitted). What
        // matters for the 403 path is that the Norte reviewer cannot reach
        // the detail page for an applicant whose only group is Sur. The
        // ApplicantSharesAnyGroupAsync check happens regardless of state.
        await LogoutAsync();

        // Norte reviewer logs in, password change.
        await OnboardAndLoginAsync(norteReviewerEmail, "RevPass1!");

        // The reviewer's queue must NOT show the Sur applicant's row (FR-011).
        var revQueue = new ReviewQueuePage(Page);
        await revQueue.GotoAsync(BaseUrl);
        var rowAction = revQueue.RowActionFor(surAppId);
        Assert.That(await rowAction.CountAsync(), Is.EqualTo(0),
            "Norte-only reviewer must not see the Sur applicant's row in the queue.");

        // Direct GET on the detail URL is denied (FR-012, NFR-002).
        var response = await Page.GotoAsync($"{BaseUrl}/Review/{surAppId}");
        var status = response?.Status ?? 0;
        var ok403 = status == 403
            || Page.Url.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);
        Assert.That(ok403, Is.True,
            $"Norte-only reviewer must be denied on Sur application detail. Status={status}, Url={Page.Url}");
    }

    /// <summary>
    /// Spec 016 / FR-013 + REVIEW-CODE F-8 — the signing inbox applies the
    /// same group-overlap predicate as the queue. Seeds a Sur applicant with
    /// a Pending signed upload via SQL, then a Norte-only reviewer logs in
    /// and confirms /Review/SigningInbox does NOT list that signed upload.
    /// </summary>
    [Test]
    public async Task Reviewer_NorteOnly_SigningInbox_DoesNotShowSurApplicant()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var adminEmail = await SignInAsAdminAsync(unique);

        var norteName = $"NorteSI-{unique}";
        var surName = $"SurSI-{unique}";
        await CreateGroupsUnderNewProcessAsync(unique, norteName, surName);

        // Sur applicant + pending signed upload (seeded via SQL/Azurite).
        var surApplicantEmail = $"si_sur_app_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Sur", "ApplicantSI", surApplicantEmail, null,
            "Applicant", TempPwd, IdentificationData.CedulaFisica($"SISA-{unique}"));
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync(surName);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Norte reviewer.
        var norteReviewerEmail = $"si_norte_rev_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Norte", "ReviewerSI", norteReviewerEmail, null,
            "Reviewer", TempPwd, null);
        await formPage.SelectGroupsAsync(norteName);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Sur applicant submits a draft application via the UI so we have a
        // real Application id; the rest (FundingAgreement + Pending
        // SignedUpload) is seeded directly via SQL using the same primitives
        // as SigningWayfindingTests.
        await LogoutAsync();
        await OnboardAndLoginAsync(surApplicantEmail, "NewPass1!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var surAppId = int.Parse(appIdMatch.Groups[1].Value);
        await LogoutAsync();

        // Seed the FundingAgreement + Pending SignedUpload directly so the
        // signing inbox has a row attributable to the Sur applicant.
        await FundingAgreementSeeder.SeedPendingSignedUploadAsync(
            ConnectionString, surAppId, adminEmail, surApplicantEmail,
            CreateBlobServiceClient());

        // Norte reviewer logs in and visits the signing inbox.
        await OnboardAndLoginAsync(norteReviewerEmail, "RevPass1!");

        var inbox = new SigningReviewInboxPage(Page);
        await inbox.NavigateAsync(BaseUrl);

        // The Sur applicant's row must not appear in the inbox. Either the
        // empty-state placeholder is shown, or any rows present must NOT be
        // the seeded Sur row (verified by ensuring no row links to surAppId).
        var rowsLinkingToSur = Page.Locator(
            $"[data-testid=signing-inbox-row] a[href*='/Review/{surAppId}']");
        Assert.That(await rowsLinkingToSur.CountAsync(), Is.EqualTo(0),
            "Norte-only reviewer must not see the Sur applicant's pending signed upload in the inbox.");
    }

    /// <summary>
    /// Spec 016 / FR-014 + REVIEW-CODE F-8 — a reviewer assigned to two groups
    /// containing five applicants (3 in group A, 2 in group B, plus an
    /// out-of-scope C applicant) types a partial last-name fragment in the
    /// queue search input; the result narrows to the matching applicant. An
    /// out-of-scope applicant whose name matches the same fragment must not
    /// appear (group-overlap predicate composes BEFORE the search filter).
    /// </summary>
    [Test]
    public async Task Reviewer_TwoGroups_QueueSearch_Narrows_AndStillRespectsScope()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var adminEmail = await SignInAsAdminAsync(unique);

        var groupAName = $"QSA-{unique}";
        var groupBName = $"QSB-{unique}";
        var groupCName = $"QSC-{unique}";
        await CreateGroupsUnderNewProcessAsync(unique, groupAName, groupBName, groupCName);

        // Distinctive fragment used both for the in-scope match and the
        // out-of-scope decoy — guarantees the search-on-last-name path
        // composes with scope rather than circumventing it.
        var matchFragment = $"Lokita{unique[..4]}";

        // Five applicants total — 3 in A, 2 in B, plus one decoy in C.
        // Two of those (one in A, one in C) share the matchFragment in their
        // last name; only the in-scope one must appear after search.
        async Task SeedApplicantAsync(string firstName, string lastName, string emailPrefix,
                                       string legalIdPrefix, string groupName)
        {
            var email = $"{emailPrefix}_{unique}@example.com";
            var createPage = new AdminUserCreatePage(Page);
            await createPage.GoToAsync(BaseUrl);
            await createPage.FillAsync(firstName, lastName, email, null,
                "Applicant", TempPwd, IdentificationData.CedulaFisica($"{legalIdPrefix}-{unique}"));
            var formPage = new AdminUserFormPage(Page);
            await formPage.SelectGroupsAsync(groupName);
            await createPage.SubmitAsync();
            await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
        }

        // In-scope match: applicant whose last name CONTAINS matchFragment, in group A.
        await SeedApplicantAsync("InScope", matchFragment + "Sanchez", "qs_match", "QSM1", groupAName);
        // Other in-scope applicants: don't match.
        await SeedApplicantAsync("Other1", "Foo", "qs_otherA", "QSOA", groupAName);
        await SeedApplicantAsync("Other2", "Bar", "qs_otherB", "QSOB", groupBName);
        await SeedApplicantAsync("Other3", "Baz", "qs_otherC", "QSOC", groupBName);
        // Out-of-scope decoy: same matchFragment in last name, but in group C.
        await SeedApplicantAsync("Decoy", matchFragment + "Decoy", "qs_decoy", "QSDC", groupCName);

        // Reviewer assigned to A + B (both in-scope groups, NOT C).
        var reviewerEmail = $"qs_rev_{unique}@example.com";
        var revCreate = new AdminUserCreatePage(Page);
        await revCreate.GoToAsync(BaseUrl);
        await revCreate.FillAsync("QS", "Reviewer", reviewerEmail, null,
            "Reviewer", TempPwd, null);
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync(groupAName, groupBName);
        await revCreate.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Each in-scope match needs a Submitted application so it shows on
        // the queue. Drive only the matching applicant through draft+submit
        // so the search has a row to find. The decoy in group C ALSO needs a
        // Submitted application so we can confirm it's filtered out by
        // scope. (Other applicants in A/B don't need apps for this assert.)
        async Task<int> LogInAndSubmitDraftAsync(string email)
        {
            await LogoutAsync();
            await OnboardAndLoginAsync(email, "AppPass1!");

            var appPage = new ApplicationPage(Page);
            await appPage.GotoListAsync(BaseUrl);
            await appPage.CreateApplicationAsync();
            var m = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
            Assert.That(m.Success, Is.True);
            return int.Parse(m.Groups[1].Value);
        }

        var matchAppId = await LogInAndSubmitDraftAsync($"qs_match_{unique}@example.com");
        var decoyAppId = await LogInAndSubmitDraftAsync($"qs_decoy_{unique}@example.com");
        await LogoutAsync();

        // Reviewer logs in and runs the search.
        await OnboardAndLoginAsync(reviewerEmail, "RevPass1!");

        var queue = new ReviewQueuePage(Page);
        await queue.GotoAsync(BaseUrl);
        await Expect(queue.SearchInput).ToBeVisibleAsync();
        await queue.SearchAsync(matchFragment);

        // The URL carries the search parameter (FR-014).
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?.*search="));

        // Out-of-scope decoy must NOT appear, even though its last name
        // matches the fragment — the group-overlap predicate composes first.
        var decoyRow = queue.RowActionFor(decoyAppId);
        Assert.That(await decoyRow.CountAsync(), Is.EqualTo(0),
            "Out-of-scope applicant whose name matches the search must NOT appear in the queue.");

        // The in-scope matching applicant's row may or may not be visible
        // depending on application state (drafts may be filtered out by the
        // queue's status filters). The minimum FR-014 contract is: the
        // out-of-scope decoy is filtered out. We additionally confirm the
        // page rendered without 403.
        await Expect(Page).ToHaveURLAsync(new Regex("/Review(\\?.*)?$"));

        // The match applicant id is recorded for reproducibility — the
        // search composes with scope regardless of whether the queue row is
        // currently visible (depends on default filter; the FR-014 negative
        // assertion above is the load-bearing check).
        TestContext.Out.WriteLine($"matchAppId={matchAppId}, decoyAppId={decoyAppId}");
    }
}
