# Implementation Plan: Fund Process Reception Windows + Applicant Timing UX

**Branch**: `044-process-reception-windows` | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/044-process-reception-windows/spec.md`

## Summary

Replace the per-application Solicitud duration submission gate with admin-configured, absolute-date **reception windows** on a fund Process, stored as general `ProcessEvents`. Submission and new-draft creation are gated by "now ∈ an active reception window" (pure UTC instant comparison); Costa Rica timezone matters only at admin input and display. Every refusal returns a typed es-CR reason, and applicants get a server-rendered countdown/notice. The orphaned `SolicitudWindowDays` column and its only two consumers (submit gate + Solicitud StageExpiry arm, plus the autosave gate) are removed; Revisión/Facturación stage timing is untouched. See [research.md](./research.md) for the nine design decisions and [data-model.md](./data-model.md)/[contracts/interfaces.md](./contracts/interfaces.md) for the shapes.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), EF Core 10
**Primary Dependencies**: ASP.NET MVC, ASP.NET Identity, .NET Aspire, Syncfusion (unaffected). **No new managed dependencies.**
**Storage**: SQL Server via dacpac (schema source of truth); new `dbo.ProcessEvents` table; `dbo.Processes.SolicitudWindowDays` dropped
**Testing**: NUnit (Unit + Integration against real SQL) + Playwright E2E via `AspireFixture`
**Target Platform**: Linux container (Aspire-orchestrated)
**Project Type**: Server-rendered MVC monolith, Clean Architecture (Domain/Application/Infrastructure/Web)
**Performance Goals**: Gating adds one indexed query (active windows by `ProcessId`) per submit/create/notice render — negligible
**Constraints**: es-CR default culture; no CDN/new deps; dacpac-only schema; backward-compatible (no-window = open); time via existing `IStageExpiryClock`
**Scale/Scope**: 1 new table, 1 new aggregate + pure evaluator, 1 admin card, 1 applicant notice, removal of one legacy gate across submit/autosave/StageExpiry

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. Constitution v1.1.0.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | ✅ Domain: `ProcessEvent` entity, enums, pure `ReceptionWindowEvaluation`, exception. Application: `IReceptionWindowService`/`IReceptionWindowQuery`/`IBusinessTimeZone` interfaces + commands + `UserFacingErrorCode`. Infrastructure: EF impls, configuration, audit. Web: controller actions, views, components, resources. Dependencies point inward; gating evaluation is a pure domain function fed snapshots (no EF in Domain). |
| **II. Rich Domain Model** | ✅ `ProcessEvent` validates its own invariants (`EndUtc > StartUtc`, name) in factory/`Update`; state transitions (`Activate`/`Deactivate`) are entity methods; window-state computation is on the entity/pure evaluator, not the controller. Cross-aggregate enforcement (load sibling windows, throw) sits in the handler exactly as the prior `stageClosesAt` resolution did. |
| **III. E2E Testing (NON-NEGOTIABLE)** | ✅ Each user story gets Playwright coverage (admin CRUD US1; submit gating US2; notice states US3; draft-creation guard US4). Regression filter proves SC-005 (no-window submission unchanged). Boundary second (SC-002) is unit/integration with faked clock per research D2. |
| **IV. Schema-First DB** | ✅ New table + column drop via dacpac `.sql` + idempotent PostDeployment script (`07_DropSolicitudWindowDays.sql`); no EF migrations. EF used for access only. |
| **V. Spec-Driven Development** | ✅ spec.md → this plan → tasks (next) → implementation. User stories independently testable and prioritized. |
| **VI. Simplicity / YAGNI** | ✅ Non-reception event types are schema-only (no speculative behavior); no global process dates; no per-fund timezone; gating reduced to instant comparison (no timezone machinery in the hot path); reuse of `IStageExpiryClock`, FundService/ProcessService/Details-card patterns. One justified abstraction: `IBusinessTimeZone` (a current need — admin input/display in CR), not speculative. |

**Quality gates**: validation errors collected and shown (es-CR); optimistic concurrency via `RowVersion` on `ProcessEvent`; authorization preserved (admin-only CRUD; applicant gating composes with existing ownership/role checks; refusal reasons never disclose cross-tenant data).

**Result**: PASS (pre-Phase-0 and post-Phase-1). No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/044-process-reception-windows/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 — 9 decisions
├── data-model.md        # Phase 1 — entity, enums, pure evaluator, schema, EF
├── contracts/
│   └── interfaces.md     # Phase 1 — service/query/error/route/component contracts
├── quickstart.md        # Phase 1 — run + test walkthrough
├── review_brief.md
├── REVIEW-SPEC.md
└── checklists/requirements.md
```

### Source code (affected paths)

```text
src/FundingPlatform.Domain/
├── Entities/ProcessEvent.cs                         # NEW aggregate
├── Entities/Process.cs                              # MOD: drop SolicitudWindowDays + arm; add Events nav
├── Enums/ProcessEventType.cs                        # NEW
├── Enums/ReceptionWindowState.cs                    # NEW
├── ReceptionWindows/ReceptionWindowEvaluation.cs    # NEW pure evaluator + result types
└── Exceptions/ReceptionWindowClosedException.cs     # NEW

src/FundingPlatform.Application/
├── Processes/ReceptionWindows/IReceptionWindowService.cs   # NEW (+ commands)
├── Processes/ReceptionWindows/IReceptionWindowQuery.cs     # NEW (+ row DTO)
├── Time/IBusinessTimeZone.cs                               # NEW
├── Errors/UserFacingErrorCode.cs                           # MOD: + ReceptionWindowClosed
└── Processes/Queries/IProcessQueryService.cs               # MOD: drop SolicitudWindowDays DTO field

src/FundingPlatform.Infrastructure/
├── Services/ReceptionWindowService.cs                      # NEW (CRUD + audit)
├── Services/ReceptionWindowQuery.cs                        # NEW (reads + Evaluate)
├── Time/BusinessTimeZone.cs                                # NEW (config-driven TimeZoneInfo)
├── Persistence/Configurations/ProcessEventConfiguration.cs # NEW
├── Persistence/Configurations/ProcessConfiguration.cs      # MOD: drop SolicitudWindowDays map
├── Services/SubmitApplicationHandler.cs                    # MOD: drop Solicitud resolve; add reception gate
├── Services/AutosaveFieldHandler.cs                        # MOD: remove Solicitud throw (FR-015)
├── StageExpiry/StageExpiryEvaluator.cs                     # MOD: drop Solicitud arm/projection
├── Services/ProcessService.cs                              # MOD: drop SolicitudWindowDays projection
└── DependencyInjection.cs                                  # MOD: register new services + IBusinessTimeZone

src/FundingPlatform.Database/
├── Tables/dbo.ProcessEvents.sql                            # NEW
├── Tables/dbo.Processes.sql                                # MOD: remove column
└── PostDeployment/07_DropSolicitudWindowDays.sql           # NEW (idempotent) + SeedData.sql config delete

src/FundingPlatform.Web/
├── Controllers/Admin/AdminProcessesController.cs           # MOD: + 4 reception-window actions
├── Controllers/ApplicationController.cs                    # MOD: create guard; replace Solicitud banner
├── Filters/DomainExceptionFilter.cs                        # MOD: + ReceptionWindowClosedException → 422
├── ViewModels/ReceptionWindowNoticeViewModel.cs            # NEW
├── Views/Admin/Processes/Details.cshtml                    # MOD: + windows card; drop Solicitud override option
├── Views/Application/Create.cshtml                         # MOD: + notice
├── Views/Application/Edit.cshtml                           # MOD: replace Solicitud banner with notice
├── Views/Shared/_ReceptionWindowNotice.cshtml              # NEW
├── Resources/AdminReceptionWindowsResources.cs             # NEW
└── Resources/ReceptionWindowResources.cs                   # NEW

tests/  (Unit: evaluator + entity + Process; Integration: submit/autosave/CRUD vs real SQL; E2E: US1–US4 + regression)
```

**Structure Decision**: Standard four-layer Clean Architecture monolith; all paths above are real directories in this repo. No new project.

## Phase boundaries

- **Phase 0 (research.md)**: complete — 9 decisions, all unknowns resolved.
- **Phase 1 (data-model.md, contracts/, quickstart.md)**: complete — entity/enum/evaluator/schema/EF, service+query+error+route+component contracts, run/test walkthrough.
- **Phase 2 (tasks.md)**: produced by `/speckit-tasks` — NOT part of this command.

## Complexity Tracking

No constitution violations. Table intentionally empty.
