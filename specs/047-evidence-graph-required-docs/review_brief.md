# Review Brief: Evidence Graph & Required-Document Rules (P3)

**Spec:** specs/047-evidence-graph-required-docs/spec.md
**Generated:** 2026-07-16

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Financial-execution program **slice P3 of 9**, on top of P1 (`045`) and P2 (`046`). It turns P1/P2's thin, hard-coded evidence (exactly one Bank Receipt + one Invoice per disbursement, 1:1, replace-in-place, no history) into a **configurable, versioned evidence graph**: typed documents that link many-to-many to budget-lines with per-line amount allocation, admin-defined per-category **required-document rules**, a new **signed-acceptance** reconciliation leg, and an explicit **budget-line closure gate** that cannot pass while required evidence is missing or amounts don't reconcile to the colón. This is the audit-readiness heart of the seed.

## Scope Boundaries

- **In scope:** evidence graph + per-line allocation (US1); per-Category/global-default required-doc matrix + live completeness (US2); line closure with zero-colón reconciliation gate + audited reopen (US3); evidence version history (US4).
- **Out of scope:** warnings/severity/discrepancy-lifecycle (P4); currency + bank-statement + FX-adjustment evidence (P5); reversals + credit-note/refund money semantics (P6); reporting/statements (P7); segregation-of-duties/approver role (P8); import (P9); multi-agency; participant self-upload; OCR; FR-033 config axes beyond category+default; accept/reject review workflow.
- **Why these boundaries:** keep the slice to "typed, versioned, requirable evidence enforced at a closure gate" and preserve P1/P2's clean money-gate; every deferral maps to a later ratified slice.

## Critical Decisions

### Evidence is a first-class, application-scoped graph node
- **Choice:** evidence links optionally to a disbursement AND M:N to budget-lines with per-line allocation; acceptance/credit-note/refund/other can attach with no payment.
- **Trade-off:** richer than P1's payment-anchored 1:1 model; coexistence/migration with the existing `DisbursementEvidence` is deferred to the plan (OQ-1).
- **Feedback:** is application-scoped (not strictly payment-scoped) evidence the right call? (Confirmed in brainstorming.)

### Closure is a per-budget-line, off-ledger milestone
- **Choice:** Financial Operator closes a line only when required docs present + payments validated + `paid = invoiced = accepted` to the colón + required docs fully allocated; audited reopen-with-reason allowed; no ledger entry.
- **Trade-off:** closure ≠ money movement (mirrors P2 keeping `Committed` off-ledger); reopen keeps the slice self-sufficient without P6 reversal machinery.
- **Feedback:** off-ledger closure + reopen-allowed acceptable for audit posture?

### All P3 reconciliation is zero-colón blocking
- **Choice:** the acceptance leg and evidence-allocation checks are hard blocks; no warnings/severity.
- **Trade-off:** consistent with P1/P2, but real signed acceptances can lag payments operationally — a blocking acceptance leg could stall closures until the signed doc arrives.
- **Feedback:** confirmed blocking (not warning) is right for the first cut.

## Areas of Potential Disagreement

### Blocking signed-acceptance leg
- **Decision:** invoice↔acceptance and the per-line equality chain block closure.
- **Why this might be controversial:** operationally, acceptance signatures often trail the payment; a hard block means a paid, invoiced line stays open until the acceptance is collected.
- **Alternative view:** make the acceptance leg a warning until P4's discrepancy lifecycle exists.
- **Seeking input on:** none outstanding — decided blocking; revisit in P4 if it causes closure backlog.

### Slice size
- **Decision:** ship all four capabilities (A+B+C+D) in one slice.
- **Why this might be controversial:** it's the largest slice yet.
- **Alternative view:** split evidence-graph+version-history from required-docs+closure.
- **Seeking input on:** none — decided to keep together; plan will use story checkpoints.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Evidence types (6) | Bank Receipt, Invoice, Signed Acceptance, Credit Note, Refund Receipt, Other | Bank Statement + FX Adjustment deferred to P5/P6 |
| New terminal line state | Closed | above P2's derived `Validated` |
| Completeness signal | EvidenceIncomplete | derived indicator when required docs missing |
| Config unit | Required-document rule set (per Category / global default) | mirrors ChecklistTemplate active-config pattern |

## Open Questions

- [ ] OQ-1: generalize `DisbursementEvidence` into the new evidence entity vs. add a table alongside (migration shape).
- [ ] OQ-2: exact `Closed` representation on `Item` + closure metadata.
- [ ] OQ-3: version chain as evidence child table vs. generic document-version table.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Migration coexistence of old vs new evidence model | High | Resolve OQ-1 in plan; FR-006 keeps P1 receipt/invoice valid; SC-006 regression guard |
| Blocking acceptance leg stalls closures | Med | Reopen path + revisit as warning in P4 |
| Completeness must read disbursement-anchored + line-linked evidence | Med | Plan note (REVIEW-SPEC optional item) to union both sources |
| Largest slice → checkpoint drift | Med | Independent story checkpoints; keep P1/P2 regression green throughout |

---
*Share with reviewers before implementation.*
