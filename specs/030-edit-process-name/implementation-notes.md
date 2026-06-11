# Implementation Notes: Admin — Edit Process Name

> Technical context for HOW. The spec (`spec.md`) is the WHAT/WHY source of truth. These notes
> capture seams discovered during brainstorming so the plan/implementation can reuse existing
> patterns rather than rediscover them.

## Design Decisions

### Decision: Inline edit on the Details page (not a dedicated /Edit page)
- **Chosen**: Add an inline editable Name card/form to `Views/Admin/Processes/Details.cshtml`,
  matching the existing Fund-reassignment and stage-window inline-form pattern already on that
  page.
- **Rejected**: A separate `/Admin/Processes/{id}/Edit` page reached via an "Editar" button on
  the Index rows.
- **Rationale**: User chose inline. The Details page is already the editing surface for every
  other Process detail (Fund, stage windows, Plantilla, Groups, close) — a separate page would
  fragment the UX. Scope is a single field, so a full CRUD edit page is overkill.

### Decision: Rename allowed at any status (including Closed)
- **Chosen**: The Name affordance renders for both Active and Closed Processes; no
  `ProcessClosedException` guard on rename.
- **Rejected**: Hiding/blocking rename when Closed (which is how Fund/windows/Plantilla/Groups
  behave).
- **Rationale**: User explicitly decided renaming should be allowed at any time. This is the
  *simpler* option — the existing domain `Process.Rename()` already has **no** Closed guard, so
  no domain change is required. (Note this is an intentional inconsistency with the other
  mutations, recorded here so it is not "fixed" later by mistake.)

## Existing Seams to Reuse (verified during brainstorming)

| Concern | Existing artifact | File |
|---|---|---|
| Domain mutation | `Process.Rename(string)` — already exists, trims, no-ops on equal name, validates required + ≤120 | `src/FundingPlatform.Domain/Entities/Process.cs:81` |
| Name max length | `Process.MaxNameLength = 120` | `src/FundingPlatform.Domain/Entities/Process.cs:20` |
| Service style to mirror | `IProcessService.ReassignFundAsync` + `ReassignProcessFundCommand` | `src/FundingPlatform.Application/Processes/IProcessService.cs:25,50` |
| Service impl style | `ProcessService.ReassignFundAsync` (loads entity, mutates, audits, SaveChanges) | `src/FundingPlatform.Infrastructure/Services/ProcessService.cs` |
| Audit seam | `IAdminAuditEventWriter.WriteAsync(eventKind, actorUserId, payloadJson, ct)` | — |
| Audit constant convention | `process.created`, `process.fund_reassigned`, `process.closed`, `process.stage_window.overridden` | `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` |
| Uniqueness gate | `UX_Processes_Name` unique nonclustered index (no app-layer pre-check; relies on `DbUpdateException`) | `src/FundingPlatform.Database/Tables/dbo.Processes.sql:30` |
| Controller action to mirror | `AdminProcessesController.ChangeFund` (POST, antiforgery, try/catch → TempData flash, redirect to Details) | `src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs:86` |
| Duplicate-name copy | "Ya existe un proceso con ese nombre." (already used on Create) | `src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs:155` |
| Toast/flash pattern | `TempData["SuccessMessage"]` / `TempData["ErrorMessage"]` surfaced as toasts (spec 024) | — |

## Net new artifacts (anticipated — confirm in plan)

- `AdminAuditEvent.ProcessRenamed` constant → string `"process.renamed"` (+ `process.` prefix
  so existing `TargetTypeProcess` derivation picks it up automatically).
- `RenameProcessCommand(int ProcessId, string NewName)` record + `IProcessService.RenameAsync`
  + `ProcessService.RenameAsync` impl (load → `Process.Rename` → audit `ProcessRenamed` →
  `SaveChangesAsync`; let `DbUpdateException` bubble to the controller for the duplicate path).
- `AdminProcessesController` `Rename` POST action (route e.g. `{id:int}/Rename`), mirroring
  `ChangeFund`: antiforgery, `ArgumentException` → inline ModelState error, `DbUpdateException`
  → "Ya existe un proceso con ese nombre.", `KeyNotFoundException` → 404, success → TempData
  success + redirect to Details.
- Inline Name edit card in `Views/Admin/Processes/Details.cshtml` with `data-testid` hooks for
  E2E (e.g. `admin-process-rename-form`, `admin-process-rename-input`,
  `admin-process-rename-submit`).

## Testing notes
- Integration test (real DB, per CLAUDE.md — never mocks): rename happy path + duplicate
  rejection (exercises the unique index) + no-op same-name (no audit row).
- E2E (Playwright, full suite must be green before "delivered"): rename an Active Process,
  rename a Closed Process, duplicate-name rejection, empty-name rejection.
- Unit: `Process.Rename` boundary (≤120 / >120, whitespace trim, equal-name no-op) — some may
  already exist; extend as needed.
