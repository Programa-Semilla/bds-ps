# Review Guide: Tranches & Budget-Lines (Financial Execution P2)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-07-16

---

## What This Spec Does

After a funding agreement executes, the operating agency pays suppliers on the participant's behalf. P1 (spec 045) records those payments against one flat allocation number. This slice gives that number **structure**: the allocation is split into **tranches** (funding phases) of **budget-lines** (the existing approved line items), each payment is attributed to the specific lines it covers, and a new **Committed** balance shows money obligated-but-not-yet-paid. Everything still reconciles to the colón.

**In scope:** tranche setup by the reviewer (derived amounts), a per-line commit step, per-line payment attribution (one payment ↔ many lines), the six-dimension composed balance (participant → tranche → line), and two new blocking reconciliation checks (split integrity, per-line over-payment).

**Out of scope (and this is where the boundaries matter):** evidence is still attached at the *disbursement* level, not per line — evidence↔line allocation, document versioning, and required-doc/closure rules are all **P3**. Non-blocking warnings and a discrepancy lifecycle are **P4**; foreign currency **P5**. Tranches are a *money partition*, deliberately **not** a time/milestone release gate. These lines were drawn to keep P2 to the structural money model.

## Bigger Picture

This is slice **P2 of a 9-slice financial-execution program** ([roadmap](../../brainstorm/41-financial-disbursement-platform.md)); P1 shipped as PR #78. The load-bearing design bet — ratified in brainstorming — is **budget-line = the existing `Item`** rather than a new financial entity. That reuse is what lets the zero-colón guarantee stay *structural*: because a tranche's amount is derived as the sum of its lines' budgets (and a line's budget is the selected quote's already-pinned CRC amount), "Σ tranche = allocation" is true by construction, not by a runtime check. If you disagree with budget-line = `Item`, most of the plan changes shape — so that's the highest-leverage thing to pressure-test. Everything else (join-table topology, off-ledger commit status, the reviewer/operator role split) follows established patterns from specs 035/040/045.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec Overview + Core model](spec.md#overview) and [research D4–D5](research.md). As you read:

- Budget-line = `Item` loads financial responsibility (commit state, tranche membership) onto an entity that already carries submission/review concerns. Is that consolidation right, or does the money model deserve its own entity even at the cost of duplicating line identity? ([plan Complexity Tracking](plan.md#complexity-tracking))
- A tranche's amount is **derived**, never entered ([FR-003](spec.md#functional-requirements)). Does any real agency workflow set a tranche budget *first* and fit purchases within it? If so, the "derived" model is wrong and we'd need independently-set amounts + a partition reconciliation check.
- Unassigned lines fall into a **virtual default tranche** with no database row ([research D4](research.md#d4--tranche-freeze-virtual-default-tranche--execution-guard-fr-002-fr-004-fr-005)). Does a synthetic-but-invisible tranche read cleanly, or would a materialized default row be less surprising to future maintainers?

### Key decisions that need your eyes (12 min)

**Commit lives off the append-only ledger** ([research D1](research.md#d1--line-commit-off-ledger-status-not-a-ledger-entry-spec-oq-1), [FR-009](spec.md#functional-requirements))
Committing a line is a mutable `Item.CommitState`, not a ledger entry — the ledger stays "settled cash only." This defers reversal vocabulary to P6.
- Question: is "commitment is an obligation, not cash" a safe long-term stance, or will auditors eventually want commitment events in the immutable trail? (The `line.committed` `AdminAuditEvent` is the current trail — is that enough?)

**`Available = Allocated − Paid`, unchanged from P1** ([FR-017](spec.md#functional-requirements))
Committed is display-only; the official available balance still drops at *payment*, not at commitment.
- Question: should committed-but-unpaid money count as unavailable to the participant? (The data supports either; switching would be a P7 reporting choice, but it's worth confirming now.)

**Disbursements may span tranches** ([FR-012](spec.md#functional-requirements))
A single payment can attribute to lines in different tranches.
- Question: does that match how the agency's real bank payments map to phases, or would confining a payment to one tranche give cleaner rollups?

**Per-line over-payment is blocking, re-checked at `Validar`** ([FR-019](spec.md#functional-requirements), [research D6](research.md#d6--line-level-reconciliation-new-pure-disbursementlinereconciliation))
Symmetric with P1's participant-level over-disbursement gate — re-evaluated against freshly-read sums to catch the concurrent-partial-payment race single-row concurrency can't.
- Question: is blocking (vs. a non-blocking warning) the right severity for a line slightly over its committed budget, given warnings don't arrive until P4?

**Reviewer authors tranches, Financial Operator commits/attributes** ([contracts §4](contracts/interfaces.md#4-web-routes))
Two roles, two surfaces, frozen at execution — no new role.
- Question: is the reviewer the right owner of tranche structure (they already assign line codes here), or is phase planning really a financial-operator concern that shouldn't happen pre-execution?

### Areas where I'm less certain (5 min)

- [research D4 freeze](research.md#d4--tranche-freeze-virtual-default-tranche--execution-guard-fr-002-fr-005): I chose to enforce the freeze with a domain guard on `State != AgreementExecuted` rather than a new execution-time hook, because `ExecuteAgreement` is a pure state flip with no financial side effect today. That's simpler, but it means "frozen" is enforced at each mutation entry rather than by a single latching event — if you'd expect an explicit freeze snapshot, this is the spot to push back.
- [research D3 line status](research.md#d3--budget-line-status-filter-values-spec-oq-3--fr-020): the five derived status values are my synthesis of the seed's larger status list; a reviewer closer to the agency's vocabulary may want different buckets (e.g. an explicit "Scheduled for payment").
- [data-model composed projection](data-model.md#balance-projection--composed-tree-application-dtos): I assert participant `Allocated` (from Σ line budgets) equals the P1 ledger snapshot because lines are frozen before the first disbursement. If there's any path where a line's converted amount can change *after* the allocation snapshot, that equality (and SC-003) breaks — worth a skeptical look.

### Risks and open questions (5 min)

- The join table has two FK paths to `Applications` (via `Disbursements` and via `Items`). The plan uses CASCADE on one and NO ACTION on the other ([data-model](data-model.md#aggregate-2--disbursementlineallocation-new--the-mn-join), the `ItemImpacts` topology). If both were cascade the dacpac publish fails — is the chosen direction (cascade from `Disbursements`) the one you'd want operationally?
- `Item.CommitState` is a new TINYINT enum; specs 035/040/045 all hit `Byte→Int32` materialization failures that EF-InMemory hid. The plan mandates real-SQL integration tests ([quickstart gotchas](quickstart.md#gotchas-carried-from-p1--house-conventions)) — is that coverage sufficient, or do you want an explicit materialization test like `DisbursementEnumMaterializationTests`?
- SC-003 requires each balance level to equal the sum of its children to the colón. Is a single integration assertion enough, or should this be property-tested across randomized splits?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
