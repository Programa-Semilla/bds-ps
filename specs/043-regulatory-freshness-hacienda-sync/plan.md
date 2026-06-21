# Implementation Plan: Regulatory Freshness Gating + Hacienda API Sync

**Branch**: `043-regulatory-freshness-hacienda-sync` | **Date**: 2026-06-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/043-regulatory-freshness-hacienda-sync/spec.md`

## Summary

Close the slice-A (spec 038) compliance loop with two capabilities and their supporting surfaces:

1. **Staleness block** — at the auditor's audit-stage advance actions (generate/confirm the agreement PDF, release for signature), block when any provider the application *relies on* (the distinct `Supplier`s referenced by its approved items' `Item.SelectedSupplierId`) has any required regulatory field (Hacienda/CCSS/SICOP) stale — last-reviewed timestamp null or older than a configurable window (default 30 days). A non-blocking warning surfaces the same finding earlier on the reviewer send-to-audit and auditor screens.
2. **Daily Hacienda sync** — a `BackgroundService` (modeled on `StageExpiryReminderService`) runs once daily at a configurable local time, iterates all suppliers, calls `GET https://api.hacienda.go.cr/fe/ae?identificacion={id}` via an injectable `IHaciendaApiClient`, maps `situacion`→`HaciendaStatus`, updates the value when changed, always refreshes Hacienda freshness metadata (source `Api`, by `"system"`), records per-provider sync outcome, and writes audit (reusing slice-A `supplier.regulatory_changed`/`supplier.regulatory_reviewed` for success + a new `supplier.hacienda_sync_failed` for failures).

Plus: per-provider sync-failure visibility on the supplier detail screen + admin-list filter/badge; a daily stale-value digest emailed directly (not via the per-application outbox) to group-scoped auditors. No new managed dependency (built-in `HttpClient` via `IHttpClientFactory`); the live API is never called in tests (a `FakeHaciendaApiClient` is config-selected, mirroring `StubAiClient`).

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, .NET Aspire, built-in `HttpClient` (`IHttpClientFactory`) — **no new NuGet**
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`); EF Core for access only
**Testing**: NUnit (Unit/Integration), Playwright (E2E via `AspireFixture`); `FakeHaciendaApiClient` test double
**Target Platform**: Linux container (Aspire-orchestrated)
**Project Type**: Web application (ASP.NET MVC, Clean Architecture: Domain / Application / Infrastructure / Web)
**Performance Goals**: daily batch over the full supplier catalog without exhausting resources (throttled/batched, FR-017); freshness gate adds one indexed read per advance action
**Constraints**: worker must never crash the host and a single provider's failure must not abort the batch (FR-024); optimistic concurrency via existing `Supplier.RowVersion` (FR-025); es-CR copy; live Hacienda API never hit in tests
**Scale/Scope**: supplier catalog (tens–hundreds today); ~3 new Supplier columns; 2 new background services; 1 external client + 1 fake; 1 freshness service; gate insertions at 2–3 advance points; 2 admin-surface touches; 1 digest email factory + templates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| **I. Clean Architecture** | PASS | `IHaciendaApiClient`, `IRegulatoryFreshnessService`, options classes live in **Application**; live client, fake, freshness EF impl, workers, EF config live in **Infrastructure**; gate calls + views in **Web**. Dependencies point inward. |
| **II. Rich Domain Model** | PASS | Sync result application + staleness are domain behavior on `Supplier` (`ApplyHaciendaSyncResult`, `RecordHaciendaSyncFailure`, `IsRegulatoryStale`), returning slice-A `RegulatoryChange`. No anemic leakage. |
| **III. E2E Testing (NON-NEGOTIABLE)** | PASS | Each user story has Playwright coverage; a Development-only trigger endpoint runs the sync/digest deterministically; `FakeHaciendaApiClient` keeps E2E offline. |
| **IV. Schema-First** | PASS | New Supplier columns added to `dbo.Suppliers.sql` + EF config; no EF migrations; `HaciendaSyncOutcome` mapped `HasConversion<byte?>()`. |
| **V. Specification-Driven** | PASS | spec.md → plan.md → tasks.md → implement. |
| **VI. Simplicity / Progressive Complexity** | PASS | Reuses slice-A regulatory model/audit, the `StageExpiryReminderService` recurring-job pattern, the `StubAiClient` config-gated fake pattern, and the existing email pipeline. Configurable window + provider gate have sensible defaults; missing config does not crash. No new dependency. |

**Quality-gate specifics:** optimistic concurrency on the sync write (Principle/gate compliance via `Supplier.RowVersion`); freshness gate enforced server-side (not just hidden in UI); auditor authorization/group-scope already enforced by slice-C controllers — the gate is additive.

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/043-regulatory-freshness-hacienda-sync/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (OQ1/OQ2/OQ3 resolved, scheduling, scoping)
├── data-model.md        # Phase 1 — Supplier deltas, enums, freshness/finding shapes
├── quickstart.md        # Phase 1 — how to run/verify the feature locally + in tests
├── contracts/
│   └── interfaces.md     # Phase 1 — IHaciendaApiClient, IRegulatoryFreshnessService, options, mapper contract
└── tasks.md             # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Domain/
    Entities/Supplier.cs                         # + HaciendaSyncAttemptAt/Outcome/Error; ApplyHaciendaSyncResult, RecordHaciendaSyncFailure, IsRegulatoryStale, StaleRequiredFields
    Enums/HaciendaSyncOutcome.cs                 # NEW (Success/Failure)
    Enums/RegulatoryField.cs                     # existing (reused)
    ValueObjects/RegulatoryChange.cs             # existing (reused as sync return)
    Entities/AdminAuditEvent.cs                  # + "supplier.hacienda_sync_failed" constant

  FundingPlatform.Application/
    Abstractions/Hacienda/IHaciendaApiClient.cs  # NEW seam + HaciendaLookupResult/HaciendaSituacion DTOs
    Regulatory/IRegulatoryFreshnessService.cs    # NEW gate/warn query + StaleRegulatoryFinding DTO
    Regulatory/RegulatoryFreshnessOptions.cs     # NEW (FreshnessWindowDays=30)
    Regulatory/HaciendaSyncOptions.cs            # NEW (Provider, Enabled, RunAtLocalTime, BatchSize, throttle)

  FundingPlatform.Infrastructure/
    Hacienda/LiveHaciendaApiClient.cs            # NEW HttpClient impl (IHttpClientFactory)
    Hacienda/FakeHaciendaApiClient.cs            # NEW test double (canned + static counters), mirrors StubAiClient
    Hacienda/HaciendaStatusMapper.cs             # NEW pure response→HaciendaStatus mapping
    Services/RegulatoryFreshnessService.cs       # NEW EF impl of IRegulatoryFreshnessService
    BackgroundServices/HaciendaSyncService.cs    # NEW daily sync worker + public RunOnceAsync seam
    BackgroundServices/RegulatoryFreshnessDigestService.cs  # NEW daily digest worker + public RunOnceAsync seam
    Email/RegulatoryDigestEmailFactory.cs        # NEW (mirrors StageReminderEmailFactory; brand shell)
    Persistence/Configurations/SupplierConfiguration.cs  # + new columns mapping
    DependencyInjection.cs                       # Configure options, provider gate, AddHttpClient, AddHostedService x2
  FundingPlatform.Database/
    Tables/dbo.Suppliers.sql                     # + HaciendaSyncAttemptAt/Outcome/Error columns

  FundingPlatform.Web/
    Controllers/FundingAgreementController.cs    # Generate: insert freshness gate (block)
    Controllers/AuditController.cs               # Detail: surface non-blocking warning
    Controllers/Admin/AdminSuppliersController.cs# Detail: show last-sync outcome; Index: "verificación fallida" filter/badge
    Controllers/DevSeedController.cs (or existing dev seam)  # Development-only RunHaciendaSync / RunFreshnessDigest triggers
    Views/Audit/*.cshtml, Views/Review/Review.cshtml        # warning panel partial
    Views/Emails/Regulatory/*.cshtml             # digest html + text twins (041 brand shell)
    Resources/*.resx (es-CR)                     # block/warning/failure/digest strings

tests/
  FundingPlatform.Tests.Unit/                    # HaciendaStatusMapper table; Supplier sync/freshness methods; freshness finding logic
  FundingPlatform.Tests.Integration/             # RegulatoryFreshnessService queries; HaciendaSyncService.RunOnceAsync (changed/unchanged/404/failure) w/ Fake; digest service
  FundingPlatform.Tests.E2E/                     # block+message+re-authorize; sync via dev endpoint → status/audit/freshness; failure surface; digest capture; early warnings
```

**Structure Decision**: Existing 4-layer Clean Architecture (Domain/Application/Infrastructure/Web) + the three test projects. This feature adds files within those layers; no new project, no new top-level structure.

## Phase 0 — Research (decisions)

See [research.md](./research.md). Summary of resolved decisions:

- **OQ1 — Hacienda→`HaciendaStatus` mapping** (live-sampled): `Inscrito`+moroso=NO+omiso=NO → `AlDia`; `Inscrito`+moroso=SI → `EstadoMoroso`; `Inscrito`+omiso=SI (not moroso) → `CobroAdministrativo`; `Desinscrito`+moroso=NO → `DesinscritoAlDia`; `Desinscrito`+moroso=SI → `DesinscritoMoroso`; **200 `No inscrito` → `SinInscripcion`**; **HTTP 404 (`{code:404,…}` "information not available") → `SinInformacion`** (distinct from "No inscrito"); transport/5xx/timeout/parse error / malformed-or-missing local id → **failure** (no value change). `DesinscritoDeOficio` is not detectable from `fe/ae` and is never auto-set.
- **OQ2 — referenced-provider scope**: the distinct set of `Supplier` referenced by the application's **approved** items via `Item.SelectedSupplierId` (the suppliers that appear in the funding agreement). Not all attached quotations.
- **OQ3 — notification**: a **daily digest** sent **directly via `IEmailSender`** (the `StageExpiryReminderService` pattern), **not** the per-application outbox — so no new `NotificationEvent`. Scoped by applications in the audit pipeline (`PendingAudit`/`ReturnedFromAudit`) per group → auditors of that group (reusing the slice-C group→auditor resolution); one aggregated email per auditor.
- **Scheduling**: a `BackgroundService` that computes the delay to the next configured local time-of-day (`RunAtLocalTime`, default `06:00` America/Costa_Rica), runs, repeats; a public `RunOnceAsync` test seam (mirrors `ExecuteOneCycleAsync`). Minor, justified deviation from the pure `PeriodicTimer` interval because §16.5 requires a wall-clock morning run.
- **Test seam selection**: config gate `Regulatory:HaciendaSync:Provider` = `Live` (default in real envs) / `Fake` (Aspire dev + E2E), mirroring `AiComparison:Provider` Anthropic/Stub. `FakeHaciendaApiClient` exposes static counters + canned results + a reset, mirroring `StubAiClient`.

## Phase 1 — Design & Contracts

Artifacts: [data-model.md](./data-model.md), [contracts/interfaces.md](./contracts/interfaces.md), [quickstart.md](./quickstart.md).

**Domain (Rich Model):**
- `Supplier` gains `HaciendaSyncAttemptAt` (DateTime?), `HaciendaSyncOutcome` (`HaciendaSyncOutcome?`), `HaciendaSyncError` (string?, ≤500), plus:
  - `RegulatoryChange ApplyHaciendaSyncResult(HaciendaStatus mapped, DateTime nowUtc)` — sets `HaciendaStatus` if changed (kind `Changed`) else `ReviewedNoChange`; stamps `HaciendaLastReviewedAt=nowUtc`, `HaciendaLastReviewedBy="system"`, `HaciendaLastReviewedSource=Api`; sets sync outcome `Success`, clears error.
  - `void RecordHaciendaSyncFailure(DateTime nowUtc, string reason)` — sets `HaciendaSyncAttemptAt`, outcome `Failure`, error; **does not touch any status or last-reviewed field** (FR-018).
  - `bool IsRegulatoryStale(int windowDays, DateTime nowUtc)` + `IReadOnlyList<RegulatoryField> StaleRequiredFields(int windowDays, DateTime nowUtc)` — pure freshness predicate per FR-001/FR-005 (null OR older than window, for all three required fields).

**Application:**
- `IHaciendaApiClient.LookupAsync(string identificacion, CancellationToken)` → `HaciendaLookupResult` (Found{nombre, situacion{estado,moroso,omiso}} | NotRegistered(404) | Failed(reason)).
- `IRegulatoryFreshnessService`:
  - `Task<IReadOnlyList<StaleRegulatoryFinding>> GetStaleFindingsForApplicationAsync(int appId, CancellationToken)` — the gate/warning query over the app's selected suppliers.
  - `StaleRegulatoryFinding` = { SupplierId, SupplierName, Field, LastReviewedAt? }.
- Options: `RegulatoryFreshnessOptions { int FreshnessWindowDays = 30 }`; `HaciendaSyncOptions { string Provider = "Live"; bool Enabled = true; string RunAtLocalTime = "06:00"; int BatchSize = 100; int PerCallDelayMs = 0 }`.

**Infrastructure:**
- `HaciendaStatusMapper.Map(HaciendaLookupResult)` → `HaciendaStatus?` (null only when the result is `Failed`; mapping per OQ1).
- `LiveHaciendaApiClient` — typed `HttpClient` (BaseAddress `https://api.hacienda.go.cr`), GET `/fe/ae?identificacion=...`, 200→parse, 404→NotRegistered, else/parse-error→Failed; timeout + try/catch → Failed.
- `RegulatoryFreshnessService` — EF query: load app → approved items → distinct `SelectedSupplier`s → compute stale findings using `RegulatoryFreshnessOptions.FreshnessWindowDays`.
- `HaciendaSyncService` — daily; per supplier: skip/Fail malformed id; `LookupAsync`; `Map`; `ApplyHaciendaSyncResult` or `RecordHaciendaSyncFailure`; write audit (slice-A verbs for success, `supplier.hacienda_sync_failed` for failure); save per supplier with `RowVersion` (on `DbUpdateConcurrencyException` skip+log, FR-025); batch/throttle; never let one provider abort the run (FR-024).
- `RegulatoryFreshnessDigestService` — daily; gather audit-pipeline apps with stale selected suppliers, group→auditors, one aggregated `IEmailSender` send per auditor through the 041 brand shell.

**Web:**
- Freshness gate inserted at `FundingAgreementController.Generate` (auditor path) and `AuditWorkflowService.ReleaseForSignatureAsync` + `ConfirmAgreementPdf` path — call `IRegulatoryFreshnessService`; if findings non-empty, refuse with an es-CR toast/inline message enumerating provider+field+last-reviewed (FR-007), mirroring the existing `IsAuditChecklistCompleteAsync` check shape.
- Non-blocking warning partial on `Review.cshtml` (send-to-audit) and `Views/Audit/*` from the same service.
- `AdminSuppliersController` Detail shows last-sync outcome/time/reason; Index gains a "verificación fallida" filter + row badge.
- Development-only endpoints to trigger `HaciendaSyncService.RunOnceAsync` and `RegulatoryFreshnessDigestService.RunOnceAsync` for E2E (mirrors the `GET /Account/SeedUser` dev seam; 404 outside Development).

**Agent context:** the `<!-- SPECKIT … -->` block in `CLAUDE.md` is updated to point at this plan.

### Post-Design Constitution Re-Check

Re-evaluated after the design above: **still PASS.** No new project, no new managed dependency, schema change is dacpac-only, domain logic stays in the entity, every story is E2E-testable with an offline fake, and optimistic concurrency is honored on the sync write. Complexity Tracking remains empty.

## Complexity Tracking

No constitution violations — none required.
