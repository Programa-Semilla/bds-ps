# Review Guide: AI-Powered Quote Comparison for Reviewers

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-12

---

## What This Spec Does

Today, when a reviewer has to compare two or more supplier quotations on the same line item ("Ficha"), they leave the platform, paste the supplier PDFs into ChatGPT, and bring a hand-edited table back into their decision. This spec brings that workflow inside the review screen: one click yields a Tabler comparison table plus Spanish-narrative analysis sections, all grounded in the structured DB rows + attached supplier files, cached by an input hash so the second reviewer sees it instantly, audited end-to-end, and gated by a per-application rate limit + per-run token cap.

**In scope:** per-item synchronous generation (US1), hash-keyed cache with auto-staleness (US2), application-level "Generar todo" background queue with polling (US3), admin bypass of rate/token guards with audit (US4), per-cell source citations to the originating PDFs (US5). Anthropic Claude is the only provider, behind `IAiClient`. PII redaction happens at the egress boundary. All AI output is es-CR Spanish.

**Out of scope (explicit):** OCR for image-only PDFs (refuse with a Spanish message), spreadsheet ingestion (`unsupported_format`), SignalR (polling instead), a history table (in-place replace only), cross-application comparisons, multi-provider routing, the cost-rollup dashboard itself (the audit row carries the dimensions for a future spec). The 70%-time-reduction target ([SC-012](spec.md#measurable-outcomes)) is measured post-deploy, not as an implementation task.

## Bigger Picture

This is the first feature in this codebase that calls an external LLM. The plan treats that gravity carefully: a deterministic PII redactor sits in front of every byte that leaves the process ([NFR-S1](spec.md#security)), prompts and JSON schemas are checked-in source files versioned in lock-step with the cache key ([NFR-M2](spec.md#maintainability)), and the audit row records hashes + token costs but never the raw payload ([FR-H2](spec.md#h-audit)). The shape sets the pattern for future AI features in the platform.

It also leans on the prior six months of spec work: spec 013 (`Supplier` / `SupplierBranch`), spec 014 (`IObjectStorage` for blob fetch + signed citation URLs), spec 015 (per-quotation exchange-rate snapshot used for the cache hash AND for CRC display), spec 016 (group-overlap predicate on every endpoint + the `AdminAuditEvent` table this feature reuses unchanged). Each integration is named in [research.md](research.md#integration-anchor-confirmation).

The `Anthropic.SDK` NuGet is a new managed dependency; this spec is its approval vehicle per CLAUDE.md ([A-10](spec.md#assumptions)). The plan justifies it under [research.md "Decision: Anthropic.SDK"](research.md#decision-anthropicsdk-nuget-new-managed-dependency). Anthropic's `.NET SDK` does support tool-use / JSON-mode for schema-constrained output and native PDF/Vision ingestion, which is what the extract stage relies on — worth a brief glance at the SDK docs if you want to sanity-check the extract path.

---

## Spec Review Guide (30 minutes)

> Each section points to specific sections + frames the review as questions.

### Understanding the approach (8 min)

Read [plan.md "Summary"](plan.md#summary) and the [Orchestration flow](contracts/ai-client.md#icomparisonorchestrator-application-abstraction) (steps 1–13 in `contracts/ai-client.md`). As you read, consider:

- The pipeline is `extract → normalize → compare`. Extract is one AI call per supplier in parallel (default concurrency 4); compare is a single AI call over the normalized array. Does the [extract/compare split](spec.md#c-three-stage-pipeline) feel right, or would a single-shot prompt covering all suppliers be simpler at this scale?
- The cache key is `sha256(canonical_json(input_descriptor))` including supplier IDs, branch IDs, blob content hashes, line state, snapshot IDs, prompt version, and schema version ([FR-D2](spec.md#d-cache--invalidation)). Anything you'd add or remove?
- All AI bytes pass through `IPiiRedactor`. The plan ([T023](tasks.md)) limits MVP redaction to regex + structured-field redaction across 5 enumerated fields. Is that boundary tight enough for the supplier docs you've actually seen in production?

### Key decisions that need your eyes (12 min)

**Model picks: Sonnet 4.6 for extract, Opus 4.7 for compare** ([plan.md "Decisions Locked"](plan.md#decisions-locked-this-plan-open-question-reconfirmation), row A-2)

The plan cites a fixture cost estimate of ~$0.40–0.60 per item full pipeline at 2–4 suppliers + ~10 MB of attachments. Both models are configurable.
- Question for reviewer: at the projected steady-state volume (how many items × suppliers per week?), is $0.40–0.60/item × `RateLimitPerApp24h=10` × app count an acceptable monthly spend? If not, is Sonnet-only across both stages worth a fixture re-test?

**In-place replace, no history table** ([FR-D4](spec.md#d-cache--invalidation))

Every regen overwrites the previous artifact. Token costs + latency are recorded on the audit row; the artifact JSON itself is not retained per generation.
- Question for reviewer: if a reviewer disputes a comparison's analysis a week later, all we have is the latest one + the audit metadata. Is that the right trade-off for MVP, or does keeping the prior JSON until the next overwrite earn its keep cheaply?

**Polling at default 3 s, no SignalR** ([A-4](spec.md#assumptions), [FR-F2](spec.md#f-generar-todo--polling))

50-item worst case ⇒ ~16 RPS peak from one reviewer browser. Plan notes SignalR can swap in later as a local change.
- Question for reviewer: does a single reviewer holding 16 RPS open against `/Review/ItemStatus/{id}` for ~25 min raise any noise on the existing reverse-proxy / log-volume side?

**Admin override is two-step** ([A-7](spec.md#assumptions), [T066](tasks.md))

Admin must toggle **Anular límites** first, then click **Forzar regeneración total**, to prevent accidental full-app regenerations.
- Question for reviewer: do admins have a legitimate "rip the bandage off" workflow where a one-click composite would actually save grief, or is the friction here exactly the point?

**Worker is in-process `BackgroundService` co-located with web** ([plan.md "Constraints"](plan.md#technical-context), [research.md](research.md#decision-aspire-hosted-backgroundservice-for-comparisonjob-worker-in-process-not-separate-project))

YAGNI argument: the orchestrator is the real boundary; Aspire can split the process later without a spec-layer change.
- Question for reviewer: any production deployment posture (scaling, restart cadence) that would make this an actively bad starting point versus splitting from day 1?

**`AiComparisonBypassed` informational event — emitted or not?** ([data-model.md L90](data-model.md#adminauditevent-existing--spec-016--reused) vs [audit-event-payload.md](contracts/audit-event-payload.md#action-constants) vs [T074](tasks.md))

`data-model.md` documents a third action constant `AiComparisonBypassed` "emitted alongside the success/failure event when any bypass flag is set." `contracts/audit-event-payload.md` only documents two action constants and T074 explicitly says it is **not** added (flags on the main event suffice).
- Question for reviewer: which is the intended behaviour? This needs to be one or the other before code drops, because rollup queries differ.

### Areas where I'm less certain (5 min)

- **Pre-flight token estimate** ([FR-G2](spec.md#g-cost-guardrails), [T072](tasks.md)): the spec describes a rough estimate from blob byte sizes + structured payload size. The token-cap message must name the offending input (e.g. "PDF de 50 páginas"). I'm uncertain how cleanly that maps to a regex of attached-blob metadata — Claude's tokenizer for vision/PDF blocks isn't deterministically estimable from `length(bytes)`. The acceptance scenario US4#3 wants a specific user-facing message; the estimator may need to be conservative to avoid false rejects. Worth a sanity check on the estimator design before T072 lands.
- **Repository contract drift** ([data-model.md L180](data-model.md#repository-contracts-application-abstractions) vs [T063](tasks.md)): `data-model.md` lists `GetAsync`, `GetPendingForApplicationAsync`, `GetByApplicationItemAsync`, `EnqueueAsync`, `UpdateAsync`, `GetOrphanedRunningAsync`. T063 adds `GetNextPendingAsync` (atomic claim) but does not say which of the original methods are dropped. Probably none — but the eventual repository surface should be reviewed for cohesion.
- **`PromptCatalog` naming**: [plan.md L146](plan.md#source-code-repository-root) and [data-model.md L82–83](data-model.md#inputdescriptor-application-value-object) say `AnthropicPromptCatalog`; [T021](tasks.md) implements it as `PromptCatalog` in `Application/AiComparison/`. The un-prefixed name is more accurate given Application-layer placement, but the inconsistency should pick one before the file lands.
- **Concurrency control is in-process `SemaphoreSlim`** ([contracts/endpoints.md "Notes"](contracts/endpoints.md#notes)). MVP runs a single web replica; cross-process safety is deferred. I'd flag this as a known gap before the first scale-out window.

### Risks and open questions (5 min)

- If supplier PDFs frequently turn out to be image-only ([A-1](spec.md#assumptions)), the refuse-the-file UX could create reviewer friction in a way that re-opens the OCR question. Is there any signal on what fraction of supplier files in the existing storage are image-only? See [research.md "Outstanding Risks"](research.md#outstanding-risks-carry-into-tasksmd-as-visibility-not-blockers).
- The schema version is part of the input hash, so bumping `AiComparison:SchemaVersion=v2` ([quickstart.md "Operator notes"](quickstart.md#operator-notes-post-deploy)) silently invalidates every cached artifact. Does the runbook ([T090](tasks.md)) need an explicit "low-traffic-window" guard, or is the cache-rebuild cost (per-app rate-limit-bounded) low enough to land schema bumps any time?
- The audit shape ([audit-event-payload.md](contracts/audit-event-payload.md#payloadjson-shape-v1)) is the substrate the future cost-rollup dashboard ([A-8](spec.md#assumptions), [SC-011](spec.md#measurable-outcomes)) will build on without further schema work. If any field is wrong or missing today, it's a one-line fix; in 6 months it's a backfill. Anything you'd add now?
- [NFR-P2](spec.md#performance) (cached page-load ≤100 ms over baseline) and [NFR-P3](spec.md#performance) (hash recompute ≤50 ms) are concrete thresholds, but no task asserts them in a test. Should there be a perf-budget check in CI or is a one-off measurement against fixtures sufficient?
- [SC-005](spec.md#measurable-outcomes) (es-CR for 100 % of generations across a fixture suite) and [SC-010](spec.md#measurable-outcomes) (second provider touches only impl folder) are stated as criteria but rely on copy-review ([T088](tasks.md)) and code-review checklist respectively. Strong-enough verification?

---
*Full context in linked [spec](spec.md) and [plan](plan.md).*
