# Phase 1 Contracts: Tranches & Budget-Lines (P2)

Service interfaces, DTOs, controller routes, and reason codes. All new/edited surfaces reuse P1/040 patterns (`Result`/`Result<T>`, `List<DomainError>`, es-CR reason codes, group-overlap + executed-state 404 no-disclosure).

## 1. Reviewer tranche setup — `ITrancheService` (NEW, Application/Tranches)

```csharp
public interface ITrancheService
{
    Task<IReadOnlyList<TrancheView>> GetForApplicationAsync(int applicationId, CancellationToken ct);
    Task<Result<int>> CreateAsync(int applicationId, string name, string actorUserId, CancellationToken ct);
    Task<Result> RenameAsync(int applicationId, int trancheId, string name, string actorUserId, CancellationToken ct);
    Task<Result> DeleteAsync(int applicationId, int trancheId, string actorUserId, CancellationToken ct);
    Task<Result> AssignItemAsync(int applicationId, int itemId, int? trancheId, string actorUserId, CancellationToken ct);
}
```

**DTOs** (`TrancheDtos.cs`): `TrancheView(int Id, string Name, int Ordinal, decimal DerivedAmount, IReadOnlyList<int> ItemIds)`; `TrancheEditorLine(int ItemId, string? LineCode, string ProductName, decimal Budget, int? TrancheId)`.

**Behavior:** every method resolves the application, enforces `State != AgreementExecuted` (else a `TrancheFrozen` reason — freeze, D4), routes CRUD through the `Application` aggregate methods, and writes a `tranche.*` `AdminAuditEvent` using the two-SaveChanges pattern (mirrors `FundService`). Duplicate name → `TrancheNameInUse` (accent/case pre-check via `CompanyNameNormalizer` + `UX_Tranches_ApplicationId_Name` `DbUpdateException` backstop). Implementation `Infrastructure/Services/TrancheService.cs`.

## 2. Commit + attribution — `IDisbursementService` (EDIT, Application/Disbursements)

```csharp
// NEW methods
Task<Result> CommitLineAsync(int applicationId, int itemId, string actorUserId, CancellationToken ct);
Task<Result> UncommitLineAsync(int applicationId, int itemId, string actorUserId, CancellationToken ct);

// EDIT — commands carry the per-line split
public sealed record LineAllocationInput(int ItemId, decimal Amount);
public sealed record RecordDisbursementCommand( /* …P1 fields… */ IReadOnlyList<LineAllocationInput> Lines);
public sealed record EditDisbursementCommand(   /* …P1 fields… */ IReadOnlyList<LineAllocationInput> Lines);
```

**`CommitLineAsync`:** item ∈ application, `State == AgreementExecuted`, `Item.Commit()`; audit `line.committed`. Idempotent.
**`UncommitLineAsync`:** refuse (`LineHasPayment`) if any non-cancelled `DisbursementLineAllocation` references the item; else `Item.Uncommit()`; audit `line.uncommitted`.
**`RecordAsync`/`EditAsync`:** after P1's executed/amount/bankref checks — validate every `Lines[].ItemId` ∈ application and `CommitState == Committed` (else `LineNotCommitted`), run `DisbursementLineReconciliation.EvaluateSplit` (blocking → `SplitMismatch`), persist the `DisbursementLineAllocation` set (replace-all on Edit). Audit payload gains `lines`.
**`ValidateAsync` (EDIT):** after P1 evidence + participant `Evaluate` gate, add the per-line over-payment gate — read fresh per-line committed budgets + non-cancelled payment sums for the lines this disbursement touches, call `DisbursementLineReconciliation.EvaluateLineOverpayments`; any discrepancy → block with `LineOverpayment` (does **not** post the ledger entry). Symmetric with P1's `WouldExceedAllocation`.

## 3. Composed balances — `IParticipantBalanceProjection` (EDIT)

```csharp
Task<ParticipantBalance> GetForApplicationAsync(int applicationId, CancellationToken ct);      // now 6-dim
Task<ComposedBalance> GetComposedForApplicationAsync(int applicationId, CancellationToken ct);  // NEW tree
```

`ComposedBalance` / `TrancheBalance` / `BudgetLineBalance` / `BudgetLineStatus` per data-model. EF impl reuses `DisbursementAllocation.ResolveAsync` for the ledger cross-check and the `LineBudget` LINQ twin for per-line budgets. `AsNoTracking`, bounded correlated queries.

## 4. Web routes

**`TrancheController`** (NEW) — `[Authorize(Roles="Reviewer,Admin")]`, `[Route("Review/{applicationId:int}/Tranches")]`, group-overlap gate (`ApplicantSharesAnyGroupAsync` else `Forbid`), all POSTs `[ValidateAntiForgeryToken]`:

| Action | Verb / route | Purpose |
|---|---|---|
| `Create` | `POST ""` | create tranche (name) |
| `Rename` | `POST "{trancheId:int}/Rename"` | rename |
| `Delete` | `POST "{trancheId:int}/Delete"` | delete (re-parent lines → synthetic) |
| `Assign` | `POST "Assign"` | assign item → tranche (`itemId`, `trancheId?`) |

Rendered as `_TrancheEditor.cshtml` on `Review.cshtml` when `ShowReviewerChecklist == true` (`ResponseFinalized`, no agreement). Item selector uses `data-searchable` (spec 031).

**`DisbursementController`** (EDIT) — same class auth (`Financial Operator,Admin,Auditor`; write = Financial Operator; executed+group 404 no-disclosure), `[Route("Applications/{applicationId:int}/Disbursements")]`:

| Action | Verb / route | Notes |
|---|---|---|
| `Commit` (NEW) | `POST "Lines/{itemId:int}/Commit"` | `GuardWriteAsync` then `CommitLineAsync` |
| `Uncommit` (NEW) | `POST "Lines/{itemId:int}/Uncommit"` | `UncommitLineAsync` |
| `Record` (EDIT) | `POST "Record"` | VM binds a per-line split editor → `Lines` |
| `Edit` (EDIT) | `POST "{disbursementId:int}/Edit"` | replace splits |
| `Index` (EDIT) | `GET ""` | now `GetComposedForApplicationAsync`; renders `_TrancheBalancePanel` + `_BudgetLineRow` (per-line commit buttons + status) |

## 5. Reason codes (es-CR) — `DisbursementReasons` (EDIT) + `TrancheResources` (NEW)

| Code | When | es-CR gist |
|---|---|---|
| `SplitMismatch` | Σ line-allocations ≠ amount | "La suma de las líneas no coincide con el monto del desembolso." |
| `LineNotCommitted` | attribution to uncommitted line | "La línea debe comprometerse antes de asignarle un pago." |
| `LineOverpayment` | Validar: Σ payments > committed | "El pago de la línea «{code}» excede su presupuesto comprometido." |
| `LineHasPayment` | un-commit with a payment | "No se puede descomprometer una línea con pagos registrados." |
| `TrancheFrozen` | tranche edit post-execution | "La estructura de tramos quedó congelada al ejecutarse el convenio." |
| `TrancheNameInUse` | duplicate tranche name | "Ya existe un tramo con ese nombre en esta solicitud." |

Reason codes map to es-CR via the existing `Result.Failure` → resource path (P1 pattern). Line labels use `LineCode ?? APP-line fallback`.

## 6. Contract test coverage (maps to spec Success Criteria)

- **SC-001 / FR-003:** integration — for a tranched application, Σ tranche `DerivedAmount` = `DisbursementAllocation.ResolveAsync` snapshot, to the colón; synthetic tranche present iff any line unassigned.
- **SC-002 / FR-013:** unit — `EvaluateSplit` rejects mismatched splits; integration — `RecordAsync` refuses `SplitMismatch`, accepts exact.
- **SC-003:** integration — composed projection: each tranche = Σ its lines, participant = Σ tranches, participant `Allocated` = ledger snapshot.
- **SC-004 / FR-019:** unit — `EvaluateLineOverpayments`; integration — `ValidateAsync` blocks `LineOverpayment`, re-checked against fresh sums (concurrent-attribution race).
- **SC-005 / FR-020:** integration — filter budget-lines by tranche/status/supplier/validation-state.
- **SC-006 / FR-005:** integration — a pre-P2 executed application (no tranche rows, `CommitState` default 0) yields the P1 balances unchanged and one synthetic tranche.
- **FR-007:** integration — `UncommitLineAsync` refused once a payment is attributed.
- **FR-021:** E2E — Auditor/Admin see read-only (no commit/attribute/tranche-edit); Financial Operator writes; reviewer owns tranche setup.
