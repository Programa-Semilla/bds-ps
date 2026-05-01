# Quickstart: Azure Blob Storage with Environment-Driven Provider Selection

**Feature**: 014-azure-blob-storage

This quickstart walks through the three configurations the abstraction supports.

## 1. Local development (default — Azurite)

No setup beyond Docker.

```bash
# from repo root
dotnet run --project src/FundingPlatform.AppHost
```

Aspire boots SQL Server, Azurite, and the Web project. Aspire dashboard shows the `storage` resource healthy within ~30 s. The Web project resolves a `BlobServiceClient` against Azurite's well-known endpoint automatically. No connection strings to set.

To inspect blobs:
- Azure Storage Explorer connected to `http://127.0.0.1:10000/devstoreaccount1` (Azurite emulator account).
- Or `az storage blob list --account-name devstoreaccount1 --container-name signed-funding-agreements --connection-string "$AZURITE_CONN"`.

## 2. Local development (offline opt-in — LocalFilesystem)

Useful when Docker is not available (e.g. on a constrained dev VM).

```bash
# in user secrets or appsettings.Development.json
Storage:Provider = LocalFilesystem
Storage:LocalFilesystem:RootPath = /tmp/funding-platform-storage
```

Restart the AppHost. Azurite is no longer started. Files are written under `/tmp/funding-platform-storage/{category}/{owner-segment}/{entity-id}/{suffix}.{ext}` mirroring the cloud key layout.

## 3. Production (managed identity)

Production deployment templates set:

```yaml
Storage:Provider: AzureBlob
Storage:AccountReference: <azd-resolved storage account name>
# no Storage:ConnectionString
```

Managed identity is acquired via `DefaultAzureCredential`. The deployment workflow asserts the platform's identity has the `Storage Blob Data Contributor` role on the target account.

If `Storage:ConnectionString` is set in `Production`, the AppHost logs a warning at startup and the deployment template's gating step rejects the deploy.

## 4. Automated tests (Azurite via AspireFixture)

```bash
dotnet test tests/FundingPlatform.Tests.Integration
dotnet test tests/FundingPlatform.Tests.E2E
```

`AspireFixture` is invoked with `--EphemeralStorage=true`, which:
1. Starts a fresh Azurite container.
2. Awaits its health endpoint.
3. Pre-creates the four containers from FR-013.
4. Yields to tests with a clean state every fixture run.

To opt into the LocalFilesystem fallback (only when Azurite is unreachable, e.g. constrained CI):

```bash
dotnet test tests/FundingPlatform.Tests.Integration -- \
  --environment Storage__TestFallback__AllowFilesystem=true
```

A warning is logged. Tests still pass; provider parity coverage is reduced.

## 5. Smoke test

```bash
# inside the AppHost dashboard, open the Web project URL
# 1. log in
# 2. upload a signed funding agreement on an existing application
# 3. observe the upload succeed and a download link work
# 4. restart the AppHost (Ctrl+C, re-run)
# 5. download the same signed PDF — bytes match
```

## 6. CI parity (FR-009)

The repo has no GitHub Actions workflows checked in (`.github/workflows/` is
absent at the time of writing, per the convention that automation is owned
by a separate ops repo). When CI is wired up, the integration + E2E suites
MUST run the same Aspire-Azurite-backed pipeline as developer laptops, with
no shared Azure secret. Any runner with Docker satisfies the prerequisite.

Run on a Linux runner:

```bash
# Restore + build once.
dotnet restore FundingPlatform.slnx
dotnet build FundingPlatform.slnx --configuration Release --no-restore

# Schema + Application units (no Azure dependency).
dotnet test tests/FundingPlatform.Tests.Unit \
    --configuration Release --no-build

# Integration suite — Azurite via the AzuriteFixture (Docker required).
dotnet test tests/FundingPlatform.Tests.Integration \
    --configuration Release --no-build

# E2E suite — Aspire orchestrator boots the Web project + Azurite + SQL Server.
dotnet test tests/FundingPlatform.Tests.E2E \
    --configuration Release --no-build
```

When the workflow lands, mirror these three invocations and require the job
to fail if `AZURE_*` credentials leak into the runner environment (the
`HermeticEnvironmentTests` fixture asserts this in-band).

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| AppHost startup error: "Storage provider 'AzureBlob' configured but no account reference" | `Storage:Provider=AzureBlob` without an account / connection string. | Add the account reference (managed identity in prod) or switch to Azurite locally. |
| Logs show `ObjectStorage.Upload outcome=RetryExhausted` | Azure transient outage exceeded the 30 s budget. | Retry the user action; if persistent, check the Azure Storage account health. |
| `LocalProviderUrlNotSupportedException` | Caller asked for a `TimeLimitedUrl` against the LocalFilesystem provider. | Switch the request to `BackendStream` or run against `Azurite`/`AzureBlob`. |
