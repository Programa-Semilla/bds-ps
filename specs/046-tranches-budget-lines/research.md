# Phase 0 Research: Tranches & Budget-Lines (P2)

Consolidated decisions resolving the plan's unknowns. Each entry: **Decision → Rationale → Alternatives rejected**, grounded in the actual P1/040/`Item` code.

---

## D1 — Line commit: off-ledger status, not a ledger entry (spec OQ-1)

**Decision:** A committed line is a **mutable operational status on `Item`** (`CommitState` TINYINT), **not** a `DisbursementLedgerEntry`. `Committed` (the balance dimension) = Σ budgets of `Item`s whose `CommitState == Committed`.

**Rationale:** P1's append-only ledger holds only **settled cash facts** — one `Allocation` snapshot + one immutable `Disbursement` entry per validated payment (`LedgerEntryType { Allocation=0, Disbursement=1 }`). Commitment is an *obligation*, not cash movement (spec FR-009); putting it on the ledger would break the "ledger = committed facts" crux invariant and force a reversal vocabulary that the roadmap explicitly defers to P6. P1 already keeps analogous not-yet-settled state (recorded-but-unvalidated disbursements) **off-ledger** as mutable rows — commit follows the same precedent.

**Alternatives rejected:** (a) a `LedgerEntryType.Commitment` entry — would need reversal entries on un-commit, growing the immutability boundary before P6 is designed; (b) a separate `LineCommitment` table — an extra table + join for a single reversible boolean-ish state that `Item` can carry directly.

---

## D2 — Commit-state representation: enum column on `Item` (spec OQ-2)

**Decision:** `ItemCommitState : byte { Uncommitted = 0, Committed = 1 }`, stored as `dbo.Items.CommitState TINYINT NOT NULL CONSTRAINT DF_Items_CommitState DEFAULT (0)`, EF-mapped `HasConversion<byte>()`. No `CommittedBy/At` columns — who/when is captured by the `line.committed` / `line.uncommitted` `AdminAuditEvent` (FR-022).

**Rationale:** Mirrors the existing `Item.ReviewStatus` (TINYINT enum, `DF_Items_ReviewStatus DEFAULT (0)`) exactly — a nullable-safe inline column add with a zero default, so pre-P2 rows are `Uncommitted` with no backfill (spec 032 `UserCode` / spec 037 `CompanyId` precedent). The **`HasConversion<byte>()` is mandatory** — specs 035/040/045 all hit `Byte→Int32` materialization failures that EF-InMemory hid and only real SQL caught (guarded by `DisbursementEnumMaterializationTests`); the plan's integration tests must materialize this on real SQL.

**Un-commit reversibility guard (FR-007):** "has a recorded payment" is derived, not a column — `Uncommit` is refused iff any `DisbursementLineAllocation` row references the item under a non-cancelled disbursement. No extra state needed.

**Alternatives rejected:** `bool IsCommitted` — an enum leaves room for future line states (e.g. a P4 `Cancelled`) without a schema type change; `ItemReviewStatus` reuse — conflates review approval with financial obligation (different lifecycles).

---

## D3 — Budget-line "status" filter values (spec OQ-3 / FR-020)

**Decision:** Budget-line **status** is a **derived projection**, not stored, with these values (es-CR labels in `TrancheResources`):

| Status | Derivation |
|---|---|
| `Uncommitted` | `Item.CommitState == Uncommitted` |
| `Committed` | committed, no non-cancelled payment attributed |
| `PartiallyPaid` | committed, Σ non-cancelled attributions `> 0` and `<` line budget |
| `Paid` | committed, Σ non-cancelled attributions `≥` line budget, not all validated |
| `Validated` | committed, all attributions on that line are on validated disbursements and Σ ≥ budget |

**Validation state** is a *separate* filter facet (FR-020 lists both "status" and "validation state"): `HasPending` / `FullyValidated`. Both computed in-query on the composed projection; no persisted status column (avoids a status-drift maintenance burden — the same reasoning behind P1 keeping `DisbursementState` derived-on-mutation).

**Rationale:** Line status is a pure function of commit state + attribution sums + disbursement states already in the model; storing it would duplicate truth and risk drift. Mirrors P1, which never persisted a participant "status."

**Alternatives rejected:** a stored `Item.LineStatus` column recomputed on every disbursement mutation — drift risk + extra write coupling for zero read benefit at this scale.

---

## D4 — Tranche freeze: virtual default tranche + execution guard (FR-002, FR-004, FR-005)

**Decision (default tranche):** Do **not** materialize a default `Tranche` row. A line's tranche = its explicit `Item.TrancheId`, or a **synthetic default tranche** (no row) when `TrancheId IS NULL`. The composed projection presents the synthetic tranche (es-CR "General") **iff** ≥1 line has `TrancheId == null`. An application whose reviewer defined no tranches shows exactly one synthetic tranche = all lines (FR-002); every pre-P2 executed application is unchanged with zero migration (FR-005, SC-006).

**Structural zero-colón guarantee:** because every priced line belongs to exactly one tranche (explicit *or* synthetic), Σ (all tranche amounts, incl. synthetic) = Σ all line budgets = allocation, always — FR-003 holds by construction with no runtime partition check.

**Decision (freeze):** Tranche mutations (create/rename/delete/assign/unassign) are guarded in the `Application` aggregate on **`State != AgreementExecuted`** (throw once executed) and are surfaced in the UI only on the **reviewer pre-audit surface** (`ReviewController.Review` when `ShowReviewerChecklist == true`, i.e. `State == ResponseFinalized` with no `FundingAgreement`). There is **no existing execution hook** (`Application.ExecuteAgreement` at `ResponseFinalized → AgreementExecuted` is a pure state flip; the P1 allocation snapshot is lazy at first `Record`, post-execution) — so freeze is enforced by the domain guard, not a new hook. This is simpler and race-free (the state is authoritative).

**Rationale:** `Item`s are already stable post-execution (applicant can't edit post-submit; reviewer sets `SelectedSupplier` before `ResponseFinalized`), so deriving tranche amounts live from frozen lines is stable and equal to the ledger snapshot. Virtual-default avoids a migration and a lazy-row hook entirely.

**Alternatives rejected:** (a) lazy default-`Tranche` row at execution (modeled on the P1 allocation snapshot) — adds a hook + a filtered-unique index + backfill semantics for zero benefit over the virtual approach; (b) forcing "all lines assigned before send-to-audit" — unnecessary friction; the synthetic bucket absorbs leftovers while preserving the zero-colón guarantee.

---

## D5 — Per-line budget derivation: extract `ApplicationCurrencyTotal.LineBudget(Item)`

**Decision:** Add a per-item helper `static decimal LineBudget(Item item)` to `ApplicationCurrencyTotal` (Application layer) that returns the selected quotation's CRC amount, reusing the exact load-bearing logic already inlined in `Compute`:

```csharp
// selected quotation = the one matching the chosen supplier; skip legacy-needs-review
var chosen = item.Quotations.FirstOrDefault(q => q.SupplierId == item.SelectedSupplierId);
return chosen is { LegacyNeedsReview: false, ConvertedCrcAmount: { } amt } ? amt : 0m;
```

`Compute` is refactored to call `LineBudget` per item (behavior-preserving). The **composed EF projection** replicates the same selection in LINQ (correlated: `Quotations` where `SupplierId == Item.SelectedSupplierId && !LegacyNeedsReview`, take `ConvertedCrcAmount`).

**Rationale:** the per-line "budget" is exactly the number that already rolls up into the allocation total (`chosen.ConvertedCrcAmount`, a pinned snapshot on `Quotation`) — no new pricing concept, and one shared helper keeps tranche/line composition from ever drifting from the allocation. The over-payment domain check (in-memory) uses `LineBudget`; the projection (SQL) uses the LINQ twin.

**Invariant (SC-003):** participant `Allocated` in the composed view = Σ line budgets = Σ tranche amounts = the P1 ledger snapshot (`DisbursementAllocation.ResolveAsync`). The three are computed identically (frozen lines), so each level equals the sum of its children to the colón. The composed projection computes participant `Allocated` as Σ line budgets for internal consistency and asserts equality with the ledger snapshot in an integration test.

**Alternatives rejected:** storing a per-item budget snapshot column — the `Quotation.ConvertedCrcAmount` is already the pinned snapshot; duplicating it invites drift.

---

## D6 — Line-level reconciliation: new pure `DisbursementLineReconciliation`

**Decision:** Add a pure static `Domain/Services/DisbursementLineReconciliation.cs` (parallel to P1's `DisbursementReconciliation`), and extend `ReconciliationComparison` with `DisbursementSplitVsTotal = 3` and `LinePaymentVsBudget = 4`:

- **Split integrity (at Record/Edit):** `EvaluateSplit(decimal disbursementAmount, IReadOnlyList<(int ItemId, decimal Amount)> lines)` → a blocking `ReconciliationDiscrepancy(DisbursementSplitVsTotal, …)` iff `|Σ line amounts − disbursementAmount| ≥ 0.01`.
- **Per-line over-payment (at Validar):** `EvaluateLineOverpayments(IReadOnlyList<LinePaymentVsBudget> lines)` → one blocking `LineOverpaymentDiscrepancy(ItemId, LineLabel, Committed, Paid, Overage)` per line where `Paid − Committed ≥ 0.01`. Symmetric with P1's `TotalVsAllocation` over-disbursement check: re-computed at `Validar` against **freshly-read** committed sums (closes the concurrent-partial-payment race single-row `RowVersion` can't catch — the P1 R5 lesson).

Zero tolerance reuses P1's `MinDetectableDifference = 0.01`. All produced discrepancies are `DiscrepancySeverity.Blocking`.

**Rationale:** keeps P1's `DisbursementReconciliation` (the three participant-level comparisons) untouched and adds the line dimension as a sibling pure service — deterministic (NFR-020), unit-testable in isolation, no I/O. The `DisbursementService.ValidateAsync` gate calls both services after its existing evidence/participant checks.

**Alternatives rejected:** folding line checks into `DisbursementReconciliation.Evaluate` — would bloat one method with two granularities and force every caller to pass line data even when only participant reconciliation is needed.

---

## D7 — Attribution & tranche persistence: mutable-set + join, mirroring P1 write patterns

**Decision:**
- `RecordDisbursementCommand`/`EditDisbursementCommand` gain `IReadOnlyList<LineAllocationInput> Lines` (`ItemId`, `Amount`). `DisbursementService.RecordAsync`/`EditAsync`: validate all `ItemId`s belong to the application and are `Committed` (else blocking reason), run `EvaluateSplit` (blocking), then persist `DisbursementLineAllocation` rows as the disbursement's owned set (replace-all on Edit). Follows P1's two-SaveChanges audit pattern; the `disbursement.recorded`/`.edited` audit payload carries the split array.
- Commit/Uncommit: new `IDisbursementService.CommitLineAsync`/`UncommitLineAsync(applicationId, itemId, actor)` → `Item.Commit()`/`Uncommit()` guarded methods; Uncommit refused if any non-cancelled `DisbursementLineAllocation` references the item; audit `line.committed`/`line.uncommitted`.
- Tranche CRUD: new `ITrancheService`/`TrancheService` (mirrors `FundService` shape) with `CreateAsync`/`RenameAsync`/`DeleteAsync`/`AssignItemAsync`/`UnassignItemAsync`, all through `Application` aggregate methods; audit `tranche.*`.

**Join table** follows the `ItemImpacts` template exactly: surrogate `Id` PK, FK to `Disbursements` **CASCADE** (single ownership path) + FK to `Items` **NO ACTION** (avoids the multiple-cascade-path publish failure — the spec-029/035 lesson), composite `UNIQUE (DisbursementId, ItemId)` (one attribution row per line per disbursement), covering `IX` on `ItemId`, `CK Amount > 0`, `RowVersion`.

**Rationale:** every choice mirrors a shipping pattern (P1 service commit discipline, `ItemImpacts` join FK topology, `FundService` CRUD shape, `AssignLineCodeToItem` aggregate mediation) → minimal novel surface, maximal review confidence.

---

## D8 — Roles, placement, audit: reuse, no new role

**Decision:** No new role. **Reviewer** owns tranche setup (new `TrancheController` at `Review/{applicationId:int}/Tranches`, `[Authorize(Roles="Reviewer,Admin")]`, group-overlap gate mirroring `ReviewController`). **Financial Operator** owns commit + attribution (extends `DisbursementController`, which already authorizes `Financial Operator,Admin,Auditor` with **write = Financial Operator only**, Auditor/Admin read-only, executed-state + group-overlap 404 no-disclosure). New audit prefixes in `AdminAuditEventWriter`: `tranche.*` → `TargetTypeTranche` (id from payload `trancheId`) and `line.*` → target `Item` (id from payload `itemId`); attribution rides the existing `disbursement.*` prefix.

**Rationale:** the Financial Operator role + its group-scoping (`NormalizeGroupIdsForRole` keeps its groups; only Admin is groupless) already ships from P1; the reviewer group-overlap gate already ships from 040. Zero role/seed work — `10_SeedFinancialOperatorRole.sql` stands.

**Alternatives rejected:** a dedicated tranche-setup role — over-segmentation; the reviewer already authors line codes / prepares the agreement on the same surface.
