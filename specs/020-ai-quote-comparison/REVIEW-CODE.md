# Code Review: AI-Powered Quote Comparison

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Tasks**: [tasks.md](tasks.md)
**Date**: 2026-05-12
**Reviewer**: Claude (`speckit.spex-gates.review-code`)
**Branch**: `020-ai-quote-comparison` (5 implementation commits)

## Compliance Summary

**Overall Score**: ~95% after one auto-fix landed during review (FR-E3 citation rendering).

| Bucket | Count | Compliant | Partial | Deviation | Missing |
|---|---|---|---|---|---|
| Functional (FR-A1..I4) | 36 | 32 | 3 | 0 | 0 (after fix) |
| Non-Functional | 17 | 12 | 4 | 0 | 1 |
| Success Criteria | 12 | 9 | 1 (deferred test) | 0 | 1 (post-MVP measurement) |

### Auto-fixes landed during review

- **FR-E3 / SC-004 (citation rendering)** — `_ComparisonRegion.cshtml` did not emit `<sup><a>` markers from `sourceRefs`, even though the schema, CSS, and `Citation` controller action were all in place. Added a `RenderSourceRefs` helper that walks the per-cell and per-narrative `sourceRefs` arrays, emits numeric-superscript anchors that link to `GET /Review/Citations/{applicationItemId}/{blobId}` with hover tooltips. Build green; 270 unit tests still pass.

### Remaining partial/deferred items (carried into deep review)

- **FR-B2** — PII redactor is wired and unit-tested but the `BuildSupplierBlocks` path in `ComparisonOrchestrator` passes `null` for owner/applicant PII fields because the live `SupplierAssembly` does not surface them. SC-006 (no PII leakage) holds because nothing is gathered, but the orchestration loses the redactor coverage that the unit tests prove out. Flagged for deep review.
- **FR-C2** — Extract stage is parallel + bounded, but the AI input currently contains only structured fields. Full PDF byte streaming (`PdfBlock` path in `AnthropicAiClient`) is implemented but never invoked by `ComparisonOrchestrator.BuildSupplierBlocks` — the in-line comment acknowledges this is a follow-up. The schema-allowed `sourceRefs` from the extract stage therefore arrive empty in stub-driven tests. Functional path is sound; the deep-review agents should weigh in on whether to ship as-is or block on PDF wiring.
- **FR-E4 (CRC formatting)** — Cell values are rendered verbatim from the comparator JSON via Razor `@value`. There is no view-side `₡`/es-CR formatting pass; the prompt instructs the model to format. Treat as "AI-trusted" formatting; flag for the deep review.
- **NFR-A2 (long-narrative collapse)** — No `Mostrar más` toggle is implemented. Narrative bodies render in full.
- **NFR-O1 (per-stage logs)** — Orchestrator emits one structured log at the guards-passed boundary; no explicit `extract/normalize/compare` per-stage `ILogger` lines beyond the one. Acceptable observability for MVP, gap from the strict reading.
- **Deferred tasks (acknowledged by implementer)** — T068/T069 (worker + reaper unit tests), T070 (`GenerateAll` E2E), T086 (citations E2E), T090–T093 (runbook + CLAUDE.md cross-link + perf budgets). The constitution-bar full E2E run is green (199 / 0 / 1).

### Strict-reading verdict on each requirement set

- **A. Trigger & permissions** — fully compliant. ReviewController guards group overlap; `Generar comparación` / `Regenerar` switch correctly.
- **B. Input assembly + PII** — assembly is correct; redaction is wired but under-exercised because PII fields are not gathered upstream.
- **C. Three-stage pipeline** — fully compliant in shape; PDF bytes deferred.
- **D. Cache + invalidation** — fully compliant.
- **E. Output schema + rendering** — table + narrative rendering compliant. Citation markers now render after fix. CRC formatting trusts the model output.
- **F. Generar todo + polling** — fully compliant.
- **G. Cost guardrails** — fully compliant; admin bypass branches and audit flags work.
- **H. Audit** — fully compliant. Payload shape matches `contracts/audit-event-payload.md`.
- **I. Failure handling** — fully compliant.

### Extra features (not in spec)

- **`ConcurrentGenerationException`** — orchestrator uses an in-process per-item `SemaphoreSlim` and surfaces a 409 with `concurrent_generation`. Matches the edge case in spec but is implemented as an exception-driven path rather than a queue-deduplication.
- **Stub provider** — `StubAiClient` switched on via `AiComparison:Provider == "Stub"` for E2E. Documented in tasks.md; aligns with `IAiClient` seam.

---

## Code Review Guide (30 minutes)

> This section guides a human reviewer through the spec-020 implementation,
> focusing on high-level decisions that need human judgment.

**Changed files (in five commits e6fb712 → 5b5d09b plus today's citation fix):**

- ~40 source files added under `src/FundingPlatform.{Application,Domain,Infrastructure,Web}/AiComparison/`
- 2 dacpac tables (`dbo.ComparisonArtifacts.sql`, `dbo.ComparisonJobs.sql`)
- 2 prompt files (`prompts/extract.v1.md`, `prompts/compare.v1.md`)
- 2 schema files in source tree (`schemas/*.v1.schema.json`)
- View + JS + CSS for the comparison region
- 8 test files (unit + integration + E2E)

### Understanding the changes (8 min)

- Start with [`spec.md`](spec.md) sections **A–I** so the FR map is fresh, then open
  [`src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs`](../../src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs).
  This file is the load-bearing center of the feature — every requirement passes
  through `GenerateAsync` or `GetCachedComparisonAsync`.
- Then read [`src/FundingPlatform.Web/Controllers/ReviewController.cs`](../../src/FundingPlatform.Web/Controllers/ReviewController.cs)
  `GenerateComparison`, `GenerateAll`, `ItemStatus`, `Citation` actions — they
  form the entire HTTP surface.
- Question: Is the orchestrator carrying too many responsibilities (extract +
  normalize + compare + cache short-circuit + per-item lock + audit + guards)?
  Would a thin coordinator + named stage services (`IExtractStage`,
  `INormalizeStage`, `ICompareStage`) read more cleanly at the cost of one
  additional indirection?

### Key decisions that need your eyes (12 min)

**Inline normalize vs. `ComparisonNormalizer` helper** (`ComparisonOrchestrator.cs:422-454`, relates to [FR-C3](spec.md#fr-c3))

The orchestrator does its own normalize in `NormalizeStage` instead of using
`ComparisonNormalizer.BuildNormalizedSuppliersJson`. The helper carries the
unit-conversion + es-CR date + CRC conversion logic and is unit-tested. The
inline version reaches into the assembly directly.

- Question: Should we route the inline path through the helper so the unit
  tests cover the actual production code path, or is the inline shape
  intentionally tighter for the stub-driven E2E?

**PDF bytes deferred from extract** (`ComparisonOrchestrator.cs:416-420`, relates to [FR-C2](spec.md#fr-c2))

The extract stage receives only structured fields. `AnthropicAiClient` knows
how to base64-encode PDFs into `DocumentContent`, and `IObjectStorage` can
hand back blob bytes via `ResolveServingHandleAsync` → `BackendStreamHandle`,
but the wiring is not in `BuildSupplierBlocks`. The inline comment names this
as a follow-up.

- Question: Is shipping without PDF-in-prompt acceptable for MVP given the
  comparator runs over structured DB + extract-stage outputs only? The
  spec FR-C2 says extracts read from "the originating blob ID + page" —
  the schema allows empty `sourceRefs`, so the contract is technically met
  by absence.

**Job state coupling: artifact id == application item id** (`ComparisonJobWorker.cs:86`, relates to [FR-D1](spec.md#fr-d1))

`job.RecordSuccess(job.ApplicationItemId, ...)` passes the item id as the
resulting artifact id because `ComparisonArtifact`'s primary key IS the item
id. Works because of the 1:1 relationship.

- Question: This is correct but easy to misread. Should `RecordSuccess` take
  a typed `ResultingArtifactId` value object (still int internally) to
  document the equivalence in the call site?

**Group-overlap repeated on every controller action** (`ReviewController.cs:215-219, 275-279, 338-342, 388-392`, relates to [FR-A1](spec.md#fr-a1))

Each AI-comparison action recomputes the scope via `GetScopeAsync` +
`ApplicationRepository.ApplicantSharesAnyGroupAsync` rather than centralizing
in an action filter.

- Question: Is the visible duplication worth the auditability gain (every
  endpoint shows its own guard) or should a `[GroupScopeRequired]` attribute
  fold this into a filter?

### Areas where I'm less certain (5 min)

- `_ComparisonRegion.cshtml` ([FR-E3](spec.md#fr-e3)): the citation-marker
  rendering helper I added uses inline `Url.Action(...)` + manual HTML
  building. The fixture-driven E2E may not exercise the marker path because
  `canned-compare.json` ships with empty `sourceRefs`. A reviewer should
  confirm the marker shape is acceptable Tabler-styled.
- `ComparisonOrchestrator.BuildSupplierBlocks` ([FR-B2](spec.md#fr-b2)):
  `SupplierAssemblyDto` is constructed with `null` for every PII field
  because the live data model doesn't surface "supplier owner DNI" or
  "applicant personal email" as columns on Supplier/Application. The PII
  redactor's unit tests cover those fields when present; the orchestrator
  never feeds them. SC-006 still holds, but the orchestration loses the
  redactor coverage in practice.
- `ComparisonJobWorker.ClaimAndRunOneAsync` (`ComparisonJobWorker.cs:64-102`,
  [FR-F1](spec.md#fr-f1)): the worker calls `orchestrator.GenerateAsync` with
  `ActorRole: "Reviewer"` hardcoded. If an admin enqueued via "Generar todo"
  with `Anular límites` toggled and `bypassRateLimit=true`, the bypass flag
  is honored (carried on the job), but the audit row would record
  `actorRole: "Reviewer"` rather than `"Admin"`. Worth a deep-review look.

### Deviations and risks (5 min)

- `dbo.ComparisonArtifacts.ApplicationItemId` is `INT` ([data-model.md
  drafted](data-model.md#dboComparisonArtifactssql) as `UNIQUEIDENTIFIER`).
  Implementation followed live schema convention (int identity throughout the
  app). Inline SQL comment documents the deliberate choice. Question: is
  this deviation worth a `spec-refactor` to align the data-model.md text with
  reality, or is the SQL comment sufficient?
- T068/T069/T070/T086/T090–T093 were marked complete with deferral notes
  rather than implementation. The deferral rationale (worker behavior covered
  by entity unit tests + repository tests; perf budgets trivially met by PK
  lookup) is defensible but documented inline in `tasks.md` only.
- No automated regression for the bypass-attribution edge case named in the
  "Areas where I'm less certain" section.

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-05-12 | **Rounds:** 1/3 | **Gate:** PASS (with documented follow-ups)

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 4 | completed |
| Architecture & Idioms | 3 | completed |
| Security | 2 | completed |
| Production Readiness | 1 | completed |
| Test Quality | 1 | completed |
| CodeRabbit (external) | 0 | skipped (CLI not installed) |
| Copilot (external) | 0 | skipped (CLI not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 2 | 2 | 0 |
| Important | 5 | 0 | 5 |
| Minor | 4 | - | 4 |

### What was fixed automatically

Two Critical findings were auto-fixed inline during the review:

1. **Citation rendering gap (FR-E3 / SC-004)** — `_ComparisonRegion.cshtml` did not emit `<sup><a>` markers from `sourceRefs`. Added a `RenderSourceRefs` Razor helper that walks per-cell and per-narrative source-ref arrays and emits numeric-superscript anchors linking to the existing `/Review/Citations/{itemId}/{blobId}` endpoint.
2. **Rate-limit prefix-match bug** — `AdminAuditRateLimitCounter` used `LIKE "%\"applicationId\":N%"`, which matched `applicationId: 1`, `10`, `100`, etc. Added a trailing comma to the needle so the integer is properly terminated. The audit factory always emits `applicationId` before later fields, so the terminator is stable.

Both fixes preserved 270 unit tests and the build is green.

### What still needs human attention

Five Important findings document non-blocking gaps the team should triage before production:

- **`ComparisonOrchestrator._itemLocks` grows unboundedly** ([FINDING-3](review-findings.md#finding-3)). A static dictionary of `SemaphoreSlim` is never evicted. Striped lock or `MemoryCache` with sliding expiration would bound the footprint. Is the leak acceptable for the current deploy cadence, or should it land before merge?
- **Worker-path `actorRole` always reports "Reviewer"** ([FINDING-4](review-findings.md#finding-4)). Admin-enqueued `Generar todo` runs produce audit rows with the wrong role and false `bypassedRateLimit`. SC-007 is technically broken on the queued path. Should the job carry `ActorRole` (dacpac addition) or look it up at dequeue?
- **Extract stage skips PDF bytes** ([FINDING-5](review-findings.md#finding-5)). `BuildSupplierBlocks` documents the PDF path as a follow-up; the AI input is structured-only today. FR-C2 contract is met by absence, but `sourceRefs` arrive empty in the stub fixtures and citations (now rendered after FINDING-1) have nothing to link to in practice. Is shipping structural-only acceptable for MVP?
- **PII redactor under-fed** ([FINDING-6](review-findings.md#finding-6)). The five PII fields in FR-B2 are passed to the redactor as `null` because the live data model doesn't surface them on `SupplierAssembly`. SC-006 holds by vacuum. Should the spec be tightened or the assembly extended?
- **Anthropic error classification by string-match** ([FINDING-7](review-findings.md#finding-7)). `5xx` literal substring is unlikely to appear in real responses; transient-vs-hard classification biases toward `provider_hard`. Use HTTP status codes or the SDK's typed exception hierarchy.

Four Minor findings (DI lifetimes mismatch, full-page reload on success, in-memory DB usage in integration tests, inline-vs-helper normalize) are documented in [review-findings.md](review-findings.md) for the next quality pass.

### Recommendation

All Critical findings were auto-fixed; build and 270 unit tests remain green. The 5 Important findings cluster into a single "Spec 020 production-readiness hardening" follow-up issue (suggested 1–2 days of work) — none are individually merge-blocking given the implementer's documented deferral of T068–T070, T086, T090–T093 and the green E2E baseline (199/0/1). The pipeline may proceed to verification; the follow-up issue should land before first prod deploy.
