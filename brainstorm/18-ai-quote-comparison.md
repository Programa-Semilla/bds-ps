# Brainstorm: AI-Powered Quote Comparison for Reviewers

**Date:** 2026-05-11
**Status:** spec-created
**Spec:** specs/020-ai-quote-comparison/

## Problem Framing

Reviewers comparing supplier quotations on a multi-supplier item today must download each attached file (PDFs, images, occasionally spreadsheets), upload them into ChatGPT with a hand-curated prompt, read the comparison there, then return to the platform to act on it. The round-trip costs minutes per item, varies in quality across reviewers, leaks vendor-sensitive content into a third party with no audit trail, and produces a comparison that is invisible to every other reviewer or admin who later opens the same application.

The brainstorm scoped a first-shipped version that brings this AI comparison **into the review screen** as a persisted, hash-keyed artefact reusable across reviewers, while staying compatible with the existing supplier catalog (013), multi-currency snapshots (015), object storage (014), group-overlap authorization (016), and admin audit (016).

The seed prompt (`brainstorm/seeds/claude_code_speckit_brainstorm_prompt.md`) covered roughly six months of work (multi-provider routing, OCR pipeline, embeddings, multi-agent orchestration, dashboards, full automation). Decomposition into MVP + future specs was the first move.

## Approaches Considered

### A: Thin slice — single-shot prompt + ephemeral output
- **Pros:** Cheapest to build; fastest first ship; validates UX before investing in cache or schema; minimal surface area.
- **Cons:** Hallucination risk highest (no extraction grounding); cost balloons (every re-view = a new AI call); no programmatic access to the result; reviewers cannot trust unverified single-shot output on commercial fields.
- **Outcome:** rejected — the headline value depends on the comparison being trustworthy *and* reusable.

### B: Thin slice + IComparisonProvider seam
- **Pros:** Same minimal scope as A but with a swap point for future providers.
- **Cons:** Still single-shot; still ephemeral; defers the hard problem (hallucination control) without addressing it.
- **Outcome:** rejected — abstraction without addressing the trust problem ships the wrong MVP.

### C: Full pipeline (extract → normalize → compare) with hash cache and structured output **(chosen)**
- **Pros:** Per-supplier extraction grounded in structured DB rows + attached files = lowest hallucination floor; normalization step is pure server-side (no AI cost for the unit/date/currency reconciliation); structured output with flexible attribute rows + narrative blocks + per-cell source citations lets the table evolve item-by-item without schema rework; hash-keyed cache makes re-views free, makes "stale" computable rather than guessed, and makes "Generar todo" cheap when most items are already fresh.
- **Cons:** Larger MVP than A/B; needs the JSON schema, the redactor, the cache key scheme, the polling endpoint, and the worker queue all in the first ship.
- **Outcome:** chosen — these costs are the things future specs would have had to retrofit anyway; building them up-front avoids two rebuilds.

### D: Defer the cache, ship ephemeral first
- **Pros:** Smaller first ship; validates UX before cache logic.
- **Cons:** Cost story unbounded; reviewer trust in the comparison depends on freshness signals that the cache alone can produce.
- **Outcome:** rejected — the cache *is* the trust mechanism, not an optimization.

## Sub-decisions Locked Through Brainstorm Q&A

| Dimension | Choice | Notes |
|---|---|---|
| AI provider | Anthropic Claude direct + `IAiClient` seam (single provider in MVP) | Sonnet 4.6 default for extract, Opus 4.7 for compare; both configurable; final pick reconfirmed at plan time. |
| AI input | Hybrid: structured DB rows + attached blobs (Claude PDF/vision native) | Anchors hallucinations to authoritative DB values; no separate OCR pipeline in MVP. |
| Persistence | Hash-keyed cache, auto-invalidate on input change, latest-only (no history table) | Hash includes ordered supplier IDs, file blob hashes, line state, currency snapshot ID, prompt version, schema version. |
| Output shape | Structured JSON with **flexible attribute rows** + narrative blocks + per-cell source citations → Tabler comparison table + analysis panel | Driven by the `Ficha 3` reference image (`brainstorm/seeds/image (1).png`): variable rows (Material, Diseño, Compatibilidad, Resistencia, Garantía, Peso…) + analytical sections (Sistemas de Marca, Mecanismo de Sujeción, Plazos de Respaldo, Análisis de Costos, Logística y Ubicación) + numeric superscript citations. |
| Trigger | Reviewer + admin per-item; app-level "Generar todo" enqueues per-item jobs (skips cached unless admin "Forzar regeneración total") | Group-overlap authorization piggybacks spec 016. |
| Cost guardrails | Per-app rate limit (default 10 / 24 h) + per-run token cap (default 200,000 input tokens) | Admin "Anular límites" toggle bypasses both per-click; recorded on the audit event. |
| Data posture | Anthropic API zero-retention default + PII redactor at the boundary | Redacted set in MVP: cédula, applicant phone, applicant email, supplier owner DNI, supplier owner phone. |
| Failure UX | Hard fail with manual retry; cache stays visible; no auto-retry | Predictable + simpler than auto-retry; reviewers see what failed and why. |
| Audit | Reuse existing `AdminAuditEvent` (spec 016); hashes + structured metadata only; no raw I/O retention | Audit shape carries the keys a future cost-rollup dashboard needs. |
| Orchestration | Sync per-item HTTP request (60 s typical / 90 s hard timeout); app-level "Generar todo" → background worker queue + 3 s polling | No SignalR in MVP; switching is a local change later. |
| Scope unit | Per `ApplicationItem` ("Ficha"); never whole-application aggregated | Each item independently invalidates and regenerates. |
| Locale | es-CR Spanish, no English fallback | Per spec 012. |

## Decision

Ship spec 020 with the **full pipeline (C) + Anthropic + IAiClient seam** path. Manual reviewer/admin trigger only; no auto-generation on submission. Streaming, multi-provider routing, embeddings, history tables, cost-rollup dashboard, and SignalR are explicit future specs.

Pre-production status (Assumption A-9) lets the team edit the dacpac directly for `dbo.ComparisonArtifacts` and `dbo.ComparisonJobs` and bump the JSON schema version freely; cached artefacts can be invalidated as needed.

## Open Threads

- Image-only PDF strategy — refuse with "envíe un PDF con capa de texto" message vs. introduce an OCR-then-redact pre-pass (A-1 / OQ-001).
- Final model picks — Sonnet 4.6 extract + Opus 4.7 compare default; reconsider after a token-cost estimate against a sample application (A-2 / OQ-002).
- Spreadsheet (.xlsx/.csv) ingestion in MVP — currently deferred; reconfirm during plan whether basic text conversion belongs in MVP (A-3 / OQ-003).
- Polling vs. SignalR for "Generar todo" — polling chosen for MVP; reconfirm at plan time once Aspire+SignalR overhead is measured (A-4 / OQ-004).
- Citation marker style — numeric superscripts mimicking the source image; final visual + interaction (hover preview vs. click-through) deferred to design pass during plan (A-5 / OQ-005).
- DB-vs-file discrepancy reconciliation — default is "comparator gets both values + flags it"; alternatives are "DB wins" or "file wins" silently (A-6 / OQ-006).
- "Forzar regeneración total" UX placement — two-step (toggle Override → click Generate all) chosen; single-click composite admin action rejected as too easy to mis-fire (A-7 / OQ-007).
- Token-cost dashboard scope — out of MVP; FR-H3 promises the audit shape supports it; confirm aggregation dimensions at plan time (A-8 / OQ-008).
- SC-012 measurement protocol — define how the 70 % task-time reduction is measured (sample selection, who runs it, baseline definition) during plan.
- Domain behaviour methods on `ComparisonArtifact` and `ComparisonJob` (`IsStaleAgainst(InputDescriptor)`, `Reap()`, `RecordSuccess(...)`, `RecordFailure(...)`) to satisfy Constitution Principle II (Rich Domain Model) — flagged in REVIEW-SPEC.md.
- History table for compliance — does the team need an append-only audit trail of every AI output beyond the latest cached artefact, or is "latest only" acceptable forever?
- Redaction list completeness — should the deny-list expand to banking info, CCSS account numbers, and fiscal IDs of third parties before MVP ships, or stay at the 5 fields and revisit?
- Multi-provider posture — any near-term need for OpenAI / Azure / Gemini (data residency, cost, customer requirement) that would push multi-provider into MVP?
- Schema-first DB project: new entities `ComparisonArtifact` and `ComparisonJob` will be edited directly into the dacpac; pin during planning whether composite indexes on `(ApplicationItemId)` and `(ApplicationId, Status)` are sufficient or whether a covering index on the polling read path is also needed.
- Anthropic.SDK NuGet package as a new managed dependency: this spec is the approval vehicle per CLAUDE.md; reconfirm at plan time the exact version pin and any transitive supply-chain notes.
