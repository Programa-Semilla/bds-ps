# Review Brief: Financial Disbursement Core (P1)

**Spec:** specs/045-financial-disbursement-core/spec.md
**Generated:** 2026-07-15

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

First of nine slices in a financial-execution program that extends Capital Semilla downstream of an executed funding agreement. It stands up the spine: a dedicated **Disbursement** record, an **append-only ledger**, a **real-time five-dimension participant balance**, and a **zero-colón reconciliation engine** that ties disbursement ↔ bank receipt ↔ invoice ↔ approved total. It deliberately excludes tranches, budget-lines, the full evidence graph, reporting, FX, and adjustments — all mapped to later slices. Multi-agency and Mentori are parked entirely.

## Scope Boundaries

- **In scope:** one Disbursement (many per agreement, partial payments, single participant, CRC only) + one bank receipt + one invoice; three to-the-colón comparisons, all blocking; append-only ledger (Allocation + Disbursement entries); five balance dimensions; new group-scoped Financial Operator role; Auditor/Admin read-only; freely-correct-until-validated then locked; audit trail.
- **Out of scope:** tranches/budget-lines (P2), evidence graph/versioning (P3), full multi-level reconciliation + warnings + discrepancy lifecycle (P4), FX (P5), interest/fees/refunds/reversals (P6), reporting/statements/exports (P7), segregation of duties (P8), migration/import (P9), multi-agency & Mentori (parked), participant self-service (Phase 2).
- **Why these boundaries:** the seed's own conclusion (§24) and Risks 1–2 say the product *is* the transaction/balance/reconciliation core; a thin vertical slice de-risks that core early while matching the team's slice-by-slice delivery cadence.

## Critical Decisions

### Anchor at agreement-total, not budget-lines
- **Choice:** a Disbursement reduces the executed **funding-agreement total**; tranches/budget-lines are P2.
- **Trade-off:** less faithful to the seed's many-to-many vision now, but the skeleton stands on a shipping object without inventing structure; the ledger math is unchanged when P2 adds a `budgetLineId` dimension.
- **Feedback:** is agreement-level the right P1 anchor, or do budget-lines feel inseparable?

### Ledger holds only committed facts; pending records are off-ledger
- **Choice:** the append-only ledger gets a Disbursement entry only at **Validar**; recorded-but-unvalidated disbursements are mutable, off-ledger "pending" records. Balance spans both: `Paid = Validated + Pending`, `Available = Allocated − Paid`.
- **Trade-off:** two sources of truth for the projection, but it makes "Available drops at payment", "freely correctable until validated / locked after", and "nothing mutable enters the ledger" simultaneously true — and cancelling a pending record needs no compensating entry.
- **Feedback:** is this crux invariant acceptable?

### Available reduces at payment (FR-082 resolution)
- **Choice:** `Available = Allocated − Paid` (drops at recording); `Validated` is the separate "proven correct" number.
- **Trade-off:** the seed left this open; keying "available" off validation instead becomes a P7 reporting choice — the ledger carries both, so it's not locked in.

## Areas of Potential Disagreement

### Over-disbursement makes Available negative
- **Decision:** a recorded-but-blocked over-disbursing disbursement counts toward `Paid`, so `Available` presents negative rather than clamping to zero.
- **Why controversial:** some prefer never showing a negative balance.
- **Alternative view:** exclude blocked disbursements from `Paid` until resolved.
- **Seeking input on:** is a loud negative the right signal, or should blocked-over-disbursement be quarantined out of the balance?

### Introduce the Financial Operator role now
- **Decision:** add a new group-scoped role in P1 rather than reusing Reviewer.
- **Why controversial:** it's a role rollout inside a "thin" slice.
- **Alternative view:** reuse Reviewer, add the role later.
- **Seeking input on:** worth the upfront role cost to avoid an authorization migration across P2–P9? (Recommended: yes.)

### Validar is explicit but unsegregated
- **Decision:** the same operator may record and validate; segregation is P8.
- **Alternative view:** require two people from day one.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Payment event | Disbursement | distinct from the agreement's execution; resolves brainstorm #32 thread |
| Validation action | Validar | explicit human gate on complete evidence + zero discrepancies |
| New role | Financial Operator | group-scoped, mirrors Auditor rollout (038/040) |
| Balance dimensions | Allocated / Paid / Validated / Pending validation / Available | Committed deferred to P2 |
| Ledger entry types | Allocation, Disbursement | refunds/reversals/etc. are P6 |

## Open Questions

- [ ] Over-disbursement discrepancy data shape (attached-to-latest vs. agreement-scoped record) — deferred to `/speckit-plan`.
- [ ] First-class optimistic-concurrency FR vs. edge-case-only — planner's call.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Mutable-balance anti-pattern (seed Risk 2) | High | Append-only ledger mandated as sole balance source (FR-017/018) |
| Reconciliation drift / non-determinism | High | Zero-tolerance, deterministic engine, auto re-run on change (FR-011/012/016, NFR-020) |
| Authorization retrofit across later slices | Med | Group-scoped role introduced now, reusing shipped machinery |
| Scope creep from a 170-FR seed | Med | Every parked concern mapped to a named later slice |

---
*Share with reviewers before implementation.*
