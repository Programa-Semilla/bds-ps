# Phase 1 Data Model: Tranches & Budget-Lines (P2)

Additive to the spec-045 model. **New:** `Tranche`, `DisbursementLineAllocation`, `ItemCommitState`, `DisbursementLineReconciliation` + its VOs. **Edited:** `Item`, `Application`, `ParticipantBalance`, `ReconciliationComparison`.

## Enums (Domain/Enums)

```
ItemCommitState : byte { Uncommitted = 0, Committed = 1 }               // NEW — dbo.Items.CommitState TINYINT
ReconciliationComparison : byte {                                       // EDIT — +3, +4
    DisbursementVsBankReceipt = 0, DisbursementVsInvoice = 1, TotalVsAllocation = 2,
    DisbursementSplitVsTotal = 3,   // NEW — Σ line-allocations vs disbursement amount
    LinePaymentVsBudget = 4         // NEW — Σ payments to a line vs its committed budget
}
```

All TINYINT enums require `HasConversion<byte>()` in EF (035/040/045 `Byte→Int32` lesson; verify on real SQL).

## Aggregate 1 — `Tranche` (NEW)

**Domain entity** `Domain/Entities/Tranche.cs` (`sealed`), keyed by `ApplicationId`. A named funding phase grouping the application's `Item`s. Amount is **not stored** (derived at projection = Σ member-line budgets).

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `ApplicationId` | int | FK → Applications, NO ACTION |
| `Name` | string | ≤ 60, trimmed, required; unique per application (case-insensitive) |
| `Ordinal` | int | display order (1, 2, 3…); assigned by service |
| `CreatedAtUtc` | DateTimeOffset(0) | `DF` `SYSUTCDATETIME()` |
| `UpdatedAtUtc` | DateTimeOffset(0) | |
| `RowVersion` | byte[] | optimistic concurrency |

**Behavior:** `static Tranche Create(int applicationId, string name, int ordinal)` (trim, ≤60, non-empty); `void Rename(string name)` (same guards). Uniqueness of `Name` within an application and the freeze/state gate are enforced by the **`Application` aggregate root** (see below), not by `Tranche` alone — `Tranche` never sees its siblings. No hard-delete method on the entity; deletion is via the aggregate (which re-parents member lines to null → the synthetic tranche).

**Table** `Database/Tables/dbo.Tranches.sql`: PK clustered `Id`; `FK_Tranches_Applications … ON DELETE NO ACTION`; `Name NVARCHAR(60) NOT NULL`; `Ordinal INT NOT NULL`; `CreatedAtUtc/UpdatedAtUtc DATETIMEOFFSET(0)` with `DF_`; `ROWVERSION`; index `IX_Tranches_ApplicationId`; **`UX_Tranches_ApplicationId_Name UNIQUE (ApplicationId, Name)`** (one name per application — DB backstop; the accent/case pre-check lives in the service, mirroring `CompanyNameNormalizer`).

## Aggregate 2 — `DisbursementLineAllocation` (NEW — the M:N join)

**Domain entity** `Domain/Entities/DisbursementLineAllocation.cs` (`sealed`), the attribution of a portion of a disbursement to one committed line. Owned by the `Disbursement` (replace-all set on Record/Edit).

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `DisbursementId` | int | FK → Disbursements, **CASCADE** (single ownership path) |
| `ItemId` | int | FK → Items, **NO ACTION** (avoids multiple-cascade-path) |
| `Amount` | decimal(18,2) | `CK > 0` |
| `RowVersion` | byte[] | |

**Behavior:** `static DisbursementLineAllocation For(int disbursementId, int itemId, decimal amount)` (amount > 0). No mutators — a split change replaces the row set (mirrors how evidence is Replaced, not patched).

**Table** `Database/Tables/dbo.DisbursementLineAllocations.sql` (models `dbo.ItemImpacts.sql`): PK clustered `Id`; `FK_DisbursementLineAllocations_Disbursements … ON DELETE CASCADE`; `FK_DisbursementLineAllocations_Items … ON DELETE NO ACTION`; `CK_DisbursementLineAllocations_Amount_Positive CHECK ([Amount] > 0)`; **`UX_DisbLineAlloc_Disbursement_Item UNIQUE (DisbursementId, ItemId)`** (≤1 attribution per (disbursement, line)); covering `IX_DisbLineAlloc_ItemId ON (ItemId)` (per-line payment sums); `ROWVERSION`.

**Cascade note:** two FK paths reach `Applications` (via `Disbursements` and via `Items`); only the `Disbursements` path is CASCADE, the `Items` path is NO ACTION — exactly the `ItemImpacts` topology, so the dacpac publishes without a multiple-cascade-path error.

## Edited entity — `Item` (budget-line)

Add two columns + guarded behaviors; everything else (spec 018/035/039) unchanged.

| New field | Type | Notes |
|---|---|---|
| `TrancheId` | int? | FK → Tranches, NO ACTION; null = synthetic default tranche |
| `CommitState` | ItemCommitState (TINYINT) | `DF (0)` Uncommitted |

**New behavior (all aggregate-mediated — `internal` where the root is the single entry point):**
- `internal void AssignTranche(int? trancheId)` — sets `TrancheId`; called by `Application.AssignItemToTranche`.
- `internal void Commit()` — guard `CommitState == Uncommitted` (idempotent no-op if already committed); sets `Committed`. Called by the Financial-Operator service path via the aggregate or directly on the tracked item (commit is post-execution, operator-owned — see contracts).
- `internal void Uncommit()` — sets `Uncommitted`; the "no recorded payment" guard is enforced by the service (queries `DisbursementLineAllocation`), not the entity (the entity can't see allocations).

Per-line **budget** stays derived (not a column): `ApplicationCurrencyTotal.LineBudget(item)` = selected quotation's `ConvertedCrcAmount` (0 if none / legacy-needs-review).

**Table edit** `dbo.Items.sql` (inline, no post-deploy backfill — spec 032/037 precedent):
```sql
[TrancheId]   INT     NULL,
[CommitState] TINYINT NOT NULL CONSTRAINT [DF_Items_CommitState] DEFAULT (0),
...
CONSTRAINT [FK_Items_Tranches] FOREIGN KEY ([TrancheId]) REFERENCES [dbo].[Tranches]([Id]) ON DELETE NO ACTION,
...
CREATE NONCLUSTERED INDEX [IX_Items_TrancheId] ON [dbo].[Items] ([TrancheId]) WHERE [TrancheId] IS NOT NULL;
```

## Edited aggregate root — `Application`

New aggregate-mediated methods (mirror `AssignLineCodeToItem` — look up in `_items`/`_tranches`, enforce sibling uniqueness, delegate, bump `UpdatedAt`). All guard **`State != AgreementExecuted`** (throw `InvalidOperationException` once executed — the freeze, D4). New backing `List<Tranche> _tranches` + `IReadOnlyList<Tranche> Tranches`.

- `Tranche CreateTranche(string name)` — reject duplicate name (case/accent-insensitive), assign next `Ordinal`.
- `void RenameTranche(int trancheId, string name)` — reject duplicate.
- `void DeleteTranche(int trancheId)` — re-parent member items to `TrancheId = null` (→ synthetic), then remove the tranche.
- `void AssignItemToTranche(int itemId, int? trancheId)` — validate both belong to the aggregate; `null` unassigns (→ synthetic).

Commit/Uncommit are **post-execution** (operator), so they are **not** blocked by the execution freeze; they live on `Item` and are driven by the disbursement service (which owns the "no payment" guard). The execution freeze applies only to tranche structure + assignment (reviewer, pre-execution).

## Value objects (Domain)

```csharp
// EDIT — ParticipantBalance gains Committed (5 → 6 dims). Available = Allocated − Paid (unchanged).
public sealed record ParticipantBalance(
    decimal Allocated, decimal Committed, decimal Paid,
    decimal Validated, decimal PendingValidation, decimal Available);

// NEW — per-line over-payment discrepancy (line-scoped sibling of ReconciliationDiscrepancy)
public sealed record LineOverpaymentDiscrepancy(
    int ItemId, string LineLabel, decimal Committed, decimal Paid, decimal Overage,
    DiscrepancySeverity Severity /* = Blocking */);

// NEW — input row to the per-line over-payment evaluator
public readonly record struct LinePaymentVsBudget(int ItemId, string LineLabel, decimal CommittedBudget, decimal PaidToLine);
```

`ReconciliationDiscrepancy` (P1) is reused verbatim for the split-integrity comparison (no line context needed — it concerns the whole disbursement).

## Pure domain service — line reconciliation (NEW)

`Domain/Services/DisbursementLineReconciliation.cs` (static, pure, deterministic — NFR-020; `MinDetectableDifference = 0.01` reused):

```csharp
// Split integrity — at Record/Edit. Blocking iff |Σ lines − amount| ≥ 0.01.
static IReadOnlyList<ReconciliationDiscrepancy> EvaluateSplit(
    decimal disbursementAmount, IReadOnlyList<(int ItemId, decimal Amount)> lines);

// Per-line over-payment — at Validar, re-checked against freshly-read sums (P1 R5 symmetry).
static IReadOnlyList<LineOverpaymentDiscrepancy> EvaluateLineOverpayments(
    IReadOnlyList<LinePaymentVsBudget> lines); // one Blocking per line where Paid − Committed ≥ 0.01
```

## Balance projection — composed tree (Application DTOs)

`IParticipantBalanceProjection.GetComposedForApplicationAsync(applicationId, ct)` → `ComposedBalance`:

```csharp
public sealed record ComposedBalance(ParticipantBalance Participant, IReadOnlyList<TrancheBalance> Tranches);
public sealed record TrancheBalance(
    int? TrancheId, string Name, int Ordinal,         // TrancheId null = synthetic "General"
    ParticipantBalance Balance, IReadOnlyList<BudgetLineBalance> Lines);
public sealed record BudgetLineBalance(
    int ItemId, string? LineCode, string ProductName, string? SupplierName,
    ItemCommitState CommitState, BudgetLineStatus Status,   // Status = derived (D3)
    ParticipantBalance Balance);                            // per-line 6-dim (Allocated = line budget)
public enum BudgetLineStatus { Uncommitted, Committed, PartiallyPaid, Paid, Validated }
```

**Composition rules (SC-003 — each level = Σ children):**
- Line `Allocated` = `LineBudget(item)`; `Committed` = budget if `CommitState==Committed` else 0; `Validated`/`Pending` = Σ its `DisbursementLineAllocation.Amount` where parent disbursement is Validated / (Recorded∨Inconsistent); `Paid` = Validated+Pending; `Available` = Allocated − Paid (may be negative).
- Tranche = Σ its lines (synthetic tranche = Σ lines with null `TrancheId`).
- Participant = Σ tranches; equals the P1 flat `ParticipantBalance` (Allocated reconciles to the `DisbursementAllocation.ResolveAsync` ledger snapshot — asserted by integration test).

P1's flat `GetForApplicationAsync` stays (now returns the 6-dim `ParticipantBalance`; `Committed` = Σ committed line budgets).

## Relationships

```text
Application (1) ──< Tranche (0..N)                    [FK ApplicationId, NO ACTION]
Tranche (1) ──< Item (0..N)                           [FK Item.TrancheId, NO ACTION; null = synthetic]
Disbursement (1) ──< DisbursementLineAllocation (1..N) [FK DisbursementId, CASCADE]
Item (1) ──< DisbursementLineAllocation (0..N)         [FK ItemId, NO ACTION]
   → UNIQUE (DisbursementId, ItemId): ≤1 attribution per (disbursement, line)
```

## EF configuration notes (Infrastructure/Persistence/Configurations)

- `TrancheConfiguration` (NEW): `ToTable("Tranches")`; `Name` `HasMaxLength(60)`; `RowVersion` `IsRowVersion()`; `HasIndex(t => t.ApplicationId)`; `HasIndex(t => new { t.ApplicationId, t.Name }).IsUnique().HasDatabaseName("UX_Tranches_ApplicationId_Name")`; FK to `Application` `OnDelete(Restrict)`, no nav collection exposed beyond the aggregate list.
- `DisbursementLineAllocationConfiguration` (NEW): `ToTable("DisbursementLineAllocations")`; `Amount` `decimal(18,2)`; `RowVersion` `IsRowVersion()`; `HasIndex(a => a.ItemId)`; `HasIndex(a => new { a.DisbursementId, a.ItemId }).IsUnique()`; FK to `Disbursement` `OnDelete(Cascade)`, FK to `Item` `OnDelete(ClientCascade)` (mirrors `ItemImpactConfiguration`).
- `ItemConfiguration` (EDIT): `Property(i => i.CommitState).HasConversion<byte>().IsRequired()`; `HasOne<Tranche>().WithMany().HasForeignKey(i => i.TrancheId).OnDelete(DeleteBehavior.Restrict)` (mirrors the `SelectedSupplier` block already there); filtered index `HasIndex(i => i.TrancheId).HasFilter("[TrancheId] IS NOT NULL")`.
- Registration: configs auto-picked by `ApplyConfigurationsFromAssembly`; add `DbSet<Tranche> Tranches => Set<Tranche>()` and `DbSet<DisbursementLineAllocation> DisbursementLineAllocations => Set<DisbursementLineAllocation>()` to `AppDbContext` under a `// Spec 046` comment. `_tranches` on `Application` needs a backing-field mapping (mirror `_items`).

## Dacpac deliverables

- **New tables:** `dbo.Tranches.sql`, `dbo.DisbursementLineAllocations.sql`.
- **Edited table:** `dbo.Items.sql` (+`TrancheId`, +`CommitState`, +FK, +filtered index) — inline, no post-deploy backfill.
- **No new post-deploy script** — Financial Operator role already seeded by `10_SeedFinancialOperatorRole.sql`.

## Audit actions (AdminAuditEvent string constants + writer prefixes)

New constants on `AdminAuditEvent` + routing in `AdminAuditEventWriter`:
- `tranche.created`, `tranche.renamed`, `tranche.deleted`, `tranche.item_assigned`, `tranche.item_unassigned` → new `tranche.` prefix → `TargetTypeTranche` (id from payload `trancheId`).
- `line.committed`, `line.uncommitted` → new `line.` prefix → target `Item` (id from payload `itemId`).
- Per-line attribution rides the existing `disbursement.recorded` / `disbursement.edited` (payload gains a `lines: [{itemId, amount}]` array).
