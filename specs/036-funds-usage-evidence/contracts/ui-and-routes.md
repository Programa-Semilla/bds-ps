# Contract: UI, Routes & Status Codes

Controller: `FundsUsageEvidenceController`, attribute `[Authorize(Roles = "Reviewer,Admin")]`,
route prefix `[Route("Applications/{applicationId:int}/Evidence")]` (mirrors `FundingAgreementController`).

Every action resolves reviewer scope and gates the application:
```csharp
var scope = await _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);
if (!scope.IsAdmin && !await _appRepo.ApplicantSharesAnyGroupAsync(applicationId, scope.GroupIds, ct))
    return NotFound();   // no disclosure (FR-002)
```
Out-of-scope, non-existent application, or application not in `AgreementExecuted` for a mutating action →
`NotFound()` (the stage is simply unavailable; FR-001/FR-012). Applicants never reach these actions (role gate).

## Routes

| Verb | Route | Action | Purpose | Success | Failure |
|---|---|---|---|---|---|
| GET | `/Applications/{id}/Evidence` | `Index` | Stage view: evidence list + upload form (or empty state). Shown only when `State == AgreementExecuted`; otherwise `NotFound()`. | 200 + view | 404 |
| POST | `/Applications/{id}/Evidence/Upload` | `Upload` | Upload one file (+ optional note). `[UploadSizeGuard(FileCategory.FundsUsageEvidence)]`, antiforgery. | 302 → `Index` + success toast | 413 (size), 422/redisplay (type/note), 404 (scope/state) |
| POST | `/Applications/{id}/Evidence/{evidenceId:int}/Note` | `EditNote` | Set/clear/change the ≤250-char note. Antiforgery. | 302 → `Index` + toast | 422 (note > 250), 404 |
| POST | `/Applications/{id}/Evidence/{evidenceId:int}/Delete` | `Delete` | Delete item (after confirm dialog). Antiforgery. | 302 → `Index` + toast | 404 (already gone → harmless) |
| GET | `/Applications/{id}/Evidence/{evidenceId:int}/Download` | `Download` | Stream the original file (BackendStream). | 200 + file | 404 |

Notes:
- Multi-file: the upload form may submit several files; the controller loops, creating one item per file
  (or the form posts one file at a time — pin during implementation; FR-003 only requires multiple items
  to accumulate). Size guard applies per file via the resource filter.
- The size-cap rejection reuses `UploadSizeGuardFilter.RejectionMessage` (es-CR, HTTP 413). On a normal
  browser POST the redirect surfaces the message as an error toast (spec 024).
- Delete uses the spec-024 confirm dialog (`data-confirm` / confirm modal); no native `confirm()`.

## View contract

`Views/FundsUsageEvidence/Index.cshtml`:
- Stage heading **"Evidencia de uso de fondos"** (es-CR).
- Upload form: file input (accept hint for allowed types) + optional note textarea (maxlength 250 + live counter).
- Evidence list rendered via `_EvidenceRow.cshtml`, each row showing: file name (download link), note (inline,
  with an edit affordance), uploaded-by (display name), uploaded-at (es-CR date), and a delete button (confirm).
- Empty state: es-CR message when the list is empty (FR-011).
- `data-testid` hooks: `evidence-stage`, `evidence-upload-form`, `evidence-file-input`, `evidence-note-input`,
  `evidence-row`, `evidence-download`, `evidence-note-edit`, `evidence-delete`, `evidence-empty`.

Stage entry point: a conditional "Evidencia de uso de fondos" link/card on the per-application reviewer
surface (funding-agreement detail/panel area), rendered only when `State == AgreementExecuted` (research D7),
linking to `Index`.
