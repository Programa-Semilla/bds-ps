# Implementation Plan: Fund (Fondo) Entity

**Branch**: `029-fund-entity` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/029-fund-entity/spec.md` (evolved 2026-06-10)

## Summary

Introduce a `Fund` (Fondo) aggregate as the top-level container above `Process` (`Fund → Process → Group → Application`). A Fund carries Name, Description, Active/Archived status, and an optional applicant-downloadable regulation PDF (spec-014 storage). `Process` gains a required `FundId`. Two planning decisions expand the original scope: (1) **authoritative `Application.GroupId` anchor** captured at creation, making each application's Process/Fund exact and fixing the nondeterministic Plantilla lookup; (2) **force-freeze on archive** — a reusable `ExcludeArchivedFund` query filter plus controller/domain mutation guards make every application under an archived Fund read-only and hidden from non-admins. Admin Fund CRUD, Process Fund selector, applicant download, and exact Fund filtering on existing reports complete the feature. No new managed dependencies; es-CR throughout; schema via dacpac.

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: .NET Aspire, ASP.NET Identity, spec-014 `IObjectStorage` (Azurite/AzureBlob/LocalFilesystem), Tabler.io (vendored), Playwright. **No new NuGet deps.**
**Storage**: SQL Server (dacpac source of truth) + object storage for the regulation PDF (new `FileCategory.FundRegulation`)
**Testing**: xUnit (Unit/Integration — Integration hits a real DB), Playwright E2E via `AspireFixture`
**Target Platform**: Linux container (Aspire-orchestrated)
**Project Type**: Web application (Clean Architecture: Domain / Application / Infrastructure / Web + Database dacpac)
**Performance Goals**: interactive admin/applicant CRUD; freeze filter is one extra indexed join (`IX_Applications_GroupId`, `IX_Processes_FundId`) — negligible
**Constraints**: es-CR copy; no CDN; no EF migrations (dacpac only); reuse vendored assets; full E2E suite green is the delivery bar
**Scale/Scope**: ~6 new files (Domain entity/enum/exception, Infra service/EF config, Web controller/views/resources) + edits across Process/Application/reports/query-filter; 3 schema objects (new table + 2 FK columns)

## Constitution Check

*GATE: must pass before and after design. Source: `.specify/memory/constitution.md`.*

| Principle | Compliance |
|---|---|
| **I. Clean Architecture** | `Fund`/`FundStatus`/`FundArchivedException` in Domain; `IFundService` + storage/audit abstractions in Application; `FundService`, EF configs, `ApplicationQueryFilter.ExcludeArchivedFund` in Infrastructure; controllers/views in Web. Dependencies point inward. ✅ |
| **II. Rich Domain Model** | Fund encapsulates lifecycle + regulation invariants; `Application` exposes `IsFrozen` + throws `FundArchivedException` on mutation; Process `SetFund` guarded. No anemic rows. ✅ |
| **III. E2E (NON-NEGOTIABLE)** | One independently-runnable Playwright class per user story (US1–US6), POM, golden + error paths (quickstart). ✅ |
| **IV. Schema-first dacpac** | `dbo.Funds`, `Processes.FundId`, `Applications.GroupId` all via `.sql`; idempotent post-deploy seed; no EF migrations / `EnsureCreated`. ✅ |
| **Conventions** | es-CR copy + resources; reuse spec-014 storage, spec-024 toast/confirm, `AdminAuditEvent`; no new managed deps. ✅ |

**Result: PASS (no violations).** Complexity Tracking below records the deliberate scope expansion (not a violation, but a justified deviation from the brainstormed scope).

## Project Structure

### Documentation (this feature)
```text
specs/029-fund-entity/
├── plan.md              # this file
├── research.md          # decisions D1–D10
├── data-model.md        # entities + dacpac DDL
├── contracts/ui-and-routes.md
├── quickstart.md        # validation + E2E map
├── spec.md              # evolved 2026-06-10
├── review_brief.md
└── REVIEW-SPEC.md
```

### Source changes (repository root)
```text
src/
  FundingPlatform.Domain/
    Entities/Fund.cs                         # NEW aggregate
    Enums/FundStatus.cs                      # NEW
    Exceptions/FundArchivedException.cs      # NEW
    Entities/Process.cs                      # + FundId, Fund nav, Create(fundId), SetFund
    Entities/Application.cs                  # + GroupId, Group nav, IsFrozen + freeze guards
    Entities/AdminAuditEvent.cs              # + fund.* action/target constants
  FundingPlatform.Application/
    Abstractions/Storage/FileCategory.cs     # + FundRegulation
    Abstractions/Storage/StorageOptions.cs   # + FundRegulation category
    Abstractions/IApplicationQueryFilter.cs  # + ExcludeArchivedFund
    Abstractions/IFundService.cs             # NEW (or service interface)
    Services/ApplicationService.cs           # capture GroupId on create
    Services/SubmitApplicationHandler.cs     # Plantilla via anchor
  FundingPlatform.Infrastructure/
    Services/FundService.cs                  # NEW (CRUD + audit + storage)
    Persistence/Configurations/FundConfiguration.cs        # NEW
    Persistence/Configurations/ProcessConfiguration.cs     # + Fund FK
    Persistence/Configurations/ApplicationConfiguration.cs # + Group FK
    Persistence/ApplicationQueryFilter.cs    # ExcludeArchivedFund + apply at non-admin read sites
    Services/GetApplicationReviewProjection.cs             # Plantilla via anchor
    Persistence/Reports/ReportQueryService.cs              # Fund filter/column
  FundingPlatform.Web/
    Controllers/Admin/AdminFundsController.cs # NEW
    Controllers/Admin/AdminProcessesController.cs          # Fund selector + list filter
    Controllers/ApplicationController.cs       # GroupId capture + freeze guards
    Controllers/QuotationController.cs         # freeze guards
    Controllers/FundRegulationController.cs    # NEW applicant download (or action)
    Controllers/Admin/AdminReportsController.cs            # Fund filter param
    ViewModels/Admin/AdminFundViewModels.cs    # NEW
    Resources/AdminFundsResources.cs           # NEW es-CR
    Views/Admin/Funds/{Index,Create,Edit,Details}.cshtml   # NEW
    Views/Admin/Processes/{Index,Create}.cshtml            # Fund column/selector
    Views/Application/Create.cshtml            # Group selector
    Views/Admin/Reports/{Applications,FundedItems,Aging}.cshtml # Fund filter
    Views/Shared/_Layout.cshtml                # sidebar "Fondos"
  FundingPlatform.Database/
    Tables/dbo.Funds.sql                       # NEW
    Tables/dbo.Processes.sql                   # + FundId, FK, index
    Tables/dbo.Applications.sql                # + GroupId, FK, index
    PostDeployment/0X_SeedFunds.sql            # NEW (ordered before Process/Group seeds)
tests/
  Tests.Unit/        Fund domain, freeze guard, anchor resolution
  Tests.Integration/ FK + ExcludeArchivedFund + reports join (real DB)
  Tests.E2E/         US1–US6 Playwright classes
```

**Structure Decision**: Standard Clean Architecture layout already in the repo; the feature slots a new aggregate + admin slice and threads two cross-cutting changes (anchor, freeze) through existing Application/Process/reports code.

## Implementation Phases (suggested ordering for `/speckit-tasks`)

1. **Schema + Domain**: `dbo.Funds`, `Processes.FundId`, `Applications.GroupId`; `Fund`/`FundStatus`/`FundArchivedException`; Process/Application/AuditEvent edits; EF configs. (Build green, integration FK tests.)
2. **Application anchor (US6)**: create-flow Group selector + capture; Plantilla resolution via anchor; seed anchors. (Unit + E2E US6.)
3. **Admin Fund CRUD + regulation (US1)**: `FundService` (+audit+storage), controller, views, resources, sidebar; `FileCategory.FundRegulation` + storage options. (E2E US1.)
4. **Process ↔ Fund (US2)** and **regulation download (US3)**: Process selector + list column/filter; applicant download. (E2E US2, US3.)
5. **Force-freeze (US4)**: `ExcludeArchivedFund` filter at all non-admin read sites + controller/domain mutation guards. (Unit + Integration + E2E US4.)
6. **Reports Fund filter/column (US5)**: request/row DTOs, query clause, CSV, views. (E2E US5.)
7. **Full E2E suite** green (delivery bar).

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Authoritative `Application.GroupId` anchor (+create-flow selector, Plantilla refactor) | Product-owner chose exact Fund-on-reports; the existing model has no deterministic Application→Process link (Plantilla used `FirstOrDefault`) | Approximating Fund via group-membership overlap is ambiguous (applicant in multiple Processes) and would make reports/freeze unreliable |
| Force-freeze (`ExcludeArchivedFund` across ~9 read sites + controller/domain mutation guards) | Product-owner chose immediate freeze of in-flight work on archive | Existing `Process.Close` *blocks* until no active work — explicitly not the chosen behavior; "block new only" leaves in-flight work live, contradicting "freeze any action" |

Both deviations were surfaced to and chosen by the product owner during planning and are recorded in spec.md → *Planning Evolution*.

## Risks & mitigations

- **Freeze filter omission**: missing a read site leaves archived-Fund apps visible. Mitigation: enumerate every `ExcludeDeleted` call site (research D6) and pair the new filter; integration test asserts non-admin invisibility per surface.
- **Anchor capture UX**: new applicant-facing step. Mitigation: auto-select the single-group case (no new friction for the common path); E2E US6 covers all three cardinalities. (Open item OI-1.)
- **Reviewer visibility regression**: anchor must not narrow what reviewers see. Mitigation: keep the group-overlap predicate; anchor is additive (Out of Scope note).
- **Seed ordering**: Fund must exist before Processes (required FK). Mitigation: post-deploy script ordered first; idempotent MERGE.

## Open items — RESOLVED at plan review (2026-06-10)

- OI-1: ✅ applicant create selector = **auto (1 eligible) / choose (many) / block (none)** per FR-018.
- OI-2: ✅ `fund-regulations` size cap = **20 MiB** (matches signed-agreement cap).
- OI-3: ✅ report Fund filter **includes archived Funds** for admin visibility.
