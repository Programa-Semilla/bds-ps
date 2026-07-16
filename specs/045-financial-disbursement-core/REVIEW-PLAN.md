# Review Guide: Financial Disbursement Core

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-07-15

---

## What This Spec Does

After a funding agreement is signed and executed, real money still has to leave the bank against it, and today that reconciliation happens in spreadsheets — which is where invoices go missing and balances drift. This slice gives a **Financial Operator** a controlled way to record each **disbursement**, attach the **bank receipt + invoice** that prove it, and have the system reconcile the three amounts **to the colón**. It stands up an **append-only ledger** and a **real-time five-dimension participant balance** so "approved = paid = bank = invoiced" is enforced, not reconstructed.

**In scope:** one `Disbursement` (many per agreement, partial payments, single participant, CRC only) + one bank receipt + one invoice; three blocking zero-tolerance comparisons; append-only ledger (Allocation + Disbursement entries); the five balance dimensions; a new group-scoped `Financial Operator` role; freely-correct-until-validated then locked; full audit.

**Out of scope (each mapped to a later slice in [spec.md §Out of Scope](spec.md#out-of-scope)):** tranches/budget-lines (P2), the full evidence graph (P3), multi-level reconciliation + warnings + discrepancy lifecycle (P4), foreign-currency execution (P5), interest/fees/refunds/reversals (P6), reporting (P7), segregation-of-duties (P8), migration (P9). Multi-agency and Mentori are parked entirely. The boundary worth your scrutiny: **is agreement-level allocation (no budget-lines) a coherent thing to ship on its own, or does it feel half-built without P2?**

## Bigger Picture

This is **slice 1 of a 9-slice program** ([brainstorm/41-financial-disbursement-platform.md](../../brainstorm/41-financial-disbursement-platform.md)) that extends Capital Semilla downstream of `AgreementExecuted`. It deliberately reuses three shipped subsystems rather than inventing: the FundsUsageEvidence (036) storage/upload stack, the Auditor (038/040) role+group-scoping machinery, and the AdminAuditEvent trail. The single biggest architectural bet — an append-only ledger instead of a mutable balance column — comes straight from the seed brief's Risk #2. If you want the origin of these decisions, the six ratified brainstorm decisions are summarized in [research.md](research.md) (R1–R10), each tied to a real code seam.

One thing that changed the design mid-planning and is worth a reviewer knowing: **`FundingAgreement` stores no monetary total** — the money lives on each item's `Quotation.ConvertedCrcAmount`. So the "allocation" is a computed sum, snapshotted into the ledger. That's [R1](research.md#r1--allocation-amount-source-the-approved-total), and it's the kind of assumption most likely to be wrong if the quotation-selection model isn't what I think it is (see "less certain" below).

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md §Purpose + Scope anchor](spec.md#purpose) and the ⚠️ ledger/pending note under [FR-017–020](spec.md#requirements). As you read:

- The whole balance model hinges on one invariant: the ledger holds only **committed** facts (Allocation + a Disbursement entry posted *at validation*), while recorded-but-unvalidated disbursements are **mutable, off-ledger** and still counted toward `Paid`. Does that split read as elegant or as two-sources-of-truth that will confuse the next engineer?
- `Available = Allocated − Paid` drops at **payment**, not validation ([FR-020](spec.md#requirements)). Is that the number the business will actually treat as "available"? The seed left this open (§15.1) and we picked payment — could the agency instead expect "available" to reflect only *validated* execution?
- Allocation = Σ selected quotations' CRC amount, snapshotted ([R1](research.md#r1--allocation-amount-source-the-approved-total)). Is "the executed application's selected line-item quotations" an unambiguous set in the current model?

### Key decisions that need your eyes (12 min)

**Introduce the `Financial Operator` role now** ([research.md R6](research.md#r6--financial-operator-role-group-scoped-consistent), tasks [T020–T021](tasks.md))
A new group-scoped Identity role in a "thin" first slice, vs. reusing `Reviewer` and deferring. The bet is that paying the role cost once avoids an authorization migration across P2–P9.
- Worth the upfront cost, or premature until a later slice actually needs a distinct actor?
- We chose **optional groups** (like Auditor) but a **shown** group selector — deliberately *not* copying the Auditor 038/040 drift. Should a Financial Operator instead be *required* to have ≥1 group?

**The append-only ledger table, when a state-projection would do for P1** ([plan.md Complexity Tracking](plan.md#complexity-tracking))
This is the one tracked complexity. For P1 alone, balances are derivable from `Disbursements`-by-state + a computed allocation; the ledger table earns its keep at P6 (refunds/reversals) and P2 (budget-line dimension).
- Is building the ledger now the right call, or is it speculative complexity we should defer until P6 forces it?

**All discrepancies block; discrepancies are computed, not stored** ([research.md R4](research.md#r4--reconciliation-engine-representation))
P1 has no discrepancy lifecycle (that's P4), so we store only the derived `State` (Inconsistent/Recorded) and recompute the discrepancy details on read via a pure evaluator.
- Is "no persisted discrepancy history in P1" acceptable given the audit-heavy domain, or does an auditor need the discrepancy trail from day one?

**Over-disbursement drives `Available` negative** ([spec.md FR-020 + edge cases](spec.md#requirements), tasks [T038–T039](tasks.md))
A blocked over-disbursing disbursement still counts toward `Paid`, so `Available` shows e.g. −₡100,000 rather than clamping.
- Is a loud negative the right signal, or should a blocked disbursement be quarantined out of the balance entirely?

### Areas where I'm less certain (5 min)

- ~~[R1]: I assumed the allocation is a hand-rolled Σ of selected quotations' `ConvertedCrcAmount`.~~ **Resolved:** confirmed against the code — reuse the existing single-source-of-truth **`ApplicationCurrencyTotal.Compute(app).Total`** ([research.md R1](research.md#r1--allocation-amount-source-the-approved-total), [T025/T034](tasks.md)), which already encodes `SelectedSupplierId`-based selection and skips legacy/unconverted rows. The funding-agreement PDF gate guarantees completeness by execution time. One residual for a reviewer: is it acceptable that the disbursement allocation is defined to *equal* the `ApplicationCurrencyTotal` rollup (so both move together if that rollup ever changes), rather than an independent snapshot of the agreement's stated amount?
- ~~[T029 vs T046]: US1 built the controller without group-overlap scoping, deferring it to US5.~~ **Resolved:** group-overlap scoping + no-disclosure 404 now land in [T029](tasks.md) (US1), so the surface is secure by default; US5 keeps only the Auditor read-only write-guard. ([research.md R10](research.md#r10--routing--read-surface))
- [research.md R7](research.md#r7--audit-trail-disbursement-on-adminauditevent): I routed disbursement audit to `AdminAuditEvent` (not `VersionHistory`) because VersionHistory can't carry before/after and is Application-scoped. Reasonable, but it means the disbursement history lives in the *admin* audit surface, not the application timeline — is that where an auditor will look for it?

### Risks and open questions (5 min)

- The `TINYINT` enum columns use `HasConversion<byte>()`, and EF-InMemory **hides** the `Byte→Int32` materialization throw ([tasks.md T022](tasks.md), the 035/040 lesson). If [T022](tasks.md)'s real-SQL integration test is skipped or weakened, does this ship a latent E2E-only failure?
- ~~Concurrency: is optimistic concurrency sufficient for two operators recording partial payments that each fit under the ceiling but together exceed it?~~ **Resolved:** the over-disbursement check is re-run at validation against the committed Σ ([T027](tasks.md), [research.md R5](research.md#r5--over-disbursement-attribution), [FR-005](spec.md#requirements)) — the second to validate is refused. Lock-free (the retrying execution strategy forbids a serializing transaction).
- [FR-007](spec.md#requirements) (a contract does not substitute for an invoice) has no implementing task — it's satisfied by *omission* (no contract concept exists in P1). Is "enforced by absence" adequate, or should there be an explicit test that a contract can't stand in for the invoice?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
