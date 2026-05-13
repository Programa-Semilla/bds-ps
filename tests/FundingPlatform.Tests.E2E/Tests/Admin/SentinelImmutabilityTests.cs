using System.Net;
using System.Net.Http;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

public class SentinelImmutabilityTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string SentinelEmail = "admin@programa-semilla.test";
    private const string SentinelConfiguredPassword = "Sentinel123!";

    private async Task<string> SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"sentinel_imm_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Sentinel", "Imm", $"SIM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
        return adminEmail;
    }

    private async Task<string> GetSentinelUserIdAsync()
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id FROM dbo.AspNetUsers WHERE IsSystemSentinel = 1", conn);
        var id = await cmd.ExecuteScalarAsync() as string;
        Assert.That(id, Is.Not.Null.And.Not.Empty,
            "Sentinel user should exist. Did SeedSentinelAdminAsync run?");
        return id!;
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var cookies = await Page.Context.CookiesAsync();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            AllowAutoRedirect = false,
        };
        var baseUri = new Uri(BaseUrl);
        foreach (var c in cookies)
        {
            if (string.IsNullOrEmpty(c.Name)) continue;
            handler.CookieContainer.Add(baseUri,
                new System.Net.Cookie(c.Name, c.Value, c.Path ?? "/", baseUri.Host));
        }
        var client = new HttpClient(handler) { BaseAddress = baseUri };
        return client;
    }

    private static async Task<string> GetAntiForgeryTokenAsync(IPage page, string baseUrl)
    {
        await page.GotoAsync($"{baseUrl}/Admin/Users");
        var token = await page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        return token ?? string.Empty;
    }

    [Test]
    public async Task Sentinel_DirectDisablePost_Rejected()
    {
        await SignInAsAdminAsync();
        var sentinelId = await GetSentinelUserIdAsync();
        var token = await GetAntiForgeryTokenAsync(Page, BaseUrl);
        Assert.That(token, Is.Not.Empty, "Anti-forgery token must be obtainable from /Admin/Users.");

        using var client = await AuthenticatedClientAsync();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync($"/Admin/Users/{sentinelId}/Disable", content);

        // Sentinel guard rejects: response is a redirect back to /Admin/Users with an error banner;
        // confirm the sentinel state is unchanged via SQL.
        Assert.That(((int)response.StatusCode), Is.LessThan(500),
            $"Disable POST against sentinel id should not 500. Got {response.StatusCode}.");

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT LockoutEnd FROM dbo.AspNetUsers WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@id", sentinelId);
        var lockoutEnd = await cmd.ExecuteScalarAsync();
        Assert.That(lockoutEnd, Is.Null.Or.EqualTo(DBNull.Value),
            "Sentinel LockoutEnd must remain NULL after attempted disable.");
    }

    [Test]
    public async Task Sentinel_CreateUserWithSentinelEmail_Rejected()
    {
        await SignInAsAdminAsync();
        await Page.GotoAsync($"{BaseUrl}/Admin/Users/Create");
        await Page.FillAsync("[name=FirstName]", "Should");
        await Page.FillAsync("[name=LastName]", "Fail");
        await Page.FillAsync("[name=Email]", SentinelEmail);
        await Page.FillAsync("[name=InitialPassword]", "TempPass1!");
        await Page.SelectOptionAsync("[name=Role]", "Reviewer");
        await Page.Locator("[data-testid=admin-user-create-submit]").ClickAsync();

        // Form re-renders with an error (still on /Admin/Users/Create or with validation summary).
        await Expect(Page.Locator(".validation-summary-errors, .field-validation-error").First).ToBeVisibleAsync();

        // Confirm the sentinel hasn't been clobbered.
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.AspNetUsers WHERE NormalizedEmail = @email",
            conn);
        cmd.Parameters.AddWithValue("@email", SentinelEmail.ToUpperInvariant());
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(1), "Sentinel email should map to exactly one user (the sentinel itself).");
    }

    [Test]
    public async Task Sentinel_CanLogIn_WithConfiguredPassword()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.FillAsync("[name=Email]", SentinelEmail);
        await Page.FillAsync("[name=Password]", SentinelConfiguredPassword);
        await Page.Locator("form[action*='Account/Login'] button[type=submit]").ClickAsync();

        // Sentinel should NOT be redirected to ChangePassword (MustChangePassword == false).
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/ChangePassword"));
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/Login"));
    }
}
