# Implementation Plan: Centralized Supplier Catalog with Multi-Branch Support and Admin-Controlled Compliance

**Branch**: `013-supplier-catalog` | **Date**: 2026-04-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification at `/specs/013-supplier-catalog/spec.md`

## Summary

Convert the current single-row-per-legal-ID `Suppliers` model into a structured catalog: one canonical `Supplier` per legal ID with N `SupplierBranches` underneath, four admin-only flags (CCSS / Hacienda / SICOP / electronic-invoice), and a `Draft → PendingReview → Verified | Rejected` lifecycle on the supplier identity. Quotations gain a `SupplierBranchId` foreign key; the existing `Quotations.SupplierId` stays as a denormalized helper (invariant: equals the branch's `SupplierId`). `SupplierScore.ComputeForItem` (spec 003) signature shifts to `(Quotation, Supplier, SupplierBranch)` triples; the math is unchanged. New applicant UX: search by legal ID, branch picker, "add new branch" affordance, no compliance/e-invoice checkboxes. New admin UX: `Admin/Suppliers` page list + detail + edit + verify/reject — slots into the spec 009 admin shell. Migration is forward-only single-transaction: every existing supplier is marked `Verified` with current compliance flags preserved, gets one default `Sede principal` branch carrying its prior contact data, and quotations are repointed via JOIN; assertion checks abort on inconsistency. Day-one parity on existing recommendations is the primary success criterion (SC-003).

## Technical Context

**Language/Version**: C# / .NET 10.0 (matches all prior specs).
**Primary Dependencies**: ASP.NET MVC, Entity Framework Core 10.0 (data access only), ASP.NET Identity, .NET Aspire. No new managed dependencies. Reuses existing static-asset stacks: Tabler.io (spec 008), Fraunces / Inter / JetBrains Mono / canvas-confetti (spec 011), Syncfusion HTML-to-PDF (spec 005, untouched by this feature).
**Storage**: SQL Server (Aspire-managed for dev, dacpac schema management). **Schema change**: new `dbo.SupplierBranches` table (1:N under `dbo.Suppliers`); five new columns on `dbo.Suppliers` (`VerificationStatus`, `CreatedByApplicantId`, `VerifiedByUserId`, `VerifiedAt`, `RejectionReason`); six existing columns dropped from `dbo.Suppliers` (`ContactName`, `Email`, `Phone`, `Location`, `ShippingDetails`, `WarrantyInfo`) after migration; one new column on `dbo.Quotations` (`SupplierBranchId`). One filtered unique index on `SupplierBranches (SupplierId, IsDefault)` enforces single default per supplier. Local file system for documents (existing). No new storage subsystems.
**Testing**: xUnit unit tests, xUnit integration tests with WebApplicationFactory + Aspire fixture, Playwright for .NET (NUnit) E2E. Per the project testing convention, E2E uses ephemeral SQL via the `--EphemeralStorage=true` flag in `AspireFixture`.
**Target Platform**: Linux server (Aspire-orchestrated containers in dev; production target unchanged).
**Project Type**: Web service (ASP.NET MVC monolith with Clean Architecture layers).
**Performance Goals**: Per spec SC-006, the migration completes in under 60 seconds against the production database. Per NFR-004, supplier-by-legal-ID lookup is debounced client-side (250 ms) and rate-limited server-side (existing IP rate limit suffices). No new performance budgets beyond the existing project baseline.
**Constraints**: Schema-First Database Management (Constitution IV) means dacpac is the sole source of truth — all schema changes flow through `.sql` files in `src/FundingPlatform.Database/`, with seed/migration logic in `PostDeployment/`. EF Core migrations are prohibited. ASP.NET Identity sentinel (system-admin user from spec 009) is a hard dependency (FR-063). `Quotations.SupplierId` invariant must be enforced at the application layer (`AddQuotation` writes both `SupplierBranchId` and `SupplierId` from the same branch).
**Scale/Scope**: 7 user stories (5 P1, 2 P2), 30+ functional requirements, ~50–100 supplier rows in production today (small dataset; migration will be sub-second on this volume). Approximately 2 new domain entities (`Supplier` aggregate enriched with `Branches` collection + `VerificationStatus` enum, `SupplierBranch` entity), 1 new admin controller, 1 modified applicant controller, 1 algorithm signature change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance | Notes |
|---|---|---|
| **I. Clean Architecture** | ✅ | All new code follows the four-layer rule. `Supplier` and `SupplierBranch` entities + the `VerificationStatus` enum live in `Domain/Entities` and `Domain/Enums`. New repository interface `ISupplierBranchRepository` + the extended `ISupplierRepository` live in `Domain/Interfaces`. New commands (`CreateDraftSupplierCommand`, `AddBranchCommand`, `VerifySupplierCommand`, `RejectSupplierCommand`, `EditSupplierCommand`, `EditBranchCommand`) and the modified `AddSupplierQuotationCommand` live in `Application/Suppliers/Commands`. EF configurations live in `Infrastructure/Persistence/Configurations`. Controllers live in `Web/Controllers/` (applicant flow) and `Web/Controllers/Admin/` (admin flow). |
| **II. Rich Domain Model** | ✅ | The `Supplier` aggregate root owns its lifecycle methods: `SubmitForReview()`, `Verify(string verifierUserId)`, `Reject(string verifierUserId, string reason)`, plus `AddBranch(...)`, `EditBranch(...)`, `MarkDefaultBranch(...)`. Validation invariants (legal ID uniqueness, exactly one default branch, status transitions) are enforced inside the entity. The `SupplierScore` value object continues to live in `Domain/ValueObjects` and gains the two new flags (`IsSupplierVerified`, `IsSupplierRejected`) without leaking framework concerns. |
| **III. End-to-End Testing (NON-NEGOTIABLE)** | ✅ | Each of the 7 user stories from the spec gets at least one Playwright E2E test under `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/` and `tests/FundingPlatform.Tests.E2E/Tests/Admin/Suppliers/`. Page Object Model additions: `AddQuotationPage` extended with the supplier-search-and-pick flow, new `AdminSuppliersListPage`, `AdminSupplierDetailPage`. Pre-existing E2E tests for spec 003 (review screen, recommendation badge) are amended to assert the two new score flags. |
| **IV. Schema-First Database Management** | ✅ | All schema changes ship as edits to `.sql` files in `src/FundingPlatform.Database/Tables/` plus a new pre/post-deployment script. **No EF migrations.** Migration logic (column adds, table adds, default-branch creation, FK-rewire, column drops, assertion checks) lives in a new `src/FundingPlatform.Database/PostDeployment/Migrations/013_SupplierCatalog.sql` invoked from `SeedData.sql`. |
| **V. Specification-Driven Development** | ✅ | This plan is the artifact. `tasks.md` will follow via `/speckit-tasks`. |
| **VI. Simplicity and Progressive Complexity** | ✅ | YAGNI applied throughout: no province enum, no audit log table, no notifications, no admin-direct-create, no soft-delete, no merge tooling, no external API integrations — all explicitly out of scope per the spec's Assumptions block. The branch entity is justified by an explicit current need (multi-office reality stated in the seed); no speculative tables added. |

**Gate result: PASS.** No principle violations to track.

### Post-Design Re-evaluation

After completing Phase 0 (research.md) and Phase 1 (data-model.md, contracts/, quickstart.md), the constitution check is re-run:

| Principle | Re-evaluation |
|---|---|
| **I. Clean Architecture** | Reaffirmed. The phase-1 artifacts allocate every new file to its correct layer (domain → application → infrastructure → web). No layer leaks. |
| **II. Rich Domain Model** | Reaffirmed and strengthened. `data-model.md` shows the `Supplier` aggregate owning all branch CRUD (R2). Lifecycle methods (`SubmitForReview`, `Verify`, `Reject`, `EditByAdmin`, `RenameByApplicant`, `AddBranch`, `EditBranch`) live on the entity. Domain guards on illegal transitions are documented (e.g., `Verify` on a Draft throws). |
| **III. End-to-End Testing (NON-NEGOTIABLE)** | Reaffirmed. `quickstart.md` walks every user story manually; the planned Playwright POMs cover all 7 stories. `MigrationTests.cs` (integration, not E2E) covers the migration SC-003 parity check — appropriate test layer per R8. |
| **IV. Schema-First Database Management** | Reaffirmed. R3 documents the dacpac-only migration path; `data-model.md` shows the SQL with assertion-guarded post-deployment script. No EF Core migrations introduced. Legacy columns parked in `dbo.Suppliers.sql` for one release with a `TODO[013-cleanup]` marker (matches spec 010 currency rollout precedent). |
| **V. Specification-Driven Development** | Reaffirmed. plan.md / research.md / data-model.md / contracts/ all complete; tasks.md follows via `/speckit-tasks`. |
| **VI. Simplicity and Progressive Complexity** | Reaffirmed. Research surfaced one borderline complexity (R3 dacpac migration mechanics) and chose the established team pattern (legacy columns survive one release) over a more elaborate two-deploy alternative. No speculative abstractions added. |

**Post-design gate result: PASS.** Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/013-supplier-catalog/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── http-routes.md   # Controller route table + verbs + ViewModels
│   └── permission-matrix.md  # Authorization rules per route
├── REVIEW-SPEC.md       # Already created during /speckit-specify gate review
├── review_brief.md      # Already created during /speckit-specify
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/
│   │   ├── Supplier.cs                # MODIFIED: aggregate root; gains Branches collection,
│   │   │                                #          VerificationStatus, lifecycle methods
│   │   └── SupplierBranch.cs          # NEW: branch entity
│   ├── Enums/
│   │   └── SupplierVerificationStatus.cs   # NEW: Draft / PendingReview / Verified / Rejected
│   ├── Interfaces/
│   │   ├── ISupplierRepository.cs           # MODIFIED: gains GetByIdWithBranchesAsync,
│   │   │                                    #           ListPendingAsync, etc.
│   │   └── ISupplierBranchRepository.cs    # NEW (or expose branch ops via ISupplierRepository
│   │                                        # — see research.md decision)
│   └── ValueObjects/
│       └── SupplierScore.cs            # MODIFIED: ComputeForItem signature change
│                                       #           + two new result flags
├── FundingPlatform.Application/
│   ├── Suppliers/                      # NEW namespace folder
│   │   ├── Commands/
│   │   │   ├── CreateDraftSupplierCommand.cs
│   │   │   ├── EditDraftSupplierCommand.cs
│   │   │   ├── AddBranchCommand.cs
│   │   │   ├── EditBranchCommand.cs
│   │   │   ├── VerifySupplierCommand.cs
│   │   │   ├── RejectSupplierCommand.cs
│   │   │   ├── EditSupplierCommand.cs        # admin-only fields (compliance + e-invoice)
│   │   │   └── EditBranchByAdminCommand.cs
│   │   ├── Queries/
│   │   │   ├── SearchSupplierByLegalIdQuery.cs
│   │   │   ├── ListSuppliersForAdminQuery.cs # filter / search
│   │   │   └── GetSupplierDetailQuery.cs     # supplier + branches + applications referencing it
│   │   ├── DTOs/
│   │   │   ├── SupplierLookupResultDto.cs
│   │   │   ├── SupplierDetailDto.cs
│   │   │   └── SupplierBranchDto.cs
│   │   └── Services/
│   │       └── SupplierCatalogService.cs    # transactional orchestration
│   ├── Applications/Commands/
│   │   └── AddSupplierQuotationCommand.cs   # MODIFIED: replaces flat supplier fields with
│   │                                        # SupplierBranchId (existing) or
│   │                                        # NewSupplier/NewBranch payload (draft creation)
│   └── Services/
│       ├── ApplicationService.cs            # MODIFIED: AddSupplierQuotationAsync rewritten;
│       │                                    # SubmitAsync now flips owned Drafts to PendingReview
│       └── ReviewService.cs                 # MODIFIED: passes SupplierBranch into SupplierScore
├── FundingPlatform.Infrastructure/
│   └── Persistence/
│       ├── Configurations/
│       │   ├── SupplierConfiguration.cs           # MODIFIED: drops moved columns,
│       │   │                                       #           adds new ones, configures
│       │   │                                       #           Branches navigation
│       │   └── SupplierBranchConfiguration.cs     # NEW: filtered unique index on IsDefault
│       └── Repositories/
│           └── SupplierRepository.cs               # MODIFIED: branch-aware queries
├── FundingPlatform.Database/                       # dacpac project
│   ├── Tables/
│   │   ├── dbo.Suppliers.sql                       # MODIFIED: schema reshape
│   │   ├── dbo.SupplierBranches.sql               # NEW
│   │   └── dbo.Quotations.sql                      # MODIFIED: add SupplierBranchId column + FK
│   └── PostDeployment/
│       ├── SeedData.sql                            # MODIFIED: invoke new migration script
│       └── Migrations/
│           └── 013_SupplierCatalog.sql             # NEW: idempotent migration with assertions
└── FundingPlatform.Web/
    ├── Controllers/
    │   ├── SupplierController.cs                    # MODIFIED: search-by-legalId,
    │   │                                            # branch-picker partial,
    │   │                                            # add-new-branch action
    │   └── Admin/
    │       └── AdminSuppliersController.cs          # NEW: list / detail / edit / verify / reject
    ├── ViewModels/
    │   ├── AddSupplierViewModel.cs                  # MODIFIED: drops compliance & e-invoice;
    │   │                                            # gains BranchPicker and AddBranch payloads
    │   ├── SupplierLookupResultViewModel.cs        # NEW
    │   └── Admin/
    │       ├── AdminSupplierListViewModel.cs        # NEW
    │       └── AdminSupplierDetailViewModel.cs      # NEW
    └── Views/
        ├── Supplier/
        │   ├── Add.cshtml                           # MODIFIED: rewritten as a step-flow
        │   └── _BranchPicker.cshtml                 # NEW partial
        └── Admin/
            └── Suppliers/                           # NEW folder
                ├── Index.cshtml
                └── Detail.cshtml

tests/
├── FundingPlatform.Tests.Unit/
│   ├── Domain/
│   │   ├── SupplierTests.cs                         # NEW: lifecycle method invariants
│   │   ├── SupplierBranchTests.cs                   # NEW
│   │   └── SupplierScoreTests.cs                    # MODIFIED: new signature + flags
│   └── Application/
│       └── SupplierCatalogServiceTests.cs           # NEW
├── FundingPlatform.Tests.Integration/
│   └── Persistence/
│       ├── SupplierRepositoryTests.cs               # NEW: filtered unique index, search
│       └── MigrationTests.cs                        # NEW: dry-run the migration on a seeded DB
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/
    │   ├── AddQuotationPage.cs                      # MODIFIED: branch-picker affordances
    │   └── Admin/
    │       ├── AdminSuppliersListPage.cs            # NEW
    │       └── AdminSupplierDetailPage.cs           # NEW
    └── Tests/
        ├── Suppliers/
        │   ├── ApplicantReusesVerifiedSupplierTests.cs    # User Story 1
        │   ├── ApplicantAddsNewBranchTests.cs              # User Story 2
        │   ├── ApplicantCreatesDraftSupplierTests.cs       # User Story 3
        │   └── SubmitFlipsDraftToPendingTests.cs           # User Story 4
        └── Admin/Suppliers/
            ├── AdminVerifiesPendingTests.cs                 # User Story 5
            ├── AdminEditsVerifiedTests.cs                    # User Story 6
            └── AdminFiltersQueueTests.cs                     # User Story 7
```

**Structure Decision**: Continue using the existing single-monolith Clean Architecture layout (`src/FundingPlatform.{Domain,Application,Infrastructure,Web}` + `src/FundingPlatform.Database` for dacpac). The admin Suppliers screens slot into `Web/Controllers/Admin/` + `Web/Views/Admin/Suppliers/` per the spec 009 + spec 010 pattern (see `AdminUsersController` and `AdminReportsController` as templates). No new project, no new layer.

## Complexity Tracking

> No Constitution violations to justify. The spec applies YAGNI consistently and all proposed additions serve current, stated needs.
