# Feature Specification: Azure Blob Storage with Environment-Driven Provider Selection

**Feature Branch**: `014-azure-blob-storage`
**Created**: 2026-05-01
**Status**: Draft
**Input**: User description: "Introduce Azure cloud storage for file upload/download in this .NET Aspire platform, replacing the current local-filesystem-only implementation. The platform must use Azure Blob Storage in production while keeping local development simple and productive."

## Clarifications

### Session 2026-05-01

- Q: What is the canonical object key format the platform writes? → A: `{category}/{owner-segment}/{entity-id}/{deterministic-suffix}.{ext}`, where `owner-segment` is the applicant or admin scope (`applicants/{applicantId}` for applicant-owned files, `admin` for global admin uploads), `entity-id` is the domain id (e.g., funding-application id, signed-agreement id, supplier-import batch id), and `deterministic-suffix` is the owning entity's GUID, persisted on the row when the file is uploaded.
- Q: Retry budget for transient cloud failures? → A: Use the storage SDK's default exponential-backoff retry policy with a hard cap of 3 retries and a total budget of 30 seconds per operation. After exhaustion, the abstraction surfaces a non-retryable error and the caller decides user-facing messaging.
- Q: Default retention policy for each container? → A: No platform-enforced retention policy. The abstraction exposes a configuration seam (FR-023) but ships with the seam set to "no policy" for every container. Operations team sets retention via Azure portal or infrastructure-as-code outside this feature; signed funding agreements are explicitly flagged as candidates for legal-hold-style policies in the operator runbook.
- Q: Where is connection-string authentication permitted? → A: Connection strings are permitted only in local development (Azurite) and lower environments (dev / staging) via Aspire connection-string references. Production deployment templates MUST use managed identity exclusively; a `Storage:Provider=AzureBlob` startup with a connection string in `Production` environment MUST log a warning and SHOULD be rejected by deployment gating.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Production deployment uses managed cloud storage (Priority: P1)

A platform operator deploys the funding platform to Azure. Every file the platform receives or serves — signed funding agreements (PDFs), supplier catalog imports (CSV/XLSX), application attachments — lives in durable, encrypted, access-controlled cloud object storage rather than on a single VM's local disk. Files survive container restarts, scale beyond a single node, and are recoverable.

**Why this priority**: This is the entire reason the work exists. Without it, the platform cannot run safely in production. Local disk is single-host, ephemeral on container restart, and not auditable as a regulated funding platform requires.

**Independent Test**: Deploy the AppHost configured with the Azure provider against an Azure Blob Storage account. Run the existing upload-a-signed-PDF flow end-to-end and verify the file is persisted as a blob, retrievable across an AppHost restart, and inaccessible to unauthenticated callers.

**Acceptance Scenarios**:

1. **Given** the platform is configured for the cloud provider and an applicant has just signed a funding agreement, **When** the signed PDF upload completes, **Then** the file is persisted as a blob in the funding-agreements container under a deterministic key, and a subsequent download by the same applicant succeeds after the AppHost is restarted.
2. **Given** an admin requests a signed agreement they own, **When** the download endpoint runs authorization, **Then** access is granted by the application before any storage call is made; an unauthenticated request to the same blob path returns a 401/403 from the application without ever exposing a blob URL.
3. **Given** the cloud provider is configured with managed identity, **When** the platform starts, **Then** no connection-string secret is read from configuration and storage operations succeed against the configured account.

---

### User Story 2 - Local developer runs a production-equivalent stack with one command (Priority: P1)

A developer clones the repo and runs `dotnet run --project src/FundingPlatform.AppHost`. The AppHost starts an Azurite emulator container alongside SQL Server, wires the platform's storage client to it automatically, and the developer can immediately upload, download, and delete files using the same code paths that run in production. No connection strings, no Azure account, no manual `az` setup.

**Why this priority**: Local-prod parity is what makes the abstraction trustworthy. If local dev uses a different backend, bugs leak into production. This is also the daily developer-experience surface — if it is painful, the team will route around it.

**Independent Test**: On a clean checkout, `dotnet run --project src/FundingPlatform.AppHost` starts cleanly; the Aspire dashboard shows an Azurite resource healthy; uploading a signed PDF through the UI succeeds; the blob can be inspected via Azure Storage Explorer or the Azurite REST endpoint.

**Acceptance Scenarios**:

1. **Given** a fresh clone with Docker available and no Azure credentials configured, **When** the developer launches AppHost, **Then** Azurite starts as an Aspire-managed container and the storage abstraction routes all operations to it.
2. **Given** the Azurite container is running, **When** the developer uploads, downloads, and deletes a file via the platform, **Then** all three operations succeed using the same code path that runs against Azure Blob Storage in production.
3. **Given** a developer explicitly opts into the local filesystem provider via configuration, **When** the AppHost starts, **Then** Azurite is not started and the platform reads/writes files under a configured local directory.

---

### User Story 3 - Automated tests run hermetically, in-CI, without Azure credentials (Priority: P1)

The integration test fixture and the E2E (Playwright) fixture each boot an isolated stack that exercises real storage operations against Azurite. CI runs the full suite with no Azure account, no shared secret, and no flakiness from a remote endpoint.

**Why this priority**: The team's delivery bar is a personally-executed green E2E run. If storage tests need real Azure, the bar becomes unenforceable in PR CI and locally — that breaks the existing delivery contract.

**Independent Test**: Run `dotnet test tests/FundingPlatform.Tests.Integration` and `dotnet test tests/FundingPlatform.Tests.E2E` on a machine with no Azure credentials. Both must pass, with storage operations actually exercising the abstraction (not stubbed) against an Azurite container managed by the test fixture.

**Acceptance Scenarios**:

1. **Given** AspireFixture is invoked with `EphemeralStorage=true`, **When** the fixture starts, **Then** an Azurite container is provisioned with a clean state and is awaited until ready before tests run.
2. **Given** the integration suite runs, **When** a test uploads a fixture PDF and the same test downloads it, **Then** the download stream matches the uploaded bytes exactly.
3. **Given** Azurite cannot start in a constrained CI environment, **When** the test fixture is configured to fall back, **Then** it uses the local filesystem provider with a temp directory and the suite still passes (with a warning logged).

---

### User Story 5 - Oversized uploads are rejected before they touch storage (Priority: P2)

A user attempts to upload a file larger than the configured cap for that file category. The platform rejects the upload with a clear, localized error before streaming any bytes to the storage backend, regardless of provider.

**Why this priority**: Existing behavior (`SignedUpload:MaxSizeBytes` = 20 MiB) must be preserved. Lower than P1 because the cap is already enforced at the controller for signed PDFs; the spec's requirement is to keep that contract uniform across all categories and not regress when the backend changes.

**Acceptance Scenarios**:

1. **Given** a category's cap is set to 20 MiB, **When** the user submits a 25 MiB file, **Then** the platform returns a localized "file too large" error, no blob is created, and no partial bytes are written.
2. **Given** a category's cap is unset, **When** any upload occurs, **Then** the platform applies a documented default cap rather than allowing unbounded uploads.

---

### Edge Cases

- **Missing blob on download**: If a download is requested for a key that does not exist in the configured backend (e.g., a stale URL, a deleted file), the platform returns a clear 404-equivalent error with a stable error code, logs the operation outcome with the requested key (sanitized), and does not leak whether the requester was authorized to see it had it existed.
- **Connection failure / transient outage**: Storage operations against the cloud backend retry transient failures using exponential backoff. The retry budget is **at most 3 retries with a 30-second total budget per operation** (the storage SDK's default policy, capped if the SDK default is more permissive). After the budget is exhausted, the abstraction surfaces a non-retryable error; the application decides between user-facing retry messaging and a hard failure.
- **Streaming of large files**: Upload and download of any file above a configured streaming threshold MUST avoid buffering the entire payload in memory, regardless of provider.
- **Path collisions**: Two different logical entities MUST NOT be able to resolve to the same blob key. The naming convention guarantees uniqueness even when human-supplied filenames collide.
- **Local-mode parity gaps**: Any operation that the cloud provider supports (e.g., conditional create, time-limited URL issuance) but the local filesystem provider cannot, MUST surface a documented "not supported in local provider" error rather than silently no-op'ing.
- **Cleanup after failed uploads**: If an upload fails partway, no orphan blob remains under the key. The application either commits the blob atomically or removes the partial.
- **Sensitive content in logs**: Storage operations MUST log keys, sizes, durations, and outcomes — never blob contents or signed URLs that grant access.

## Requirements *(mandatory)*

### Functional Requirements

#### Storage abstraction
- **FR-001**: The platform MUST expose a single storage abstraction that supports: stream upload, stream download, existence check, delete, and resolution of a serving handle (either a backend stream or a time-limited URL) suitable for sending the blob to a client.
- **FR-002**: All file IO in the platform (signed funding agreement uploads/downloads, supplier catalog imports, application attachments, generated PDF persistence) MUST go through this abstraction. No call site may construct a `FileStream` or call `File.OpenRead`/`File.OpenWrite` against the platform's storage paths directly.
- **FR-003**: The abstraction MUST be the same shape regardless of backend, so that a consumer of the abstraction needs no knowledge of which provider is configured.

#### Environment-driven provider selection
- **FR-004**: The active storage provider MUST be selected at runtime via configuration (`Storage:Provider` with values `AzureBlob`, `Azurite`, or `LocalFilesystem`). The same code, deployed to different environments, MUST be able to use any provider without recompilation.
- **FR-005**: In production, the default and recommended provider MUST be `AzureBlob`.
- **FR-006**: For local development, the default provider MUST be `Azurite`, started and wired up automatically by the AppHost so a developer running `dotnet run --project src/FundingPlatform.AppHost` gets a working storage stack with no extra setup.
- **FR-007**: `LocalFilesystem` MUST remain available as an explicit opt-in for offline scenarios, configured by setting `Storage:Provider=LocalFilesystem` and `Storage:LocalFilesystem:RootPath`.
- **FR-008**: Automated tests (Integration and E2E) MUST default to `Azurite` provisioned by the test fixture. Falling back to `LocalFilesystem` is permitted ONLY when an explicit opt-in is configured (e.g., `Storage:TestFallback:AllowFilesystem=true`); the test fixture MUST log a warning when fallback is used and MUST NOT silently switch providers if Azurite fails to start.
- **FR-009**: CI pipelines MUST run the same test stack as local automated tests, with Azurite provisioned in the pipeline.

#### Configuration & credentials
- **FR-010**: No connection strings or storage credentials may be hardcoded in source. Credentials are resolved at runtime through Aspire resource references, environment variables, or managed identity.
- **FR-011**: When the configured provider is `AzureBlob`, the platform MUST prefer managed identity for authentication. A connection-string fallback MUST be supported only in local development and lower environments (`Development`, `Staging`); in `Production`, a connection-string-based configuration MUST log a warning at startup and SHOULD be rejected by deployment gating. Production deployment templates ship with managed-identity-only configuration.
- **FR-012**: Misconfiguration (e.g., `Storage:Provider=AzureBlob` without a usable account reference) MUST fail fast at AppHost startup with a clear error, rather than failing on the first user upload.

#### Containers, naming, paths
- **FR-013**: The platform MUST map each file category to its own container (or container-equivalent prefix). At minimum:
  - Signed funding agreements → `signed-funding-agreements`
  - Supplier catalog imports → `supplier-catalog-imports`
  - Application attachments → `application-attachments`
  - Generated artifact PDFs (e.g., system-generated agreements before signature) → `generated-artifacts`
- **FR-014**: Object keys MUST follow the canonical format `{category}/{owner-segment}/{entity-id}/{deterministic-suffix}.{ext}`:
  - `category` is the container name (FR-013) or, in `LocalFilesystem` mode, the top-level directory under `Storage:LocalFilesystem:RootPath`.
  - `owner-segment` is `applicants/{applicantId}` for applicant-owned files and `admin` for global admin uploads (e.g., supplier catalog imports). Future tenant scopes append to this segment without breaking older keys.
  - `entity-id` is the platform's domain id for the owning aggregate (e.g., funding-application id, signed-agreement id, supplier-import batch id).
  - `deterministic-suffix` is the owning entity's GUID, persisted on the row when the file is uploaded.
  - `ext` is the original file extension (lower-cased; `.pdf`, `.csv`, `.xlsx`, etc.).
  - Keys MUST be reconstructable from the platform's domain identifiers without consulting a separate index. Two distinct domain entities MUST NOT resolve to the same key.
- **FR-016**: Containers MUST be created by the platform on demand if they do not exist, and MUST be private (no anonymous public access).

#### Download / serving model
- **FR-017**: For each file category the spec defines whether downloads are served by backend streaming through the application or by issuing a time-limited URL. The default and the rationale MUST be documented per category. Initial defaults:
  - Signed funding agreements → backend streaming (small files, strict authorization, audit trail required).
  - Supplier catalog imports → backend streaming (admin-only, small).
  - Application attachments → backend streaming (mixed sensitivity, simpler to audit).
  - Generated artifact PDFs → backend streaming.
  - Any future "large media" category MAY use time-limited URLs; introducing such a category requires updating this section.
- **FR-018**: Authorization decisions MUST happen in the application layer before a storage handle (stream or URL) is produced. The storage abstraction MUST NOT make authorization decisions.
- **FR-019**: When a time-limited URL is used, its expiry MUST be configurable per category and MUST default to no more than 15 minutes.

#### Large file handling
- **FR-020**: Upload and download paths MUST stream rather than buffer in memory for any payload above a configured threshold (default 1 MiB). Memory usage during a single upload or download of a 100 MiB file MUST remain bounded.
- **FR-021**: Each category MUST have a configurable upload size cap with the following explicit defaults:
  - Signed funding agreements: 20 MiB (matches existing `SignedUpload:MaxSizeBytes`).
  - Supplier catalog imports: 50 MiB (admin-only bulk import; CSV/XLSX may be large).
  - Application attachments: 20 MiB.
  - Generated artifact PDFs: 20 MiB (set by the platform itself; the cap protects against runaway PDF generation).
  - Each cap MUST be overridable via configuration (`Storage:Categories:{name}:MaxSizeBytes`).
- **FR-022**: Oversize uploads MUST be rejected before any byte is persisted to the backend.

#### Lifecycle, observability, security
- **FR-023**: The abstraction MUST expose a per-container lifecycle/retention seam (a configuration surface that can carry retention values or `"no policy"`). The seam ships with `"no policy"` for every container; the operations team configures retention values via Azure portal or infrastructure-as-code outside this feature. The operator runbook MUST flag `signed-funding-agreements` as a candidate for legal-hold-style policies.
- **FR-025**: Every storage operation (upload, download, exists, delete, URL issuance) MUST log: operation type, container, key, size (where known), duration, outcome (success/error code), and provider name. Logs MUST NOT include blob contents or full signed URLs.
- **FR-026**: At-rest encryption is assumed via Azure defaults for `AzureBlob` and `Azurite`. The `LocalFilesystem` provider MUST document that encryption-at-rest is the host's responsibility and MUST NOT be the production default (FR-005 enforces this).
- **FR-027**: All containers used by the platform MUST disallow anonymous public access. The platform MUST refuse to start (or surface a clear health-check failure) if it detects that a configured container has anonymous access enabled.

#### Migration / rollout
- **FR-028**: Toggling between providers MUST require only configuration changes — no source changes and no container rebuild.
- **FR-029**: After this feature ships, no production code path may write a new file to the local filesystem outside the `LocalFilesystem` provider's own implementation.

### Key Entities *(include if feature involves data)*

- **Stored Object**: A binary payload identified by `(container, key)`. Carries metadata: size in bytes, content type, created-at, the logical owner (tenant/applicant/feature scope), and the provider that produced it. Maps 1:1 to a blob in `AzureBlob`/`Azurite` and to a file under a deterministic relative path in `LocalFilesystem`.
- **File Category**: A logical bucket (e.g., signed funding agreements, supplier catalog imports, application attachments, generated artifacts) that determines the container, the upload size cap, the serving model (stream vs. URL), and the retention seam.
- **Storage Provider Configuration**: The active provider name, its credentials/resource reference, the local-mode root path (when applicable), and per-category overrides (caps, serving model, retention seam values).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new developer can clone the repo and successfully upload, download, and delete a file through the platform within 10 minutes of `dotnet run --project src/FundingPlatform.AppHost`, with no Azure credentials configured.
- **SC-002**: After deployment to Azure with the `AzureBlob` provider, no file the platform writes is persisted to the host's local disk under any path other than transient OS temp directories.
- **SC-003**: 100% of existing call sites that touch `File.OpenRead`/`File.OpenWrite`/`FileStream` against platform storage paths are migrated to use the abstraction (verified by repository search returning zero matches outside the `LocalFilesystem` provider implementation and tests).
- **SC-004**: Switching the active provider in any environment is achievable by a single configuration change and an AppHost restart, with no source modifications and zero impact on downstream code paths that consume the abstraction.
- **SC-005**: The full Integration and E2E test suites pass on a machine with no Azure credentials, exercising real storage operations against Azurite (not stubs/mocks).
- **SC-006**: Memory usage during a 100 MiB upload or download stays within a small constant multiple (≤ 4×) of the configured streaming threshold from FR-020, and never grows linearly with payload size.
- **SC-008**: The Aspire dashboard for a local AppHost run shows the Azurite resource healthy and reachable within 30 seconds of AppHost start.
- **SC-009**: An accidental misconfiguration (provider set to `AzureBlob` without a usable account reference) causes AppHost startup to fail with a single clear error, never silently degrades to a different provider.

## Assumptions

- The platform's deployment target is a single Azure Storage account per environment, with separate storage accounts for production vs. lower environments rather than reusing a single account across stages.
- Managed identity is available in production. The platform's Azure deployment templates (out of scope for this spec but assumed by the operations team) will assign the necessary `Storage Blob Data Contributor` role to the platform's identity on the target account.
- The platform is not yet in production; there is no legacy on-disk corpus to migrate. The storage abstraction is the day-one design and every row is created with a populated `BlobKey` from the moment the feature ships.
- Docker is available on developer machines and CI agents (Aspire already requires it for the SQL Server container).
- Files handled by the platform are bounded by the existing 20 MiB default per category. The streaming requirement is in place to keep the abstraction safe if the cap is raised later, not because today's traffic includes 100 MiB files.
- The `FundingAgreement:CurrencyIsoCode`, `FundingAgreement:LocaleCode`, and similar configuration keys are independent of storage and are unaffected.
- The constitution and existing CLAUDE.md guidance apply: integration tests must exercise a real backend (Azurite), no mocks; vendored dependencies are preferred and any new managed dependency must be approved.
- Tabler.io UI behavior, language packs, PDF generation pipeline, and admin reports are unaffected except that they consume storage through the new abstraction.
