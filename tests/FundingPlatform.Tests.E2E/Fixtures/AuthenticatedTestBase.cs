using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace FundingPlatform.Tests.E2E.Fixtures;

public class AuthenticatedTestBase : PageTest
{
    private static readonly AspireFixture _fixture = new();
    private static bool _initialized;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    protected string BaseUrl => _fixture.BaseUrl;
    protected string ConnectionString => _fixture.ConnectionString;

    /// <summary>
    /// Spec 021 / T029 — surface the smtp4dev REST client for notification E2E tests.
    /// Null in the NFR-007 degraded mode (sidecar failed to start); tests that
    /// require it MUST Assert.Inconclusive when null rather than silently passing.
    /// </summary>
    protected MailCaptureClient? MailCapture => _fixture.MailCapture;

    /// <summary>
    /// Spec 014 — exposes a configured <see cref="BlobServiceClient"/> so tests/seeders
    /// can write placeholder blobs to Azurite at the same canonical keys recorded on
    /// the seeded SQL rows. Returns null when the fixture fell back to filesystem mode.
    /// </summary>
    protected BlobServiceClient? CreateBlobServiceClient() => _fixture.CreateBlobServiceClient();

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        };
    }

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        // Fail fast: no UI operation should take longer than ~10s. A longer wait means
        // the app is unresponsive (a real problem), not a transient render — surfacing
        // it in 10s instead of Playwright's default 30s keeps feedback tight.
        Assertions.SetDefaultExpectTimeout(10_000);

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await _fixture.StartAsync();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Per-test: cap Playwright's default action + navigation timeout at 10s (down
    /// from the 30s default) so a hung page load / element wait fails fast instead of
    /// stalling the suite. Runs after PageTest's own setup creates the Page.
    /// </summary>
    [SetUp]
    public void ConfigureFastTimeouts()
    {
        Page.SetDefaultTimeout(10_000);
        Page.SetDefaultNavigationTimeout(10_000);
    }

    /// <summary>
    /// Spec 035 / US2 — impact relocated from the Application aggregate to each
    /// line item; the app-level Impact step (<c>Views/Application/Impact.cshtml</c>)
    /// was removed. This helper makes every item of <paramref name="appId"/>'s draft
    /// impact-complete by visiting each item's Edit page and picking + filling the
    /// first active impact template. Retained under its original name so the ~26
    /// legacy "add item → set impact → submit" call sites keep working unchanged;
    /// it now reflects the per-item model instead of a single app-level step.
    /// </summary>
    protected async Task SetImpactFromEditAsync(int appId)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Collect the item ids from the per-row "Editar" links (the quotation
        // sub-rows live under a different testid, so they're not matched here).
        var editLinks = Page.Locator("[data-testid=application-edit-item-row] a:has-text('Editar')");
        var count = await editLinks.CountAsync();
        var itemIds = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var href = await editLinks.Nth(i).GetAttributeAsync("href") ?? string.Empty;
            var m = Regex.Match(href, @"/Item/(\d+)/Edit");
            if (m.Success)
            {
                itemIds.Add(int.Parse(m.Groups[1].Value));
            }
        }

        var itemPage = new ItemPage(Page);
        foreach (var itemId in itemIds.Distinct())
        {
            await itemPage.SetImpactViaEditAsync(appId, itemId, BaseUrl);
        }
    }

    /// <summary>
    /// Spec 021 / US2 / FR-017 — submits a complete draft through the gated
    /// editor button → <c>/review</c> → "Confirmar y enviar". Leaves the page
    /// on the Details summary of the now-submitted Application.
    /// </summary>
    protected async Task SubmitDraftViaReviewAsync(int appId)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var submit = Page.Locator("[data-testid=application-edit-submit]");
        await Expect(submit).ToBeEnabledAsync();
        await submit.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/.+/Review"));
        await Page.Locator("[data-testid=review-confirm-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
    }

    /// <summary>
    /// Seeds an applicant for the E2E suite. Spec 032 — public self-registration was
    /// removed (the Register form is gone / returns 404), so this no longer drives a
    /// UI form; it calls the dev-only <c>/Account/SeedUser</c> seam (Development-gated,
    /// no UI), which reproduces the former Register POST. <paramref name="legalId"/> is
    /// still treated as a unique SEED, deterministically converted to a valid canonical
    /// cédula física so existing callers that passed free-form ids (e.g. "SAPP-1234")
    /// keep working with collision-safe values. The <c>page</c> parameter is retained
    /// for call-site compatibility; user creation no longer navigates it.
    /// </summary>
    protected async Task RegisterUserAsync(IPage page, string email, string password, string firstName, string lastName, string legalId)
    {
        using (var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        using (var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) })
        {
            var qs = $"?email={Uri.EscapeDataString(email)}" +
                     $"&password={Uri.EscapeDataString(password)}" +
                     $"&firstName={Uri.EscapeDataString(firstName)}" +
                     $"&lastName={Uri.EscapeDataString(lastName)}" +
                     $"&legalId={Uri.EscapeDataString(IdentificationData.CedulaFisica(legalId))}";
            var response = await client.GetAsync($"/Account/SeedUser{qs}");
            response.EnsureSuccessStatusCode();
        }

        // Spec 016 — every reviewer-driven E2E surface (queue, signing inbox,
        // application detail) composes a group-overlap predicate. Existing
        // tests that register an applicant or reviewer via this helper never
        // touched the group catalog; without a default membership the queue
        // is empty and detail-page access is denied. The dev-only
        // /Account/AssignAllGroups endpoint assigns the user to every seeded
        // group so the legacy tests' real-user-journey assertions still see
        // the data they expect. AssignRoleAsync(role=Admin) strips these
        // memberships afterwards to preserve the FR-008 admin invariant.
        await AssignAllGroupsAsync(email);
    }

    /// <summary>
    /// Spec 016 — calls the dev-only <c>/Account/AssignAllGroups</c> endpoint
    /// so a freshly registered user becomes a member of every seeded group.
    /// </summary>
    protected async Task AssignAllGroupsAsync(string email)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var response = await client.GetAsync($"/Account/AssignAllGroups?email={Uri.EscapeDataString(email)}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Spec 033 — completes onboarding for an admin-invited user: follows the
    /// set-password invite link (from the InvitationSent confirmation or the
    /// email), sets a password on <c>/Account/ResetPassword</c>, and lands back
    /// on the Login page. After this, <see cref="LoginAsync"/> with the same
    /// password signs the user in (no forced change-password — invited users
    /// have MustChangePassword=false).
    /// </summary>
    protected async Task SetPasswordViaInviteAsync(string inviteLink, string newPassword)
    {
        await Page.GotoAsync(inviteLink);
        var reset = new ResetPasswordPage(Page);
        await Expect(reset.FormRoot).ToBeVisibleAsync();
        await reset.SubmitAsync(newPassword, newPassword);
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Login"));
    }

    /// <summary>
    /// Spec 033 — onboards an admin-created (passwordless) user and signs them in.
    /// Admin create no longer assigns a password (the user is emailed a
    /// set-password invitation), so tests that create a user via the admin UI and
    /// then log in as them can no longer use a temp password + first-login
    /// change-password. This helper obtains a set-password link via the dev-only
    /// <c>LatestPasswordResetLink</c> seam — a SEPARATE token issuer (60-min reset
    /// lifetime, no supersede), NOT the admin-create-issued invite token. It is used
    /// by lifecycle/scope tests that only need an onboarded, signed-in user; the real
    /// create-issued invitation path (72h, supersede-on-resend) is covered end-to-end
    /// by <c>UserInvitationTests</c>, which follows the link rendered on the
    /// create/resend confirmation. The result here is equivalent for the caller's
    /// purpose: authenticated as the user, with no forced change-password.
    /// </summary>
    protected async Task OnboardAndLoginAsync(string email, string password)
    {
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

        await SetPasswordViaInviteAsync(link, password);
        await LoginAsync(Page, email, password);
    }

    protected async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync($"{BaseUrl}/Account/Login");
        await page.FillAsync("[name=Email]", email);
        await page.FillAsync("[name=Password]", password);
        await page.Locator("form[action*='Account/Login'] button[type=submit]").ClickAsync();
    }

    protected async Task AssignRoleAsync(string email, string role)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var response = await client.GetAsync($"/Account/AssignRole?email={Uri.EscapeDataString(email)}&role={Uri.EscapeDataString(role)}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Spec 017 — wipes admin-relevant state (audit events, supplier statuses,
    /// groups, impact templates) so spec-017 zero-fixture E2E assertions
    /// don't fight the cumulative state left behind by earlier tests in the
    /// shared <see cref="AspireFixture"/>. Always pair with
    /// <see cref="SeedAdminFixtureAsync"/> in a teardown — otherwise reviewer
    /// queue tests downstream lose their seeded groups + impact templates and
    /// fall over.
    /// </summary>
    protected async Task ResetAdminFixtureAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var response = await client.GetAsync("/Account/ResetAdminFixture");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Spec 017 — re-plants the post-deploy seed (Norte/Sur/Centro groups +
    /// the two demo ImpactTemplates) after a <see cref="ResetAdminFixtureAsync"/>
    /// call so subsequent tests in the shared fixture keep working.
    /// </summary>
    protected async Task SeedAdminFixtureAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var response = await client.GetAsync("/Account/SeedAdminFixture");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Drives the UI through the full happy-path from application creation to
    /// <c>ResponseFinalized</c> state (item approved, review finalized, applicant accepted).
    /// No Funding Agreement is generated here.
    /// </summary>
    /// <param name="uniqueId">A short unique suffix (e.g. 8-hex chars) used to namespace
    /// all seeded users and legal IDs so parallel tests don't collide.</param>
    /// <param name="quotationFilePath">Path to a placeholder PDF file to attach as supplier quotations.</param>
    /// <returns>A tuple of (ApplicationId, ApplicantEmail, ApplicantPassword).</returns>
    protected async Task<(int ApplicationId, string ApplicantEmail, string ApplicantPassword)>
        CreateApplicationAndSubmitResponseAsync(string uniqueId, string quotationFilePath)
    {
        const string password = "Test123!";
        var applicantEmail = $"seed_applicant_{uniqueId}@example.com";
        var reviewerEmail = $"seed_reviewer_{uniqueId}@example.com";
        var adminEmail = $"seed_admin_{uniqueId}@example.com";

        await RegisterUserAsync(Page, adminEmail, password, "Seed", "Admin", $"SADM-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");

        await RegisterUserAsync(Page, applicantEmail, password, "Seed", "Applicant", $"SAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        // Spec 021 / US2 — draft creation opens the draft editor.
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Items + supplier quotations — Item/Add + Supplier/Add are linked
        // from the draft editor and redirect back to it.
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Seed Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync(IdentificationData.CedulaJuridica($"SQ1-{uniqueId}"), "Supplier Alpha", 900m, "2027-12-31", quotationFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync(IdentificationData.CedulaJuridica($"SQ2-{uniqueId}"), "Supplier Beta", 1100m, "2027-12-31", quotationFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await RegisterUserAsync(Page, reviewerEmail, password, "Seed", "Reviewer", $"SREV-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, password);

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var suppOptions = await supplierDropdown.Locator("option").AllAsync();
        await supplierDropdown.SelectOptionAsync(await suppOptions[1].GetAttributeAsync("value") ?? "");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await reviewPage.FinalizeButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await LoginAsync(Page, applicantEmail, password);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return (appId, applicantEmail, password);
    }
}

[SetUpFixture]
public class GlobalTeardown
{
    [OneTimeTearDown]
    public void RunAfterAllTests()
    {
        // Aspire host is cleaned up when the process exits.
        // We don't dispose mid-run because the fixture is shared across test classes.
    }
}
