---
description: "Task list for spec 020 AI-Powered Quote Comparison for Reviewers"
---

# Tasks: AI-Powered Quote Comparison for Reviewers

**Input**: Design documents from `/specs/020-ai-quote-comparison/`
**Prerequisites**: spec.md, plan.md, research.md, data-model.md, contracts/ (all complete)

**Tests**: E2E tests are NON-NEGOTIABLE per Constitution Principle III. Unit + integration test tasks are included where they materially improve confidence (PII redactor determinism, hash determinism, schema validation, domain state machines, normalizer behavior, guards, reaper) — selected to satisfy spec Acceptance Scenarios and Success Criteria without over-instrumenting.

**Organization**: Tasks grouped by user story (US1–US5). Foundational phase blocks all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no in-phase dependency)
- **[Story]**: US1/US2/US3/US4/US5 (omitted in Setup, Foundational, Polish)
- File paths absolute from repo root.

## Path Conventions

- Web app structure already in place: `src/FundingPlatform.{AppHost,Web,Application,Domain,Infrastructure,Database,ServiceDefaults}`.
- Tests: `tests/FundingPlatform.Tests.{Unit,Integration,E2E}` and `tests/Fixtures/`.
- Source-tree prompt + schema artifacts: `prompts/` and `schemas/` at repo root (per plan).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring the new dependency, configuration knobs, and source-tree artifacts into place.

- [ ] T001 Add `Anthropic.SDK` NuGet package to `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj` (pinned to latest stable at time of work; record exact version in plan.md if it drifts).
- [ ] T002 [P] Create `prompts/extract.v1.md` with the per-supplier extract system + user prompt (in es-CR, includes the prompt-injection mitigation language from NFR-S5, references `schemas/ExtractedSupplierOffering.v1.schema.json`).
- [ ] T003 [P] Create `prompts/compare.v1.md` with the comparator system + user prompt (in es-CR, includes prompt-injection mitigation, enforces narrative-section titles, references `schemas/ComparisonArtifact.v1.schema.json`).
- [ ] T004 [P] Copy `specs/020-ai-quote-comparison/contracts/ComparisonArtifact.v1.schema.json` to `schemas/ComparisonArtifact.v1.schema.json` (source-tree home per NFR-M2 — the file in `contracts/` is the spec-time artifact; the file in `schemas/` is the runtime reference).
- [ ] T005 [P] Copy `specs/020-ai-quote-comparison/contracts/ExtractedSupplierOffering.v1.schema.json` to `schemas/ExtractedSupplierOffering.v1.schema.json`.
- [ ] T006 Register the new configuration knobs in `src/FundingPlatform.AppHost/AppHost.cs` (`AiComparison:*` keys from plan.md Configuration Knobs table) and wire them to the web app via `WithEnvironment("AiComparison__...", ...)`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, domain entities, application abstractions, DI wiring, PII redactor, input hasher, repositories. Everything every user story depends on.

**⚠️ CRITICAL**: No user-story work begins until this phase completes.

### Schema

- [ ] T007 Add `src/FundingPlatform.Database/Tables/dbo.ComparisonArtifacts.sql` per data-model.md (PK on `ApplicationItemId`, FK to `dbo.Items` ON DELETE CASCADE, non-clustered index on `InputHash`).
- [ ] T008 [P] Add `src/FundingPlatform.Database/Tables/dbo.ComparisonJobs.sql` per data-model.md (PK on `Id`, FK to `dbo.Items` ON DELETE CASCADE, indexes `(Status, LastStatusChangeAt)` + `(ApplicationItemId, Status)`).
- [ ] T009 [P] Add the project file include lines for the two new `.sql` files to `src/FundingPlatform.Database/FundingPlatform.Database.sqlproj` if MSBuild does not auto-include them.

### Domain entities + value objects

- [ ] T010 Create `src/FundingPlatform.Domain/Entities/ComparisonArtifact.cs` aggregate root with private setters, factory `Create(...)`, behavior methods `IsStaleAgainst(InputDescriptor) → FreshnessResult` and `ReplaceWith(...)`, invariants (64-hex hash, non-negative tokens/latency, non-empty schema/prompt versions). Throw `ArgumentException` on construction violations.
- [ ] T011 [P] Create `src/FundingPlatform.Domain/Entities/ComparisonJob.cs` aggregate root with `Status` enum (`Pending`/`Running`/`Completed`/`Failed`), state-machine-guarded `Enqueue(...)` factory, `Start(IClock)`, `RecordSuccess(...)`, `RecordFailure(...)`, `Reap(IClock)` per data-model.md. Illegal transitions throw `InvalidOperationException`.
- [ ] T012 [P] Create `src/FundingPlatform.Domain/Entities/FreshnessResult.cs` and `ChangedInput` enum (FileAdded, FileRemoved, LineEdited, SupplierAdded, SupplierRemoved, SnapshotChanged, SchemaBumped, PromptVersionBumped).

### Application abstractions

- [ ] T013 [P] Create `src/FundingPlatform.Application/Abstractions/AiComparison/IAiClient.cs` with `ExtractRequest`/`CompareRequest`/`AiInputBlock` (`TextBlock`, `PdfBlock`)/`ExtractResult`/`CompareResult` record types per contracts/ai-client.md.
- [ ] T014 [P] Create `src/FundingPlatform.Application/Abstractions/AiComparison/IPiiRedactor.cs` with `SupplierAssemblyDto`, `RedactionResult`, `RedactedSpan` per contracts/ai-client.md.
- [ ] T015 [P] Create `src/FundingPlatform.Application/Abstractions/AiComparison/IComparisonOrchestrator.cs` with `GenerateComparisonCommand`, `GenerateComparisonResult` (Success/Failure variants), `ItemStatusResult`, `ItemState` enum, `Freshness` enum per contracts/ai-client.md.
- [ ] T016 [P] Create `src/FundingPlatform.Application/Abstractions/AiComparison/InputDescriptor.cs` value object per data-model.md (immutable record with `OrderedSupplierIds`, `OrderedBranchIds`, `BlobReferences`, `LineState`, `PromptVersion`, `SchemaVersion`).
- [ ] T017 [P] Create `src/FundingPlatform.Application/Abstractions/AiComparison/IComparisonArtifactRepository.cs` + `IComparisonJobRepository.cs` with method signatures from data-model.md.
- [ ] T018 Create `src/FundingPlatform.Application/Abstractions/AiComparison/AiProviderExceptions.cs` (`AiProviderTransientException`, `AiProviderHardException` with `ProviderCode` property, `AiSchemaInvalidException` with validator path).

### Application services (non-orchestrator pieces, used across stories)

- [ ] T019 Implement `src/FundingPlatform.Application/AiComparison/InputHasher.cs` — pure function `Compute(InputDescriptor) → string`. Canonical JSON: sorted keys, declared array order, SHA-256 lower-case hex. No null-vs-missing ambiguity.
- [ ] T020 [P] Implement `src/FundingPlatform.Application/AiComparison/ComparisonNormalizer.cs` — pure server-side normalize stage: unit alignment (kg/lb, m/cm, unit/box), date normalization to es-CR `MMM DD, YYYY`, CRC conversion using each quotation's spec-015 snapshot id, DB-vs-file discrepancy passthrough (both values + flag, per A-6).
- [ ] T021 [P] Implement `src/FundingPlatform.Application/AiComparison/PromptCatalog.cs` — loads `prompts/extract.v1.md` and `prompts/compare.v1.md` at startup; exposes `ExtractPrompt`, `ComparePrompt`, `PromptVersion`, `SchemaVersion`. Singleton.
- [ ] T022 [P] Implement `src/FundingPlatform.Application/AiComparison/SchemaValidator.cs` — wraps `JsonSchema.Net` to validate JSON strings against the v1 schemas; throws `AiSchemaInvalidException` with the validator's first error path on failure.

### Infrastructure: redactor + repositories + EF config

- [ ] T023 Implement `src/FundingPlatform.Infrastructure/AiComparison/Redaction/PiiRedactor.cs` (and `Patterns/` regex helpers). FR-B2 fields: cédula (CR national-ID pattern), CR phone (e.g. `^[+]?506?[ ]?[0-9]{4}[-]?[0-9]{4}$` family), email pattern. Structured-field redaction for applicant national id / applicant phone / applicant email / supplier owner DNI / supplier owner phone. Deterministic. Refuses (throws) if a supplied "file text" is empty/whitespace-only, signaling the caller to surface `pii_redaction_failed`.
- [ ] T024 [P] Implement `src/FundingPlatform.Infrastructure/Persistence/Configurations/ComparisonArtifactConfiguration.cs` (EF entity config).
- [ ] T025 [P] Implement `src/FundingPlatform.Infrastructure/Persistence/Configurations/ComparisonJobConfiguration.cs` (EF entity config; `Status` as `HasConversion<string>`).
- [ ] T026 Add `DbSet<ComparisonArtifact> ComparisonArtifacts` and `DbSet<ComparisonJob> ComparisonJobs` to `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` (re-uses existing `ApplyConfigurationsFromAssembly`).
- [ ] T027 [P] Implement `src/FundingPlatform.Infrastructure/Persistence/Repositories/ComparisonArtifactRepository.cs` against `IComparisonArtifactRepository`.
- [ ] T028 [P] Implement `src/FundingPlatform.Infrastructure/Persistence/Repositories/ComparisonJobRepository.cs` against `IComparisonJobRepository`.

### Infrastructure: AI client + DI

- [ ] T029 Implement `src/FundingPlatform.Infrastructure/AiComparison/Anthropic/AnthropicAiClient.cs` (`IAiClient` impl via `Anthropic.SDK`). Uses JSON-mode/tool-use to enforce schema; surfaces transient vs hard via the typed exceptions (T018). No retry. No raw logging. Constructor takes `IOptions<AnthropicOptions>` reading `AiComparison:Anthropic:*`. **Fail-fast at DI registration** if API key missing in non-Development env.
- [ ] T030 [P] Implement a stub `src/FundingPlatform.Infrastructure/AiComparison/Anthropic/StubAiClient.cs` returning canned schema-valid responses loaded from `tests/Fixtures/AiComparison/canned-extract.json` and `tests/Fixtures/AiComparison/canned-compare.json` when `AiComparison:Provider == "Stub"`.
- [ ] T031 Add DI wiring in `src/FundingPlatform.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (or equivalent existing extension class) for: `IPiiRedactor`, `IAiClient` (`Anthropic` / `Stub`), `IComparisonArtifactRepository`, `IComparisonJobRepository`, `PromptCatalog`, `SchemaValidator`. Pick provider by `AiComparison:Provider`.

### Unit tests (foundational)

- [ ] T032 [P] Create `tests/Fixtures/Pii/` fixtures: representative supplier text snippets (CR national IDs, phones, emails, supplier-owner DNI, supplier-owner phones, mixed in narrative text). Include negative fixtures (no PII present).
- [ ] T033 [P] Implement `tests/FundingPlatform.Tests.Unit/AiComparison/PiiRedactorTests.cs` — fixture sweep proving SC-006 (no enumerated PII pattern appears in `SafePayload`), determinism (same input ⇒ same output across runs), span counts populated.
- [ ] T034 [P] Implement `tests/FundingPlatform.Tests.Unit/AiComparison/InputHasherTests.cs` — invariance under map-key reordering, sensitivity to declared list order, 64-hex shape, mutation of any field changes the hash.
- [ ] T035 [P] Implement `tests/FundingPlatform.Tests.Unit/AiComparison/ComparisonNormalizerTests.cs` — unit conversion (kg↔lb, m↔cm), date formatting es-CR, CRC conversion using snapshot id (not live rate), discrepancy passthrough.
- [ ] T036 [P] Implement `tests/FundingPlatform.Tests.Unit/AiComparison/SchemaValidationTests.cs` — happy-path validation, malformed JSON, missing required field, `additionalProperties` violation; all surface as `AiSchemaInvalidException` with validator path.
- [ ] T037 [P] Implement `tests/FundingPlatform.Tests.Unit/Domain/ComparisonArtifactBehaviorTests.cs` — `IsStaleAgainst` enumerates the right `ChangedInput`s; `ReplaceWith` rejects bad hash / negative tokens / schema-invalid JSON.
- [ ] T038 [P] Implement `tests/FundingPlatform.Tests.Unit/Domain/ComparisonJobBehaviorTests.cs` — state machine: legal transitions succeed, every illegal transition throws `InvalidOperationException`; `Reap` only acts on stale `Running`.

**Checkpoint**: Foundation ready. Domain + persistence + AI seam + redactor + hashing + schema validation are all in place and unit-verified. User stories can now begin.

---

## Phase 3: User Story 1 — Reviewer generates AI comparison for one item (Priority: P1) 🎯 MVP

**Goal**: A reviewer clicks **Generar comparación** on an item with 2+ supplier quotations and sees a Tabler-styled comparison table + Spanish narrative panel rendered inline within 60 s.

**Independent Test**: Seed an application with one item with two supplier quotations (one CRC, one USD), each with one PDF. As a reviewer, click **Generar comparación**. Confirm: table renders with both suppliers as columns, attribute rows populated, narrative sections in es-CR, **Análisis de Costos** names cheapest vs most expensive in CRC.

### Orchestrator + supplier-assembly path

- [ ] T039 [US1] Implement `src/FundingPlatform.Application/AiComparison/SupplierAssembler.cs` — given an `ApplicationItemId`, loads `Item` + each supplier's `Quotation`+`QuotationLine` rows + `Supplier`+`SupplierBranch` + attached blob references (via `IObjectStorage`). Returns a per-supplier list of `SupplierAssemblyDto` (the redactor's input shape).
- [ ] T040 [US1] Implement `src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs` (`IComparisonOrchestrator` impl): orchestration flow from `contracts/ai-client.md` section "Orchestration flow". For US1 scope: per-item lock, build `InputDescriptor`, cache short-circuit on fresh, extract (parallel), schema-validate extracts, normalize, compare, schema-validate compare, persist via `ComparisonArtifact.ReplaceWith`, emit `AdminAuditEvent`. Rate-limit + token-cap guard hooks present but no-op until US4. `GetStatusAsync` derives `ItemState`/`Freshness` from artifact + (when US3 lands) latest job.
- [ ] T041 [US1] Implement `src/FundingPlatform.Application/AiComparison/Commands/GenerateComparisonCommandHandler.cs` (thin wrapper invoked by the controller; resolves user identity + role; calls `IComparisonOrchestrator.GenerateAsync`).
- [ ] T042 [US1] Implement audit emission helper `src/FundingPlatform.Application/AiComparison/AdminAuditEventComparisonFactory.cs` — produces `AdminAuditEvent` rows with the `PayloadJson` shape from `contracts/audit-event-payload.md` (success + failure variants). Reuse existing `AdminAuditEvent.Record(...)` factory.

### Web surface

- [ ] T043 [US1] Modify `src/FundingPlatform.Web/Controllers/ReviewController.cs`: add `POST /Review/GenerateComparison/{applicationItemId}` action per `contracts/endpoints.md`. Apply group-overlap predicate via existing helper before invoking the orchestrator. Return `200 OK` with `ItemComparisonViewModel`, or one of the documented error envelopes (400/403/422/500/502/504) per the contract.
- [ ] T044 [US1] Create `src/FundingPlatform.Web/ViewModels/Review/ItemComparisonViewModel.cs` projecting the artifact JSON + `freshness` (`Fresh`/`Stale`/`None` — US1 only emits `Fresh`/`None`) + `lastUpdatedAt`.
- [ ] T045 [US1] Create `src/FundingPlatform.Web/Views/Review/_ComparisonRegion.cshtml` partial — Tabler-styled comparison table (suppliers as columns, `attributeRows` as rows, item header above) followed by a stacked panel of narrative sections. Sanitize all AI-derived text (NFR-S4). Currency formatting per spec FR-E4 (CRC `₡` prefix + es-CR thousands; non-CRC parens). Render the **Generar comparación** button when no artifact exists.
- [ ] T046 [US1] Modify `src/FundingPlatform.Web/Views/Review/Review.cshtml` to render `_ComparisonRegion` per item card. Hide the button when the item has < 2 supplier quotations and show the tooltip `"Se necesitan al menos 2 cotizaciones para comparar."` (spec acceptance scenario US1#2).
- [ ] T047 [US1] Create `src/FundingPlatform.Web/wwwroot/css/comparison.css` — comparison-region styles consistent with Tabler tokens. WCAG 2.1 AA color contrast (NFR-A1). Reference from `Review.cshtml`'s vendored-asset budget per existing pattern.
- [ ] T048 [US1] Create `src/FundingPlatform.Web/wwwroot/js/comparison.js` — minimal vanilla JS: clicking **Generar comparación** POSTs to the endpoint and replaces the comparison region with the response markup (no framework; consistent with project's no-CDN posture). Loading state + error display. No polling yet (US3 adds it).

### Integration + E2E tests

- [ ] T049 [P] [US1] Implement `tests/FundingPlatform.Tests.Integration/ComparisonOrchestratorIntegrationTests.cs` — real DB, stubbed `IAiClient` returning canned schema-valid extracts + compare. Asserts: artifact persists with correct `InputHash`/`PromptVersion`/`SchemaVersion`/`AiModel`; one `AdminAuditEvent` row emitted with the documented payload shape; success path completes in `< 60 s` (offline = sub-second).
- [ ] T050 [P] [US1] Add fixture `tests/Fixtures/AiComparison/canned-extract.json` (two suppliers' offerings, mixed CRC/USD).
- [ ] T051 [P] [US1] Add fixture `tests/Fixtures/AiComparison/canned-compare.json` (final artifact mirroring the spec User Story 1 example: suppliers as columns, attribute rows, narrative sections in es-CR including **Análisis de Costos** with cheapest/most-expensive call-out).
- [ ] T052 [US1] Implement `tests/FundingPlatform.Tests.E2E/AiComparison/GenerateComparisonTests.cs` — Playwright spec covering: reviewer signs in → opens application → clicks **Generar comparación** → comparison region renders with table + narrative; single-supplier item shows tooltip instead of button; out-of-group reviewer sees no button + direct route returns 403 (per US1 acceptance scenarios #1, #2, #4).

**Checkpoint**: US1 functional. Reviewer can generate a comparison; admin audit row written; rate-limit + token-cap guards are placeholders.

---

## Phase 4: User Story 2 — Cached comparison is reused; auto-invalidated on input change (Priority: P1)

**Goal**: Cached fresh comparison renders instantly without an AI call. Any input change surfaces a **Datos desactualizados** badge naming the changed input; the action label becomes **Regenerar**; regeneration replaces the cached artifact in place.

**Independent Test**: Run US1 to seed a cached comparison. Open the same item as a different reviewer; confirm no AI call. Edit a `QuotationLine.Quantity`; reload; confirm cached still renders with `línea editada` badge and **Regenerar** button. Click **Regenerar**; confirm overwrite + badge cleared. Bump `AiComparison:SchemaVersion`; confirm cache treated as stale.

### Cache + stale path

- [ ] T053 [US2] Modify `src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs` to project `Freshness` + `ChangedInput[]` into the `ItemComparisonViewModel` path (read via a new method `GetCachedComparisonAsync(applicationItemId)` that returns artifact JSON + freshness + changed-inputs without triggering generation).
- [ ] T054 [US2] Modify `src/FundingPlatform.Web/Controllers/ReviewController.cs` so the existing review page (`GET /Review/Review/{id}`) loads per-item cached comparisons + freshness via the orchestrator's `GetCachedComparisonAsync`. Updates `ItemCardProjection` upstream if a card-level projection exists, else inlines in the controller.
- [ ] T055 [US2] Modify `src/FundingPlatform.Web/Views/Review/_ComparisonRegion.cshtml` to render: cached artifact when present; **Datos desactualizados** badge with localized changed-input labels (`archivo añadido`, `archivo eliminado`, `línea editada`, `proveedor añadido`, `proveedor eliminado`, `tipo de cambio actualizado`, `esquema actualizado`, `prompt actualizado`) when freshness is `Stale`; button label switches to **Regenerar**.
- [ ] T056 [US2] Modify `src/FundingPlatform.Application/AiComparison/Commands/GenerateComparisonCommandHandler.cs` to honor a `forceRegenerate` flag (defaults `false`). When the cache is fresh and `forceRegenerate == false`, return the cached artifact and skip the AI calls. When `forceRegenerate == true`, run the pipeline and persist via `ReplaceWith`.

### Integration + E2E

- [ ] T057 [P] [US2] Implement `tests/FundingPlatform.Tests.Integration/ComparisonCacheStaleDiffTests.cs` — seed artifact, mutate input in each axis (file blob hash, line state, supplier set, snapshot id, schema version, prompt version) and assert `ChangedInputs` correctly names the diff.
- [ ] T058 [P] [US2] Add fixture `tests/Fixtures/AiComparison/canned-compare-v2.json` (regen result for the stale path).
- [ ] T059 [US2] Implement `tests/FundingPlatform.Tests.E2E/AiComparison/CacheFreshAndStaleTests.cs` — Playwright spec covering US2 acceptance scenarios: cached render = no AI call (assert via the stub's call counter exposed in test config); quotation-line edit ⇒ stale badge + Regenerar; **Regenerar** overwrites in place; bump `AiComparison:SchemaVersion` ⇒ stale.

**Checkpoint**: US2 functional. Cache + freshness signal + in-place regen all work.

---

## Phase 5: User Story 3 — "Generar todo" for the whole application (Priority: P2)

**Goal**: Application-level **Generar todo** enqueues per-item jobs (skipping fresh cached items); the page polls per-item status; reviewer can navigate within the application while jobs run.

**Independent Test**: Seed an application with 5 items, each having 2+ suppliers. Click **Generar todo**. Verify polling, status transitions (`Pendiente` → `En progreso` → `Listo` / `Falló`) without manual refresh, and that fresh items are skipped. Click admin **Forzar regeneración total** (after Anular límites is on); verify every item is enqueued.

### Worker + job repository surface

- [ ] T060 [US3] Implement `src/FundingPlatform.Infrastructure/AiComparison/ComparisonJobWorker.cs` — a hosted `BackgroundService` that polls `IComparisonJobRepository.GetNextPendingAsync()` and runs the orchestrator on each. Concurrency limited by `AiComparison:WorkerConcurrency` (default 2) using `SemaphoreSlim`. Updates job status via `Start`/`RecordSuccess`/`RecordFailure`. Catches and routes exceptions to `RecordFailure` with the right `failureReason`.
- [ ] T061 [US3] Implement `src/FundingPlatform.Infrastructure/AiComparison/ComparisonJobReaper.cs` — a hosted service that on startup AND every 5 minutes scans `Status='Running' AND LastStatusChangeAt < now - OrphanReapAfterMinutes` and calls `Reap(IClock)` on each, persisting the resulting `Failed` state with `failureReason='worker_crashed'`.
- [ ] T062 [US3] Register both hosted services in the existing infrastructure DI registration (alongside `EnsureContainersHostedService`).
- [ ] T063 [US3] Extend `IComparisonJobRepository` with `GetNextPendingAsync(CancellationToken)` (atomic `Pending → Running` claim, e.g. `UPDATE ... OUTPUT INSERTED.* WHERE Status = 'Pending' AND Id = (SELECT TOP 1 Id ... ORDER BY LastStatusChangeAt)` or equivalent), and `GetOrphanedRunningAsync(DateTimeOffset cutoff, ...)`. Update `ComparisonJobRepository` impl accordingly.

### Endpoints + UI

- [ ] T064 [US3] Add `POST /Review/GenerateAll/{applicationId}` action to `ReviewController` per `contracts/endpoints.md`. Resolves eligible items (≥ 2 suppliers; cache stale or missing — or all when `forceAll && isAdmin`), enqueues a `ComparisonJob` per item, returns the documented `202 Accepted` envelope.
- [ ] T065 [US3] Add `GET /Review/ItemStatus/{applicationItemId}` action returning the `ItemStatusResult` per the contract. Apply group-overlap guard.
- [ ] T066 [US3] Modify `src/FundingPlatform.Web/Views/Review/Review.cshtml` to render: app-level **Generar todo** button; admin-only **Anular límites** toggle (visual gate; server enforces role); admin-only **Forzar regeneración total** sub-action (only enabled once **Anular límites** toggle is on per A-7).
- [ ] T067 [US3] Extend `src/FundingPlatform.Web/wwwroot/js/comparison.js` to: POST `Generar todo`, then start a per-application polling loop (configurable interval from a data attribute, default 3 s) that hits `GET /Review/ItemStatus/{itemId}` for every visible item card; update each card's status pill (`Pendiente`/`En progreso`/`Listo`/`Falló`); stop polling when no item is `Pending`/`Running`; survive within-application navigation by re-binding on `DOMContentLoaded`.

### Tests

- [ ] T068 [P] [US3] Implement `tests/FundingPlatform.Tests.Integration/ComparisonJobWorkerTests.cs` — enqueue N jobs, assert worker picks them up, status transitions land, results persist, concurrency cap respected.
- [ ] T069 [P] [US3] Implement `tests/FundingPlatform.Tests.Integration/ComparisonJobReaperTests.cs` — orphaned `Running > 5 min` ⇒ `Failed/worker_crashed`; fresh `Running` left alone.
- [ ] T070 [US3] Implement `tests/FundingPlatform.Tests.E2E/AiComparison/GenerateAllTests.cs` — Playwright spec: app with 5 items (3 fresh, 2 stale) ⇒ only 2 enqueued; statuses update on poll without page reload; admin **Forzar regeneración total** path enqueues all (after enabling **Anular límites** toggle); failed job leaves prior cache visible + shows **Reintentar**.

**Checkpoint**: US3 functional. Worker + polling + skip-fresh + admin force-all all work.

---

## Phase 6: User Story 4 — Admin overrides rate limit or token cap when justified (Priority: P2)

**Goal**: Enforce per-application 24h rate limit (default 10) and per-run input token cap (default 200,000). Admin can bypass per click. Bypasses are audited.

**Independent Test**: Trigger 10 generations on an application as reviewer in quick succession; confirm 11th blocked with the spec's exact message + `failureReason=rate_limit_exceeded` in audit. As admin, toggle **Anular límites**, click **Regenerar**; confirm generation runs and audit row has `bypassedRateLimit=true`. Submit an item where pre-flight token estimate exceeds the cap as reviewer; confirm rejection before any provider call with reviewer-facing message naming the offending input.

### Guards

- [ ] T071 [US4] Implement `src/FundingPlatform.Application/AiComparison/RateLimitGuard.cs` — counts successful + failed `AdminAuditEvent` rows of `Action ∈ {AiComparisonGenerated, AiComparisonFailed}` for the application in the last 24 h via the existing audit reader; throws a typed `RateLimitExceededException` (with `windowResetsAt`) when count ≥ `AiComparison:RateLimitPerApp24h` unless `bypassRateLimit && actorRole == Admin`.
- [ ] T072 [US4] Implement `src/FundingPlatform.Application/AiComparison/TokenCapGuard.cs` — pre-flight estimate based on blob byte sizes (~rough chars-per-token + structured payload size); throws `TokenCapExceededException` (with `estimatedTokens`, `cap`, `offendingInput`) when estimate > `AiComparison:TokenCapPerRunInput` unless `bypassTokenCap && actorRole == Admin`. Identifies the offending input (largest blob) for the reviewer-facing message.
- [ ] T073 [US4] Wire both guards into `ComparisonOrchestrator.GenerateAsync` (in the orchestration flow order from contracts/ai-client.md: after the cache-hit short-circuit, before any redaction/provider call). Convert guard exceptions to `GenerateComparisonFailure` records with the right `failureReason` and emit the failure audit event.
- [ ] T074 [US4] Update `AdminAuditEventComparisonFactory` so the audit row reflects `bypassedRateLimit` / `bypassedTokenCap` flags from the command. A second informational `AiComparisonBypassed` event is **not** added; flags on the main event suffice per `contracts/audit-event-payload.md`.

### UI

- [ ] T075 [US4] Modify `src/FundingPlatform.Web/Views/Review/_ComparisonRegion.cshtml` and `Review.cshtml` to render the admin-only **Anular límites** toggle next to the per-item action AND next to the app-level **Generar todo** button. Hide for non-admins.
- [ ] T076 [US4] Update `src/FundingPlatform.Web/wwwroot/js/comparison.js` so the toggle's state is read and included as `bypassRateLimit`/`bypassTokenCap` in the POST body for both per-item and app-level generation requests.
- [ ] T077 [US4] Map the controller's error envelopes to the spec's exact Spanish messages in the JS error handler: `"Límite de generaciones alcanzado para esta solicitud (10/24h). Inténtelo más tarde o contacte un administrador."` for rate-limit, and `"El proveedor X adjuntó un PDF de 50 páginas; pida una versión recortada o ejecute como administrador para anular el límite."` (templated with the offending supplier name + page count) for token-cap.

### Tests

- [ ] T078 [P] [US4] Implement `tests/FundingPlatform.Tests.Unit/AiComparison/RateLimitGuardTests.cs` — 9 events ⇒ pass; 10 events ⇒ throw; 10 + bypass-admin ⇒ pass; non-admin can't bypass.
- [ ] T079 [P] [US4] Implement `tests/FundingPlatform.Tests.Unit/AiComparison/TokenCapGuardTests.cs` — estimate boundary, offending-input identification, admin bypass.
- [ ] T080 [US4] Implement `tests/FundingPlatform.Tests.E2E/AiComparison/AdminBypassTests.cs` — Playwright spec for US4 acceptance scenarios (rate-limit block + reviewer message; admin bypass produces correct audit row with `bypassedRateLimit=true`; token-cap pre-flight block).

**Checkpoint**: US4 functional. Cost guardrails enforced; admin override audited.

---

## Phase 7: User Story 5 — Source-citation links (Priority: P3)

**Goal**: Every cell value and narrative paragraph that derives from a supplier file carries a numeric superscript citation marker. Hover ⇒ tooltip (supplier + file). Click ⇒ open signed URL in new tab.

**Independent Test**: Generate a comparison for an item with two suppliers, each with a PDF. Confirm at least one cell + one paragraph have markers. Click a marker; confirm a new tab opens the originating PDF via a signed URL whose TTL respects spec 014 policy. Cells with no file source show no marker.

### Endpoint + rendering

- [ ] T081 [US5] Add `GET /Review/Citations/{artifactId}/{sourceRefId}` action to `ReviewController` per `contracts/endpoints.md`. Resolves the source ref (`<itemIdx>:<rowOrSectionLocator>:<sourceRefIdx>`) → reads artifact JSON → loads the blob via `IObjectStorage.ResolveServingHandleAsync` → 302 redirect to the signed URL. Group-overlap guard applied.
- [ ] T082 [US5] Modify `_ComparisonRegion.cshtml` to render numeric superscripts: a sequential 1-based counter incremented across the rendered region (per-cell and per-section ordering follows artifact JSON order); markers are `<sup><a href="/Review/Citations/{artifactId}/{sourceRefId}" target="_blank" data-tooltip="...">¹</a></sup>` (or similar); WCAG-compliant keyboard focusable (NFR-A1).
- [ ] T083 [US5] Update `comparison.css` to style the superscript markers (small, blue link, hover state, focus ring).
- [ ] T084 [US5] Update `comparison.js` to bind hover/focus tooltip behavior (tooltip text is `{supplierName} — {fileName}` constructed from the source-ref label).

### Tests

- [ ] T085 [P] [US5] Add a fixture variant to `canned-compare.json` that includes cells/paragraphs with and without `sourceRefs`, ensuring T082 renders markers correctly only where appropriate (this may be a second fixture file under `tests/Fixtures/AiComparison/canned-compare-with-citations.json`).
- [ ] T086 [US5] Implement `tests/FundingPlatform.Tests.E2E/AiComparison/CitationsTests.cs` — Playwright spec: comparison region renders markers on cells/paragraphs with `sourceRefs`; clicking a marker opens a new tab; the URL is a `302` redirect to a signed URL (assert via fetch interception); cells without `sourceRefs` carry no marker; hover surfaces tooltip with supplier + file name.

**Checkpoint**: US5 functional. Reviewers can verify claims against source files inline.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Observability, copy review, accessibility hardening, runbook notes, and constitution-compliance cleanup.

- [ ] T087 Wire structured logging at each pipeline stage (`extract`/`normalize`/`compare`) via the existing `ILogger<T>` channel: stage name, `applicationItemId`, ordered `supplierIds`, latency, token usage (where applicable), outcome. No raw prompts or PII (NFR-O1/O2/NFR-S2). Touch points: `ComparisonOrchestrator`, `AnthropicAiClient`.
- [ ] T088 [P] Verify all Spanish copy in `_ComparisonRegion.cshtml` and `comparison.js` error toasts against the spec verbatim strings; commit as a single copy-review pass.
- [ ] T089 [P] Accessibility pass: confirm comparison table header scoping (`<th scope>`), color is not the sole indicator of "stale" or "cheapest" badges (add iconography or text), citation markers keyboard-focusable, **Mostrar más** toggle on long narratives meets NFR-A2.
- [ ] T090 [P] Add an operational runbook note `docs/runbooks/ai-comparison.md` covering: bumping `AiComparison:SchemaVersion` / `PromptVersion`, Anthropic outage behavior, reaper window, audit-row dashboard query exemplars (lift from `contracts/audit-event-payload.md`).
- [ ] T091 [P] Update `CLAUDE.md` configuration-knobs table to include the new `AiComparison:*` keys (mirror plan.md table).
- [ ] T092 Run `quickstart.md` end-to-end manually (or as part of the E2E sweep) to validate every step before marking the feature delivered.
- [ ] T093 Run the **full** E2E suite (`dotnet test tests/FundingPlatform.Tests.E2E`) and verify a green run; this is the constitution Principle III delivery bar and the CLAUDE.md memory rule.

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (Setup): no deps; start first.
- Phase 2 (Foundational): depends on Phase 1; **blocks every user story**.
- Phase 3 (US1, MVP): depends on Phase 2.
- Phase 4 (US2): depends on Phase 3 (extends the orchestrator + view introduced there).
- Phase 5 (US3): depends on Phase 3 (worker calls into the orchestrator; UI extends the per-item region).
- Phase 6 (US4): depends on Phase 3 (guards plug into the orchestrator + audit flow). Can run in parallel with Phase 4 or Phase 5 if dev capacity allows; they touch different orchestrator hooks and different views/JS spots.
- Phase 7 (US5): depends on Phase 3 (citations render off the artifact view region). Can run in parallel with Phase 4/5/6.
- Phase 8 (Polish): final; depends on all desired user stories.

### Within-story ordering

- Models → repositories → application services → orchestrator wiring → controller → view + JS → integration tests → E2E.
- Tests for a story tagged `[P]` are parallelizable within the story.
- Foundational unit tests (T033–T038) are `[P]` and can be drafted alongside the implementations they target.

### Parallel opportunities

- Setup: T002, T003, T004, T005 are `[P]`.
- Foundational: schema files (T007–T009), domain entities (T010–T012), application abstractions (T013–T018), EF configs (T024–T025), repos (T027–T028), stub client (T030), fixtures (T032) and tests (T033–T038) can fan out in parallel where marked.
- Across stories: once US1 is at checkpoint, US4 (guards) and US5 (citations) can run in parallel with US2/US3 since they touch orthogonal seams.

---

## Parallel Example: Foundational Phase

```bash
# Schema (different files, no in-phase deps):
Task: T007 dbo.ComparisonArtifacts.sql
Task: T008 dbo.ComparisonJobs.sql

# Domain (different files):
Task: T010 ComparisonArtifact.cs
Task: T011 ComparisonJob.cs
Task: T012 FreshnessResult.cs + ChangedInput enum

# Application abstractions (different files):
Task: T013 IAiClient.cs
Task: T014 IPiiRedactor.cs
Task: T015 IComparisonOrchestrator.cs
Task: T016 InputDescriptor.cs
Task: T017 Repository interfaces
```

---

## Implementation Strategy

### MVP first

1. Phase 1 (Setup).
2. Phase 2 (Foundational) — fully landed + unit tests green.
3. Phase 3 (US1) — single-item generation works end-to-end with stubbed `IAiClient` + Playwright E2E green.
4. STOP and validate: run quickstart.md US1 path. Demo. Deploy if useful.

### Incremental delivery after MVP

1. + Phase 4 (US2) — cache + stale UX. Demo.
2. + Phase 5 (US3) — Generar todo + worker. Demo.
3. + Phase 6 (US4) — guardrails + admin bypass. Demo.
4. + Phase 7 (US5) — citations. Demo.
5. Phase 8 (Polish) — observability, copy, accessibility, runbook, full E2E green.

### Notes

- Each user story is independently deliverable: cache (US2) is value even if Generar todo (US3) isn't shipped yet; citations (US5) work on whichever generations the team has so far.
- US3 worker is the only piece that requires Aspire process lifecycle change (registering a new hosted service); plan around any deploy window.
- The Anthropic API key needs to be set in the configured secret store before Phase 3 can be exercised against the real provider. Until then, `AiComparison:Provider=Stub` exercises everything offline.
- Commit at the end of each phase (CLAUDE.md memory rule: every Speckit checkpoint commits + pushes without prompting).
