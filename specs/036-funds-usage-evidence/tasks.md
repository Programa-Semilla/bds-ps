---
description: "Task list for Funds-Usage Evidence Stage (036)"
---

# Tasks: Funds-Usage Evidence Stage

**Input**: Design documents from `specs/036-funds-usage-evidence/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D10), data-model.md, contracts/ui-and-routes.md, contracts/interfaces.md

**Tests**: REQUIRED. Constitution Principle III (E2E NON-NEGOTIABLE) + the integration-against-real-DB rule make unit/integration/E2E tasks mandatory per story.

**Organization**: By user story. Phases 1–2 are shared prerequisites; Phases 3–6 are the four stories in priority order; Phase 7 is polish.

## Path Conventions

Clean Architecture 4-layer (`src/FundingPlatform.{Domain,Application,Infrastructure,Web}`, `src/FundingPlatform.Database`, `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`), per plan.md.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Additive seams the rest of the feature builds on. All additive — no existing behavior changes.

- [X] T001 Create the table DDL `src/FundingPlatform.Database/Tables/dbo.FundsUsageEvidence.sql` exactly per data-model.md (PK, FK→Applications NO ACTION, FK→AspNetUsers NO ACTION, CK FileSize>0, RowVersion, IX_ApplicationId).
- [X] T002 [P] Add `FundsUsageEvidence` member to `FileCategory` (container `funds-usage-evidence`) and wire `ContainerName()` + `AllContainerNames` in `src/FundingPlatform.Application/Abstractions/Storage/FileCategory.cs`.
- [X] T003 [P] Add `StorageCategoryOptions FundsUsageEvidence` (20 MiB, `ServingMode.BackendStream`) and its `For(FileCategory.FundsUsageEvidence)` case to `src/FundingPlatform.Application/Abstractions/Storage/StorageOptions.cs` (keeps `StorageOptionsValidator` green).
- [X] T004 [P] Add action-key constants `FundsEvidenceUploaded`/`FundsEvidenceNoteEdited`/`FundsEvidenceDeleted` + `TargetTypeFundsEvidence` to `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain + persistence + service + controller skeleton + test seed that ALL stories depend on.

**⚠️ CRITICAL**: No user-story phase can begin until this phase is complete.

- [X] T005 Create the domain aggregate `src/FundingPlatform.Domain/Entities/FundsUsageEvidence.cs` with private EF ctor, `static CreateForExecutedApplication(Application, …)` enforcing `State == AgreementExecuted` (FR-001) + field/note≤250 invariants, and `EditNote(string?)` (FR-006), per contracts/interfaces.md.
- [X] T006 Create EF mapping `src/FundingPlatform.Infrastructure/Persistence/Configurations/FundsUsageEvidenceConfiguration.cs` (lengths, `IsRowVersion`, FK to Application with no nav, backing fields) and register `DbSet<FundsUsageEvidence>` + apply config in `AppDbContext`.
- [X] T007 [P] Create `src/FundingPlatform.Application/FundsUsageEvidence/EvidenceFileTypePolicy.cs` — pure allow-list (`IsAllowed(fileName, contentType, head)` + `AllowedExtensions`) for PDF/PNG/JPEG/WebP/HEIC/Word/Excel with magic-byte families per research D3.
- [X] T008 [P] Create DTOs + service interface in `src/FundingPlatform.Application/FundsUsageEvidence/` (`IFundsUsageEvidenceService`, `FundsUsageEvidenceDtos.cs`) per contracts/interfaces.md.
- [X] T009 Create `src/FundingPlatform.Infrastructure/FundsUsageEvidence/FundsUsageEvidenceService.cs` implementing list/upload/edit-note/delete/download via `IObjectStorage` (ObjectKey per data-model) + `IAdminAuditWriter` in one transaction; extend `AdminAuditEventWriter` target-routing for the `funds_evidence.` prefix; register the service (+ any repository) in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`.
- [X] T010 Create the controller skeleton `src/FundingPlatform.Web/Controllers/FundsUsageEvidenceController.cs` — `[Authorize(Roles="Reviewer,Admin")]`, `[Route("Applications/{applicationId:int}/Evidence")]`, ctor DI (`IFundsUsageEvidenceService`, `IReviewerScopeProvider`, `IApplicationRepository`, `UserManager`), and the shared group-scope gate helper (`ApplicantSharesAnyGroupAsync` + admin short-circuit → `NotFound()`).
- [X] T011 Add a Development-only test seam to fast-forward an application to `AgreementExecuted` for E2E (mirror existing `SeedUser`/`AssignAllGroups` dev seams), or confirm a reusable signing-ceremony helper exists, in the Web dev-seam controller + E2E base helpers (research D8).

**Checkpoint**: Seams, domain, service, controller shell, and the executed-app seed exist — stories can proceed.

---

## Phase 3: User Story 1 — Collect funds-usage evidence (Priority: P1) 🎯 MVP

**Goal**: In-scope reviewer uploads files to an `AgreementExecuted` application, sees them listed with metadata, and downloads them.

**Independent Test**: On an executed application, upload a PDF and an image → both listed with name/uploader/timestamp → download returns the original.

- [X] T012 [US1] Implement `Upload` action in `FundsUsageEvidenceController` (`[UploadSizeGuard(FileCategory.FundsUsageEvidence)]` + antiforgery): buffer + `EvidenceFileTypePolicy.IsAllowed` (reject → es-CR toast, no row), gate `State==AgreementExecuted`, call `service.UploadAsync`, success toast, redirect to `Index`.
- [X] T013 [US1] Implement `Index` (list via `service.ListAsync`, gated on executed state) and `Download` (BackendStream via `service.OpenForDownloadAsync`) actions in `FundsUsageEvidenceController`.
- [X] T014 [US1] Create `src/FundingPlatform.Web/Views/FundsUsageEvidence/Index.cshtml` (upload form with accept hint + list + empty state) and `_EvidenceRow.cshtml` (name/download link, note, uploader, date) + `FundsUsageEvidenceResources` es-CR copy in `src/FundingPlatform.Web/Resources/`, with `data-testid` hooks per contracts/ui-and-routes.md.
- [X] T015 [US1] Add the conditional "Evidencia de uso de fondos" stage link/card to the per-application reviewer surface (funding-agreement detail/panel area), rendered only when `State == AgreementExecuted` (research D7).
- [X] T016 [P] [US1] Unit tests in `tests/FundingPlatform.Tests.Unit` — `FundsUsageEvidence.CreateForExecutedApplication` rejects non-executed state + enforces note≤250; `EvidenceFileTypePolicy` accepts each allowed family and rejects `.txt`/`.zip`/spoofed content-type.
- [X] T017 [P] [US1] Integration tests in `tests/FundingPlatform.Tests.Integration` (real DB) — upload→list→download happy path on an executed app; upload blocked on a non-executed app; one `funds_evidence.uploaded` audit row written.
- [X] T018 [US1] E2E in `tests/FundingPlatform.Tests.E2E` — `PageObjects/FundsUsageEvidencePage.cs` + `FundsUsageEvidenceTests` US1: upload PDF+image, assert both rows + metadata, download one (uses the T011 executed-app seed).

**Checkpoint**: US1 is an independently demoable MVP — evidence can be captured and retrieved.

---

## Phase 4: User Story 2 — Annotate each evidence item (Priority: P2)

**Goal**: Optional ≤250-char note per item, settable at upload and editable after, without re-uploading.

**Independent Test**: Add a 250-char note, edit it, see it update; a 251-char note is rejected.

- [X] T019 [US2] Implement `EditNote` action in `FundsUsageEvidenceController` (antiforgery, group-gate, `evidence.EditNote` via `service.EditNoteAsync`, audit) and add the note input (upload form) + inline edit affordance (maxlength 250 + live counter) in `Index.cshtml`/`_EvidenceRow.cshtml`.
- [X] T020 [P] [US2] Unit test — `FundsUsageEvidence.EditNote` trims, empty→null, rejects >250 (`tests/FundingPlatform.Tests.Unit`).
- [X] T021 [P] [US2] Integration test — set/edit/clear note persists; >250 rejected; `funds_evidence.note_edited` audit row written (`tests/FundingPlatform.Tests.Integration`).
- [X] T022 [US2] E2E — add 250-char note on an item, edit the text (updates without re-upload), attempt 251 chars → es-CR rejection (`FundsUsageEvidenceTests`).

**Checkpoint**: US2 layers onto US1 without changing upload/list behavior.

---

## Phase 5: User Story 3 — Remove an evidence item (Priority: P2)

**Goal**: Any in-scope reviewer/admin deletes any item after a confirm dialog; blob + row removed.

**Independent Test**: Delete one of several items via confirm → it disappears and its download 404s; others remain; cancel deletes nothing.

- [X] T023 [US3] Implement `Delete` action in `FundsUsageEvidenceController` (antiforgery, group-gate, `service.DeleteAsync` → blob delete then row + audit; missing row → `NotFound()`) and wire the spec-024 confirm dialog on the delete button in `_EvidenceRow.cshtml` (no native `confirm()`).
- [X] T024 [P] [US3] Integration test — delete removes blob + row; second concurrent delete resolves to not-found without error; `funds_evidence.deleted` audit row written (`tests/FundingPlatform.Tests.Integration`).
- [X] T025 [US3] E2E — delete with confirm (row gone, download 404), cancel on another item (unchanged) (`FundsUsageEvidenceTests`).

**Checkpoint**: US3 completes the management lifecycle.

---

## Phase 6: User Story 4 — Scoped, reviewer-only access (Priority: P3)

**Goal**: Reviewer/admin-only, group-scoped, no-disclosure refusals; stage unavailable before `AgreementExecuted`.

**Independent Test**: In-group reviewer succeeds; out-of-group reviewer + applicant + pre-execution app all get 404 with no disclosure.

- [X] T026 [US4] Audit every `FundsUsageEvidenceController` action (Index/Upload/EditNote/Delete/Download) for the group-scope gate + role gate + `AgreementExecuted` gate, ensuring all failures return `NotFound()` (no disclosure) and applicants never pass the role filter.
- [X] T027 [P] [US4] Integration test — out-of-group reviewer, applicant, and a non-executed application each get not-found on list/upload/download (`tests/FundingPlatform.Tests.Integration`).
- [X] T028 [US4] E2E — out-of-group reviewer 404, applicant 404, and the stage link/route absent before `AgreementExecuted` (`FundsUsageEvidenceTests`).

**Checkpoint**: Access boundary verified end-to-end.

---

## Phase 7: Polish & Cross-Cutting

- [X] T029 [P] es-CR copy pass — confirm no English literals leak in views/JS; accept-hint lists allowed types; empty-state + all validation/toast messages live in `FundsUsageEvidenceResources`; size-cap reuses `UploadSizeGuardFilter.RejectionMessage`.
- [X] T030 [P] Run the filtered delivery gate — `dotnet test … --filter FullyQualifiedName~FundsUsageEvidence` for Unit + Integration, and the E2E `FundsUsageEvidenceTests` (all 4 stories) green; walk quickstart.md.
- [X] T031 Update `CLAUDE.md` Recent Changes + SPECKIT active-plan pointer after delivery (mark 036 shipped at PR time).

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** → **Phases 3–6 (stories, priority order)** → **Phase 7 (Polish)**.
- Within Setup: T002/T003/T004 are `[P]` (different files); T001 (dacpac) independent too.
- Foundational order: T005 → T006 (entity before EF map); T007/T008 `[P]`; T009 needs T005/T006/T008; T010 needs T008; T011 independent.
- **Story independence**: US1 is the MVP. US2/US3/US4 each extend the same controller/view but touch distinct actions/sections — US2 (`EditNote` + note UI), US3 (`Delete` + confirm), US4 (auth hardening + negative tests). They can be built in any order after US1, though they share `Index.cshtml`/`_EvidenceRow.cshtml` (coordinate edits to those two files).
- Tests within a story marked `[P]` run alongside that story's implementation (different files).

## Parallel Execution Examples

- **Setup**: T002, T003, T004 together (after/with T001).
- **Foundational**: T007 + T008 together; then T009 + T010.
- **US1**: T016 + T017 (unit + integration) in parallel with T014 view work; T018 (E2E) after T012–T015.

## Implementation Strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — a reviewer can upload, list, and download evidence on an executed application. Demoable and shippable on its own.
- Then layer US2 (notes), US3 (delete), US4 (access hardening) incrementally, running the filtered E2E for each story before moving on (delivery bar).
