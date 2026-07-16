# Brainstorm: Financial Disbursement Control Platform (program roadmap + slice P1)

**Date:** 2026-07-15
**Status:** P1 shipped (PR #78) — P2–P9 documented below for resume
**Spec:** specs/045-financial-disbursement-core/ (slice P1 only)
**Seed:** brainstorm/seeds/financial_disbursement_requirements_brainstorming.md (July 15 2026 requirements meeting — Danny Pérez, Pao Rodríguez Marín, Vivian Arias)

## Problem Framing

The operating agency receives SBD funds and executes them on behalf of participants. Today this is spreadsheets + PDFs + bank records, causing missing invoices, inconsistent balances, and differences between approved / paid / invoiced / signed amounts. The central rule is **zero-colón reconciliation**: `approved = paid = bank evidence = invoice = signed acceptance = recorded execution = reconciled bank movement`, because discrepancies face internal audit, SBD, and the Comptroller. The seed brief is a whole platform (170 FRs, ledger, reconciliation engine, multi-tenancy, migration).

**Framing decision (locked):** this is the **next evolution of Capital Semilla / FundingPlatform**, not a greenfield build. Capital Semilla already owns the entire pre-approval half (Applicant≈Participant, Company, Supplier, quotations, review→audit→funding-agreement, FundsUsageEvidence, Fund→Process→Group, audit trail, multi-currency, regulatory compliance, es-CR, roles/groups, reports/notifications). The seed adds the **post-execution money layer**. **Multi-agency and Mentori are parked entirely** (per user).

## Approaches Considered

### A: Extend Capital Semilla (CHOSEN)
- Pros: reuses ~two-thirds of the seed (identity, suppliers, funds, storage, audit, currency, roles); the seed itself frames the product as "filling the gap after SBD's agency-level tool" — the slot right after funding agreements execute.
- Cons: multi-agency ambition pulls toward greenfield (resolved by parking it).

### B: Separate greenfield financial platform
- Pros: clean multi-tenant story from day one.
- Cons: rebuilds two-thirds of what already ships; rejected.

## Decision

Extend the platform, decomposed into a **9-slice program**. Brainstormed slice **P1** to an approved spec (045). Slices P2–P9 documented below for resume.

### P1 anchor decisions (all ratified in session)
1. **Dedicated `Disbursement`** event (distinct from `AgreementExecuted` — resolves brainstorm #32's open thread); many per agreement; partial payments; single-participant only.
2. **Three to-the-colón comparisons**, zero tolerance, **all blocking**: disbursement↔bank-receipt, disbursement↔invoice, Σ↔executed-total.
3. **Thin evidence**: exactly one bank receipt + one invoice per disbursement, typed (file+amount+currency+ref+date), 1:1, reusing the existing storage seam.
4. **Five balance dimensions** (Allocated/Paid/Validated/Pending/Available); **Committed deferred to P2**; `Available = Allocated − Paid` (drops at payment, FR-082 resolved).
5. **Group-scoped `Financial Operator` role** (reuses Auditor 038/040 machinery); Auditor/Admin read-only; explicit-but-**unsegregated** `Validar`.
6. **Append-only ledger** (entry types Allocation + Disbursement); **freely-correctable-until-validated, locked-after**; reversals deferred to P6.

### The crux invariant (P1)
Ledger holds only **committed facts** — the Allocation entry plus one immutable Disbursement entry posted at `Validar`. Recorded-but-unvalidated disbursements are **mutable, off-ledger "pending" records**. Projection spans both: `Validated = Σ ledger disbursements`, `Pending = Σ pending records`, `Paid = Validated + Pending`, `Available = Allocated − Paid`. Over-disbursement still counts toward Paid → `Available` may present **negative** (loud signal, never clamped).

---

## Program Roadmap — the resumable index

Anchor for the whole program: **allocation = executed FundingAgreement total** (agreement-level in P1; subdivided from P2). Each slice is one or more future specs. To resume: start a new brainstorm/spec for the target slice using its scope + parked-FR ranges below.

| Slice | Title | Core scope | Depends on | Seed FR / AC anchors |
|-------|-------|-----------|-----------|----------------------|
| **P1** ✅ shipped (PR #78) | Financial Disbursement Core | Disbursement + append-only ledger + 5-dim balance + zero-colón reconciliation (3 comparisons, all blocking); Financial Operator role; freely-correct-until-validated | — | FR-022/023/024/025/026/027, FR-051/052(subset)/055/057/060/062, FR-081/082/083, FR-124(subset)/164/165/168/169; AC-001, AC-005 | **→ spec 045** |
| **P2** | Tranches & budget-lines | Subdivide the allocation into tranches + budget-lines; per-line attribution; **Committed** dimension; many-to-many payment↔line; balance composition by tranche/line | P1 | FR-011/012/013/014/016/017/018; §10.9 official-vs-provisional; AC-002/AC-003 (line side) |
| **P3** | Evidence graph & required-doc rules | Typed evidence (12 doc types), M:N linking + **allocation across lines**, document **version history**, configurable required-document rules, completeness matrix, "can't close with missing evidence" | P1 (P2 for line allocation) | FR-037–050; §10.8 completeness matrix; AC-002/AC-003 (evidence side) |
| **P4** | Full reconciliation engine | Multi-level reconciliation (doc→payment→line→participant→tranche→bank), **non-blocking warnings**, severity model, **discrepancy lifecycle** (open→assigned→under-correction→resolved→approved→waived), reconciliation dashboard | P1–P3 | FR-051–067 (full); §10.10 reconciliation scope |
| **P5** | Currency execution | Foreign-currency payments at **bank-applied rate on payment date**; preserve approved-vs-paid; **re-acceptance / addendum** workflow linked to original approval; quotation currency-consistency warnings | P1 (extends spec 015) | FR-068–079; AC-004; BR-008/009/010/011 |
| **P6** | Interest, fees, refunds, adjustments | Bank interest (incl. return-to-SBD), bank fees/commissions, refunds, reimbursements, **reversals, credit notes**, manual adjustments; agency-level classification **without contaminating participant balances**; new ledger entry types | P1 | FR-089–100; §10.1 ledger types; BR-015; AC-007 (agency side) |
| **P7** | Reporting & statements | Detailed execution report, participant statement, tranche report, agency financial summary, SBD-code exports (Excel/CSV/PDF), validated-vs-provisional distinction, report reproducibility/snapshots | P1–P4 | FR-101–115; §10.9; AC-006/AC-007; BR-017/018/019 |
| **P8** | Segregation of duties & approvals | Configurable approval workflows, **no self-approval of corrections**, approval thresholds by amount, delegated approval, data-entry/review/approval/closure separation | P1 | FR-122–129; Risk 8 |
| **P9** | Migration / spreadsheet import | Participant/allocation/tranche/line/payment/document import, **dry-run**, validation + error report, **duplicate/idempotency** protection, migration metadata + audit | P1–P3 | FR-150–163; AC-009; Risk 11; §10.4 idempotency |
| — | **Parked entirely** | Multi-agency & tenant isolation (FR-130–136, NFR-006), Mentori sync (FR-145–149), participant self-service portal (Phase 2), OCR document parsing, in-platform digital signature, SBD live API integration | — | — |

**Cross-cutting (fold into each slice, no dedicated slice):** notifications/alerts (FR-116–121, ride existing outbox/email), audit/traceability (FR-164–170, extend existing VersionHistory/AdminAuditEvent), decimal precision (NFR-001), transactional consistency (NFR-002), deterministic reconciliation (NFR-020).

## Open Threads

- **P2 anchor:** do budget-lines map onto the existing application line `Item` (each Item = a budget line, payments attribute per-Item) or a new entity? Decide when brainstorming P2. (from #41)
- **P4 balance-recognition revisit:** P1 keyed `Available` off payment; if the *official/reportable* available should key off validation instead, that's a P7 reporting choice — the ledger already carries both. (from #41)
- **P6 ledger vocabulary:** the P1 two-entry-type ledger grows here (refund/reversal/credit-note/interest/fee); confirm reversal semantics preserve the immutability boundary. (from #41)
- **Over-disbursement discrepancy shape:** attached-to-latest-disbursement vs. a distinct agreement-scoped record — deferred to spec 045's `/speckit-plan`. (from #41)
- **P5 depends on spec 015** multi-currency buy-rate snapshotting; confirm reuse vs. extend when brainstorming P5. (from #41)
