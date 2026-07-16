# Code Review Guide: Tranches & Budget-Lines (Financial Execution P2)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Implementation date:** 2026-07-16

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on high-level
questions that need human judgment. Compliance is ~97% (see console report); the notes
below are where a reviewer's eyes add the most value.

**Changed files:** ~35 files — Domain (2 new entities, 1 enum, 2 edited entities, 1 pure
service, 1 edited VO + 1 new VO file), Application (2 new interfaces + DTOs, edited disbursement
DTOs/reasons/`ApplicationCurrencyTotal`), Infrastructure (1 new service, edited
`DisbursementService`/`ParticipantBalanceProjection`, 2 new + 1 edited EF config, DbContext), Web
(1 new controller + edited `DisbursementController`/`ReviewController`, 5 new partials + edits, 2
new resources), 3 dacpac `.sql`, plus unit/integration/E2E tests.

### Understanding the changes (8 min)

- Start with [`ParticipantBalanceProjection.cs`](../../src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs)
  — the composed tranche→line balance tree is the heart of P2 and consolidates the projection
  work of five tasks (T022/T028/T036/T040/T041) into one method. The `GetComposedForApplicationAsync`
  method builds line balances, derives status, applies US4 filters, and groups into tranches +
  the synthetic "General" bucket.
- Then [`DisbursementService.cs`](../../src/FundingPlatform.Infrastructure/Services/DisbursementService.cs)
  — the commit/uncommit methods, the `Record`/`Edit` split validation + replace-all, and the
  `Validar` per-line over-payment gate (`EvaluateLineOverpaymentsAsync`).
- Question: the composed projection computes the participant node as **Σ lines** (not from the P1
  ledger). Does asserting `participant.Allocated == ledger snapshot` by construction (frozen lines)
  read as sound, or would you want the participant node sourced from the flat P1 projection instead?

### Key decisions that need your eyes (12 min)

**Admin can write tranches** — ✅ RESOLVED by the deep review (was `[Authorize(Roles="Reviewer,Admin")]`).
The deep-review security agent confirmed this was an FR-021 authorization gap (Admin could write
tranches cross-group, inconsistent with the sibling `DisbursementController`). Now
`TrancheController` is `[Authorize(Roles="Reviewer")]` and the editor renders only for reviewers, so
Admin is read-only for tranche definition per [FR-021](spec.md#functional-requirements). No open
question remains.

**`Lines` is optional on Record/Edit** ([`DisbursementDtos.cs`](../../src/FundingPlatform.Application/Disbursements/DisbursementDtos.cs), relates to [FR-010](spec.md#functional-requirements)/[SC-006](spec.md#success-criteria))
Attribution is opt-in: `Lines == null` ⇒ a flat P1-style disbursement (no split check); non-empty ⇒
split-integrity + committed checks + replace-all. This is what keeps the P1 `Disbursement*` E2E
regression green (they record without lines) and satisfies SC-006.
- Question: should a P2 disbursement be *required* to carry a split (forcing attribution), or is
  opt-in (flat still allowed) the right backward-compatible stance? The spec describes splitting as
  the norm but SC-006 demands legacy flat behavior survive.

**Commit routes through the aggregate, not `Item` directly** ([`Application.cs` `CommitLine`/`UncommitLine`](../../src/FundingPlatform.Domain/Entities/Application.cs))
`Item.Commit()`/`Uncommit()` are `internal`; Infrastructure can't call them (no `InternalsVisibleTo`),
so the service loads the aggregate and calls `Application.CommitLine`. These aggregate methods are
deliberately **not** subject to the tranche freeze (commit is post-execution operator work).
- Question: is loading the whole `Application` aggregate (with `Items`) per commit acceptable, or
  would a lighter tracked-`Item` load (with a public `Item.Commit`) be preferable?

**Tranche delete uses `ClientCascade`** ([`ApplicationConfiguration.cs`](../../src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicationConfiguration.cs))
Removing a tranche from the aggregate collection orphans a row with a required FK; `Restrict` made EF
try to null the FK (error). `ClientCascade` makes EF delete the orphan while the DB FK stays NO ACTION.
- Question: `DeleteTranche` re-parents member items to null *before* removing the tranche, so the
  cascade never actually deletes a referenced item — is the ClientCascade choice (vs. an explicit
  `_db.Tranches.Remove`) clear enough to the next maintainer?

### Areas where I'm less certain (5 min)

- [`ParticipantBalanceProjection.cs` `DeriveStatus`](../../src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs)
  ([D3](research.md#d3--budget-line-status-filter-values-spec-oq-3--fr-020)): the ordering of the
  five status buckets (Uncommitted→Committed→PartiallyPaid→Paid→Validated) with a 0.01 tolerance is
  my synthesis. Edge behavior at `budget == 0` (a line with no priced quote) returns `Committed`
  when committed with no payment — worth a skeptical look.
- The US4 **date filter** ([`ParticipantBalanceProjection.MatchesDate`](../../src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs))
  matches a line if *any* of its attributions' disbursement dates fall in range. There is no E2E for
  the date facet (the single-item seed makes it awkward) — only the code path + the other-facet E2E.
- Composed participant `Paid` = Σ line attributions. For a **legacy flat disbursement** (no lines),
  this is 0 at the line level while the flat balance card shows the real Paid. The two panels can
  disagree for pre-attribution data. I judged this acceptable (composed = attributed view); confirm
  that's not confusing on the UI.

### Deviations and risks (5 min)

- **FR-021 (Admin tranche write):** the one deviation from a literal spec reading; see the decision
  above. Follows [plan D8](research.md#d8--roles-placement-audit-reuse-no-new-role). Question: accept,
  or tighten to reviewer-only?
- **Reason strings placement** (deviation from [tasks T002](tasks.md)): the six service-produced es-CR
  reason strings live in the Application-layer `DisbursementReasons`, not Web `DisbursementResources`,
  because the Infrastructure services produce them and cannot depend on Web (spec 034/043 precedent).
  Question: is the cross-layer placement right?
- **Financial integration tests use the InMemory harness** (spec-045/036 precedent), with real-SQL
  TINYINT materialization + filtered-index behavior proven by the E2E suite (which passed against real
  SQL). Question: is E2E-only real-SQL coverage sufficient for the new `CommitState` TINYINT +
  `DisbursementLineAllocations` unique/CK constraints, or do you want an explicit materialization
  integration test like `DisbursementEnumMaterializationTests`?
- **No E2E spans two tranches in one split** (FR-012) — the shared seed builds a single line item, so
  cross-tranche attribution is proven only by the integration composition test, not E2E.

---

## Deep Review Report

> Automated multi-perspective code review results (5 internal agents; external tools disabled).

**Date:** 2026-07-16 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 2 (1 Critical, 1 Important) | completed |
| Architecture & Idioms | 7 (1 Important, 6 Minor) | completed |
| Security | 1 (Important) | completed |
| Production Readiness | 3 (2 Important, 1 Minor) | completed |
| Test Quality | 9 (3 Important, 6 Minor) | completed |
| CodeRabbit (external) | — | skipped (`--no-external`) |
| Copilot (external) | — | skipped (`--no-external`) |

### Findings Summary

| Severity | Found | Fixed | Documented (accepted) |
|----------|-------|-------|-----------------------|
| Critical | 1 | 1 | 0 |
| Important | 8 | 6 | 2 |
| Minor | 11 | 8 | 3 |

### What was fixed automatically

- **Correctness/money-integrity:** `ValidateAsync` now re-checks split integrity so a stale or
  partially-persisted split can never validate into the ledger; `EditAsync` refuses an amount-only
  edit that would strand a stale split. This one fix also neutralises the `Record` two-SaveChanges
  atomicity concern (money cannot move on a broken split).
- **Authorization:** `TrancheController` is reviewer-only (FR-021); the editor is reviewer-gated.
- **Reconciliation coverage:** new `BudgetLineReconciliationTests` lock the six-dimension
  participant == Σ tranches == Σ lines invariant, composed == flat `Allocated` (which also
  drift-guards the duplicated LineBudget LINQ), cross-tranche split (FR-012), the three
  payment-derived status buckets, and negative Available at line + tranche levels.
- **Cleanups:** composed projection scoped through the indexed `Disbursements.ApplicationId`;
  `APPLICATION_NOT_FOUND` reason code; class/DTO/label doc accuracy.

### What still needs human attention

Two Important findings were consciously **documented as accepted** rather than auto-fixed, both with
zero financial-integrity impact (see [review-findings.md](review-findings.md) FINDING-4 and FINDING-6):

- **Item `CommitState` has no concurrency token.** A rare same-line, two-operator race can leave a
  stale `Committed` dimension. The money-movement gate at `Validar` re-reads fresh sums and is
  **unaffected**. The only robust fix (RowVersion on the central `dbo.Items`) risks unhandled
  concurrency exceptions in existing item flows — judged a disproportionate trade for a display-only
  edge. Question: accept as a documented limitation, or schedule the `Item.RowVersion` sweep as a
  follow-up?
- **Composed tree = line-attributed view; flat card = total.** They reconcile exactly when the
  operator attributes every disbursement (the intended P2 flow, now tested); they diverge only for
  unattributed/legacy disbursements (grandfathered for SC-006). Question: is the two-view semantic
  acceptable, or should unattributed amounts surface as a residual line in the synthetic tranche?

Three Minor test-coverage follow-ups remain (2-supplier filter narrowing, date/FullyValidated filter
facets, Auditor read-only assertion) — non-blocking.

### Recommendation

All Critical and high-impact Important findings are addressed; the two remaining Important items are
documented accepted limitations with no financial-integrity impact and clear rationale. Code is ready
for human review with no known blockers. The two documented questions above are the highest-value
items for a reviewer's judgment.
