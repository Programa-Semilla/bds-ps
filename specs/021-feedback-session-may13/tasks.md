---

description: "Task list for feature 021 — Feedback Session May-13"
---

# Tasks: Feedback Session May-13 (021)

**Input**: Design documents from `/specs/021-feedback-session-may13/`
**Prerequisites**: spec.md ✓ plan.md ✓ research.md ✓ data-model.md ✓ contracts/ ✓ quickstart.md ✓

**Tests**: Constitution III mandates Playwright E2E tests for every user story. Unit + integration tests included where they cover domain invariants or background services. Mocks forbidden in integration tests (CLAUDE.md project rule).

**Organization**: Tasks grouped by user story (US1–US8). MVP scope = US1 + US2 + US3 (all P1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable — different file, no dependency on incomplete tasks
- **[Story]**: maps task to spec.md user story (US1–US8)
- Setup/Foundational/Polish tasks carry no story label

## Path Conventions

Single-solution layered structure (Clean Architecture). Paths:

- Domain: `src/FundingPlatform.Domain/`
- Application: `src/FundingPlatform.Application/`
- Infrastructure: `src/FundingPlatform.Infrastructure/`
- Web: `src/FundingPlatform.Web/`
- Database (dacpac): `src/FundingPlatform.Database/`
- Tests: `tests/FundingPlatform.Tests.{Unit,Integration,E2E}/`

---

## Phase 1: Setup

**Purpose**: Workspace scaffolding shared by every story.

- [ ] T001 Verify `dotnet build FundingPlatform.slnx` succeeds on `021-feedback-session-may13` (baseline).
- [ ] T002 [P] Create empty es-CR resource file extension stub at `src/FundingPlatform.Web/Localization/021.es-CR.resx` (keys added per FR as work lands).
- [ ] T003 [P] Reserve `wwwroot/js/` empty modules: `autosave.js`, `public-code-banner.js`, `supplier-autocomplete.js`, `province-canton-cascade.js`, `input-masks.js`, `password-eye-toggle.js`, `password-strength-legend.js` under `src/FundingPlatform.Web/wwwroot/js/`. Each starts as a single-line ES module.
- [ ] T004 [P] Add a top-of-file comment block to each new `.cs` Phase-2 entity referencing `specs/021-feedback-session-may13/data-model.md` for traceability.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema + domain + cross-cutting infrastructure. **Blocks every user story phase.**

### Schema (dacpac — `src/FundingPlatform.Database/Tables/` + `PostDeployment/`)

- [ ] T005 Create `dbo.Processes.sql` per data-model.md (cols: `Id`, `Name`, `Status`, `SolicitudWindowDays?`, `RevisionWindowDays?`, `FacturacionWindowDays?`, `CreatedAt`, `ClosedAt?`, `RowVersion`).
- [ ] T006 Create `dbo.Plantillas.sql` per data-model.md.
- [ ] T007 Create `dbo.ProcessPlantillas.sql` per data-model.md (UNIQUE on `ProcessId`).
- [ ] T008 Create `dbo.PlantillaImpactTemplates.sql` (many-to-many join `PlantillaId` ↔ `ImpactTemplateId`).
- [ ] T009 [P] Create `dbo.Provinces.sql` per data-model.md.
- [ ] T010 [P] Create `dbo.Cantons.sql` (FK to `Provinces.Id`).
- [ ] T011 [P] Create `dbo.PasswordResetTokens.sql` per data-model.md.
- [ ] T012 Alter `dbo.Groups.sql`: add `ProcessId INT NOT NULL FK → Processes.Id`.
- [ ] T013 Alter `dbo.Applications.sql`: add `PublicCode CHAR(9) NOT NULL UNIQUE` (CHECK regex), `ImpactTemplateId INT NULL FK → ImpactTemplates.Id`, `RemindersSentMask TINYINT NOT NULL DEFAULT 0`, `StageEnteredAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()`. Verify/add `DeletedAt DATETIME2(0) NULL` if missing.
- [ ] T014 Alter `dbo.Items.sql`: drop `ImpactId` column outright (NFR-001 — no production data).
- [ ] T015 Alter `dbo.ImpactParameterValues.sql`: re-target FK from `ImpactId` to `ApplicationId`; drop legacy `dbo.Impacts.sql` after.
- [ ] T016 [P] Alter `dbo.SupplierBranches.sql`: add `ContactPersonName NVARCHAR(120) NULL`, `ProvinceId INT NULL FK → Provinces.Id`, `CantonId INT NULL FK → Cantons.Id`.
- [ ] T017 [P] Alter `dbo.AspNetUsers.sql`: add `CodigoPersonal NVARCHAR(40) NULL`.
- [ ] T018 [P] Alter `dbo.SystemConfigurations.sql` post-deploy: seed rows `Stage.Solicitud.WindowDays=14`, `Stage.Revision.WindowDays=10`, `Stage.Facturacion.WindowDays=30`, `Public.Landing.Reglamento.StorageKey=NULL`, `Public.Landing.Ejemplo.StorageKey=NULL`.
- [ ] T019 Create `PostDeployment/01_SeedProvincesCantons.sql` — idempotent MERGE for 7 provinces + ~82 cantones (canonical TSE/MOPT 2020 list, includes Río Cuarto + Monteverde splits).
- [ ] T020 Create `PostDeployment/02_SeedMigracionInicialProcess.sql` — inserts *"Migración inicial"* Process, sets `Groups.ProcessId` for every existing row to its Id.
- [ ] T021 Create `PostDeployment/03_SeedSupplierAdminRole.sql` — inserts `AspNetRoles` row `SupplierAdmin`.
- [ ] T022 Wire dacpac auto-deploy verification: `dotnet run --project src/FundingPlatform.AppHost` must apply T005–T021 cleanly on a fresh container.

### Domain entities, enums, value objects, exceptions

- [ ] T023 [P] Create `src/FundingPlatform.Domain/Enums/ProcessStatus.cs`.
- [ ] T024 [P] Create `src/FundingPlatform.Domain/Enums/StageKind.cs` (`Solicitud`, `Revision`, `Facturacion`).
- [ ] T025 Create `src/FundingPlatform.Domain/Entities/Process.cs` with behavior methods `Close()`, `OverrideStageWindow(StageKind, int?)` per data-model.md.
- [ ] T026 Create `src/FundingPlatform.Domain/Entities/Plantilla.cs` with `AssignTo(Process)` (returns snapshot), `Detach(force, reason)`, `Edit(...)`.
- [ ] T027 Create `src/FundingPlatform.Domain/Entities/ProcessPlantilla.cs` (snapshot, immutable payload).
- [ ] T028 [P] Create `src/FundingPlatform.Domain/Entities/Province.cs`.
- [ ] T029 [P] Create `src/FundingPlatform.Domain/Entities/Canton.cs` (FK to Province).
- [ ] T030 [P] Create `src/FundingPlatform.Domain/Entities/PasswordResetToken.cs` with `Consume()` guard.
- [ ] T031 Update `src/FundingPlatform.Domain/Entities/Group.cs`: add `ProcessId` + navigation.
- [ ] T032 Update `src/FundingPlatform.Domain/Entities/Application.cs`: add `PublicCode` value object, `ImpactTemplateId`, `RemindersSentMask`, `StageEnteredAt`, `Impact` value object child collection (re-parented from Item). Add `SetImpact(impact)`, `Submit()` guard chain per FR-017 / data-model.md.
- [ ] T033 Update `src/FundingPlatform.Domain/Entities/Item.cs`: remove `Impact*` references; ensure quotation-count predicate still callable.
- [ ] T034 [P] Update `src/FundingPlatform.Domain/Entities/SupplierBranch.cs`: add `ContactPersonName`, `ProvinceId`, `CantonId` + cantón-belongs-to-province guard.
- [ ] T035 [P] Update `src/FundingPlatform.Domain/Entities/ApplicationUser.cs`: add `CodigoPersonal`.
- [ ] T036 [P] Update `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`: add new event-kind constants (`ProcessCreated`, `ProcessClosed`, `ProcessStageWindowOverridden`, `PlantillaAssignedToProcess`, `PlantillaForceDetached`, `SupplierAdminDeniedAccess`).
- [ ] T037 Create `src/FundingPlatform.Domain/ValueObjects/PublicCode.cs` (regex-validated, factory via `IPublicCodeGenerator`).
- [ ] T038 Create `src/FundingPlatform.Domain/ValueObjects/Impact.cs` (template + parameter values).
- [ ] T039 [P] Create `src/FundingPlatform.Domain/Exceptions/StageWindowClosedException.cs` (maps to HTTP 422).
- [ ] T040 [P] Create `src/FundingPlatform.Domain/Exceptions/ProcessClosedException.cs`.
- [ ] T041 [P] Create `src/FundingPlatform.Domain/Interfaces/IPublicCodeGenerator.cs`.
- [ ] T042 [P] Create `src/FundingPlatform.Domain/Interfaces/IStageExpiryClock.cs`.

### Application-layer interfaces + cross-cutting

- [ ] T043 [P] Create `src/FundingPlatform.Application/Abstractions/IPasswordResetTokenStore.cs`.
- [ ] T044 [P] Create `src/FundingPlatform.Application/Abstractions/IApplicationQueryFilter.cs` with `ExcludeDeleted(IQueryable<Application>)` extension entry.
- [ ] T045 [P] Create `src/FundingPlatform.Application/Abstractions/IStageExpiryEvaluator.cs` (computes T-72h / T-24h / expired bucket per Application).
- [ ] T046 [P] Create `src/FundingPlatform.Application/Abstractions/IAdminAuditEventWriter.cs` (single seam used by every controller / filter writing audit events).

### Infrastructure — implementations

- [ ] T047 Create `src/FundingPlatform.Infrastructure/PublicCodes/PublicCodeGenerator.cs` (crypto-RNG + base32 alphabet, retry on UNIQUE collision, 3 attempts).
- [ ] T048 [P] Create `src/FundingPlatform.Infrastructure/Identity/PasswordResetTokenStore.cs` (EF-backed, SHA-256 hash, single-use).
- [ ] T049 [P] EF configurations: `src/FundingPlatform.Infrastructure/Persistence/Configurations/` add files for `Process`, `Plantilla`, `ProcessPlantilla`, `Province`, `Canton`, `PasswordResetToken`; update configs for `Group`, `Application`, `Item`, `SupplierBranch`, `ApplicationUser`.
- [ ] T050 [P] Implement `src/FundingPlatform.Infrastructure/Persistence/ApplicationQueryFilter.cs` with `ExcludeDeleted` extension. Wire DI registration.
- [ ] T051 [P] Implement `src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs` (single seam writing audit rows in-transaction).

### Web cross-cutting

- [ ] T052 Create `src/FundingPlatform.Web/Filters/DomainExceptionFilter.cs`: maps `StageWindowClosedException` → 422 with es-CR message; maps `ProcessClosedException` → 422 likewise. Register globally.
- [ ] T053 [P] Create `src/FundingPlatform.Web/Controllers/CantonsApiController.cs` exposing `GET /api/cantons?provinceId={id}` returning `[{ id, name }]` with `Cache-Control: public, max-age=3600`.
- [ ] T054 [P] Create `src/FundingPlatform.Web/Views/Shared/_AutosaveIndicator.cshtml` partial (idle/saving/saved/failed states).
- [ ] T055 [P] Create `src/FundingPlatform.Web/Views/Shared/_StageCountdownBanner.cshtml` partial (open + danger + Vencido states).
- [ ] T056 [P] Create `src/FundingPlatform.Web/Views/Shared/_ProvinceCantonCascade.cshtml` partial wiring `/api/cantons` via `province-canton-cascade.js`.
- [ ] T057 [P] Create `src/FundingPlatform.Web/Views/Shared/_PasswordStrengthLegend.cshtml` partial (FR-027).
- [ ] T058 [P] Create `src/FundingPlatform.Web/Views/Shared/_PasswordEyeToggle.cshtml` partial (FR-026).
- [ ] T059 [P] Implement `src/FundingPlatform.Web/wwwroot/js/input-masks.js` (CR phone `8888-8888`, RFC email).
- [ ] T060 [P] Implement `src/FundingPlatform.Web/wwwroot/js/province-canton-cascade.js`.
- [ ] T061 [P] Implement `src/FundingPlatform.Web/wwwroot/js/password-eye-toggle.js`.
- [ ] T062 [P] Implement `src/FundingPlatform.Web/wwwroot/js/password-strength-legend.js`.
- [ ] T063 [P] Implement `src/FundingPlatform.Web/wwwroot/js/autosave.js` (per-field blur + debounce 300 ms + ETag).
- [ ] T064 [P] Implement `src/FundingPlatform.Web/wwwroot/js/public-code-banner.js` (clipboard copy + tooltip).
- [ ] T065 [P] Implement `src/FundingPlatform.Web/wwwroot/js/supplier-autocomplete.js`.

### Localization baseline

- [ ] T066 [P] Add localization keys to `Localization/021.es-CR.resx`: `Application.Disclaimer.Fx`, `Public.Hero.Cta`, `Public.Hero.Button`, `Account.ForgotPassword.Title`, `Account.ResetPassword.InvalidLink`, `Account.Profile.AdministradoBadge`, `Application.Review.ConfirmButton`, `Application.Submit.MissingFieldsHeader`, `Banner.StageExpiry.RemainingFmt`, `Banner.StageExpiry.Closed`, `Greeting.Pattern`, `Supplier.Search.Placeholder`, `Profile.UpdateSuccess`, `Profile.ChangePasswordSuccess`, `PublicLanding.PlaceholderProximamente`.
- [ ] T067 Remove keys `Bienvenido`, `BienvenidoA`, `BienvenidaA` and every `financiamiento` value from existing `*.resx` files (Funding Agreement PDF template literals exempt — those are non-resource). Update views referencing removed keys to use FR-029 / FR-030 replacements.

### Unit-test scaffolding (per Constitution III + project test rule)

- [ ] T068 [P] Unit tests `tests/FundingPlatform.Tests.Unit/Domain/PublicCodeTests.cs` (regex, alphabet, retry semantics).
- [ ] T069 [P] Unit tests `tests/FundingPlatform.Tests.Unit/Domain/ProcessTests.cs` (close-blocks-when-active, override semantics).
- [ ] T070 [P] Unit tests `tests/FundingPlatform.Tests.Unit/Domain/PlantillaSnapshotTests.cs` (assignment captures snapshot, base edits do not propagate).
- [ ] T071 [P] Unit tests `tests/FundingPlatform.Tests.Unit/Domain/PasswordResetTokenTests.cs` (consume single-use, reject expired).
- [ ] T072 [P] Unit tests `tests/FundingPlatform.Tests.Unit/Domain/ApplicationSubmitGuardTests.cs` (FR-017 predicate matrix).

### DI registration

- [ ] T073 Update `src/FundingPlatform.Application/DependencyInjection.cs` and `src/FundingPlatform.Infrastructure/DependencyInjection.cs` to register every new interface / implementation introduced above.

**Checkpoint**: Foundation ready. Schema deploys; domain + cross-cutting infrastructure compiles; unit tests green; user-story phases unblocked.

---

## Phase 3: User Story 1 — Annual program cycle administration (P1) 🎯 MVP

**Goal**: Admin can create a Process, attach a Plantilla snapshot, create Groups under it, assign reviewers — all without leaving *Administración*. Base-Plantilla edits do not mutate already-assigned ProcessPlantilla snapshots.

**Independent Test**: Admin completes create-Process → assign-Plantilla → create-Groups → assign-reviewers; then edits the base Plantilla and re-opens the Process detail — snapshot unchanged.

### Tests

- [ ] T074 [P] [US1] Integration test `tests/FundingPlatform.Tests.Integration/Persistence/ProcessRepositoryTests.cs` covering create + close + override + snapshot independence (real SQL).
- [ ] T075 [P] [US1] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US1_ProcessAdmin.cs` driving full create-Process → assign-Plantilla → create-Groups → assign-reviewers + snapshot-independence.
- [ ] T076 [P] [US1] E2E POMs `tests/FundingPlatform.Tests.E2E/PageObjects/ProcessAdminPage.cs`, `PlantillaAdminPage.cs`, `AdminUsersPage.cs` (cascading filter assertions).

### Implementation

- [ ] T077 [P] [US1] Application use cases `src/FundingPlatform.Application/Processes/CreateProcessCommand.cs`, `CloseProcessCommand.cs`, `OverrideStageWindowCommand.cs`, `AssignPlantillaCommand.cs` (each emitting audit event).
- [ ] T078 [P] [US1] Application use cases `src/FundingPlatform.Application/Plantillas/CreatePlantillaCommand.cs`, `EditPlantillaCommand.cs`, `DetachPlantillaCommand.cs`, `ArchivePlantillaCommand.cs`.
- [ ] T079 [P] [US1] Application query `src/FundingPlatform.Application/Processes/Queries/ListProcessesQuery.cs`, `GetProcessDetailQuery.cs`.
- [ ] T080 [US1] Create `src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs` with routes from `contracts/admin-routes.md` (Processes section).
- [ ] T081 [US1] Create `src/FundingPlatform.Web/Controllers/Admin/AdminPlantillasController.cs` with routes from `contracts/admin-routes.md` (Plantillas section).
- [ ] T082 [US1] Update `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` to render two-level cascading Process → Group filter (FR-034) using `_ProvinceCantonCascade.cshtml` pattern adapted for Process/Group.
- [ ] T083 [P] [US1] Views: `src/FundingPlatform.Web/Views/Admin/Processes/{Index,Create,Details}.cshtml` + `Plantillas/{Index,Create,Edit}.cshtml`. Empty-state copy from spec Edge Cases.
- [ ] T084 [US1] Update `src/FundingPlatform.Web/Views/Shared/_AdminSidebar.cshtml` to expose new *Procesos* + *Plantillas* sidebar entries within spec-017 admin grouping.

**Checkpoint**: User Story 1 fully functional; E2E green.

---

## Phase 4: User Story 2 — Applicant submits end-to-end on new flow (P1)

**Goal**: Impact-first draft; per-field autosave with *"✓ Guardado HH:MM"*; required-field markers; submit disabled until complete with tooltip listing failures; `/review` page + *"Confirmar y enviar"*; PublicCode displayed on every surface; FX disclaimer below CRC totals; cascading Province → Cantón on supplier-branch flow; greeting *"Hola, {{Nombre}}"*; CTA *"Iniciar acompañamiento"*.

**Independent Test**: Fresh applicant signs in → completes draft → impact → items → quotations → submit → /review → confirm. The resulting Application appears under PublicCode on dashboard, reviewer queue, signing inbox, Funding Agreement PDF, every notification email — never as `Solicitud N.º N`.

### Tests

- [ ] T085 [P] [US2] Integration test `tests/FundingPlatform.Tests.Integration/Applications/AutosaveEndpointTests.cs` (ETag pass + 409 + 422-on-stage-closed).
- [ ] T086 [P] [US2] Integration test `tests/FundingPlatform.Tests.Integration/Applications/SubmitGuardTests.cs` (FR-017 matrix end-to-end with real DB).
- [ ] T087 [P] [US2] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US2_ApplicantE2E.cs` covers draft → /review → confirm → PublicCode rendered everywhere (uses `ForbiddenStringsCrawler`).
- [ ] T088 [P] [US2] E2E POMs `ApplicationDraftPage.cs`, `ReviewPage.cs`, `SupplierBranchInlineForm.cs`.
- [ ] T089 [P] [US2] E2E POM helper `ForbiddenStringsCrawler.cs` (covers SC-005, used again in US7).

### Implementation

- [ ] T090 [P] [US2] Application command `src/FundingPlatform.Application/Applications/AutosaveFieldCommand.cs` + projection for autosave indicator state.
- [ ] T091 [P] [US2] Application command `src/FundingPlatform.Application/Applications/SubmitApplicationCommand.cs` (calls `Application.Submit()` guards; raises `StageWindowClosedException` if window closed).
- [ ] T092 [P] [US2] Application query `src/FundingPlatform.Application/Applications/Queries/GetApplicationReviewProjection.cs` (items / suppliers / totals + CRC conversion + Impact for `/review`).
- [ ] T093 [P] [US2] Application command + query `src/FundingPlatform.Application/Suppliers/SearchSuppliersQuery.cs`, `CreateSupplierBranchCommand.cs` (inline applicant path).
- [ ] T094 [US2] Update `src/FundingPlatform.Web/Controllers/ApplicationController.cs`: autosave POST endpoint, `/review` GET, submit POST, PublicCode display in all view models, `/Applications/{publicCode}/Edit` route binding.
- [ ] T095 [P] [US2] Update `src/FundingPlatform.Web/Views/Application/Edit.cshtml`: Impact step first, items inline, supplier search + new-branch inline form, Province → Cantón cascade, autosave indicator, required-field markers, FX disclaimer below CRC totals, banner countdown header.
- [ ] T096 [P] [US2] Create `src/FundingPlatform.Web/Views/Application/Review.cshtml` (`/review`) with *"Confirmar y enviar"* button + items / suppliers / totals / Impact summary.
- [ ] T097 [P] [US2] Update `src/FundingPlatform.Web/Views/Application/Index.cshtml` (applicant dashboard) to render PublicCode + greeting *"Hola, {{Nombre}}"*; remove any `Solicitud N.º N` template references.
- [ ] T098 [US2] Sweep every applicant-facing view (dashboard, draft, /review, reviewer queue row, signing inbox row, notification email templates) to display `PublicCode` and drop numeric ID strings. List of files to update derived from `grep -rn "Solicitud" src/FundingPlatform.Web/Views/`.
- [ ] T099 [P] [US2] Update notification-email templates (`src/FundingPlatform.Web/Views/Emails/*`) to display PublicCode in subject + body.
- [ ] T100 [US2] Wire `autosave.js`, `supplier-autocomplete.js`, `province-canton-cascade.js`, `input-masks.js`, `public-code-banner.js` into `Edit.cshtml` + `Review.cshtml`.
- [ ] T101 [US2] Localization: register all FR-022 + FR-029 + FR-030 strings; remove every `financiamiento` from applicant-facing `.resx` (Funding Agreement PDF carve-out preserved).

**Checkpoint**: User Story 2 fully functional; E2E + ForbiddenStringsCrawler green.

---

## Phase 5: User Story 3 — SupplierAdmin role (P1)

**Goal**: SupplierAdmin role with strict scope: full CRUD on `Supplier` + `SupplierBranch` + `IsCompliant` toggle; denied on every other admin route with 403 + `SupplierAdminDeniedAccess` audit event; sidebar limited to *Empresas proveedoras* + profile.

**Independent Test**: User with role `SupplierAdmin` only can use `/Admin/Suppliers*` paths; direct GET on `/Admin/Users`, `/Admin/Reports`, `/Admin/Processes` etc. all return 403 and write an audit row.

### Tests

- [ ] T102 [P] [US3] Integration test `tests/FundingPlatform.Tests.Integration/Authorization/SupplierAdminAuthorizationTests.cs` matrix: every admin controller × {allow, deny, audit row} (real DB).
- [ ] T103 [P] [US3] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US3_SupplierAdmin.cs` covering sidebar visibility + supplier CRUD + 403-on-restricted-route.
- [ ] T104 [P] [US3] E2E POM `SupplierAdminPage.cs`.

### Implementation

- [ ] T105 [P] [US3] Create `src/FundingPlatform.Web/Filters/SupplierAdminOnlyAttribute.cs` (allow Admin OR SupplierAdmin).
- [ ] T106 [P] [US3] Create `src/FundingPlatform.Web/Filters/SupplierAdminDeniedAttribute.cs` (writes `SupplierAdminDeniedAccess` audit row, returns 403 + Tabler 403 view).
- [ ] T107 [US3] Apply `[SupplierAdminOnly]` on `AdminSuppliersController` class. Apply `[SupplierAdminDenied]` on every other `/Admin/*` controller class (AdminProcessesController, AdminPlantillasController, AdminUsersController, AdminReportsController, AdminGroupsController, AdminCurrenciesController, AdminExchangeRatesController, AdminLegacyQuotationsController, AdminController).
- [ ] T108 [P] [US3] Update `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs`: default sort `LastUsedAt DESC`, Process filter, autocomplete on Name + CédulaJurídica (via `/api/suppliers/search`), SupplierBranch create surface with Province → Cantón cascade + ContactPersonName.
- [ ] T109 [P] [US3] Create `src/FundingPlatform.Web/Controllers/SuppliersApiController.cs` exposing `GET /api/suppliers/search?q=` (Name + CédulaJurídica autocomplete, 25-result cap, P95 ≤ 300 ms).
- [ ] T110 [P] [US3] Application use case `src/FundingPlatform.Application/Suppliers/Queries/SearchSuppliersForAdminQuery.cs` with EF-level filter and projection.
- [ ] T111 [US3] Update `src/FundingPlatform.Web/Views/Shared/_AdminSidebar.cshtml`: SupplierAdmin sees only *Empresas proveedoras* + profile.
- [ ] T112 [US3] Create `src/FundingPlatform.Web/Views/Shared/Error403.cshtml` (Tabler-styled) if not present; route 403 responses to it.

**Checkpoint**: User Story 3 fully functional; SC-006 matrix green; E2E green.

---

## Phase 6: User Story 4 — Stage expiry + reminder emails (P2)

**Goal**: Stage windows configurable platform-wide and per-Process; banner shows countdown / Vencido; expired window hard-blocks POSTs (422); hourly hosted service emits T-72h, T-24h, expiry reminders via existing SMTP.

**Independent Test**: With seeded windows and synthetic Applications at boundaries, the hosted service emits the right emails (captured by `CapturingEmailSender`); banner switches at thresholds; POST after expiry returns 422.

### Tests

- [ ] T113 [P] [US4] Integration test `tests/FundingPlatform.Tests.Integration/BackgroundServices/StageExpiryReminderServiceTests.cs` (CapturingEmailSender + fake `IStageExpiryClock`, real DB).
- [ ] T114 [P] [US4] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US4_StageExpiry.cs` covering banner countdown + 422 on submit after expiry + per-Process override path.

### Implementation

- [ ] T115 [P] [US4] Implement `src/FundingPlatform.Application/StageExpiry/StageExpiryEvaluator.cs` (resolves Process override → platform default → bucket determination).
- [ ] T116 [P] [US4] Implement `src/FundingPlatform.Infrastructure/Clocks/SystemStageExpiryClock.cs` + DI seam.
- [ ] T117 [US4] Implement `src/FundingPlatform.Infrastructure/BackgroundServices/StageExpiryReminderService.cs` (IHostedService, hourly timer, queries Applications, sends via existing SMTP, updates `RemindersSentMask` atomically). Retry with exponential backoff up to 5 attempts (NFR-002).
- [ ] T118 [P] [US4] Create reminder email templates `src/FundingPlatform.Web/Views/Emails/Stages/{T72,T24,Expired}.cshtml` with PublicCode + Vencido copy.
- [ ] T119 [P] [US4] Render `_StageCountdownBanner.cshtml` partial inside: applicant draft (`Edit.cshtml`), `/review`, reviewer queue row (`Reviewer/Queue.cshtml`), signing inbox row.
- [ ] T120 [US4] Wire admin UI for per-Process stage overrides (`AdminProcessesController.OverrideStageWindow` action + view widget) — extends T080.
- [ ] T121 [US4] Register hosted service + clock in `Web/Program.cs` via `Infrastructure/DependencyInjection.cs`.

**Checkpoint**: User Story 4 fully functional; integration test captures three reminder emails at boundaries; E2E green.

---

## Phase 7: User Story 5 — Profile + forgot-password (P2)

**Goal**: `/Profile` lets user self-edit FirstName/LastName/Phone/Address; Email/Role/Group/CodigoPersonal read-only with *"administrado"* badge. `/forgot-password` and `/reset-password` work end-to-end with single-use 60-min token, strength legend, eye toggle.

**Independent Test**: User forgets password → email → reset → login. Same user opens profile, edits FirstName, saves; verifies Email cannot be edited by them.

### Tests

- [ ] T122 [P] [US5] Integration test `tests/FundingPlatform.Tests.Integration/Identity/PasswordResetTokenStoreTests.cs` (issue + consume + reject-reuse + reject-expired).
- [ ] T123 [P] [US5] Integration test `tests/FundingPlatform.Tests.Integration/Identity/ForgotPasswordEnumerationTests.cs` (unknown email returns identical response).
- [ ] T124 [P] [US5] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US5_ProfileAndForgotPassword.cs` covers forgot-password full loop + profile-edit + read-only field assertions.
- [ ] T125 [P] [US5] E2E POMs `ProfilePage.cs`, `ForgotPasswordPage.cs`, `ResetPasswordPage.cs`.

### Implementation

- [ ] T126 [P] [US5] Application commands `src/FundingPlatform.Application/Identity/{IssuePasswordResetTokenCommand, ConsumePasswordResetTokenCommand, UpdateProfileCommand}.cs`.
- [ ] T127 [US5] Update `src/FundingPlatform.Web/Controllers/AccountController.cs`: add `ForgotPassword` (GET/POST), `ResetPassword` (GET/POST), `Profile` (GET), `Profile/Update` (POST), `Profile/ChangePassword` (POST). Wire `IPasswordResetTokenStore` + `DataProtectorTokenProvider` lifespan = 60 min.
- [ ] T128 [P] [US5] Views: `src/FundingPlatform.Web/Views/Account/{ForgotPassword,ResetPassword}.cshtml`, `Views/Account/Profile.cshtml` (read-only fields badged *"administrado"*, password change form with strength legend + eye toggle).
- [ ] T129 [P] [US5] Email template `src/FundingPlatform.Web/Views/Emails/Identity/ForgotPasswordEmail.cshtml` (uses existing SMTP wiring).
- [ ] T130 [US5] Configure `DataProtectionTokenProviderOptions.TokenLifespan = TimeSpan.FromMinutes(60)` in `Program.cs`. Wire neutral "no enumeration" branch in `ForgotPassword POST`.
- [ ] T131 [US5] Wire eye toggle + strength legend on every password input (Login, ResetPassword, Profile/ChangePassword): include `password-eye-toggle.js`, `password-strength-legend.js`, and `_PasswordStrengthLegend.cshtml`.

**Checkpoint**: User Story 5 fully functional; SC-009 verified (forgot-password ≤ 90 s; expired tokens rejected 100 %); E2E green.

---

## Phase 8: User Story 6 — Admin dashboard repivot + supplier search refinements (P2)

**Goal**: *Personas activas* + *Fondos entregados* KPIs on admin dashboard; pending-quotation tile moved to reviewer dashboard; admin user-list group selector cascades Process → Group; supplier list autocomplete + last-used-desc default + Process filter.

**Independent Test**: Seeded dashboard renders the two new tiles non-placeholder; pending-quotation tile renders on reviewer dashboard, absent from admin; supplier search by name returns expected matches within 300 ms.

### Tests

- [ ] T132 [P] [US6] Integration test `tests/FundingPlatform.Tests.Integration/Dashboards/AdminDashboardProjectionTests.cs` (verifies Personas activas + Fondos entregados values from seed).
- [ ] T133 [P] [US6] Integration test `tests/FundingPlatform.Tests.Integration/Suppliers/SupplierSearchPerformanceTests.cs` (P95 ≤ 300 ms @ 200+ suppliers seed scale).
- [ ] T134 [P] [US6] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US6_AdminDashboardAndSearch.cs` asserts both tiles present, pending-quotation absent on admin / present on reviewer, supplier-search-by-name returns expected result.

### Implementation

- [ ] T135 [P] [US6] Extend `src/FundingPlatform.Application/AdminDashboard/IAdminDashboardProjection.cs` with `CountPersonasActivas()`, `SumFondosEntregados()`.
- [ ] T136 [P] [US6] Move pending-quotation tile out of `IAdminDashboardProjection` (and corresponding view block) into new `src/FundingPlatform.Application/ReviewerDashboard/IReviewerDashboardProjection.cs::CountPendingQuotations()`.
- [ ] T137 [US6] Update `src/FundingPlatform.Web/Views/Admin/Index.cshtml` (`_AdminDashboard` partial) to render the two new `_KpiTile` instances. Preserve existing 4 action KPIs (spec 017 alignment).
- [ ] T138 [US6] Update `src/FundingPlatform.Web/Controllers/ReviewerDashboardController.cs` + `Views/Reviewer/Dashboard.cshtml` to host the pending-quotation tile.
- [ ] T139 [P] [US6] Update `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml`: render Process → Group cascading filter (re-uses cascade JS pattern from T060).
- [ ] T140 [P] [US6] Update `src/FundingPlatform.Web/Views/Admin/Suppliers/Index.cshtml`: default `LastUsedAt DESC` sort, Process filter, autocomplete on Name + CédulaJurídica.

**Checkpoint**: User Story 6 fully functional; E2E green.

---

## Phase 9: User Story 7 — Acompañamiento copy + public landing scaffold (P3)

**Goal**: Public `/` landing renders hero CTA + three slot regions (Reglamento, Ejemplo de cotización, Sponsor strip from spec 019). Greeting reads *"Hola, {{Nombre}}"*. *"financiamiento"* removed from every applicant-facing surface (legal Funding Agreement PDF retains it).

**Independent Test**: Grep of rendered HTML on every applicant-facing surface returns zero `financiamiento` matches, zero `Bienvenido/a` matches. Public `/` renders FR-029 CTA + slots + sponsor strip.

### Tests

- [ ] T141 [P] [US7] E2E test `tests/FundingPlatform.Tests.E2E/Tests/US7_AcompanamientoCopyAndLanding.cs` uses `ForbiddenStringsCrawler` across every applicant-facing surface, asserts public `/` slot rendering + *"Próximamente"* placeholder + CTA.
- [ ] T142 [P] [US7] E2E POM `PublicLandingPage.cs`.

### Implementation

- [ ] T143 [P] [US7] Update `src/FundingPlatform.Web/Controllers/HomeController.cs`: `Index` action `[AllowAnonymous]` for unauthenticated visitors; redirect to role dashboard if authenticated.
- [ ] T144 [P] [US7] Create `src/FundingPlatform.Web/Views/Home/Index.cshtml` (public landing): hero with FR-029 CTA, three slot regions reading from `IObjectStorage` category `public-landing-files`, sponsor strip partial reused from spec 019.
- [ ] T145 [P] [US7] Create `src/FundingPlatform.Web/Controllers/Admin/AdminPublicLandingFilesController.cs` for slot upload/clear surfaces.
- [ ] T146 [P] [US7] Create `src/FundingPlatform.Web/Views/Admin/PublicLanding/Index.cshtml` upload form.
- [ ] T147 [P] [US7] Create `src/FundingPlatform.Web/Controllers/PublicLandingFilesController.cs` exposing `GET /files/public-landing/{slot}` per `contracts/public-routes.md`.
- [ ] T148 [US7] Register storage category `public-landing-files` in `appsettings.json` + `Storage:Categories` block per CLAUDE.md table.
- [ ] T149 [US7] Sidebar + auth views: replace any `Bienvenido` rendered string with `Greeting.Pattern` template "Hola, {{Nombre}}". Confirm via grep before commit.

**Checkpoint**: User Story 7 fully functional; SC-012, SC-015 green via `ForbiddenStringsCrawler`.

---

## Phase 10: User Story 8 — Deleted Applications no longer surface as active (P3)

**Goal**: Soft-deleted Applications excluded from every dashboard surface (applicant, admin, reviewer), including the *"borrador listo para enviar"* prompt and *Solicitudes activas* counter. Centralised single helper prevents recurrence.

**Independent Test**: Create draft → admin soft-deletes → all dashboard surfaces reload → application no longer appears anywhere (counter decrements, prompt cleared).

### Tests

- [ ] T150 [P] [US8] Structural test `tests/FundingPlatform.Tests.Unit/QueryHygiene/DashboardQueriesHonorSoftDeleteTests.cs` (reflection-scan: no dashboard projection bypasses `IApplicationQueryFilter.ExcludeDeleted`).
- [ ] T151 [P] [US8] E2E regression `tests/FundingPlatform.Tests.E2E/Tests/US8_DeletedNotActive.cs` reproducing the meeting-PDF screenshot path.

### Implementation

- [ ] T152 [US8] Audit every projection / read-path under `src/FundingPlatform.Application/` and `src/FundingPlatform.Web/Controllers/` for `_db.Applications.AsQueryable()` calls. Route each through `IApplicationQueryFilter.ExcludeDeleted` (helper from T044, T050).
- [ ] T153 [US8] Specifically patch: applicant `Index` projection, admin dashboard projection, reviewer queue projection, signing inbox projection, *"Solicitudes activas"* counter, *"borrador listo para enviar"* prompt source.
- [ ] T154 [US8] Confirm `Application` soft-delete column (`DeletedAt`) exists; if absent, add via schema delta in T013 and back-fill plan (no-op, no production data).

**Checkpoint**: User Story 8 fully functional; SC-011 regression green; structural test passes.

---

## Phase 11: Polish & Cross-Cutting

**Purpose**: PDF carry-over, localization audit, quickstart validation, full E2E green run.

- [ ] T155 [P] PDF template swap: `src/FundingPlatform.Infrastructure/Pdf/FundingAgreementHtmlTemplate.cs` replace `"Solicitud N.º {{Number}}"` token with `"Solicitud {{PublicCode}}"` per OQ-4. Update integration test `FundingAgreementPdfTests` accordingly.
- [ ] T156 [P] Localization sweep: run `grep -rn 'Bienvenido\|financiamiento\|Solicitud N\\.º' src/FundingPlatform.Web/Views/` and confirm zero applicant-facing matches; carve-out preserved only in `FundingAgreementHtmlTemplate` body copy.
- [ ] T157 [P] Update `CLAUDE.md` `## Active Technologies` + `## Recent Changes` entries for spec 021.
- [ ] T158 [P] Hint attribute scaffolding (FR-020): create `Domain/Attributes/HintAttribute.cs` + `Views/Shared/_HintTooltip.cshtml`. Empty initial copy slots (OQ-8 — strings deferred).
- [ ] T159 Quickstart validation: walk through `specs/021-feedback-session-may13/quickstart.md` end-to-end on `dotnet run --project src/FundingPlatform.AppHost` and confirm every smoke-test step.
- [ ] T160 Run full E2E suite: `dotnet test tests/FundingPlatform.Tests.E2E`. Green is delivery gate (NFR-004 + project rule).
- [ ] T161 Run full unit + integration suites: `dotnet test tests/FundingPlatform.Tests.Unit && dotnet test tests/FundingPlatform.Tests.Integration`. All green.
- [ ] T162 Verify `dotnet build FundingPlatform.slnx` produces zero warnings on `021-feedback-session-may13`.
- [ ] T163 Final `ForbiddenStringsCrawler` run across every applicant-facing surface — zero `financiamiento`, zero `Bienvenido`, zero `Solicitud N.º \d+` matches.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies — immediate.
- **Phase 2 (Foundational)**: depends on Phase 1. **Blocks every user-story phase.**
- **Phases 3–10 (User Stories)**: depend on Phase 2 only. After Phase 2 completes, US1 / US2 / US3 can run in parallel (each is an independent slice). US4–US8 also independent post-Phase-2 but stylistically benefit from MVP (US1+US2+US3) landing first.
- **Phase 11 (Polish)**: depends on Phases 3–10 desired-scope completion.

### User-story dependencies (most are independent)

- **US1**: independent.
- **US2**: needs US1's *"Migración inicial"* Process to exist at the data layer (covered by T020 in Foundational). Otherwise independent.
- **US3**: independent (SupplierAdmin role seeded by T021 in Foundational).
- **US4**: needs Process to override per-Process windows (US1 conceptual; T020 covers data path). Banner partial + clock seam from Phase 2 already in place.
- **US5**: independent (PasswordResetToken table + store from Phase 2).
- **US6**: leans on US1 (Process axis for cascading filter), US3 (Supplier autocomplete shared component).
- **US7**: independent (public landing + copy sweep are isolated).
- **US8**: leans on the central `IApplicationQueryFilter` introduced in Phase 2 (T044, T050). Otherwise independent.

### Within each story

- Tests scaffolded first (still must FAIL before implementation lands per Constitution III) → models / use cases → controllers / views → wiring + JS.
- Commit after each P-marked sub-group (per CLAUDE.md commit discipline).

### Parallel opportunities

- Setup (Phase 1): T002/T003/T004 in parallel.
- Foundational schema: T005–T011 are different files — all P.
- Foundational domain: T023–T044 mostly P (different files).
- Foundational infra+web cross-cutting: T047–T065 mostly P.
- Localization baseline T066 and resx sweep T067 sequential to avoid merge conflict.
- Within US1: T077/T078/T079 P; T083 P alongside T080–T082.
- Within US2: T090–T093 P; views T095–T097 P; controller T094 + sweep T098 sequential.
- US3 tests T102–T104 P; filters T105/T106 P; updates T108–T110 P.
- US4 tests T113/T114 P; eval + clock T115/T116 P; templates T118 P.
- US5 tests T122–T125 P; commands T126 P; views T128/T129 P.
- US6 tests T132–T134 P; projection extensions T135/T136 P; views T137/T139/T140 P.
- US7 tests T141/T142 P; controllers + views T143–T147 P.
- US8 tests T150/T151 P; audit T152–T154 sequential.

---

## Parallel example — User Story 1

```bash
# Once Phase 2 done, launch in parallel:
Task: T074 Integration test ProcessRepositoryTests
Task: T075 E2E test US1_ProcessAdmin
Task: T076 POMs ProcessAdminPage, PlantillaAdminPage, AdminUsersPage
Task: T077 Application use cases Processes/
Task: T078 Application use cases Plantillas/
Task: T079 Application queries Processes/

# Then sequential (same controller files):
Task: T080 AdminProcessesController
Task: T081 AdminPlantillasController
Task: T082 AdminUsersController (cascading filter)

# Then parallel:
Task: T083 Views Admin/Processes/* + Admin/Plantillas/*
Task: T084 _AdminSidebar update
```

---

## Implementation Strategy

### MVP scope — US1 + US2 + US3 (all P1)

1. Phase 1 (Setup) → Phase 2 (Foundational, schema + domain + cross-cutting).
2. Phase 3 (US1) → MVP increment 1: admin can run an annual cycle.
3. Phase 4 (US2) → MVP increment 2: applicants can complete the new flow with PublicCode + autosave + /review.
4. Phase 5 (US3) → MVP increment 3: SupplierAdmin role delegates supplier catalog work.
5. Validate via Playwright E2E. Demo to stakeholders.

### Incremental delivery — P2 + P3

6. Phase 6 (US4 stage expiry) — operational predictability.
7. Phase 7 (US5 profile + forgot-password) — support-burden removal.
8. Phase 8 (US6 dashboard repivot + search) — daily-admin ergonomics.
9. Phase 9 (US7 copy + landing) — brand correctness.
10. Phase 10 (US8 soft-delete fix) — defect closure.
11. Phase 11 polish + full E2E green run.

### Parallel team strategy

- Once Phase 2 closes, US1 / US2 / US3 can be split across three developers — each is an independently testable slice with non-overlapping controllers/views (except US2 + US3 share the supplier-autocomplete component, which is finalized in T065 + T108–T110 with US3 absorbing the integration finish).

---

## Notes

- Constitution III mandates E2E coverage per user story; no story phase is complete without its US-numbered Playwright test green.
- Integration tests hit real SQL (project rule); no mocks.
- Delivery bar = full E2E suite green (NFR-004 + CLAUDE.md memory: *"feature is not delivered until the full E2E suite has been personally executed and is green"*).
- After each P-grouped block: commit per CLAUDE.md commit discipline (auto-commit hook fires `speckit.git.commit`).
- Avoid: re-introducing `Bienvenido` / `financiamiento` / `Solicitud N.º N` strings via copy/paste; FR-008 / FR-029 / FR-030 are CI-asserted.
