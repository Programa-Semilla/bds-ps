# Feature Specification: Evidence Graph & Required-Document Rules

**Feature Branch**: `047-evidence-graph-required-docs`
**Created**: 2026-07-16
**Status**: Draft
**Program**: Financial-execution program **slice P3 of 9** — builds on P1 (`045-financial-disbursement-core`) and P2 (`046-tranches-budget-lines`).
**Input**: User description: financial-execution slice P3 — turn the thin, hard-coded P1/P2 evidence model into a configurable, versioned evidence graph with per-line amount allocation, admin-defined required-document rules, a signed-acceptance reconciliation leg, and an explicit budget-line closure gate.

## Context

After a funding agreement executes, a **Financial Operator** records **Disbursements** (payments) against a participant's **budget-lines** (each budget-line **is** the existing application line `Item`). Today (P1/P2) each disbursement carries exactly **one Bank Receipt + one Invoice**, 1:1, reconciled to the colón against the payment; documents are replaced in place with no history; and there is no notion of a document being *required* nor of a budget-line being *closed*. That thin model cannot express the agency's real evidence process, which is the source of the audit and reconciliation pain this platform exists to solve.

This slice makes evidence a **first-class, configurable, versioned graph** linking documents to budget-lines, and adds the **closure gate** that prevents a budget-line from being finished while required evidence is missing or amounts do not reconcile to the colón.

Terminology: **budget-line = the application line `Item`**; **participant = the applicant**; **payment/disbursement** are used interchangeably. Amounts are Costa Rican colones (CRC) at `decimal(18,2)`; the reconciliation tolerance is **zero** (smallest detectable difference ₡0.01).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Typed evidence graph with per-line allocation (Priority: P1)

A Financial Operator attaches a supporting document (e.g. an invoice) to an executed application, records its metadata (type, amount, currency, document number, document date, optional supplier), and **allocates its amount across one or more budget-lines**. One document can support several budget-lines; one budget-line can be supported by several documents. Documents that are not tied to a single payment (signed acceptance, credit note, refund receipt, other) can be attached and allocated to budget-lines **without** a disbursement.

**Why this priority**: This is the substrate every other story builds on. Without a typed, many-to-many, per-line-allocated evidence model, there is nothing for the required-doc rules to check against and nothing to reconcile at closure. It is independently valuable: it already delivers AC-002/AC-003 allocation behaviour and a richer evidence record than P1.

**Independent Test**: On an executed application, attach one invoice, link it to budget-lines 1–4, distribute its amount across them, and confirm the allocation total cannot exceed the invoice amount; separately attach five invoices to a single budget-line and confirm all five are retained and summed.

**Acceptance Scenarios**:

1. **Given** an executed application with budget-lines 1, 2, 3, 4, **When** the operator attaches one invoice of ₡400,000 and allocates ₡100,000 to each line, **Then** the invoice links to all four lines and the allocation total (₡400,000) reconciles to the invoice amount. *(AC-002)*
2. **Given** the same invoice, **When** the operator tries to allocate a total exceeding ₡400,000, **Then** the system refuses the allocation and states that allocations cannot exceed the document amount. *(invariant 5)*
3. **Given** a single budget-line, **When** five separate invoices are attached and allocated to it, **Then** all five are retained and their allocated amounts sum for that line. *(AC-003)*
4. **Given** a signed acceptance document, **When** the operator attaches it and allocates it across budget-lines with no disbursement selected, **Then** the acceptance is stored and linked to those lines. *(payment-independent evidence)*
5. **Given** an attempt to save a document with no budget-line link and no disbursement link, **When** the operator submits, **Then** the system refuses it as an orphaned document. *(FR-050)*

---

### User Story 2 - Required-document rules & live completeness (Priority: P2)

An Admin configures, per **Category** (the purchase/transaction type) or a single **global default**, which of the evidence types are **Required**. A Financial Operator viewing a budget-line sees, live, which required documents are **present** and which are **missing**, recomputed on read.

**Why this priority**: Turns the hard-coded "receipt + invoice" rule into the configurable completeness matrix the seed demands (§10.8), and produces the "what's still missing" signal (FR-031, AC-005) that the closure gate consumes. Independently valuable as a visible completeness view even before closure exists.

**Independent Test**: As Admin, mark Invoice + Signed Acceptance as Required for a category; as Financial Operator, open a budget-line of that category with only a bank receipt attached and confirm the completeness view lists Invoice and Signed Acceptance as missing.

**Acceptance Scenarios**:

1. **Given** an active rule set marking Invoice as Required for category *Producto*, **When** a *Producto* budget-line has a bank receipt but no invoice, **Then** its completeness view shows the invoice as missing. *(AC-005, FR-031)*
2. **Given** a category with no specific rule, **When** a budget-line of that category is viewed, **Then** the global default rule set determines its required documents.
3. **Given** an admin edits the active rule set to add a new required type, **When** an **open** (not-closed) budget-line is next viewed, **Then** the new requirement applies to it immediately.
4. **Given** a budget-line already **closed**, **When** the admin later changes the rule set, **Then** the closed line is unaffected (no retroactive re-opening or re-validation).

---

### User Story 3 - Budget-line closure with reconciliation gate (Priority: P3)

A Financial Operator **closes** a budget-line once its evidence is complete and its amounts reconcile. Closure is blocked while any required document is missing, any attributed payment is not yet validated, the per-line equality chain is off by ≥ ₡0.01, or any required document is not fully allocated. A closed line locks its evidence; the operator can **reopen** it with a required reason (audited).

**Why this priority**: This is the payoff — the enforceable "cannot finish with missing or mismatched evidence" control (FR-032, BR-012/013, invariants 3/4). Depends on US1 (evidence to allocate) and US2 (rules to check).

**Independent Test**: Build a budget-line whose payment, invoice allocations, and signed-acceptance allocations all equal to the colón with all required docs present; close it successfully. Then introduce a ₡72 invoice shortfall and confirm closure is refused with the discrepancy shown.

**Acceptance Scenarios**:

1. **Given** a budget-line with all required documents present, every attributed payment validated, and payment = invoiced = accepted to the colón, **When** the operator closes it, **Then** it reaches the **Closed** state.
2. **Given** a budget-line where the invoice allocated to it totals ₡85,728 but the payment is ₡85,800, **When** the operator attempts to close it, **Then** closure is refused, a ₡72 discrepancy is shown on the line, and the line does not close. *(AC-001 shape)*
3. **Given** a budget-line missing a required signed acceptance, **When** the operator attempts to close it, **Then** closure is refused and the missing document is named. *(AC-005)*
4. **Given** a budget-line whose attributed payment is still Recorded (not Validated), **When** the operator attempts to close it, **Then** closure is refused pending validation.
5. **Given** a **closed** budget-line, **When** the operator attaches or replaces evidence on it, **Then** the action is refused until the line is reopened.
6. **Given** a closed budget-line, **When** the operator reopens it with a reason, **Then** the line unlocks, the reopen is audited with actor/reason/timestamp, and no balance or ledger entry changes.

---

### User Story 4 - Evidence version history (Priority: P4)

When a document is corrected — its file replaced, or a reconciliation-critical field (amount, currency, document number, document date) edited — the prior version is **retained as superseded** and a new current version is appended, with a required reason. Every version records who, when, why, and a file integrity hash. All versions remain viewable and downloadable by authorized roles.

**Why this priority**: The audit-trail requirement (FR-042/043/044, Risk 5, AC-008): corrections must never erase history. Independent of closure; layers onto US1.

**Independent Test**: Attach an invoice, then replace it with a corrected file and reason; confirm both the original and the replacement are viewable with their actor, timestamp, and reason, and that the original is marked superseded.

**Acceptance Scenarios**:

1. **Given** an attached invoice, **When** the operator replaces its file with a reason, **Then** a new current version is created, the prior version is retained as superseded, and both are viewable with actor/timestamp/reason. *(AC-008, P3 portion)*
2. **Given** an attached invoice, **When** the operator edits its amount, **Then** the change appends a new version (the prior amount is preserved in history) and requires a reason.
3. **Given** a document with multiple versions, **When** an authorized reviewer opens its history, **Then** every version is downloadable and shows its integrity hash.
4. **Given** a replace attempt with no reason, **When** the operator submits, **Then** the system refuses it and requires a reason.

---

### Edge Cases

- **Allocation exactly at the boundary**: total allocations equal to the document amount to the colón is valid; one colón over is refused.
- **Partial allocation before closure**: a document may be under-allocated (Σ allocations < amount) while a line is open; at closure each *required* document must be **fully** allocated (Σ = amount).
- **Reconciliation-critical edit after allocation**: reducing a document's amount below its already-allocated total must be refused (would strand an over-allocation) or force re-allocation; the system must not allow allocations to silently exceed the amount.
- **Closing a line with no payment**: a budget-line with no attributed payment cannot satisfy the equality chain and therefore cannot be closed (it is completed via cancellation paths, not closure).
- **Reopen of a line whose category rule changed while closed**: on reopen the line is re-subjected to the **then-current** rule set, which may now show newly-missing documents.
- **Concurrent edits**: two operators editing the same document or closing the same line concurrently must not corrupt allocations or double-close (optimistic concurrency).
- **Out-of-group / non-executed application**: a Financial Operator outside the application's group, or an application not in the executed state, must receive a flat not-found with no disclosure (identical to P1/P2).
- **Credit Note / Refund Receipt at closure**: these are evidence-only; if a category marks one Required it must be *present* for completeness, but it contributes **no** amount to the equality chain.

## Requirements *(mandatory)*

### Functional Requirements

**Evidence graph & allocation (US1)**

- **FR-001**: The system MUST represent evidence as a first-class record scoped to an application, carrying at minimum: evidence type, amount, currency (CRC), document number, document date, optional supplier, the stored file, the uploading user, and the upload timestamp.
- **FR-002**: The system MUST support exactly these evidence types: **Bank Receipt, Invoice, Signed Acceptance, Credit Note, Refund Receipt, Other**.
- **FR-003**: The system MUST allow an evidence record to link to a disbursement and/or to one or more budget-lines. Signed Acceptance, Credit Note, Refund Receipt, and Other MUST be attachable to budget-lines with no disbursement.
- **FR-004**: The system MUST allow an evidence record to be allocated across multiple budget-lines with a per-link allocated amount, and a budget-line to receive allocations from multiple evidence records (many-to-many).
- **FR-005**: The system MUST refuse to persist an allocation set whose total exceeds the evidence record's own amount.
- **FR-006**: The system MUST continue to accept the existing P1 disbursement Bank Receipt + Invoice and treat them as satisfying the receipt/invoice requirement; P1's disbursement-level reconciliation MUST remain unchanged.
- **FR-007**: The system MUST refuse to persist an evidence record that links to neither a budget-line nor a disbursement (no orphaned documents).

**Required-document rules & completeness (US2)**

- **FR-008**: An Admin MUST be able to configure, per Category or for a single global default, which evidence types are Required.
- **FR-009**: The system MUST resolve a budget-line's required-document set from its category's rule, falling back to the global default when the category has no specific rule.
- **FR-010**: The system MUST display, per budget-line and recomputed on read, which required document types are present and which are missing.
- **FR-011**: The system MUST keep at most one active rule set; editing it MUST preserve any history referenced by already-closed lines.
- **FR-012**: Rule-set changes MUST apply immediately to open (not-closed) budget-lines and MUST NOT retroactively affect already-closed lines.
- **FR-013**: Only the per-Category and global-default axes are in scope; other configuration axes (payment type, supplier type, amount threshold, currency, agency) are explicitly deferred.

**Closure & reopen (US3)**

- **FR-014**: A Financial Operator MUST be able to close a budget-line.
- **FR-015**: The system MUST refuse closure unless ALL hold: (a) every required document type for the line's category is present and linked; (b) every payment attributed to the line is Validated; (c) the per-line equality chain reconciles to the colón; (d) every required document is fully allocated (its allocation total equals its amount).
- **FR-016**: A closed budget-line MUST lock its evidence — no attach, replace, or re-allocation — until reopened.
- **FR-017**: A Financial Operator MUST be able to reopen a closed budget-line, supplying a required reason; reopening MUST unlock the line.
- **FR-018**: Closure and reopen MUST NOT post a ledger entry or change any balance dimension (off-ledger operational milestones).
- **FR-019**: The derived budget-line status MUST expose a **Closed** terminal state above the existing Validated status, and an **EvidenceIncomplete** indicator when required documents are missing.

**Version history (US4)**

- **FR-020**: Replacing an evidence file, or editing a reconciliation-critical field (amount, currency, document number, document date), MUST append a new current version and retain the prior version as superseded.
- **FR-021**: Every version-creating action MUST require a reason and MUST record the acting user, timestamp, reason, and a file integrity hash.
- **FR-022**: All versions of an evidence record MUST remain viewable and downloadable by authorized roles.
- **FR-023**: The system MUST NOT include an accept/reject document review workflow in this slice (deferred).

**Reconciliation (US1/US3)**

- **FR-024**: At closure, for each budget-line the system MUST verify to the colón that `Σ validated payment allocations = Σ invoice allocations = Σ signed-acceptance allocations`, and block closure on any mismatch.
- **FR-025**: All reconciliation checks in this slice MUST be zero-tolerance and blocking; the system MUST NOT introduce non-blocking warnings, severity levels, or a discrepancy lifecycle (deferred).
- **FR-026**: Credit Note and Refund Receipt MUST be attachable, requirable, and versioned, but MUST contribute no amount to any reconciliation check and MUST cause no balance or ledger effect in this slice.

**Access control & audit**

- **FR-027**: Only a Financial Operator (scoped to the application's group) MUST be able to attach/replace/allocate evidence and close/reopen budget-lines. Auditor and Admin MUST be read-only on evidence and version history. Out-of-group or non-executed access MUST return a flat not-found with no disclosure.
- **FR-028**: Configuring the required-document rule matrix MUST be an Admin capability and MUST be the only Admin write in this slice.
- **FR-029**: The system MUST audit every evidence attach/replace/allocate, every closure and reopen (with reason), and every rule-matrix change, recording actor, timestamp, and before/after where applicable.

### Non-Functional Requirements

- **NFR-001**: Monetary values MUST use exact decimal arithmetic (`decimal(18,2)`); no floating-point for money.
- **NFR-002**: Evidence, allocation, closure, and version writes MUST be transactional — a failure MUST NOT leave a partially-allocated or half-closed record.
- **NFR-003**: All user-facing copy MUST be Costa Rican Spanish (es-CR); no English-only UI.
- **NFR-004**: The slice MUST NOT add managed (NuGet) dependencies and MUST be additive-only at the schema level.
- **NFR-005**: File uploads MUST reuse the existing storage stack and enforce the existing file-type (magic-byte), size (20 MiB cap), and security validation (covers FR-049).

### Key Entities

- **Evidence record**: an application-scoped supporting document — type, amount, currency, document number, document date, optional supplier, stored file, uploader, upload time; optionally linked to a disbursement; the head of a version chain.
- **Evidence version**: an immutable snapshot in a document's history — file pointer, the reconciliation-critical field values at that time, actor, timestamp, reason, integrity hash; exactly one is current, the rest superseded.
- **Evidence-to-line allocation**: a link between an evidence record and a budget-line carrying the amount allocated to that line (many-to-many).
- **Required-document rule set**: the active admin-configured matrix mapping Category (or the global default) to the set of Required evidence types.
- **Budget-line closure**: the closed/open state of a budget-line plus closure metadata (who closed/reopened, when, reopen reason); an operational milestone, not a ledger fact.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: One invoice can be linked to four budget-lines with its amount distributed across them; the allocation total cannot exceed the invoice amount and reconciles to it. *(AC-002)*
- **SC-002**: One budget-line can carry five invoices whose amounts are summed and compared against the line's payment and approved amounts, with missing/excess detected. *(AC-003)*
- **SC-003**: A budget-line with a bank receipt but a required missing invoice shows the invoice as missing and cannot be closed. *(AC-005)*
- **SC-004**: A budget-line cannot reach Closed while any required document is missing or the per-line equality chain is off by ≥ ₡0.01; a fully-reconciled, fully-documented line closes successfully.
- **SC-005**: Replacing a corrected invoice preserves the original version, viewable with its actor and reason. *(AC-008, P3 portion)*
- **SC-006**: Pre-P3 executed applications (receipt + invoice only, no closure) continue to function unchanged — existing P1/P2 disbursement recording, validation, and balances are unaffected (regression).
- **SC-007**: A Financial Operator outside the application's group cannot see or act on its evidence, receiving a flat not-found.

## Assumptions

- Budget-line is the existing application line `Item`; participant is the applicant; the Financial Operator role, group-scoping, executed-state gating, and storage/upload-guard machinery from P1/P2 are reused as-is.
- "Category" is the spec-035 line-item category and is the purchase/transaction-type axis of the §10.8 completeness matrix.
- The required-document matrix follows the spec-040 ChecklistTemplate "one active configuration" admin pattern; the live completeness check follows the spec-035 `CategoryField` template-vs-instance pattern.
- Closure is a per-budget-line operational milestone (not per-disbursement and not a money movement), so it stays off the append-only ledger — mirroring P2's off-ledger treatment of `Committed`.
- Signed Acceptance participates in the per-line equality chain symmetrically with Invoice; Bank Receipt reconciles at the disbursement level (P1, unchanged) and is not part of the line-level chain.
- Retention, backup, and durability of stored evidence are provided by the existing storage stack.

## Dependencies

- **P1 (`045-financial-disbursement-core`)**: Disbursement, DisbursementEvidence, participant balance projection, disbursement-level reconciliation, Financial Operator role, storage stack, executed-state gating.
- **P2 (`046-tranches-budget-lines`)**: budget-line == `Item`, per-line payment allocation, commit state, derived budget-line status, tranche composition.
- **Spec 035** (line-item category templates) — Category as the rule axis; live completeness pattern.
- **Spec 040** (checklist templates) — the active-configuration admin pattern.
- **Spec 036/045** storage + `EvidenceFileTypePolicy` (magic-byte type gate, size cap).

## Out of Scope

- Non-blocking warnings, severity model, discrepancy lifecycle, reconciliation dashboard → **P4**.
- Foreign-currency execution, Bank Statement evidence, Exchange-Rate Adjustment evidence → **P5**.
- Payment reversals and the money semantics of Credit Notes / Refunds → **P6**.
- Reporting, participant statements, exports → **P7**.
- Segregation of duties, a separate Approver role, no-self-approval → **P8**.
- Spreadsheet import / migration → **P9**.
- Multi-agency and tenant isolation; participant self-service upload; OCR document parsing.
- Required-document configuration axes beyond per-Category + global default (payment type, supplier type, amount threshold, currency, agency) — documented seams, not built.
- Accept/reject evidence review workflow (Accepted/Rejected/PendingReview states).

## Open Questions

*(Design-phase decisions deferred to `/speckit-plan`; none block spec approval.)*

- **OQ-1**: Generalize the existing `DisbursementEvidence` table into the new application-scoped evidence entity, versus add a new evidence table alongside it (migration shape).
- **OQ-2**: The exact representation of the **Closed** state on the budget-line (stored state/flag versus extending the derived status) plus its closure metadata.
- **OQ-3**: Whether the version chain is a child table of the evidence record or a generic document-version table.
