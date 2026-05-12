# Implementation Plan: AI-Powered Quote Comparison for Reviewers

**Branch**: `020-ai-quote-comparison` | **Date**: 2026-05-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/020-ai-quote-comparison/spec.md`

## Summary

Bring per-item supplier-quotation comparison into the review screen, persisted as a hash-keyed artifact, auditable end-to-end, and grounded in structured DB rows + attached supplier files. Anthropic Claude is the only provider in MVP, hidden behind `IAiClient`. The pipeline is three stages: per-supplier `extract` (parallel AI calls, schema-constrained JSON), pure server-side `normalize` (units, dates, CRC conversion using each quotation's spec-015 snapshot rate), and a single `compare` AI call that emits the artifact JSON (Tabler comparison table + Spanish narrative panels with per-cell source citations). A SHA-256 input hash includes ordered supplier IDs, blob hashes, line state, snapshot IDs, and prompt+schema versions — match → cached, mismatch → stale badge. Cost is gated by a per-application 24h rate limit (default 10) and a per-run input-token cap (default 200,000); admins may bypass per click with the override recorded on the audit event. PII redaction at the boundary is the only path that constructs outbound provider request bodies. "Generar todo" enqueues per-item jobs into a hosted `BackgroundService`-driven worker queue with 3 s polling. All AI-generated copy is es-CR Spanish.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**:
- ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire (existing)
- **Anthropic.SDK** NuGet (new managed dep, approved via spec FR-A-10 / CLAUDE.md). Pinned to latest stable at plan time; transitive supply chain limited to `System.Text.Json` + `HttpClient`-family deps already in the graph.
- Tabler.io vendored CSS/JS (existing)
- Syncfusion HtmlToPdfConverter (existing, irrelevant here)
- JSON Schema validator: `JsonSchema.Net` (already in graph for spec 014 validation seams; reused).

**Storage**:
- SQL Server (Aspire-managed; dacpac-deployed). New tables `dbo.ComparisonArtifacts`, `dbo.ComparisonJobs`. `dbo.AdminAuditEvents.PayloadJson` carries the comparison-specific event shape — no column additions to the audit table.
- Azure Blob Storage (prod) / Azurite (dev+test) / Local filesystem (test fallback) via existing `IObjectStorage` (spec 014). Supplier blobs are read for AI input; no new categories added.

**Testing**:
- Unit (`FundingPlatform.Tests.Unit`): redactor fixture sweep, hash determinism, schema-validation guard, normalizer (units/dates/CRC), domain-entity behavior methods, rate-limit / token-cap guards.
- Integration (`FundingPlatform.Tests.Integration`): real DB; orchestrator end-to-end with `IAiClient` stub returning canned schema-valid responses; reaper marks orphaned jobs; cache stale-detection diff.
- E2E (`FundingPlatform.Tests.E2E`): Playwright + AspireFixture. Stories US1–US5. AI provider is stubbed at the DI seam (`IAiClient` replaced with a fixture client) — no real provider call from E2E.

**Target Platform**: Linux server (containerized via Aspire); browser UI (Tabler.io theme).

**Project Type**: Web application — ASP.NET MVC. Aligns with existing layout (`src/FundingPlatform.{AppHost,Web,Application,Domain,Infrastructure,Database,ServiceDefaults}`).

**Performance Goals** (from spec NFRs):
- Sync per-item generation ≤ 60 s typical, 90 s hard timeout.
- Cached page-load overhead ≤ 100 ms over baseline.
- Hash recompute ≤ 50 ms per item.
- "Generar todo" of 10 stale items completes ≤ 10 min wall-clock at default worker concurrency 2.

**Constraints**:
- es-CR only. No English fallback.
- Anthropic API key from configured secret store; never in `appsettings.json` source.
- All AI input bytes must pass through `IPiiRedactor` before egress. No bypass path.
- Schema-first DB (no EF migrations).
- Aspire orchestration; no separate worker process — `BackgroundService` co-located in `FundingPlatform.Web` process. Aspire AppHost can split it out later as a local change without spec changes.

**Scale/Scope**:
- ~50 items/application worst case (spec edge case). Default worker concurrency 2 ⇒ ~25 × 60 s = 25 min for full app regen.
- Cached read is the dominant case (US2). Designed for ≤ 100 ms overhead with `ComparisonArtifact` row lookup by `ApplicationItemId` primary key (no joins).

## Constitution Check

Constitution v1.0.0 evaluated.

| Principle | Status | Notes |
|---|---|---|
| **I. Clean Architecture** | PASS | Entities + repository interfaces in Domain; `IComparisonOrchestrator`, `IAiClient`, `IPiiRedactor`, command/query DTOs in Application; `AnthropicAiClient`, redactor impl, EF configs, hosted worker in Infrastructure; controllers/views in Web. Dependencies inward only. |
| **II. Rich Domain Model** | PASS (planned) | Behavior methods defined explicitly in this plan (see Domain Model section): `ComparisonArtifact.IsStaleAgainst(InputDescriptor)`, `ComparisonArtifact.ReplaceWith(...)`, `ComparisonJob.Start()`, `ComparisonJob.RecordSuccess(ComparisonArtifactId, TokenCost, latencyMs)`, `ComparisonJob.RecordFailure(FailureReason)`, `ComparisonJob.Reap()`. No anemic models. Validation enforced by entity constructors/methods. |
| **III. E2E Testing (NON-NEGOTIABLE)** | PASS | Each user story US1–US5 has a dedicated Playwright E2E spec. `IAiClient` stub returns canned schema-valid JSON to keep tests deterministic and offline. AspireFixture drives the full stack. |
| **IV. Schema-First DB** | PASS | New tables added as `.sql` files in `FundingPlatform.Database/Tables/` (`dbo.ComparisonArtifacts.sql`, `dbo.ComparisonJobs.sql`). No EF migrations. No column additions to `dbo.AdminAuditEvents`; comparison-specific fields live in the existing `PayloadJson` column. |
| **V. Specification-Driven Development** | PASS | spec.md + this plan + tasks.md follow the workflow. Stories independently testable. Out-of-scope items deferred explicitly. |
| **VI. Simplicity & Progressive Complexity** | PASS | Single new abstraction (`IAiClient`) justified by NFR-M1 (concrete second-provider use case). No streaming, no embeddings, no multi-provider routing, no history table, no SignalR, no cost-rollup dashboard — all explicitly deferred. Defaults provided for every configuration knob. |

**Violations**: none.

## Decisions Locked This Plan (Open Question Reconfirmation)

The spec deferred 8 open questions (A-1..A-8) for plan-time reconfirmation. All are confirmed unchanged:

| Open Q | Locked Decision | Justification |
|---|---|---|
| **A-1 / OQ-001** image-only PDFs | Refuse with `envíe un PDF con capa de texto`. No OCR path in MVP. | OCR doubles MVP surface (pre-pass + redactor that handles image text + new failure modes); the typical supplier PDF has a text layer. Defer OCR to a future spec when the refusal-rate justifies it. |
| **A-2 / OQ-002** model picks | **Extract**: `claude-sonnet-4-6`. **Compare**: `claude-opus-4-7`. Both configurable. | Extract is parallel + per-supplier and benefits from Sonnet's price/throughput. Compare is a single call over already-normalized payloads and benefits from Opus reasoning. Cost estimate against fixture: ~$0.40-0.60 per item full pipeline at 2-4 suppliers, ~10MB attachments — well under the planned per-app budget. |
| **A-3 / OQ-003** spreadsheets | Deferred. `.xlsx`/`.csv` ⇒ run fails with `unsupported_format`. No conversion in MVP. | Conversion infra is non-trivial and would need its own normalizer + test fixture set. Refusal cleanly bounds MVP scope. |
| **A-4 / OQ-004** polling vs SignalR | Polling at default 3 s. No SignalR in MVP. Swap is a local change (polling endpoint + small JS update). | Aspire+SignalR adds connection lifecycle, scale-out fanout, and reconnect logic. Polling 3 s for 50-item worst case = 16 RPS peak from a single reviewer browser — trivial. |
| **A-5 / OQ-005** citation style | Numeric superscript markers (e.g. `¹`, `²`). Hover ⇒ tooltip naming supplier + file. Click ⇒ open signed URL in new tab. Keyboard focusable per NFR-A1. | Mimics the source image reference (`brainstorm/seeds/image (1).png`). Two-mode interaction satisfies reviewer trust loop (preview before commit) and accessibility requirement. |
| **A-6 / OQ-006** DB-vs-file reconciliation | Both values flow to the comparator with a discrepancy flag. Narrative names the discrepancy explicitly. | Silent winner risks reviewer acting on the wrong value. Surfacing the discrepancy is cheap (one extra cell + narrative sentence) and preserves trust. |
| **A-7 / OQ-007** "Forzar regeneración total" | Two-step. Admin must toggle **Anular límites** then click **Forzar regeneración total**. | Single-click composite makes accidental over-regeneration too easy (re-renders the whole application at AI cost). |
| **A-8 / OQ-008** cost dashboard | Out of MVP. Audit row carries `applicationId`, `actorUserId`, `programId` (via `applicationId` join), `applicationItemId`, `occurredAt`, `tokenCostInput`, `tokenCostOutput`. Future dashboard is queryable directly from `dbo.AdminAuditEvents` JOIN `dbo.Applications` for program rollup. | FR-H3 promises the audit shape suffices. Confirmed: every dashboard dimension (app, program, reviewer, time) is reachable from existing rows + payload. |

## SC-012 Measurement Protocol

**Baseline definition**: For each of 3 reviewers, record wall-clock time on 5 multi-supplier items using the current ChatGPT round-trip flow. Wall-clock = first click on supplier file in the platform until the reviewer commits a `selectedSupplierId` for the item.

**Post-feature**: Same 3 reviewers, same items (fresh review session 1 week later to avoid recall bias), using `Generar comparación` only. Same wall-clock definition.

**Metric**: `mean(baseline_seconds_per_item) - mean(post_seconds_per_item)) / mean(baseline_seconds_per_item) >= 0.70`.

**Owner**: feature lead. **When**: 2 weeks after first prod deploy. **Recorded in**: a one-page report under `docs/measurements/sc-012-quote-comparison.md` (created post-ship; not a tasks.md artifact).

## Project Structure

### Documentation (this feature)

```text
specs/020-ai-quote-comparison/
├── spec.md                  # source of truth (locked at /speckit-specify)
├── plan.md                  # THIS FILE
├── research.md              # Phase 0 output (open-question reconfirmation summary)
├── data-model.md            # Phase 1 output (entities, behavior methods, schema sketches)
├── quickstart.md            # Phase 1 output (how to run + verify locally)
├── contracts/               # Phase 1 output (HTTP endpoints, JSON schemas)
│   ├── ComparisonArtifact.v1.schema.json
│   ├── endpoints.md         # Web routes spec 020 adds
│   └── ai-client.md         # IAiClient + IPiiRedactor + IComparisonOrchestrator boundary contracts
├── checklists/              # pre-existing
├── review_brief.md          # pre-existing
├── REVIEW-SPEC.md           # pre-existing (SOUND)
└── tasks.md                 # /speckit-tasks output (next stage)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   └── Entities/
│       ├── ComparisonArtifact.cs            # NEW — aggregate root keyed by ApplicationItemId
│       └── ComparisonJob.cs                 # NEW — aggregate root, state machine
│
├── FundingPlatform.Application/
│   ├── Abstractions/
│   │   └── AiComparison/
│   │       ├── IAiClient.cs                 # NEW — provider-agnostic seam
│   │       ├── IPiiRedactor.cs              # NEW — boundary contract
│   │       ├── IComparisonOrchestrator.cs   # NEW — single orchestration entry point
│   │       ├── InputDescriptor.cs           # NEW — value object that feeds the hash
│   │       ├── ComparisonArtifactJson.cs    # NEW — DTO mirroring schema v1
│   │       └── ComparisonJobStatus.cs       # NEW — enum + status DTO
│   ├── AiComparison/
│   │   ├── ComparisonOrchestrator.cs        # NEW — extract → normalize → compare
│   │   ├── ComparisonNormalizer.cs          # NEW — pure server-side normalize
│   │   ├── InputHasher.cs                   # NEW — canonical-json SHA-256
│   │   ├── RateLimitGuard.cs                # NEW — 24h rolling per-app
│   │   ├── TokenCapGuard.cs                 # NEW — pre-flight estimate
│   │   └── Commands/
│   │       ├── GenerateComparisonCommand.cs # NEW
│   │       ├── EnqueueGenerateAllCommand.cs # NEW
│   │       └── GetItemStatusQuery.cs        # NEW
│   └── Reviewing/
│       └── ItemCardProjection.cs            # MODIFIED — add ComparisonArtifact + freshness to item-card projection
│
├── FundingPlatform.Infrastructure/
│   ├── AiComparison/
│   │   ├── Anthropic/
│   │   │   ├── AnthropicAiClient.cs         # NEW — IAiClient impl via Anthropic.SDK
│   │   │   └── AnthropicPromptCatalog.cs    # NEW — versioned prompt strings + schema refs
│   │   ├── Redaction/
│   │   │   ├── PiiRedactor.cs               # NEW — IPiiRedactor impl, deterministic + unit-tested
│   │   │   └── Patterns/                    # cédula / phone / email regexes
│   │   ├── ComparisonJobWorker.cs           # NEW — BackgroundService draining ComparisonJobs queue
│   │   └── ComparisonJobReaper.cs           # NEW — startup-time orphan reaper
│   └── Persistence/
│       ├── Configurations/
│       │   ├── ComparisonArtifactConfiguration.cs   # NEW — EF entity config
│       │   └── ComparisonJobConfiguration.cs        # NEW
│       └── Repositories/
│           ├── ComparisonArtifactRepository.cs      # NEW
│           └── ComparisonJobRepository.cs           # NEW
│
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   ├── ReviewController.cs              # MODIFIED — POST /Review/GenerateComparison/{itemId}, POST /Review/GenerateAll/{applicationId}, GET /Review/ItemStatus/{itemId} (poll endpoint)
│   │   └── AdminController.cs               # MODIFIED — Anular límites toggle is a request-time flag, not new admin route
│   ├── Views/
│   │   └── Review/
│   │       ├── Review.cshtml                # MODIFIED — render the comparison region per item
│   │       └── _ComparisonRegion.cshtml     # NEW — table + narrative panel + citation markers
│   ├── ViewModels/
│   │   └── Review/
│   │       └── ItemComparisonViewModel.cs   # NEW — projection of ComparisonArtifactJson + freshness + status
│   └── wwwroot/
│       ├── css/comparison.css               # NEW — comparison-region styles (consistent with Tabler tokens)
│       └── js/comparison.js                 # NEW — poll + regen handlers; no framework
│
├── FundingPlatform.AppHost/
│   └── AppHost.cs                           # MODIFIED — register new config keys, wire to web env
│
├── FundingPlatform.Database/
│   ├── Tables/
│   │   ├── dbo.ComparisonArtifacts.sql      # NEW
│   │   └── dbo.ComparisonJobs.sql           # NEW
│   └── Scripts/
│       └── (no seed data needed; cache populated on first generation)
│
└── FundingPlatform.ServiceDefaults/         # no changes

tests/
├── FundingPlatform.Tests.Unit/
│   ├── AiComparison/
│   │   ├── InputHasherTests.cs              # determinism + canonical-json invariants
│   │   ├── PiiRedactorTests.cs              # fixture sweep (SC-006)
│   │   ├── ComparisonNormalizerTests.cs     # units, dates, CRC
│   │   ├── RateLimitGuardTests.cs           # 10/24h boundary + admin bypass flag
│   │   ├── TokenCapGuardTests.cs            # pre-flight reject before any IAiClient call
│   │   └── SchemaValidationTests.cs         # schema_invalid fails the run cleanly
│   └── Domain/
│       ├── ComparisonArtifactBehaviorTests.cs   # IsStaleAgainst, ReplaceWith
│       └── ComparisonJobBehaviorTests.cs        # Start/RecordSuccess/RecordFailure/Reap state machine
├── FundingPlatform.Tests.Integration/
│   ├── ComparisonOrchestratorIntegrationTests.cs    # full extract→normalize→compare via stubbed IAiClient
│   ├── ComparisonCacheStaleDiffTests.cs             # hash diff names the changed input
│   ├── ComparisonJobWorkerTests.cs                  # dequeue + status transitions
│   └── ComparisonJobReaperTests.cs                  # orphan Running > 5 min ⇒ Failed/worker_crashed
├── FundingPlatform.Tests.E2E/
│   └── AiComparison/
│       ├── GenerateComparisonTests.cs       # US1
│       ├── CacheFreshAndStaleTests.cs       # US2
│       ├── GenerateAllTests.cs              # US3
│       ├── AdminBypassTests.cs              # US4
│       └── CitationsTests.cs                # US5
└── Fixtures/                                # add canned IAiClient responses + sample supplier PDFs (text-layer + image-only refusal)

prompts/                                     # NEW — checked into source tree per NFR-M2
├── extract.v1.md
└── compare.v1.md

schemas/                                     # NEW — checked into source tree per NFR-M2
├── ExtractedSupplierOffering.v1.schema.json
└── ComparisonArtifact.v1.schema.json
```

**Structure Decision**: Web application layout already in place. No new top-level projects. `prompts/` and `schemas/` are checked-in source artifacts referenced from `AnthropicPromptCatalog` and the schema validator — they version with code (NFR-M2).

## Domain Model (Principle II — Rich Domain Model)

### `ComparisonArtifact` (aggregate root)

**Identity**: `ApplicationItemId` (Guid, primary key — one cached artifact per item).

**State** (private setters):
- `JsonContent : string` — schema-validated `ComparisonArtifact.v1.json`
- `InputHash : string` — SHA-256 hex
- `PromptVersion : string`, `SchemaVersion : string`, `AiModel : string`
- `GeneratedAt : DateTimeOffset`, `GeneratedByUserId : string`
- `TokenCostInput : int`, `TokenCostOutput : int`, `LatencyMs : int`

**Behavior methods**:
- `IsStaleAgainst(InputDescriptor descriptor) : FreshnessResult` — recomputes hash from live descriptor; returns `{IsFresh, ChangedInputs[]}`. `ChangedInputs` enumerates what diverged (file_added, file_removed, line_edited, supplier_added, supplier_removed, snapshot_changed, schema_bumped).
- `ReplaceWith(string json, string inputHash, string promptVersion, string schemaVersion, string model, string userId, int tokenIn, int tokenOut, int latencyMs)` — invariant-guarded replace; rejects empty hash / mismatched schema version / negative tokens.

**Invariants**:
- `JsonContent` is non-empty and schema-valid (validation in constructor and `ReplaceWith`).
- `InputHash` matches `^[a-f0-9]{64}$`.

### `ComparisonJob` (aggregate root)

**Identity**: `Id` (Guid).

**State**:
- `ApplicationItemId`, `RequestedByUserId`, `Status` (enum: `Pending` | `Running` | `Completed` | `Failed`)
- `BypassedRateLimit : bool`, `BypassedTokenCap : bool`
- `LastStatusChangeAt : DateTimeOffset`
- `ResultingArtifactId : Guid?` (set on success)
- `FailureReason : string?` (set on failure)
- `StartedAt : DateTimeOffset?`, `FinishedAt : DateTimeOffset?`

**Behavior methods**:
- Static factory: `ComparisonJob.Enqueue(applicationItemId, requestedByUserId, bool bypassedRateLimit, bool bypassedTokenCap, IClock clock)`.
- `Start(IClock clock)` — transitions `Pending → Running`; rejects if not `Pending`.
- `RecordSuccess(Guid artifactId, int tokenIn, int tokenOut, int latencyMs, IClock clock)` — transitions `Running → Completed`; rejects if not `Running`.
- `RecordFailure(string failureReason, IClock clock)` — transitions `Running → Failed` (or `Pending → Failed` when guard-rejected pre-flight); rejects on Completed/Failed.
- `Reap(IClock clock)` — transitions `Running → Failed` with `failureReason = "worker_crashed"` iff `LastStatusChangeAt < clock.Now - 5 min`.

**Invariants**:
- State machine guarded; illegal transitions throw `InvalidOperationException` with descriptive message.
- `ResultingArtifactId` set ⟹ `Status == Completed`.
- `FailureReason` set ⟹ `Status == Failed`.

### `InputDescriptor` (Application value object)

Pure data carrier; the input to `InputHasher.Compute(InputDescriptor) → string` (SHA-256 of canonical JSON):
- `ApplicationItemId`
- `OrderedSupplierIds : Guid[]`
- `OrderedBranchIds : Guid[]`
- `BlobHashes : (BlobId, ContentHash)[]` (already available from spec 014 storage handle)
- `LineState : (QuotationLineId, Quantity, UnitPrice, CurrencyCode, ExchangeRateSnapshotId)[]`
- `PromptVersion : string`, `SchemaVersion : string`

Canonical JSON: keys sorted; arrays in the declared order; ints as ints, strings as strings; SHA-256 hex lower-case.

## Phase 0: Research Output

`research.md` summarizes the open-question reconfirmation (already captured under **Decisions Locked** above). No outstanding `NEEDS CLARIFICATION`.

## Phase 1: Design & Contracts

### Data model

`data-model.md` documents:
1. The two new entities above (state, invariants, behavior methods).
2. Schema sketches:
   - `dbo.ComparisonArtifacts(ApplicationItemId PK, JsonContent NVARCHAR(MAX), InputHash CHAR(64), PromptVersion, SchemaVersion, AiModel, GeneratedAt, GeneratedByUserId, TokenCostInput, TokenCostOutput, LatencyMs)`.
   - `dbo.ComparisonJobs(Id PK, ApplicationItemId, RequestedByUserId, Status, BypassedRateLimit, BypassedTokenCap, LastStatusChangeAt, ResultingArtifactId NULL, FailureReason NULL, StartedAt NULL, FinishedAt NULL)`. Composite index `(ApplicationId, Status)` via projection on `ApplicationItemId` join + `(Status, LastStatusChangeAt)` for reaper.
3. Reuse of `dbo.AdminAuditEvents` unchanged. Comparison-specific payload schema documented under `contracts/audit-event-payload.md`.

### Contracts (`contracts/`)

- **`ComparisonArtifact.v1.schema.json`** — the JSON Schema the comparator output must validate against. Drives `JsonSchema.Net` validation in `ComparisonOrchestrator`.
- **`ExtractedSupplierOffering.v1.schema.json`** — the per-supplier extract output. Drives schema-constrained output for the extract AI call.
- **`endpoints.md`** — HTTP surface added by the feature:
  - `POST /Review/GenerateComparison/{applicationItemId}` (sync; 60 s; admin override flag in body).
  - `POST /Review/GenerateAll/{applicationId}` (enqueues jobs; admin override flag in body; `Forzar regeneración total` is a separate flag).
  - `GET /Review/ItemStatus/{applicationItemId}` (poll endpoint; returns `{status, freshness, lastUpdatedAt}`).
  - `GET /Review/Citations/{artifactId}/{sourceRefId}` (resolves a citation source-ref to a signed URL using existing storage handle flow).
- **`ai-client.md`** — `IAiClient`, `IPiiRedactor`, `IComparisonOrchestrator` method signatures + invariants + concrete-impl contracts (Anthropic prompt+schema choices, retry policy = none).

### Quickstart

`quickstart.md` documents:
1. Local config knobs to set (Anthropic key via user-secrets; full table mirrored from spec).
2. How to seed an application with 2 suppliers via existing test seeders.
3. How to verify US1 manually: click `Generar comparación`, expect comparison region within 60 s.
4. How to swap to the stub `IAiClient` for offline development.

### Agent context update

Replace the plan reference between `<!-- SPECKIT START -->` and `<!-- SPECKIT END -->` markers in `CLAUDE.md` with `specs/020-ai-quote-comparison/plan.md`.

## Configuration Knobs (new keys in AppHost)

| Key | Default | Notes |
|---|---|---|
| `AiComparison:Provider` | `Anthropic` | Currently only valid value. Forward-looking knob. |
| `AiComparison:Anthropic:ApiKey` | (from secret store; required outside `Development`) | Never in `appsettings.json`. |
| `AiComparison:Anthropic:ExtractModel` | `claude-sonnet-4-6` | Configurable per A-2. |
| `AiComparison:Anthropic:CompareModel` | `claude-opus-4-7` | Configurable per A-2. |
| `AiComparison:Anthropic:BaseUrl` | (Anthropic default) | Override for testing / proxy. |
| `AiComparison:ExtractConcurrency` | `4` | Per-supplier parallelism in extract stage. |
| `AiComparison:WorkerConcurrency` | `2` | `BackgroundService` parallel jobs (FR-F4). |
| `AiComparison:PollIntervalSeconds` | `3` | Generate-all polling cadence (FR-F2). |
| `AiComparison:SyncHardTimeoutSeconds` | `90` | Per-item sync request hard ceiling (NFR-P1). |
| `AiComparison:RateLimitPerApp24h` | `10` | FR-G1. |
| `AiComparison:TokenCapPerRunInput` | `200000` | FR-G2 pre-flight estimate. |
| `AiComparison:OrphanReapAfterMinutes` | `5` | Edge case: worker crash. |
| `AiComparison:PromptVersion` | `2026-05-11` | Surrogate version stamp; bumped when prompt files change. |
| `AiComparison:SchemaVersion` | `v1` | Bumped with schema file changes; part of input hash. |

All wired through `AppHost.cs` `WithEnvironment("AiComparison__...")`.

## Complexity Tracking

No constitution violations. No complexity items.

## Phase 2 Hand-off

`/speckit-tasks` consumes this plan plus the spec to produce `tasks.md`. Expected high-level task ordering:

1. Foundational (schemas, prompts, dacpac tables, `IAiClient` + `IPiiRedactor` + `IComparisonOrchestrator` contracts).
2. US1 (P1): per-item generation end-to-end — orchestrator + Anthropic client + view region + ReviewController endpoint + E2E.
3. US2 (P1): cache + stale-detection — `InputHasher`, `IsStaleAgainst`, stale badge in view + E2E.
4. US3 (P2): `Generar todo` — `ComparisonJob`, worker, polling endpoint + JS + E2E.
5. US4 (P2): rate limit + token cap + admin bypass — guards + view toggle + audit + E2E.
6. US5 (P3): citations — `Citations` endpoint + superscript markers + tooltip JS + E2E.
7. Cross-cutting: audit event payload shape, reaper, observability logs.
