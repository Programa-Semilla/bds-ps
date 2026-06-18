---
description: "Task list for Auditor Workflow Stage (spec 040, feedback-3 slice C)"
---

# Tasks: Auditor Workflow Stage

**Input**: Design documents from `specs/040-auditor-workflow-stage/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D14), data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: Included — Constitution Principle III makes E2E **non-negotiable** and integration tests must hit a real DB. Each user story carries integration + E2E tasks; domain logic carries unit tests.

**Organization**: By user story (US1–US4). Delivery bar (CLAUDE.md) = the **filtered E2E for the affected classes** green, not the full suite.

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]** = parallelizable (different files, no incomplete-task dependency)
- **[USn]** = user-story phase tasks only

## Path conventions

Clean-Architecture web app: `src/FundingPlatform.{Domain,Application,Infrastructure,Web}`, `src/FundingPlatform.Database`, `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

---

## Phase 1: Setup

**Purpose**: Baseline before changes.

- [X] T001 Confirm `dotnet build FundingPlatform.slnx` is green and record which `FundingAgreement` / `Signing` / `GenerateAgreementQueue` E2E classes currently pass (these are rewired in Phase 7); branch `040-auditor-workflow-stage` already exists.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared domain, schema, and persistence every story depends on (states, checklist model, transitions, dacpac, EF). **No user story can begin until this phase is complete.**

### Domain — enums & entities
- [X] T002 [P] Add `PendingAudit = 7`, `ReturnedFromAudit = 8` to `src/FundingPlatform.Domain/Enums/ApplicationState.cs`.
- [X] T003 [P] Create `ChecklistStage` enum (`Reviewer=1, Auditor=2, Both=3`) in `src/FundingPlatform.Domain/Enums/ChecklistStage.cs`.
- [X] T004 [P] Create `ChecklistResponseStatus` enum (`Checked=1, NotCompliant=2`) in `src/FundingPlatform.Domain/Enums/ChecklistResponseStatus.cs`.
- [X] T005 [P] Create `ChecklistTemplate` aggregate (name/description/AppliesToStage/IsActive/CreatedAt/By/RowVersion + `AddItem`/`ClearItems`/`Update`/`Activate`/`Deactivate`) in `src/FundingPlatform.Domain/Entities/ChecklistTemplate.cs` (mirror `Category.cs`).
- [X] T006 [P] Create `ChecklistTemplateItem` child (Text/DisplayOrder/IsRequired/IsActive + FK) in `src/FundingPlatform.Domain/Entities/ChecklistTemplateItem.cs` (mirror `CategoryField.cs`).
- [X] T007 [P] Create `ApplicationChecklistResponse` entity (ApplicationId, Stage, ChecklistTemplateItemId, **ItemTextSnapshot**, Status, NonComplianceReason?, CompletedByUserId, CompletedAtUtc, RowVersion) in `src/FundingPlatform.Domain/Entities/ApplicationChecklistResponse.cs`.
- [X] T008 Add gated transitions `SendToAudit`/`ReturnFromAudit`/`ResendToAudit`/`ReleaseForSignature`, the `CanAuditorGenerateFundingAgreement(out errors)` gate, and change `CanUserGenerateFundingAgreement` to `isAdmin || isAuditor` (reviewer removed) in `src/FundingPlatform.Domain/Entities/Application.cs`; each transition appends a `VersionHistory` entry (per data-model §4 / research D3).
- [X] T009 Add `AuditorConfirmedAtUtc`/`AuditorConfirmedByUserId` + `ConfirmByAuditor(userId)` to `src/FundingPlatform.Domain/Entities/FundingAgreement.cs`; make `Replace()` clear both (regenerate invalidates confirm).
- [X] T010 [P] Add `IChecklistTemplateRepository` to `src/FundingPlatform.Domain/Interfaces/IChecklistTemplateRepository.cs`.

### Database (dacpac)
- [X] T011 [P] Create `src/FundingPlatform.Database/Tables/dbo.ChecklistTemplates.sql` (IDENTITY PK, RowVersion; mirror `dbo.FundsUsageEvidence.sql`).
- [X] T012 [P] Create `src/FundingPlatform.Database/Tables/dbo.ChecklistTemplateItems.sql` (FK→ChecklistTemplates, NC index, DisplayOrder).
- [X] T013 [P] Create `src/FundingPlatform.Database/Tables/dbo.ApplicationChecklistResponses.sql` (FK→Applications + ChecklistTemplateItems both **NO ACTION**, RowVersion, NC indexes).
- [X] T014 Add `AuditorConfirmedAtUtc DATETIME2 NULL`, `AuditorConfirmedByUserId NVARCHAR(450) NULL` to `src/FundingPlatform.Database/Tables/dbo.FundingAgreements.sql`.
- [X] T015 Create `src/FundingPlatform.Database/PostDeployment/07_SeedChecklistTemplates.sql` (idempotent default **Both** template + es-CR items via `SCOPE_IDENTITY()`); register in `PostDeployment/SeedData.sql` (`:r .\07_...`) and `FundingPlatform.Database.sqlproj` (`<Build Remove>` + `<None Include>`).

### Infrastructure — EF & persistence
- [X] T016 [P] Add `ChecklistTemplateConfiguration` + `ChecklistTemplateItemConfiguration` in `src/FundingPlatform.Infrastructure/Persistence/Configurations/` (mirror `CategoryConfiguration`; aggregate cascade, RowVersion).
- [X] T017 [P] Add `ApplicationChecklistResponseConfiguration` in `src/FundingPlatform.Infrastructure/Persistence/Configurations/` (FK NO ACTION/Restrict, lengths, RowVersion).
- [X] T018 Map `AuditorConfirmedAtUtc`/`AuditorConfirmedByUserId` in `FundingAgreementConfiguration`.
- [X] T019 Register the three new DbSets on `AppDbContext`, implement `ChecklistTemplateRepository` in `src/FundingPlatform.Infrastructure/Persistence/Repositories/`, and wire DI in `Application`/`Infrastructure` `DependencyInjection`.

### Foundational tests
- [X] T020 [P] Unit tests in `tests/FundingPlatform.Tests.Unit/`: `Application` transition guards (each gate's allowed/blocked states), `CanAuditorGenerateFundingAgreement`, `FundingAgreement.ConfirmByAuditor` + clear-on-`Replace`.

**Checkpoint**: schema, domain, and persistence ready — user stories can begin.

---

## Phase 3: User Story 1 — Auditor takes an application through audit to signature (Priority: P1) 🎯 MVP

**Goal**: An auditor opens their (group-scoped) inbox, reviews an application, completes the audit checklist, approves, generates + confirms the PDF, and releases it for signature; the applicant is notified.

**Independent Test**: Seed a `PendingAudit` app in the auditor's group; as `auditor@`, audit→approve→generate→confirm→release; assert app returns to `ResponseFinalized` with an agreement and the applicant gets the "ready to sign" email; an out-of-group auditor sees neither the inbox row nor the detail page (403).

- [X] T021 [US1] Add `SeedPendingAuditApplicationAsync(appId, …)` to `tests/FundingPlatform.Tests.E2E/Fixtures/FundingAgreementSeeder.cs` (sets `State=7`, marks reviewer checklist complete).
- [X] T022 [P] [US1] Add `IAuditorQueueProjection` + `AuditInboxRowDto` in `src/FundingPlatform.Application/Audit/`.
- [X] T023 [US1] Implement `AuditorQueueProjection` (Infrastructure) querying `PendingAudit` via `IApplicationRepository.GetByStateForReviewerAsync` with the auditor's `ReviewerScopeHint` (group-scoped; admin short-circuit); excludes `ReturnedFromAudit`.
- [X] T024 [P] [US1] Add `IAuditWorkflowService` + DTOs (`AuditChecklistView`, `AuditMark`, `Result`) in `src/FundingPlatform.Application/Audit/`.
- [X] T025 [US1] Implement `AuditWorkflowService` in `src/FundingPlatform.Infrastructure/Services/`: `GetAuditChecklistAsync`, `SaveAuditChecklistAsync` (compliant/non-compliant + reason snapshots), `ApproveForAgreementAsync`, `ConfirmPdfAsync`, `ReleaseForSignatureAsync` (calls `Application.ReleaseForSignature`).
- [X] T026 [US1] Re-point `AgreementGeneratedApplicant`: remove the enqueue in `FundingAgreementService.PersistGenerationAsync`; enqueue it in `ReleaseForSignatureAsync` anchored on the release `VersionHistory` row (research D10).
- [X] T027 [US1] Re-gate PDF generation: `FundingAgreementController.Generate` (or the auditor `/Audit/{id}/Generate` path) requires Auditor||Admin + `State==PendingAudit` + audit-approved (`CanAuditorGenerateFundingAgreement`); remove the reviewer authorization branch.
- [X] T028 [US1] Create `src/FundingPlatform.Web/Controllers/AuditController.cs` `[Authorize(Roles="Auditor,Admin")]`: `GET /Audit` (group-scoped inbox), `GET /Audit/{id}` (group-overlap `Forbid()` 403 via `ApplicantSharesAnyGroupAsync`; reuse `ReviewService` projection read), `POST Checklist/Approve/Generate/Confirm/Release`, `GET Download`.
- [X] T029 [P] [US1] Create `src/FundingPlatform.Web/Views/Audit/Index.cshtml` (inbox), `Detail.cshtml` (reuse review partials incl. `_SupplierComplianceBadge`), `_AuditChecklist.cshtml` (es-CR; PDF-correct confirm; release/return buttons).
- [X] T030 [US1] Show the spec-016 group selector for the **Auditor** role on the admin user-edit form (`AdminUsersController` + view) so auditors can be assigned to groups (FR-017).
- [X] T031 [US1] Add an "Auditoría" inbox sidebar entry for the Auditor role in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`.
- [X] T032 [P] [US1] Integration test (real DB) in `tests/FundingPlatform.Tests.Integration/`: `AuditWorkflowService` approve→generate→confirm→release path + `AuditorQueueProjection` group-scoping (in-group sees, out-of-group does not).
- [X] T033 [US1] E2E `AuditorWorkflowTests` in `tests/FundingPlatform.Tests.E2E/`: golden path (seed→inbox→audit→approve→generate→confirm→release→applicant "ready to sign" captured in smtp4dev) + out-of-group auditor negative (empty inbox + 403 detail).

**Checkpoint**: an auditor can fully audit and release a seeded application; group-scope enforced.

---

## Phase 4: User Story 2 — Reviewer completes the checklist and sends to audit (Priority: P1)

**Goal**: At `ResponseFinalized` (no open appeal, no agreement yet), the reviewer completes the reviewer checklist and sends to audit; the direct "Generate agreement" action is gone; group-scoped auditors are notified.

**Independent Test**: As `reviewer@`, open a `ResponseFinalized` app; confirm no "Generate agreement"; "Send to audit" disabled until all required items checked; send → app in `PendingAudit` and in-group auditors receive the `SentToAuditAuditor` email.

- [X] T034 [US2] Render the **reviewer checklist** on `Review.cshtml` when `State==ResponseFinalized && no agreement` (extend `ReviewController` detail + `ReviewService` projection to supply the active Reviewer-stage template items).
- [X] T035 [US2] Add `POST /Review/{id}/SendToAudit` (gate: all required reviewer items checked → `Application.SendToAudit`, snapshot responses) and **remove** the reviewer/admin "Generate agreement" action from the reviewer surface.
- [X] T036 [US2] Add `SentToAuditAuditor = 21` (FR-018): enum + `ToStorageString`/`FromStorageString`; new **Auditor** `RecipientBucket`; resolver query (role `AUDITOR` ∩ applicant stage groups, exclude actor/applicant); `NotificationTemplateBindings` (CTA `/Audit/{id}`); es-CR `Views/Emails/SentToAuditAuditor(.text).cshtml`; enqueue at `SendToAudit`/`ResendToAudit`.
- [X] T037 [P] [US2] Integration test (real DB): send-to-audit transitions to `PendingAudit`, snapshots reviewer responses, and resolves `SentToAuditAuditor` recipients to in-group auditors only.
- [X] T038 [US2] E2E `ReviewerSendToAuditTests`: checklist gating (disabled until all checked), transition to `PendingAudit`, "Generate agreement" absent, auditor email captured.

**Checkpoint**: US1 + US2 work — reviewer hands off, auditor audits.

---

## Phase 5: User Story 3 — Auditor returns a non-compliant application to the reviewer (Priority: P2)

**Goal**: A non-compliant audit returns the app to the reviewer (`ReturnedFromAudit`) with per-item reasons + email; the reviewer sees reasons, reworks, re-completes the checklist, and re-sends (loop). Applicant never contacted.

**Independent Test**: As `auditor@`, mark ≥1 item non-compliant with a reason → "Return"; assert `ReturnedFromAudit`, reviewer email with reasons, applicant nothing; as `reviewer@`, see reasons → re-complete → re-send → `PendingAudit`.

- [X] T039 [US3] Add `ReturnToReviewerAsync` to `AuditWorkflowService` (requires ≥1 `NotCompliant` with reason → `Application.ReturnFromAudit`); ensure "Approve" is unavailable when any item is non-compliant.
- [X] T040 [US3] Add `ReturnedToReviewerFromAudit = 20`: enum + storage strings; `NotificationTemplateBindings` (CTA `/Review/{id}`); recipient rules (Reviewer bucket via applicant groups + Admin; exclude actor/applicant); es-CR `Views/Emails/ReturnedToReviewerFromAudit(.text).cshtml`; enqueue at the return transition.
- [X] T041 [US3] On `Review.cshtml` for `State==ReturnedFromAudit`, show the auditor's non-compliance reasons + a reviewer re-complete checklist + `POST /Review/{id}` re-send (→ `Application.ResendToAudit`), re-enqueuing `SentToAuditAuditor`.
- [X] T042 [US3] Wire `POST /Audit/{id}/Return` in `AuditController` + ensure `_AuditChecklist` captures per-item non-compliance reasons.
- [X] T043 [P] [US3] Integration test (real DB): return → `ReturnedFromAudit` + `ReturnedToReviewerFromAudit` reviewer recipients (applicant excluded); re-send → `PendingAudit`.
- [X] T044 [US3] E2E `AuditReturnTests`: return with reasons (reviewer email captured, applicant none), reviewer rework + re-send, `PendingAudit ⇄ ReturnedFromAudit` loop.

**Checkpoint**: full audit loop (approve and return) works.

---

## Phase 6: User Story 4 — Administrator manages checklist templates (Priority: P2)

**Goal**: Admin manages per-stage checklist templates (text items, ordering, required, active, `AppliesToStage`); one active per stage; editing items preserves recorded responses.

**Independent Test**: As `demo-admin@`, create a template, set stage + ordered items, activate; reviewer/auditor gates use its active items; edit an item's text → existing `ApplicationChecklistResponse` rows unchanged.

- [X] T045 [P] [US4] Add `IChecklistTemplateService` + commands/DTOs (`CreateChecklistTemplateCommand`, `EditChecklistTemplateCommand`, rows, `ActiveChecklist`, `GetActiveForStageAsync`) in `src/FundingPlatform.Application/Checklists/`.
- [X] T046 [US4] Implement `ChecklistTemplateService` in `src/FundingPlatform.Infrastructure/Services/` (mirror `FundService`): list/get/create/edit(full-replace items)/activate/deactivate; enforce **one active per effective stage**; `GetActiveForStageAsync` (stage-specific beats `Both`); write `checklist.*` audit; two-SaveChanges pattern.
- [X] T047 [P] [US4] Add `checklist.create/edit/activate/deactivate` constants + `TargetTypeChecklist` to `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` and a `checklist.` branch in `AdminAuditEventWriter.DeriveTarget` (mirror `company.*`).
- [X] T048 [US4] Add `Checklists`/`CreateChecklist`/`EditChecklist` GET+POST actions to `AdminController` + `ChecklistAdminViewModels` (list/create/edit + ordered-item VMs) in `src/FundingPlatform.Web/ViewModels/Admin/`.
- [X] T049 [P] [US4] Create `Views/Admin/Checklists.cshtml`, `CreateChecklist.cshtml`, `EditChecklist.cshtml`, `_ChecklistItemsEditor.cshtml`, `_ChecklistItemsScript.cshtml` (mirror `_CategoryFieldsEditor`/`Script`; es-CR; `data-testid` hooks).
- [X] T050 [US4] Add a "Plantillas de checklist" admin sidebar entry to `_Layout.cshtml` `procesoEntries` (+ optional dashboard capability card in `AdminDashboardProjection`).
- [X] T051 [P] [US4] Integration test (real DB): create/edit/activate; one-active-per-stage enforced; editing items leaves existing `ApplicationChecklistResponse` snapshots unchanged (FR-003).
- [X] T052 [US4] E2E `ChecklistTemplateAdminTests`: create template (stage + ordered items + required), activate, gates use it; edit preserves recorded responses.

**Checkpoint**: all four stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Rewire the known cross-cutting E2E ripple (reviewer-generates → auditor-generates) and final validation.

- [ ] T053 Rewire `FundingAgreementSeeder` (`tests/.../Fixtures`): reposition `SeedGeneratedAgreementAsync` to seed a **released** post-audit agreement at `ResponseFinalized`; keep `SeedExecutedAgreementAsync` for downstream-signing tests.
- [ ] T054 Rewire `FundingAgreementTests` (admin/reviewer generate → auditor generate; reviewer can no longer generate directly).
- [ ] T055 Rewire `GenerateAgreementQueueTests` (reviewer "ready to generate" tab → auditor inbox semantics / removal).
- [ ] T056 Rewire `SigningWayfindingTests` seeding to route through the audit stage before signing.
- [ ] T057 [P] es-CR copy pass across all new surfaces + the two new emails (no English literals; mirror existing resource conventions).
- [ ] T058 [P] Run `quickstart.md` walkthrough end-to-end on a local AppHost run.
- [ ] T059 Run the **filtered E2E delivery gate** green: `AuditorWorkflow|ReviewerSendToAudit|AuditReturn|ChecklistTemplateAdmin` + rewired `FundingAgreement|Signing|GenerateAgreementQueue`; plus Unit + Integration suites.
- [ ] T060 On merge-ready, update `CLAUDE.md` Recent Changes + the decomposition index (slice C → shipped) + brainstorm overview.

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)** → **Foundational (P2, blocks everything)** → **US1–US4 (P3–P6)** → **Polish (P7)**.
- US1 and US2 are both P1; US1 is the MVP (auditor can audit a seeded app). US2 produces real `PendingAudit` apps. They are independently testable via the seeder (US1) and the transition assertion (US2).
- US3 builds on the audit surface (US1) + reviewer rework (US2) but is independently testable by seeding a `PendingAudit` app and returning it.
- US4 is independent (admin config); the seeded default template (T015) lets US1–US3 run before US4 ships.

### Within each story
- Application-layer interfaces (`[P]`) before their Infrastructure impls; services before controllers; controllers before views; integration before E2E.
- Notification events: enum/storage → bucket/resolver → binding → templates → enqueue.

### Parallel opportunities
- Foundational: T002–T007, T010, T011–T013, T016–T017, T020 are `[P]` (distinct files).
- US1: T022/T024/T029/T032 `[P]`. US2: T037 `[P]`. US3: T043 `[P]`. US4: T045/T047/T049/T051 `[P]`.
- After Foundational, US1–US4 can be staffed in parallel (different controllers/services/views); only the shared `_Layout.cshtml` sidebar edits (T031, T050) and `Review.cshtml` (T034/T041) must serialize.

---

## Parallel Example: Foundational

```bash
# Domain enums + entities + dacpac tables together (distinct files):
Task: "T002 Add new ApplicationState values"
Task: "T003 Add ChecklistStage enum"
Task: "T004 Add ChecklistResponseStatus enum"
Task: "T005 Create ChecklistTemplate aggregate"
Task: "T006 Create ChecklistTemplateItem"
Task: "T007 Create ApplicationChecklistResponse"
Task: "T011 dbo.ChecklistTemplates.sql"
Task: "T012 dbo.ChecklistTemplateItems.sql"
Task: "T013 dbo.ApplicationChecklistResponses.sql"
```

## Implementation Strategy

### MVP (US1)
1. Phase 1 Setup → 2. Phase 2 Foundational (critical) → 3. Phase 3 US1 → **STOP & validate** (auditor audits a seeded app, group-scoped) → demo.

### Incremental
US1 (audit a seeded app) → US2 (real reviewer hand-off + auditor notice) → US3 (return loop) → US4 (admin-config checklists) → Phase 7 rewire + filtered-E2E gate. Each story adds value without breaking prior ones.

---

## Implementation deviations

- **D-A (T008/T027):** `Application.CanUserGenerateFundingAgreement` was **left unchanged**
  (its `isReviewerAssigned` param + many call sites/tests stay green). "Reviewer can no
  longer generate" is enforced at the **controller** instead: `FundingAgreementController.Generate`
  now requires `Auditor||Admin` (reviewer branch removed) + `PendingAudit` + a complete audit
  checklist for the non-admin path. `CanGenerateFundingAgreement` was broadened to accept
  `PendingAudit` so the auditor reuses the existing PDF pipeline.
- **D-B (T027):** No separate `POST /Audit/{id}/Generate`. Per T027's "(or the auditor path)"
  allowance, auditor generation reuses the re-gated `FundingAgreement/Generate` endpoint (no PDF
  pipeline duplication); on success an auditor is redirected back to `/Audit/{id}`.
- **D-C (T023):** `AuditorQueueProjection` lives in `Application/Services` (next to
  `ReviewerQueueProjection`); inbox `EnteredAuditAtUtc` is proxied by `UpdatedAt`,
  `HasProviderWarning` is surfaced on the detail page (not the row).
- **D-D (T030):** Auditor is now group-scoped: `NormalizeGroupIdsForRole` keeps Auditor
  memberships (only Admin stays groupless). `RoleRequiresGroups` unchanged — auditors MAY have
  zero groups (empty inbox), to avoid breaking the spec-038 auditor seed.
- **T036 front-loaded into US1:** the `SentToAuditAuditor`/`ReturnedToReviewerFromAudit` enum +
  Auditor bucket + resolver + bindings + 4 es-CR templates were implemented during US1 (the
  service references them), so US2/US3 only add the reviewer-facing surfaces + enqueue triggers.

## FR coverage map

| FR | Tasks |
|---|---|
| FR-001/002/003 (checklist admin, one-active, snapshot) | T005–T007, T015, T045–T052 |
| FR-004/005 (reviewer gate, send-to-audit, generate removed) | T008, T034, T035 |
| FR-006/017 (group-scoped inbox + group assignment) | T023, T028, T030, T032, T033 |
| FR-007 (reviewer-equivalent read) | T028, T029 |
| FR-008/009 (audit checklist, approve→generate) | T008, T025, T027 |
| FR-010 (confirm + release + re-point notice) | T009, T025, T026 |
| FR-011 (return + reasons + email) | T039, T040, T042 |
| FR-012 (reviewer rework) | T041 |
| FR-013 (Auditor/Admin permissions) | T008, T027, T028 |
| FR-014 (audit trail) | T008 (VersionHistory), T046/T047 (AdminAuditEvent) |
| FR-015/016 (es-CR, refusals) | T028, T035, T057 |
| FR-018 (auditor notification) | T036 |
| SC-001..006 | T033, T038, T044, T052, T059 |
