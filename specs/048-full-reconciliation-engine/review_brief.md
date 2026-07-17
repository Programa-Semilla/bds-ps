# Review Brief: Full Reconciliation Engine

**Spec:** specs/048-full-reconciliation-engine/spec.md
**Generated:** 2026-07-17

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Financial-execution **P4 of 9** — the program keystone. Today every reconciliation check (P1–P3) is an ephemeral, computed-on-read hard block that throws at validation/closure and remembers nothing. P4 makes discrepancies **first-class, persisted, stateful records** with a **severity model** (Blocking vs non-blocking Warning), a **lifecycle** operators drive (assign → under-correction → resolve, or waive a warning), full per-discrepancy **correction history**, multi-level coverage, and a **reconciliation dashboard** scoped group→agency. The zero-colón money guarantee is preserved exactly.

## Scope Boundaries

- **In scope:** persisted `Discrepancy` + `DiscrepancyEvent`; fixed per-rule severity; a 4-condition non-blocking Warning starter set; the lifecycle + waive (warnings only); synchronous in-transaction materialization; group/agency dashboard + filters; assignment-only email.
- **Out of scope:** tolerance config UI, FX + bank-statement legs (P5); interest/fees/refunds/reversals money semantics (P6); reporting/exports (P7); approver role / no-self-approval / severity config (P8); import (P9); multi-agency, Mentori, OCR, e-signature, SBD API (parked).
- **Why:** deliver the severity+lifecycle model that P5/P6/P7 all depend on, without reaching into unbuilt external-reference data.

## Critical Decisions

### Persistence model "C" — snapshot for visibility + fresh recompute at the gate
- **Choice:** every reconciliation event upserts persisted discrepancy rows, but the money gate (`Validar`/close) still recomputes fresh at the decision instant.
- **Trade-off:** two evaluation paths (persisted + fresh) instead of one source of truth, in exchange for keeping the P1/P2 race-proof guarantee intact.
- **Feedback:** is preserving the fresh-recompute gate worth the modest duplication? (Alternatives A/B were considered and rejected in brainstorming.)

### Fixed per-rule severity + configurable-tolerance seam only
- **Choice:** core money identities are permanently Blocking; a defined advisory set is Warning; only the tolerance parameter is a (future-)configurable lever, default 0 CRC, config UI deferred to P5.
- **Trade-off:** less flexible than admin-configurable severity, but the core invariant can't be waved down to advisory.
- **Feedback:** confirm no core identity should ever be operator-downgradable in P4.

### Waive asymmetry
- **Choice:** Blocking discrepancies can never be waived (only fixed); Warnings can be waived with a required, audited reason.
- **Feedback:** confirm this is the right control posture for an unsegregated P4.

## Areas of Potential Disagreement

### Self-waiver allowed in P4
- **Decision:** the same operator who recorded a payment may waive the resulting warning (audited).
- **Why controversial:** violates segregation-of-duties instinct.
- **Alternative view:** require a second party to waive.
- **Seeking input on:** accept audited self-waiver now, with no-self-approval deferred to P8? (Current answer: yes.)

### Requested-vs-approved variance warning (OQ-5)
- **Decision:** included in the starter set, *pending confirmation the data exists*.
- **Why controversial:** may not be computable if a distinct "requested" amount isn't retained post-execution.
- **Seeking input on:** if the amount isn't stored, drop the rule rather than ship it hollow — agreed?

### Slice size
- **Decision:** full bundle (all four capabilities) in one spec — the largest slice in the program.
- **Alternative view:** spine-first (persist + severity + lifecycle + dashboard over existing checks), deferring new coverage.
- **Seeking input on:** comfortable with the full-bundle size, landing US1/US2 as separate checkpoints?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Persisted discrepancy record | `Discrepancy` | Application-scoped; stable identity (scope-type, scope-entity-id, comparison-rule) |
| Correction-history child | `DiscrepancyEvent` | append-only per-transition timeline |
| Severity tiers | `Blocking` / `Warning` | fixed per rule; `Warning` was the reserved P1 seam |
| Lifecycle states | Open / Assigned / UnderCorrection / Resolved / Waived | Waived = warnings only |
| Audit family | `discrepancy.*` | AdminAuditEvent, actor + before/after |

## Open Questions

- [ ] OQ-1: `Discrepancy` scope-key shape — polymorphic key vs nullable typed FKs.
- [ ] OQ-2: refactor evaluators to emit rows vs a wrapping materializer that diffs.
- [ ] OQ-3: dashboard placement / reuse of `_DiscrepancyList`.
- [ ] OQ-4: concurrency token on `Discrepancy` (RowVersion) vs the deferred Items-RowVersion debt.
- [ ] OQ-5: confirm requested-vs-approved data availability, else drop the rule.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Materializer weakens the money gate | High | Model C keeps fresh recompute authoritative at the gate; SC-004 regresses P1–P3 |
| Stable-identity mismatch resets lifecycle state on re-run | Med | FR-003 + SC-001 pin identity/state preservation; edge cases enumerated |
| Slice size causes drift/regression | Med | Land US1/US2 as separate checkpoints; SC-006-family regression each checkpoint |
| Warning rule not computable (OQ-5) | Low | Drop-if-absent rule; caught at plan |

---
*Share with reviewers before implementation.*
