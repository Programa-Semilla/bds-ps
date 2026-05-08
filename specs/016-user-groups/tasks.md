---

description: "Task list for spec 016 — Group-Scoped Reviewer Access"
---

# Tasks: Group-Scoped Reviewer Access

**Input**: Design documents in `specs/016-user-groups/`
**Prerequisites**: `plan.md`, `spec.md`, `data-model.md`, `contracts/admin-groups.md`, `contracts/admin-users-form.md`, `research.md`, `quickstart.md`

**Tests**: This feature includes test tasks (unit + integration + E2E). The constitution mandates E2E coverage for every user story, so E2E test tasks are non-optional. Unit and integration tests are added where they protect domain invariants and EF predicate shapes.

**Organization**: Tasks are grouped by user story. Stories US1 + US2 + US3 are P1 and form the deliverable slice; US4 is P2 and follows.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependencies on incomplete tasks)
- **[Story]**: user-story label (`[US1]`, `[US2]`, `[US3]`, `[US4]`); omitted in Setup, Foundational, and Polish phases
- File paths are absolute relative to repository root

## Path Conventions

Single ASP.NET MVC project tree under `src/`. Tests under `tests/`. Schema under `src/FundingPlatform.Database/`. See `plan.md` § Project Structure.

---

## Phase 1: Setup

**Purpose**: No new tooling, dependencies, or scaffolding are required for this feature. The solution, dacpac, EF Core, Identity, Aspire orchestration, and Playwright are already in place. Phase 1 is therefore intentionally empty; all real work begins in Foundational.

_(no tasks)_

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, domain entities, EF wiring, audit writer, and shared resource files. Every user story depends on this phase.

**⚠️ CRITICAL**: No user story work may begin until this phase is complete.

### Schema (dacpac)

- [ ] T001 [P] Add `src/FundingPlatform.Database/Tables/dbo.Groups.sql` per data-model.md (Id IDENTITY PK, Name NVARCHAR(100) COLLATE Latin1_General_CI_AI NOT NULL, CreatedAt/UpdatedAt DATETIMEOFFSET NOT NULL, unique non-clustered index on Name)
- [ ] T002 [P] Add `src/FundingPlatform.Database/Tables/dbo.UserGroupMemberships.sql` (composite PK (UserId, GroupId), FKs to AspNetUsers.Id and Groups.Id with ON DELETE CASCADE on both, AssignedAt DATETIMEOFFSET NOT NULL, non-clustered index on (GroupId, UserId))
- [ ] T003 [P] Add `src/FundingPlatform.Database/Tables/dbo.AdminAuditEvents.sql` (Id BIGINT IDENTITY PK, OccurredAt DATETIMEOFFSET, ActorUserId FK to AspNetUsers NO CASCADE, Action/TargetType/TargetId NVARCHAR, PayloadJson NVARCHAR(MAX) NULL)
- [ ] T004 Append demo seed to `src/FundingPlatform.Database/PostDeployment/SeedData.sql` — insert `Norte`, `Sur`, `Centro` into `dbo.Groups` if absent (idempotent guard)

### Domain entities

- [ ] T005 [P] Create `src/FundingPlatform.Domain/Entities/Group.cs` with private setters, static `Create(string name)` and instance `Rename(string newName)` enforcing trim + non-empty + length ≤ 100; expose `IReadOnlyCollection<UserGroupMembership> Memberships`
- [ ] T006 [P] Create `src/FundingPlatform.Domain/Entities/UserGroupMembership.cs` (UserId, GroupId, AssignedAt; constructor enforces non-empty UserId and positive GroupId)
- [ ] T007 [P] Create `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` with static `Record(...)` factory validating non-empty fields
- [ ] T008 Modify `src/FundingPlatform.Domain/Entities/ApplicationUser.cs` to add `ICollection<UserGroupMembership> Memberships` navigation (private setter, initialized in constructor) — keep existing fields untouched

### EF configurations

- [ ] T009 [P] Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/GroupConfiguration.cs` mapping table name, identity key, Name uniqueness via index, default `SYSUTCDATETIME()` on CreatedAt/UpdatedAt
- [ ] T010 [P] Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/UserGroupMembershipConfiguration.cs` mapping composite key, both FKs with cascade-on-delete, and the `(GroupId, UserId)` index
- [ ] T011 [P] Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/AdminAuditEventConfiguration.cs`
- [ ] T012 [P] Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs` (or extend an existing one if present) wiring the `ApplicationUser → Memberships` collection
- [ ] T013 Modify `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` to add `DbSet<Group>`, `DbSet<UserGroupMembership>`, `DbSet<AdminAuditEvent>` (T013 depends on T005–T012 because the DbSet generic types reference the new entities and configurations are picked up by `ApplyConfigurationsFromAssembly`)

### Audit writer

- [ ] T014 [P] Create `src/FundingPlatform.Application/Audit/IAdminAuditWriter.cs` (single async `WriteAsync(AdminAuditEvent ev, CancellationToken ct)`)
- [ ] T015 [P] Create `src/FundingPlatform.Infrastructure/Audit/AdminAuditWriter.cs` implementing `IAdminAuditWriter` over the DbContext, register in DI in the same Identity/Persistence wiring as existing infrastructure services

### Localized copy

- [ ] T016 [P] Create `src/FundingPlatform.Web/Resources/AdminGroupsResources.cs` (es-CR strings: page title, create/edit/delete labels, member-count column, validation messages `NameRequired`, `NameTooLong`, `NameAlreadyInUse`, delete-confirm copy)
- [ ] T017 [P] Extend `src/FundingPlatform.Web/Resources/AdminUsersResources.cs` (or create if missing) with `GroupSelectorLabel`, `GroupSelectorHelpText`, `AtLeastOneGroupRequired`, `GroupNotFound`, `ConcurrencyConflict`

**Checkpoint**: Schema, entities, EF wiring, audit, and copy are in place. User stories may now begin.

---

## Phase 3: User Story 1 — Admin manages the catalog of groups (Priority: P1) 🎯 MVP

**Goal**: Admin-only CRUD on the `Group` catalog (FR-001 ‒ FR-003, FR-006).

**Independent Test**: Per `spec.md` Story 1 Independent Test — admin creates two distinct groups, fails on duplicate, renames, deletes; non-admin gets 403 from the management URL.

### Tests for User Story 1

- [ ] T018 [P] [US1] Add `tests/FundingPlatform.Tests.Unit/Domain/GroupTests.cs` covering `Create` validation (empty/whitespace/over-length), `Rename` validation, and the `UpdatedAt` bump
- [ ] T019 [US1] Add `tests/FundingPlatform.Tests.Integration/GroupServiceTests.cs` against the real DB — create succeeds, create-with-duplicate-name (case- and accent-insensitive) fails on the unique index, rename preserves memberships, delete cascades through `UserGroupMemberships`, audit row written for every mutation

### Implementation for User Story 1

- [ ] T020 [P] [US1] Create `src/FundingPlatform.Application/Admin/Groups/IGroupService.cs` (`ListAsync`, `CreateAsync(name)`, `RenameAsync(id, name, actorUserId)`, `DeleteAsync(id, actorUserId)`) and `GroupCommands.cs` for DTOs
- [ ] T021 [US1] Create `src/FundingPlatform.Infrastructure/Services/GroupService.cs` implementing `IGroupService` — uses DbContext, writes one `AdminAuditEvent` per mutation via `IAdminAuditWriter`, surfaces unique-index violations as `DuplicateGroupNameException` (caught by the controller and rendered as `ModelState`)
- [ ] T022 [P] [US1] Create `src/FundingPlatform.Web/ViewModels/Admin/AdminGroupsIndexViewModel.cs` and `AdminGroupRow` record per `contracts/admin-groups.md`
- [ ] T023 [P] [US1] Create `src/FundingPlatform.Web/ViewModels/Admin/AdminGroupCreateViewModel.cs` and `AdminGroupEditViewModel.cs`
- [ ] T024 [US1] Create `src/FundingPlatform.Web/Controllers/Admin/AdminGroupsController.cs` with `[Authorize(Roles = "Admin")]`, all five actions (Index GET, Create GET/POST, Edit GET/POST, Delete POST) per the contract; collect all validation errors in a single `ModelState` round-trip
- [ ] T025 [P] [US1] Create `src/FundingPlatform.Web/Views/Admin/Groups/Index.cshtml` (table with name + member count, Create button, Edit/Delete row actions)
- [ ] T026 [P] [US1] Create `src/FundingPlatform.Web/Views/Admin/Groups/Create.cshtml`
- [ ] T027 [P] [US1] Create `src/FundingPlatform.Web/Views/Admin/Groups/Edit.cshtml` (rename + delete confirmation form)
- [ ] T028 [US1] Create `tests/FundingPlatform.Tests.E2E/PageObjects/AdminGroupsPage.cs` (POM for create/edit/delete flows)
- [ ] T029 [US1] Create `tests/FundingPlatform.Tests.E2E/Tests/AdminGroupCrudTests.cs` covering Story 1's acceptance scenarios 1–4 (admin creates group; duplicate name rejected; rename preserves memberships; non-admin direct URL → 403)

**Checkpoint**: Story 1 is independently demonstrable.

---

## Phase 4: User Story 2 — Admin assigns one or more groups to non-admin users (Priority: P1)

**Goal**: User create + edit forms gain a multi-select group selector; FR-007 ‒ FR-010 enforced.

**Independent Test**: Per `spec.md` Story 2 Independent Test — create reviewer with zero groups blocked, with two groups succeeds; promoting to Admin discards memberships; demoting Admin → Reviewer blocks save until at least one group selected.

### Tests for User Story 2

- [ ] T030 [US2] Add `tests/FundingPlatform.Tests.Integration/UserAdministrationGroupsTests.cs` covering: create user with 0 groups (Reviewer/Applicant) rejected; create with N groups inserts N rows; edit diff (added vs removed) applied in a single transaction; role flipped to Admin → all memberships removed and posted GroupIds ignored; concurrency-stamp mismatch → ConcurrencyConflict surfaced

### Implementation for User Story 2

- [ ] T031 [P] [US2] Modify `src/FundingPlatform.Web/ViewModels/Admin/AdminUserCreateViewModel.cs` adding `int[] GroupIds` and `IReadOnlyList<AdminUserGroupOption> AvailableGroups`
- [ ] T032 [P] [US2] Modify `src/FundingPlatform.Web/ViewModels/Admin/AdminUserEditViewModel.cs` with the same fields plus the existing `RowVersion`/concurrency-stamp wire-up
- [ ] T033 [US2] Modify `src/FundingPlatform.Application/Admin/Users/IUserAdministrationService.cs` and `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` to accept `int[] groupIds` on Create/Edit, enforce role-based rules per `contracts/admin-users-form.md`, write a single `AdminAuditEvent` for the diff (or none on no-op)
- [ ] T034 [US2] Modify `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` Create + Edit actions to populate `AvailableGroups`, bind `GroupIds`, route through the updated service, and re-render the form on validation failure
- [ ] T035 [P] [US2] Modify the admin user Create view (`src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml`) to render the multi-select bound to `GroupIds`, hidden when role = Admin (CSS `d-none` + small JS toggle that mirrors the existing role-dependent UI pattern)
- [ ] T036 [P] [US2] Modify the admin user Edit view (`src/FundingPlatform.Web/Views/Admin/Users/Edit.cshtml`) with the same control, pre-selecting current memberships, plus the existing `RowVersion` hidden field
- [ ] T037 [US2] Create or extend `tests/FundingPlatform.Tests.E2E/PageObjects/AdminUserFormPage.cs` to support reading and setting the multi-select
- [ ] T038 [US2] Create `tests/FundingPlatform.Tests.E2E/Tests/AdminUserGroupAssignmentTests.cs` covering Story 2's five acceptance scenarios end-to-end through the admin UI

**Checkpoint**: Story 2 is demonstrable; users now carry membership data.

---

## Phase 5: User Story 3 — Reviewer sees only applicants from shared groups (Priority: P1)

**Goal**: FR-011 ‒ FR-016, NFR-001, NFR-002. Group-overlap predicate is composed at the EF query level on every reviewer-facing surface; detail-page authorization enforces the same rule server-side; admins are exempt; applicants always see their own data.

**Independent Test**: Per `spec.md` Story 3 Independent Test — three groups, three applicants distributed across them, two reviewers (single-group, multi-group), one admin; verify queue, signing inbox, search, detail-page authorization, and admin bypass.

### Tests for User Story 3

- [ ] T039 [P] [US3] Add `tests/FundingPlatform.Tests.Unit/Application/ReviewerScopePredicateTests.cs` validating that the composed `IQueryable` filter is short-circuited when `IsAdmin == true` and otherwise emits an `EXISTS`-shaped predicate (assert via `ToQueryString()` on a real `IQueryable<Application>` instance, or via a fake `DbContext`)
- [ ] T040 [US3] Add `tests/FundingPlatform.Tests.Integration/ReviewerQueueScopeTests.cs` against the real DB — seeds three applicants in (`Norte`), (`Sur`), (`Norte+Sur`) and two reviewers; asserts queue, signing-inbox, applicant-search, and detail-auth all return scope-correct results; admin sees everything; applicant always sees own application

### Implementation for User Story 3

- [ ] T041 [P] [US3] Create `src/FundingPlatform.Application/Reviewer/IReviewerScope.cs` (`bool IsAdmin`, `IReadOnlyCollection<int> GroupIds`)
- [ ] T042 [US3] Create `src/FundingPlatform.Infrastructure/Identity/ReviewerScopeProvider.cs` reading `ClaimsPrincipal` and DB to produce an `IReviewerScope` per request; register as scoped DI
- [ ] T043 [US3] Modify `src/FundingPlatform.Application/Services/ReviewerQueueProjection.cs` (interface + impl) to accept `IReviewerScope`, compose the EF predicate, and remove any existing in-memory filtering — NFR-001 mandates query-level
- [ ] T044 [US3] Modify the signing-inbox query in `src/FundingPlatform.Infrastructure/Services/SignedUploadService.GetInboxAsync` (and `IInboxQuery`/equivalent) to compose the same predicate; admin caller short-circuits as today
- [ ] T045 [US3] Modify the reviewer applicant/application search service (locate via `SearchController` or the corresponding projection class — see plan § Project Structure) to compose the same predicate
- [ ] T046 [US3] Modify `src/FundingPlatform.Web/Controllers/ReviewController.cs` `Review(int id)` and any signing-detail action to enforce overlap server-side; deny with 403 when scope is non-admin and the application's applicant has no shared group; applicant-self-access path remains as it is today
- [ ] T047 [P] [US3] Add or extend `tests/FundingPlatform.Tests.E2E/PageObjects/ReviewQueuePage.cs` and a new `SigningInboxPage.cs` if missing, to support assertions on the displayed applicant set
- [ ] T048 [US3] Create `tests/FundingPlatform.Tests.E2E/Tests/ReviewerScopeTests.cs` exercising all five acceptance scenarios from spec.md Story 3 (single-group reviewer; out-of-scope detail URL → 403; applicant own access; admin bypass on every surface; reviewer with zero memberships sees empty queue and 403 on detail)

**Checkpoint**: Story 3 is demonstrable; the visible reviewer experience is now scoped.

---

## Phase 6: User Story 4 — Group deletion cascades cleanly (Priority: P2)

**Goal**: FR-004, FR-005. Admin deletes a group → all `UserGroupMembership` rows for it are removed, no user records are deleted, users left with zero groups can still sign in and admin can still see them.

**Independent Test**: Per `spec.md` Story 4 Independent Test — seed users in two groups, delete one, verify remaining memberships untouched, deleted-group rows gone, no user deletions, reviewer-with-no-remaining-groups can still log in but sees an empty queue.

### Tests for User Story 4

- [ ] T049 [US4] Add `tests/FundingPlatform.Tests.Integration/GroupDeletionCascadeTests.cs` against the real DB — exercises Story 4's three acceptance scenarios: dual-group user retains the surviving group; reviewer left with zero memberships sees an empty queue and stays signed-in; applicant left with zero memberships still appears in the admin user list and admin can still open their application
- [ ] T050 [US4] Add `tests/FundingPlatform.Tests.E2E/Tests/GroupDeletionCascadeTests.cs` covering the same scenarios end-to-end through the admin and reviewer UIs

### Implementation for User Story 4

- [ ] T051 [US4] Verify (no implementation expected — cascade is configured by `T010` and the dacpac FK in `T002`): document in `tests/FundingPlatform.Tests.Integration/GroupDeletionCascadeTests.cs` an explicit assertion that the EF round-trip and the dacpac shape both deliver the cascade. Open a follow-up implementation task only if the assertion fails. (No new production code is expected here.)

**Checkpoint**: Story 4 is demonstrable. Feature scope is complete.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T052 [P] Update `CLAUDE.md` "Active Technologies" and "Recent Changes" to mention spec 016 (Group / UserGroupMembership / AdminAuditEvent — group-scoped reviewer access)
- [ ] T053 Run `quickstart.md` end-to-end manually as a smoke check before declaring delivery
- [ ] T054 Run the full E2E suite (`dotnet test tests/FundingPlatform.Tests.E2E`) and confirm every test passes — per the CLAUDE.md delivery bar, the feature is not delivered until this run is personally executed and green

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: empty.
- **Foundational (Phase 2)**: starts immediately. Within Phase 2: `T001 / T002 / T003` parallel; `T004` after `T001` (seed depends on the table); `T005 / T006 / T007` parallel; `T008` after `T006` (it references `UserGroupMembership`); `T009 / T010 / T011 / T012` parallel after their entities exist; `T013` after the configurations; `T014 / T015 / T016 / T017` parallel.
- **Phase 2 Checkpoint** blocks every user-story phase.
- **User stories (Phases 3–6)**: each starts after Phase 2 completes. US1, US2, US3 can run in parallel by separate developers if staffed; US4 depends on US1 (group delete UI) and US3 (reviewer queue) being in place to be testable end-to-end.
- **Polish (Phase 7)**: after US1 + US2 + US3 + US4 complete.

### Within Each User Story

- Tests written first (constitution: TDD-leaning, but practically the integration + E2E tests are written alongside implementation, with the E2E gate being mandatory before delivery)
- Domain entity → service → controller → view → page object → E2E tests
- Story complete (its E2E tests green) before moving on

### Parallel Opportunities

- All four schema tasks (T001–T003) and the seed (T004 after T001) sit in the dacpac; they touch different files and can land in one batch.
- Domain entities (T005–T007) and EF configurations (T009–T012) are file-disjoint and parallelizable.
- Resource files (T016, T017) are parallelizable.
- View files inside one story (T025–T027 for US1; T035–T036 for US2) are parallelizable.

---

## Parallel Example: Phase 2 fan-out

```bash
# Schema (after FundingPlatform.Database project structure exists, which it does):
Task: "Add dbo.Groups.sql"               # T001
Task: "Add dbo.UserGroupMemberships.sql" # T002
Task: "Add dbo.AdminAuditEvents.sql"     # T003

# Domain entities (independent files):
Task: "Create Group.cs"                  # T005
Task: "Create UserGroupMembership.cs"    # T006
Task: "Create AdminAuditEvent.cs"        # T007

# EF configurations (independent files):
Task: "GroupConfiguration.cs"            # T009
Task: "UserGroupMembershipConfiguration.cs" # T010
Task: "AdminAuditEventConfiguration.cs"  # T011
Task: "ApplicationUserConfiguration.cs"  # T012
```

---

## Implementation Strategy

### MVP First (User Story 1 + Story 2 + Story 3)

The three P1 stories are the deliverable slice. Story 1 alone produces a catalog without value to reviewers; Story 2 alone produces orphan memberships without filtering; Story 3 cannot operate on empty memberships. Ship them as one slice (matching the brainstorm decision recorded in spec.md Story 2 "Why this priority").

1. Complete Phase 2 Foundational.
2. Complete Phase 3 (US1).
3. Complete Phase 4 (US2).
4. Complete Phase 5 (US3).
5. **STOP and VALIDATE**: run the full E2E suite; confirm `quickstart.md` walkthrough succeeds; constitution gate satisfied.
6. Then layer Phase 6 (US4) and Polish.

### Incremental Delivery

Phase 6 (Story 4) is genuinely additive — it can ship in a follow-up if needed without breaking US1–US3.

### Parallel Team Strategy

After Phase 2:

- Developer A: US1 (T018–T029)
- Developer B: US2 (T030–T038)
- Developer C: US3 (T039–T048)

US4 follows once the three converge.

---

## Notes

- All new copy is es-CR (NFR-004); resource files are the source of truth.
- All admin-mutating actions write exactly one `AdminAuditEvent` row each (NFR-005); no-op edits do not.
- Reviewer-side filtering is composed at the EF query level (NFR-001); no in-memory post-filter is acceptable.
- Detail-page authorization (NFR-002) re-uses the same overlap predicate against the loaded entity, so listing and detail cannot drift.
- Membership changes must take effect on the very next request (NFR-003); `IReviewerScope` is request-scoped and reads memberships fresh per request.
- Commit at the end of each phase (constitution).
- Delivery bar: Phase 7's full E2E run MUST be personally executed and green.
