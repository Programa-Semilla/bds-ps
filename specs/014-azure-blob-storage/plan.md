# Implementation Plan: Azure Blob Storage with Environment-Driven Provider Selection

**Branch**: `014-azure-blob-storage` | **Date**: 2026-05-01 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/014-azure-blob-storage/spec.md`

## Summary

Replace the current single-implementation `IFileStorageService` (file-system only, single flat directory under `FileStorage:Path`) with a richer `IObjectStorage` abstraction that supports categorized containers, deterministic keys, streaming upload/download, existence/delete, and a serving handle (stream or time-limited URL). Wire three implementations selected at runtime by `Storage:Provider`: `AzureBlob` (production, managed-identity-first), `Azurite` (default for local dev, provisioned by Aspire), and `LocalFilesystem` (offline opt-in / test fallback). Retrofit every call site (signed-PDF upload, supplier catalog import, application attachments, Syncfusion-generated PDFs) to consume the new abstraction. Keep the existing 20 MiB cap, surface per-category caps, and enforce streaming above 1 MiB. The platform is greenfield — there is no legacy on-disk corpus and no migration tool; the abstraction is the day-one design.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies (existing)**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire 13.2.x, Syncfusion HtmlToPdfConverter, Tabler.io (vendored), Playwright for .NET (test only)
**Primary Dependencies (new, justified)**:
- `Aspire.Hosting.Azure.Storage` (AppHost orchestration of Azurite + Azure Storage account references)
- `Azure.Storage.Blobs` (BlobContainerClient / BlobClient — actual SDK)
- `Azure.Identity` (DefaultAzureCredential / managed-identity chain in production)
- `Aspire.Azure.Storage.Blobs` (Aspire client integration package on the Web project — health checks, OTel)
- `Microsoft.Extensions.Azure` (transitive — DI extensions for Azure clients)
**Storage**: Azure Blob Storage in production / Azurite (Docker container) in dev+test / local filesystem fallback. SQL Server unchanged.
**Testing**: xUnit (unit, integration), Playwright for .NET (E2E). Integration + E2E run against Azurite via existing `AspireFixture` (extended to provision Azurite resource).
**Target Platform**: Linux container (production Azure App Service or Container Apps), developer machines (Linux/macOS/Windows with Docker), CI agents (GitHub Actions / Azure DevOps Linux runners).
**Project Type**: ASP.NET MVC web application orchestrated by .NET Aspire (existing layout).
**Performance Goals**: Memory bounded for 100 MiB streams (SC-006). Azurite local-dev healthy within 30 s of AppHost start (SC-008). Storage operation retry budget ≤ 30 s, ≤ 3 retries.
**Constraints**: No mocks in integration tests (CLAUDE.md). Vendored UI assets only — Storage NuGet packages are server-side and exempt. Production credentials via managed identity (FR-011). Keys deterministic (FR-014).
**Scale/Scope**: Funding platform: hundreds of applicants, low-thousands of signed agreements + attachments. Throughput requirements not aggressive; durability and auditability dominate.

## Constitution Check

*Re-evaluated after Phase 1 design — see "Post-Design Re-check" below.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | PASS | `IObjectStorage` lives in Application layer (use-case interface), implementations in Infrastructure. Web layer composes via DI. Aspire wiring is the documented exception. The legacy `IFileStorageService` in `Domain.Interfaces` is moved to `Application.Abstractions.Storage` — see Complexity Tracking. |
| II. Rich Domain Model | PASS | No new domain entities. Existing entities (FundingAgreement, Quotation, etc.) keep their behavior; the storage relocation only replaces a port. |
| III. End-to-End Testing (NON-NEGOTIABLE) | PASS | Playwright E2E coverage for: signed-PDF upload + download against Azurite; supplier catalog upload against Azurite; provider switch (Azurite → LocalFilesystem) via configuration; oversize rejection. Integration tests cover migration command + every provider against the Azurite container provisioned by `AspireFixture`. |
| IV. Schema-First Database Management | PASS | New domain field `BlobKey` (`nvarchar(1024) NOT NULL`) added to `FundingAgreements`, `SignedUploads`, and `Documents`. Schema change lands in the dacpac. No backfill script — rows are inserted with a populated `BlobKey` from the moment the feature ships. EF migrations / `EnsureCreated` remain prohibited. |
| V. Specification-Driven Development | PASS | spec.md, this plan, and tasks.md (next stage) drive the work. |
| VI. Simplicity and Progressive Complexity | PASS | Complexity tracked below. The interface-based design was already sanctioned by the constitution as "future swap to cloud storage" — that future is now. We add three implementations; we do not introduce CDN, queues, or large-file resumable uploads (deferred). |

### Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New NuGet packages (`Aspire.Hosting.Azure.Storage`, `Aspire.Azure.Storage.Blobs`, `Azure.Storage.Blobs`, `Azure.Identity`, `Microsoft.Extensions.Azure`) | First-party Azure storage SDK + Aspire integration is the supported path; vendoring it is impractical (multi-MB binary, frequent CVE updates). | Manually calling the Azure REST API would force us to re-implement retry, streaming chunking, and managed-identity token acquisition. Not justifiable. |
| Moving `IFileStorageService` from `Domain.Interfaces` to `Application.Abstractions.Storage` (and renaming to `IObjectStorage`) | The new abstraction needs DTOs (`StoredObject`, `StorageHandle`, category enum) and operates on use-case primitives, which violates "Domain has zero external dependencies" if left in Domain. | Keeping the interface in Domain forced the LocalFileStorageService into Domain via its types; that would now require Domain to know about `BlobContainerClient` indirectly. Moving the port up is the constitution-aligned fix. |

## Project Structure

### Documentation (this feature)

```text
specs/014-azure-blob-storage/
├── plan.md                    # This file
├── spec.md
├── research.md                # Phase 0 — package choices, identity model, key-format details
├── data-model.md              # Phase 1 — StoredObject, StorageHandle, FileCategory, ObjectKey
├── contracts/
│   └── IObjectStorage.md      # Phase 1 — port contract (method shapes, error semantics)
├── quickstart.md              # Phase 1 — how to run AppHost with each provider
├── checklists/
│   └── requirements.md
├── REVIEW-SPEC.md             # produced by Stage 2
├── REVIEW-PLAN.md             # produced by Stage 5
├── REVIEW-CODE.md             # produced by Stage 7
└── tasks.md                   # produced by Stage 4
```

### Source Code (repository root)

```text
src/
  FundingPlatform.AppHost/
    AppHost.cs                                  # +Azurite resource, +Azure.Storage account ref, +env wiring
    FundingPlatform.AppHost.csproj              # +Aspire.Hosting.Azure.Storage
  FundingPlatform.Application/
    Abstractions/
      Storage/
        IObjectStorage.cs                       # NEW — port (upload/download/exists/delete/handle)
        StoredObject.cs                         # NEW — record (Container, Key, Size, ContentType, CreatedAt, Provider)
        StorageHandle.cs                        # NEW — discriminated: BackendStream | TimeLimitedUrl
        FileCategory.cs                         # NEW — enum (SignedFundingAgreement, SupplierCatalogImport, ApplicationAttachment, GeneratedArtifact)
        ObjectKey.cs                            # NEW — value object (Build, Parse) implementing FR-014
        StorageOptions.cs                       # NEW — bound from "Storage:" section
  FundingPlatform.Infrastructure/
    Storage/
      AzureBlobObjectStorage.cs                 # NEW — AzureBlob + Azurite implementation (same SDK)
      LocalFilesystemObjectStorage.cs           # NEW — replaces LocalFileStorageService
      ObjectStorageDiagnostics.cs               # NEW — single ILogger wrapper enforcing FR-025 fields
      ObjectStorageRegistration.cs              # NEW — DI: select provider by Storage:Provider
      Legacy/
        FileStorageServiceFacade.cs             # NEW — adapter to keep IFileStorageService callers working during migration window (deleted in last task)
    DependencyInjection.cs                       # +AddObjectStorage(...)
  FundingPlatform.Web/
    Controllers/FundingAgreementController.cs   # CHANGED — uses IObjectStorage
    Controllers/SupplierController.cs           # CHANGED
    Controllers/QuotationController.cs          # CHANGED if it touches files (audit step)
    Controllers/Admin/AdminReportsController.cs # CHANGED if writes anything to disk
    Helpers/IllustrationHelper.cs               # CHANGED if writes anything to disk
  FundingPlatform.Database/
    dbo/Tables/                                  # CHANGED — add BlobKey nvarchar(1024) NOT NULL
  FundingPlatform.Domain/
    Interfaces/IFileStorageService.cs           # DELETED at end (after callers migrated)

tests/
  FundingPlatform.Tests.Unit/
    Storage/
      ObjectKeyTests.cs                         # FR-014 conformance
      StorageOptionsTests.cs                    # config binding
      ObjectStorageDiagnosticsTests.cs          # FR-025 — keys/sizes/durations logged, contents are not
  FundingPlatform.Tests.Integration/
    Storage/
      AzuriteObjectStorageTests.cs              # upload/download/exists/delete/url roundtrip via Aspire fixture
      LocalFilesystemObjectStorageTests.cs      # parity tests
      OversizeUploadTests.cs                    # FR-021 / FR-022 per category
  FundingPlatform.Tests.E2E/
    Storage/
      SignedFundingAgreementUploadDownloadTests.cs  # P1 user-story coverage
      SupplierCatalogUploadTests.cs                  # P1 / P2
      ProviderSwitchE2ETests.cs                       # configures LocalFilesystem, restarts, downloads succeed
      OversizeRejectionTests.cs                       # FR-022
```

**Structure Decision**: existing Aspire-orchestrated MVC layout is reused. The only addition is a `Storage/` folder in Application and Infrastructure. The constitution's project layout is preserved.

## Phase 0 — Research summary

Output: `research.md`. Resolves:
- Which Azure SDK + Aspire integration packages to use, why, and which alternatives were rejected.
- Authentication chain: `DefaultAzureCredential` order in production vs. connection-string fallback in lower environments.
- Aspire Azurite resource provisioning — how to wire the Web project's `BlobServiceClient` to the emulator without conditional code.
- Object key format details (FR-014) — character set, length cap, container naming rules.
- Test fixture changes — extending `AspireFixture` so `EphemeralStorage=true` provisions a clean Azurite container per fixture run.

## Phase 1 — Design & Contracts

Outputs: `data-model.md`, `contracts/IObjectStorage.md`, `quickstart.md`, agent context update.

- `data-model.md` documents `StoredObject`, `StorageHandle` (BackendStream / TimeLimitedUrl), `FileCategory` enum, `ObjectKey` value object, the new EF persistence column (`BlobKey nvarchar(1024) NOT NULL`), and the relationships between them.
- `contracts/IObjectStorage.md` documents the port:
  - `Task<StoredObject> UploadAsync(FileCategory category, ObjectKey key, Stream content, string contentType, long? contentLength, CancellationToken ct)`
  - `Task<Stream> OpenReadAsync(FileCategory category, ObjectKey key, CancellationToken ct)`
  - `Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)`
  - `Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)`
  - `Task<StorageHandle> ResolveServingHandleAsync(FileCategory category, ObjectKey key, ServingMode preferred, CancellationToken ct)`
  - Error semantics: `ObjectNotFoundException`, `ObjectStorageOperationException` with exhausted-retry sentinel, oversize rejection at the controller layer (not the port).
- `quickstart.md` shows three commands: production deploy with managed identity, local AppHost with Azurite (default), local AppHost with `LocalFilesystem` opt-in.
- Agent context update via `.specify/scripts/bash/update-agent-context.sh claude` adds `Aspire.Hosting.Azure.Storage`, `Azure.Storage.Blobs`, and `Azure.Identity` to the recognized stack.

## Phase 2 — Tasks (deferred to /speckit-tasks)

Tasks are generated by `/speckit-tasks` from this plan; not produced here. The expected story-driven decomposition: Foundation (port + DTOs + diagnostics + dacpac column), Story 2 (local-dev Azurite via Aspire), Story 1 (production AzureBlob impl), Story 3 (test fixture wiring + integration suite), Story 5 (oversize rejection alignment), Polish (delete legacy `IFileStorageService`, runbook, agent context refresh).

## Post-Design Re-check (Constitution)

After drafting this plan and the Phase 1 artifacts, the Constitution Check is re-evaluated. Result: **PASS**, with the two Complexity Tracking entries above accepted as justified. No new violations introduced by Phase 1 design.
