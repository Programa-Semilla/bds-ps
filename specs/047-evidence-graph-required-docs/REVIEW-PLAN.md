# Review Guide: Evidence Graph & Required-Document Rules (P3)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-07-16

---

## What This Spec Does

After a funding agreement executes, a Financial Operator records payments against a participant's budget-lines. Today the supporting paperwork is thin and rigid — exactly one bank receipt + one invoice per payment, no history, no notion of a document being *required* or a line being *finished*. This slice makes evidence a configurable, versioned graph: typed documents linked many-to-many to budget-lines with per-line amounts, an admin-defined per-category "which documents are required" matrix, and an explicit **budget-line closure** that refuses to complete while required evidence is missing or amounts don't reconcile to the colón.

**In scope:** the evidence graph + per-line allocation (US1), the required-doc matrix + live completeness (US2), the closure gate with the `paid = accepted` reconciliation leg + audited reopen (US3), append-only version history (US4).

**Out of scope:** non-blocking warnings / discrepancy lifecycle (P4); currency + bank-statement + FX-adjustment evidence (P5); reversals and the *money* semantics of credit-notes/refunds (P6); reporting (P7); segregation-of-duties / a separate approver (P8); import (P9); multi-agency; participant self-upload; OCR; an accept/reject document-review workflow. The out-of-scope list is deliberately long because this is 1 of 9 program slices — the boundaries are where reviewer feedback matters most.

## Bigger Picture

This is **P3** of the 9-slice financial-execution program that extends Capital Semilla past agreement execution into money execution. P1 (`045`) built the disbursement + append-only ledger + zero-colón reconciliation; P2 (`046`) subdivided allocation into tranches → budget-lines (= the existing `Item`) with per-line payment attribution. P3 sits on both. The load-bearing design choice is to leave P1's clean money-gate (`DisbursementEvidence`) **untouched** and add the graph alongside — see [research.md D1](research.md#d1--evidence-storage-shape-resolves-oq-1). Everything downstream (P4 reconciliation dashboard, P7 reporting) will read the evidence graph and closure state this slice introduces.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec Context](spec.md#context) and [User Story 1](spec.md#user-story-1---typed-evidence-graph-with-per-line-allocation-priority-p1), then [research.md D1](research.md#d1--evidence-storage-shape-resolves-oq-1) and [D6](research.md#d6--reconciliation-legs-per-line-equality-chain). As you read:

- The graph is a *second* evidence model living next to P1's `DisbursementEvidence`. Is "two evidence tables" the right call, or does it invite the divergence the spec tries to avoid? (The counter-argument: `FundsUsageEvidence` already established the pattern, and generalizing the money-gate table is high-risk churn.)
- Does treating budget-line = `Item` still hold up when a *line* now has its own closed/open lifecycle on top of P2's commit state?

### Key decisions that need your eyes (12 min)

**The invoice leg is inherited, not re-checked** ([research.md D6](research.md#d6--reconciliation-legs-per-line-equality-chain))
The per-line equality chain is stated as `paid = invoiced = accepted`, but the plan makes only `paid == accepted` a *new* blocking check — `paid == invoiced` is argued to hold by construction because P1 forces disbursement↔invoice and P2 forces the split to sum to the disbursement.
- Question: is that transitivity argument airtight for every real case (partial payments, a line paid by several disbursements), or should the plan add an explicit per-line invoiced-sum check to be safe? This is the decision most likely to hide a correctness gap.

**Closure is off-ledger and reopenable** ([spec FR-017/FR-018](spec.md#functional-requirements), [research.md D3](research.md#d3--budget-line-closed-state--closure-metadata-resolves-oq-2))
Closing a line writes no ledger entry and can be reversed with a reason.
- Question: for audit posture, is a freely-reopenable closure (by the same Financial Operator who closed it) acceptable, or does "closed" need to mean something stronger before P8 adds segregation-of-duties?

**Blocking signed-acceptance leg** ([spec FR-024/FR-025](spec.md#functional-requirements))
The acceptance leg hard-blocks closure.
- Question: signed acceptances often lag the payment operationally — will a hard block create a backlog of paid-but-unclosable lines, and is the audited-reopen escape hatch enough until P4 introduces warnings?

**Required-doc matrix has no snapshot table** ([research.md D5](research.md#d5--required-document-rule-matrix-admin-config))
Unlike the `ChecklistTemplate` precedent it mirrors, the matrix drops the per-line response/snapshot table, relying on "completeness is live + closure is stored."
- Question: is it acceptable that a closed line records *no* trace of which rule-set version it satisfied — only that it's closed? (The plan's claim: reopening re-subjects it to the current rule, so a snapshot adds nothing.)

### Areas where I'm less certain (5 min)

- [research.md D6](research.md#d6--reconciliation-legs-per-line-equality-chain): I resolved the invoice/payment redundancy by making the invoice leg implicit. If the operational reality has invoices that *don't* equal their disbursement (e.g. a single graph invoice spanning multiple disbursements), that assumption breaks and the equality chain needs an explicit invoiced term. I flagged it but did not fully close it.
- [spec AC-002/AC-003](spec.md#success-criteria): these are framed around invoices as graph nodes, but the plan satisfies them largely via the *disbursement* split for disbursement-anchored invoices. A reviewer should confirm that interpretation matches what stakeholders pictured when they said "one invoice across four lines."
- [data-model.md](data-model.md) `UNIQUE (CategoryId)` with a nullable column: relying on SQL Server's single-NULL semantics for the global-default row is subtle; the service enforces it too, but it's worth a second look.

### Risks and open questions (5 min)

- If a supplementary graph Invoice *is* allocated to a line that a disbursement already covers ([research.md D1](research.md#d1--evidence-storage-shape-resolves-oq-1)), could completeness or the equality chain double-count? The plan says graph invoices are constrained only by allocation-integrity — is that enough to prevent double-counting at closure?
- The completeness resolver reads *two* sources (disbursement-anchored + graph) per line ([tasks.md T034](tasks.md)). On the disbursement `Index` with many lines, is the batched query ([T061](tasks.md)) sufficient to avoid an N+1, or should this be load-tested?
- Credit Note / Refund Receipt are inert in P3 ([spec FR-026](spec.md#functional-requirements)) yet requirable via the matrix — is a "required but reconciliation-inert" document type confusing to operators, or clearly a placeholder for P6?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
