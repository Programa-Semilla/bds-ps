# Feature Specification: Full Reconciliation Engine

**Feature Branch**: `048-full-reconciliation-engine`
**Created**: 2026-07-17
**Status**: Draft
**Program slice**: Financial-execution **P4 of 9** (roadmap: `brainstorm/41-financial-disbursement-platform.md`). Depends on P1 (spec 045), P2 (spec 046), P3 (spec 047), all shipped.
**Input**: Full Reconciliation Engine — turn the platform's zero-colón reconciliation from ephemeral, throw-at-the-gate checks into a persisted, stateful discrepancy-management system with a severity model (Blocking vs non-blocking Warning), a discrepancy lifecycle (assign / correct / resolve / waive) with per-discrepancy history, multi-level reconciliation coverage, and a group→agency reconciliation dashboard.

## Overview

Slices P1–P3 established money movement (disbursements, append-only ledger, tranches, budget-lines, evidence graph) with **every reconciliation check treated as a zero-colón hard block**: mismatches are computed on read at the moment an operator tries to validate a disbursement or close a budget-line, and they simply throw. Nothing about a discrepancy is remembered between requests — an operator cannot see the list of open problems, assign one to a colleague, track a correction in progress, or knowingly accept a benign anomaly.

P4 makes discrepancies **first-class, persisted, stateful records**. A discrepancy is detected as data changes, given a **severity** (Blocking or non-blocking Warning), moved through a **lifecycle** by the Financial Operator, and shown on a **reconciliation dashboard** with full per-discrepancy correction history. The zero-colón money guarantees from P1–P3 are preserved exactly: blocking discrepancies still prevent validation/closure, and the money gate still recomputes fresh at the decision instant. What is new is *visibility, tracking, governance, and the non-blocking warning tier* — the model that later slices (P5 currency, P6 adjustments, P7 reporting) all build on.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persisted discrepancies with severity (Priority: P1)

As a **Financial Operator**, when I record or edit a disbursement, allocate a line, attach or replace evidence, or attempt to validate/close, the system detects every reconciliation mismatch in the affected application and **records each one as a persisted discrepancy** with an expected amount, actual amount, difference, source, related participant/line, and a **severity** — either **Blocking** (a core zero-colón money identity) or **Warning** (a non-blocking advisory condition). Blocking discrepancies continue to prevent the record from reaching a validated or closed state, exactly as before; warnings never block.

**Why this priority**: This is the spine of the whole slice and the whole downstream program. Without persisted discrepancies carrying a severity, there is nothing to give a lifecycle, nothing to show on a dashboard, and no non-blocking tier for later slices to register rules against. It also preserves the money guarantee that is the product's entire reason for existing.

**Independent Test**: Record a disbursement whose invoice amount differs from the paid amount by one colón; verify a persisted **Blocking** discrepancy exists with correct expected/actual/difference and that validation is refused. Separately, create a condition matching a warning rule (e.g. two disbursements with the same supplier + amount + date); verify a persisted **Warning** discrepancy exists and that it does **not** block validation.

**Acceptance Scenarios**:

1. **Given** a disbursement whose invoice amount is 72 CRC less than the paid amount, **When** the operator saves it, **Then** a persisted Blocking discrepancy (comparison = paid-vs-invoice) is recorded with expected, actual, and a −72 difference, and any attempt to validate is refused.
2. **Given** an application with no mismatches, **When** reconciliation runs, **Then** no open discrepancies exist and validation/closure proceed.
3. **Given** two disbursements to the same supplier for the same amount on the same date, **When** reconciliation runs, **Then** a persisted **Warning** discrepancy (possible duplicate payment) is recorded and validation is **not** blocked by it.
4. **Given** an invoice dated after the payment it supports, **When** reconciliation runs, **Then** a non-blocking **Warning** (evidence date anomaly) is recorded.
5. **Given** a validated payment on a budget-line whose independently-allocated graph invoice differs, **When** reconciliation runs, **Then** a **Warning** (graph-invoice allocation drift) is recorded rather than the mismatch passing silently (absorbs spec 047 FINDING-13).

---

### User Story 2 - Discrepancy lifecycle with correction history (Priority: P1)

As a **Financial Operator**, I can move a discrepancy through a lifecycle: **assign** it to a responsible operator, mark it **under correction** while I work it, and reach **resolved**. A discrepancy that is fixed (numbers now match, or within an authorized tolerance) **auto-resolves** on the next reconciliation run. For **Warning** discrepancies only, I can **waive** one — deliberately accepting it — which **requires a reason** and is audited. **Blocking discrepancies can never be waived**; the only way to clear one is to make the numbers match. Every state change is recorded in the discrepancy's own **correction history** (who, when, from-state, to-state, reason/explanation), shown as a timeline.

**Why this priority**: Persisted discrepancies without a lifecycle are just a read-only list. The lifecycle is what lets a team actually manage reconciliation work — the core operational value of the slice — and the waive path is the mechanism that makes the non-blocking tier meaningful. The blocking/waive asymmetry is what protects the zero-colón invariant.

**Independent Test**: Assign an open discrepancy to an operator (verify state = Assigned, assignee set, history row added); mark it under correction; then either fix the underlying numbers and verify it auto-resolves on re-run, or (for a warning) waive it with a reason and verify state = Waived with the reason captured in history and an audit event written. Verify a waive attempt on a Blocking discrepancy is refused.

**Acceptance Scenarios**:

1. **Given** an open discrepancy, **When** the operator assigns it to a user, **Then** its state becomes Assigned, the assignee is recorded, and a history row captures the transition + actor.
2. **Given** an assigned Blocking discrepancy, **When** the operator corrects the underlying amounts so they match, **Then** on the next reconciliation run the discrepancy auto-transitions to Resolved (the row is retained, not deleted).
3. **Given** an open **Warning**, **When** the operator waives it with a required reason, **Then** its state becomes Waived, the reason is stored in the correction history, and a `discrepancy.waived` audit event is written with actor + before/after.
4. **Given** a **Blocking** discrepancy, **When** an operator attempts to waive it, **Then** the action is refused (blocking discrepancies cannot be waived).
5. **Given** a **Waived** Warning, **When** the underlying amount changes so the anomaly is now a different figure, **Then** the discrepancy reopens (the accepted condition no longer matches reality).
6. **Given** a resolved discrepancy whose condition recurs later, **When** reconciliation detects it again, **Then** it reopens as Open under the same stable identity (its prior history remains visible).

---

### User Story 3 - Reconciliation dashboard (Priority: P2)

As a **Financial Operator**, I have a **reconciliation dashboard** scoped to my groups' applications: summary tiles showing open discrepancy **count and amount by severity** with roll-ups by fund/process, and a **filterable list** of open/unresolved discrepancies by participant, tranche, budget-line, supplier, date, severity, responsible user, and lifecycle state. Each row deep-links to a **discrepancy detail** showing expected amount, actual amount, difference, source document, related participant, related budget-line, severity, required action, and the correction-history timeline. As an **Admin**, I see the same dashboard **agency-wide** (all groups/funds). As an **Auditor**, I see a group-scoped, **read-only** slice. Discrepancy status and severity are **never conveyed by color alone**.

**Why this priority**: The dashboard is the surface that turns persisted discrepancies + lifecycle into a usable control tool and satisfies the seed's agency-wide reconciliation view. It is P2 rather than P1 because the underlying records and lifecycle (US1/US2) are what deliver correctness; the dashboard makes them navigable.

**Independent Test**: Seed discrepancies across two groups; log in as a Financial Operator in one group and verify only that group's discrepancies appear; log in as Admin and verify both groups appear; log in as Auditor and verify the group-scoped read-only view has no lifecycle actions. Exercise each FR-065 filter and verify the list narrows correctly. Verify summary tiles reflect correct counts/amounts by severity.

**Acceptance Scenarios**:

1. **Given** open discrepancies in groups A and B, **When** a Financial Operator scoped to group A views the dashboard, **Then** only group-A discrepancies appear.
2. **Given** the same data, **When** an Admin views the dashboard, **Then** discrepancies from both groups appear (agency-wide).
3. **Given** an Auditor scoped to group A, **When** they view the dashboard, **Then** they see group-A discrepancies read-only with no assign/waive/resolve controls.
4. **Given** a mix of severities and suppliers, **When** the user filters by severity = Warning and a specific supplier, **Then** the list shows only matching open discrepancies.
5. **Given** a discrepancy row, **When** the user opens its detail, **Then** expected/actual/difference/source/participant/line/severity/required-action and the correction-history timeline are shown.
6. **Given** any discrepancy status indicator, **When** rendered, **Then** the status is communicated by text/icon and not by color alone.

---

### User Story 4 - Assignment notification (Priority: P3)

As a **Financial Operator**, when a discrepancy is **assigned to me**, I receive an email (through the existing notification outbox) so I know I am responsible for it. Notifications fire **on assignment only**, not on every detection.

**Why this priority**: A useful convenience that closes the loop on the lifecycle's assignment step, but the slice delivers its core value (US1–US3) without it. Detection-time notifications are deliberately excluded because blocking discrepancies flicker in and out constantly while an operator corrects numbers, which would make detection alerts noise.

**Independent Test**: Assign a discrepancy to a demo operator whose email is in the non-prod allowlist; verify exactly one assignment email is captured (smtp4dev) addressed to that operator, and that merely detecting a discrepancy (without assignment) produces no email.

**Acceptance Scenarios**:

1. **Given** an open discrepancy, **When** it is assigned to an operator, **Then** exactly one assignment notification is enqueued and delivered to that operator.
2. **Given** reconciliation detects new discrepancies, **When** no assignment occurs, **Then** no notification is sent.

---

### Edge Cases

- **Re-run stability**: an operator edits an unrelated field on an application that already has an Assigned discrepancy — the discrepancy must keep its state and assignee (matched by stable identity), not reset to Open.
- **Auto-resolve then recurrence**: a Blocking discrepancy resolves when numbers match, then a later edit reintroduces the mismatch — it reopens under the same identity with prior history intact.
- **Waived warning changes**: a waived possible-duplicate warning where a later edit changes the amount — the waiver no longer applies, so it reopens.
- **Concurrent lifecycle edits**: two operators act on the same discrepancy at nearly the same time — the second action must not silently overwrite the first (optimistic concurrency on the discrepancy record).
- **Money-gate race**: an operator validates while another edit is in flight — the fresh recompute at the gate (not the persisted snapshot) is authoritative, so a mismatch introduced after the last materialization still blocks validation.
- **Tolerance boundary**: with a rule tolerance of 0 CRC, a 1-colón difference is a discrepancy; the tolerance parameter exists but defaults to zero (configuration UI is out of scope, deferred to P5).
- **Blocking discrepancy assigned but unresolved**: closure/validation stays refused regardless of assignment or under-correction state.
- **Empty scope**: an operator with no group memberships sees an empty dashboard (no disclosure of other groups' data).

## Requirements *(mandatory)*

### Functional Requirements

**Detection & materialization**

- **FR-001**: The system MUST run reconciliation for an application whenever a relevant amount or evidence item in that application is added, edited, or removed (record/edit disbursement, allocate lines, attach/replace/delete evidence, validate, commit/uncommit, close/reopen).
- **FR-002**: Each reconciliation run MUST persist its results as discrepancy records (the visibility snapshot), scoped to the affected application, within the same unit of work as the triggering change.
- **FR-003**: Each discrepancy MUST have a **stable identity** of (scope-type, scope-entity-id, comparison-rule). On re-run, an already-present discrepancy MUST be updated in place (preserving lifecycle state, assignee, and history); a discrepancy no longer present MUST be auto-transitioned to Resolved (its row retained, never deleted); a newly-detected one MUST be inserted as Open.
- **FR-004**: The money gates (disbursement validation, budget-line closure) MUST recompute reconciliation **fresh at the decision instant** and block on that fresh result, independent of the persisted snapshot, preserving the P1–P3 race-proof guarantee.
- **FR-005**: Every reconciliation rule MUST carry a tolerance parameter with a default of **0 CRC**; a difference greater than the tolerance is a discrepancy. (Admin-facing tolerance configuration is out of scope — see Out of Scope.)
- **FR-006**: All monetary comparisons MUST use exact decimal arithmetic; the system MUST detect differences as small as one colón.

**Severity model**

- **FR-007**: Every reconciliation rule MUST have a **fixed severity**, either **Blocking** or **Warning**; severity is not user-configurable in this slice.
- **FR-008**: The following comparisons MUST be **Blocking**: paid-vs-bank-receipt, paid-vs-invoice, invoice-vs-signed-acceptance, Σ line-allocations-vs-disbursement, Σ per-line-payments-vs-committed-budget, participant Σ-vs-allocation, and the P3 completeness/closure equality legs.
- **FR-009**: A record MUST be prevented from reaching a validated or closed state while any **Blocking** discrepancy in its scope is unresolved. **Warning** discrepancies MUST never block validation or closure.
- **FR-010**: The system MUST detect and record the following **Warning** conditions (the P4 starter set): (a) evidence date anomalies — evidence dated **after its related payment date**, or dated **before the funding-agreement execution date** (both concrete, already-stored anchors); (b) possible duplicate payment (same supplier + amount + date across disbursements); (c) graph-invoice allocation drift (a line with a validated payment whose independently-allocated graph invoice differs). *(A fourth candidate — requested-vs-approved variance — was **dropped** during planning: research confirmed the platform stores no "requested" amount distinct from the executed allocation, so the rule is not computable. See research.md D4 / resolved OQ-5.)*

**Lifecycle & history**

- **FR-011**: A discrepancy MUST support the states **Open, Assigned, UnderCorrection, Resolved**; **Warning** discrepancies MUST additionally support **Waived**.
- **FR-012**: A Financial Operator MUST be able to assign a discrepancy to a responsible user and mark it under correction.
- **FR-013**: A **Blocking** discrepancy MUST reach **Resolved only** by the underlying amounts matching (within the rule's tolerance, which is 0 CRC by default in this slice), auto-transitioning on the reconciliation run that recomputes its scope clean. A Blocking discrepancy MUST NOT be waivable.
- **FR-014**: A **Warning** discrepancy MUST be waivable by a Financial Operator with a **required reason**; waiving MUST be audited. A waived Warning MUST reopen if the underlying amount changes.
- **FR-015**: The Financial Operator MUST drive all lifecycle transitions; Auditor and Admin MUST be read-only with respect to the lifecycle.
- **FR-016**: The system MUST maintain a per-discrepancy **correction history** capturing, per transition: timestamp, actor, from-state, to-state, and reason/explanation where applicable. The history MUST be viewable as a timeline on the discrepancy detail.
- **FR-017**: Lifecycle transitions and waivers MUST also write to the global admin audit trail under a `discrepancy.*` event family (actor, before/after).
- **FR-018**: Concurrent lifecycle edits on the same discrepancy MUST be detected so a later action does not silently overwrite an earlier one.

**Multi-level coverage**

- **FR-019**: Discrepancy-producing checks MUST run and persist at the **document, payment, budget-line, participant, and tranche** levels.
- **FR-020**: The dashboard MUST present **program and agency levels as roll-up views** (aggregations of the levels above), not as additional discrepancy-producing comparisons.

**Dashboard & visibility**

- **FR-021**: A Financial Operator MUST see a reconciliation dashboard scoped to their groups' applications; an Admin MUST see the agency-wide view (all groups/funds); an Auditor MUST see a group-scoped, read-only view.
- **FR-022**: The dashboard MUST show summary tiles of open discrepancy **count** and open discrepancy **amount** by severity, with roll-ups by fund/process.
- **FR-023**: The dashboard MUST provide a list of open/unresolved discrepancies filterable by **participant, tranche, budget-line, supplier, date, severity, responsible user, and lifecycle state**.
- **FR-024**: The discrepancy detail MUST surface expected amount, actual amount, difference, source document, related participant, related budget-line, severity, required action, and the correction-history timeline.
- **FR-025**: Discrepancy status and severity MUST NOT be communicated by color alone (text/icon required).
- **FR-026**: All user-facing copy MUST be es-CR.

**Notifications**

- **FR-027**: When a discrepancy is **assigned** to a responsible user, the system MUST notify that user by email (best-effort — a delivery failure MUST NOT block the assignment). The system MUST NOT notify on mere detection. *(Delivery mechanism is a plan decision — see research.md D6: a direct-send factory, not the stage-group outbox.)*

### Key Entities

- **Discrepancy**: a persisted, stateful record of a single detected mismatch, identified by (scope-type, scope-entity-id, comparison-rule). Attributes: scope (level + referenced entity), comparison rule, expected amount, actual amount, difference, source-document reference, related participant, related budget-line, severity (Blocking/Warning), lifecycle state, assignee, tolerance applied, timestamps, concurrency token. Scoped to an application.
- **DiscrepancyEvent**: an append-only child of Discrepancy recording each lifecycle transition — timestamp, actor, from-state, to-state, reason/explanation. Renders as the correction-history timeline (FR-016).
- **Reconciliation rule** (conceptual): a named comparison with a fixed severity and a tolerance parameter, evaluated by the reconciliation engine and producing zero or more Discrepancy records per run. Extends the existing P1–P3 comparison set with the P4 warning conditions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A discrepancy that is Assigned or UnderCorrection retains its state and assignee across unrelated re-runs of reconciliation on the same application (no reset to Open).
- **SC-002**: A Blocking discrepancy auto-transitions to Resolved on the first reconciliation run after its underlying amounts match, and reopens under the same identity if the mismatch later recurs, with prior history preserved.
- **SC-003**: A Warning discrepancy never blocks disbursement validation or budget-line closure; a Blocking discrepancy always does — verified on a fresh recompute at the gate even when a mismatch is introduced after the last materialization.
- **SC-004**: The P1–P3 money-gate regression suite (the SC-006 Disbursement / Tranche / BudgetLine / Evidence family) stays green — zero-colón validation/closure behavior is unchanged.
- **SC-005**: A Financial Operator sees only their groups' discrepancies; an Admin sees all groups; an Auditor sees a group-scoped read-only view; every FR-023 filter narrows the list correctly.
- **SC-006**: Waiving a Blocking discrepancy is impossible; waiving a Warning requires a reason, is recorded in the discrepancy's correction history, and writes an audit event.
- **SC-007**: Exactly one assignment notification is delivered per assignment (verified via mail capture); detection without assignment produces none.

## Assumptions

- **Budget-line = the existing `Item`** (established P2 decision); "participant" = the application's applicant/participant; "tranche" is the P2 `Tranche` entity. Reconciliation scopes reuse these.
- **The reconciliation engine reuses the existing pure evaluators** (`DisbursementReconciliation`, `DisbursementLineReconciliation`, P3 closure legs) as its computation core; P4 adds persistence, severity classification, lifecycle, and the warning rules around them. Whether the evaluators are refactored to emit rows or wrapped by a materializer is a plan-time decision (see Open Questions).
- **Materialization is synchronous, in the same unit of work as the triggering mutation** (no new background worker), consistent with the existing two-SaveChanges service pattern; the money gate's fresh recompute is the authoritative correctness check regardless of snapshot freshness.
- **Financial Operator, Auditor, and Admin roles and their group-scoping already exist** (P1/spec 040) and are reused unchanged; no new role is introduced.
- **The email outbox (spec 021) and its non-prod allowlist are reused** for the assignment notification; a new notification event is added to the existing pipeline.
- **Schema is additive dacpac-only** (new `Discrepancy` + `DiscrepancyEvent` tables; tolerance represented as a parameter/column seam); **no new managed (NuGet) dependencies**. TINYINT enum columns require EF `HasConversion<byte>()` (established project gotcha).
- **es-CR is the default culture**; all new copy follows the existing resource pattern.

## Dependencies

- **P1 (spec 045)** — Disbursement, append-only ledger, `DisbursementReconciliation`, Financial Operator role, `DiscrepancySeverity` enum (currently `Blocking=0` with a reserved Warning tier), `ReconciliationDiscrepancy` value object (currently computed-on-read, not persisted).
- **P2 (spec 046)** — Tranche, budget-line attribution, `DisbursementLineReconciliation`, per-line committed budget, composed balance tree.
- **P3 (spec 047)** — evidence graph, per-line allocation, completeness/closure equality legs, budget-line closure gate; **absorbs 047 FINDING-13** (graph-invoice allocation drift becomes a Warning).
- **Spec 021** — email outbox pipeline and non-prod allowlist.
- **Spec 040** — Auditor role and group-scoping precedent.

## Out of Scope

Deferred to later program slices (roadmap `brainstorm/41-financial-disbursement-platform.md`):

- **P8** — full per-rule severity configuration; the "Approved" review-gate lifecycle state; approver role; no-self-approval rule; approval thresholds by amount; delegated approval; data-entry/review/approval/closure segregation.
- **P5** — admin tolerance-configuration UI; foreign-currency reconciliation; bank-statement evidence type; bank-account external reconciliation (platform totals vs actual bank statement).
- **P6** — interest, bank fees, refunds, reimbursements, reversals, credit-note/refund-receipt money semantics.
- **P7** — reporting, participant statements, tranche/agency financial summary, SBD-code exports, report reproducibility/snapshots; agency-vs-SBD-received reconciliation (needs an agency-received reference figure the platform does not model).
- **P9** — migration / spreadsheet import.
- **Parked entirely** — multi-agency & tenant isolation, Mentori sync, participant self-service portal, OCR document parsing, in-platform digital signature, SBD live API integration.

## Open Questions

*All resolved during `/speckit-plan` — see `research.md` (D1–D6) and `plan.md`.*

- **OQ-1** ✅ (research D2): polymorphic `(ScopeType, ScopeEntityId)` scope key + owned append-only `DiscrepancyEvent` child (copy spec-047 `Evidence`/`EvidenceVersion`); stable identity = unique index `(ApplicationId, ScopeType, ScopeEntityId, Comparison)`.
- **OQ-2** ✅ (research D1): a **wrapping** `IReconciliationMaterializer` — the pure evaluators and the money gates stay unchanged; materialization is additive for visibility.
- **OQ-3** ✅ (research D5): new group-scoped `IReconciliationDashboardProjection` + `Reconciliation` controller (inbox-style); extend the per-application `_DiscrepancyList` to read persisted rows.
- **OQ-4** ✅ (research D2): `Discrepancy.RowVersion` optimistic concurrency; independent of the deferred `dbo.Items`-RowVersion debt (different table).
- **OQ-5** ✅ (research D4): **no stored "requested" amount exists** → FR-010(a) **dropped**; warning set is the three conditions in FR-010. A redefinition (cheapest-estimate vs allocation) is documented for a future slice, not built in P4.
