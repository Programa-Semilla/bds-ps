# Contracts: Evidence Graph & Required-Document Rules (spec 047)

Application-layer service interfaces + Web routes. Signatures are indicative (WHAT the boundary exposes), not final code. All reuse P1/P2 group-scoping (out-of-group → flat 404) and the `Financial Operator` write / Auditor+Admin read-only posture, except the matrix surface (Admin-only).

---

## `IEvidenceService` (Application/Evidence) — Financial Operator writer

```csharp
// Reads (group-scoped; Auditor/Admin read-only)
Task<IReadOnlyList<EvidenceSummary>> ListForApplicationAsync(int applicationId, CancellationToken ct);
Task<EvidenceDetail?> GetAsync(int applicationId, int evidenceId, CancellationToken ct);         // incl. version chain + allocations
Task<IReadOnlyList<EvidenceVersionRow>> GetVersionsAsync(int applicationId, int evidenceId, CancellationToken ct);
Task<EvidenceDownload?> OpenForDownloadAsync(int applicationId, int evidenceId, int? versionNumber, CancellationToken ct); // any version

// Writes (Financial Operator only)
Task<Result<int>> AttachAsync(AttachEvidenceCommand cmd, string actorUserId, CancellationToken ct);   // uploads blob, v1, orphan guard, alloc integrity
Task<Result> ReplaceAsync(ReplaceEvidenceCommand cmd, string actorUserId, CancellationToken ct);       // new version (reason required), supersede prior
Task<Result> AllocateAsync(AllocateEvidenceCommand cmd, string actorUserId, CancellationToken ct);      // replace-all Evidence↔line rows; Σ ≤ amount
Task<Result> DeleteAsync(int applicationId, int evidenceId, string actorUserId, CancellationToken ct);  // pre-close only; blob best-effort cleanup

// commands
record AttachEvidenceCommand(int ApplicationId, EvidenceType Type, int? DisbursementId, decimal Amount,
    string Currency, string DocumentReferenceNumber, DateOnly DocumentDate, int? SupplierId,
    IReadOnlyList<LineAllocationInput> Lines, /* uploaded file */ ...);
record ReplaceEvidenceCommand(int ApplicationId, int EvidenceId, string Reason, decimal? Amount, ..., /* new file? */);
record AllocateEvidenceCommand(int ApplicationId, int EvidenceId, IReadOnlyList<LineAllocationInput> Lines);
record LineAllocationInput(int ItemId, decimal Amount);
```

**Refusals** (`EvidenceReasons.Codes`): `Orphaned`, `AllocationExceedsAmount`, `LineClosed` (evidence locked on a closed line), `ReasonRequired` (replace without reason), `EvidenceLocked` (line closed).

## `IBudgetLineClosureService` (Application/Disbursements or Evidence) — Financial Operator

```csharp
Task<Result> CloseAsync(int applicationId, int itemId, string? reason, string actorUserId, CancellationToken ct);
Task<Result> ReopenAsync(int applicationId, int itemId, string reason, string actorUserId, CancellationToken ct); // reason required
Task<LineCompleteness> GetCompletenessAsync(int applicationId, int itemId, CancellationToken ct);   // required vs present per type
```

**Close gate (all re-checked against fresh reads):** required docs present (both sources D1) · every attributed payment `Validated` · `LinePaid == LineAccepted` (0.01) · each required evidence fully allocated. **Refusals** (`EvidenceReasons.Codes`): `MissingRequiredDocuments`, `PaymentNotValidated`, `LineEqualityMismatch`, `RequiredEvidenceNotFullyAllocated`, `AlreadyClosed`/`NotClosed`.

## `IDocumentRuleService` (Application/DocRules) — Admin only

```csharp
Task<IReadOnlyList<DocumentRuleSetRow>> ListAsync(CancellationToken ct);          // per-category + global default
Task<DocumentRuleSetDetail?> GetAsync(int? categoryId, CancellationToken ct);
Task<Result> UpsertAsync(UpsertDocumentRuleCommand cmd, string actorUserId, CancellationToken ct);  // full-replace items, docrule.* audit
record UpsertDocumentRuleCommand(int? CategoryId, IReadOnlyList<(EvidenceType Type, bool IsRequired)> Items);

// Resolution used by completeness (read side)
IReadOnlyCollection<EvidenceType> ResolveRequiredTypes(int? categoryId);          // category set else global default else empty
```

## Pure domain reconciliation (Domain/Services) — add to `DisbursementLineReconciliation`

```csharp
// 0.01 tolerance, Blocking discrepancies; fresh sums supplied by the service
static IReadOnlyList<LineOverpaymentDiscrepancy> EvaluateLineEquality(
    IReadOnlyList<LineEqualityInput> lines);   // LinePaid vs LineAccepted per line
readonly record struct LineEqualityInput(int ItemId, decimal LinePaid, decimal LineAccepted);
```

---

## Web routes

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/Applications/{applicationId}/Evidence` | FinOp/Auditor/Admin (group) | evidence list + per-line completeness |
| GET | `/Applications/{applicationId}/Evidence/{id}` | ″ | detail + version chain + allocations |
| POST | `/Applications/{applicationId}/Evidence` | FinOp | attach (`[UploadSizeGuard]` + magic-byte) |
| POST | `/Applications/{applicationId}/Evidence/{id}/Replace` | FinOp | new version (reason) |
| POST | `/Applications/{applicationId}/Evidence/{id}/Allocate` | FinOp | replace-all line allocations |
| POST | `/Applications/{applicationId}/Evidence/{id}/Delete` | FinOp | pre-close only |
| GET | `/Applications/{applicationId}/Evidence/{id}/Download` (`?v=`) | FinOp/Auditor/Admin | any version |
| POST | `/Applications/{applicationId}/Lines/{itemId}/Close` | FinOp | close budget-line |
| POST | `/Applications/{applicationId}/Lines/{itemId}/Reopen` | FinOp | reopen (reason) |
| GET/POST | `/Admin/DocumentRules`, `/Admin/CreateDocumentRule`, `/Admin/EditDocumentRule` | Admin | matrix CRUD (antiforgery, TempData es-CR) |

Existing `DisbursementController` `Index`/`Detail` gain the per-line completeness + `EvidenceIncomplete` + `Closed` badges and the Close/Reopen actions (reuse `IsAccessibleAsync`/`GuardWriteAsync`). New `EvidenceController` mirrors `DisbursementController`'s group-scope + upload-guard boundary.

## Audit contract

| Verb | When | Payload (for `ExtractIntId` routing) |
|---|---|---|
| `evidence.attached` / `.replaced` / `.allocated` | attach/replace/allocate | `{ evidenceId, applicationId, type }` |
| `closure.line_closed` / `.line_reopened` | close/reopen | `{ itemId, applicationId, reason }` |
| `docrule.upserted` | matrix save | `{ categoryId }` (null → global) |

## es-CR copy

- `FundingPlatform.Application/Evidence/EvidenceReasons.cs` + `DocRules/DocRuleReasons.cs` — service refusals (with `Codes`).
- `FundingPlatform.Web/Resources/EvidenceResources.cs` + `DocRuleResources.cs` — labels, evidence-type + status label/badge switch helpers.
