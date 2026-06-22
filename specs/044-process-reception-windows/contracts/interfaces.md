# Contracts: Fund Process Reception Windows (044)

Internal seams (this is an MVC monolith — "contracts" are the service interfaces, exception/error surfaces, routes, and view components added or changed).

## Application layer

### `IReceptionWindowService` (admin CRUD)
`FundingPlatform.Application/Processes/ReceptionWindows/IReceptionWindowService.cs` — mirrors `IProcessService`/`IFundService`.
```csharp
Task<int> CreateAsync(CreateReceptionWindowCommand cmd, string actorUserId, CancellationToken ct);
Task UpdateAsync(UpdateReceptionWindowCommand cmd, string actorUserId, CancellationToken ct);
Task SetActiveAsync(int windowId, bool isActive, string actorUserId, CancellationToken ct);
Task DeleteAsync(int windowId, string actorUserId, CancellationToken ct);

record CreateReceptionWindowCommand(int ProcessId, string Name, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, string? ApplicantFacingMessage, string? Description, int DisplayOrder);
record UpdateReceptionWindowCommand(int WindowId, string Name, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, string? ApplicantFacingMessage, string? Description, int DisplayOrder);
```
Validation (`EndUtc > StartUtc`, name) is enforced by the domain factory/`Update`; service surfaces `ArgumentException` for the controller to map to an es-CR `ModelState` error. Audit kinds `process.reception_window.{created,updated,activated,deactivated,deleted}` written via the two-SaveChanges discipline; routed through the existing `process.` prefix in `AdminAuditEventWriter` (no new target type).

### `IReceptionWindowQuery` (reads + availability)
`FundingPlatform.Application/Processes/ReceptionWindows/IReceptionWindowQuery.cs`
```csharp
// Admin card: all windows for a process with computed state badge.
Task<IReadOnlyList<ReceptionWindowRow> GetForProcessAsync(int processId, DateTimeOffset nowUtc, CancellationToken ct);
// Gating/notice: availability for the process owning a group (Application->Group->Process).
Task<ReceptionAvailability> GetAvailabilityForGroupAsync(int groupId, DateTimeOffset nowUtc, CancellationToken ct);
Task<ReceptionAvailability> GetAvailabilityForApplicationAsync(int applicationId, DateTimeOffset nowUtc, CancellationToken ct);

record ReceptionWindowRow(int Id, string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    string? ApplicantFacingMessage, string? Description, bool IsActive, int DisplayOrder,
    ReceptionWindowState State); // Upcoming | OpenNow | Closed
```
Impl loads **active** reception windows (`EventType=ReceptionWindow && IsActive`) and calls `ReceptionWindowEvaluation.Evaluate`. `GetForProcessAsync` returns all (active + inactive) for the admin card with per-row `ComputeState`.

### `IBusinessTimeZone`
`FundingPlatform.Application/Time/IBusinessTimeZone.cs` (Infrastructure impl reads `Process:BusinessTimeZone`, default `America/Costa_Rica`).
```csharp
DateTimeOffset ToUtc(DateTime businessLocal);     // admin datetime-local input → UTC
DateTimeOffset ToBusinessLocal(DateTimeOffset utc); // UTC → CR for display
TimeSpan CurrentOffset { get; }                   // -06:00 for CR
```
Gating never calls this (pure instant comparison); used by Web for input/display only.

### `UserFacingErrorCode.ReceptionWindowClosed` (new enum value)
`FundingPlatform.Application/Errors/UserFacingErrorCode.cs` — translated by the Web `IUserFacingErrorTranslator` with **Detail passed verbatim** (mirrors spec-043 `RegulatoryDataStale`), e.g. `"La recepción de solicitudes abre el 01/03/2026 a las 00:00."` / `"…ya cerró el 01/06/2026 a las 00:00."`

## Domain layer

- `ProcessEvent` entity + `ProcessEventType` enum + `ReceptionWindowState` enum (see data-model.md).
- `ReceptionWindowSnapshot`, `ReceptionAvailability`, `SubmissionAvailabilityStatus`, `ReceptionWindowEvaluation.Evaluate` (pure).
- `ReceptionWindowClosedException : Exception` — `Domain/Exceptions/ReceptionWindowClosedException.cs`, carries `SubmissionAvailabilityStatus Status` + `DateTimeOffset? BoundaryUtc` (next-open or last-closed instant).
- **Changed**: `Application.Submit(int minQuotations)` (drops `currentStage/stageClosesAt/now`). `StageWindowClosedException` retained in the codebase only if still used by Revisión/Facturación paths — confirm at impl; the Solicitud throw is removed.

## Web layer

### Routes
| Method | Route | Action |
|---|---|---|
| POST | `/Admin/Processes/{id}/ReceptionWindows` | `AdminProcessesController.CreateReceptionWindow` |
| POST | `/Admin/Processes/{id}/ReceptionWindows/{windowId}/Update` | `UpdateReceptionWindow` |
| POST | `/Admin/Processes/{id}/ReceptionWindows/{windowId}/SetActive` | `SetReceptionWindowActive` |
| POST | `/Admin/Processes/{id}/ReceptionWindows/{windowId}/Delete` | `DeleteReceptionWindow` |

All `[ValidateAntiForgeryToken]`, re-render `Details` via `BuildDetailsViewModelAsync` on validation error (spec-030 pattern); success → `TempData["SuccessMessage"]` + redirect to `Details`. `datetime-local` inputs parsed as CR local → UTC via `IBusinessTimeZone.ToUtc`.

### Submission/creation gating
- `SubmitApplicationHandler.SubmitAsync` — evaluate availability via `IReceptionWindowQuery.GetAvailabilityForApplicationAsync(_clock.UtcNow)`; if `!CanSubmit` throw `ReceptionWindowClosedException`. `DomainExceptionFilter` maps it → **422** + es-CR (alongside the existing `StageWindowClosedException` case which remains for any non-Solicitud usage, or is removed if unused).
- `AutosaveFieldHandler` — **remove** the Solicitud `StageWindowClosedException` throw (FR-015).
- `ApplicationController.Create` POST — after group/company validation, `GetAvailabilityForGroupAsync`; if `!CanCreateDraft` add es-CR `ModelState` error on `GroupId` and re-render.

### Views / components
- `_ReceptionWindowNotice.cshtml` + `ReceptionWindowNoticeViewModel` — rendered atop `Create.cshtml` and `Edit.cshtml`; **replaces** the Solicitud `_StageCountdownBanner` on `Edit.cshtml` (`ApplicationController.cs:759` Solicitud branch removed). States: Open (close countdown), Upcoming (open instant + drafting-allowed note), Closed, Unrestricted (renders nothing). Pure-render ViewModel (server-computed remaining `TimeSpan`).
- "Ventanas de recepción" card on `Views/Admin/Processes/Details.cshtml` — list with state badges + add/edit/deactivate/delete (toast + confirm-dialog per spec 024). Rendered for Active and Closed processes.
- Admin stage-override card loses its **Solicitud** option (Revisión/Facturación remain).

### es-CR resources
- `AdminReceptionWindowsResources` (admin card copy, validation, audit-free strings) and `ReceptionWindowResources` (applicant notice copy: open/upcoming/closed, "puede preparar un borrador") — static-constant classes per the `AdminFundsResources` pattern.

## Backward compatibility
- Process with zero `ProcessEvents` → `Unrestricted` → submit + create allowed (FR-007, SC-005): existing submission tests pass unchanged.
- Already-submitted applications unaffected by later window edits (FR-017; gating is point-in-time at submit).
