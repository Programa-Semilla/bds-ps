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

**Admin can write tranches** ([`TrancheController.cs:24`](../../src/FundingPlatform.Web/Controllers/TrancheController.cs), relates to [FR-021](spec.md#functional-requirements))
The controller is `[Authorize(Roles="Reviewer,Admin")]` and does not restrict Admin from POSTing,
mirroring [research D8](research.md#d8--roles-placement-audit-reuse-no-new-role) and the shipping
`ReviewController`. FR-021 literally says "Admins MUST be read-only" for tranche definition.
Financial actions (commit/record/attribute/validate) **do** correctly keep Admin read-only
(`DisbursementController.CanWrite = FinOp only`).
- Question: is admin-as-reviewer for tranche *definition* acceptable (consistent with spec 016
  FR-015 and the way spec 045 narrowed Admin only for money movement), or should the tranche
  write endpoints be reviewer-only to match FR-021 literally? This is the one real
  spec-vs-plan tension in the slice.

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
