# Implementation Plan: Feedback Session May-13 (021)

**Branch**: `021-feedback-session-may13` | **Date**: 2026-05-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/021-feedback-session-may13/spec.md`

## Summary

Single-shot delivery of 26 stakeholder refinements bundled into one mega-spec (34 FRs, 8 user stories P1-P3). Primary architectural moves: introduce `Process` aggregate above `Group`, introduce `Plantilla` with copy-on-assign `ProcessPlantilla` snapshot, lift `Impact` from `Item` to `Application`, add `SupplierAdmin` role, replace numeric `"Solicitud N.º N"` with opaque `Application.PublicCode` (8-char dictation-safe base32), add CR `Province`/`Canton` cascading catalogs, add `PasswordResetToken` + `/forgot-password` flow, add stage-expiry windows + hourly hosted background service for reminder emails, public `/` landing scaffold, admin dashboard repivot to *Personas activas* + *Fondos entregados*, copy pivot from *financiamiento* → *acompañamiento*, and the soft-delete dashboard-filter bug fix. Schema cutover via dacpac (no production data → drop `Item.Impact` outright, no backfill). All new UX uses existing Tabler vendored primitives — no new managed dependencies.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0  
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, Syncfusion HtmlToPdfConverter, MailKit (SMTP, existing), Tabler.io (vendored), Playwright for .NET  
**Storage**: SQL Server (Aspire-managed container in dev; dacpac for schema). New tables: `Processes`, `Plantillas`, `ProcessPlantillas`, `Provinces`, `Cantons`, `PasswordResetTokens`. Schema deltas on `Groups` (`ProcessId` FK), `Applications` (`PublicCode`, `ImpactTemplateId`, `ImpactParameterValuesJson` or normalised), `Items` (drop `ImpactId`), `SupplierBranches` (`ContactPersonName`, `ProvinceId`, `CantonId`), `ApplicationUsers` (`CodigoPersonal`), `SystemConfigurations` (stage-expiry defaults). New event kinds on `AdminAuditEvents`.  
**Testing**: NUnit (unit + integration), Playwright (E2E) via `AspireFixture` + Page Object Model. Integration tests hit real SQL container, never mocks. MailKit in-process capture for reminder + reset-token email assertions.  
**Target Platform**: Linux server (Aspire-orchestrated); browser surfaces tested in Chromium via Playwright. PDF generation via Syncfusion on Linux with vendored license fallback in dev.  
**Project Type**: Web application — ASP.NET MVC server-rendered. Single-project layered architecture per Clean Architecture constitution (Domain / Application / Infrastructure / Web).  
**Performance Goals**: Supplier autocomplete ≤ 300 ms P95 at seed scale ≥ 200 suppliers (SC-007, NFR-006). Autosave server round-trip + *"✓ Guardado"* render ≤ 1 s (US2 AC-2). Stage-expiry reminder granularity ≤ ±1 hour (SC-008). PublicCode generation worst-case ≤ 3 retries (collision retry path).  
**Constraints**: No new managed (NuGet/npm) dependencies (NFR-005). All new UX built on Tabler primitives + existing vendored modules. es-CR default culture, every new string in localization catalog (NFR-003, spec 012 dependency). Existing SMTP wiring is sole email provider. No EF migrations — schema source of truth is dacpac (Constitution IV). E2E suite must be green before merge (NFR-004).  
**Scale/Scope**: Seed scale = 200+ suppliers, 7 provinces, ~82 cantones, low-hundreds users, low-thousands Applications across multiple Processes. Single SQL Server instance. Single Web instance with hosted `BackgroundService` for stage-expiry reminders (no separate worker process).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Clean Architecture** | ✅ | `Process`, `Plantilla`, `ProcessPlantilla`, `Province`, `Canton`, `PasswordResetToken` live in `Domain.Entities`; queries / commands in `Application` use cases; EF mapping + storage in `Infrastructure`; MVC surfaces in `Web`. New `IProcessRepository`, `IPasswordResetTokenStore`, `IStageExpiryEvaluator` interfaces defined in Application, implemented in Infrastructure. |
| **II. Rich Domain Model** | ✅ | `Process.Close()`, `Process.OverrideStageWindow()`, `Plantilla.AssignTo(Process)` (returns snapshot), `Application.SetImpact(ImpactTemplate)`, `Application.GeneratePublicCode()`, `PasswordResetToken.Consume()` are domain-side behavior methods. State transitions (`Process.Status`, soft-delete on `Application`, token consumption) are gated by domain validation. |
| **III. E2E Testing (NON-NEGOTIABLE)** | ✅ | Each user story (US1–US8) maps to a Playwright test file with golden path + key error scenarios. POM extended: `ProcessAdminPage`, `PlantillaAdminPage`, `ApplicationDraftPage` (Impact-first + autosave), `ReviewPage` (`/review`), `ProfilePage`, `ForgotPasswordPage`, `ResetPasswordPage`, `PublicLandingPage`, `SupplierAdminPage`. Reminder-email path verified via in-process MailKit capture in integration tests (US4). |
| **IV. Schema-First (dacpac)** | ✅ | All schema deltas authored as `.sql` in `FundingPlatform.Database`. New: `dbo.Processes.sql`, `dbo.Plantillas.sql`, `dbo.ProcessPlantillas.sql`, `dbo.Provinces.sql`, `dbo.Cantons.sql`, `dbo.PasswordResetTokens.sql`. Alters: `dbo.Groups.sql` (+`ProcessId`), `dbo.Applications.sql` (+`PublicCode`, +`ImpactTemplateId`, soft-delete column already exists), `dbo.Items.sql` (-`ImpactId`), `dbo.SupplierBranches.sql` (+`ContactPersonName`, +`ProvinceId`, +`CantonId`), `dbo.AspNetUsers.sql` (+`CodigoPersonal`), `dbo.SystemConfigurations.sql` (+ stage-expiry rows). Seed: `Provinces` (7), `Cantons` (~82), seeded `"Migración inicial"` Process for existing Groups (PostDeployment script). |
| **V. Specification-Driven Development** | ✅ | This plan derives from `spec.md`; all 34 FRs and 16 SCs trace to user stories US1–US8. tasks.md (Stage 4) will preserve user-story groupings so each P1 story is independently shippable. |
| **VI. Simplicity + Progressive Complexity** | ✅ | Default Plantilla-per-Process cardinality = one-to-one (OQ-1 resolution); per-Plantilla expiry overrides deferred (OQ-3); BCCR auto-fetch + Tropic AI extraction deferred (FR-023, Out of Scope). Reusing existing `IObjectStorage`, `AdminAuditEvent`, SMTP, Tabler, `_KpiTile`, `_CapabilityCard`. Single hosted `BackgroundService` rather than separate worker process. New entities created only where the spec demonstrably needs them. |

**Result**: PASS. No complexity-tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/021-feedback-session-may13/
├── plan.md              # This file
├── research.md          # Phase 0 — resolves OQ-1..OQ-10 + key tech decisions
├── data-model.md        # Phase 1 — entities, relationships, validation rules
├── quickstart.md        # Phase 1 — dev runbook for the feature
├── contracts/           # Phase 1 — controller-route surface
│   ├── admin-routes.md
│   ├── applicant-routes.md
│   ├── public-routes.md
│   └── audit-events.md
├── checklists/
│   └── requirements.md  # existing (spec quality checklist)
├── REVIEW-SPEC.md       # existing (spec-review verdict: SOUND)
├── review_brief.md      # existing (reviewer guide)
├── spec.md
└── tasks.md             # Phase 2 — produced by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/
│   │   ├── Process.cs                       # NEW
│   │   ├── Plantilla.cs                     # NEW
│   │   ├── ProcessPlantilla.cs              # NEW
│   │   ├── Province.cs                      # NEW
│   │   ├── Canton.cs                        # NEW
│   │   ├── PasswordResetToken.cs            # NEW
│   │   ├── Group.cs                         # +ProcessId
│   │   ├── Application.cs                   # +PublicCode, +ImpactTemplateId, +Impact value object
│   │   ├── Item.cs                          # -ImpactId
│   │   ├── SupplierBranch.cs                # +ContactPersonName, +ProvinceId, +CantonId
│   │   ├── ApplicationUser.cs               # +CodigoPersonal
│   │   └── AdminAuditEvent.cs               # new event kinds
│   ├── Enums/
│   │   ├── ProcessStatus.cs                 # NEW
│   │   └── StageKind.cs                     # NEW (solicitud/revision/facturacion)
│   ├── ValueObjects/
│   │   └── PublicCode.cs                    # NEW (regex-validated factory)
│   ├── Interfaces/
│   │   ├── IPublicCodeGenerator.cs          # NEW
│   │   └── IStageExpiryClock.cs             # NEW (clock abstraction for tests)
│   └── Exceptions/
│       └── StageWindowClosedException.cs    # NEW → mapped to HTTP 422
├── FundingPlatform.Application/
│   ├── Processes/                           # NEW use cases (Create/Close/AssignPlantilla/OverrideStageWindow)
│   ├── Plantillas/                          # NEW use cases (Create/Edit/Detach + snapshot)
│   ├── Suppliers/                           # supplier admin search + last-used-desc projection
│   ├── Applications/                        # autosave command, submit gating, /review projection
│   ├── Identity/                            # NEW — ForgotPassword/ResetPassword commands, profile-edit command
│   ├── PublicLanding/                       # NEW projection for FR-031 slots
│   ├── AdminDashboard/                      # add Personas activas + Fondos entregados projections
│   ├── ReviewerDashboard/                   # receive pending-quotation tile (moved from admin)
│   ├── StageExpiry/                         # NEW — IStageExpiryEvaluator, ReminderSchedule
│   └── Abstractions/
│       └── IPasswordResetTokenStore.cs      # NEW
├── FundingPlatform.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/                  # EF mappings for Process, Plantilla, ProcessPlantilla, Province, Canton, PasswordResetToken
│   │   └── Repositories/
│   ├── Identity/
│   │   ├── PasswordResetTokenStore.cs       # NEW (EF-backed)
│   │   └── SupplierAdminRoleSeeder.cs       # NEW
│   ├── PublicCodes/
│   │   └── PublicCodeGenerator.cs           # NEW (base32 + retry)
│   ├── BackgroundServices/
│   │   └── StageExpiryReminderService.cs    # NEW IHostedService (hourly cadence)
│   └── Email/
│       └── ReminderEmailTemplates.cs        # NEW (reuses existing SMTP)
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   ├── Admin/
│   │   │   ├── AdminProcessesController.cs          # NEW — Processes CRUD + AssignPlantilla + StageOverride
│   │   │   ├── AdminPlantillasController.cs         # NEW — base Plantilla CRUD
│   │   │   ├── AdminSuppliersController.cs          # updated for SupplierAdmin role + last-used sort + Process filter
│   │   │   ├── AdminUsersController.cs              # cascading Process→Group filter, CodigoPersonal admin-only
│   │   │   └── AdminController.cs                   # KPI repivot (Personas activas / Fondos entregados)
│   │   ├── AccountController.cs                     # +ForgotPassword, +ResetPassword, +Profile
│   │   ├── ApplicationController.cs                 # autosave endpoints, /review, submit gating, PublicCode display
│   │   ├── HomeController.cs                        # NEW public `/` landing
│   │   └── ReviewerDashboardController.cs           # receive pending-quotation tile
│   ├── Filters/
│   │   └── SupplierAdminOnlyAttribute.cs            # NEW — 403 + AdminAuditEvent
│   ├── Views/
│   │   ├── Admin/                                   # Processes/Plantillas/Suppliers/Users views
│   │   ├── Account/                                 # ForgotPassword/ResetPassword/Profile
│   │   ├── Application/                             # Impact-first draft, /review confirm page, PublicCode banner
│   │   ├── Home/                                    # public `/` (anonymous landing)
│   │   ├── Shared/
│   │   │   ├── _StageCountdownBanner.cshtml         # NEW — applicant/reviewer/signing inbox
│   │   │   ├── _PasswordStrengthLegend.cshtml       # NEW
│   │   │   ├── _ProvinceCantonCascade.cshtml        # NEW partial
│   │   │   └── _AutosaveIndicator.cshtml            # NEW partial
│   │   └── _ViewImports.cshtml
│   ├── wwwroot/
│   │   ├── lib/                                     # existing Tabler/Inter/Fraunces/JetBrains/canvas-confetti (no additions)
│   │   ├── js/
│   │   │   ├── autosave.js                          # NEW vanilla module
│   │   │   ├── public-code-banner.js                # NEW (clipboard copy + tooltip)
│   │   │   ├── supplier-autocomplete.js             # NEW
│   │   │   ├── province-canton-cascade.js           # NEW
│   │   │   ├── input-masks.js                       # NEW (phone 8888-8888 mask)
│   │   │   ├── password-eye-toggle.js               # NEW
│   │   │   └── password-strength-legend.js          # NEW
│   │   └── css/
│   └── Localization/
│       └── es-CR.resx                                # NEW strings (FR-029, FR-030, FR-022 disclaimer, banner copy)
├── FundingPlatform.Database/
│   ├── Tables/
│   │   ├── dbo.Processes.sql                        # NEW
│   │   ├── dbo.Plantillas.sql                       # NEW
│   │   ├── dbo.ProcessPlantillas.sql                # NEW
│   │   ├── dbo.Provinces.sql                        # NEW
│   │   ├── dbo.Cantons.sql                          # NEW
│   │   ├── dbo.PasswordResetTokens.sql              # NEW
│   │   ├── dbo.Groups.sql                           # +ProcessId
│   │   ├── dbo.Applications.sql                     # +PublicCode, +ImpactTemplateId (idx unique on PublicCode)
│   │   ├── dbo.Items.sql                            # -ImpactId
│   │   ├── dbo.SupplierBranches.sql                 # +ContactPersonName, +ProvinceId, +CantonId
│   │   ├── dbo.AspNetUsers.sql                      # +CodigoPersonal
│   │   └── dbo.SystemConfigurations.sql             # + stage-expiry keys
│   └── PostDeployment/
│       ├── 01_SeedProvincesCantons.sql              # NEW
│       ├── 02_SeedMigracionInicialProcess.sql       # NEW (assigns existing Groups)
│       └── 03_SeedSupplierAdminRole.sql             # NEW (AspNetRoles row)
└── FundingPlatform.ServiceDefaults/                 # unchanged

tests/
├── FundingPlatform.Tests.Unit/
│   ├── Domain/                                      # +PublicCodeTests, +ProcessTests, +PlantillaSnapshotTests, +PasswordResetTokenTests
│   └── Application/                                 # +StageExpiryEvaluatorTests
├── FundingPlatform.Tests.Integration/
│   ├── Persistence/                                 # +ProcessRepositoryTests, snapshot independence test
│   ├── Identity/                                    # +PasswordResetTokenStoreTests
│   ├── BackgroundServices/                          # +StageExpiryReminderServiceTests (MailKit capture)
│   └── Authorization/                               # +SupplierAdminAuthorizationTests (403 + audit row)
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/                                 # new POMs listed above
    └── Tests/
        ├── US1_ProcessAdmin.cs                      # NEW
        ├── US2_ApplicantE2E.cs                      # NEW
        ├── US3_SupplierAdmin.cs                     # NEW
        ├── US4_StageExpiry.cs                       # NEW
        ├── US5_ProfileAndForgotPassword.cs          # NEW
        ├── US6_AdminDashboardAndSearch.cs           # NEW
        ├── US7_AcompanamientoCopyAndLanding.cs      # NEW
        └── US8_DeletedNotActive.cs                  # NEW regression
```

**Structure Decision**: Existing single-solution layered structure under `src/` (Clean Architecture: Domain / Application / Infrastructure / Web / ServiceDefaults). All new entities, use cases, EF mappings, controllers, views, and Playwright tests fit the existing layout; no new top-level projects. The Database project receives new tables + alters + post-deployment seed scripts; Identity is extended in place rather than re-platformed.

## Complexity Tracking

> No constitution violations — no entries required.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| — | — | — |
