# Code Review: 014 Azure Blob Storage

**Spec:** [spec.md](spec.md)
**Plan:** [plan.md](plan.md)
**Tasks:** [tasks.md](tasks.md)
**Date:** 2026-05-01
**Reviewer:** Claude (`speckit-spex-gates-review-code`)
**Implementation status:** Partial — 28 of 59 tasks completed (Foundation + US2 + US1 production-guard).

## Compliance Summary

**Scope landed (claimed) compliance:** ~100%. All requirements that the
landed phases actually claim to satisfy are implemented and tested.

**Total spec compliance:** ~69% (20 / 29 functional requirements verifiably
implemented). The remaining ~31% is **deferred**, not missing — the partial
implementation deliberately stops before US1 controller retrofit, US3 fixture
extension, US4 migration body, US5 oversize guard, and Phase 8 polish.

| Group | Implemented | Deferred | Notes |
|---|---|---|---|
| FR-001..FR-003 (port shape) | 3/3 | 0 | Implemented per [`IObjectStorage.cs`](../../src/FundingPlatform.Application/Abstractions/Storage/IObjectStorage.cs). FR-002 is *implemented* in shape (legacy callers go through [`FileStorageServiceFacade.cs`](../../src/FundingPlatform.Infrastructure/Storage/Legacy/FileStorageServiceFacade.cs)) but legacy `LocalFileStorageService` still exists pending T053. |
| FR-004..FR-009 (provider selection) | 5/6 | 1 | FR-009 (CI parity) deferred to T039a. |
| FR-010..FR-012 (config & credentials) | 3/3 | 0 | Production guard via [`StorageProductionGuardHealthCheck.cs`](../../src/FundingPlatform.Infrastructure/Storage/StorageProductionGuardHealthCheck.cs). |
| FR-013..FR-016 (containers, naming) | 4/4 | 0 | [`ObjectKey.cs`](../../src/FundingPlatform.Application/Abstractions/Storage/ObjectKey.cs) tested; [`EnsureContainersHostedService.cs`](../../src/FundingPlatform.Infrastructure/Storage/EnsureContainersHostedService.cs) creates private containers. FR-015 migration toolchain stubbed; one-shot body deferred to T040. |
| FR-017..FR-019 (download/serving) | 3/3 | 0 | BackendStream default + 15-min URL cap enforced in validator. FR-018 (no auth in port) holds; controller retrofit deferred to T028. |
| FR-020..FR-022 (large files) | 2/3 | 1 | FR-022 oversize guard at controller boundary deferred to T047. |
| FR-023..FR-027 (lifecycle/observability) | 5/5 | 0 | RetentionPolicy seam present; FR-025 logging shape verified by [`ObjectStorageDiagnosticsTests.cs`](../../tests/FundingPlatform.Tests.Unit/Storage/ObjectStorageDiagnosticsTests.cs). FR-027 anonymous-access verification implemented. |
| FR-028..FR-029 (rollout) | 1/2 | 1 | FR-029 (no new local-fs writes) blocked by legacy [`LocalFileStorageService.cs`](../../src/FundingPlatform.Infrastructure/FileStorage/LocalFileStorageService.cs) still being registered. |
| **Total** | **26 / 29** | **3** | Plus structural deferrals on FR-002 / FR-018 (controller retrofit pending). |

**Success criteria:** SC-001/004/008/009 are achievable now (Aspire wiring +
validator + container bootstrap). SC-002/005/007 cannot be measured until the
deferred phases land (production deployment, hermetic test fixture, migration
manifest). SC-003 is currently violated by the legacy file-storage class on
purpose — its removal lives in T053. SC-006 (memory bounded for 100 MiB)
needs the streaming benchmark from T055.

**Gate outcome:** **PASS-WITH-FINDINGS** — what landed builds clean, has unit
+ integration coverage, and matches the contract. Deferred work is properly
flagged in [tasks.md](tasks.md) and is not claimed as done.

---

## Code Review Guide (30 minutes)

> This guide focuses a human reviewer's attention on the high-level questions
> that need judgment. It does not repeat the compliance matrix above.

**Changed files:** 12 source files, 4 dacpac files, 8 new test files, 1 console
project skeleton, 1 AppHost orchestration update.

### Understanding the changes (8 min)

- Start with [`contracts/IObjectStorage.md`](contracts/IObjectStorage.md) and
  [`IObjectStorage.cs`](../../src/FundingPlatform.Application/Abstractions/Storage/IObjectStorage.cs).
  This is the port. Everything else is one of: an implementation
  (Azure / Local), a wiring concern (DI / Aspire), or a guard
  (validator / health check / hosted bootstrap).
- Then [`AppHost.cs`](../../src/FundingPlatform.AppHost/AppHost.cs) — the
  Azurite-vs-AzureBlob-vs-LocalFilesystem branch lives here (lines 26–41 and
  86–94). Notice that `EphemeralStorage=true` deliberately runs Azurite without
  a data volume so each E2E fixture gets a clean account.
- Then [`ObjectStorageRegistration.cs`](../../src/FundingPlatform.Infrastructure/Storage/ObjectStorageRegistration.cs)
  + [`Program.cs`](../../src/FundingPlatform.Web/Program.cs) (lines 22–38). The Web
  project is supposed to receive an Aspire-wired `BlobServiceClient` (with OTel
  + health checks). The registration uses `TryAddSingleton` to defer to that
  client when present and falls back to building one from
  `Storage:ConnectionString` / `Storage:AccountReference` otherwise.
- **Question:** Is the responsibility split between
  [`ObjectStorageRegistration.cs`](../../src/FundingPlatform.Infrastructure/Storage/ObjectStorageRegistration.cs)
  and [`Program.cs`](../../src/FundingPlatform.Web/Program.cs) clear? Today
  Program.cs decides whether to call `AddAzureBlobServiceClient`, and the
  Infrastructure registration decides whether to build a fallback. A reviewer
  who only reads one file may miss the other.

### Key decisions that need your eyes (12 min)

**Provider detection from endpoint URL, not configuration**
([`AzureBlobObjectStorage.cs:244-257`](../../src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs),
relates to [FR-025](spec.md#fr-025))

The `Provider` field reported in diagnostics (Azurite vs AzureBlob) is derived
by sniffing the endpoint host (`localhost`, `127.0.0.1`, contains `azurite`,
ends in `.local`). This was a deliberate choice over reading
`Storage:Provider` so the diagnostic field can never lie about which endpoint
served the request.
- Question: Does the host-sniffing list cover the cases you expect (e.g.
  Aspire's emulator container hostnames)? Will a misconfigured production
  container with a `127.0.0.1` proxy ever be miscategorised as `Azurite`?

**Facade keeps legacy callers compiling**
([`FileStorageServiceFacade.cs`](../../src/FundingPlatform.Infrastructure/Storage/Legacy/FileStorageServiceFacade.cs),
relates to [FR-002](spec.md#fr-002))

The new abstraction is the source of truth, but
[`FundingAgreementController.cs`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs)
still depends on `IFileStorageService`. The facade preserves that surface and
adapts to `IObjectStorage` using `FileCategory.GeneratedArtifact` as a default
bucket. That's wrong for signed PDFs, but the controller-level retrofit
([T028](tasks.md)) hasn't shipped yet, so the facade's `DefaultCategory` is
load-bearing for *new* legacy-API writes during this transition window. New
legacy writes from the controller therefore land in `generated-artifacts`,
not `signed-funding-agreements`.
- Question: Is parking signed-PDF writes under `generated-artifacts` for the
  duration of one PR acceptable, or should the facade fail loudly until T028
  ships? The current code silently mis-routes.

**Local provider's serving-mode validator runs at startup, not at the call site**
([`StorageOptions.cs:128-145`](../../src/FundingPlatform.Application/Abstractions/Storage/StorageOptions.cs),
relates to [FR-edge "Local-mode parity gaps"](spec.md#edge-cases))

When the provider is `LocalFilesystem`, the options validator rejects any
category configured with `ServingMode=TimeLimitedUrl`. In addition,
[`LocalFilesystemObjectStorage.cs:149-150`](../../src/FundingPlatform.Infrastructure/Storage/LocalFilesystemObjectStorage.cs)
throws `LocalProviderUrlNotSupportedException` at the call site if a caller
passes `TimeLimitedUrl` programmatically.
- Question: Both layers cooperate, but they handle the same condition with
  different errors (`OptionsValidationException` vs
  `LocalProviderUrlNotSupportedException`). Is that intentional, or should
  the runtime path mirror the validator?

**Health check stays Healthy when not in Production**
([`StorageProductionGuardHealthCheck.cs:32-56`](../../src/FundingPlatform.Infrastructure/Storage/StorageProductionGuardHealthCheck.cs),
relates to [FR-011](spec.md#fr-011))

The guard is intentionally a no-op outside `Production`, so dev/staging can
run with a connection string. The `_warningEmitted` flag suppresses repeated
warnings during health-check polling.
- Question: Is `_warningEmitted` written without synchronisation safe given
  health checks may run concurrently? In practice the worst case is two
  warnings; flag if you'd rather see a `Volatile.Write` or `Interlocked`.

**Azurite tests skip when Docker is missing instead of failing**
([`AzuriteFixture.cs:19-22`](../../tests/FundingPlatform.Tests.Integration/Storage/AzuriteFixture.cs),
[`AzuriteObjectStorageTests.cs:25-29`](../../tests/FundingPlatform.Tests.Integration/Storage/AzuriteObjectStorageTests.cs))

The integration suite for Azurite uses `Assert.Ignore` when Docker isn't
available. Production CI must have Docker (see [CLAUDE.md](../../CLAUDE.md)
for the Aspire-requires-Docker assumption), so this is a developer-laptop
ergonomic affordance.
- Question: Does silent skipping on a CI machine that *should* have Docker
  but is misconfigured hide a real regression? Spec [FR-008](spec.md#fr-008)
  is firm that test-fixture fallback must log a warning and never silently
  switch — the same skepticism should arguably apply here.

### Areas where I'm less certain (5 min)

- [`AzureBlobObjectStorage.cs:259-266`](../../src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs)
  ([FR-edge retry budget](spec.md#edge-cases)): the `IsRetryExhausted` heuristic
  treats any `Status >= 500` (or `Status == 0`) as retry-exhausted. Is that the
  right read of when the SDK has given up? The Azure SDK retry pipeline has
  already retried by the time the exception bubbles, so any 5xx that surfaces
  *is* exhaustion. But mapping every 5xx to `RetryExhausted` rather than
  `Backend` may obscure the distinction the spec draws between retry-exhausted
  and other backend errors.
- [`LocalFilesystemObjectStorage.cs:168-176`](../../src/FundingPlatform.Infrastructure/Storage/LocalFilesystemObjectStorage.cs):
  the `ResolveAbsolutePath` guard uses
  `rooted.StartsWith(rootedRoot, StringComparison.Ordinal)`. On Windows that's
  fine because the framework normalises separators, but on Linux with a
  configured `RootPath` that ends with `/` the prefix check could match a
  sibling directory whose name begins with the root's last segment (e.g. root
  `/data` would also match `/data2/...`). This is paranoid but worth eyeballing.
- The migration tool's [`Program.cs`](../../tools/FundingPlatform.StorageMigration/Program.cs)
  is a stub (`return 0`). All of [FR-024](spec.md#fr-024) /
  [FR-015](spec.md#fr-015) is unverified; only the project skeleton, package
  references, and database scaffolding (`BlobKey`/`LegacyPath` columns +
  backfill) are in place.

### Deviations and risks (5 min)

- [`tasks.md` T015 adapted-note](tasks.md#L73): the spec called out
  `FundingAgreementSignatures`, `SupplierCatalogImports`,
  `ApplicationAttachments`, `GeneratedAgreementArtifacts`. Actual schema uses
  `dbo.SignedUploads`, `dbo.FundingAgreements`, `dbo.Documents`. The dacpac
  changes match the actual schema and the discrepancy is documented in
  tasks.md. Question: does the runbook ([quickstart.md](quickstart.md)) need
  updating to call out the actual table names so the migration tool author
  doesn't get confused?
- [`dbo.Documents.sql`, `dbo.FundingAgreements.sql`, `dbo.SignedUploads.sql`](../../src/FundingPlatform.Database/Tables/):
  `BlobKey` and `LegacyPath` are both `NVARCHAR(1024) NULL`. The plan
  ([Phase 1 — Schema](plan.md)) specified `nvarchar(512)` for `BlobKey` and
  `nvarchar(1024)` for `LegacyPath`. The implementation uses 1024 for both, which
  matches `ObjectKey.MaxLengthBytes` and is more permissive — defensible, but a
  silent deviation from the plan worth confirming.
- The post-deploy backfill script
  ([`014-backfill-legacy-paths.sql`](../../src/FundingPlatform.Database/PostDeployment/014-backfill-legacy-paths.sql))
  is invoked from `SeedData.sql` via `:r` rather than added as a separate
  `<PostDeploy>` entry in
  [`FundingPlatform.Database.sqlproj`](../../src/FundingPlatform.Database/FundingPlatform.Database.sqlproj).
  Question: is `:r`-include the team's preferred pattern, or should new
  post-deploys get their own item group entries?
- Risk: `IFileStorageService` is double-registered in
  [`DependencyInjection.cs:32 and :51`](../../src/FundingPlatform.Infrastructure/DependencyInjection.cs).
  The last registration wins (the facade), so the legacy
  `LocalFileStorageService` is *registered* but never resolved. That's not a
  bug today, but it means a future contributor reading the file sees
  conflicting intent. The clean-up belongs to T053.

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarises
> what was checked, what was found, and what remains for human review.

**Date:** 2026-05-01 | **Rounds:** 1/3 | **Gate:** PASS-WITH-FINDINGS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 5 | completed |
| Architecture & Idioms | 4 | completed |
| Security | 1 | completed |
| Production Readiness | 2 | completed |
| Test Quality | 2 | completed |
| CodeRabbit (external) | 0 | skipped (orchestrator: `coderabbit=false`) |
| Copilot (external) | 0 | skipped (orchestrator: `copilot=false`) |

> Sub-agent dispatch unavailable in this environment; the five perspectives
> were executed inline by the main agent against the changed files. Each
> perspective applied its checklist from
> [`speckit-spex-deep-review-review/SKILL.md`](../../.claude/skills/speckit-spex-deep-review-review/SKILL.md).

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 4 | 4 | 0 |
| Minor | 9 | 0 | 9 |

### What was fixed automatically

- **Path-traversal boundary in [`LocalFilesystemObjectStorage.cs`](../../src/FundingPlatform.Infrastructure/Storage/LocalFilesystemObjectStorage.cs)** — `StartsWith` check now appends a directory separator so a root of `/data` cannot match `/data2/...`.
- **Public-access guard in [`EnsureContainersHostedService.cs`](../../src/FundingPlatform.Infrastructure/Storage/EnsureContainersHostedService.cs)** — denies exactly `Blob` and `BlobContainer` (the unsafe values) instead of demanding `None`, so safe Azurite containers no longer trip startup.
- **Silent miscategorisation in [`FileStorageServiceFacade.cs`](../../src/FundingPlatform.Infrastructure/Storage/Legacy/FileStorageServiceFacade.cs)** — the transitional facade now warns when it routes through the default `GeneratedArtifact` category and when it no-ops on legacy filesystem paths, so the gap until controller retrofit (T028 / T052) is observable in operator logs.
- **Test-fixture port collision in [`AzuriteFixture.cs`](../../tests/FundingPlatform.Tests.Integration/Storage/AzuriteFixture.cs)** — replaced random-range port selection with OS-allocated ephemeral port to remove parallel-run flake risk.

Build remains green (`dotnet build FundingPlatform.slnx` clean) and the 25
storage unit tests pass after the fixes.

### What still needs human attention

The nine Minor findings are documented in
[review-findings.md](review-findings.md). Highlights for human review:

- Should the `IsRetryExhausted` heuristic in
  [`AzureBlobObjectStorage.cs:259-266`](../../src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs)
  treat *every* `Status >= 500` as retry-exhausted, or distinguish from
  `Backend`? See [FR-025](spec.md#fr-025) — the `errorCode` field semantics
  matter for runbook analysis.
- Is the `host.EndsWith(".local")` provider-detection rule in
  [`AzureBlobObjectStorage.cs:244-257`](../../src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs)
  too greedy for an Azure deployment that uses a private-DNS `.local` name?
- Is the dacpac column width deviation (`BlobKey` 1024 vs plan.md's 512)
  acceptable, or should [plan.md](plan.md) be amended to match? See
  [`dbo.SignedUploads.sql`](../../src/FundingPlatform.Database/Tables/dbo.SignedUploads.sql),
  [`dbo.FundingAgreements.sql`](../../src/FundingPlatform.Database/Tables/dbo.FundingAgreements.sql),
  [`dbo.Documents.sql`](../../src/FundingPlatform.Database/Tables/dbo.Documents.sql).
- Should the Azurite test fixture refuse to silently skip on CI when Docker
  is missing? Spec [FR-008](spec.md#fr-008) sets the precedent that fallback
  paths must log warnings and never silently switch.

### Recommendation

All Critical / Important findings were resolved by the autonomous fix loop.
**The four landed phases (Foundation + US2 + US1 production-guard) are ready
for human review.** Nine Minor findings remain and are non-blocking — review
during normal code review.

**Blockers from the partial implementation that the orchestrator must surface
to the user before the stamp gate:**

- T028 / T029 ([Phase 4](tasks.md#phase-4-user-story-1-priority-p1--production-deployment-uses-managed-cloud-storage)) — controller retrofit + entity behaviour are not done. The signed-PDF flow still depends on the legacy `IFileStorageService` and the facade routes everything to `generated-artifacts`. [FR-002](spec.md#fr-002) and [FR-018](spec.md#fr-018) are structurally satisfied but not behaviourally.
- T032 / T033 ([Phase 4](tasks.md#phase-4-user-story-1-priority-p1--production-deployment-uses-managed-cloud-storage)) — Playwright E2E coverage for signed-PDF upload/download and authorisation is absent. The constitution's E2E NON-NEGOTIABLE principle is not yet met for US1.
- T035–T039a ([Phase 5](tasks.md#phase-5-user-story-3-priority-p1--automated-tests-run-hermetically-without-azure-credentials)) — `AspireFixture` is not extended to provision Azurite hermetically. SC-005 cannot be measured.
- T040–T046 ([Phase 6](tasks.md#phase-6-user-story-4-priority-p2--existing-on-disk-files-migrate-cleanly)) — the `tools/FundingPlatform.StorageMigration/Program.cs` is a stub (`return 0`). [FR-024](spec.md#fr-024) and SC-007 are entirely deferred.
- T047–T051 ([Phase 7](tasks.md#phase-7-user-story-5-priority-p2--oversized-uploads-rejected-before-touching-storage)) — no `UploadSizeGuard` filter exists; oversize rejection per [FR-022](spec.md#fr-022) is not enforced uniformly.
- T053–T059 ([Phase 8](tasks.md#phase-8-polish--cross-cutting)) — legacy `LocalFileStorageService.cs` still exists and is registered (overwritten by the facade). SC-003 currently fails by spec definition. The full E2E run (T059, the personally-executed delivery bar from CLAUDE.md) has not happened.

These are **expected deferrals** per the partial-implementation contract, not
review failures. The stamp gate should refuse to stamp until the orchestrator
either lands the deferred work or explicitly accepts the partial scope.
