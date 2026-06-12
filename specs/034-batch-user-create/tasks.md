# Tasks: Batch user creation (bulk applicant provisioning via CSV)

**Input**: Design documents from `specs/034-batch-user-create/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/contracts.md

**Tests**: INCLUDED — Constitution III makes Playwright E2E non-negotiable per story; the team also covers pure logic with unit tests and the service with real-DB-shaped integration tests (CLAUDE.md delivery bar).

**Organization**: Grouped by user story (US1 P1, US2 P1, US3 P2) so each is independently implementable and testable.

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: parallelizable (different files, no incomplete-task dependency)
- **[Story]**: US1/US2/US3 on story-phase tasks only

## Path Conventions

Clean Architecture layers under `src/` (`FundingPlatform.Application` / `.Infrastructure` / `.Web`) and tests under `tests/` (`.Tests.Unit` / `.Tests.Integration` / `.Tests.E2E`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding shared by all stories.

- [ ] T001 [P] Create the batch folder and the CSV header contract `BatchUserCsvColumns` (canonical es-CR header names + order: `Grupo,Proceso,Fondo,Nombre,Apellido 1,Apellido 2,Email,Teléfono,Cédula,Código de usuario`; plus a header-match helper that trims, is case/accent-insensitive, and strips a leading UTF-8 BOM on the first column) in `src/FundingPlatform.Application/Admin/Users/Batch/BatchUserCsvColumns.cs`
- [ ] T002 [P] Add Spec-034 es-CR resource constants (page title/help, template-download label, file input/submit labels, the four file-level error messages, the per-row reason messages, and result headings/row format) to `src/FundingPlatform.Web/Resources/AdminUsersResources.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Pure parsing/normalization, the transient DTOs, the service contract, and the upload entry point — all stories depend on these. **No story can start until this phase is done.**

- [ ] T003 [P] Implement the in-house RFC-4180 subset reader `CsvParser` (comma delimiter; double-quote-wrapped fields with `""` escaping; embedded commas/newlines inside quotes; CRLF/LF; leading UTF-8 BOM; ignores trailing blank lines; returns header `string[]` + data rows `string[][]`) in `src/FundingPlatform.Application/Admin/Users/Batch/CsvParser.cs`
- [ ] T004 [P] Implement `PhoneNormalizer.Normalize(string?) -> string?` (null/blank → null; split on `/ , ; |` and whitespace-between-digit-groups, take first token; strip non-digits; drop a leading `506` when result length > 8; empty → null) in `src/FundingPlatform.Application/Admin/Users/Batch/PhoneNormalizer.cs`
- [ ] T005 [P] Add the transient DTOs `BatchUserImportRow` (RowNumber + 10 raw cells), `BatchUserCreateOutcome` (RowNumber, KeyField, Succeeded, Reason?), and `BatchUserCreateResult` (Succeeded[], Errored[]) in `src/FundingPlatform.Application/Admin/Users/Batch/` (one file each: `BatchUserImportRow.cs`, `BatchUserCreateOutcome.cs`, `BatchUserCreateResult.cs`)
- [ ] T006 Add `Task<BatchUserCreateResult> CreateUsersBatchAsync(IReadOnlyList<BatchUserImportRow> rows, string actorUserId, CancellationToken ct)` to the service contract in `src/FundingPlatform.Application/Admin/Users/IUserAdministrationService.cs` (depends on T005)
- [ ] T007 Add an empty `CreateUsersBatchAsync` implementation stub (returns all-errored placeholder) in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` so the solution compiles; real logic lands in US1/US2/US3 (depends on T006)
- [ ] T008 Add the view models `AdminUserBatchUploadViewModel` and `AdminUserBatchResultViewModel` (+ `AdminUserBatchResultRow` record) in `src/FundingPlatform.Web/ViewModels/Admin/AdminUserBatchViewModels.cs`
- [ ] T009 Add the controller surface to `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs`: `[HttpGet("Batch")] Batch()`, `[HttpGet("Batch/Template")] BatchTemplate()` (streams the `BatchUserCsvColumns` header + one example row as `text/csv; charset=utf-8`, attachment `plantilla-usuarios.csv`), and `[HttpPost("Batch")] [ValidateAntiForgeryToken] Batch(IFormFile? csv, ...)` that performs **file-level validation** (in-memory byte cap; `CsvParser` parse; header match; 1..200 data rows) then calls `CreateUsersBatchAsync` and renders the result; on any file-level failure re-render `Batch` with the first matching es-CR message and create nothing (depends on T001, T003, T008)
- [ ] T010 [P] Create the upload view `src/FundingPlatform.Web/Views/Admin/Users/Batch.cshtml` (file input, template-download link, CSV-only + 200-row es-CR hints, file-level error region) and a "Crear por lote" entry point from the users list `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml`
- [ ] T011 [P] Create the result view skeleton `src/FundingPlatform.Web/Views/Admin/Users/BatchResult.cshtml` (renders `AdminUserBatchResultViewModel`: total count + succeeded/errored sections; sections filled per story) (depends on T008)
- [ ] T012 [P] Unit tests for the CSV parser (BOM, quoted field with comma, quoted field with embedded newline, CRLF vs LF, trailing blank line, quote-escape `""`) in `tests/FundingPlatform.Tests.Unit/Batch/CsvParserTests.cs` (depends on T003)
- [ ] T013 [P] Unit tests for the phone normalizer (`"8888-1111"`, `"506 8888 1111"`, `"+506 88881111"`, `"8888-1111 / 7777-2222"`, blank/null) in `tests/FundingPlatform.Tests.Unit/Batch/PhoneNormalizerTests.cs` (depends on T004)

**Checkpoint**: Solution builds; upload page renders; template downloads; parser + normalizer unit tests green. No rows are created yet.

---

## Phase 3: User Story 1 — Admin bulk-creates applicants from a CSV (Priority: P1) 🎯 MVP

**Goal**: An all-valid CSV creates one invited Solicitante per row (account + applicant + group membership), each receiving the spec-033 set-password invitation.

**Independent Test**: Upload an all-valid CSV; confirm N accounts exist with UserCode + group membership and N invitation emails were captured.

- [ ] T014 [US1] Implement the happy-path body of `CreateUsersBatchAsync` in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs`: for each row, normalize phone (`PhoneNormalizer`), canonicalize cédula física via `Identification.TryFrom(IdentificationType.CedulaFisica, …)`, compose `LastName = (Apellido1 + " " + Apellido2).Trim()`, resolve `Grupo` by name to a `Group.Id`, build `CreateUserRequest` (role `Applicant`, `GroupIds=[groupId]`, canonical LegalId, `CedulaFisica`, trimmed UserCode), call `CreateUserAsync`, and record a succeeded `BatchUserCreateOutcome` (carrying email) on success (depends on T007; full validation/chain added in US2/US3)
- [ ] T015 [US1] In `AdminUsersController.Batch` (POST), after `CreateUsersBatchAsync`, iterate `result.Succeeded` and call the existing `IssueAndSendInvitationAsync(email, ct)` per created user (best-effort, unchanged semantics), then render `BatchResult` with the populated view model in `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` (depends on T009, T014)
- [ ] T016 [P] [US1] Fill the **succeeded** section of `src/FundingPlatform.Web/Views/Admin/Users/BatchResult.cshtml` (count + per-row "Fila {n}: {email} creado") (depends on T011)
- [ ] T017 [US1] Integration test (EF InMemory, real-DB-shaped) `BatchUserCreationTests.AllValid_CreatesInvitedApplicantsWithGroupAndCode`: seed Fund→Process→Groups, run `CreateUsersBatchAsync` with all-valid rows, assert N applicants persisted with canonical LegalId + UserCode + one `UserGroupMembership` each, and `Succeeded.Count == rows.Count` in `tests/FundingPlatform.Tests.Integration/Application/BatchUserCreationTests.cs` (depends on T014)
- [ ] T018 [P] [US1] Create the E2E page object `AdminBatchUsersPage` (GoTo `/Admin/Users/Batch`, download template, `SetInputFilesAsync` the CSV, submit, read result counts/rows) in `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminBatchUsersPage.cs`
- [ ] T019 [US1] E2E `BatchUserCreateTests.AllValid_CreatesUsers_AndSendsInvitations`: sign in as admin, upload an all-valid temp CSV (Groups Norte/Sur under "Migración inicial"/"Fondo General"), assert the result shows N succeeded, the users appear in `/Admin/Users`, and N invitation emails are captured via `MailCapture` in `tests/FundingPlatform.Tests.E2E/Tests/Admin/BatchUserCreateTests.cs` (depends on T015, T018)

**Checkpoint**: US1 independently deliverable — an all-valid file provisions a cohort with invitations.

---

## Phase 4: User Story 2 — Per-row validation with a succeeded/errored report (Priority: P1)

**Goal**: Invalid rows are skipped (never block valid rows) and reported with es-CR reasons; succeeded + errored counts equal the data-row count.

**Independent Test**: Upload a mixed CSV (blank email, duplicate código, bad cédula); valid rows created, each bad row reported with a reason.

- [ ] T020 [US2] Add row-level validation in `CreateUsersBatchAsync` before `CreateUserAsync`: required cells (Nombre, Apellido 1, Email, Cédula, Código, Grupo, Proceso, Fondo), email shape, cédula física invalid, UserCode length > 50 → errored `BatchUserCreateOutcome` with the matching es-CR reason; valid rows proceed (in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs`) (depends on T014)
- [ ] T021 [US2] Add the in-file duplicate pre-scan (normalized Email / canonical Cédula / trimmed Código; first occurrence proceeds, later occurrences errored "duplicado en el archivo") in `CreateUsersBatchAsync` in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` (depends on T020)
- [ ] T022 [US2] Map `CreateUserAsync` `DomainError.Code` results (`EMAIL_IN_USE`, `LEGAL_ID_IN_USE`, `USER_CODE_IN_USE`, defensive fallback) to es-CR row reasons → errored outcomes in `CreateUsersBatchAsync` in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` (depends on T020)
- [ ] T023 [P] [US2] Fill the **errored** section of `src/FundingPlatform.Web/Views/Admin/Users/BatchResult.cshtml` (count + per-row "Fila {n}: {keyField} — {reason}") and render the file-level error region in `Batch.cshtml` (depends on T011, T010)
- [ ] T024 [US2] Integration test `BatchUserCreationTests.Mixed_CreatesValid_SkipsInvalid_WithReasons` (blank-email, duplicate-código in-file, duplicate vs existing DB user, bad-cédula, oversized-código): assert valid rows created, each bad row errored with the expected reason, and `Succeeded.Count + Errored.Count == rows.Count` in `tests/FundingPlatform.Tests.Integration/Application/BatchUserCreationTests.cs` (depends on T020, T021, T022)
- [ ] T025 [US2] E2E `BatchUserCreateTests.Mixed_ReportsSucceededAndErrored`: upload a mixed CSV, assert the report lists the valid rows as succeeded and each invalid row as errored with a visible es-CR reason, and the bad rows created no users in `tests/FundingPlatform.Tests.E2E/Tests/Admin/BatchUserCreateTests.cs` (depends on T019, T023)

**Checkpoint**: US2 deliverable — robust partial-success reporting on top of US1.

---

## Phase 5: User Story 3 — Group → Proceso → Fondo chain integrity (Priority: P2)

**Goal**: A row whose Grupo/Proceso/Fondo names don't form a coherent spec-029 chain is skipped and reported; coherent rows succeed.

**Independent Test**: Upload a row whose Grupo exists but sits under a different Proceso/Fondo than named; it is skipped with a chain reason while coherent rows succeed.

- [ ] T026 [US3] Replace the US1 simple group-name lookup in `CreateUsersBatchAsync` with full chain resolution: resolve `Fondo`/`Proceso`/`Grupo` by name (each 0/1 via unique names) and validate `group.ProcessId == process.Id && process.FundId == fund.Id`; on any unknown name or broken link → errored with the specific es-CR reason (not-found vs chain-mismatch); on success use `group.Id` for `GroupIds` in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` (depends on T020)
- [ ] T027 [US3] Integration test `BatchUserCreationTests.WrongChain_RowSkipped`: seed two Funds/Processes so a Group exists under one chain; a row naming that Group with a different Proceso/Fondo is errored (chain mismatch) while a coherent row succeeds in `tests/FundingPlatform.Tests.Integration/Application/BatchUserCreationTests.cs` (depends on T026)
- [ ] T028 [US3] E2E `BatchUserCreateTests.ChainMismatch_RowSkipped`: upload a CSV mixing a coherent row with a wrong-chain row; assert the wrong-chain row is errored with a chain reason and the coherent row is created in `tests/FundingPlatform.Tests.E2E/Tests/Admin/BatchUserCreateTests.cs` (depends on T025, T026)

**Checkpoint**: US3 deliverable — chain guard prevents mis-filed memberships.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: File-level rejection completeness, template correctness, es-CR sweep, delivery gate.

- [ ] T029 [P] Complete + harden file-level validation in `AdminUsersController.Batch` (POST): non-CSV/unreadable, header mismatch, zero data rows, > 200 rows, byte cap — each returning its first-matching es-CR message and creating nothing in `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` (depends on T009)
- [ ] T030 [P] Integration/E2E for file-level rejection (SC-005): a >200-row file and a header-mismatched file each create nothing and show one es-CR message — add to `tests/FundingPlatform.Tests.E2E/Tests/Admin/BatchUserCreateTests.cs` (depends on T029)
- [ ] T031 [P] Test the template download (`GET /Admin/Users/Batch/Template`) returns the exact header + correct content-type/disposition in `tests/FundingPlatform.Tests.Integration/Application/BatchUserCreationTests.cs` or an E2E assertion (depends on T009)
- [ ] T032 [P] es-CR copy sweep: confirm no English literals in new views/resources/messages; verify accent-insensitive header match against an accented variant (depends on T023)
- [ ] T033 Run the filtered E2E delivery gate (`dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~BatchUserCreate"`) plus the new Unit + Integration filters; confirm green per quickstart.md (depends on T019, T025, T028, T030)
- [ ] T034 Update `CLAUDE.md` Recent Changes with the delivered 034 surface + test counts (after T033 is green)

---

## Dependencies & Execution Order

- **Setup (P1)** → **Foundational (P2)** → **US1 (P3)** → **US2 (P4)** → **US3 (P5)** → **Polish (P6)**.
- US2 and US3 both build on the US1 orchestration body (T014); US2 (validation/dedupe) and US3 (chain) touch the same `CreateUsersBatchAsync` method, so do them sequentially (US2 then US3), not in parallel, to avoid edit collisions.
- Story independence: each story is independently *testable* (US1 all-valid, US2 mixed, US3 wrong-chain) even though US2/US3 extend US1's method.

## Parallel Opportunities

- Phase 1: T001 ∥ T002.
- Phase 2: T003 ∥ T004 ∥ T005 (then T006→T007); views T010 ∥ T011; unit tests T012 ∥ T013.
- Within US1: T016 (view) ∥ T018 (page object) while T014/T015 (service/controller) proceed.
- Phase 6: T029 ∥ T031 ∥ T032 (T030 after T029; T033 last).

## Implementation Strategy

- **MVP = US1** (all-valid bulk create + invitations). Ship/validate that first.
- Add **US2** (per-row report) for real-world resilience, then **US3** (chain guard).
- Each checkpoint is independently demoable; the filtered E2E per story is the delivery gate (CLAUDE.md).
