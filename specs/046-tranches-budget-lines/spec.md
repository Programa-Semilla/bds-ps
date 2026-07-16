# Feature Specification: Tranches & Budget-Lines (Financial Execution P2)

**Feature Branch**: `046-tranches-budget-lines`
**Created**: 2026-07-16
**Status**: Draft
**Input**: User description: "Tranches & Budget-Lines (Financial Execution P2) — subdivide a participant's flat P1 allocation into tranches of budget-lines, attribute disbursements per-line (M:N), add a Committed balance dimension, and compose balances by tranche and line. Preserves P1's zero-colón discipline."

## Overview

Slice **P2 of 9** in the financial-execution program. P1 (spec 045) established money execution downstream of an executed funding agreement: a Financial Operator records disbursements against a participant's flat allocation, with to-the-colón reconciliation and a five-dimension balance projection computed at the participant (application) level. P2 turns that single flat number into a **structured, line-level execution picture**: the allocation is subdivided into **tranches** (funding phases) of **budget-lines**, disbursements are attributed to the specific lines they pay (many-to-many), a **Committed** balance dimension captures the obligate-before-pay step, and balances compose by tranche and line — all while preserving P1's zero-colón discipline.

A **budget-line is the existing application line item** (`Item`) — the platform's approved, priced, supplier-selected line. No new line concept is introduced.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reviewer subdivides the allocation into tranches (Priority: P1)

While preparing a participant's funding agreement, the reviewer groups the application's approved line items into one or more named tranches (funding phases, e.g. "Tramo 1", "Tramo 2"). Each line item belongs to exactly one tranche. Each tranche's amount is the sum of its lines' budgets, so the tranches always partition the allocation exactly. If the reviewer defines no tranches, the system treats the application as having a single tranche holding all lines. The structure is frozen when the agreement executes.

**Why this priority**: The tranche/line structure is the foundation every other P2 behavior composes onto. On its own it delivers value — the operator, auditor, and reports can now see the allocation broken into phases and lines — and it keeps every existing executed application working via the single-default-tranche rule.

**Independent Test**: Create an application with several line items, group them into two tranches during agreement preparation, execute, and confirm the allocation displays composed by tranche and line with Σ tranche amounts equal to the allocation to the colón. Confirm an application with no tranches defined shows one default tranche containing all lines.

**Acceptance Scenarios**:

1. **Given** an application in the funding-agreement stage with 4 priced line items, **When** the reviewer assigns items 1–2 to "Tramo 1" and items 3–4 to "Tramo 2", **Then** each tranche's amount equals the sum of its lines' budgets and the two tranche amounts sum exactly to the allocation.
2. **Given** an application whose reviewer defined no tranches, **When** the agreement executes, **Then** the system presents a single default tranche containing all line items with amount equal to the allocation.
3. **Given** an executed application with defined tranches, **When** anyone attempts to change the tranche structure or reassign a line, **Then** the change is refused (structure frozen at execution).
4. **Given** an application already executed before this feature shipped, **When** its balances are viewed, **Then** it behaves as a single-tranche application with no data change and identical participant-level totals.

---

### User Story 2 - Financial Operator commits budget-lines before paying (Priority: P2)

After execution, the Financial Operator obligates budget to a line by **committing** it — a distinct step before any payment, reflecting a formal obligation to the supplier. Committing a line contributes its budget to the participant's (and tranche's) **Committed** balance. A line can be un-committed freely until the first payment is recorded against it. Only committed lines can receive disbursement attributions.

**Why this priority**: The Committed dimension is the headline new balance concept (FR-014 of the seed) and the obligate-then-pay gate that makes per-line attribution meaningful. It depends on US1's structure but is independently demonstrable.

**Independent Test**: On an executed, tranched application, commit two of four lines and confirm Committed equals the sum of those two lines' budgets at line, tranche, and participant levels; un-commit one and confirm Committed drops accordingly; confirm a line with no commitment cannot receive a disbursement attribution.

**Acceptance Scenarios**:

1. **Given** an executed application with four lines all uncommitted, **When** the operator commits lines A and B, **Then** Committed = budget(A) + budget(B) at line, tranche, and participant levels, and Committed ≤ Allocated.
2. **Given** a committed line with no recorded payment, **When** the operator un-commits it, **Then** the commitment is removed and Committed decreases by that line's budget.
3. **Given** a committed line that already has a recorded payment, **When** the operator attempts to un-commit it, **Then** the action is refused with a clear reason.
4. **Given** an uncommitted line, **When** the operator attempts to attribute a disbursement to it, **Then** the attribution is refused (obligate-then-pay).

---

### User Story 3 - Financial Operator attributes disbursements to lines (Priority: P2)

When recording a disbursement, the operator splits its amount across one or more committed budget-lines. One payment can cover several lines; one line can be paid by several payments (many-to-many). A single disbursement may attribute to lines in different tranches. The sum of a disbursement's line-allocations must equal the disbursement amount exactly. Paid, Validated, Pending, and Available now compose per line and per tranche. Over-paying a line is a blocking discrepancy caught at validation.

**Why this priority**: Per-line attribution is what makes the composed balances real (Paid/Validated/Pending only exist per-line once payments are attributed). Depends on US1 and US2.

**Independent Test**: On a tranched application with committed lines, record a disbursement split across two lines in two different tranches, confirm the split sums to the disbursement amount and Paid composes correctly per line/tranche; attempt a split that doesn't sum to the amount and confirm rejection; attempt to validate a disbursement that over-pays a line and confirm it is blocked.

**Acceptance Scenarios**:

1. **Given** a disbursement of ₡500,000 and committed lines X (₡300,000) and Y (₡200,000), **When** the operator allocates ₡300,000 to X and ₡200,000 to Y, **Then** the disbursement is accepted and Paid = ₡300,000 on X and ₡200,000 on Y, composing up to each line's tranche and the participant.
2. **Given** a disbursement of ₡500,000, **When** the operator's line-allocations sum to ₡450,000 or ₡550,000, **Then** the disbursement is rejected (split integrity, zero-colón, blocking).
3. **Given** two disbursements each partly attributed to the same line, **When** both are recorded, **Then** that line's Paid equals the sum of both attributions (one line, many payments).
4. **Given** a line committed at ₡300,000 with ₡250,000 already paid, **When** the operator records and attempts to validate a further ₡100,000 attribution to that line (total ₡350,000 > ₡300,000), **Then** validation is blocked with a per-line over-payment reason, re-checked against the freshly-read committed sum at the moment of validation.
5. **Given** any over-payment on a line, **When** balances are viewed, **Then** that line's (and its tranche's, and the participant's) Available presents negative and is never clamped to zero.

---

### User Story 4 - Viewing and filtering budget-lines (Priority: P3)

Operators and auditors view a participant's execution broken down by tranche and line, and filter the budget-lines by participant, tranche, status, supplier, validation state, and date to find the lines they need.

**Why this priority**: Navigational/reporting convenience over the P2 structure. Valuable but not required for the core execution mechanics.

**Independent Test**: On a participant with lines across multiple tranches, suppliers, and validation states, apply each filter and confirm the budget-line list narrows correctly.

**Acceptance Scenarios**:

1. **Given** a participant with lines in two tranches, **When** the user filters by "Tramo 2", **Then** only that tranche's lines are shown.
2. **Given** lines in different validation states, **When** the user filters by validation state, **Then** only matching lines are listed.

---

### Edge Cases

- **Pre-feature executed applications**: no tranche assignments exist → treated as a single default tranche holding all lines; participant-level totals are identical to P1 (no regression, no data rewrite).
- **Line with zero payments at end of execution**: allowed — a committed-but-unpaid line is a valid end state (closure/completeness rules are deferred to P3).
- **Un-commit a line that already has a recorded payment**: refused (would strand a payment).
- **Disbursement attributing to an uncommitted line**: refused (obligate-then-pay).
- **Line-allocations that don't sum to the disbursement amount**: refused (split integrity).
- **Over-payment on a line**: surfaces as negative Available at line, tranche, and participant levels (visible signal) and blocks validation (enforced).
- **Concurrent validation racing a fresh attribution**: the per-line over-payment check is re-evaluated against the committed sum read at validation time (symmetric with P1's participant-level over-disbursement gate).
- **Empty application (no line items)**: a degenerate single tranche of amount zero; no lines to commit or pay.

## Requirements *(mandatory)*

### Functional Requirements

**Tranche structure (US1)**

- **FR-001**: The reviewer MUST be able to define one or more named tranches for an application during the funding-agreement stage and assign each line item to exactly one tranche.
- **FR-002**: The system MUST treat an application whose reviewer defined no tranches as having a single default tranche containing all its line items.
- **FR-003**: A tranche's amount MUST be the sum of its assigned lines' budgets (derived, never entered by hand), guaranteeing that the sum of all tranche amounts equals the participant's allocation exactly.
- **FR-004**: The system MUST freeze the tranche structure and line-to-tranche assignments at execution and refuse any change thereafter.
- **FR-005**: The system MUST compute correct balances for applications executed before this feature by treating them as single-default-tranche applications, without rewriting their data.

**Commit / obligate (US2)**

- **FR-006**: The Financial Operator MUST be able to commit an individual budget-line, obligating its budget.
- **FR-007**: The Financial Operator MUST be able to un-commit a budget-line, but only while that line has no recorded payment.
- **FR-008**: The system MUST refuse any disbursement attribution to a line that is not committed.
- **FR-009**: Committing a line MUST be a reversible operational state, not an immutable settled-cash fact.

**Per-line payment attribution (US3)**

- **FR-010**: The Financial Operator MUST split each disbursement's amount across one or more committed budget-lines.
- **FR-011**: The system MUST support one payment covering multiple lines and one line covered by multiple payments (many-to-many).
- **FR-012**: The system MUST allow a single disbursement to attribute to lines belonging to different tranches; each attribution composes into its own line's tranche.
- **FR-013**: The system MUST refuse a disbursement whose line-allocations do not sum exactly to the disbursement amount (split integrity, zero-colón, blocking).

**Balance composition (US1–US3)**

- **FR-014**: The system MUST expose six balance dimensions — Allocated, Committed, Paid, Validated, Pending, Available — at the participant, tranche, and line levels.
- **FR-015**: Committed MUST equal the sum of committed lines' budgets and MUST never exceed Allocated at any level.
- **FR-016**: Paid MUST equal the sum of disbursement line-allocations (Validated + Pending); Validated MUST equal the sum of validated-disbursement line-allocations; Pending MUST equal the sum of recorded-but-unvalidated line-allocations.
- **FR-017**: Available MUST equal Allocated − Paid at each level and MUST be allowed to present negative (never clamped) as an over-payment signal.

**Reconciliation (US3)**

- **FR-018**: The system MUST retain P1's three to-the-colón comparisons (disbursement ↔ bank receipt, disbursement ↔ invoice, participant Σ ↔ allocation), all blocking.
- **FR-019**: At validation (`Validar`), the system MUST block when the sum of payments attributed to any line of the disbursement exceeds that line's committed budget, re-checked against the committed sum read at validation time.

**Access & filtering (US4)**

- **FR-020**: Users MUST be able to filter budget-lines by participant, tranche, status, supplier, validation state, and date.
- **FR-021**: Only reviewers MUST be able to define tranches and assign lines to tranches; only Financial Operators MUST be able to commit lines, record disbursements, attribute them to lines, and validate. Auditors and Admins MUST be read-only for all of these actions.

**Traceability**

- **FR-022**: The system MUST record tranche definition, line commit/un-commit, and per-line disbursement attribution in the audit trail.

### Key Entities *(include if feature involves data)*

- **Tranche**: A named funding phase belonging to one application. Groups one or more line items. Its amount is derived (sum of its lines' budgets). Frozen at execution.
- **Budget-line**: The existing application line item (`Item`). Gains: membership in exactly one tranche, a commitment state (uncommitted/committed), and a budget = its selected-quote CRC amount.
- **Disbursement line-allocation**: The attribution of a portion of a disbursement's amount to a specific committed budget-line. Realizes the many-to-many relationship between payments and lines.
- **Composed balance**: The six-dimension balance (Allocated, Committed, Paid, Validated, Pending, Available) computed at participant, tranche, and line granularity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reviewer can subdivide any participant's allocation into tranches and, for every application, the sum of tranche amounts equals the allocation with zero colón difference.
- **SC-002**: 100% of accepted disbursements have line-allocations that sum exactly to the disbursement amount; none with a mismatched split are ever accepted.
- **SC-003**: For every participant, the six balances reconcile to the colón across participant, tranche, and line levels (each level's totals equal the sum of its children).
- **SC-004**: 100% of attempts to validate a disbursement that over-pays any line are blocked.
- **SC-005**: A financial operator can view a participant's execution broken down by tranche and line and narrow the budget-line list by each of the specified filters.
- **SC-006**: Every application executed before this feature displays balances identical to P1 (no regression), with no data migration required beyond the single-default-tranche interpretation.

## Assumptions

- Budget-line is the existing `Item`; a line's budget is its selected-quote CRC amount (the per-line component of the allocation total the platform already computes). No separate line-pricing concept is introduced.
- The allocation itself remains P1's figure (the executed funding-agreement CRC total). P2 subdivides it; it does not recompute it.
- Tranches are a **money partition**, not a time- or milestone-based release gate; no tranche unlocks or scheduling behavior is in scope.
- Committing a line is an operational status (reversible until first payment), tracked off the append-only ledger — commitment is an obligation, not a settled cash movement.
- Currency stays CRC end-to-end (P1); foreign-currency payment at bank rate is deferred to P5.
- Evidence stays 1:1 at the disbursement level (P1's one bank receipt + one invoice per disbursement); evidence↔line many-to-many allocation, document version history, required-document rules, and completeness/closure gates are deferred to P3.
- Non-blocking warnings, the discrepancy lifecycle, and the reconciliation dashboard are deferred to P4.
- Roles reuse the existing group-scoped Financial Operator (spec 045) and reviewer funding-agreement stage (spec 040) machinery; the audit trail reuses existing mechanisms.
- Schema changes are additive and dacpac-only; no new managed dependencies.

## Dependencies

- **Spec 045 (P1)**: Disbursement, disbursement ledger, participant-balance projection, reconciliation evaluator, and the Financial Operator role.
- **Spec 040**: the reviewer funding-agreement stage where tranche setup lives.
- The existing `Item` / allocation-total computation, the audit trail, and es-CR resources.
