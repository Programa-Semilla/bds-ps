# Review Guide: Regulatory Freshness Gating + Hacienda API Sync

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-21

---

## What This Spec Does

Suppliers carry tax/social-security compliance statuses (Hacienda, CCSS/Caja, SICOP/CCOP). Slice A (spec 038) recorded *when* each was last reviewed but never enforced freshness or refreshed it automatically. This slice does both: it **blocks** an application from advancing through the auditor stage while any supplier it will contract with has a regulatory value that hasn't been reviewed within a configurable window (default 30 days), and it runs a **daily job** that refreshes each supplier's Hacienda status from the public Costa Rican tax API.

**In scope:** the staleness block at the auditor's advance actions + an early non-blocking warning; the daily Hacienda sync (real API, behind a test fake); per-provider sync-failure visibility; a daily stale-value digest to auditors.

**Out of scope:** other feedback-3 slices (E/F/G/H); any change to slice A's regulatory model/enums; real-time per-request Hacienda lookups; an API for CCSS/SICOP (those stay manual); retroactive blocking of already-released agreements. See [Out of Scope](spec.md#out-of-scope).

## Bigger Picture

This is the fourth and final foundation-dependent slice of feedback-3 (A→B→C shipped; D closes the compliance arc). It deliberately consumes seams slice A left dangling — the `RegulatoryReviewSource.Api` enum value and the per-field `LastReviewedAt/By/Source` columns were reserved in 038 specifically for this work. The external dependency is the Ministerio de Hacienda `fe/ae` endpoint (`api.hacienda.go.cr`), a public, unauthenticated "comprobante electrónico" taxpayer-status lookup that Costa Rican e-invoicing systems already rely on; the contract was captured live during planning (see [research.md D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1)). After this, the remaining feedback-3 slices (E/F/G/H) are all independent of the auditor/compliance machinery.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec Overview](spec.md#overview) and [User Story 1](spec.md#user-story-1---stale-provider-regulatory-values-block-application-advancement-priority-p1) + [User Story 2](spec.md#user-story-2---daily-hacienda-sync-keeps-tax-status-current-and-audited-priority-p1), then [research.md](research.md). As you read:

- The block fires at the **auditor's** advance actions (generate/confirm/release), not the reviewer's. Given slice C moved PDF generation to the auditor, is that the right single chokepoint, or should the reviewer's send-to-audit also hard-block rather than just warn? ([FR-004](spec.md#functional-requirements), [research D7](research.md#d7--where-the-hard-gate-is-enforced))
- The gate checks the suppliers chosen per line item (`Item.SelectedSupplierId`), i.e. exactly the agreement's counterparties. Does "relies on" ([FR-006](spec.md#functional-requirements)) match your mental model, or would you expect *every* attached quotation's supplier to be in scope? ([research D2](research.md#d2--referenced-provider-scope-for-the-gate-resolves-oq2))

### Key decisions that need your eyes (12 min)

**All three fields block equally — Hacienda included** ([FR-005](spec.md#functional-requirements))
The brainstorm chose to let a stale Hacienda value block too, so a prolonged API outage that lets Hacienda go stale will halt advancement (fail-safe).
- Question: is fail-safe-on-outage right, or should Hacienda — being machine-maintained — be exempt from blocking so an API outage never blocks funding?

**404 maps to `SinInformacion`, not `SinInscripcion`** ([research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1))
Live sampling revealed HTTP 404 means "information not available", while a genuine *unregistered* taxpayer comes back as a 200 with `estado:"No inscrito"`. The mapper treats these differently.
- Question: do you agree a 404 should record "sin información" (and refresh the timestamp) rather than be treated as a hard failure or as "sin inscripción"?

**`Inscrito` + `omiso=SI` → `CobroAdministrativo`** ([research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1), [tasks T043](tasks.md))
This is the one mapping row not directly confirmed by a live sample; `DesinscritoDeOficio` is left unreachable because `fe/ae` exposes no signal for it.
- Question: is this acceptable to ship as a best-effort mapping with a stakeholder-confirm task (T043), or should it block until confirmed?

**Daily digest sent directly, bypassing the notification outbox** ([research D3](research.md#d3--stale-value-notification-daily-digest-direct-send-audit-pipeline-scoped-resolves-oq3))
A recurring multi-application per-auditor digest doesn't fit the per-application outbox idempotency key, so it follows the `StageExpiryReminderService` direct-send pattern instead — no new `NotificationEvent`.
- Question: is reusing the reminder-service pattern (rather than forcing the outbox) the right call, and is scoping the digest to *audit-pipeline* applications (vs. every stale supplier catalog-wide) appropriately actionable?

**Wall-clock daily scheduling without a cron library** ([research D4](research.md#d4--daily-scheduling-at-a-wall-clock-morning-time-resolves-165))
No existing service runs at a wall-clock time; this adds a small "delay until next 06:00 CR" loop rather than taking a Quartz/NCronJob dependency.
- Question: acceptable, or would you prefer an external scheduler / a managed cron dependency for a daily job?

### Areas where I'm less certain (5 min)

- [research D3](research.md#d3--stale-value-notification-daily-digest-direct-send-audit-pipeline-scoped-resolves-oq3): the spec's [FR-022](spec.md#functional-requirements) phrases the digest as provider-centric ("providers whose values are stale"), but I refined it to *application-pipeline-centric* scoping so it reuses the group→auditor resolution and stays actionable. That's a real interpretation choice — a reviewer might prefer the literal provider-centric reading.
- [research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1): the 404-vs-"No inscrito" distinction is inferred from a handful of live IDs; the full Hacienda `estado` vocabulary isn't documented, so an unrecognized `estado` is deliberately mapped to a *failure* (surfaced) rather than guessed.
- [plan.md](plan.md#phase-1--design--contracts): I assumed `Microsoft.Extensions.Http` (`AddHttpClient`) is already transitively present so the live client needs no new package — worth a quick confirm at implementation (T019/T021).

### Risks and open questions (5 min)

- If a previously-`AlDia` supplier returns a transient 404, the sync would record `SinInformacion` and refresh its timestamp — masking the prior good status until the next successful run. Is the once-daily cadence + the [failure surface](spec.md#user-story-3---sync-failures-are-visible-never-silent-priority-p2) enough, or do we want 404 to be a soft outcome that doesn't overwrite a known-good value? ([research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1))
- The freshness gate adds a query at the auditor advance action. For an application with many line items/suppliers, is one batched read acceptable, or should findings be cached on the audit screen load? ([plan Performance Goals](plan.md#technical-context))
- The daily sync iterates *all* suppliers ([FR-017](spec.md#functional-requirements)); `BatchSize` throttles it but there's no numeric perf threshold. At current catalog size this is fine — does the project anticipate a supplier count where this needs a measured budget?

---
*Full context in linked [spec](spec.md), [plan](plan.md), [research](research.md), and [tasks](tasks.md).*
