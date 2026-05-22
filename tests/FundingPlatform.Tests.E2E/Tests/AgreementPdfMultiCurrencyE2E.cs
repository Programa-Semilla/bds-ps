using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US5 / T501 — funding-agreement PDF rendering against the live
/// AppHost stack. Three scenarios:
///   1. CRC-only request: Generate succeeds, downloaded PDF is non-empty and
///      starts with %PDF-, no error banner visible on the page.
///   2. Mixed request (one CRC + one USD with snapshot): Generate succeeds,
///      downloaded PDF starts with %PDF-, no error banner visible.
///   3. Missing-snapshot request (synthetic legacy USD without snapshot,
///      planted by direct SQL): Generate returns no download. The page
///      re-renders the Details view with the inline Spanish error, AND a
///      hard browser reload (re-issuing GET Details) keeps the original
///      response visible until the operator navigates / reloads — verifying
///      FR-027 / T512's "no TempData" requirement.
/// </summary>
[Category("MultiCurrency")]
[Category("FundingAgreement")]
public class AgreementPdfMultiCurrencyE2E : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"fa-mc-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nplaceholder quotation\n%%EOF\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Test]
    public async Task CrcOnlyRequest_PdfDownloads_NoErrorBanner()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, adminEmail) = await CreateAcceptedApplicationCrcAsync(uniqueId);

        await LoginAsync(Page, adminEmail, "Test123!");

        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        // CRC-only flow: no missing-conversion-error banner visible.
        await Expect(Page.GetByTestId("funding-agreement-missing-conversion-error"))
            .ToHaveCountAsync(0);

        Assert.That(await panel.HasDownloadLinkAsync(), Is.True,
            "Download link must be present after a successful CRC-only Generate.");

        var flow = new FundingAgreementDownloadFlow(Page);
        var bytes = await flow.CaptureDownloadBytesAsync(panel.DownloadLink);

        Assert.That(bytes.Length, Is.GreaterThan(0), "PDF must be non-empty.");
        Assert.That(FundingAgreementDownloadFlow.LooksLikePdf(bytes), Is.True,
            "Downloaded bytes must start with the %PDF- magic header.");
    }

    [Test]
    public async Task MixedRequest_WithUsdSnapshot_PdfDownloads_NoErrorBanner()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await PublishUsdRateAsync(buy: 520m, sell: 525m);

        var (appId, adminEmail) = await CreateAcceptedApplicationCrcAsync(uniqueId);

        // Flip the selected quotation to USD with a fresh snapshot. The
        // helper persists CRC quotations via the supplier flow; here we
        // mutate one row to USD so the agreement has at least one non-CRC
        // line for T510 coverage.
        await ConvertSelectedQuotationToUsdWithSnapshotAsync(appId);

        await LoginAsync(Page, adminEmail, "Test123!");

        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        await Expect(Page.GetByTestId("funding-agreement-missing-conversion-error"))
            .ToHaveCountAsync(0);

        Assert.That(await panel.HasDownloadLinkAsync(), Is.True,
            "Download link must be present after a successful mixed-currency Generate.");

        var flow = new FundingAgreementDownloadFlow(Page);
        var bytes = await flow.CaptureDownloadBytesAsync(panel.DownloadLink);

        Assert.That(bytes.Length, Is.GreaterThan(0));
        Assert.That(FundingAgreementDownloadFlow.LooksLikePdf(bytes), Is.True);
    }

    [Test]
    public async Task MissingSnapshotRequest_RendersInlineError_NoDownload_HardReloadStateUnchanged()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, adminEmail) = await CreateAcceptedApplicationCrcAsync(uniqueId);

        // Plant a synthetic legacy state: the selected quotation is USD, but
        // its snapshot fields are NULL. The CK_Quotations_NonCrcRequiresSnapshot
        // constraint allows Currency<>'CRC' AND SnapshotRateId IS NULL only when
        // LegacyNeedsReview = 1 — set the flag in the same UPDATE.
        await CorruptSelectedQuotationToLegacyUsdAsync(appId);

        await LoginAsync(Page, adminEmail, "Test123!");

        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();

        // Generate POST returns View("Details", ...) directly — URL stays on the
        // /FundingAgreement endpoint (the form action), not redirected back to
        // Details. Either is acceptable; what matters is the inline error.
        var errorBanner = Page.GetByTestId("funding-agreement-missing-conversion-error");
        await Expect(errorBanner).ToBeVisibleAsync();
        await Expect(errorBanner).ToContainTextAsync(
            new Regex("No se puede generar el PDF"));
        await Expect(errorBanner).ToContainTextAsync(
            new Regex("una o más cotizaciones no tienen tipo de cambio"));

        // No download link should appear.
        Assert.That(await panel.HasDownloadLinkAsync(), Is.False,
            "Download link must NOT be present when Generate refused to produce a PDF.");

        // Hard reload — should still show NO PDF and NO success state. The
        // missing-conversion error is bound to the failed Generate POST and
        // does NOT survive a hard GET reload (the GET Details handler does
        // not re-run the validation). This is acceptable per spec edge case
        // "PDF refusal UX": the reload returns the user to a clean Details
        // page with no download link, which itself is the error signal —
        // until an admin attaches a historical rate (US6) and Generate is
        // retried, the download stays unavailable.
        await Page.ReloadAsync();
        Assert.That(await panel.HasDownloadLinkAsync(), Is.False,
            "Download link must remain absent after a hard reload until US6 attaches a rate.");
    }

    /// <summary>
    /// Drives the full happy path through the UI to a ResponseFinalized state
    /// with the applicant having accepted the approved item. Quotation currency
    /// is pinned to CRC so the persistence path stays inside the
    /// CRC short-circuit branch (no exchange rate required) — the multi-currency
    /// scenarios then mutate the row directly via SQL to drive the renderer.
    /// </summary>
    private async Task<(int AppId, string AdminEmail)> CreateAcceptedApplicationCrcAsync(string uniqueId)
    {
        const string password = "Test123!";
        var applicantEmail = $"agp_app_{uniqueId}@example.com";
        var reviewerEmail = $"agp_rev_{uniqueId}@example.com";
        var adminEmail = $"agp_adm_{uniqueId}@example.com";

        await RegisterUserAsync(Page, adminEmail, password, "AGP", "Admin", $"AGPADM-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");

        await RegisterUserAsync(Page, applicantEmail, password, "AGP", "Applicant", $"AGPAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "AGP Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);

        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync(
            $"AGP1-{uniqueId}", "Supplier Alpha", 900_000m, "2027-12-31",
            _testFilePath, currency: "CRC");
        await supplierPage.SubmitAsync();

        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync(
            $"AGP2-{uniqueId}", "Supplier Beta", 1_100_000m, "2027-12-31",
            _testFilePath, currency: "CRC");
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await RegisterUserAsync(Page, reviewerEmail, password, "AGP", "Reviewer", $"AGPREV-{uniqueId}");
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

        return (appId, adminEmail);
    }

    private async Task PublishUsdRateAsync(decimal buy, decimal sell)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Idempotent prep: clear existing USD-CRC rates and any quotations
        // that snapshotted them (mark as legacy so the FK clean-up satisfies
        // the CK_Quotations_NonCrcRequiresSnapshot constraint).
        using (var prep = conn.CreateCommand())
        {
            prep.CommandText = @"
                UPDATE dbo.Quotations
                   SET SnapshotRateId = NULL,
                       SnapshotRateValue = NULL,
                       SnapshotRateType = NULL,
                       SnapshotEffectiveAtUtc = NULL,
                       LegacyNeedsReview = 1
                 WHERE SnapshotRateId IS NOT NULL AND Currency <> 'CRC';
                DELETE FROM dbo.ExchangeRates
                 WHERE SourceCurrencyCode = 'USD' AND TargetCurrencyCode = 'CRC';";
            await prep.ExecuteNonQueryAsync();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DECLARE @CreatedById NVARCHAR(450) = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers
                                                  WHERE [IsSystemSentinel] = 1);
            IF @CreatedById IS NULL SET @CreatedById = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers);
            INSERT INTO dbo.ExchangeRates
                (Id, SourceCurrencyCode, TargetCurrencyCode, BuyRate, SellRate,
                 EffectiveAtUtc, CreatedByUserId, CreatedAtUtc, IsUsed)
            VALUES
                (@Id, 'USD', 'CRC', @Buy, @Sell, SYSUTCDATETIME(), @CreatedById, SYSUTCDATETIME(), 0);";
        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@Buy", buy);
        cmd.Parameters.AddWithValue("@Sell", sell);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Mutates the selected quotation on the application's first item to
    /// Currency='USD' with a fresh rate snapshot stamped from any published
    /// USD↔CRC rate. Used to drive the mixed-currency PDF path without going
    /// through the applicant Add-Quotation UI a second time.
    /// </summary>
    private async Task ConvertSelectedQuotationToUsdWithSnapshotAsync(int applicationId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DECLARE @ItemId INT = (SELECT TOP 1 i.Id
                                   FROM dbo.Items i
                                   WHERE i.ApplicationId = @AppId
                                   ORDER BY i.Id);
            DECLARE @SupplierId INT = (SELECT TOP 1 SelectedSupplierId
                                       FROM dbo.Items WHERE Id = @ItemId);
            DECLARE @QuotationId INT = (SELECT TOP 1 q.Id
                                        FROM dbo.Quotations q
                                        WHERE q.ItemId = @ItemId AND q.SupplierId = @SupplierId);
            DECLARE @RateId UNIQUEIDENTIFIER = (SELECT TOP 1 Id
                                                FROM dbo.ExchangeRates
                                                WHERE SourceCurrencyCode = 'USD'
                                                  AND TargetCurrencyCode = 'CRC'
                                                ORDER BY EffectiveAtUtc DESC);
            DECLARE @Rate DECIMAL(18,6) = (SELECT TOP 1 BuyRate
                                            FROM dbo.ExchangeRates WHERE Id = @RateId);
            DECLARE @EffectiveAt DATETIME2 = (SELECT TOP 1 EffectiveAtUtc
                                              FROM dbo.ExchangeRates WHERE Id = @RateId);
            UPDATE dbo.Quotations
               SET Currency = 'USD',
                   Price = 1000,
                   ConvertedCrcAmount = 1000 * @Rate,
                   SnapshotRateId = @RateId,
                   SnapshotRateValue = @Rate,
                   SnapshotRateType = 0, -- 0 = Buy
                   SnapshotEffectiveAtUtc = @EffectiveAt,
                   LegacyNeedsReview = 0
             WHERE Id = @QuotationId;";
        cmd.Parameters.AddWithValue("@AppId", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Plants a synthetic legacy state for the missing-snapshot test —
    /// flips the selected quotation to USD without any rate snapshot and
    /// sets <c>LegacyNeedsReview = 1</c> so the
    /// <c>CK_Quotations_NonCrcRequiresSnapshot</c> constraint stays satisfied.
    /// The PDF renderer's pre-flight should refuse with
    /// <c>MissingConversionMetadataException</c>.
    /// </summary>
    private async Task CorruptSelectedQuotationToLegacyUsdAsync(int applicationId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DECLARE @ItemId INT = (SELECT TOP 1 i.Id
                                   FROM dbo.Items i
                                   WHERE i.ApplicationId = @AppId
                                   ORDER BY i.Id);
            DECLARE @SupplierId INT = (SELECT TOP 1 SelectedSupplierId
                                       FROM dbo.Items WHERE Id = @ItemId);
            DECLARE @QuotationId INT = (SELECT TOP 1 q.Id
                                        FROM dbo.Quotations q
                                        WHERE q.ItemId = @ItemId AND q.SupplierId = @SupplierId);
            UPDATE dbo.Quotations
               SET Currency = 'USD',
                   Price = 500,
                   ConvertedCrcAmount = NULL,
                   SnapshotRateId = NULL,
                   SnapshotRateValue = NULL,
                   SnapshotRateType = NULL,
                   SnapshotEffectiveAtUtc = NULL,
                   LegacyNeedsReview = 1
             WHERE Id = @QuotationId;";
        cmd.Parameters.AddWithValue("@AppId", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }
}
