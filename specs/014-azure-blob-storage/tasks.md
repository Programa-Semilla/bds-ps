---

description: "Task list for 014-azure-blob-storage"
---

# Tasks: Azure Blob Storage with Environment-Driven Provider Selection

**Input**: Design documents from `/specs/014-azure-blob-storage/`
**Prerequisites**: spec.md, plan.md, research.md, data-model.md, contracts/IObjectStorage.md, quickstart.md

**Tests**: REQUIRED. The constitution mandates Playwright E2E for every user story (Principle III, NON-NEGOTIABLE) and integration tests against a real backend (no mocks; CLAUDE.md). Test tasks are written before or alongside implementation per task-level discipline; the suite as a whole must be green before delivery.

**Organization**: Tasks are grouped by user story so each can be implemented and validated independently. The MVP is US1 + US2 + US3 (P1 stories together), since the abstraction is only meaningful when production, local-dev, and tests all use it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks).
- **[Story]**: User story label (US1–US5).
- File paths are absolute or repo-rooted.

## Path Conventions

Repo-rooted paths anchored at `/mnt/D/repos/bds-ps`:
- App: `src/FundingPlatform.AppHost/`, `src/FundingPlatform.Application/`, `src/FundingPlatform.Infrastructure/`, `src/FundingPlatform.Web/`, `src/FundingPlatform.Database/`
- Tools: `tools/FundingPlatform.StorageMigration/`
- Tests: `tests/FundingPlatform.Tests.Unit/`, `tests/FundingPlatform.Tests.Integration/`, `tests/FundingPlatform.Tests.E2E/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring the new NuGet dependencies, project skeleton, and configuration surface online before any storage code lands.

- [x] T001 Add `<PackageReference Include="Aspire.Hosting.Azure.Storage" />` to `src/FundingPlatform.AppHost/FundingPlatform.AppHost.csproj` (version aligned with existing `Aspire.Hosting.SqlServer` 13.2.x).
- [x] T002 Add `<PackageReference Include="Aspire.Azure.Storage.Blobs" />` and `<PackageReference Include="Azure.Identity" />` to `src/FundingPlatform.Web/FundingPlatform.Web.csproj`. (Pulls in `Azure.Storage.Blobs` transitively.)
- [x] T003 [P] Create the `tools/FundingPlatform.StorageMigration/` console project (`dotnet new console -lang C# -f net10.0`), reference `FundingPlatform.Application` and `FundingPlatform.Infrastructure`, add to `FundingPlatform.slnx`. Include `Aspire.Azure.Storage.Blobs`, `Azure.Identity`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Configuration.Json`.
- [x] T004 [P] Create `src/FundingPlatform.Application/Abstractions/Storage/` directory and add empty placeholder files (`IObjectStorage.cs`, `StoredObject.cs`, `StorageHandle.cs`, `FileCategory.cs`, `ObjectKey.cs`, `StorageOptions.cs`) so subsequent tasks land cleanly.
- [x] T005 [P] Create `src/FundingPlatform.Infrastructure/Storage/` directory; ensure existing `FundingPlatform.Infrastructure/FileStorage/LocalFileStorageService.cs` is left in place untouched until T053.

**Checkpoint**: solution restores; `dotnet build FundingPlatform.slnx` succeeds with empty placeholders.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the port, value objects, configuration binding, diagnostics wrapper, dacpac schema delta, and Aspire wiring. Every user story depends on these.

**⚠️ CRITICAL**: No user story work may begin until this phase is complete.

### Application-layer types

- [x] T006 [P] Implement `FileCategory` enum (`SignedFundingAgreement`, `SupplierCatalogImport`, `ApplicationAttachment`, `GeneratedArtifact`) with `[Description]` attributes mapping each to its container name in `src/FundingPlatform.Application/Abstractions/Storage/FileCategory.cs`.
- [x] T007 [P] Implement `ObjectKey` value object with `Build(category, ownerSegment, entityId, suffix, extension)`, `Parse(string)`, `ToString()` and validation (lowercase, no `..`, length ≤ 1024) in `src/FundingPlatform.Application/Abstractions/Storage/ObjectKey.cs`.
- [x] T008 [P] Implement `StoredObject` record (Container, Key, SizeBytes, ContentType, CreatedAt, Provider) in `src/FundingPlatform.Application/Abstractions/Storage/StoredObject.cs`. Add `StorageProviderName` enum in same file or sibling.
- [x] T009 [P] Implement `StorageHandle` abstract record + `BackendStreamHandle`, `TimeLimitedUrlHandle` and `ServingMode` enum in `src/FundingPlatform.Application/Abstractions/Storage/StorageHandle.cs`.
- [x] T010 [P] Implement `StorageOptions` POCO matching `data-model.md` § StorageOptions in `src/FundingPlatform.Application/Abstractions/Storage/StorageOptions.cs`. Include nested `StorageRetryBudgetOptions`, `StorageLocalFilesystemOptions`, `StorageCategoryOptions` (carries `MaxSizeBytes`, `ServingMode`, `UrlExpirySeconds`, and a `RetentionPolicy` seam string defaulting to `"none"` per FR-023), `StorageTestFallbackOptions`. Add `IValidateOptions<StorageOptions>` for FR-021/FR-019/FR-011/FR-012 validation (URL expiry must be ≤ 15 minutes; LocalFilesystem must reject `ServingMode=TimeLimitedUrl`).
- [x] T011 [US-Foundation] Define `IObjectStorage` interface and the four exception types (`ObjectNotFoundException`, `OversizeException`, `LocalProviderUrlNotSupportedException`, `ObjectStorageOperationException` with a `Reason` enum carrying `RetryExhausted`/`Backend`) in `src/FundingPlatform.Application/Abstractions/Storage/IObjectStorage.cs` plus a sibling `Exceptions.cs`. Depends on T006–T010.

### Diagnostics wrapper

- [x] T012 Implement `ObjectStorageDiagnostics` in `src/FundingPlatform.Infrastructure/Storage/ObjectStorageDiagnostics.cs`. Wraps a delegate per operation, emits the structured log event from `data-model.md` § Logging shape with `event`, `container`, `key`, `sizeBytes`, `durationMs`, `outcome`, `provider`, `errorCode`. MUST NOT log blob contents or signed URLs. Depends on T011.

### DI / configuration

- [x] T013 Implement `ObjectStorageRegistration.AddObjectStorage(this IServiceCollection, IConfiguration)` in `src/FundingPlatform.Infrastructure/Storage/ObjectStorageRegistration.cs`. Reads `Storage:Provider`, validates the options, registers the corresponding singleton implementation, and registers `ObjectStorageDiagnostics`. Throws fail-fast on misconfiguration (FR-012). Wire it into `src/FundingPlatform.Infrastructure/DependencyInjection.cs`.

### Aspire orchestration

- [x] T014 Update `src/FundingPlatform.AppHost/AppHost.cs` to: (a) read `Storage:Provider` (default `Azurite`); (b) when not in `EphemeralStorage` and provider is `Azurite`, call `builder.AddAzureStorage("storage").RunAsEmulator(emu => emu.WithDataVolume("fundingplatform-blobdata")).AddBlobs("blobs")`; (c) when `EphemeralStorage=true` and provider is `Azurite`, run the emulator without a volume so each fixture run starts fresh; (d) push the storage reference into the Web project via `WithReference`; (e) propagate the relevant `Storage:*` env vars to the Web project.

### Database schema

- [x] T015 [P] Edit `src/FundingPlatform.Database/dbo/Tables/FundingAgreementSignatures.sql` (and the equivalent tables for SupplierCatalogImports, ApplicationAttachments, GeneratedAgreementArtifacts as confirmed in research.md R8) to add nullable `BlobKey nvarchar(1024) NULL` and nullable `LegacyPath nvarchar(1024) NULL` columns.  *Adapted: actual table names are `dbo.SignedUploads`, `dbo.FundingAgreements`, `dbo.Documents` per current dacpac.*
- [x] T016 [P] Add post-deployment script `src/FundingPlatform.Database/Scripts/Post-Deploy/014-backfill-legacy-paths.sql` that copies the existing absolute-path columns into `LegacyPath` where `LegacyPath IS NULL` (per `data-model.md`). Include it in the post-deploy publish profile.

### Foundational unit tests

- [x] T017 [P] Unit tests for `ObjectKey.Build`/`Parse`/validation rules (round-trip, illegal characters, length cap, default extension) in `tests/FundingPlatform.Tests.Unit/Storage/ObjectKeyTests.cs`. Depends on T007.
- [x] T018 [P] Unit tests for `StorageOptions` binding + validation (provider must be valid; LocalFilesystem requires `RootPath`; production environment + connection-string warns; per-category `MaxSizeBytes` defaults applied) in `tests/FundingPlatform.Tests.Unit/Storage/StorageOptionsTests.cs`. Depends on T010.
- [x] T019 [P] Unit tests for `ObjectStorageDiagnostics` log shape (single event per call, all required fields, content not leaked, error path tagged `RetryExhausted`) using an `ITestOutputHelper`-backed logger or `FakeLogger` in `tests/FundingPlatform.Tests.Unit/Storage/ObjectStorageDiagnosticsTests.cs`. Depends on T012.

**Checkpoint**: foundation compiles, validation rules tested, dacpac builds. AppHost starts with the new options surface (Azurite resource visible in dashboard) but no implementation routes traffic to it yet.

---

## Phase 3: User Story 2 (Priority: P1) — Local developer runs production-equivalent stack with one command 🎯 MVP slice

**Goal**: One-command local dev with Azurite + the new abstraction wired through Aspire. Land this first because it gives every subsequent story a runnable environment.

**Independent Test**: On a clean clone with Docker, `dotnet run --project src/FundingPlatform.AppHost` → Aspire dashboard shows `storage` healthy → upload + download + delete a file via the Web project succeed.

### Implementation

- [x] T020 [US2] Implement `AzureBlobObjectStorage` in `src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs` covering all `IObjectStorage` methods using `BlobServiceClient`. Wraps every operation in `ObjectStorageDiagnostics` and the configured retry budget (`Storage:RetryBudget`). Provides both `BackendStreamHandle` and `TimeLimitedUrlHandle` (SAS) outcomes; SAS expiry is read from `Storage:Categories:{name}:UrlExpirySeconds` with a 15-minute default cap (FR-019). Provider name surfaces as `AzureBlob` or `Azurite` based on the resolved endpoint. Depends on T011, T012, T013.
- [x] T021 [US2] Implement `LocalFilesystemObjectStorage` in `src/FundingPlatform.Infrastructure/Storage/LocalFilesystemObjectStorage.cs`. Maps `(category, key)` to `{RootPath}/{container}/{key}`, performs atomic writes via temp-file-then-rename, throws `LocalProviderUrlNotSupportedException` on `ServingMode.TimeLimitedUrl`. Depends on T011, T012.
- [x] T022 [US2] Wire `AddObjectStorage` into `src/FundingPlatform.Web/Program.cs` (or wherever the Web composition root lives — confirm via grep). Bind `StorageOptions` from configuration. Depends on T013, T020, T021.
- [x] T023 [US2] Container bootstrap: on Web app startup, call a hosted service that ensures the four containers from FR-013 exist (Azurite + AzureBlob only). Implementation: `src/FundingPlatform.Infrastructure/Storage/EnsureContainersHostedService.cs`. Depends on T020.
- [x] T024 [US2] Add a `FileStorageServiceFacade : IFileStorageService` adapter at `src/FundingPlatform.Infrastructure/Storage/Legacy/FileStorageServiceFacade.cs` that delegates to `IObjectStorage` using `FileCategory.GeneratedArtifact` (placeholder until callers migrate per their own stories). Register it so existing call sites keep compiling between stages. Depends on T020.

### Tests for User Story 2

- [x] T025 [P] [US2] Integration test against Azurite via `AspireFixture`: upload, download, exists, delete roundtrip in `tests/FundingPlatform.Tests.Integration/Storage/AzuriteObjectStorageTests.cs`. Verify byte-for-byte equality. Depends on T020.  *Adapted: uses standalone Docker-based AzuriteFixture (fixture-managed Aspire Azurite is wired in T035 for E2E).*
- [x] T026 [P] [US2] Integration test for `LocalFilesystemObjectStorage` parity (same scenarios) in `tests/FundingPlatform.Tests.Integration/Storage/LocalFilesystemObjectStorageTests.cs`. Verify URL request throws `LocalProviderUrlNotSupportedException`. Depends on T021.
- [x] T027 [P] [US2] Integration test confirming the Aspire-managed Azurite resource auto-creates the four containers from FR-013 within 30 s of fixture startup in `tests/FundingPlatform.Tests.Integration/Storage/ContainerBootstrapTests.cs`. Depends on T023.

**Checkpoint**: a developer running `dotnet run --project src/FundingPlatform.AppHost` can upload/download/delete via the new abstraction; the Web project still serves the legacy `IFileStorageService` callers via the facade until their stories land.

---

## Phase 4: User Story 1 (Priority: P1) — Production deployment uses managed cloud storage

**Goal**: Make `Storage:Provider=AzureBlob` against a real Azure Storage account viable, with managed identity, the production-time fail-fast guard, and authorization unchanged at the controller layer.

**Independent Test**: Deploy AppHost configured with `AzureBlob` against an Azure Storage account; signed-PDF upload/download succeed; restarted AppHost still serves the file; unauthenticated request returns 401/403 from the application without exposing a blob URL.

### Implementation

- [ ] T028 [US1] Migrate `src/FundingPlatform.Web/Controllers/FundingAgreementController.cs` from `IFileStorageService` to `IObjectStorage`: build `ObjectKey` from the signed-agreement aggregate, call `UploadAsync(FileCategory.SignedFundingAgreement, ...)` after authorization passes, persist `BlobKey` on the entity. Use `ResolveServingHandleAsync(..., ServingMode.BackendStream, ...)` for downloads. Depends on T020, T015.
- [ ] T029 [P] [US1] Update `FundingAgreementSignature` entity (in `src/FundingPlatform.Domain/Entities/`) to expose `BlobKey` and a behavior method `RecordBlob(ObjectKey)` rather than a setter (Constitution Principle II). Depends on T015.
- [x] T030 [US1] Production credential resolution: confirm `DefaultAzureCredential` chain in `AzureBlobObjectStorage` registration (T013); when `Environment == "Production"` and `Storage:ConnectionString` is set, the registration logs a warning AND a startup health-check returns Degraded so a deployment gate can fail (FR-011). Implement health-check in `src/FundingPlatform.Infrastructure/Storage/StorageProductionGuardHealthCheck.cs`. Depends on T013.
- [x] T031 [US1] Container access posture check: on container creation in `EnsureContainersHostedService`, set public-access level to `None`; on each AppHost startup, the same hosted service verifies anonymous access is disabled and refuses to start if any container has public access enabled (FR-027). Depends on T023.

### Tests for User Story 1

- [ ] T032 [P] [US1] E2E (Playwright): applicant signs a funding agreement → uploads signed PDF via the existing UI flow → AppHost restart → applicant downloads the same PDF; assert byte-for-byte match. Test file `tests/FundingPlatform.Tests.E2E/Storage/SignedFundingAgreementUploadDownloadTests.cs`. Depends on T028.
- [ ] T033 [P] [US1] E2E: unauthenticated request to a known blob path returns 401/403 from the application; the signed URL never appears in the HTML response. Test file `tests/FundingPlatform.Tests.E2E/Storage/SignedFundingAgreementAuthorizationTests.cs`. Depends on T028.
- [x] T034 [P] [US1] Integration test for the production guard: simulate `Environment=Production` + connection string → health check returns `Degraded` and startup logs the configured warning, in `tests/FundingPlatform.Tests.Integration/Storage/ProductionGuardTests.cs`. Depends on T030.

**Checkpoint**: signed-PDF flows go end-to-end through `IObjectStorage`. Local Azurite, ephemeral test Azurite, and a manually-pointed Azure account all work without code changes.

---

## Phase 5: User Story 3 (Priority: P1) — Automated tests run hermetically without Azure credentials

**Goal**: Bring the full integration + E2E suite green against Azurite without any Azure credentials, including the FR-008 fallback opt-in.

**Independent Test**: On a machine with no Azure credentials, `dotnet test tests/FundingPlatform.Tests.Integration` and `dotnet test tests/FundingPlatform.Tests.E2E` both pass.

### Implementation

- [ ] T035 [US3] Extend `tests/FundingPlatform.Tests.E2E/AspireFixture.cs` (and any sibling Integration fixture) to: (a) wait for the Azurite resource to be healthy before yielding; (b) pre-create the four containers from FR-013; (c) expose helpers for tests to compute keys and seed blobs. Depends on T014, T023.
- [ ] T036 [P] [US3] Implement the `Storage:TestFallback:AllowFilesystem` flag (FR-008): when set to `true` AND Azurite cannot start within a configured timeout, the fixture switches the Web project to `LocalFilesystem` with a temp directory and logs a warning. Update `AspireFixture` accordingly. Depends on T035.

### Tests for User Story 3

- [ ] T037 [P] [US3] Integration test that runs against `EphemeralStorage=true` with Azurite, uploads a fixture PDF, downloads it, asserts byte equality, in `tests/FundingPlatform.Tests.Integration/Storage/HermeticAzuriteRoundtripTests.cs`. Depends on T035.
- [ ] T038 [P] [US3] Integration test that forces Azurite startup failure (point the fixture at a port that's blocked), enables `Storage:TestFallback:AllowFilesystem=true`, and confirms the suite still passes against `LocalFilesystem` with the warning emitted, in `tests/FundingPlatform.Tests.Integration/Storage/TestFallbackTests.cs`. Depends on T036.
- [ ] T039 [P] [US3] E2E confirms a clean run with no Azure credentials by asserting absence of `AZURE_*` env vars at fixture start in `tests/FundingPlatform.Tests.E2E/Storage/HermeticEnvironmentTests.cs`. Depends on T035.
- [ ] T039a [US3] CI parity (FR-009): add a GitHub Actions workflow snippet (or update existing) under `.github/workflows/` to ensure pipelines run the same Aspire-Azurite-backed Integration + E2E suites with no shared Azure secret; document in `quickstart.md` § CI. Depends on T035.

**Checkpoint**: integration + E2E green on a developer laptop with no Azure secrets configured. Delivery bar (full personally-executed E2E green run) becomes achievable for this branch.

---

## Phase 6: User Story 4 (Priority: P2) — Existing on-disk files migrate cleanly

**Goal**: One-shot migration tool that moves every legacy file into the configured cloud backend, idempotently, with an auditable manifest.

**Independent Test**: Seed a temp dir with N known files matching the legacy layout, run the migration, observe every file present at its computed key, the source untouched, and a manifest covering 100% of files.

### Implementation

- [ ] T040 [US4] Implement `tools/FundingPlatform.StorageMigration/Program.cs`: command-line parses `--legacy-root`, `--provider`, `--account-reference`/`--connection-string`, `--manifest-out`, `--parallelism N` (default 1, max 8). Builds a host with `IObjectStorage` registered, walks the legacy root, looks up each file's owning row in `FundingDbContext` to derive `(FileCategory, ownerSegment, entityId)`, computes the deterministic suffix (SHA-256 prefix), calls `UploadAsync` if absent, writes the manifest. Depends on T020, T011.
- [ ] T041 [P] [US4] Implement `tools/FundingPlatform.StorageMigration/MigrationManifest.cs` (JSON Lines append-only writer + reader for re-runs and verification). Depends on T040 minimal scaffolding.
- [ ] T042 [P] [US4] Add a verifier subcommand `--verify` that re-reads the manifest and asserts every `Uploaded` entry still exists in the configured backend; exits non-zero on any drift. Depends on T040.
- [ ] T043 [US4] Update production deployment runbook (in `specs/014-azure-blob-storage/quickstart.md` § 5 — already drafted; refine after T040 lands) to specify the migration must run before the provider toggle.

### Tests for User Story 4

- [ ] T044 [P] [US4] Integration test that seeds a temp legacy dir + corresponding DB rows, runs `Program.Main`, then asserts every file exists at its key and the manifest matches expectations, in `tests/FundingPlatform.Tests.Integration/Storage/MigrationCommandTests.cs`. Depends on T040.
- [ ] T045 [P] [US4] Integration test for idempotency: run the migration twice, assert the second run reports `Skipped-Existing` for every entry and exits 0, in `tests/FundingPlatform.Tests.Integration/Storage/MigrationIdempotencyTests.cs`. Depends on T040.
- [ ] T046 [P] [US4] Integration test for failure handling: seed a corrupted/unreadable file, assert the manifest records `Failed`, the run exits non-zero, and other files still upload, in `tests/FundingPlatform.Tests.Integration/Storage/MigrationFailureHandlingTests.cs`. Depends on T040.

**Checkpoint**: migration tool exists, has tests, the runbook documents its use.

---

## Phase 7: User Story 5 (Priority: P2) — Oversized uploads rejected before touching storage

**Goal**: Per-category caps (FR-021) wired through controllers; no oversize bytes ever reach the backend regardless of provider.

**Independent Test**: Submit a > cap file via each upload endpoint; receive a localized 413-equivalent error; observe no blob in the configured backend; observe no successful `ObjectStorage.Upload` log entry.

### Implementation

- [ ] T047 [US5] Implement an `IUploadSizeGuard` (or controller filter) that, for each `FileCategory`, reads `Storage:Categories:{name}:MaxSizeBytes` from the resolved `StorageOptions` and rejects oversized uploads at the controller boundary BEFORE streaming to `IObjectStorage`. Place in `src/FundingPlatform.Web/Filters/UploadSizeGuardAttribute.cs`. Depends on T010.
- [ ] T048 [US5] Apply `UploadSizeGuard` to every upload action in `FundingAgreementController`, `SupplierController`, `QuotationController` (or whichever controllers handle `ApplicationAttachment`). Depends on T047, T028.
- [ ] T049 [P] [US5] Localize the rejection message in es-CR + any other resource files used by the project; reuse existing localization infrastructure from spec 012.

### Tests for User Story 5

- [ ] T050 [P] [US5] E2E: submit a 25 MiB file to the signed-PDF endpoint with the cap at 20 MiB → assert the localized error, and that no blob was created in Azurite, in `tests/FundingPlatform.Tests.E2E/Storage/SignedFundingAgreementOversizeRejectionTests.cs`. Depends on T048.
- [ ] T051 [P] [US5] Integration test parameterized across all four categories asserting per-category caps are enforced, in `tests/FundingPlatform.Tests.Integration/Storage/PerCategoryOversizeTests.cs`. Depends on T048.

**Checkpoint**: oversize rejection is enforced uniformly; the controller boundary is the only place a cap is checked.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T052 [P] Migrate `SupplierController.cs`, `QuotationController.cs`, and any remaining `IFileStorageService` callers (per research.md R8 inventory) to `IObjectStorage`. After this task, the facade has no remaining callers.
- [ ] T053 Delete `src/FundingPlatform.Domain/Interfaces/IFileStorageService.cs`, `src/FundingPlatform.Infrastructure/FileStorage/LocalFileStorageService.cs`, and `src/FundingPlatform.Infrastructure/Storage/Legacy/FileStorageServiceFacade.cs`. Remove their DI registrations. Update any using directives that referenced them.
- [ ] T054 Verify SC-003: run `grep -rn 'FileStream\|File\.OpenRead\|File\.OpenWrite' src/ --include='*.cs'` and confirm zero matches outside `LocalFilesystemObjectStorage` and tests. Document the result in `specs/014-azure-blob-storage/REVIEW-CODE.md` (will be appended by the review-code stage).
- [ ] T055 [P] Run the streaming-memory benchmark (custom test harness in `tests/FundingPlatform.Tests.Integration/Storage/StreamingMemoryTests.cs`) for a 100 MiB upload + download against Azurite; assert peak managed memory ≤ 2 × `StreamingThresholdBytes`. Confirms SC-006.
- [ ] T056 [P] Update `CLAUDE.md` "Configuration knobs" table with the new `Storage:*` keys (`Storage:Provider`, `Storage:Categories:{name}:MaxSizeBytes`, `Storage:Categories:{name}:UrlExpirySeconds`, `Storage:Categories:{name}:RetentionPolicy`) and remove or mark deprecated the existing `FileStorage:Path` entry. Also document in the operator runbook that `LocalFilesystem` provider does not provide encryption-at-rest (FR-026 — host responsibility) and that `signed-funding-agreements` is the legal-hold candidate (FR-023).
- [ ] T057 [P] Update `specs/014-azure-blob-storage/quickstart.md` § 7 troubleshooting if real-world startup yielded any new failure modes during T020/T022 implementation.
- [ ] T058 Run `.specify/scripts/bash/update-agent-context.sh claude` again to capture any tech additions surfaced during implementation (already once; idempotent re-run keeps it current).
- [ ] T059 Personally execute the full E2E suite (`dotnet test tests/FundingPlatform.Tests.E2E`) and the integration suite; confirm green and record the run timestamp in `REVIEW-CODE.md` (delivery bar from CLAUDE.md / memory).

**Checkpoint**: legacy interface gone, every call site migrated, full suite green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: independent — must finish before Phase 2.
- **Foundational (Phase 2)**: blocks all user-story phases. T011 depends on T006–T010; T013 depends on T011, T012; T014 depends on T013; T015–T016 are dacpac changes parallel to the .NET work.
- **US2 (Phase 3)**: depends on Phase 2 (especially T013, T014).
- **US1 (Phase 4)**: depends on Phase 2 + T020/T021 from US2 (it runs the same impls against Azure).
- **US3 (Phase 5)**: depends on Phase 2 + T020/T021/T023.
- **US4 (Phase 6)**: depends on US2 (for `IObjectStorage` impls) + T015 (for `BlobKey`).
- **US5 (Phase 7)**: depends on T010 (options) and T028 (controller migration baseline).
- **Polish (Phase 8)**: depends on all stories.

### Within Each Story

- Tests and implementation interleave per task. The constitution requires every story to ship with E2E coverage; partial completion of a story without its tests is not "done".
- Models / value objects before services; services before controllers/wiring.

### Parallel Opportunities

- T003–T005 (project skeleton + placeholders): all parallel after T001/T002.
- T006–T010 (foundation types): all parallel.
- T015–T016 (dacpac) are parallel to T006–T013.
- T017–T019 (foundation unit tests): parallel after their respective targets land.
- T025–T027 (US2 integration tests): parallel.
- T032–T034 (US1 tests): parallel.
- T037–T039 (US3 tests): parallel.
- T044–T046 (US4 tests): parallel.
- T050–T051 (US5 tests): parallel.
- T055–T058 (polish): parallel.

---

## Parallel Example: Phase 2 Foundation

```bash
# After T001/T002 land, run the foundation types and dacpac edits concurrently:
T006 (FileCategory.cs)
T007 (ObjectKey.cs)
T008 (StoredObject.cs)
T009 (StorageHandle.cs)
T010 (StorageOptions.cs)
T015 (dacpac BlobKey columns)
T016 (post-deploy backfill script)

# Then sequentially:
T011 (IObjectStorage + exceptions, depends on T006–T010)
T012 (Diagnostics, depends on T011)
T013 (DI registration, depends on T011, T012)
T014 (AppHost wiring, depends on T013)
```

---

## Implementation Strategy

### MVP First — US2 + US1 + US3 together

The constitution treats integration-test-with-real-backend as a delivery requirement, so the natural MVP slice is:
1. Phase 1 + Phase 2 (Setup + Foundation).
2. US2 (local-dev Azurite) — gives a runnable environment.
3. US1 (production AzureBlob via the same impl) — confirms parity.
4. US3 (hermetic test fixture) — gives the team back a green E2E run.

Stop here, validate, deploy if ready. US4 (migration) and US5 (oversize) are P2 and can be added incrementally without breaking US1–US3.

### Incremental Delivery

After MVP, US4 lands the migration tool (separately runnable, low risk to the running platform) and US5 enforces oversize rejection at the controller boundary. Polish (T052–T059) cleans up the facade and confirms SC-003 / SC-006.

### Parallel Team Strategy

A two-developer team after Phase 2:
- Dev A: US2 → US1 (storage impls + retrofit signed-PDF flow).
- Dev B: US3 (test fixture) in parallel with US2.
- Either dev: US4 + US5 once US1 is stable.

---

## Notes

- [P] markers are conservative — same-file edits inside the same controller are NOT parallel.
- The facade (T024) is a deliberate temporary; T053 deletes it.
- Every commit should leave the build green; per CLAUDE.md "commit and push at every phase checkpoint without prompting".
- Before flipping production from `LocalFilesystem` to `AzureBlob`, the migration manifest from US4 MUST report 100% success.
