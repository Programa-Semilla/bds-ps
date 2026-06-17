# Implementation Plan: Applicant Companies — controlled company selection on submission

**Branch**: `037-applicant-companies` | **Date**: 2026-06-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/037-applicant-companies/spec.md`

## Summary

Replace the applicant's free-text company name on application creation with a controlled dropdown of **admin-assigned companies**. Introduce a new admin-managed `Company` aggregate (one applicant → many companies; name-only; active/archived). On creation the applicant selects one of their **active** companies; a single company auto-selects, multiple force a choice, zero blocks creation. Each application stores a nullable `CompanyId` reference **plus** the existing `CompanyName` column re-purposed as a **frozen name snapshot** (copied at creation, re-copied on draft re-select, frozen at submit) — so historical applications preserve their name after later edits. Admins create with ≥1 company, add, rename, soft-archive/unarchive (with a last-active floor); the batch CSV gains a required `Nombre de la empresa` column. All server-side selection is ownership- and active-validated. Greenfield: no backfill; `CompanyId` is nullable so existing applications stay valid.

The design reuses established seams verbatim: the spec-029 `0/1/many` anchor-selection rendering pattern (for the company dropdown, mirroring `GroupId`), the spec-031 `data-searchable` enhancer, the spec-036 `FundService`/`FundsUsageEvidence` aggregate+config+dacpac patterns, the `AdminAuditEvent`/`AdminAuditEventWriter` prefix-routed audit, the spec-034 batch CSV machinery, the spec-018 autosave draft-edit path, and the existing admin user create/edit/batch flow. **No new managed dependencies. No new application state.**

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: ASP.NET Identity, .NET Aspire, SQL Server (dacpac schema), Tabler.io (vendored), Playwright (E2E). No new managed dependencies.
**Storage**: SQL Server via dacpac (schema source of truth). New table `dbo.Companies`; nullable column `dbo.Applications.CompanyId`.
**Testing**: xUnit/NUnit unit + integration (real DB), Playwright E2E via `AspireFixture`. Delivery bar = filtered E2E green for the new/affected classes.
**Target Platform**: Linux server (Aspire-orchestrated), es-CR default culture.
**Project Type**: Web application (Clean Architecture: Domain / Application / Infrastructure / Web).
**Performance Goals**: No new hot paths; company lookups are small per-applicant sets (indexed by `ApplicantId`). N/A beyond standard request latency.
**Constraints**: Schema-first (no EF migrations); retrying execution strategy forbids raw `BeginTransactionAsync` in single-SaveChanges paths (CreateUserAsync attaches companies in the **same** SaveChanges as the Applicant); es-CR copy throughout; UX/UI parity with existing admin surfaces.
**Scale/Scope**: Applicant (Solicitante) role only. ~1 new table, ~1 new column, 1 new aggregate, 1 new repo, 1 new admin service, edits to ~6 existing files on the create path + ~5 on the admin path + batch + autosave + audit + seed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Compliance |
|---|---|
| **I. Clean Architecture** | Domain: `Company` entity + `ICompanyRepository` interface; Application: `CompanyDto`, request/command changes, `ICompanyAdministrationService` interface; Infrastructure: `CompanyConfiguration`, `CompanyRepository`, `CompanyAdministrationService`, `UserAdministrationService`/`AutosaveFieldHandler` edits; Web: controllers/views/viewmodels. Dependencies point inward. ✅ |
| **II. Rich Domain Model** | `Company` encapsulates name invariants (trim/required/≤200), rename, and archive/unarchive state; `Application.SetCompany(companyId, nameSnapshot)` owns the snapshot + freeze gate (`EnsureNotFrozen`). The **last-active-company floor** is a cross-aggregate rule (needs sibling count) → enforced in `CompanyAdministrationService`, documented as a justified service-level invariant. ✅ |
| **III. E2E (NON-NEGOTIABLE)** | 4 user stories → 4 Playwright classes (selection, admin management, history preservation, batch). Page Object Model. Independently runnable. ✅ |
| **IV. Schema-First** | `dbo.Companies.sql` + nullable `Applications.CompanyId` column/FK edited in the Database project; EF config mirrors. No migrations/`EnsureCreated`. Demo seed in `IdentityConfiguration`. ✅ |
| **V. SDD** | spec.md → plan.md → tasks.md → implement. Stories prioritized & independently testable. ✅ |
| **VI. Simplicity / YAGNI** | Nullable FK + name-snapshot reuse avoids versioning and backfill; company carries a single attribute; no new state/deps. Greenfield. ✅ |

**Result: PASS** (no violations; no Complexity Tracking entries required).

## Project Structure

### Documentation (this feature)

```text
specs/037-applicant-companies/
├── spec.md              # Complete (/speckit-specify)
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D13
├── data-model.md        # Phase 1 — Company aggregate, Application change, schema
├── contracts/
│   └── interfaces.md     # Phase 1 — service/repo interfaces, routes, audit, batch contract
├── quickstart.md        # Phase 1 — how to run/verify
├── review_brief.md      # From brainstorm
├── REVIEW-SPEC.md       # From brainstorm (SOUND)
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Domain/
    Entities/Company.cs                         # NEW aggregate (Name, ArchivedAt, invariants)
    Entities/Application.cs                     # + CompanyId, SetCompany(id, snapshot); ctor change
    Entities/AdminAuditEvent.cs                 # + company.* action consts + TargetTypeCompany
    Repositories/ICompanyRepository.cs          # NEW (Domain interface)
  FundingPlatform.Application/
    Admin/Users/DTOs/CreateUserRequest.cs       # + CompanyNames (at-creation companies)
    Admin/Users/DTOs/UpdateUserRequest.cs       # (unchanged — companies edited via sub-routes)
    Admin/Users/DTOs/UserDetailDto.cs           # + Companies (read surface)
    Admin/Users/DTOs/CompanyDto.cs              # NEW (Id, Name, IsArchived)
    Admin/Users/Batch/BatchUserCsvColumns.cs    # + "Nombre de la empresa" (Count 10→11)
    Admin/Users/Batch/BatchUserImportRow.cs     # + NombreEmpresa
    Admin/Users/Batch/BatchUserRowReasons.cs    # + company es-CR reasons
    Admin/Companies/ICompanyAdministrationService.cs  # NEW (add/rename/archive/unarchive)
    Applications/Commands/CreateApplicationCommand.cs # CompanyName → CompanyId
    Applications/AutosaveFieldCommand.cs        # (unchanged shape; new field-key "CompanyId")
    Services/ApplicationService.cs              # resolve+validate Company at creation
  FundingPlatform.Infrastructure/
    Persistence/Configurations/CompanyConfiguration.cs   # NEW
    Persistence/Configurations/ApplicationConfiguration.cs # + CompanyId mapping/index
    Persistence/AppDbContext.cs                 # + DbSet<Company>
    Persistence/Repositories/CompanyRepository.cs        # NEW
    Services/CompanyAdministrationService.cs    # NEW (mirrors FundService; folds DB in)
    Identity/UserAdministrationService.cs       # attach companies in CreateUserAsync; batch map
    Services/AutosaveFieldHandler.cs            # CompanyId field-key (re-select, re-snapshot)
    Identity/IdentityConfiguration.cs           # seed demo companies
  FundingPlatform.Database/
    Tables/dbo.Companies.sql                    # NEW
    Tables/dbo.Applications.sql                 # + CompanyId column + FK + index
  FundingPlatform.Web/
    Controllers/Admin/AdminUsersController.cs   # company sub-routes; batch parse; create attach
    Controllers/ApplicationController.cs        # company dropdown population + validation
    ViewModels/CreateApplicationViewModel.cs    # company selection (0/1/many) fields
    ViewModels/Admin/AdminUserCreateViewModel.cs# + Companies (repeatable)
    ViewModels/Admin/AdminUserEditViewModel.cs  # + Companies list (manage)
    Views/Application/Create.cshtml             # free-text → <select data-searchable>
    Views/Application/Edit.cshtml               # company-name input → company <select> (autosave)
    Views/Admin/Users/Create.cshtml             # repeatable company inputs (Applicant-only)
    Views/Admin/Users/Edit.cshtml               # "Empresas" management card
    Resources/AdminCompaniesResources(.resx)    # es-CR strings
tests/
  FundingPlatform.Tests.Unit/                   # Company entity, CSV column, reasons
  FundingPlatform.Tests.Integration/            # CompanyAdministrationService, create-with-company, batch
  FundingPlatform.Tests.E2E/
    Tests/ApplicantCompanySelectionTests.cs     # US1
    Tests/AdminCompanyManagementTests.cs        # US2
    Tests/CompanyHistoryPreservationTests.cs    # US3
    Tests/BatchUserCompanyTests.cs              # US4 (or extend BatchUserCreateTests)
    PageObjects/AdminUserCompaniesPage.cs        # NEW
    PageObjects/...ApplicationCreatePage          # extend for company select
```

**Structure Decision**: Standard 4-layer Clean Architecture already in place; this feature adds one aggregate and threads it through the existing create/admin/batch/autosave seams. No structural change.

## Phase Sequencing (for /speckit-tasks)

1. **Foundational** (blocks all stories): `Company` entity + `ICompanyRepository` + `CompanyConfiguration` + `dbo.Companies.sql` + `Applications.CompanyId` (column/FK/EF) + `Application.SetCompany`/ctor change + audit consts + DbSet. Demo-company seed.
2. **US2 (P1) Admin management** — `CompanyAdministrationService` + sub-routes + Create/Edit views + create-time attach in `UserAdministrationService`. (Companies must exist before they can be selected.)
3. **US1 (P1) Applicant selection** — `CreateApplicationCommand` CompanyId, `ApplicationService` resolve/validate, `CreateApplicationViewModel` 0/1/many, `Create.cshtml` dropdown, server ownership/active guards.
4. **US3 (P2) History preservation** — snapshot freeze at submit + draft re-select via autosave `CompanyId` field-key + `Edit.cshtml` selector. (Snapshot mechanics; verified after US1/US2.)
5. **US4 (P2) Batch** — CSV column + row DTO + parse + template + reasons + create-time attach for batch rows.
6. **Polish** — es-CR resources, audit payloads, read-surface checks (Details/Review/PDF keep showing snapshot), filtered E2E green.

US2 and US1 are both P1 and co-required for a usable MVP; US2 sequences first because companies must exist to be selectable.

## Complexity Tracking

No constitution violations. The only non-entity invariant — the **last-active-company floor** — lives in `CompanyAdministrationService` rather than the `Company` entity because it depends on counting the applicant's *other* active companies (cross-aggregate); this is the same justified pattern the codebase uses for uniqueness/floor rules that an aggregate cannot see in isolation.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
