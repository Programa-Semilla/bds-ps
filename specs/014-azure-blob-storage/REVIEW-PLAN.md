# Review Guide: Azure Blob Storage with Environment-Driven Provider Selection

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-01

---

## What This Spec Does

The platform currently writes every uploaded or generated file to the local
filesystem of a single host. That is fine for a developer laptop but unsafe for
a regulated funding workflow in production: files vanish on container restart,
do not scale beyond one node, and have no audit/encryption guarantees. This
spec replaces the single `IFileStorageService` with an `IObjectStorage`
abstraction that runs against Azure Blob Storage in production, Azurite (the
local emulator) in dev and tests, and a local-filesystem provider as an
explicit offline opt-in. A one-shot migration tool moves the legacy on-disk
files into the new backend before the production toggle.

**In scope:** the storage port + three implementations, Aspire wiring of an
Azurite emulator, deterministic key format
([FR-014](spec.md#functional-requirements)), per-category caps, streaming
uploads/downloads, oversize rejection, hermetic test fixture against Azurite,
and a one-shot CLI migration with manifest.

**Out of scope:** dual-read providers (explicitly rejected — see
[FR-015](spec.md#functional-requirements)), CDN / large-media SAS workflows,
managed-identity role assignment templates, and platform-level retention
policies (the abstraction exposes a seam set to "no policy" — operations team
configures retention via Azure portal / IaC outside this feature).

## Bigger Picture

This is the first feature that punctures the AppHost-only deployment story:
until now, "production" has been a wishlist item and every spec has assumed
the SQL-Server-in-a-container model. With this spec the platform gains a
second managed resource (Azure Storage), a second authentication chain
(`DefaultAzureCredential`), and the first dependency on an external Azure
quota. Reviewers should ask whether the operations team is actually ready for
this transition and whether the migration tool's one-shot, no-dual-read posture
([FR-015](spec.md#functional-requirements)) leaves an acceptable rollback
window if the cutover fails.

Aspire 13.2's `Aspire.Hosting.Azure.Storage` now provides a first-party Azurite
resource with `RunAsEmulator(...)`, which removes most of the historical pain
of testing against an emulator. The plan leans on this; if the team wanted to
target a different cloud later, the abstraction shape is generic enough but
the Aspire wiring is not.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md § Storage abstraction](spec.md#storage-abstraction) and
[§ Environment-driven provider selection](spec.md#environment-driven-provider-selection)
for the core decision. Then skim
[plan.md § Summary](plan.md#summary) and the
[Complexity Tracking table](plan.md#complexity-tracking).

- Does the unified `IObjectStorage` shape genuinely cover both a backend stream
  and a time-limited URL ([FR-001](spec.md#functional-requirements)) without
  baking provider concerns into callers?
- Is selecting the provider purely from configuration
  ([FR-004](spec.md#functional-requirements)) the right level of indirection,
  or should the test fixture and the production composition root not pretend
  to share a code path?
- The plan moves `IFileStorageService` from `Domain.Interfaces` to
  `Application.Abstractions.Storage` (Complexity Tracking row 2) — does that
  layer move feel constitution-aligned, or does it deserve its own constitution
  amendment?

### Key decisions that need your eyes (12 min)

**Move the storage port out of Domain into Application**
([plan.md § Constitution Check](plan.md#constitution-check), Complexity Tracking)

The new abstraction needs DTOs (`StoredObject`, `StorageHandle`, category
enum), so leaving it in `Domain.Interfaces` would force Domain to know about
infrastructure-shaped types. The plan moves it.
- Question for reviewer: do you agree this is the cleanest fix, or would you
  rather keep the port in Domain with primitive-typed methods and let the
  category live in Application? Either choice has long-term cost.

**One-shot migration, no dual-read**
([FR-015](spec.md#functional-requirements),
[plan.md § Phase 2 strategy](plan.md#phase-2--tasks-deferred-to-speckit-tasks))

Going from `LocalFilesystem` to `AzureBlob` requires running the migration
tool, then flipping `Storage:Provider`. There is no fallback that serves blobs
from both backends concurrently.
- Question: if the migration completes but a few rows fail post-cutover (e.g.,
  a row written between `migration finish` and `provider toggle`), is the
  recovery path documented? `quickstart.md § 5` is the runbook home.

**SAS URLs disabled by default**
([FR-017](spec.md#functional-requirements),
[contracts/IObjectStorage.md](contracts/IObjectStorage.md))

Every initial category serves via backend streaming through the application.
Time-limited URLs are reserved for a future "large media" category. Default
expiry cap is 15 minutes ([FR-019](spec.md#functional-requirements)).
- Question: is forcing every download through the app server the right
  trade-off for audit and authorization simplicity, given that signed
  agreements are typically <5 MiB? What load on the Web project's egress
  budget would change this answer?

**New NuGet dependencies require spec approval**
(CLAUDE.md, [plan.md Complexity Tracking](plan.md#complexity-tracking))

`Aspire.Hosting.Azure.Storage`, `Aspire.Azure.Storage.Blobs`,
`Azure.Storage.Blobs`, `Azure.Identity` are added. This is the first material
expansion of the managed-dependency footprint since the project's "vendored
posture" was set.
- Question: does the Complexity Tracking justification meet your bar for
  approval, or do you want to see the alternative (calling the REST API
  directly) costed out further?

**Production guard returns Degraded health, not startup failure**
([FR-011](spec.md#functional-requirements), tasks
[T030](tasks.md#phase-4-user-story-1-priority-p1--production-deployment-uses-managed-cloud-storage))

When `Environment=Production` and a connection string is present, the plan
logs a warning and returns Degraded from a health check so a deployment gate
can fail. It does not throw at startup.
- Question: should this be a hard startup failure instead? The spec says
  "SHOULD be rejected by deployment gating" — the plan reads that as
  health-check-driven. Is that interpretation acceptable?

### Areas where I'm less certain (5 min)

- [spec.md FR-008](spec.md#functional-requirements): the "test fallback to
  LocalFilesystem when Azurite cannot start" path is opt-in, but it is unclear
  whether the team wants that path to exist at all in CI given the delivery
  bar. I read it as "off by default in CI, on for constrained dev laptops" —
  reviewer should confirm.
- [plan.md § Phase 1 Design](plan.md#phase-1--design--contracts) and
  [tasks T028 / T029](tasks.md#phase-4-user-story-1-priority-p1--production-deployment-uses-managed-cloud-storage):
  the plan adds `BlobKey` columns to existing tables and a `LegacyPath`
  backfill column. I assumed Domain entities would expose `RecordBlob(...)`
  behavior rather than a setter. If the team prefers a separate
  `StoredFileReference` value object on each aggregate, the dacpac shape
  changes.
- [tasks T024 facade](tasks.md#phase-3-user-story-2-priority-p1--local-developer-runs-production-equivalent-stack-with-one-command--mvp-slice):
  the temporary `FileStorageServiceFacade` always maps to
  `FileCategory.GeneratedArtifact`. That is a deliberate short-term placeholder
  during the migration window — but if a caller writes a non-artifact file
  through the facade between US2 and US1, it lands under the wrong key. The
  facade lifetime should be one PR, not a multi-week window.

### Risks and open questions (5 min)

- If the production storage account's managed identity is not configured by
  the time we deploy, FR-012's fail-fast behavior surfaces a deployment-time
  error rather than a runtime one. Does the operations team have a path to
  pre-validate the role assignment before the platform starts?
  ([FR-011](spec.md#functional-requirements))
- The Aspire emulator runs as a Docker container. CI agents that already need
  Docker for SQL Server will be fine; constrained environments without Docker
  fall through to the test-fallback path. Is the team comfortable that fallback
  doesn't run in PR CI? ([FR-009](spec.md#functional-requirements),
  [tasks T039a](tasks.md#phase-5-user-story-3-priority-p1--automated-tests-run-hermetically-without-azure-credentials))
- The migration tool depends on `FundingDbContext` to look up which entity
  owns each legacy file. If a legacy file has no DB row (orphan), the manifest
  will report it as `Failed`. Should the runbook explicitly call out the
  expected resolution? ([FR-024](spec.md#functional-requirements),
  [tasks T040–T046](tasks.md#phase-6-user-story-4-priority-p2--existing-on-disk-files-migrate-cleanly))
- Streaming-memory benchmark ([SC-006](spec.md#measurable-outcomes),
  [tasks T055](tasks.md#phase-8-polish--cross-cutting)) asserts ≤ 2× the
  streaming threshold. If a future test runs on a CI agent under memory
  pressure, the assertion may flake. Is the threshold tunable per environment?

---
*Full context in linked [spec](spec.md) and [plan](plan.md).*
