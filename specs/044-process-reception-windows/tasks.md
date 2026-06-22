---
description: "Task list for 044-process-reception-windows implementation"
---

# Tasks: Fund Process Reception Windows + Applicant Timing UX

**Input**: Design documents from `specs/044-process-reception-windows/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: INCLUDED — Constitution Principle III (E2E non-negotiable) + the project's filtered-E2E delivery bar make tests required, not optional. Unit covers the pure evaluator/boundary (SC-002); Integration hits real SQL (per CLAUDE.md, never mocks); E2E covers each user story.

**Organization**: Tasks grouped by user story. Foundational phase (Phase 2) rips out the legacy Solicitud gate and lays the new ProcessEvent model — it blocks all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: US1–US5 maps to spec.md user stories

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Cross-cutting timezone abstraction every later phase reuses.

- [X] T001 [P] Add `IBusinessTimeZone` to `src/FundingPlatform.Application/Time/IBusinessTimeZone.cs` (`ToUtc`, `ToBusinessLocal`, `CurrentOffset`) and impl `src/FundingPlatform.Infrastructure/Time/BusinessTimeZone.cs` (resolves `TimeZoneInfo` from config key `Process:BusinessTimeZone`, default `America/Costa_Rica`, fixed −06:00 `TimeSpan` fallback if the zone is absent). Register in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: New domain model + schema, and removal of the legacy Solicitud duration gate. After this phase, submission has no timing restriction (no-window = open, FR-007/SC-005) — the new gate is layered on in US2.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T002 [P] Add `ProcessEventType` enum (`ReceptionWindow=0`, `Informational=1`, `Deadline=2`, `Milestone=3`) in `src/FundingPlatform.Domain/Enums/ProcessEventType.cs` and `ReceptionWindowState` enum (`Upcoming`/`OpenNow`/`Closed`) in `src/FundingPlatform.Domain/Enums/ReceptionWindowState.cs`.
- [X] T003 [P] Create `ProcessEvent` entity in `src/FundingPlatform.Domain/Entities/ProcessEvent.cs` — factory `CreateReceptionWindow(...)`, `Update(...)`, `Activate`/`Deactivate`, `ComputeState(nowUtc)`; invariants `EndUtc > StartUtc`, name trim/≤120, descriptions ≤500.
- [X] T004 [P] Create pure evaluator in `src/FundingPlatform.Domain/ReceptionWindows/ReceptionWindowEvaluation.cs` with `ReceptionWindowSnapshot`, `SubmissionAvailabilityStatus`, `ReceptionAvailability` (incl. `CanSubmit`/`CanCreateDraft`) and static `Evaluate(windows, nowUtc)` per data-model.md (empty→Unrestricted; inside→Open w/ latest-End; else NextWindow→BeforeFirst/Between; else AllWindowsClosed).
- [X] T005 [P] Add `ReceptionWindowClosedException` in `src/FundingPlatform.Domain/Exceptions/ReceptionWindowClosedException.cs` carrying `SubmissionAvailabilityStatus Status` + `DateTimeOffset? BoundaryUtc`.
- [X] T006 [P] Unit tests in `tests/FundingPlatform.Tests.Unit/Domain/ReceptionWindowEvaluationTests.cs` + `ProcessEventTests.cs`: empty, open, **boundary `now==Start`→Open / `now==End`→Closed (SC-002)**, before-first, between, all-closed, overlap (latest-End wins), inactive excluded; entity `EndUtc<=StartUtc` rejection.
- [X] T007 Create table `src/FundingPlatform.Database/Tables/dbo.ProcessEvents.sql` (columns per data-model.md, `CK_ProcessEvents_EndAfterStart`, FK→Processes NO ACTION, `IX_ProcessEvents_ProcessId` INCLUDE IsActive,EventType).
- [X] T008 Add `ProcessEventConfiguration` in `src/FundingPlatform.Infrastructure/Persistence/Configurations/ProcessEventConfiguration.cs` (`EventType` `HasConversion<byte>()` — **mandatory TINYINT gotcha**; lengths; `CreatedAt` default sql; `RowVersion`; FK to Process). Add `DbSet<ProcessEvent>` + `Process.Events` nav in `AppDbContext`/`Process.cs`.
- [X] T009 Remove `SolicitudWindowDays`: domain property + `OverrideStageWindow`/`OverrideForStage` Solicitud arms in `src/FundingPlatform.Domain/Entities/Process.cs`; EF map in `Persistence/Configurations/ProcessConfiguration.cs`; column in `Database/Tables/dbo.Processes.sql`; DTO field in `Application/Processes/Queries/IProcessQueryService.cs`; projection in `Infrastructure/Services/ProcessService.cs`.
- [X] T010 Add idempotent `src/FundingPlatform.Database/PostDeployment/07_DropSolicitudWindowDays.sql` (`COL_LENGTH` guard → `DROP COLUMN`) and delete the `Stage.Solicitud.WindowDays` row from `PostDeployment/SeedData.sql`.
- [X] T011 Remove the Solicitud submission gate: change `Application.Submit` signature to `Submit(int minQuotations)` (drop `currentStage/stageClosesAt/now` + the `StageWindowClosedException` throw) in `src/FundingPlatform.Domain/Entities/Application.cs`; delete `ResolveStageClosesAtAsync`/`ResolvePlatformDefaultAsync` and update the call site in `src/FundingPlatform.Infrastructure/Services/SubmitApplicationHandler.cs`.
- [X] T012 Remove the Solicitud throw + `stageClosesAt` resolution from `src/FundingPlatform.Infrastructure/Services/AutosaveFieldHandler.cs` (FR-015: existing-draft editing always allowed; keep `AutosaveConflictException`).
- [X] T013 Remove the Solicitud arm + projection from `src/FundingPlatform.Infrastructure/StageExpiry/StageExpiryEvaluator.cs` (keep Revisión/Facturación).
- [X] T014 [P] Rewrite the legacy Solicitud-window tests to the new model: drop the window assertions from `tests/FundingPlatform.Tests.Unit/Domain/ApplicationSubmitGuardTests.cs`, `tests/FundingPlatform.Tests.Integration/Applications/SubmitGuardTests.cs`, and `tests/FundingPlatform.Tests.Integration/Applications/AutosaveEndpointTests.cs` (autosave no longer 422 on a closed window; boundary assertion now lives in T006).

**Checkpoint**: Solution builds; submission/autosave are time-ungated; existing no-window submission tests (SC-005) green.

---

## Phase 3: User Story 1 — Admin configures reception windows (Priority: P1) 🎯 MVP

**Goal**: Admins CRUD reception windows on a Process with state badges and `end>start` validation.

**Independent Test**: On `/Admin/Processes/{id}` create two non-contiguous windows, edit, deactivate, delete, and reject an `end≤start` window — no applicant flow needed.

- [X] T015 [P] [US1] `IReceptionWindowService` + `Create/Update/SetActive/Delete` commands in `src/FundingPlatform.Application/Processes/ReceptionWindows/IReceptionWindowService.cs`.
- [X] T016 [P] [US1] `IReceptionWindowQuery` + `ReceptionWindowRow` DTO in `src/FundingPlatform.Application/Processes/ReceptionWindows/IReceptionWindowQuery.cs` (`GetForProcessAsync`, `GetAvailabilityForGroupAsync`, `GetAvailabilityForApplicationAsync`).
- [X] T017 [US1] `ReceptionWindowService` impl in `src/FundingPlatform.Infrastructure/Services/ReceptionWindowService.cs` (CRUD via `ProcessEvent` domain methods; two-SaveChanges audit; surfaces `ArgumentException` for validation); register in `DependencyInjection.cs`.
- [X] T018 [US1] `ReceptionWindowQuery` impl in `src/FundingPlatform.Infrastructure/Services/ReceptionWindowQuery.cs` (load active reception windows → `ReceptionWindowEvaluation.Evaluate`; `GetForProcessAsync` returns all w/ per-row `ComputeState`); register in `DependencyInjection.cs`.
- [X] T019 [US1] Add audit-kind constants `process.reception_window.{created,updated,activated,deactivated,deleted}` in `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` (route via existing `process.` prefix — no new target type in `AdminAuditEventWriter`).
- [X] T020 [P] [US1] es-CR `AdminReceptionWindowsResources` in `src/FundingPlatform.Web/Resources/AdminReceptionWindowsResources.cs` (card title, labels, state badges, validation, flash).
- [X] T021 [US1] Add 4 actions (`CreateReceptionWindow`/`UpdateReceptionWindow`/`SetReceptionWindowActive`/`DeleteReceptionWindow`) to `src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs` — `[ValidateAntiForgeryToken]`, `datetime-local`→UTC via `IBusinessTimeZone.ToUtc`, success `TempData` + redirect, validation re-renders `Details` via `BuildDetailsViewModelAsync`.
- [X] T022 [US1] Add "Ventanas de recepción" card (list + state badges + add/edit/deactivate/delete forms with toast/confirm) to `src/FundingPlatform.Web/Views/Admin/Processes/Details.cshtml`, rendered for Active **and** Closed; remove the **Solicitud** option + summary row from the stage-override card (Revisión/Facturación stay).
- [X] T023 [US1] Integration tests `tests/FundingPlatform.Tests.Integration/Processes/ReceptionWindowServiceTests.cs` (real SQL): create/update/setactive/delete; `end≤start` rejection; overlap allowed; audit rows written.
- [ ] T024 [US1] E2E `tests/FundingPlatform.Tests.E2E/ReceptionWindowAdminTests.cs`: create two non-contiguous windows, edit one, deactivate one, delete one, reject `end≤start`; assert state badges.

**Checkpoint**: Admins can fully configure windows; persisted + audited.

---

## Phase 4: User Story 2 — Submission gated by reception windows (Priority: P1)

**Goal**: Submission allowed only inside an active window (CR time); typed es-CR refusal otherwise; no-window = open.

**Independent Test**: Seed a window covering now-±/past/future; submit a complete application each time — allowed inside, 422-with-reason before/between/after, open when no windows.

- [X] T025 [P] [US2] Add `UserFacingErrorCode.ReceptionWindowClosed` in `src/FundingPlatform.Application/Errors/UserFacingErrorCode.cs` and map it (Detail verbatim) in the Web `IUserFacingErrorTranslator` impl.
- [X] T026 [US2] Inject `IReceptionWindowQuery` + `IStageExpiryClock` into `SubmitApplicationHandler`; before `application.Submit(minQuotations)`, evaluate `GetAvailabilityForApplicationAsync(_clock.UtcNow)` and throw `ReceptionWindowClosedException(status, boundary)` when `!CanSubmit`.
- [X] T027 [US2] Add a `ReceptionWindowClosedException` case → **422** + typed es-CR message in `src/FundingPlatform.Web/Filters/DomainExceptionFilter.cs` (alongside/replacing the Solicitud `StageWindowClosedException` case).
- [X] T028 [US2] Integration tests `tests/FundingPlatform.Tests.Integration/Applications/ReceptionWindowSubmissionTests.cs` (faked `IStageExpiryClock`): open→submit OK; before/between/all-closed→`ReceptionWindowClosedException` w/ correct status+boundary; no-window→OK; boundary `now==Start` OK / `now==End` blocked (SC-002); **non-retroactivity (FR-017): submit under an open window, then deactivate/delete that window → the already-submitted application is unchanged (state stays Submitted) and still readable.**
- [ ] T029 [US2] E2E `tests/FundingPlatform.Tests.E2E/ReceptionWindowSubmissionTests.cs`: window seeded around real `UtcNow` → submit succeeds; past-only window → submit blocked with es-CR reason (422 toast).

**Checkpoint**: Submission hard-gated; refusals explained; backward-compatible.

---

## Phase 5: User Story 3 — Applicant timing notices & countdown (Priority: P1)

**Goal**: Prominent open/upcoming/closed notice on create + draft-edit, with precise CR instant + countdown.

**Independent Test**: Render create/edit for windows in each state; verify the right mode, instant (es-CR `dd/MM/yyyy HH:mm`), and remaining-time when open.

- [X] T030 [P] [US3] `ReceptionWindowNoticeViewModel` in `src/FundingPlatform.Web/ViewModels/ReceptionWindowNoticeViewModel.cs` (state, boundary instant in CR local, remaining `TimeSpan`, applicant message).
- [X] T031 [P] [US3] `_ReceptionWindowNotice.cshtml` partial in `src/FundingPlatform.Web/Views/Shared/` — Open (close countdown), Upcoming (open instant + "puede preparar un borrador"), Closed, Unrestricted (renders nothing); pure render.
- [X] T032 [P] [US3] es-CR `ReceptionWindowResources` in `src/FundingPlatform.Web/Resources/ReceptionWindowResources.cs` (notice copy).
- [X] T033 [US3] Build the notice VM in `src/FundingPlatform.Web/Controllers/ApplicationController.cs` for Create + Edit (via `IReceptionWindowQuery` + `IBusinessTimeZone`); render it atop `Views/Application/Create.cshtml`; on `Views/Application/Edit.cshtml` **replace** the Solicitud `_StageCountdownBanner` (remove its build branch at the former `:759`); disabled-submit explanation when not open.
- [ ] T034 [US3] E2E `tests/FundingPlatform.Tests.E2E/ReceptionWindowNoticeTests.cs`: open (countdown shown) / upcoming (next-open instant + drafting note) / closed; assert es-CR datetime format and disabled-submit reason.

**Checkpoint**: Applicants see professional, accurate timing notices.

---

## Phase 6: User Story 4 — Draft creation guarded against dead-ends (Priority: P2)

**Goal**: Block starting a NEW draft when all windows are closed; existing-draft edit unaffected.

**Independent Test**: All-closed process → create refused with es-CR reason; existing draft still opens/edits; no-window/future-window → create allowed.

- [X] T035 [US4] In `ApplicationController.Create` POST, after group/company validation and before `CreateApplicationAsync`, call `GetAvailabilityForGroupAsync(_clock.UtcNow)`; when `!CanCreateDraft` add an es-CR `ModelState` error on `GroupId` and re-render (no guard on existing-draft edit).
- [ ] T036 [US4] E2E `tests/FundingPlatform.Tests.E2E/ReceptionWindowDraftGuardTests.cs`: all-closed process blocks new draft (es-CR reason) but existing draft still editable; no-window/upcoming process allows create.

**Checkpoint**: No dead-end drafts; existing drafts never trapped.

---

## Phase 7: User Story 5 — Future-proof event model (Priority: P3, schema-only)

**Goal**: Confirm the `ProcessEvent` shape admits future event types without reshape; reception stored with `ControlsSubmissionAvailability=true`.

**Independent Test**: Persist a reception window and a non-reception event type; both round-trip.

- [X] T037 [US5] Integration assertion in `tests/FundingPlatform.Tests.Integration/Processes/ProcessEventSchemaTests.cs`: reception window persists with `EventType=ReceptionWindow` + `ControlsSubmissionAvailability=true`; a `ProcessEventType.Informational` row round-trips (no behavior) — proving the schema accepts other types.

**Checkpoint**: US5 verified structurally.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T038 [P] Run `quickstart.md` walkthrough manually (admin config → applicant gating/notice → no-window regression).
- [ ] T039 Run delivery-gate suites: `Tests.Unit` (ReceptionWindow*), `Tests.Integration` (ReceptionWindow*/SubmitGuard/Autosave), filtered E2E (`ReceptionWindow*`) **plus** the no-window submission regression (Submit*/ApplicantCompanySelection) for SC-005. Confirm green.
- [ ] T040 [P] Update CLAUDE.md *Recent Changes* + decomposition/brainstorm status to shipped on merge; optional multi-agent deep review (`speckit-spex-deep-review-review`).

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)** → **Foundational (P2)** blocks everything → **US1–US5 (P3–P7)** → **Polish (P8)**.
- Within Foundational: T002–T005 [P]; T006 after T002–T005; T007/T008 after T003; T009–T013 are the legacy-removal set (T011 needs the `Submit` signature change reflected in T014 test rewrite); T014 [P] after T011–T013.

### User-story dependencies
- **US1** needs Foundational (entity/query/service infra). MVP.
- **US2** needs Foundational + `IReceptionWindowQuery` (T016/T018 from US1). Tests can seed windows via the service (T017) or direct insert.
- **US3** needs Foundational + `IReceptionWindowQuery` + `IBusinessTimeZone`.
- **US4** needs Foundational + `IReceptionWindowQuery` (`GetAvailabilityForGroupAsync`).
- **US5** needs only Foundational schema (T007/T008).

> US2–US4 share `IReceptionWindowQuery`; deliver US1's query/service (T016–T018) first, then US2/US3/US4 can proceed in parallel.

### Parallel opportunities
- Setup T001 ∥ start of Foundational reads.
- Foundational: T002, T003, T004, T005 in parallel; T014 parallel with US1 start.
- US1: T015, T016, T020 in parallel; US3: T030, T031, T032 in parallel.
- After T016–T018 land, US2 / US3 / US4 implementations can run in parallel (distinct files).

---

## Implementation Strategy

### MVP (Foundational + US1 + US2)
1. Phase 1 Setup → Phase 2 Foundational (build green; no-window submission regression passes).
2. US1 (admins can configure windows) → validate via T024.
3. US2 (gating + refusal) → validate via T028/T029. **This is the smallest shippable increment that delivers the client's core ask.**

### Incremental
US3 (notice) → US4 (create guard) → US5 (schema assertion) → Polish. Each is independently testable and additive.

## Notes
- [P] = different files, no incomplete dependency. [Story] = traceability.
- Real-SQL integration tests only (no mocks — CLAUDE.md). E2E seeds windows relative to real `UtcNow`; boundary-second is unit/integration with a faked clock.
- Commit after each task or logical group; checkpoints are safe stop/validate points.
