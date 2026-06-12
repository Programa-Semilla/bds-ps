# Tasks: Admin-only user provisioning + unique applicant User Code

**Input**: Design documents from `specs/032-admin-user-code/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/contracts.md, quickstart.md

**Tests**: Included — Constitution III makes E2E non-negotiable and SC-006 requires filtered E2E; integration tests hit a real DB (CLAUDE.md), unit covers the entity guard.

**Organization**: By user story. US1 (registration removal) is independent of the UserCode data layer. US2 and US3 both depend on the Phase 2 foundation.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: parallelizable (different file, no incomplete-task dependency)
- File paths are repo-relative under `src/` unless noted.

---

## Phase 1: Setup

**Purpose**: Baseline inventory before changes.

- [X] T001 [P] Inventory every `Register` reference for the SC-001 sweep: grep `asp-action="Register"`, `Url.Action("Register"`, `/Account/Register`, `nameof(Register)` across `src/FundingPlatform.Web/` and record the full hit list (drives T042).
- [X] T002 [P] Confirm the ephemeral E2E seed applicant `applicant@programa-semilla.test` has `UserCode = NULL` today, and note that US2/US3 E2E must seed/assign (and clean up) a known code for deterministic uniqueness + search assertions (plan.md "Notes for /speckit-tasks").

---

## Phase 2: Foundational — UserCode data layer (BLOCKS US2 + US3)

**Purpose**: Create the `Applicant.UserCode` column, entity, mapping, and DTO surface that US2 and US3 reference.
**⚠️ US1 does NOT depend on this phase** — registration removal can proceed in parallel or first.

- [X] T003 Add column `[UserCode] NVARCHAR(50) NULL` to `src/FundingPlatform.Database/Tables/dbo.Applicants.sql` (nullable → migration-safe, no backfill).
- [X] T004 Add filtered unique index in the same file (GO-separated, mirroring `UX_Appeals_OneOpenPerApplication`): `CREATE UNIQUE NONCLUSTERED INDEX [UX_Applicants_UserCode] ON [dbo].[Applicants]([UserCode]) WHERE [UserCode] IS NOT NULL;`
- [X] T005 Add `UserCode` (`string?`, private setter) to `src/FundingPlatform.Domain/Entities/Applicant.cs`: optional trailing ctor param `string? userCode = null`, add param to `UpdateProfile(...)`, and a guard (trim → null when whitespace; throw `ArgumentException` when non-null length > 50).
- [X] T006 Mirror in `src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicantConfiguration.cs`: `builder.Property(a => a.UserCode).HasMaxLength(50);` + `builder.HasIndex(a => a.UserCode).IsUnique().HasDatabaseName("UX_Applicants_UserCode").HasFilter("[UserCode] IS NOT NULL");`
- [X] T007 [P] Add `string? UserCode` to `src/FundingPlatform.Application/Admin/Users/DTOs/CreateUserRequest.cs`.
- [X] T008 [P] Add `string? UserCode` to `src/FundingPlatform.Application/Admin/Users/DTOs/UpdateUserRequest.cs`.
- [X] T009 [P] Add `string? UserCode` to `src/FundingPlatform.Application/Admin/Users/DTOs/UserDetailDto.cs`.
- [X] T010 [P] Unit test the entity guard in `tests/FundingPlatform.Tests.Unit/` (whitespace→null; >50 throws; valid value set via ctor and via `UpdateProfile`).

**Checkpoint**: column + entity + DTOs exist; US2 and US3 can begin.

---

## Phase 3: User Story 1 — Close public self-registration (Priority: P1) 🎯 MVP

**Goal**: `/Account/Register` returns 404; no register affordance anywhere; admin create remains the only path.
**Independent Test**: GET/POST `/Account/Register` → 404; `/` and `/Account/Login` show no register link; admin create still works. (No dependency on Phase 2.)

- [X] T011 [US1] Delete the `Register` GET (`:47-53`) and POST (`:55-99`) actions from `src/FundingPlatform.Web/Controllers/AccountController.cs`; leave the constructor and `_dbContext`/`_userManager` intact (used by Login/ForgotPassword/Profile).
- [X] T012 [US1] Delete `src/FundingPlatform.Web/Views/Account/Register.cshtml`.
- [X] T013 [US1] Delete the now-dead `src/FundingPlatform.Web/ViewModels/RegisterViewModel.cs`.
- [X] T014 [US1] In `src/FundingPlatform.Web/Views/Home/Index.cshtml` (`:30-33`), repoint the hero CTA from `asp-action="Register"` to `asp-action="Login" asp-controller="Account"` (keep `data-testid="public-landing-cta-button"`).
- [X] T015 [US1] In `src/FundingPlatform.Web/Views/Account/Login.cshtml` (`:43-45`), remove the "¿Aún no tienes cuenta? Crea una aquí" block.
- [X] T016 [US1] Build `FundingPlatform.slnx`; remove any dangling `using`/`@model RegisterViewModel` references the deletions exposed; confirm no compile error.
- [X] T017 [US1] E2E `RegistrationRemovedTests` in `tests/FundingPlatform.Tests.E2E/`: GET `/Account/Register`→404, POST→404 (no user created), no register link on `/` or `/Account/Login`, hero CTA resolves to the Login URL.

**Checkpoint**: US1 independently shippable.

---

## Phase 4: User Story 2 — Admin assigns a unique User Code (Priority: P1)

**Goal**: Admin create/edit asks for a required, unique, ≤50-char User Code for Solicitante only; applicant sees it read-only on profile.
**Independent Test**: create Solicitante blank→blocked, dup→blocked, valid→created; switch to non-applicant role→field gone; applicant profile shows it read-only.
**Depends on**: Phase 2 (T003–T009).

- [X] T018 [US2] In `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` `CreateUserAsync` (Applicant branch `~206-234`): pass `request.UserCode` into the `new Applicant(...)` ctor and `UpdateProfile(...)`; add a uniqueness pre-check mirroring the `LEGAL_ID_IN_USE` guard — when `UserCode` non-empty and `await _dbContext.Applicants.AnyAsync(a => a.UserCode == request.UserCode, ct)` → delete the just-created user and return `DomainError("USER_CODE_IN_USE", nameof(CreateUserRequest.UserCode), ...)`.
- [X] T019 [US2] In the same file's `UpdateUserAsync` (Applicant branch `~398-436`): thread `request.UserCode` through; add a uniqueness pre-check excluding self (`a.UserCode == request.UserCode && a.UserId != target.Id`) → `USER_CODE_IN_USE`.
- [X] T020 [US2] Map `UserCode` into the `UserDetailDto` projection in `UserAdministrationService.cs` (so the Edit form prefills and detail/list can read it).
- [X] T021 [P] [US2] Add `public string? UserCode { get; set; }` with `[StringLength(50)]` to `src/FundingPlatform.Web/ViewModels/Admin/AdminUserCreateViewModel.cs`.
- [X] T022 [P] [US2] Add the same `UserCode` `[StringLength(50)]` to `src/FundingPlatform.Web/ViewModels/Admin/AdminUserEditViewModel.cs`.
- [X] T023 [US2] In `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` `Create` POST: when role == `Applicant` and `string.IsNullOrWhiteSpace(vm.UserCode)` add ModelState error `AdminUsersResources.UserCodeRequired` on `UserCode`; map `vm.UserCode`→`CreateUserRequest`; on the `USER_CODE_IN_USE` result (and any `DbUpdateException` whose message contains `UX_Applicants_UserCode`) add the es-CR `UserCodeInUse` ModelState error and re-render.
- [X] T024 [US2] Same wiring in `AdminUsersController` `Edit` POST (required-for-Solicitante, map, duplicate→es-CR); skip both checks when role ≠ `Applicant`.
- [X] T025 [P] [US2] Add consts to `src/FundingPlatform.Web/Resources/AdminUsersResources.cs`: `UserCodeLabel = "Código de usuario"`, `UserCodeRequired = "El código de usuario es obligatorio para el rol Solicitante."`, `UserCodeInUse = "El código de usuario ya está en uso."`.
- [X] T026 [US2] In `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml`: render a labelled `UserCode` input (wrap with `data-testid="admin-user-usercode"`) and extend the existing role-change JS (`:119-124`, the LegalId show/hide) to also show the block only when role == `Applicant`.
- [X] T027 [US2] Same field + role-toggle JS in `src/FundingPlatform.Web/Views/Admin/Users/Edit.cshtml`, prefilled from the model.
- [X] T028 [P] [US2] Add `public string? UserCode { get; init; }` to `src/FundingPlatform.Web/ViewModels/ProfileViewModel.cs` and populate it from `applicant?.UserCode` in `AccountController.BuildProfileViewModelAsync` (`~:486`).
- [X] T029 [US2] In `src/FundingPlatform.Web/Views/Account/Profile.cshtml` (mirror the `CodigoPersonal` block `:103-111`): add a read-only "Código de usuario" field with the `administrado` badge and `data-testid="profile-usercode"`, rendered only when the value/applicant is present.
- [X] T030 [US2] Integration tests in `tests/FundingPlatform.Tests.Integration/` (real DB): create duplicate UserCode→blocked; update to another applicant's code→blocked; update keeping own code→allowed; create non-applicant role→no Applicant row / no UserCode check.
- [X] T031 [US2] E2E `AdminUserCodeTests` in `tests/FundingPlatform.Tests.E2E/`: Solicitante create blank→error, duplicate→error, valid→created; role toggle hides the field; non-applicant create has no field; applicant profile shows the read-only code. (Seed + teardown a throwaway code per T002.)

**Checkpoint**: US2 independently shippable.

---

## Phase 5: User Story 3 — Widen search to identification + User Code (Priority: P2)

**Goal**: The single search box on five surfaces also matches LegalId + UserCode (+ email on the reviewer queue); admin list + applicants report/CSV surface the code column.
**Independent Test**: per surface, search by code / cédula / email / name returns the seeded applicant; empty term unchanged.
**Depends on**: Phase 2 (column exists). Best demoed with a US2-assigned code, but E2E can seed a code directly.

- [X] T032 [US3] Widen `UserAdministrationService.ListUsersAsync` search (`~49-56`): add a correlated `_dbContext.Applicants.Any(a => a.UserId == u.Id && (a.LegalId.Contains(term) || (a.UserCode != null && a.UserCode.Contains(term))))` clause to the existing Email/First/Last predicate.
- [X] T033 [US3] Widen `ApplicationRepository.GetByStateForReviewerAsync` search (`~199-208`): add `EF.Functions.Like(a.Applicant.UserCode, likeTerm)` and `EF.Functions.Like(a.Applicant.Email, likeTerm)` to the OR chain.
- [X] T034 [P] [US3] `ReportQueryService` Aging block (`~354-361`): add `|| EF.Functions.Like(a.Applicant.UserCode, pattern)`.
- [X] T035 [P] [US3] `ReportQueryService` Applications block (`~520-527`): add `|| EF.Functions.Like(a.Applicant.UserCode, pattern)`.
- [X] T036 [US3] `ReportQueryService` Applicants block (`~580-587`): add `|| EF.Functions.Like(a.UserCode, pattern)`; and add a `UserCode` field to the Applicants report row DTO/projection it builds (consumed by T038).
- [X] T037 [US3] In `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml`: add a "Código de usuario" column rendering the value or `—`; update the search input placeholder text (via `AdminUsersResources`, e.g. `"Nombre, correo, identificación o código de usuario"`).
- [X] T038 [US3] Add a "Código de usuario" column to the Applicants report view `src/FundingPlatform.Web/Views/Admin/Reports/Applicants.cshtml` and its CSV export (header + row) in the report/CSV writer path.
- [X] T039 [P] [US3] Update the search placeholder in `src/FundingPlatform.Web/Resources/ReviewerQueueResources.cs` from `"Nombre o cédula"` to `"Nombre, cédula o código de usuario"`.
- [X] T040 [US3] Integration tests in `tests/FundingPlatform.Tests.Integration/`: for the admin-list predicate, the reviewer-queue predicate, and the three report predicates — assert a seeded applicant matches by UserCode, LegalId, email, and name; and that an empty term returns the full page.
- [X] T041 [US3] E2E `UserCodeSearchTests` in `tests/FundingPlatform.Tests.E2E/`: search by code on the admin users list, reviewer queue, and applicants report returns the seeded applicant; assert the "Código de usuario" column renders on the admin list and the applicants CSV header/row contains it.

**Checkpoint**: US3 shippable.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T042 SC-001 sweep: re-run the T001 greps → confirm zero residual `Register` references; fix any straggler links/usings.
- [ ] T043 Run the filtered E2E classes green and capture counts: `RegistrationRemovedTests`, `AdminUserCodeTests`, `UserCodeSearchTests` (`dotnet test tests/FundingPlatform.Tests.E2E --filter ...`). Run Unit + Integration suites green.
- [ ] T044 Update `CLAUDE.md` Recent Changes with the `032-admin-user-code` summary + delivery counts; flip the SPECKIT marker to "implemented".

---

## Dependencies & Order

- **Phase 1** → no deps.
- **Phase 2 (T003–T010)** → blocks **US2 (Phase 4)** and **US3 (Phase 5)**. Does **not** block **US1 (Phase 3)**.
- **US1 (Phase 3)** → independent; can land first as the MVP, in parallel with Phase 2.
- **US2 (Phase 4)** → needs Phase 2. T018/T019 (service) before T023/T024 (controller) before T026/T027 (views). T028/T029 (profile) independent within US2.
- **US3 (Phase 5)** → needs Phase 2 (column). T036 (projection) before T038 (column render). Otherwise the 5 search edits are independent files.
- **Phase 6** → after the stories it verifies.

## Parallel Execution Examples

- **Phase 2**: T007, T008, T009 (three DTO files) and T010 (unit) run together after T005/T006.
- **US2**: T021, T022, T025, T028 are `[P]` (distinct files) once the service (T018–T020) is in.
- **US3**: T034, T035, T039 are `[P]` (distinct files); T032/T033/T036 touch separate files too but T036 precedes T038.

## Implementation Strategy

- **MVP = US1** (registration closed) — independently deliverable, no schema dependency. Ship/verify first.
- Then **Phase 2 + US2** (the governed unique code), then **US3** (search reach). Each story checkpoints with its own filtered E2E (Constitution III, SC-006). Commit at each checkpoint (CLAUDE.md speckit-checkpoint discipline).

## Deviations discovered during implementation

- **D-1 (US1, scope+):** The `Register`-reference sweep found a navbar link in `Views/Shared/_Layout.cshtml` ("Crear cuenta") not listed in the plan's two link sites. Removed it too (SC-001 "no register links remain anywhere"). Tracked under T015.
- **D-2 (US1, test architecture):** The E2E suite bootstraps **all** test users through the public Register form via `AuthenticatedTestBase.RegisterUserAsync` (~103 call sites across ~60 files). Removing public registration would break the whole suite. Resolution: added a **Development-gated, no-UI** dev seam `GET /Account/SeedUser` (mirrors the existing dev seams `AssignRole`/`AssignAllGroups`/`ResetAdminFixture`/`SeedAdminFixture`) that reproduces the former Register POST, and rewired `RegisterUserAsync` to call it. The product surface stays admin-only (FR-004); the seam is unreachable outside Development (404). 3 tests that used the `RegisterPage` page object directly were rewritten (`AuthenticationTests` → seed+login; `RoleAwareSidebarTests` → drop the removed-page assertion; `InputMaskIdentificationTests` → relocate the spec-026 mask tests to `/Admin/Users/Create`, same `_LegalIdField` partial). `PageObjects/RegisterPage.cs` deleted (orphaned).
- **D-3 (US2, test architecture, resolved):** Making User Code required for Solicitante affects every admin-create-applicant test. Mitigated at the page-object layer: `AdminUserCreatePage.FillAsync` auto-fills a unique User Code for the Applicant role (mirroring its existing FR-008 group auto-select). Audit of all 5 admin-edit test files confirmed none keep an applicant at the Applicant role with an empty code (they edit non-applicants or demote an auto-coded applicant to Reviewer), so no edit-test changes were required.
- **D-5 (US1, FR-002 nuance — found during E2E):** With the `Register` action deleted, **GET** `/Account/Register` returns **404** (the user-facing case, spec-compliant), but a **POST** to the same path surfaces as **405 Method Not Allowed** rather than 404 (an ASP.NET Core routing/`UseHttpsRedirection` interaction in the http-based E2E harness). Functionally identical to the spec intent — the registration handler no longer exists, so no account can be created — but it is a literal deviation from FR-002's "GET or POST … MUST return 404." The E2E asserts the POST is rejected (404 **or** 405). Recorded for the code-review/evolve gate; reconcile FR-002 wording or normalize POST→404 (e.g., an explicit catch-all) if the literal 404 is required. Also note: the self-service profile is attribute-routed at `/Profile` (not `/Account/Profile`) — a test-only URL fix.
- **D-4 (US3, test coverage):** Direct E2E covers the two surfaces that gain a visible column — the admin users list (FR-012) and the applicants report (FR-014/FR-016): searching by code returns the applicant and the column renders. The reviewer queue (FR-013) and the Applications/Aging reports (FR-014) are **match-only** (no column) and use the *identical* `EF.Functions.Like(...UserCode...)` predicate over the same `Applicant.UserCode` column that the applicants-report E2E exercises end-to-end; the admin-list correlated predicate is additionally integration-tested. Standing up a full reviewer-queue submission flow for one more LIKE clause was judged disproportionate under the filtered-E2E delivery bar. Recorded for the code-review gate.
