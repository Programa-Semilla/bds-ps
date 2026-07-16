# Code Review: Financial Disbursement Core (spec 045)

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall: 100%** (31/31 functional requirements, all edge cases, SC-001–SC-008).

- Functional requirements FR-001–FR-031: compliant.
- Edge cases (zero/negative, non-CRC, amount-edited rerun, exact-total, cancel-leaves-no-ledger, concurrent edits, concurrent partial-payment race, both-differ, replace-before-validation, negative-Available): compliant.
- One deviation from [plan.md](plan.md) was **resolved toward the spec**, not away from it — see "Deviations" below (FR-025 Admin read-only).

Delivery gate: Unit 10/0, Integration 6/0, filtered E2E 10/0 (real SQL).

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on high-level questions that need human judgment.

**Changed files:** ~50 (Domain entities/enums/VOs/pure service; Application interfaces/DTOs/reasons; Infrastructure service/projection/EF-configs/audit/role wiring; Web controller×2/views/resources; 3 dacpac tables + role seed; unit/integration/E2E tests).

### Understanding the changes (8 min)

- Start with [`DisbursementReconciliation.cs`](../../src/FundingPlatform.Domain/Services/DisbursementReconciliation.cs): the pure, deterministic heart of the feature — three comparisons, zero-colón tolerance. Everything else orchestrates around it.
- Then [`DisbursementService.cs`](../../src/FundingPlatform.Infrastructure/Services/DisbursementService.cs): how record/edit/attach/validate/cancel thread the evaluator, the append-only ledger, and the two-SaveChanges audit.
- Then [`data-model.md`](data-model.md) §"Balance projection" alongside [`ParticipantBalanceProjection.cs`](../../src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs).
- Question: the balance is split between an append-only ledger (Allocated/Validated) and mutable off-ledger rows (Pending). Does that split read clearly, or would a single source have been simpler for P1? (The ledger is a deliberate P6 substrate — see [Complexity Tracking](plan.md#complexity-tracking).)

### Key decisions that need your eyes (12 min)

**Validate-time over-disbursement re-check** ([`DisbursementService.ValidateAsync`](../../src/FundingPlatform.Infrastructure/Services/DisbursementService.cs), relates to [FR-005](spec.md)). Record/edit only give an *early* over-disbursement signal; the authoritative gate re-runs comparison (c) against the freshly-read committed Σ at validation, because single-row `RowVersion` concurrency can't catch a cross-row invariant ([research R5](research.md#r5--over-disbursement-attribution)). It's lock-free by necessity (the retrying execution strategy forbids a serializing transaction).
- Question: is the validation-time re-read + distinct "would-exceed-allocation" refusal a sufficient race resolution for P1, or do you want an app-lock despite the retrying-strategy cost?

**Allocation snapshot reuses `ApplicationCurrencyTotal.Compute`** ([`RecordAsync`](../../src/FundingPlatform.Infrastructure/Services/DisbursementService.cs), [research R1](research.md#r1--allocation-amount-source-the-approved-total)). The first record snapshots the canonical CRC rollup into one immutable `Allocation` ledger entry; the projection reads the entry thereafter (falling back to `Compute` pre-first-disbursement).
- Question: is snapshot-at-first-disbursement the right moment, or should the allocation be frozen at execution instead?

**Two-SaveChanges vs one** ([`DisbursementService`](../../src/FundingPlatform.Infrastructure/Services/DisbursementService.cs)). Record + AttachEvidence use SaveChanges #1 (assign id) → #2 (audit), mirroring `FundsUsageEvidenceService`; edit/validate/cancel commit row+ledger+audit in a single SaveChanges (no new id needed in the payload).
- Question: is the asymmetry (two vs one) justified, or would you standardize on one pattern?

**DisbursementInbox added beyond the task list** ([`DisbursementInboxController.cs`](../../src/FundingPlatform.Web/Controllers/DisbursementInboxController.cs)). [T020](tasks.md) mandated a "Desembolsos" sidebar entry but no landing controller; rather than ship a dead link, I added a thin inbox reusing the spec-041 `IEvidenceInboxProjection` (identical row set: executed apps in active processes, group-scoped).
- Question: acceptable reuse, or should the sidebar entry have deep-linked differently?

### Areas where I'm less certain (5 min)

- [`DisbursementService.GetAsync`](../../src/FundingPlatform.Infrastructure/Services/DisbursementService.cs) recomputes discrepancies on read for *all* states, including Validated. A validated disbursement is locked, so its own comparisons can't drift, but a *later* over-disbursement by a sibling could make comparison (c) surface on the validated row's detail. It's display-only (the row stays Validated), but is that confusing?
- `ParseAmount`/`ParseDate` in [`DisbursementController`](../../src/FundingPlatform.Web/Controllers/DisbursementController.cs) bind money/date as strings and parse invariant-first (browser number inputs post `.`), falling back to es-CR. Whole-colón values are unambiguous; is the invariant-first choice right for real decimal inputs?
- Integration tests run on EF-InMemory (spec-036 precedent); the real-SQL `Byte→Int32` materialization + filtered-unique idempotency are proven by the E2E suite, not integration. Is that division acceptable, or do you want a live-SQL integration harness?

### Deviations and risks (5 min)

- **FR-025 Admin read-only** (`DisbursementController.CanWrite`, [FR-025](spec.md)): [plan.md R10](research.md#r10--routing--read-surface) / [contracts](contracts/interfaces.md) specified writes for "Financial Operator, Admin". The spec is explicit that **Auditor and Admin are read-only**, and for a money-movement surface that's the domain-correct segregation. I narrowed `CanWrite` to Financial Operator only (Admin now 403s on writes, matching FR-025). No E2E exercised Admin-writing-a-disbursement, so nothing regressed. Question: **is Admin-read-only the intended behavior, or did the plan mean Admin to retain super-user write here?** (If the latter, revert `CanWrite` to include Admin and evolve FR-025.)
- **es-CR copy is a static class, not `.resx`** ([`DisbursementResources.cs`](../../src/FundingPlatform.Web/Resources/DisbursementResources.cs)): [T031](tasks.md) named a `.resx`, but every existing resource in the codebase is a static class; I followed the codebase convention. Question: acceptable?
- **Views under `Views/Disbursement/` (singular)**, not the `Views/Disbursements/` [T030](tasks.md) wrote — MVC resolves views by controller name (`DisbursementController` → `Disbursement`). Cosmetic path deviation only.
