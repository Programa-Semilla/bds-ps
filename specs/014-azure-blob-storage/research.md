# Research: Azure Blob Storage with Environment-Driven Provider Selection

**Feature**: 014-azure-blob-storage
**Date**: 2026-05-01

This document resolves the technical unknowns identified in plan.md Phase 0 before contracts and data model are designed.

## R1 — Aspire integration packages

**Decision**: Use the first-party Aspire integration triplet:
- `Aspire.Hosting.Azure.Storage` on `FundingPlatform.AppHost` for orchestration (provisions Azurite locally, references an Azure Storage account in production via `azd` parameter binding).
- `Aspire.Azure.Storage.Blobs` on `FundingPlatform.Web` for the client integration — adds a configured `BlobServiceClient`/`BlobContainerClient`, OTel spans, and health-checks out of the box.
- `Azure.Storage.Blobs` is pulled in transitively but consumed directly for typed `BlobClient` operations.
- `Azure.Identity` for `DefaultAzureCredential` in production.

**Rationale**: Aspire's hosting integration is the documented, supported way to run Azurite alongside the rest of the dev stack. The client integration package wires `IBlobServiceClient` into DI with the right credentials based on the resource reference (connection string locally, managed identity in production). Hand-rolling either layer would re-implement Aspire conventions and break health-check / dashboard parity.

**Alternatives considered**:
- **Manual `BlobServiceClient` registration**: rejected — gives up health checks, OTel, and resource discovery. Would also require duplicating the AppHost-vs-Web credential resolution code.
- **Containerized Azurite invoked via `docker compose` outside Aspire**: rejected — splits the local-dev story across two orchestrators, breaks the "one command" SC-001 success criterion.
- **Use `MinIO` as a generic S3-compatible local emulator**: rejected — Azurite has fewer emulation gaps for blob-only workloads, is officially supported, and matches production semantics 1:1.

## R2 — Authentication chain

**Decision**:
- Production: `DefaultAzureCredential` configured with `ManagedIdentityCredential` first in the chain. Connection-string usage in `Production` environment logs a warning at startup and is rejected by the deployment template (env-var check in `appsettings.Production.json` + a startup health check).
- `Development` / `Staging`: `DefaultAzureCredential` falls through to `EnvironmentCredential` / `AzureCliCredential` if managed identity is unavailable; connection strings are also accepted from Aspire connection-string references (`Storage:ConnectionString`).
- Local dev (Azurite) and tests: a hard-coded "well-known" Azurite connection string surfaced via Aspire — never read from user secrets, never persisted.

**Rationale**: Managed identity is the documented best practice for Azure-hosted .NET workloads (no rotated secrets, RBAC-driven). `DefaultAzureCredential` lets the same code path work locally for developers signed into the Azure CLI without #if/#else.

**Alternatives considered**:
- **Storage account key per environment**: rejected — secret rotation burden, cannot scope per workload.
- **SAS token at startup**: rejected — every workload would need a separate SAS issuer, and SAS tokens can't authenticate management-plane calls (e.g., container creation).

## R3 — Object key format details (FR-014)

**Decision**: `{category}/{owner-segment}/{entity-id}/{deterministic-suffix}.{ext}`
- Lowercase, ASCII-only. Container names match the Azure naming rules (lowercase, 3–63 chars, hyphens, no double hyphens) — already consistent with FR-013 names.
- `owner-segment`: `applicants/{applicantId}` (GUID, dashed lowercase) or `admin`.
- `entity-id`: the GUID of the owning aggregate (FundingApplication, Quotation, SupplierCatalogImportBatch, etc.) in dashed lowercase form.
- `deterministic-suffix`: GUID of the storage record itself (e.g., the `FundingAgreementSignature.BlobKey` GUID seed, generated when the row is created). The platform is greenfield — there is no legacy corpus to address with a content-derived suffix.
- `ext`: original extension from the upload, lowercased; if absent, `.bin`.
- Total key length capped at 1024 bytes (Azure Blob max is 1024 chars, leave headroom).

**Rationale**: Keys are deterministic from domain identifiers (FR-014), human-debuggable, and resistant to filename collision.

**Alternatives considered**:
- **Hash-only keys** (`{category}/{sha256(content)}`): rejected — not reconstructable from domain identifiers without scanning the blob first.
- **Sequential numeric keys**: rejected — not collision-resistant under multi-writer races.

## R4 — Aspire Azurite resource

**Decision**: In `AppHost.cs` add a single `AddAzureStorage("storage").RunAsEmulator()` call when not in `EphemeralStorage` test mode. Tests opt into a fresh emulator per fixture run by passing `EphemeralStorage=true` (already the existing pattern for SQL).

```csharp
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emu => emu.WithDataVolume("fundingplatform-blobdata"));

var blobs = storage.AddBlobs("blobs");

webApp.WithReference(blobs);
```

In production deployments, `RunAsEmulator()` is replaced by `ProvisionAsExisting(...)` resolving to the env-specific storage account; the Web project's reference is identical.

**Rationale**: Mirrors the existing SQL Server pattern (volume in dev, ephemeral in tests). Keeps `EphemeralStorage` as the single switch the fixture toggles.

**Alternatives considered**:
- **A separate Azurite resource per category**: rejected — Azure Blob Storage uses a single account with multiple containers; modeling per-container resources is over-decomposition.

## R5 — Streaming upload safety

**Decision**: Use `BlockBlobClient.UploadAsync(Stream, BlobUploadOptions)` with `TransferOptions.MaximumConcurrency = 1, InitialTransferSize = 1 MiB, MaximumTransferSize = 4 MiB`. Above the 1 MiB streaming threshold (FR-020), the SDK chunks transparently and never buffers the full payload. Below the threshold, a single block write is used.

**Rationale**: Default SDK behavior already streams; tightening the chunk-size knobs makes memory usage predictable for SC-006 (≤ 2× the streaming buffer for a 100 MiB payload). The reduced concurrency keeps memory deterministic in the funding platform's modest-throughput context.

**Alternatives considered**:
- **`OpenWriteAsync()` / append blob model**: rejected — append blobs aren't suited for one-shot uploads; OpenWrite requires manual flush discipline.
- **Manual chunking via REST**: rejected — re-implements SDK behavior with no benefit.

## R6 — Test fixture changes

**Decision**: Extend `AspireFixture` so that `EphemeralStorage=true` (already used for SQL) also:
- Provisions an Azurite container with `--cleanup` (fresh state).
- Awaits the container's health endpoint before yielding to tests (existing wait pattern reused).
- Pre-creates the four containers (`signed-funding-agreements`, `supplier-catalog-imports`, `application-attachments`, `generated-artifacts`) so per-test setup doesn't repeat container creation.
- Exposes the connection string as `Storage:ConnectionString` to the Web app.

**Rationale**: Keeps the existing `EphemeralStorage=true` switch as the single test-mode flag. Pre-creating containers shaves a noticeable chunk off integration test startup.

**Alternatives considered**:
- **Per-test Azurite**: rejected — startup cost compounds; Azurite isolation between tests is achieved via per-test key prefixes (test name + GUID), not per-test containers.

## R7 — Existing call-site inventory

**Decision**: Repository search `grep -rn 'FileStream|File\.Open|File\.Write|File\.Read' src/ --include='*.cs'` identifies the call sites that need to use the new abstraction:

| Path | Migration target |
|------|------------------|
| `src/FundingPlatform.Web/Controllers/FundingAgreementController.cs` | `IObjectStorage.UploadAsync(FileCategory.SignedFundingAgreement, …)` for the generated agreement and `ResolveServingHandleAsync(…)` for downloads. |
| `src/FundingPlatform.Application/Services/SignedUploadService.cs` | `FileCategory.SignedFundingAgreement` for signed-PDF intake. |
| `src/FundingPlatform.Application/Services/ApplicationService.cs` | `FileCategory.ApplicationAttachment` for quotation documents. |

Static UI assets (`wwwroot`) and admin report streams (Response.Body) are not platform storage and are out of scope.

**Rationale**: Captures every call site so tasks.md can map work 1:1.

## R8 — Out-of-scope confirmations

The following are explicitly NOT addressed in this feature and are deferred:
- CDN / public asset distribution (already out of scope per spec).
- Cross-region replication policy values.
- Background virus scanning (would require a queue + scanner service; not requested).
- Resumable uploads / multipart for files >100 MiB (current cap 50 MiB max per category).
- A separate generic file-share product (Azure Files, NFS).
