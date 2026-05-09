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

// E2E fixture runs with EphemeralStorage=true and a fresh DB per fixture run, so
// the sentinel admin (admin@FundingPlatform.com) is seeded on every startup. In
// ephemeral mode we force the deterministic test password regardless of other
// config layers — otherwise an appsettings.Development.json entry (added by
// spec 010) wins via `??` and the test fixture can't predict the password.
// Outside ephemeral, fall back to whatever Admin:DefaultPassword is configured.
var adminDefaultPassword = ephemeralStorage
    ? "Sentinel123!"
    : builder.Configuration["Admin:DefaultPassword"];

var webApp = builder.AddProject<Projects.FundingPlatform_Web>("webapp")
    .WithExternalHttpEndpoints()
    .WithReference(sqlServer)
    .WaitFor(sqlServer)
    .WithEnvironment("Syncfusion__LicenseKey", syncfusionLicense)
    .WithEnvironment("FundingAgreement__LocaleCode", localeCode)
    .WithEnvironment("FundingAgreement__CurrencyIsoCode", currencyIsoCode)
    .WithEnvironment("SignedUpload__MaxSizeBytes", signedUploadMaxSizeBytes)
    .WithEnvironment("AdminReports__DefaultCurrency", adminReportsDefaultCurrency)
    .WithEnvironment("AdminReports__CsvRowLimit", adminReportsCsvRowLimit);

if (!string.IsNullOrEmpty(adminDefaultPassword))
{
    webApp.WithEnvironment("Admin__DefaultPassword", adminDefaultPassword);
}

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
