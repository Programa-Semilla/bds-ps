# Research: Full Reconciliation Engine (spec 048 / financial-execution P4)

**Date:** 2026-07-17
**Method:** 5 parallel codebase-research agents over the shipped P1–P3 slices (specs 045/046/047), the notification/audit subsystems (specs 021/040/041), and the applicant/review model (specs 035/037). Resolves plan open questions OQ-1…OQ-5 from spec.md.

---

## D0 — Baseline: reconciliation today is pure + computed-on-read (confirms model C is a genuine gap)

Both existing evaluators are **pure static domain services** returning transient value objects that are **computed on read and never persisted** — the only materialized reconciliation output today is the *derived* `DisbursementState` (`Disbursement.ApplyReconciliation`) and `ItemClosureState`.

- `DisbursementReconciliation.Evaluate(disbursementAmount, bankReceiptAmount?, invoiceAmount?, sumOfNonCancelledIncludingThis, allocation)` → `IReadOnlyList<ReconciliationDiscrepancy>`; three comparisons, all `Blocking`, `>= 0.01` tolerance; over-disbursement only for `TotalVsAllocation`.
- `DisbursementLineReconciliation.EvaluateSplit / EvaluateLineOverpayments / EvaluateLineEquality` → line-level discrepancies (`LineOverpaymentDiscrepancy`). `EvaluateLineEquality` (P3 closure, FR-024) checks `|LinePaid − LineAccepted| >= 0.01` in **either** direction.
- `ReconciliationComparison` (byte): `DisbursementVsBankReceipt=0, DisbursementVsInvoice=1, TotalVsAllocation=2, DisbursementSplitVsTotal=3, LinePaymentVsBudget=4`.
- `DiscrepancySeverity` (byte): only `Blocking=0`; the doc-comment reserves a `Warning` tier "for the P4 non-blocking discrepancy lifecycle."
- **Call sites (the FR-001 trigger surface):** `DisbursementService` — `RecordAsync`, `EditAsync`, `ValidateAsync` (the race-proof money gate — re-reads fresh `SumNonCancelledAsync` + fresh committed budgets), `ReconcileAsync` helper (Edit + Attach/replace evidence), `GetAsync` (read). `BudgetLineClosureService.CloseAsync` (four fresh-read gates incl. `EvaluateLineEquality`).

**Implication:** P4 adds a persistence/materialization layer *around* these evaluators. The evaluators and the money gates stay untouched, preserving the FR-004 fresh-recompute guarantee.

---

## D1 — OQ-2 RESOLVED: a wrapping materializer, evaluators unchanged

**Decision:** Introduce a new `IReconciliationMaterializer` (Application) / `ReconciliationMaterializer` (Infrastructure) that **wraps** the existing pure evaluators plus the new warning evaluators, computes the application's current discrepancy set, and reconciles it against the persisted `Discrepancy` rows (upsert / auto-resolve / insert per FR-003). The existing evaluators are **not** refactored to emit rows.

**Rationale:**
- The money gates (`ValidateAsync`, `CloseAsync`) must keep calling the pure evaluators directly and throwing on a *fresh* recompute (FR-004 / SC-004). If we refactored the evaluators to emit persisted rows, the gate's authoritative check would depend on the materializer having fired — exactly the race-proofing we must not weaken.
- A wrapping materializer is purely additive: it runs *after* each mutation's domain `SaveChanges` (two-SaveChanges discipline, D6) for visibility/lifecycle, while the gate's throw-path is unchanged.
- Keeps the two evaluators pure/deterministic (NFR-020) and their existing unit tests green.

**Trigger wiring (FR-001):** inject `IReconciliationMaterializer` into `DisbursementService`, `EvidenceService`, and `BudgetLineClosureService`; call `MaterializeAsync(applicationId, actorUserId, ct)` at the end of every mutating method (record/edit/validate/cancel disbursement; attach/replace/delete/allocate evidence; commit/uncommit line; close/reopen line). Explicit call sites, no magic.

**Alternatives rejected:** (a) refactor evaluators to emit rows — weakens the gate (above); (b) EF SaveChanges interceptor / background worker — hidden control flow, eventual-consistency window, and the gate already protects correctness so async buys nothing (contradicts synchronous-materialization anchor).

---

## D2 — OQ-1 RESOLVED: polymorphic scope key + owned append-only history (copy Evidence+EvidenceVersion)

**Decision:** `Discrepancy` is an Application-scoped aggregate keyed by `int ApplicationId` (flat, **no** navigation collection on `Application` — the R2 pattern), owning a private `List<DiscrepancyEvent>` exposed read-only (copy spec-047 `Evidence` + `EvidenceVersion`). Scope is a **polymorphic pair** `(DiscrepancyScopeType ScopeType, int ScopeEntityId)`, **not** nullable typed FKs.

- **Stable identity (FR-003)** = unique index on `(ApplicationId, ScopeType, ScopeEntityId, Comparison)`. Exactly **one** row ever exists per identity; it is upserted in place and transitions state (Open↔Assigned↔UnderCorrection↔Resolved↔Waived) — a recurrence reopens the *same* row (no filtered index needed; no duplicates by construction).
- `ScopeEntityId` holds `DisbursementId` (Payment), `ItemId` (BudgetLine), `ApplicationId` (Participant), `TrancheId` (Tranche), or `EvidenceId` (Document) per `ScopeType`.

**Rationale:** nullable typed FKs to Disbursement/Item/Tranche/Evidence would create multiple cascade paths to `Applications` (the spec-029/035 publish-failure lesson) and 4–5 mostly-null columns. A polymorphic pair gives one clean unique index for the identity, one FK (to `Applications`, NO ACTION) plus the assignee FK. Trade-off: no referential integrity on `ScopeEntityId` — acceptable because the rows are **engine-managed** (only the materializer writes them) and always recomputed from live data; a stale scope id simply auto-resolves on the next run.

**Concurrency (OQ-4):** `Discrepancy` carries its own `RowVersion` (`IsRowVersion`) for lifecycle-edit optimistic concurrency (FR-018, constitution quality gate) — independent of the deferred `dbo.Items`-RowVersion debt (different table). Lifecycle handlers catch `DbUpdateConcurrencyException` → es-CR "recargá y reintentá." `DiscrepancyEvent` is immutable/append-only (no RowVersion) — copy `DisbursementLedgerEntry` (static factories, no mutators).

**Structural precedents to copy verbatim:** `Evidence`/`EvidenceVersion` (owned collection, `PropertyAccessMode.Field`, `internal` child ctor, root assigns sequence), `Disbursement` (state-machine factory + guarded transition methods), `DisbursementLedgerEntry` (immutable append-only child). EF configs auto-register via `ApplyConfigurationsFromAssembly`; **every byte enum needs `.HasConversion<byte>()`** (the `Byte→Int32` gotcha, regression-tested). dacpac: two additive greenfield tables (`dbo.Discrepancies`, `dbo.DiscrepancyEvents`), NO ACTION FK to Applications + AspNetUsers, CASCADE child→parent, unique index on the identity, `RowVersion`, `DECIMAL(18,2)` money, `DATETIMEOFFSET(0)` timestamps. No post-deploy backfill (greenfield). No new role (reuse Financial Operator).

---

## D3 — Severity + the warning starter set (OQ-5 drops one rule)

**Decision:** Extend `DiscrepancySeverity` with `Warning=1`. Extend `ReconciliationComparison` with the warning comparisons. Severity is **fixed per rule** (spec FR-007).

**Warning starter set — now THREE conditions** (OQ-5 dropped the fourth, D4):
- `EvidenceDateAnomaly=5` — evidence dated after its related payment date, or before the funding-agreement execution date (both concrete stored anchors).
- `PossibleDuplicatePayment=6` — same supplier + amount + date across non-cancelled disbursements.
- `GraphInvoiceAllocationDrift=7` — a line with a validated payment whose independently-allocated graph invoice differs (absorbs spec 047 FINDING-13).

New pure evaluator `ReconciliationWarnings` (Domain/Services) with one static method per warning, returning descriptors the materializer maps to `Discrepancy` rows with `Severity=Warning`. Deterministic, unit-testable, mirrors `DisbursementReconciliation` style.

**Blocking set unchanged (FR-008):** the existing `DisbursementReconciliation` three + `DisbursementLineReconciliation` split/overpayment/equality legs. The materializer classifies each evaluator output's `Comparison`→severity via a fixed map (all existing comparisons Blocking; the three new ones Warning).

---

## D4 — OQ-5 RESOLVED: DROP the requested-vs-approved variance warning

**Finding (definitive):** there is **no stored "requested" monetary amount** distinct from the approved/executed allocation anywhere in the model.
- `Item` stores no amount; the applicant submits **competing `Quotation`s**, the reviewer *selects* one (`Item.Approve(supplierId)` → `SelectedSupplierId`). For an approved line, requested == approved by construction (same quote).
- The approved/executed figure **is** stored: `DisbursementLedgerEntry.Amount` where `EntryType = Allocation` (snapshot of `ApplicationCurrencyTotal.Compute`, resolved by `DisbursementAllocation.ResolveAsync`).
- The funding-agreement "Recursos solicitados" table is **computed at PDF-render time** (cheapest-quote proxy), not stored; `FundingAgreement` holds only PDF metadata, no monetary total.

**Decision:** **Drop FR-010(a)** per the spec's own OQ-5 instruction ("if only one amount is retained, drop rather than ship hollow"). The P4 warning set is the three conditions in D3. Documented redefinition option for a future slice (needs stakeholder sign-off, NOT built in P4): "pre-review cheapest estimate (`ApplicationCurrencyTotal.ComputeCheapestEstimate`, live-recomputed) vs executed allocation" — rejected for P4 because it compares a live lower-bound estimate against a frozen snapshot and is not a genuine "requested" amount.

**Spec action:** remove FR-010(a); the remaining three warnings renumber; note the drop in spec + surface to the user.

---

## D5 — OQ-3 RESOLVED: dashboard = new group-scoped projection + inbox-style controller; per-app surface extends `_DiscrepancyList`

**Decision:**
- New `IReconciliationDashboardProjection` (Application) / EF impl (Infrastructure) modeled on `EvidenceInboxProjection`: `GetSummaryAsync(IReviewerScope, ReconciliationFilter?, ct)` (tiles) + `GetDiscrepanciesAsync(IReviewerScope, ReconciliationFilter, ct)` (filtered list). Group-scoping **in-query**: admin short-circuit, group-overlap via `UserGroupMemberships` on `app.Applicant.UserId`, empty-group non-admin → empty, `ExcludeDeleted`/`ExcludeArchivedFund`, `MaxRows` cap (500). Filterable dimensions resolved by joining/materializing then filtering in-memory (the `ParticipantBalanceProjection` build-then-filter pattern).
- New `ReconciliationDashboardController` `[Authorize(Roles="Financial Operator,Admin,Auditor")]` `[Route("Reconciliation")]` — cross-application (group→agency). `Index` (dashboard: `_KpiTile` summary strip + `_BudgetLineFilterToolbar`-style GET-form + list). `Detail(id)` (one discrepancy + its `DiscrepancyEvent` timeline). Lifecycle POSTs (`Assign`/`MarkUnderCorrection`/`Waive`) guarded by a **per-discrepancy** `GuardWriteAsync`: load discrepancy → `ApplicantSharesAnyGroupAsync(app, scope)` → flat `NotFound()` if out-of-scope, THEN `Forbid()` if `!CanWrite()` (`CanWrite() => User.IsInRole("Financial Operator")` — Auditor+Admin read-only). Sidebar entry in `operativoEntries` (Financial Operator/Admin/Auditor).
- **Per-application surface:** extend `_DiscrepancyList.cshtml` (currently binds the transient VO per-disbursement) to render the **persisted** discrepancy rows incl. severity badge (text+icon, never color-alone, FR-025) and lifecycle state; add a deep-link to the discrepancy detail. Keep it rendering on the Disbursement `Detail` page.

**Filter mapping (FR-023):** participant (=application, always), severity/state/assignee/date (direct columns), tranche/budget-line (line/tranche-scoped rows), supplier (rows resolving to a supplier via the line/quotation join). es-CR copy: new `ReconciliationResources` (Web); extend `DisbursementResources.ComparisonLabel` for the three new comparisons; refusal strings in `DiscrepancyReasons` (Application).

---

## D6 — Notifications: direct-send best-effort factory (refines FR-027 away from the outbox)

**Decision:** send the assignment email via a **direct-send factory** `DiscrepancyAssignmentEmailFactory` (mirror `InvitationEmailFactory` + `IEmailViewRenderer` + inline `IEmailSender.SendAsync`), **best-effort** (a send failure is logged and never blocks the assignment — mirror `PasswordChangedEmailFactory`). Recipient = the assignee's own email (known exactly). Branded `_EmailLayout` shell; es-CR; `.text` twin.

**Rationale:** the outbox pipeline resolves recipients from **stage-group/role buckets** ("whoever plays role X in this app's groups"), which is the opposite of "notify this one user I just picked." Reusing it would require a new `Assignee` bucket + `AssigneeUserId` payload field + a `VersionHistory` anchor per (re)assignment for dedup — machinery bent against its grain for a P3 convenience. Direct-send is the established pattern for known-recipient mail (specs 033/041), less code, and best-effort is appropriate since the dashboard is the durable record and the email is a nudge. The allowlist still applies on the send path (E2E mail-capture via smtp4dev works).

**Spec action:** FR-027 reworded to be delivery-mechanism-neutral (drop the "outbox" prescription — a HOW leak); mechanism recorded here. **Considered alternative:** outbox + new `Assignee` bucket + `AssigneeUserId` payload + per-assignment `VersionHistory` anchor — rejected for P4 on YAGNI grounds; revisit if assignment notifications later need retry/delivery-audit guarantees.

---

## D7 — Audit: new `discrepancy.*` AdminAuditEvent family + two-SaveChanges discipline

**Decision:** add `discrepancy.*` action constants to `AdminAuditEvent` (`DiscrepancyAssigned`, `DiscrepancyUnderCorrection`, `DiscrepancyWaived`, `DiscrepancyResolved`, `DiscrepancyReopened`) + `TargetTypeDiscrepancy`, and a `discrepancy.` prefix branch in `AdminAuditEventWriter.DeriveTarget` extracting `discrepancyId` (mirror `disbursement.`). Payloads embed `discrepancyId` + `applicationId` + `before`/`after`.

- **Human lifecycle transitions** (assign/under-correction/waive) write a `DiscrepancyEvent` (the per-discrepancy timeline, FR-016) **and** an `AdminAuditEvent` (the global trail, FR-017), via the two-SaveChanges pattern: mutate aggregate → `SaveChanges` #1 (get ids) → stage audit (+ inline best-effort email) → `SaveChanges` #2. No explicit transaction (the Aspire retrying execution strategy forbids it — established rationale).
- **Auto transitions** (materializer auto-resolve / auto-reopen) write only a `DiscrepancyEvent` with `ActorUserId` = the **system-sentinel user id** (spec-043 lesson: the `"system"` literal violated the `AspNetUsers` FK; use the real sentinel id) and are folded into the materializer's own SaveChanges. They may also write a compact `discrepancy.resolved`/`.reopened` audit row (system actor).

---

## Cross-cutting confirmations
- **No new managed dependencies.** Additive dacpac-only schema (2 tables). No new role.
- **es-CR** throughout (default culture); resource-class split (Web `*Resources` view copy / Application `*Reasons` refusal strings) per specs 034/043 precedent.
- **Decimal precision** `DECIMAL(18,2)`, `>= 0.01` tolerance constant (NFR-001).
- **Regression:** the P1–P3 money-gate suites (SC-006 family) must stay green — the gates are untouched (D1). SC-004.

## Open questions — all resolved
| OQ | Resolution |
|----|------------|
| OQ-1 scope key | D2 — polymorphic `(ScopeType, ScopeEntityId)` + owned append-only `DiscrepancyEvent`; RowVersion on root |
| OQ-2 emit vs wrap | D1 — wrapping materializer; evaluators + money gates unchanged |
| OQ-3 dashboard | D5 — new group-scoped projection + `Reconciliation` controller; extend `_DiscrepancyList` |
| OQ-4 concurrency | D2 — `Discrepancy.RowVersion`, independent of Items-RowVersion debt |
| OQ-5 requested-vs-approved | D4 — DROP FR-010(a) (no stored requested amount); warning set = 3 |
