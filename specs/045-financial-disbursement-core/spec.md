# Feature Specification: Financial Disbursement Core

**Feature Branch**: `045-financial-disbursement-core`
**Created**: 2026-07-15
**Status**: Draft
**Input**: Brainstorm session — see `brainstorm/41-financial-disbursement-platform.md` and seed `brainstorm/seeds/financial_disbursement_requirements_brainstorming.md`

> **Program context.** This is **slice 1 of 9 (program-slice "P1")** of a financial-execution program that extends the existing Capital Semilla / FundingPlatform. Multi-agency and Mentori integration are parked entirely; the remaining eight slices are enumerated in **Out of Scope** below and detailed in the brainstorm document. To avoid collision with that program numbering, the prioritized user stories in this spec use **US1…US5** labels (their P1/P2… priorities are *internal to this feature*).

## User Scenarios & Testing *(mandatory)*

### User Story US1 - Record a disbursement, prove it, reconcile it to the colón (Priority: P1)

A Financial Operator opens an executed funding agreement, records that money left the bank (a **disbursement**: date, amount in CRC, bank transaction reference), and attaches the two documents that prove it — the **bank receipt** and the **invoice**, each with its own amount. The system immediately reconciles the three amounts to the colón. If they match, the operator can validate the disbursement; if any differ — even by one colón — the disbursement is flagged **Inconsistent** with the exact discrepancy and cannot be validated until corrected.

**Why this priority**: This is the walking skeleton of the entire financial-execution program. On its own it replaces the spreadsheet reconciliation that is the core business pain: it proves that *approved = paid = bank = invoiced* is enforced by the system rather than reconstructed by hand.

**Independent Test**: With one executed agreement, record a disbursement, attach a bank receipt and an invoice, observe the reconciliation result, correct a mismatch, and validate — all without any tranche, budget-line, report, or balance view.

**Acceptance Scenarios**:

1. **Given** an executed agreement of ₡85,800 and a disbursement of ₡85,800 with a bank receipt of ₡85,800 and an invoice of **₡85,728**, **When** the invoice is recorded, **Then** the system detects a **₡72** discrepancy, identifies the invoice as the source, marks the disbursement **Inconsistent**, and refuses validation.
2. **Given** that same Inconsistent disbursement, **When** the operator corrects the invoice amount to ₡85,800, **Then** reconciliation re-runs automatically, the discrepancy clears, and the operator can validate the disbursement.
3. **Given** a disbursement with a bank receipt but **no invoice**, **When** the operator attempts to validate, **Then** the system refuses, states that the invoice is missing, and the disbursement remains not-validated.
4. **Given** a fully matching disbursement with both documents present, **When** the operator performs **Validar**, **Then** the disbursement becomes **Validated** and an immutable ledger entry is posted.

---

### User Story US2 - See the participant's real-time balance in five dimensions (Priority: P2)

An operator, auditor, or admin views a participant's financial position and sees five figures that update in real time as disbursements are recorded and validated: **Allocated, Paid, Validated, Pending validation, Available**. Each figure is explained by the underlying transactions — no figure is a hand-maintained number.

**Why this priority**: The balance is the headline the business reads. Separating "money gone" (Paid/Available) from "money proven correct" (Validated) surfaces exactly the risk an auditor cares about and is impossible with the current spreadsheet.

**Independent Test**: Record and validate a sequence of disbursements against one agreement and confirm the five dimensions match the definitions (`Available = Allocated − Paid`, `Paid = Validated + Pending`, etc.) at every step.

**Acceptance Scenarios**:

1. **Given** an executed agreement of ₡1,000,000 with no disbursements, **When** the balance is viewed, **Then** Allocated=₡1,000,000, Paid=₡0, Validated=₡0, Pending=₡0, Available=₡1,000,000.
2. **Given** one **recorded but not yet validated** disbursement of ₡300,000, **When** the balance is viewed, **Then** Paid=₡300,000, Pending=₡300,000, Validated=₡0, Available=₡700,000.
3. **Given** that disbursement is then **Validated**, **When** the balance is viewed, **Then** Validated=₡300,000, Pending=₡0, Paid=₡300,000, Available=₡700,000 (Available unchanged by validation).

---

### User Story US3 - Partial payments and the over-disbursement guard (Priority: P3)

An operator records **several** disbursements against one executed agreement (partial payments), and the system prevents the total of all disbursements from exceeding the agreement's approved total.

**Why this priority**: Partial payments are the real world (a purchase paid in two transfers; a tranche paid ahead of the rest). The over-disbursement guard is the agreement-level integrity rule that protects the allocation until budget-lines refine it in a later slice.

**Independent Test**: Record two disbursements that together stay within the total (both succeed), then attempt a third that would exceed it (blocked as a discrepancy).

**Acceptance Scenarios**:

1. **Given** an executed agreement of ₡1,000,000 with a validated disbursement of ₡600,000, **When** the operator records a second disbursement of ₡400,000, **Then** it is accepted and Available becomes ₡0.
2. **Given** the same agreement with ₡600,000 already disbursed, **When** the operator records a disbursement of ₡500,000, **Then** the system raises a blocking **over-disbursement** discrepancy and does not let it validate.
3. **Given** an agreement with only ₡400,000 disbursed against a ₡1,000,000 total, **When** the balance is viewed, **Then** under-disbursement is **not** a discrepancy — Available simply reads ₡600,000.

---

### User Story US4 - Correct before validation, lock after, with a full audit trail (Priority: P4)

While a disbursement is not yet validated, the operator may freely correct it (amount, references, dates, replace either file) or cancel it. Once validated, it is locked — no edits, no deletion. Every action is recorded with who did it, when, and the before/after values.

**Why this priority**: Auditability is the platform's reason to exist; the immutability boundary and audit trail are what make the numbers defensible to internal auditors, SBD, or the Comptroller.

**Independent Test**: Edit and cancel a pending disbursement (allowed), validate another, then attempt to edit/delete the validated one (refused), and confirm every action appears in the audit trail.

**Acceptance Scenarios**:

1. **Given** a **Recorded/Inconsistent** disbursement, **When** the operator edits its amount or replaces a file, **Then** the change is accepted and reconciliation re-runs.
2. **Given** a **Recorded/Inconsistent** disbursement, **When** the operator cancels it, **Then** it becomes **Cancelled**, contributes nothing to the balance, and leaves no ledger entry behind.
3. **Given** a **Validated** disbursement, **When** anyone attempts to edit or delete it, **Then** the action is refused (correction would require a reversal, which is out of scope for this slice).
4. **Given** any disbursement action (create, edit, replace, validate, cancel), **When** an auditor inspects the trail, **Then** the actor, timestamp, and before/after values are present.

---

### User Story US5 - Role scoping and read-only visibility (Priority: P5)

Only a **Financial Operator** (scoped to their groups) can record and validate disbursements, and only within agreements belonging to their groups. **Auditor** and **Admin** users have read-only visibility into disbursements, balances, and discrepancies. Applicants/participants have no access to this surface.

**Why this priority**: Correct authorization from the first slice avoids retrofitting access control across all later slices; read-only auditor visibility is a compliance expectation from day one.

**Independent Test**: As a Financial Operator, confirm access to in-group agreements and refusal (flat not-found) for out-of-group ones; as an Auditor/Admin, confirm read-only views; as an applicant, confirm refusal.

**Acceptance Scenarios**:

1. **Given** a Financial Operator whose groups include an agreement's group, **When** they open its financial surface, **Then** they can record, edit, and validate disbursements there.
2. **Given** a Financial Operator whose groups do **not** include an agreement's group, **When** they attempt to reach it, **Then** the system returns a flat not-found (no disclosure of existence).
3. **Given** an Auditor or Admin, **When** they open the financial surface, **Then** they can view disbursements, balances, and discrepancies but see no record/edit/validate controls.
4. **Given** an applicant/participant, **When** they attempt to reach the financial surface, **Then** access is refused.

---

### Edge Cases

- **Zero or negative amount**: recording a disbursement, bank receipt, or invoice with a non-positive amount is rejected.
- **Non-CRC currency**: only CRC is accepted in this slice; a foreign-currency amount is rejected (foreign-currency execution is a later slice).
- **Amount edited after evidence attached**: reconciliation re-runs and the disbursement's Inconsistent/clean state updates accordingly.
- **Partial disbursements summing exactly to the total**: allocation is fully consumed; Available reads ₡0; no discrepancy.
- **Cancelling a pending disbursement**: it disappears from Paid/Pending immediately and leaves nothing in the append-only ledger (it never posted).
- **Concurrent edits** to the same disbursement: the second writer is prevented from silently overwriting the first (optimistic concurrency).
- **Concurrent partial payments across two disbursements**: two operators each record a disbursement that individually fits under the remaining allocation but together exceed it; because single-row concurrency cannot catch this cross-row case, the over-disbursement check is re-run at validation, and the second disbursement to validate is refused (the money already moved, so `Available` reads negative until resolved).
- **Both amounts differ**: if bank receipt and invoice both differ from the disbursement, both comparisons are reported so the operator sees every source of difference, not just the first.
- **Replacing a file before validation**: overwrites the prior file (no version history in this slice); after validation, replacement is refused.
- **Over-disbursement makes Available negative**: when a recorded disbursement pushes the participant's total past the allocation, `Available` reads negative (e.g. −₡100,000) rather than clamping to ₡0, so the over-disbursement is visible in the balance, not just on the blocked disbursement.

## Requirements *(mandatory)*

### Functional Requirements

**Disbursement**
- **FR-001**: A Financial Operator MUST be able to record a Disbursement against an **executed** funding agreement, capturing payment date, amount (CRC), a bank transaction reference, and one optional free-text bank/account reference.
- **FR-002**: The system MUST allow **many** Disbursements per agreement (partial payments) and MUST bind each Disbursement to exactly **one** participant/agreement (no cross-participant disbursements).
- **FR-003**: The system MUST reject a Disbursement, bank receipt, or invoice amount that is zero or negative.
- **FR-004**: The system MUST reject any amount denominated in a currency other than CRC in this slice.
- **FR-005**: The sum of a participant's non-cancelled Disbursements MUST NOT exceed the executed agreement total; an attempt to exceed it MUST raise a blocking over-disbursement discrepancy. Under-disbursement MUST NOT be treated as a discrepancy. The over-disbursement check MUST be re-evaluated at validation against the current committed total, so that two disbursements recorded concurrently — each individually within the ceiling — cannot both reach `Validated` and breach it (the second to validate is refused).

**Evidence**
- **FR-006**: Each Disbursement MUST require exactly one **bank receipt** and exactly one **invoice**, each carrying a file, an amount, a currency, a document reference number, and a document date.
- **FR-007**: A contract MUST NOT substitute for the invoice; there is no contract concept in this slice.
- **FR-008**: The system MUST validate uploaded files for type, maximum size, and content-signature safety, reusing the platform's existing document-storage controls.
- **FR-009**: A Disbursement MUST NOT be validatable until **both** the bank receipt and the invoice are present.
- **FR-010**: Before validation, replacing either file MUST overwrite the prior file with no version chain; after validation, replacement MUST be refused.

**Reconciliation**
- **FR-011**: On every create or edit of a Disbursement or its evidence, the system MUST run exactly three comparisons: (a) Disbursement amount vs bank receipt amount; (b) Disbursement amount vs invoice amount; (c) sum of Disbursements vs executed agreement total.
- **FR-012**: Reconciliation MUST use a **zero-colón** tolerance and MUST detect a difference as small as one colón. The tolerance MUST NOT be configurable in this slice.
- **FR-013**: When amounts do not match, the system MUST record a discrepancy showing expected amount, actual amount, difference, source document, and severity.
- **FR-014**: Discrepancy status MUST be communicated by icon and text label, never by color alone.
- **FR-015**: Every discrepancy in this slice MUST be **blocking**: a Disbursement with any unresolved discrepancy MUST be in state **Inconsistent** and MUST NOT be validatable.
- **FR-016**: After any correction, the system MUST re-run reconciliation automatically.

**Ledger & balance**
- **FR-017**: Balances MUST be derived from an **append-only ledger**, never from a stored mutable balance value. In this slice the ledger has exactly two entry types: **Allocation** and **Disbursement**.
- **FR-018**: The ledger MUST hold only committed facts: the Allocation entry, plus one immutable Disbursement entry posted at the moment of validation. A recorded-but-not-yet-validated Disbursement MUST be a mutable, off-ledger "pending" record.
- **FR-019**: The system MUST present a participant balance with five dimensions — **Allocated, Paid, Validated, Pending validation, Available** — defined as: Allocated = executed agreement total; Validated = sum of validated Disbursements; Pending validation = sum of pending Disbursements; Paid = Validated + Pending; **Available = Allocated − Paid**.
- **FR-020**: `Available` MUST reduce at the moment of payment (recording a Disbursement), independent of validation. A recorded-but-blocked over-disbursing Disbursement (FR-005) still counts toward `Paid` because the money has left the bank; consequently `Available` MAY present as **negative**, which MUST be shown plainly as an over-disbursement signal until the condition is resolved (never clamped to zero or hidden).
- **FR-021**: A given Disbursement MUST NOT be counted more than once in any balance figure.
- **FR-022**: All monetary values MUST use exact decimal arithmetic; floating-point MUST NOT be used for monetary amounts or totals.
- **FR-023**: Every balance figure MUST be explainable from its underlying ledger entries and pending records (no unexplained numbers).

**Roles & workflow**
- **FR-024**: The system MUST introduce a new **Financial Operator** role that is **group-scoped** using the platform's existing role-and-group mechanism; the operator MUST act only within agreements belonging to their groups and MUST see only the financial surface, not the full application.
- **FR-025**: **Auditor** and **Admin** users MUST have read-only visibility into Disbursements, balances, and discrepancies.
- **FR-026**: A Disbursement MUST follow the state machine: **Recorded** → **Inconsistent** (has a blocking discrepancy) or **Validated** (via an explicit Validar action gated on both evidence present AND zero discrepancies); **Cancelled** is reachable from any pre-validation state.
- **FR-027**: **Validar** MUST be an explicit human action, and in this slice it MUST NOT require a different actor from the one who recorded the Disbursement (no segregation of duties yet).
- **FR-028**: A pre-validation Disbursement MUST be freely editable, file-replaceable, and cancellable; a validated Disbursement MUST be locked against edit and deletion.
- **FR-029**: Out-of-group access to a financial surface MUST return a flat not-found (no disclosure); applicants/participants MUST be refused.

**Audit**
- **FR-030**: Every Disbursement action (create, edit, replace, validate, cancel) MUST be written to the platform's existing audit trail with actor, timestamp, and before/after values.
- **FR-031**: A validated Disbursement MUST NOT be destructively deleted; correction of a validated Disbursement is out of scope for this slice (requires a later reversal/credit-note capability).

### Key Entities *(include if feature involves data)*

- **Disbursement**: a recorded money movement against one executed funding agreement (one participant). Attributes: payment date, amount (CRC), bank transaction reference, optional bank/account reference, state (Recorded / Inconsistent / Validated / Cancelled).
- **Bank Receipt** (evidence): the proof that the bank moved money; file + amount + currency + reference number + document date; exactly one per Disbursement.
- **Invoice** (evidence): the billed document justifying the payment; file + amount + currency + reference number + document date; exactly one per Disbursement.
- **Ledger Entry**: an append-only, immutable record of a committed financial fact; type ∈ {Allocation, Disbursement}; carries amount and links to the agreement/participant; Disbursement entries are posted at validation.
- **Discrepancy**: a detected mismatch attached to a Disbursement; comparison type, expected amount, actual amount, difference, source document, severity.
- **Participant Balance** (projection, not stored): the five dimensions Allocated / Paid / Validated / Pending validation / Available, derived from ledger entries and pending Disbursements.
- **Financial Operator** (role): a group-scoped actor authorized to record and validate Disbursements within its groups.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A bank receipt or invoice amount that differs from the disbursement by one colón produces a blocking discrepancy that names the source document, and the disbursement cannot be validated (reference case: pago ₡85,800 vs factura ₡85,728 → ₡72).
- **SC-002**: A disbursement missing either required document cannot be validated, and the system states which document is missing.
- **SC-003**: After a mismatched amount is corrected to match, reconciliation clears automatically and the disbursement can be validated without any further manual re-check.
- **SC-004**: At every step, the five balance dimensions reconcile exactly to their definitions (`Available = Allocated − Paid`; `Paid = Validated + Pending`), and each figure traces to underlying transactions.
- **SC-005**: The total of a participant's non-cancelled disbursements can never exceed the executed agreement total; an attempt is blocked.
- **SC-006**: A validated disbursement cannot be edited or deleted through any path.
- **SC-007**: 100% of disbursement actions appear in the audit trail with actor and before/after values.
- **SC-008**: A Financial Operator can act only within their groups (out-of-group agreements are indistinguishable from non-existent), while Auditor and Admin see the same data read-only and applicants are refused.

## Assumptions

- The **executed funding agreement** (existing platform concept) is the source of the participant's allocation; its approved total — taken as its **CRC** total — is the ceiling for disbursements. No new allocation object is introduced in this slice.
- The over-disbursement condition (comparison "c") is an **agreement-level** result; the slice attributes it as a blocking discrepancy on the disbursement whose recording first crosses the ceiling, and it clears if earlier disbursements are cancelled or the offending one is reduced.
- File storage, upload-size guards, and content-signature validation are provided by the platform's existing document-storage capability (as used by funds-usage evidence); no new storage infrastructure is added.
- The **Financial Operator** role reuses the same role-and-group scoping mechanism already used for the Auditor role, including admin group assignment and a role-aware sidebar entry.
- The audit trail reuses the platform's existing version-history / admin-audit facilities; no new audit subsystem is introduced.
- All user-facing copy is es-CR.
- Schema changes follow the existing database-project (dacpac) pattern; **no new managed dependencies** are added (exact-decimal support is native).
- Balance updates are transactional: a failure never leaves a partially updated balance.
- Reconciliation is deterministic: the same inputs always yield the same discrepancy result.

## Out of Scope

Each parked item maps to a later slice of the financial-execution program (see the brainstorm document for full detail):

- **Tranches & budget-lines** (subdividing the allocation, per-line attribution) — *slice P2*.
- **Full evidence graph** — typed metadata for many document types, many-to-many linking and allocation across lines, document version history — *slice P3*.
- **Full reconciliation engine** — multi-level (document → payment → line → participant → tranche → bank), non-blocking warnings, discrepancy lifecycle (assigned → under-correction → waived), reconciliation dashboard — *slice P4*.
- **Foreign-currency execution** — bank-applied rate on payment date, approved-vs-paid preservation, re-acceptance/addendum — *slice P5*.
- **Interest, bank fees, refunds, reimbursements, reversals, credit notes, manual adjustments** — *slice P6*.
- **Reporting** — execution report, participant statements, tranche/agency summaries, SBD-code exports — *slice P7*.
- **Segregation of duties** — no self-validation, approval thresholds, delegated approval — *slice P8*.
- **Migration / spreadsheet import** — dry-run, duplicate detection, migration audit — *slice P9*.
- **Multi-agency & tenant isolation** and **Mentori synchronization** — parked entirely (no slice).
- **Participant self-service balance view** — Phase 2.
