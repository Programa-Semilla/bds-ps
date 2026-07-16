# Phase 1 Contracts: Financial Disbursement Core

**Spec:** spec.md · **Data model:** data-model.md · **Date:** 2026-07-15

The project is a server-rendered ASP.NET MVC app. "Contracts" = Application-layer service interfaces (the seams the Web layer depends on) + the HTTP endpoint surface + the AdminAudit event vocabulary. No public REST API is exposed.

## Application-layer interfaces

### `IDisbursementService` (Application/Disbursements/)

```csharp
Task<IReadOnlyList<DisbursementListItem>> ListAsync(int applicationId, CancellationToken ct);
Task<DisbursementDetail?>                 GetAsync(int applicationId, int disbursementId, CancellationToken ct);
Task<Result<int>>   RecordAsync(RecordDisbursementCommand cmd, string actorUserId, CancellationToken ct);
Task<Result>        EditAsync(EditDisbursementCommand cmd, string actorUserId, CancellationToken ct);
Task<Result<int>>   AttachEvidenceAsync(AttachDisbursementEvidenceCommand cmd, string actorUserId, CancellationToken ct); // create-or-replace per Kind
Task<Result>        ValidateAsync(int applicationId, int disbursementId, string actorUserId, CancellationToken ct);
Task<Result>        CancelAsync(int applicationId, int disbursementId, string actorUserId, CancellationToken ct);
Task<DisbursementEvidenceDownload?> OpenEvidenceForDownloadAsync(int applicationId, int disbursementId, EvidenceKind kind, CancellationToken ct);
```

- `Result`/`Result<T>` = existing collect-all-errors result shape (Constitution: all validation errors surfaced at once). Concurrency surfaced as a retryable error (`ex.GetType().Name == "DbUpdateConcurrencyException"`), es-CR message.
- `RecordAsync` gate: application `AgreementExecuted`; amount > 0; CRC. Posts the `Allocation` ledger entry if none exists (idempotent via filtered-unique index), in the same SaveChanges as the disbursement. Recomputes State via the pure evaluator.
- `EditAsync` / `AttachEvidenceAsync` gate: `State ∈ {Recorded, Inconsistent}`; re-run reconciliation (FR-016) and persist derived State.
- `ValidateAsync`: gate `IsValidatable` (both evidence present + zero discrepancies); flips State=Validated and posts the immutable `Disbursement` ledger entry (one SaveChanges); refuses with a specific es-CR reason otherwise (missing-evidence vs has-discrepancy).
- `CancelAsync`: gate `State ∈ {Recorded, Inconsistent}`.
- Every mutating method writes an `AdminAuditEvent` (`disbursement.*`) with before/after in payload, as `SaveChanges #2` (two-SaveChanges house pattern).

### `IParticipantBalanceProjection` (Application/Disbursements/)

```csharp
Task<ParticipantBalance> GetForApplicationAsync(int applicationId, CancellationToken ct);
```
Returns the five dimensions (see data-model). Read-only; used by the operator write surface and the Auditor/Admin read surface.

### DTOs / commands (Application/Disbursements/DisbursementDtos.cs)

- `RecordDisbursementCommand(int ApplicationId, DateOnly PaymentDate, decimal Amount, string BankTransactionReference, string? BankAccountReference)`
- `EditDisbursementCommand(int ApplicationId, int DisbursementId, DateOnly PaymentDate, decimal Amount, string BankTransactionReference, string? BankAccountReference)`
- `AttachDisbursementEvidenceCommand(int ApplicationId, int DisbursementId, EvidenceKind Kind, decimal Amount, string Currency, string DocumentReferenceNumber, DateOnly DocumentDate, Stream Content, string FileName, string ContentType, long FileSize)`
- `DisbursementListItem(int Id, DateOnly PaymentDate, decimal Amount, DisbursementState State, bool HasBankReceipt, bool HasInvoice, bool IsValidatable)`
- `DisbursementDetail(... + IReadOnlyList<ReconciliationDiscrepancy> Discrepancies, evidence summaries)`
- `DisbursementEvidenceDownload(Stream Content, string ContentType, string FileName)`

## HTTP endpoint surface — `DisbursementController`

`[Authorize(Roles = "Financial Operator,Admin,Auditor")]` · `[Route("Applications/{applicationId:int}/Disbursements")]`

| Verb | Route | Roles (effective) | Purpose |
|---|---|---|---|
| GET | `""` | FinOp, Admin, Auditor | List disbursements + five-dimension balance (Auditor/Admin: controls hidden — read-only) |
| GET | `"{disbursementId:int}"` | FinOp, Admin, Auditor | Detail: amounts, evidence, live discrepancy list |
| POST | `"Record"` | FinOp, Admin | Record a disbursement `[ValidateAntiForgeryToken]` |
| POST | `"{disbursementId:int}/Edit"` | FinOp, Admin | Edit pre-validation details |
| POST | `"{disbursementId:int}/Evidence"` | FinOp, Admin | Upload/replace bank receipt or invoice `[UploadSizeGuard(FileCategory.DisbursementEvidence)]` `[ValidateAntiForgeryToken]` |
| POST | `"{disbursementId:int}/Validate"` | FinOp, Admin | Validar (gated) `[ValidateAntiForgeryToken]` |
| POST | `"{disbursementId:int}/Cancel"` | FinOp, Admin | Cancel a pending disbursement `[ValidateAntiForgeryToken]` |
| GET | `"{disbursementId:int}/Evidence/{kind}/Download"` | FinOp, Admin, Auditor | Stream a stored document |

**Authorization gates (mirroring `FundsUsageEvidenceController` / `AuditController`):**
- Role refusal → **403** via the `[Authorize(Roles=...)]` attribute (applicant/participant refused).
- Read-only enforcement: write POSTs additionally require `User.IsInRole("Financial Operator") || User.IsInRole("Admin")`; an Auditor hitting a write POST → 403.
- Out-of-group / not-executed → flat **404** (`NotFound()`), no disclosure — `admin short-circuit || IReviewerScopeProvider + ApplicantSharesAnyGroupAsync`, AND `Application.State == AgreementExecuted`.
- Cross-application disbursement-id guard: `disbursement.ApplicationId == route applicationId` else 404.
- Upload path buffers the file, reads `EvidenceFileTypePolicy.HeadByteCount` bytes, calls `EvidenceFileTypePolicy.IsAllowed(...)` before delegating (magic-byte allow-list).

## AdminAudit event vocabulary (Domain/Entities/AdminAuditEvent.cs)

New constants + `DeriveTarget` prefix branch (`disbursement.*` → `TargetTypeDisbursement`, real id parsed from payload):

```
disbursement.recorded
disbursement.edited
disbursement.evidence_attached      // payload: kind
disbursement.evidence_replaced      // payload: kind
disbursement.validated
disbursement.cancelled
TargetTypeDisbursement = "disbursement"
```
Payload JSON carries `{ disbursementId, applicationId, before?, after? }` (before/after for edits/replacements; amounts/state for lifecycle).

## Role & DI wiring contracts

- Role `"Financial Operator"` added to: `IdentityConfiguration.roles[]`, `UserAdministrationService.AllowedRoles` + `SelectPrimaryRole` precedence, `AccountController.AssignRole` allow-list (dev seam). `NormalizeGroupIdsForRole` unchanged (keeps groups for non-Admin).
- Admin form: role added to Create/Edit `roles[]` + label maps; group selector **shown** (not in `IsGrouplessRole`; JS `isGroupless` excludes it).
- Sidebar `operativoEntries`: `new("disbursement-inbox", "Desembolsos", ..., new[] { "Financial Operator", "Admin" })`.
- DI: `services.AddScoped<IDisbursementService, DisbursementService>();` and `AddScoped<IParticipantBalanceProjection, ...>();` in `Infrastructure/DependencyInjection.cs`.
- Storage: `FileCategory.DisbursementEvidence` + `StorageCategoryOptions.DisbursementEvidence` (20 MiB, BackendStream) + container name in `AllContainerNames`.

## es-CR resource contract

`Web/Resources/DisbursementResources` (+ any Application-layer copy per the spec-034 cross-layer precedent if service produces user-facing reasons): refusal/validation strings — over-disbursement, missing evidence, has-discrepancy, non-CRC, out-of-window states, concurrency retry. All es-CR.
