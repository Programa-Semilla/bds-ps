# Brainstorm: Financial Disbursement Control Platform (program roadmap + slice P1)

**Date:** 2026-07-15
**Status:** P1 shipped (PR #78), P2 shipped (PR #79), **P3 shipped (PR #80)** — P4–P9 documented below for resume
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
| **P3** ✅ shipped (PR #80) | Evidence graph & required-doc rules | Typed evidence (12 doc types), M:N linking + **allocation across lines**, document **version history**, configurable required-document rules, completeness matrix, "can't close with missing evidence" | P1 (P2 for line allocation) | FR-037–050; §10.8 completeness matrix; AC-002/AC-003 (evidence side) |
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

---

## Revisit: 2026-07-16 — Slice P2 brainstormed → spec 046

**Status:** P2 spec-created (`specs/046-tranches-budget-lines/`, branch `046-tranches-budget-lines`, commit `ce367ed`). P1 remains shipped (PR #78, spec 045). P3–P9 still parked for resume.

### P2 anchor decisions (ratified this session)

1. **Budget-line = the existing application line `Item`** (resolves the P2 anchor open thread from #41). No new line entity. A line's budget = its selected-quote CRC amount (the per-line component of `ApplicationCurrencyTotal.Compute`). The `Item.LineCode` convention already used a `"T1-1"` (tranche-1, line-1) example — reuse was the latent intent.
2. **`Tranche` = new entity keyed by `ApplicationId`**, a named funding phase ("Tramo N"). Each `Item` assigned to **exactly one** tranche.
3. **Tranche amount is *derived*** = Σ its assigned lines' budgets ⇒ **Σ tranche amounts = allocation by construction** (the partition is structural, never a runtime reconciliation). Chosen over independently-set tranche amounts (rejected: the extra flexibility of reserving unassigned tranche money isn't needed when lines are already priced at execution).
4. **Tranches defined by the reviewer during the funding-agreement stage** (spec 040); default to a **single tranche holding all lines** if none defined; **frozen at execution** alongside the allocation snapshot.
5. **`Committed` = a distinct, explicit obligation act** by the Financial Operator (obligate-then-pay), not an auto-derived synonym for Allocated. Per-line lifecycle: **Uncommitted → Committed → (paid) → Validated**. Commit is **reversible until the first payment lands on the line**; a disbursement may only attribute to **Committed** lines. Kept off the append-only ledger (commitment isn't settled cash — FR-009).
6. **Per-line payment attribution (M:N)** via disbursement→line allocation rows `(DisbursementId, ItemId, Amount)`. One payment → many lines; one line → many payments. **A disbursement MAY span tranches** (user call — each allocation row rolls into its own line's tranche).
7. **Balance dimensions 5 → 6**: adds **Committed** (Allocated / Committed / Paid / Validated / Pending / Available), exposed per **participant, tranche, and line**. **`Available = Allocated − Paid` unchanged** from P1 (official available drops at payment, not commitment; may go negative, never clamped). Committed is display-only, does not alter Available.
8. **Reconciliation = P1's three + two line-level, all zero-colón blocking**: (kept) disbursement↔bank-receipt, disbursement↔invoice, participant Σ↔allocation; (new) **Σ of a disbursement's line-allocations = disbursement amount** (split integrity); (new) **per-line Σ payments ≤ line committed budget**, blocking at `Validar`, re-checked against the freshly-read committed Σ (symmetric with P1's participant-level over-disbursement gate).

### P2 scope boundaries (confirmed deferrals)
- **Evidence stays 1:1 at the disbursement level** (P1's one bank receipt + one invoice); evidence↔line M:N allocation, doc version history, required-doc rules, completeness/closure gates → **P3**.
- Non-blocking warnings / discrepancy lifecycle / reconciliation dashboard → **P4**. Currency → **P5**. Interest/fees/refunds/reversals → **P6**. Reporting → **P7**. Segregation → **P8**. Import → **P9**.
- Tranches are a **money partition, not a time/milestone release gate**.
- **No new managed deps; additive dacpac-only schema** (new `Tranches` + disbursement↔line allocation join; `Item` gains nullable tranche membership + commit state).

### Updated open threads (carry into `/speckit-plan` for spec 046)
- Does a line **commit** get its own ledger entry type, or stay a mutable off-ledger status? (leaning off-ledger — FR-009).
- Concrete per-line **commit-state representation** (enum on `Item` vs. a separate row).
- Exact set of budget-line **"status" filter values** for FR-020 (uncommitted/committed/paid/validated + validation state).
- **P4 balance-recognition revisit still stands**: if the *official/reportable* available should key off validation instead of payment, that's a P7 reporting choice — the ledger carries both. P2 keeps payment-based Available.
- Spec review (`REVIEW-SPEC.md`): **SOUND**, 0 critical / 0 important.

---

## Revisit: 2026-07-16 — Slice P3 brainstormed → spec 047

**Status:** P3 **shipped (PR #80)** (`specs/047-evidence-graph-required-docs/`). P1 (045, PR #78) + P2 (046, PR #79) remain shipped. P4–P9 still parked.

### P3 scope confirmed (ratified this session)

User chose **all four** capabilities (A+B+C+D) in one slice — the program's largest:
- **A** — expand evidence **types** + add the **signed-acceptance** reconciliation leg P1 deferred.
- **B** — **configurable required-document rules** + live completeness matrix + closure gate.
- **C** — document **version history** (replace preserves prior).
- **D** — evidence→line **M:N with per-line amount allocation** (AC-002/AC-003).

### P3 anchor decisions (5 decisions, all ratified)

1. **Type set trimmed to six** (kept P3 focused): Bank Receipt, Invoice, Signed Acceptance, Credit Note, Refund Receipt, Other. **Bank Statement + Exchange-Rate Adjustment deferred to P5/P6** (their reconciliation legs aren't built yet).
2. **Evidence = first-class Application-scoped node**, optional Disbursement link + **M:N to budget-lines with per-line allocation**; acceptance/credit-note/refund/other attach to lines **without a payment**. P1's disbursement receipt+invoice reconciliation stays untouched.
3. **Required-doc rules scoped to per-`Category` + single global default** (§10.8 matrix = spec-035 Category rows); other five FR-033 axes (payment/supplier type, amount threshold, currency, agency) documented as **future seams, not built**. Reuses spec-040 ChecklistTemplate one-active pattern + spec-035 `CategoryField` live-completeness pattern.
4. **Closure = per-budget-line, off-ledger operational milestone** by the **Financial Operator** (not a new approver — segregation deferred to P8). Blocked unless: required docs present + payments Validated + per-line equality chain to the colón + required docs fully allocated. **Audited reopen-with-reason** allowed (mirrors P2 commit reversibility). Derived line status gains **Closed** terminal + **EvidenceIncomplete** indicator.
5. **Version history = append-only chain** (file + reconciliation-critical fields; reason + actor + hash per version); **no accept/reject review workflow** (deferred to P4/P8). **All P3 reconciliation stays zero-colón blocking** — no warnings/severity/lifecycle (P4). **Credit Note & Refund Receipt are evidence-only** in P3 (requirable/versioned, no reconciliation leg, no balance effect — money semantics are P6).

### P3 scope boundaries (confirmed deferrals)
- Warnings / severity / discrepancy lifecycle / reconciliation dashboard → **P4**. Currency + bank-statement + FX-adjustment evidence → **P5**. Reversals + credit-note/refund money semantics → **P6**. Reporting/statements → **P7**. Segregation-of-duties / approver role → **P8**. Import → **P9**. Multi-agency, participant self-upload, OCR → parked.
- **No new managed deps; additive dacpac-only schema** (new evidence graph + version chain + evidence↔line allocation + required-doc matrix + per-line Closed state).

### Open threads (carry into `/speckit-plan` for spec 047)
- **OQ-1:** generalize the existing `DisbursementEvidence` table into the new Application-scoped evidence entity, vs. add a new table alongside (migration shape).
- **OQ-2:** exact **Closed** representation on `Item` (stored state/flag vs. extending the derived status) + closure metadata.
- **OQ-3:** version chain as an evidence child table vs. a generic document-version table.
- **Plan note (from REVIEW-SPEC):** the completeness check must read **both** disbursement-anchored (P1 receipt/invoice) and line-linked evidence, so a disbursement's invoice counts toward its paid lines' completeness.
- **Watch (from REVIEW-SPEC):** largest slice yet — keep the P1/P2 regression (SC-006) green at each story checkpoint; consider landing US4 (version history) as an independent checkpoint.
- Spec review (`REVIEW-SPEC.md`): **SOUND**, 0 critical / 0 important.
