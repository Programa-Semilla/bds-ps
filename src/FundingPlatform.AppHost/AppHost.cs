var builder = DistributedApplication.CreateBuilder(args);

// Tests pass --EphemeralStorage=true to opt out of dev-convenience features that
// don't survive a fresh-container test run: the persistent SQL data volume and
// the auto-deploy SQL project. The E2E fixture deploys the dacpac itself via
// sqlpackage so it can wait synchronously for completion before tests start.
var ephemeralStorage = string.Equals(
    builder.Configuration["EphemeralStorage"], "true", StringComparison.OrdinalIgnoreCase);

var sqlBuilder = builder.AddSqlServer("sqlserver");
if (!ephemeralStorage)
{
    sqlBuilder = sqlBuilder.WithDataVolume("fundingplatform-sqldata");
}
else
{
    // Bind /var/opt/mssql to a tmpfs so SQL Server's data dir lives in RAM and
    // dies with the container — prevents Docker from creating an anonymous
    // volume per test run that piles up on the host (issue observed
    // 2026-05-01: hundreds of dangling volumes after repeat fixture runs).
    sqlBuilder = sqlBuilder.WithContainerRuntimeArgs("--tmpfs", "/var/opt/mssql");
}

var sqlServer = sqlBuilder.AddDatabase("fundingdb");

if (!ephemeralStorage)
{
    builder.AddSqlProject<Projects.FundingPlatform_Database>("database-schema")
           .WithReference(sqlServer);
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
var currencyIsoCode = builder.Configuration["FundingAgreement:CurrencyIsoCode"] ?? "COP";
var funderLegalName = builder.Configuration["FundingAgreement:Funder:LegalName"] ?? "";
var funderTaxId = builder.Configuration["FundingAgreement:Funder:TaxId"] ?? "";
var funderAddress = builder.Configuration["FundingAgreement:Funder:Address"] ?? "";
var funderContactEmail = builder.Configuration["FundingAgreement:Funder:ContactEmail"] ?? "";
var funderContactPhone = builder.Configuration["FundingAgreement:Funder:ContactPhone"] ?? "";
var signedUploadMaxSizeBytes = builder.Configuration["SignedUpload:MaxSizeBytes"] ?? "20971520";
var adminReportsDefaultCurrency = builder.Configuration["AdminReports:DefaultCurrency"] ?? "COP";
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
    .WithEnvironment("FundingAgreement__Funder__LegalName", funderLegalName)
    .WithEnvironment("FundingAgreement__Funder__TaxId", funderTaxId)
    .WithEnvironment("FundingAgreement__Funder__Address", funderAddress)
    .WithEnvironment("FundingAgreement__Funder__ContactEmail", funderContactEmail)
    .WithEnvironment("FundingAgreement__Funder__ContactPhone", funderContactPhone)
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

builder.Build().Run();
