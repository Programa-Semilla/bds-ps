# Review Brief: Tranches & Budget-Lines (Financial Execution P2)

**Spec:** specs/046-tranches-budget-lines/spec.md
**Generated:** 2026-07-16

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Slice P2 of the 9-slice financial-execution program. P1 (spec 045) records disbursements against a participant's single flat allocation. P2 subdivides that allocation into **tranches** (funding phases) of **budget-lines**, attributes each disbursement to the specific lines it pays (many-to-many), adds a **Committed** balance dimension for the obligate-before-pay step, and composes the six balances by tranche and line — preserving P1's to-the-colón reconciliation.

## Scope Boundaries

- **In scope:** tranche structure (reviewer-defined, derived amounts), per-line commit/obligation, per-line disbursement attribution (M:N), the Committed dimension, per-participant/tranche/line balance composition, one new blocking line-level over-payment check, budget-line filtering.
- **Out of scope:** evidence↔line allocation / doc versioning / required-doc & closure rules (P3); non-blocking warnings, discrepancy lifecycle, reconciliation dashboard (P4); foreign-currency payment (P5); interest/fees/refunds/reversals (P6); reporting/exports (P7); approval segregation (P8); import (P9). Tranches are a money partition, **not** a time/milestone release gate.
- **Why these boundaries:** keep the slice to the structural money-partition + attribution mechanics; everything requiring the graph, warnings, currency, or reporting rides a later slice.

## Critical Decisions

### Budget-line = the existing `Item`
- **Choice:** no new line entity; the approved, priced, supplier-selected application line item *is* the budget line.
- **Trade-off:** `Item` takes on financial-execution responsibilities alongside its submission/review role.
- **Feedback:** comfortable loading commit-state + tranche membership onto `Item`, or would you rather see a thin financial-side entity referencing it?

### Tranche amount is *derived*, not entered
- **Choice:** a tranche's amount = Σ its assigned lines' budgets, so Σ tranche = allocation holds by construction (no runtime reconciliation of the partition).
- **Trade-off:** you can't reserve tranche money before assigning lines to it.
- **Feedback:** does the agency ever set a tranche amount first and fit purchases within it (which would need the independently-set model instead)?

### Commit is an explicit, reversible step
- **Choice:** the operator obligates a line (Committed) before paying it; reversible until the first payment; only committed lines can be paid.
- **Trade-off:** one extra operator action per line vs. auto-committing every executed line.
- **Feedback:** is there a real moment where money is obligated to a supplier but not yet paid, that this should mirror?

## Areas of Potential Disagreement

### Disbursements may span tranches
- **Decision:** a single payment can attribute to lines in different tranches; each allocation row rolls into its own line's tranche.
- **Why this might be controversial:** it loosens the "one bank payment = one phase" mental model.
- **Alternative view:** confine each disbursement to a single tranche for cleaner rollups.
- **Seeking input on:** confirm spanning is what real bank payments look like here (chosen per your call in brainstorming).

### `Available = Allocated − Paid` (unchanged from P1), with Committed as a display-only dimension
- **Decision:** Committed does not change the Available formula; the official available balance still drops at payment, not at commitment.
- **Why this might be controversial:** some agencies treat committed funds as unavailable.
- **Alternative view:** subtract Committed from Available (revisited as a possible P7 reporting choice — the data supports both).
- **Seeking input on:** is "committed but not yet paid" money still considered available to the participant?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Funding phase | Tranche ("Tramo") | Named sub-allocation grouping lines |
| Budget line | `Item` (existing) | The approved application line item |
| New balance dimension | Committed | Σ budgets of obligated lines |
| Obligation act | Commit / un-commit | Financial Operator, reversible until first payment |
| Payment→line link | Line-allocation | Splits a disbursement across committed lines |

## Open Questions

- [ ] Should a line commit produce a ledger entry, or stay an off-ledger operational status? (leaning off-ledger — commitment isn't settled cash; FR-009)
- [ ] Concrete representation of per-line commit state (enum on `Item` vs. separate row) — a planning decision.
- [ ] Exact set of budget-line "status" filter values for FR-020.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| `Item` accumulating financial responsibility blurs its submission role | Medium | Keep commit/tranche concerns behind explicit behavior methods; revisit if `Item` grows unwieldy |
| Pre-feature executed applications regressing | High | Single-default-tranche interpretation with no data rewrite; SC-006 asserts P1 parity |
| Concurrent validation racing a fresh attribution (over-payment slips through) | High | Re-check per-line committed Σ at validation time (symmetric with P1's participant gate) |
| Balance composition arithmetic drift across levels | Medium | SC-003 requires each level's totals to equal the sum of its children, to the colón |

---
*Share with reviewers before implementation.*
