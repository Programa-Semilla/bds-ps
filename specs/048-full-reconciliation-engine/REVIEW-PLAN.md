# Review Guide: Full Reconciliation Engine

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md) | **Research:** [research.md](research.md)
**Generated:** 2026-07-17

---

## What This Spec Does

Today the platform's zero-colón reconciliation checks (built in slices P1–P3) are invisible: they are recomputed on every read and thrown at the moment an operator tries to validate a payment or close a budget line, and nothing about a problem is remembered between requests. A Financial Operator can't see the list of open problems, hand one to a colleague, track a correction in progress, or knowingly accept a benign anomaly. This slice (P4) turns each mismatch into a **persisted record** with a severity (hard **Blocking** vs advisory **Warning**), a **lifecycle** the operator drives, a full **correction history**, and a **dashboard** to work the queue.

**In scope:** persisted `Discrepancy` + `DiscrepancyEvent`; a fixed per-rule severity model with a 3-condition Warning starter set; the lifecycle (assign → under-correction → resolve/waive) with history; a group→agency dashboard with filters; a best-effort assignment email.

**Out of scope (each named to a later slice):** tolerance config UI + currency + bank-statement reconciliation (P5); interest/fees/refunds/reversals money semantics (P6); reporting/exports (P7); the approver role / no-self-approval / configurable severity (P8); import (P9); multi-agency, Mentori, OCR (parked). See [Out of Scope](spec.md#out-of-scope).

## Bigger Picture

P4 is the **program keystone**: P5, P6, and P7 all reference the severity + lifecycle model this slice establishes, so its data shape matters beyond this delivery. The design deliberately does the least that unblocks them — e.g. the tolerance is a *parameter* now but its config UI waits for P5, where FX rounding creates the first real need. The slice also closes a debt: [spec 047 FINDING-13](research.md#d3--severity--the-warning-starter-set-oq-5-drops-one-rule) (a validated line payment vs an independently-allocated graph invoice) becomes the `GraphInvoiceAllocationDrift` warning. The one architectural bet worth understanding is [persistence model "C"](research.md#d1--oq-2-resolved-a-wrapping-materializer-evaluators-unchanged): a wrapping materializer persists a *visibility snapshot*, but the money gates keep recomputing fresh and throwing — so nothing about the P1–P3 correctness guarantees changes.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [Overview](spec.md#overview) and [research D0–D1](research.md#d0--baseline-reconciliation-today-is-pure--computed-on-read-confirms-model-c-is-a-genuine-gap). The central choice is that discrepancies get persisted for *visibility and lifecycle*, but the authoritative money gate still recomputes fresh at the decision instant.

- Is it acceptable that there are two evaluation paths — a persisted snapshot (for the dashboard) and a fresh recompute (at `Validar`/close) — rather than one source of truth? The alternative (a single materialized source the gate reads) was rejected to preserve P1/P2 race-proofing ([research D1](research.md#d1--oq-2-resolved-a-wrapping-materializer-evaluators-unchanged)). Does that trade feel right?
- The materializer runs [synchronously in the mutating request](plan.md#technical-context), per application. Is that the right latency/consistency trade versus a background worker?

### Key decisions that need your eyes (12 min)

**Dropped the requested-vs-approved warning** ([FR-010](spec.md#functional-requirements), [research D4](research.md#d4--oq-5-resolved-drop-the-requested-vs-approved-variance-warning))
Research found the platform stores no "requested" amount distinct from the executed allocation — applicants submit competing quotes and the reviewer *selects* one, so requested == approved by construction.
- Do you agree the rule should be dropped rather than redefined as "cheapest-estimate vs allocation"? The redefinition compares a live-recomputed lower bound against a frozen snapshot — is that a meaningful control, or noise?

**Assignment email is direct-send, not the outbox** ([FR-027](spec.md#functional-requirements), [research D6](research.md#d6--notifications-direct-send-best-effort-factory-refines-fr-027-away-from-the-outbox))
The outbox resolves recipients from stage-group/role buckets; a discrepancy is assigned to one named person.
- Is best-effort inline send (no retry/delivery-audit) acceptable for this notice, given the dashboard is the durable record? Or is the retry/audit guarantee worth adding an `Assignee` bucket to the outbox?

**Waive asymmetry** ([FR-013/FR-014](spec.md#functional-requirements))
Blocking discrepancies can never be waived (only fixed); Warnings can be waived with a reason.
- Is "audited self-waive allowed in P4" (the same operator who recorded a payment can waive its warning, no second party) the right posture until segregation lands in P8?

**Polymorphic scope key, no FK on `ScopeEntityId`** ([data-model](data-model.md#aggregate-discrepancy-domainentitiesdiscrepancycs-dbodiscrepancies), [research D2](research.md#d2--oq-1-resolved-polymorphic-scope-key--owned-append-only-history-copy-evidenceevidenceversion))
One `(ScopeType, ScopeEntityId)` pair references payments/lines/participants/tranches/documents without per-type FKs.
- The rows are engine-managed and always recomputed, so a stale scope id auto-resolves next run. Is giving up referential integrity here worth avoiding the multiple-cascade-path dacpac problem that 5 typed FKs would create?

### Areas where I'm less certain (5 min)

- **Dashboard filter resolution** ([contracts](contracts/interfaces.md#application--dashboard-projection-new-group-scoped)): filtering by supplier/tranche requires resolving them from `ScopeEntityId` per scope type, done in-memory after a capped materialization (`MaxRows=500`). For a payment-scoped row, "supplier" isn't directly on the disbursement — I assumed the projection resolves it via the line/quotation join where one exists, and simply doesn't match the supplier filter where it can't. Is that the behavior you'd expect, or should unresolvable-dimension rows be excluded/flagged?
- **Auto-resolve actor** ([research D7](research.md#d7--audit-new-discrepancy-adminauditevent-family--two-savechanges-discipline)): auto transitions are attributed to a system-sentinel user id. I assumed the spec-043 sentinel exists and is reusable — the plan should confirm that during implementation (T026).
- **US2 E2E before US3 UI** ([tasks T035](tasks.md#phase-3-user-story-2--discrepancy-lifecycle-with-correction-history-priority-p1)): I drive the lifecycle E2E through a Development-only test seam so US2 is independently testable without the dashboard. If you'd rather not add a test seam, the alternative is to reorder so US3's UI lands first — but that breaks the "each story independently testable" principle.

### Risks and open questions (5 min)

- If the materializer ever fails to fire on a mutation, the dashboard goes stale — but does that ever compromise **money correctness**? (Intended answer: no, because the gate recomputes fresh — [FR-004](spec.md#functional-requirements)/[SC-004](spec.md#measurable-outcomes). Worth confirming the reviewer agrees the gate is genuinely independent of the snapshot.)
- The [stable-identity upsert](spec.md#functional-requirements) (FR-003) is the subtle correctness core: does mapping every evaluator output to a `(ApplicationId, ScopeType, ScopeEntityId, Comparison)` key actually guarantee an Assigned/Waived row is never duplicated as a fresh Open one? The edge cases in [spec Edge Cases](spec.md#edge-cases) are where this is tested — do they cover the cases you'd worry about?
- This is the largest slice in the program. The plan lands [US1+US2 as one spine checkpoint](plan.md#implementation-phasing-for-speckit-tasks) with the P1–P3 regression run at each checkpoint. Is that decomposition enough to keep drift/regression risk manageable?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
