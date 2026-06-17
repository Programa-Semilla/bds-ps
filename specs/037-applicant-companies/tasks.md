---
description: "Task list for 037-applicant-companies"
---

# Tasks: Applicant Companies — controlled company selection on submission

**Input**: Design documents from `specs/037-applicant-companies/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D13), data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: Included. Constitution III makes E2E non-negotiable; the repo also ships unit + integration (real DB). The delivery bar is **filtered E2E green** for the new/affected classes.

**Organization**: By user story. Implementation order follows plan.md: Foundational → US2 (admin management, P1) → US1 (applicant selection, P1) → US3 (history preservation, P2) → US4 (batch, P2) → Polish. US2 precedes US1 because companies must exist to be selectable.

## Path Conventions

4-layer Clean Architecture: `src/FundingPlatform.{Domain,Application,Infrastructure,Web}`, schema in `src/FundingPlatform.Database`, tests in `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

---

## Phase 1: Setup

No new project/tooling. Verify baseline builds before changes.

- [ ] T001 Confirm the solution builds clean on branch `037-applicant-companies`: `dotnet build FundingPlatform.slnx`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `Company` aggregate, schema, persistence wiring, the `Application` change + its ripple, and audit constants. **All user stories depend on this phase.**

### Domain

- [ ] T002 [P] Create `Company` aggregate in `src/FundingPlatform.Domain/Entities/Company.cs`: properties `Id, ApplicantId, Name, ArchivedAt, CreatedAt, UpdatedAt, RowVersion`; ctor `Company(int applicantId, string name)` (trim/required/≤200, `ArchivedAt=null`, stamp timestamps); `Rename(string)` (trim/required/≤200, no-op if equal-after-trim, bump `UpdatedAt`); `Archive()`/`Unarchive()` (set/clear `ArchivedAt`); `bool IsActive => ArchivedAt is null`. Use the `Data[ValidationReasonKey]` reason-discriminator pattern from `Application.SetCompanyName` for invariant failures (per data-model.md).
- [ ] T003 [P] Create `ICompanyRepository` in `src/FundingPlatform.Domain/Repositories/ICompanyRepository.cs` with the signatures in contracts/interfaces.md (`GetActiveByApplicantAsync`, `GetAllByApplicantAsync`, `GetActiveByIdForApplicantAsync`, `GetByIdAsync`, `CountActiveExceptAsync`, `AddAsync`, `SaveChangesAsync`).
- [ ] T004 Add `company.*` audit constants to `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`: `ActionCompanyCreate="company.create"`, `ActionCompanyRename="company.rename"`, `ActionCompanyArchive="company.archive"`, `ActionCompanyUnarchive="company.unarchive"`, `TargetTypeCompany="company"`.

### Application change + ripple (D7)

- [ ] T005 In `src/FundingPlatform.Domain/Entities/Application.cs`: add `int? CompanyId { get; private set; }`; change the ctor to `Application(int applicantId, int groupId, int companyId, string companyNameSnapshot)` (set `CompanyId`, snapshot name via existing trim/≤200 path); add `SetCompany(int companyId, string nameSnapshot)` guarded by `EnsureNotFrozen()` (set `CompanyId` + re-copy snapshot + bump `UpdatedAt`). Keep `SetCompanyName` as a private snapshot helper or inline it; remove it from the applicant free-text path.
- [ ] T006 Update all existing `new Application(...)` and `SetCompanyName(...)` call sites to the new ctor/`SetCompany` signature across `src/` and `tests/` (compile-driven sweep; covers `ApplicationService`, any factories, and unit/integration test builders). This is the ripple flagged in plan.md/quickstart.md.

### Persistence (Schema-First, D9)

- [ ] T007 [P] Create `src/FundingPlatform.Database/Tables/dbo.Companies.sql` exactly per data-model.md: PK, `FK_Companies_Applicants` (NO ACTION), `IX_Companies_ApplicantId`, filtered `UX_Companies_ApplicantId_Name (ApplicantId, Name) WHERE ArchivedAt IS NULL`, `RowVersion`, `DF_Companies_CreatedAt`.
- [ ] T008 Edit `src/FundingPlatform.Database/Tables/dbo.Applications.sql`: add nullable `[CompanyId] INT NULL` after `[CompanyName]`, `CONSTRAINT [FK_Applications_Companies] ... ON DELETE NO ACTION` (inline — nullable, no backfill), and `CREATE NONCLUSTERED INDEX [IX_Applications_CompanyId]`.
- [ ] T009 [P] Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/CompanyConfiguration.cs` mirroring `FundConfiguration` (table, key, `Name` required/≤200, `ArchivedAt` optional, `CreatedAt` default `GETUTCDATE()`, `RowVersion.IsRowVersion()`, both indexes).
- [ ] T010 Edit `src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicationConfiguration.cs`: map `CompanyId` (`builder.Property(a => a.CompanyId)`, `HasOne<Company>().WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.NoAction)`, `HasIndex(...).HasDatabaseName("IX_Applications_CompanyId")`).
- [ ] T011 Add `public DbSet<Company> Companies => Set<Company>();` to `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` (config auto-registers via `ApplyConfigurationsFromAssembly`).
- [ ] T012 Implement `CompanyRepository` in `src/FundingPlatform.Infrastructure/Persistence/Repositories/CompanyRepository.cs` over `AppDbContext` (per T003 contract) and register it in DI (mirror where `IFundRepository`/sibling repos are registered).

### Foundational tests

- [ ] T013 [P] Unit tests `tests/FundingPlatform.Tests.Unit/CompanyTests.cs`: name trim/required/≤200, rename no-op, archive/unarchive toggles `IsActive`, snapshot-freeze interaction on `Application.SetCompany` after submit throws.

**Checkpoint**: Domain + schema + persistence compile; `dotnet build` and `dotnet test tests/FundingPlatform.Tests.Unit` green.

---

## Phase 3: User Story 2 — Administrator manages an applicant's companies (Priority: P1)

**Goal**: Admins create a Solicitante with ≥1 company, and add/rename/archive/unarchive afterward (with the last-active floor); applicants cannot manage companies.

**Independent test**: As admin, create a Solicitante (≥1 company required), then add a second, rename one, archive one, attempt to archive the last active (blocked); confirm an applicant has no company-management surface.

### Application layer

- [ ] T014 [P] [US2] Create `CompanyDto` in `src/FundingPlatform.Application/Admin/Users/DTOs/CompanyDto.cs` (`int Id, string Name, bool IsArchived`).
- [ ] T015 [US2] Add `IReadOnlyList<string> CompanyNames` to `CreateUserRequest` (`src/FundingPlatform.Application/Admin/Users/DTOs/CreateUserRequest.cs`) and `IReadOnlyList<CompanyDto> Companies` to `UserDetailDto` (`.../DTOs/UserDetailDto.cs`).
- [ ] T016 [P] [US2] Create `ICompanyAdministrationService` + `CompanyMutationResult` in `src/FundingPlatform.Application/Admin/Companies/ICompanyAdministrationService.cs` (`ListAsync`, `AddAsync`, `RenameAsync`, `ArchiveAsync`, `UnarchiveAsync` per contracts/interfaces.md).

### Infrastructure

- [ ] T017 [US2] Implement `CompanyAdministrationService` in `src/FundingPlatform.Infrastructure/Services/CompanyAdministrationService.cs` (mirrors `FundService`; namespace `…Services` to avoid the type-vs-namespace clash, per the spec-036 gotcha). Each verb: app-level normalized active-name uniqueness pre-check (NFD+strip+lower-es, D3) → mutate → `IAdminAuditWriter.WriteAsync(AdminAuditEvent.Record(...))` with the D10 payloads → single `SaveChangesAsync`. `ArchiveAsync` enforces the last-active floor via `CountActiveExceptAsync` (D5). `UnarchiveAsync` blocks active-name collisions. Register in DI.
- [ ] T018 [US2] Extend `UserAdministrationService.CreateUserAsync` (`src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs`): for the Applicant branch, after building the `Applicant`, add a `Company` row per `request.CompanyNames` (trim, skip blanks, dedupe within request, uniqueness pre-check) and write a `company.create` audit per company — committed in the **same** `SaveChangesAsync` as the Applicant (no separate transaction; spec-036 gotcha). `UpdateUserAsync` is unchanged (companies edited via sub-routes).

### Web

- [ ] T019 [US2] Add company sub-route actions to `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs`: `POST {id}/Companies/Add`, `POST {id}/Companies/{companyId}/Rename`, `POST {id}/Companies/{companyId}/Archive`, `POST {id}/Companies/{companyId}/Unarchive` (each: resolve `{id}`→applicant, assert `{companyId}` belongs to that applicant → else 404 no-disclosure, `ValidateAntiForgeryToken`, call service, surface es-CR via toast/inline). In Create POST, validate ≥1 non-empty company when `Role==Applicant` (es-CR) and pass `CompanyNames`; in Edit GET, load `UserDetailDto.Companies`.
- [ ] T020 [P] [US2] Add `Companies` to `AdminUserCreateViewModel` (repeatable name inputs) and `AdminUserEditViewModel` (companies list) in `src/FundingPlatform.Web/ViewModels/Admin/`.
- [ ] T021 [US2] Update `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml`: add an Applicant-only `companiesField` block (repeatable inputs + add/remove JS) wired into the existing role-toggle `updateVisibility()` (show only when `role==='Applicant'`).
- [ ] T022 [US2] Update `src/FundingPlatform.Web/Views/Admin/Users/Edit.cshtml`: add an "Empresas" management card (list active+archived with rename / archive / unarchive actions posting to the sub-routes + an "Agregar empresa" form), Applicant-only.
- [ ] T023 [P] [US2] Add `AdminCompaniesResources` es-CR strings in `src/FundingPlatform.Web/Resources/` for the labels/messages in contracts/interfaces.md (duplicate, archive-last, unarchive-collision, ≥1-required, add/rename/archive/unarchive confirmations).

### Seed + tests

- [ ] T024 [US2] Seed demo companies in `src/FundingPlatform.Infrastructure/Identity/IdentityConfiguration.cs` (`SeedUsersAsync`): two active companies for `applicant@programa-semilla.test` (e.g. `Acme Consulting S.A.`, `TechCorp Ltda.`), idempotent (`!Companies.Any(c => c.ApplicantId == applicant.Id)`), in a `SaveChanges`.
- [ ] T025 [US2] Integration tests `tests/FundingPlatform.Tests.Integration/CompanyAdministrationTests.cs` (real DB): add, rename (+no-op), archive, unarchive, **floor block on last active**, **duplicate active-name block**, **unarchive collision block**, audit rows written; create-user-with-companies attaches ≥1 and rejects zero.
- [ ] T026 [P] [US2] E2E `tests/FundingPlatform.Tests.E2E/Tests/AdminCompanyManagementTests.cs` + `PageObjects/AdminUserCompaniesPage.cs`: admin creates applicant with ≥1 company, adds a second, renames, archives, last-active archive blocked; applicant has no management surface. `[data-testid]` hooks throughout.

**Checkpoint**: US2 independently demoable; integration green; filtered E2E `AdminCompanyManagement` green.

---

## Phase 4: User Story 1 — Applicant selects a company on submission (Priority: P1)

**Goal**: The free-text company field becomes a controlled dropdown of the applicant's active companies (single auto-select / multi explicit-choice / zero blocked), validated server-side.

**Independent test**: Applicant with one company → auto-selected, creates without choosing; applicant with multiple → must choose; forged/cross-applicant `CompanyId` rejected server-side.

- [ ] T027 [US1] Change `CreateApplicationCommand` (`src/FundingPlatform.Application/Applications/Commands/CreateApplicationCommand.cs`) from `(ApplicantId, CompanyName, GroupId)` to `(ApplicantId, CompanyId, GroupId)`.
- [ ] T028 [US1] Update `ApplicationService.CreateApplicationAsync` (`src/FundingPlatform.Application/Services/ApplicationService.cs`): inject/resolve `ICompanyRepository.GetActiveByIdForApplicantAsync(cmd.CompanyId, cmd.ApplicantId)`; null → `UserFacingError` (new `UserFacingErrorCode.CompanyInvalid`, es-CR) with no disclosure (FR-018/019); else construct `new Application(cmd.ApplicantId, cmd.GroupId, company.Id, company.Name)`. Add the new error code + es-CR mapping in the error translator.
- [ ] T029 [US1] Update `CreateApplicationViewModel` (`src/FundingPlatform.Web/ViewModels/CreateApplicationViewModel.cs`): replace free-text `CompanyName` with `int? CompanyId` (`[Required]`, es-CR), `IReadOnlyList<SelectListItem> Companies`, `bool HasNoCompanies`, `bool IsSingleCompany` (mirror the existing `GroupId` 0/1/many fields).
- [ ] T030 [US1] Update `ApplicationController` (`src/FundingPlatform.Web/Controllers/ApplicationController.cs`): add `ResolveActiveCompaniesAsync(userId)` (mirrors `ResolveEligibleGroupsAsync`); populate company fields in Create GET; in Create POST validate posted `CompanyId` ∈ active set (server-side, FR-018/019), build `CreateApplicationCommand(applicantId, CompanyId, GroupId)`, re-populate on redisplay.
- [ ] T031 [US1] Update `src/FundingPlatform.Web/Views/Application/Create.cshtml`: replace the company-name `<input>` with the 0/1/many rendering — single → hidden + disabled read-only; multiple → `<select asp-for="CompanyId" asp-items="Model.Companies" data-searchable>` with `— Seleccione una empresa —`; zero → block + es-CR message to contact admin. Keep `data-testid` hooks (`application-create-company`, `-error`, `-readonly`).
- [ ] T032 [P] [US1] E2E `tests/FundingPlatform.Tests.E2E/Tests/ApplicantCompanySelectionTests.cs`: single-company auto-select creates; multi-company requires choice; zero-company blocked; forged `CompanyId` (another applicant's / archived) rejected server-side (HTTP-level POST). SQL-seed throwaway single/zero-company applicants as needed; extend the application-create page object for the company select.

**Checkpoint**: US1 + US2 = usable MVP; filtered E2E `ApplicantCompanySelection` green.

---

## Phase 5: User Story 3 — Historical company names are preserved (Priority: P2)

**Goal**: Snapshot freezes at submission and re-copies on draft re-select; renaming a company never rewrites prior applications.

**Independent test**: Create under a company, rename it, confirm the existing application shows the old name and a new one shows the new name; change the company on a draft and confirm the snapshot updates.

- [ ] T033 [US3] Add the `"CompanyId"` field-key case to `AutosaveFieldHandler.ApplyFieldMutation` (`src/FundingPlatform.Infrastructure/Services/AutosaveFieldHandler.cs`): parse int (reject non-int), look up `Companies` by `(id, application.ApplicantId, ArchivedAt IS NULL)` → null throws es-CR-mapped ownership/active failure, else `application.SetCompany(company.Id, company.Name)`. Remove the old `"CompanyName"` field-key. Inject the company lookup (`ICompanyRepository` or direct `_db.Companies`, matching the handler's style).
- [ ] T034 [US3] Update `src/FundingPlatform.Web/Views/Application/Edit.cshtml`: replace the autosave company-name input with a `<select data-searchable>` of active companies autosaved under field-key `CompanyId` (preserve etag/autosave wiring + `data-testid`).
- [ ] T035 [US3] Enforce FR-020 at submit: in the submit path (`src/FundingPlatform.Domain/Entities/Application.cs` `Submit()` and/or the submit method in `src/FundingPlatform.Application/Services/ApplicationService.cs`), verify the linked company is still active; if archived, throw/return an es-CR-mapped error requiring re-selection (`La empresa seleccionada fue archivada…`). The active check needs company state, so do the lookup in `ApplicationService` (domain `Submit()` stays pure) and surface the es-CR message via the existing error-translator path.
- [ ] T036 [P] [US3] E2E `tests/FundingPlatform.Tests.E2E/Tests/CompanyHistoryPreservationTests.cs`: create-under-company → admin rename → existing app shows old name, new app shows new name; draft re-select updates snapshot; submit-with-archived-company blocked until re-select.

**Checkpoint**: filtered E2E `CompanyHistoryPreservation` green.

---

## Phase 6: User Story 4 — Bulk import assigns the first company (Priority: P2)

**Goal**: The batch CSV gains a required trailing `Nombre de la empresa` column; each created applicant gets their first company.

**Independent test**: Download template (column present); import a file; each created applicant has exactly that company; blank/oversize company cells reported per-row.

- [ ] T037 [US4] Edit `src/FundingPlatform.Application/Admin/Users/Batch/BatchUserCsvColumns.cs`: add `NombreEmpresa="Nombre de la empresa"` as the last `Ordered` entry, `Count=11`.
- [ ] T038 [P] [US4] Add `string? NombreEmpresa` to `BatchUserImportRow` (`.../Batch/BatchUserImportRow.cs`); add `CompanyNameBlank="Falta el nombre de la empresa."` and `CompanyNameTooLong="El nombre de la empresa supera los 200 caracteres."` to `BatchUserRowReasons` (`.../Batch/BatchUserRowReasons.cs`).
- [ ] T039 [US4] In `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs`: parse `Cell(cells,10)` into `BatchUserImportRow.NombreEmpresa` (Batch POST), and add `"Empresa ABC"` to the `BatchTemplate` example row. In `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` `CreateUsersBatchAsync`, validate the company cell (required/≤200 → es-CR reason) and set `CreateUserRequest.CompanyNames = [trimmed]` (attaches via T018).
- [ ] T040 [US4] Integration test `tests/FundingPlatform.Tests.Integration/BatchUserCompanyTests.cs` (or extend `BatchUserCreationTests`): all-valid attaches one company each; blank company → row errored; >200 chars → row errored; template header is the 11-column order.
- [ ] T041 [P] [US4] E2E `tests/FundingPlatform.Tests.E2E/Tests/BatchUserCompanyTests.cs` (or extend `BatchUserCreateTests`): template download shows the new column; import creates applicants with their company; mixed file reports blank-company rows.

**Checkpoint**: filtered E2E batch class green.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T042 [P] Verify read surfaces still render the snapshot unchanged (Index/Details/Review/FundingAgreement Details + PDF cover) — grep `CompanyName` usages; no behavioral change expected (data-model.md/Application Display Sites).
- [ ] T043 [P] es-CR copy pass: confirm no English-only strings introduced; all messages from contracts/interfaces.md present in resources/markup.
- [ ] T044 Run filtered E2E delivery bar: `ApplicantCompanySelection`, `AdminCompanyManagement`, `CompanyHistoryPreservation`, batch company class — all green (quickstart.md). Run the full suite only if the `Application`-ctor / autosave-field-key ripple is suspected to break shared application-create/submit tests.
- [ ] T045 Update `specs/037-applicant-companies/` Evolution notes if any deviation from research.md emerged during implementation; prepare the CLAUDE.md "Recent Changes" entry for the ship commit.

---

## Dependencies & Execution Order

- **Phase 2 (Foundational)** blocks everything. T002–T013 in order (T002/T003/T007/T009/T013 are `[P]`; T005→T006 sequential; T008/T010/T011/T012 after T002/T007).
- **US2 (Phase 3)** depends on Foundational; precedes US1 (companies must exist to be selectable).
- **US1 (Phase 4)** depends on Foundational + US2 (uses seeded/managed companies).
- **US3 (Phase 5)** depends on US1 (selection must exist before re-select/freeze).
- **US4 (Phase 6)** depends on Foundational + the T018 attach path (US2); independent of US1/US3 otherwise.
- **Polish (Phase 7)** last.

**MVP scope**: Foundational + US2 + US1 (both P1). US3 + US4 are P2 increments.

**Parallel opportunities**: within Foundational, T002/T003/T007/T009/T013; within US2, T014/T016/T020/T023/T026; E2E classes per story (T026, T032, T036, T041) are independent once their phase code lands.

## Format validation

All tasks use `- [ ] Tnnn [P?] [USn?] description + file path`. Setup/Foundational/Polish carry no story label; US phases carry `[US1]`/`[US2]`/`[US3]`/`[US4]`.
