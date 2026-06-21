var builder = DistributedApplication.CreateBuilder(args);

// Tests pass --EphemeralStorage=true to opt out of dev-convenience features that
// don't survive a fresh-container test run: the persistent SQL data volume and
// the auto-deploy SQL project. The E2E fixture deploys the dacpac itself via
// sqlpackage so it can wait synchronously for completion before tests start.
var ephemeralStorage = string.Equals(
    builder.Configuration["EphemeralStorage"], "true", StringComparison.OrdinalIgnoreCase);

// Publish mode (azd up) provisions a real Azure SQL DB; run mode keeps the
// proven AddSqlServer container path. Branching on IsPublishMode avoids the
// RunAsContainer quirks that left sqlpackage hanging against the local
// container (Azure SQL DB resource lacks the SQL-auth health probe the
// CommunityToolkit dacpac integration auto-waits on). The dacpac project is
// only registered in run mode where the toolkit's typed
// WithReference(IResourceBuilder<SqlServerDatabaseResource>) overload binds
// correctly — sharing a IResourceWithConnectionString variable across both
// branches falls through to a generic overload that never wires the
// SqlProject deployment hook (resource sits in Waiting forever).
IResourceBuilder<IResourceWithConnectionString> sqlServer;

if (builder.ExecutionContext.IsPublishMode)
{
    sqlServer = builder.AddAzureSqlServer("sqlserver")
                       .AddDatabase("fundingdb");
}
else
{
    var localSql = builder.AddSqlServer("sqlserver");
    if (!ephemeralStorage)
    {
        localSql = localSql.WithDataVolume("fundingplatform-sqldata");
    }
    else
    {
        // Bind /var/opt/mssql to a tmpfs so SQL Server's data dir lives in RAM and
        // dies with the container — prevents Docker from creating an anonymous
        // volume per test run that piles up on the host. The mssql user inside
        // the container is uid 10001; without uid=10001 the tmpfs is root-owned
        // and SQL Server cannot initialize, leaving sqlpackage to retry forever
        // against an empty endpoint (observed 2026-05-01).
        localSql = localSql.WithContainerRuntimeArgs(
            "--tmpfs", "/var/opt/mssql:uid=10001,mode=755");
    }

    var localSqlDb = localSql.AddDatabase("fundingdb");
    sqlServer = localSqlDb;

    if (!ephemeralStorage)
    {
        builder.AddSqlProject<Projects.FundingPlatform_Database>("database-schema")
               .WithReference(localSqlDb);
    }
}

// Spec 014 (T014): Azure Storage / Azurite. Provider defaults to Azurite for
// local-dev parity (FR-006); production overrides Storage:Provider=AzureBlob.
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Azurite";

// Spec 014 (T036) / FR-008 — opt-in fallback to LocalFilesystem. When the
// operator (typically a CI runner) has explicitly enabled this flag, the
// AppHost honours Provider=LocalFilesystem outright and provisions a temp
// directory so the Web project boots without Azure or Azurite. Default
// posture stays strict: production deployments do not see this flag.
var allowFilesystemFallback = string.Equals(
    builder.Configuration["Storage:TestFallback:AllowFilesystem"],
    "true",
    StringComparison.OrdinalIgnoreCase);

string? localFilesystemRoot = null;
if (allowFilesystemFallback &&
    string.Equals(storageProvider, "LocalFilesystem", StringComparison.OrdinalIgnoreCase))
{
    localFilesystemRoot = Path.Combine(
        Path.GetTempPath(),
        $"fundingplatform-storage-{Guid.NewGuid():N}");
    Directory.CreateDirectory(localFilesystemRoot);
}

IResourceBuilder<Aspire.Hosting.ApplicationModel.IResourceWithConnectionString>? blobsResource = null;
if (string.Equals(storageProvider, "Azurite", StringComparison.OrdinalIgnoreCase))
{
    var storage = builder.AddAzureStorage("storage")
        .RunAsEmulator(emu =>
        {
            if (!ephemeralStorage)
            {
                emu.WithDataVolume("fundingplatform-blobdata");
            }
            else
            {
                // tmpfs the Azurite data dir so test runs don't accumulate
                // anonymous Docker volumes (mirrors the SQL-side fix above).
                emu.WithContainerRuntimeArgs("--tmpfs", "/data");
            }
        });

    blobsResource = storage.AddBlobs("blobs");
}

var syncfusionLicense = builder.Configuration["Syncfusion:LicenseKey"] ?? "Ngo9BigBOggjHTQxAR8/V1JHaF1cXmhMYVJpR2NbeU5xdF9DZVZURGY/P1ZhSXxVdkFhXX1cdXFQRmJVU019XEE=";
var localeCode = builder.Configuration["FundingAgreement:LocaleCode"] ?? "es-CR";
// Spec 015 / T907 — base currency is CRC; the prior "COP" default predates the
// multi-currency feature and contradicts the platform's only base currency.
var currencyIsoCode = builder.Configuration["FundingAgreement:CurrencyIsoCode"] ?? "CRC";
// Spec 018 / FR-019 — FundingAgreement:Funder:* keys removed. Funder identity is
// now hardcoded inside the sworn-declaration partial of the branded PDF.
var signedUploadMaxSizeBytes = builder.Configuration["SignedUpload:MaxSizeBytes"] ?? "20971520";
// Spec 015 / T907 follow-up — base currency is CRC. The prior "COP" fallback was
// incompatible with the multi-currency conversion path (no rate, no FK to the
// seeded Currencies catalog), causing every legacy supplier-add flow to 500.
var adminReportsDefaultCurrency = builder.Configuration["AdminReports:DefaultCurrency"] ?? "CRC";
var adminReportsCsvRowLimit = builder.Configuration["AdminReports:CsvRowLimit"] ?? "50000";

// Spec 020 — AI quote comparison knobs. The provider defaults to the offline
// "Stub" so E2E (constitution Principle III) runs without an Anthropic API key.
// Real environments override AiComparison:Provider=Anthropic and supply the
// API key via the configured secret store (never appsettings).
var aiProvider = builder.Configuration["AiComparison:Provider"] ?? "Stub";
var aiApiKey = builder.Configuration["AiComparison:Anthropic:ApiKey"];
var aiExtractModel = builder.Configuration["AiComparison:Anthropic:ExtractModel"] ?? "claude-sonnet-4-6";
var aiCompareModel = builder.Configuration["AiComparison:Anthropic:CompareModel"] ?? "claude-opus-4-7";
var aiBaseUrl = builder.Configuration["AiComparison:Anthropic:BaseUrl"];
var aiExtractConcurrency = builder.Configuration["AiComparison:ExtractConcurrency"] ?? "4";
var aiWorkerConcurrency = builder.Configuration["AiComparison:WorkerConcurrency"] ?? "2";
var aiPollIntervalSeconds = builder.Configuration["AiComparison:PollIntervalSeconds"] ?? "3";
var aiSyncHardTimeoutSeconds = builder.Configuration["AiComparison:SyncHardTimeoutSeconds"] ?? "90";
var aiRateLimitPerApp24h = builder.Configuration["AiComparison:RateLimitPerApp24h"] ?? "10";
var aiTokenCapPerRunInput = builder.Configuration["AiComparison:TokenCapPerRunInput"] ?? "200000";
var aiOrphanReapAfterMinutes = builder.Configuration["AiComparison:OrphanReapAfterMinutes"] ?? "5";
var aiPromptVersion = builder.Configuration["AiComparison:PromptVersion"] ?? "2026-05-11";
var aiSchemaVersion = builder.Configuration["AiComparison:SchemaVersion"] ?? "v1";

// E2E fixture runs with EphemeralStorage=true and a fresh DB per fixture run, so
// the sentinel admin (admin@programa-semilla.test) is seeded on every startup. In
// ephemeral mode we force the deterministic test password regardless of other
// config layers — otherwise an appsettings.Development.json entry (added by
// spec 010) wins via `??` and the test fixture can't predict the password.
// Outside ephemeral, fall back to whatever Admin:DefaultPassword is configured.
var adminDefaultPassword = ephemeralStorage
    ? "Sentinel123!"
    : builder.Configuration["Admin:DefaultPassword"];

// Spec 021 — Notifications config read once: validated in publish mode below and
// forwarded to the Web container after webApp is defined. AppHost previously only
// validated these and never forwarded them, so the running container fell back to
// the appsettings default (Provider=Mailtrap) and prod mail never reached Mailgun.
var notificationsProvider    = builder.Configuration["Notifications:Provider"];
var notificationsBaseUrl     = builder.Configuration["Notifications:BaseUrl"];
var mailgunApiKey            = builder.Configuration["Notifications:Mailgun:ApiKey"];
var mailgunDomain            = builder.Configuration["Notifications:Mailgun:Domain"];
var mailgunBaseUrl           = builder.Configuration["Notifications:Mailgun:BaseUrl"];
var notificationsSenderName  = builder.Configuration["Notifications:Sender:Name"];
var notificationsSenderEmail = builder.Configuration["Notifications:Sender:Email"];

// Spec 021 / FR-016 — fail-fast in publish mode when Mailgun is *explicitly*
// selected but its config is incomplete. This block runs during azd's manifest
// generation (`dotnet run --publisher manifest`), NOT at container runtime —
// the AppHost never executes in Azure. azd does not forward the azd-environment
// (.env) values into that manifest-generation process, so `notificationsProvider`
// is normally null here. We therefore must NOT default an absent provider to
// "Mailgun" (doing so aborted every `azd up` with a false FR-016 failure). The
// authoritative runtime fail-fast lives in the Web project's
// NotificationsServiceCollectionExtensions (FR-016), which sees real config when
// the container boots in Production. This guard only catches a locally-set,
// explicit Mailgun misconfiguration before publishing.
if (builder.ExecutionContext.IsPublishMode)
{
    var provider = notificationsProvider;
    if (string.Equals(provider, "Mailgun", StringComparison.OrdinalIgnoreCase))
    {
        var missing = new[]
        {
            ("Notifications:Mailgun:ApiKey", mailgunApiKey),
            ("Notifications:Mailgun:Domain", mailgunDomain),
            ("Notifications:Sender:Email",   notificationsSenderEmail),
            ("Notifications:BaseUrl",        notificationsBaseUrl),
        }
        .Where(p => string.IsNullOrWhiteSpace(p.Item2))
        .Select(p => p.Item1)
        .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "Spec 021 FR-016 — Notifications:Provider=Mailgun in publish mode requires "
                + $"the following config keys to be set: {string.Join(", ", missing)}. "
                + "Set them via azd env / Key Vault before re-publishing.");
        }
    }
}

var webApp = builder.AddProject<Projects.FundingPlatform_Web>("webapp")
    .WithExternalHttpEndpoints()
    .WithReference(sqlServer)
    .WaitFor(sqlServer)
    .WithEnvironment("Syncfusion__LicenseKey", syncfusionLicense)
    .WithEnvironment("FundingAgreement__LocaleCode", localeCode)
    .WithEnvironment("FundingAgreement__CurrencyIsoCode", currencyIsoCode)
    .WithEnvironment("SignedUpload__MaxSizeBytes", signedUploadMaxSizeBytes)
    .WithEnvironment("AdminReports__DefaultCurrency", adminReportsDefaultCurrency)
    .WithEnvironment("AdminReports__CsvRowLimit", adminReportsCsvRowLimit)
    .WithEnvironment("AiComparison__Provider", aiProvider)
    .WithEnvironment("AiComparison__Anthropic__ExtractModel", aiExtractModel)
    .WithEnvironment("AiComparison__Anthropic__CompareModel", aiCompareModel)
    .WithEnvironment("AiComparison__ExtractConcurrency", aiExtractConcurrency)
    .WithEnvironment("AiComparison__WorkerConcurrency", aiWorkerConcurrency)
    .WithEnvironment("AiComparison__PollIntervalSeconds", aiPollIntervalSeconds)
    .WithEnvironment("AiComparison__SyncHardTimeoutSeconds", aiSyncHardTimeoutSeconds)
    .WithEnvironment("AiComparison__RateLimitPerApp24h", aiRateLimitPerApp24h)
    .WithEnvironment("AiComparison__TokenCapPerRunInput", aiTokenCapPerRunInput)
    .WithEnvironment("AiComparison__OrphanReapAfterMinutes", aiOrphanReapAfterMinutes)
    .WithEnvironment("AiComparison__PromptVersion", aiPromptVersion)
    .WithEnvironment("AiComparison__SchemaVersion", aiSchemaVersion);

if (!string.IsNullOrEmpty(aiApiKey))
{
    webApp.WithEnvironment("AiComparison__Anthropic__ApiKey", aiApiKey);
}
if (!string.IsNullOrEmpty(aiBaseUrl))
{
    webApp.WithEnvironment("AiComparison__Anthropic__BaseUrl", aiBaseUrl);
}

// Spec 021 / T005 / FR-030 / NFR-007 — smtp4dev SMTP-capture sidecar is a
// LOCAL-DEV + E2E-ONLY resource. Gated behind !IsPublishMode so `azd publish`
// does NOT provision a smtp4dev Container App in Azure. Production routes mail
// through MailgunHttpEmailSender (raw HttpClient), validated above per FR-016.
// Sidecar failure in run mode must NOT block dev workflow per NFR-007 — the
// Web project's NoOpEmailSender fallback is the safety net.
if (!builder.ExecutionContext.IsPublishMode)
{
    var smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev", "3.6.1")
        .WithEndpoint(targetPort: 25, name: "smtp", scheme: "tcp")
        .WithHttpEndpoint(targetPort: 80, name: "http");

    var smtpEndpoint = smtp4dev.GetEndpoint("smtp");

    webApp
        .WithReference(smtpEndpoint)
        .WithReference(smtp4dev.GetEndpoint("http"))
        .WaitFor(smtp4dev)
        // Spec 021 / T086 fix — Aspire's WithReference on the smtp endpoint emits
        // a service-discovery env var whose exact key shape varies across Aspire
        // versions / contexts (host process vs. testing builder). Resolving via
        // that env var inside MailtrapSmtpEmailSender produced "connection
        // refused" against localhost:25 in the E2E run because the env var was
        // absent and the host-fallback fired against an unmapped port. Bind the
        // resolved dynamic host:port directly into the platform's own
        // Notifications:Mailtrap:Host/Port config keys so the existing config
        // fallback path resolves to the right endpoint deterministically.
        .WithEnvironment("Notifications__Mailtrap__Host",
            ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Host)}"))
        .WithEnvironment("Notifications__Mailtrap__Port",
            ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Port)}"))
        // Spec 033 — also point the DIRECT-send path (Smtp:* → SmtpEmailSender,
        // used by Account/ForgotPassword and the admin set-password invitation) at
        // the same smtp4dev endpoint. Without a non-empty Smtp:Host the platform
        // falls back to LoggingEmailSender (DependencyInjection.cs), so those
        // transactional emails were logged but never captured. UseSsl=false because
        // smtp4dev on port 25 is plain SMTP (no STARTTLS).
        .WithEnvironment("Smtp__Host",
            ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Host)}"))
        .WithEnvironment("Smtp__Port",
            ReferenceExpression.Create($"{smtpEndpoint.Property(EndpointProperty.Port)}"))
        .WithEnvironment("Smtp__UseSsl", "false");
}

if (!string.IsNullOrEmpty(adminDefaultPassword))
{
    webApp.WithEnvironment("Admin__DefaultPassword", adminDefaultPassword);
}

// Publish mode → pin the container to the Production environment. ASP.NET Core's
// IHostEnvironment gates the recipient-allowlist bypass (FR-017/FR-019), the
// Mailgun runtime fail-fast, and the Storage FR-011 guard; without this the
// deployed container could resolve to a non-Production environment and silently
// drop every real recipient via RecipientAllowlistFilter. Run mode is left alone
// so local dev keeps Development (demo-user seeding, dev exception page, etc.).
if (builder.ExecutionContext.IsPublishMode)
{
    webApp.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
}

// Spec 021 — forward Notifications config to the Web container. Conditional-on-
// present so local run mode keeps the appsettings default (Provider=Mailtrap) and
// the smtp4dev Mailtrap Host/Port wiring above; publish mode (azd env / Key Vault
// supplies the keys, validated by the FR-016 fail-fast) routes prod mail through
// MailgunHttpEmailSender. The runtime allowlist bypass + Mailgun fail-fast still
// require ASPNETCORE_ENVIRONMENT=Production on the container (set via azd).
void ForwardNotification(string envKey, string? value)
{
    if (!string.IsNullOrWhiteSpace(value))
    {
        webApp.WithEnvironment(envKey, value);
    }
}

ForwardNotification("Notifications__Provider",         notificationsProvider);
ForwardNotification("Notifications__BaseUrl",          notificationsBaseUrl);

// Spec 041 bugfix — in local run mode, when Notifications:BaseUrl is NOT explicitly
// configured, pin it to the Web app's own Aspire-assigned external endpoint. This is
// the base used by BACKGROUND-dispatched mail (the outbox dispatch worker + stage-
// reminder worker), which has no HTTP request to fall back to, so without this it used
// the stale appsettings localhost default and produced broken email image URLs. Direct-
// send mail already resolves the live request host via IEmailBaseUrlProvider. Skipped in
// publish mode (Azure uses the container template / azd-env value).
if (!builder.ExecutionContext.IsPublishMode && string.IsNullOrWhiteSpace(notificationsBaseUrl))
{
    webApp.WithEnvironment("Notifications__BaseUrl", webApp.GetEndpoint("https"));
}
ForwardNotification("Notifications__Mailgun__ApiKey",  mailgunApiKey);
ForwardNotification("Notifications__Mailgun__Domain",  mailgunDomain);
ForwardNotification("Notifications__Mailgun__BaseUrl", mailgunBaseUrl);
ForwardNotification("Notifications__Sender__Name",     notificationsSenderName);
ForwardNotification("Notifications__Sender__Email",    notificationsSenderEmail);

// Spec 014 (T014): wire Storage provider configuration to the Web project.
webApp.WithEnvironment("Storage__Provider", storageProvider);
if (blobsResource is not null)
{
    webApp.WithReference(blobsResource).WaitFor(blobsResource);
    // Ensure the resolved connection string from the Aspire emulator is surfaced
    // under the Storage:ConnectionString key the platform consumes.
    webApp.WithEnvironment("Storage__ConnectionString", blobsResource.Resource.ConnectionStringExpression);
}

// Spec 014 (T036) — propagate the test-fallback flag so the Web project can
// honour it when Azurite is unreachable. When the operator forced
// Provider=LocalFilesystem with the flag enabled, push the temp root we
// provisioned above into config so LocalFilesystemObjectStorage has somewhere
// safe to read/write without leaking outside the test sandbox.
webApp.WithEnvironment(
    "Storage__TestFallback__AllowFilesystem",
    allowFilesystemFallback ? "true" : "false");
if (!string.IsNullOrEmpty(localFilesystemRoot))
{
    webApp.WithEnvironment("Storage__LocalFilesystem__RootPath", localFilesystemRoot);
}

// Container Apps deploy: replace Aspire's synthesized image with a Dockerfile that bakes in
// the Chromium runtime libs Syncfusion's BlinkConverter needs (libnss3, libgbm1, libgtk-3-0t64,
// fonts, etc.). Without these the published image throws
// "Failed to launch chromium: Missing required dependent packages" at PDF render time.
// Run mode is unaffected — PublishAsDockerFile only kicks in during `azd publish`.
webApp.PublishAsDockerFile(container =>
    container.WithDockerfile(
        contextPath: "../..",
        dockerfilePath: "src/FundingPlatform.Web/Dockerfile"));

builder.Build().Run();
