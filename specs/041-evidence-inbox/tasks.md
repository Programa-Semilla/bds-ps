---

description: "Task list for funds-usage evidence inbox (041)"
---

# Tasks: Funds-Usage Evidence Inbox

**Input**: Design documents from `specs/041-evidence-inbox/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D8), data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: INCLUDED — Constitution III makes Playwright E2E non-negotiable; one integration test guards the query predicate. Unit test is optional (InMemory can't enforce the DB filter; kept light).

**Organization**: Grouped by user story (US1 P1, US2 P2, US3 P2). No new state/schema/managed dependency (NFR-002).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies)
- **[Story]**: US1 / US2 / US3 (Setup/Foundational/Polish carry no story label)

## Path Conventions

ASP.NET MVC, Clean Architecture: `src/FundingPlatform.{Domain,Application,Infrastructure,Web}/`, tests under `tests/FundingPlatform.Tests.{Unit,Integration,E2E}/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: es-CR copy + namespace scaffolding shared across stories.

- [ ] T001 [P] Create `EvidenceInboxResources` (.resx + designer, es-CR) with keys `Nav`="Evidencia de uso de fondos", `Title`, `Empty` in `src/FundingPlatform.Web/Resources/EvidenceInboxResources.*`
- [ ] T002 [P] Add es-CR keys `ReadOnly_Notice` and `Error_ProcessClosed` to `src/FundingPlatform.Web/Resources/FundsUsageEvidenceResources.*` (values per contracts/interfaces.md)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The inbox query backbone. Blocks US1 and US3's scoping assertions. (US2's read-only gate is independent and may start in parallel with this phase.)

**⚠️ CRITICAL**: US1 cannot begin until T003–T005 are complete.

- [ ] T003 Define `IEvidenceInboxProjection` + `EvidenceInboxRowDto` (per contracts/interfaces.md) in `src/FundingPlatform.Application/EvidenceInbox/IEvidenceInboxProjection.cs`
- [ ] T004 Implement `EvidenceInboxProjection` in `src/FundingPlatform.Infrastructure/Persistence/EvidenceInboxProjection.cs` — single EF query: `State == AgreementExecuted` ∧ `Group.Process.Status == Active` ∧ group-overlap (admin short-circuit via `UserGroupMemberships`, empty-group → empty) ∧ `ExcludeDeleted` ∧ `ExcludeArchivedFund`; order by `UpdatedAt` desc; take 200 (mirror `ReviewerDashboardProjection` + `SignedUploadRepository.GetPendingInboxAsync`)
- [ ] T005 Register `IEvidenceInboxProjection` → `EvidenceInboxProjection` in DI alongside the other reviewer projections (Infrastructure `ServiceCollection` extension)

**Checkpoint**: Inbox query available and scoped.

---

## Phase 3: User Story 1 - Reviewer returns to an executed application to add evidence (Priority: P1) 🎯 MVP

**Goal**: A persistent, group-scoped sidebar inbox of executed applications in active processes, each linking to its evidence page.

**Independent Test**: As an in-scope reviewer with an `AgreementExecuted` app in an `Active` process, open the sidebar entry → see the app → click through → upload a file. Empty case shows a friendly es-CR message.

### Tests for User Story 1

- [ ] T006 [P] [US1] Integration test `tests/FundingPlatform.Tests.Integration/EvidenceInboxQueryTests.cs` — real-DB matrix over `State × Process.Status × group-overlap`: only `AgreementExecuted ∧ Active ∧ in-scope` returns; archived-fund + soft-deleted excluded; empty-group reviewer → empty; admin sees all
- [ ] T007 [P] [US1] E2E `tests/FundingPlatform.Tests.E2E/Tests/EvidenceInboxTests.cs` (`[Category("EvidenceInbox")]`): `Inbox_ListsExecutedActiveProcessApp_AndLinksToEvidence` (row `data-application-number`, click → `/Applications/{id}/Evidence`, upload succeeds) and `Inbox_EmptyForReviewerWithNoQualifyingApps` (`evidence-inbox-empty`). Reuse `FundingAgreementSeeder.SeedExecutedAgreementAsync`

### Implementation for User Story 1

- [ ] T008 [P] [US1] Create `EvidenceInboxViewModels` (inbox VM + row VM) in `src/FundingPlatform.Web/ViewModels/EvidenceInboxViewModels.cs`
- [ ] T009 [US1] Create `EvidenceInboxController` (`[Authorize(Roles="Reviewer,Admin")]`, `[Route("Evidence")]`, `GET ""` → `Index`) resolving scope via `IReviewerScopeProvider` and rendering rows from `IEvidenceInboxProjection` in `src/FundingPlatform.Web/Controllers/EvidenceInboxController.cs` (depends on T003–T005, T008)
- [ ] T010 [US1] Create `src/FundingPlatform.Web/Views/EvidenceInbox/Index.cshtml` — rows (`data-testid="evidence-inbox-row"`, `data-application-number`, `evidence-inbox-open` link) + `evidence-inbox-empty` empty state; es-CR via `EvidenceInboxResources`
- [ ] T011 [US1] Add sidebar entry to `operativoEntries` in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` (slug `evidence-inbox`, label from resources, icon `ti ti-folder`, roles `Reviewer,Admin`, href `Url.Action("Index","EvidenceInbox")`)

**Checkpoint**: US1 fully functional — reviewer reaches evidence via the sidebar (SC-001).

---

## Phase 4: User Story 2 - Closing the process freezes and de-lists evidence (Priority: P2)

**Goal**: When the governing Process is `Closed`, the app leaves the inbox (already delivered by T004's `Active` filter) and the evidence page becomes read-only (view + download only; writes rejected server-side).

**Independent Test**: Close the process of an executed app; confirm it's absent from the inbox, its evidence page loads read-only (notice shown, no write controls), download works, and crafted upload/note/delete POSTs are rejected with no change.

### Tests for User Story 2

- [ ] T012 [P] [US2] E2E in `EvidenceInboxTests.cs`: `ClosedProcess_AppDeListed_AndEvidenceReadOnly` (admin closes via `POST /Admin/Processes/{id}/Close`; app gone from inbox; page loads; `evidence-readonly-notice` shown; no upload/save/delete controls; download still works) and `ClosedProcess_DirectMutationRejected` (crafted POST to Upload/Note/Delete → no change + es-CR toast). Optional: `ReopenedProcess_ReappearsAndEditable`

### Implementation for User Story 2

- [ ] T013 [US2] Add `bool IsReadOnly` to `src/FundingPlatform.Web/ViewModels/FundsUsageEvidenceIndexViewModel.cs`
- [ ] T014 [US2] In `src/FundingPlatform.Web/Controllers/FundsUsageEvidenceController.cs`: add private `IsProcessClosedAsync(applicationId, ct)` (EF `Group.Process.Status`); set `IsReadOnly` in `Index`; in `Upload`/`EditNote`/`Delete`, **after** `IsAccessibleAsync`, if closed → no mutation, set `FundsUsageEvidenceResources.Error_ProcessClosed` toast, redirect to `Index` (FR-006/FR-007)
- [ ] T015 [P] [US2] Edit `src/FundingPlatform.Web/Views/FundsUsageEvidence/Index.cshtml` — when `IsReadOnly`: hide upload form, render `evidence-readonly-notice` banner (`ReadOnly_Notice`)
- [ ] T016 [P] [US2] Edit `src/FundingPlatform.Web/Views/FundsUsageEvidence/_EvidenceRow.cshtml` — when `IsReadOnly`: hide save-note + delete controls, keep download (thread `IsReadOnly` into the row model/partial)

**Checkpoint**: US1 + US2 both work; closing a process freezes evidence and de-lists the app.

---

## Phase 5: User Story 3 - Access control is preserved on the new surfaces (Priority: P2)

**Goal**: The inbox and read-only mode do not weaken spec-036 access control: applicants and out-of-group reviewers are refused with no disclosure, in both `Active` and `Closed` states.

**Independent Test**: Out-of-group reviewer and owning applicant each hit the evidence page + a download/mutation route (active and closed) → refused with no disclosure; applicants never see the sidebar entry.

### Tests for User Story 3

- [ ] T017 [P] [US3] E2E in `EvidenceInboxTests.cs`: `OutOfGroupReviewer_AndApplicant_Refused` — out-of-group reviewer → 404 on page + download + mutation routes (active and closed); applicant → 404/refusal and no `evidence-inbox` sidebar entry; an in-scope reviewer's inbox never lists another group's app

### Implementation for User Story 3

- [ ] T018 [US3] In `FundsUsageEvidenceController` confirm the process-closed check runs strictly **after** `IsAccessibleAsync` (and `EvidenceBelongsAsync`) so an unauthorized caller still gets the flat 404 and never learns "closed vs. nonexistent" (FR-008); add a code comment pinning the order. Confirm `EvidenceInboxProjection` applies scope in-query (no UI-only filtering) — covered by T004/T006, asserted here

**Checkpoint**: All three stories independently functional; no disclosure regressions.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T019 Run the filtered E2E gate: `dotnet test tests/FundingPlatform.Tests.E2E --filter "Category=EvidenceInbox"` green, plus a regression pass of any touched evidence/process classes (`FundsUsageEvidence*`); confirm spec-036 active-process behavior unregressed (SC-005)
- [ ] T020 [P] (Optional) Unit test `tests/FundingPlatform.Tests.Unit/Application/EvidenceInboxProjectionTests.cs` — admin short-circuit + empty-group → empty (InMemory; keep DB-filter assertions in T006)
- [ ] T021 Update `specs/041-evidence-inbox/tasks.md` deviation log (if any) and add a CLAUDE.md **Recent Changes** entry on delivery (counts: Unit/Integration/filtered-E2E)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: depends on Setup; blocks US1 + US3. US2 (T013–T016) is independent of the projection and may run in parallel with Foundational.
- **US1 (P3)**: after Foundational.
- **US2 (P4)**: T012 depends on T004 (de-list via `Active` filter) **and** T013–T016 (read-only). The read-only impl itself depends only on Setup (T002).
- **US3 (P5)**: depends on US1 (inbox surface) and benefits from US2 (closed-state assertions); the refusal core can test against US1 alone.
- **Polish (P6)**: after the stories you intend to ship.

### Within Each Story

- Tests authored to FAIL first (TDD per superpowers), then implementation.
- Models/VMs before controller; controller before view; view + nav last.

### Parallel Opportunities

- T001, T002 in parallel (different resource files).
- T006, T007 in parallel (integration vs E2E).
- T008 in parallel with T006/T007 (VM is independent of tests).
- T015, T016 in parallel (different view files) once T013/T014 land.
- US2 read-only impl (T013–T016) can proceed in parallel with US1 once Setup is done.

---

## Parallel Example: User Story 1

```bash
# Tests for US1 together:
Task: "Integration EvidenceInboxQueryTests (state×status×group matrix)"
Task: "E2E EvidenceInboxTests inbox-lists + empty"
# Then VM in parallel with tests:
Task: "EvidenceInboxViewModels"
```

---

## Implementation Strategy

### MVP First (US1)

1. Setup (T001–T002) → Foundational (T003–T005) → US1 (T006–T011).
2. **STOP & VALIDATE**: reviewer reaches evidence via the sidebar (SC-001). Shippable MVP — closes the reported gap.

### Incremental Delivery

1. MVP (US1) → demo.
2. Add US2 (read-only + de-list) → demo.
3. Add US3 (access-control assertions + ordering safeguard) → demo.
4. Polish (T019–T021).

---

## Notes

- No new `ApplicationState`, no schema change, no managed dependency (NFR-002). `ProcessStatus` already EF-mapped — no TINYINT-conversion work.
- `AgreementExecuted` does not block `ProcessService.CloseAsync`, so US2's closed scenario is reachable (research D6).
- Delivery gate = filtered `EvidenceInbox` E2E personally executed and green (project convention), not the full ~30-min suite.
- Commit after each task or logical group (Constitution).
