# Implementation Plan: Supplier Branch Location Cascade (Provincia → Cantón → Distrito)

**Branch**: `025-supplier-location-cascade` | **Date**: 2026-05-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/025-supplier-location-cascade/spec.md`

## Summary

Finish spec-021 FR-014 (the never-wired Provincia → Cantón cascade) and extend it to a third level by adding a **Distrito** catalog. Add a `dbo.Districts` table + seed mirroring the existing `dbo.Cantons`, a `District` domain entity, a `SupplierBranches.DistrictId` FK, a `GET /api/districts?cantonId=` endpoint, a generalized data-driven cascade script, and a three-`<select>` partial wired into all three branch-location surfaces (applicant new-supplier, applicant new-branch, admin branch-edit). All three levels are required on those forms; the legacy `SupplierBranch.Province` string column is retained as a server-composed display value so existing display surfaces are untouched. **Pure additive change** — no behavior removed; existing null-location branches stay valid.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, SQL Server (Aspire-managed), Tabler.io (vendored), Playwright (E2E). No new managed dependency.
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`). New table `dbo.Districts`; new nullable FK column `dbo.SupplierBranches.DistrictId`. Seed via PostDeployment MERGE script.
**Testing**: xUnit (Unit + Integration against a real SQL container via `AspireFixture`), Playwright (E2E through the browser).
**Target Platform**: Linux server (Aspire orchestration).
**Project Type**: Web (ASP.NET MVC), Clean Architecture (Domain / Application / Infrastructure / Web).
**Performance Goals**: Cascade fetch is a single indexed lookup per change (`IX_Districts_CantonId`), public-cached 1h like `/api/cantons`. No perf-sensitive path.
**Constraints**: es-CR copy; no CDN (vendored assets only); schema-first DB; rich domain invariant on the aggregate; validation errors aggregated (shown at once).
**Scale/Scope**: 7 provinces × 84 cantones × ~488 distritos (read-only catalog). Three form surfaces, two write paths (`SupplierCatalogService`, `AdminSuppliersController`/`Supplier.EditBranch`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | PASS — `District` entity in Domain; catalog read + location resolution/validation behind an Application abstraction (`ILocationCatalogReader`) implemented in Infrastructure over `AppDbContext`; Web depends inward only. |
| **II. Rich Domain Model** | PASS — the cross-level consistency rule lives on the aggregate (`SupplierBranch.SetLocation`), extended to the district tier. Controllers/services orchestrate; they do not own the invariant. |
| **III. E2E (NON-NEGOTIABLE)** | PASS — three independently-testable user stories, each gets Playwright coverage driving the real applicant/admin journey (golden path + incomplete-location error). Page Object Model. |
| **IV. Schema-First DB** | PASS — `dbo.Districts.sql` + `DistrictId` column edited in the Database project; seed is a PostDeployment MERGE script (`02_SeedDistricts.sql`). No EF migrations. |
| **V. Specification-Driven** | PASS — spec → plan → tasks → implement; this is the plan phase. |
| **VI. Simplicity / Progressive Complexity** | PASS — extends an existing, proven 2-tier pattern by one tier; no speculative abstraction; no new dependency. The one new abstraction (`ILocationCatalogReader`) serves a current need (server-side hierarchy validation reused by 2 write paths). |
| **Quality gate: errors shown at once** | PASS by design — all-3-required messages aggregated into `ModelState` and re-rendered with the form (consistent with spec 023), not surfaced one-at-a-time. |
| **Quality gate: authorization** | PASS — unchanged; `SupplierController.VerifyOwnershipAsync` (applicant owns application) and admin role gate remain. |

**Result: PASS. No violations → Complexity Tracking left empty.**

## Key Design Decisions

1. **Mirror, don't reinvent.** `District` mirrors `Canton` (Id, CantonId FK, `Code CHAR(8)` = `'PP_CC_DD'`, `Name`); `DistrictConfiguration` mirrors `CantonConfiguration`; `DistrictsApiController` mirrors `CantonsApiController`. This keeps the codebase symmetric and review-cheap.

2. **Cascade JS generalization.** Rather than a second bespoke script, generalize the existing `province-canton-cascade.js` into a data-driven helper: a source `<select>` carries `data-cascade-endpoint` (e.g. `/api/cantons` or `/api/districts`), `data-cascade-param` (`provinceId`/`cantonId`), `data-cascade-target`, and `data-cascade-placeholder`. The province→cantón behavior is preserved exactly (regression-safe); cantón→distrito is the same mechanism with different attributes. Chained resets propagate (province change clears cantón **and** distrito).

3. **One partial, three hosts.** `_ProvinceCantonCascade.cshtml` → `_LocationCascade.cshtml` with a third `<select>`; its VM (`ProvinceCantonCascadeViewModel` → `LocationCascadeViewModel`) gains `DistrictFieldName` / `Districts` / `SelectedDistrictId`. Hosts pass field-name prefixes so the same partial binds to `NewSupplier.FirstBranch.*`, `NewBranch.*`, and the admin `*` names without id/name collisions (district select id is derived from the field name, as the cantón select already is).

4. **Server-side hierarchy validation (FR-005, defense-in-depth).** On POST, the active path resolves the submitted `DistrictId` through `ILocationCatalogReader.GetDistrictChainAsync(districtId)` → returns the district with its cantón + province, then asserts `district.CantonId == submittedCantonId` and `canton.ProvinceId == submittedProvinceId`. Mismatch/forged id → aggregated `ModelState` error, no write. This never trusts the client's claimed parent ids.

5. **Display continuity (FR-013).** On successful save the controller/service composes `"{Distrito}, {Cantón}, {Provincia}"` and writes it to the legacy `SupplierBranch.Province` string. Every existing display surface (`_BranchPicker.cshtml:45`, admin `Detail.cshtml:160`, DTOs) keeps reading `Province` unchanged. FK columns are the source of truth.

6. **Domain arity reconciliation (deviation candidate).** `SetLocation` is extended to `(int? provinceId, int? cantonId, int? districtId, Canton? canton, District? district)`. It keeps the existing **province+cantón both-or-neither** rule and adds: *if `districtId` is set, `cantonId` must be set and `district.CantonId == cantonId`.* The **all-three-together** guarantee (spec FR-006/FR-011) is enforced at the form/controller layer for the three wired surfaces. Rationale: the only other `SetLocation` caller — `CreateSupplierBranchHandler` (the spec-021 inline `ApplicationController` path, which has **no live UI**, per EVOLVE-NOTE-us2-applicant-flow) — is out of scope; this signature keeps it compiling without forcing it into scope or leaving it in a half-consistent state. **Flag for REVIEW-CODE as a Deviation** (mirrors how spec 023 FR-008 was handled): domain allows province+cantón without distrito; forms do not. Revisit if the inline path is ever rebuilt.

7. **Write-path threading.** `AddBranchInput` (Application DTO) + `AddBranchInputViewModel` + `AdminEditBranchViewModel` gain `int? ProvinceId/CantonId/DistrictId`. `Supplier.AddBranch` / `Supplier.CreateDraft` / `Supplier.EditBranch` gain the location parameters and call `branch.SetLocation(...)` internally (invariant stays on the aggregate). The Application service resolves the catalog entities via `ILocationCatalogReader` and composes the display string before invoking the aggregate.

## Project Structure

### Documentation (this feature)

```text
specs/025-supplier-location-cascade/
├── spec.md              # /speckit-specify output
├── plan.md              # this file
├── research.md          # Phase 0 — distrito data source + decisions
├── data-model.md        # Phase 1 — District entity, SupplierBranch delta, invariant
├── quickstart.md        # Phase 1 — how to run/verify the cascade end to end
├── contracts/
│   └── districts-api.md  # GET /api/districts?cantonId={id} contract
├── checklists/requirements.md
├── REVIEW-SPEC.md
└── tasks.md             # /speckit-tasks output (not created here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Domain/Entities/
    District.cs                          # NEW — mirrors Canton.cs
    SupplierBranch.cs                    # EDIT — DistrictId + DistrictRef; SetLocation 3-tier
    Supplier.cs                          # EDIT — AddBranch/CreateDraft/EditBranch take location
  FundingPlatform.Application/
    Abstractions/Location/ILocationCatalogReader.cs   # NEW — district-chain lookup
    Suppliers/Services/SupplierCatalogService.cs       # EDIT — resolve+compose+set location
    Applications/Commands/AddSupplierQuotationCommand.cs  # EDIT — AddBranchInput FK ids
  FundingPlatform.Infrastructure/
    Persistence/Configurations/DistrictConfiguration.cs   # NEW — mirrors CantonConfiguration
    Persistence/Configurations/SupplierBranchConfiguration.cs  # EDIT — DistrictId FK
    Persistence/AppDbContext.cs                            # EDIT — DbSet<District>
    Location/LocationCatalogReader.cs                      # NEW — impl over AppDbContext
  FundingPlatform.Web/
    Controllers/DistrictsApiController.cs                  # NEW — mirrors CantonsApiController
    Controllers/SupplierController.cs                      # EDIT — provinces in GET/Search; resolve+validate+compose on POST
    Controllers/Admin/AdminSuppliersController.cs          # EDIT — Detail loads provinces/canton/district; EditBranch sets location
    ViewModels/LocationCascadeViewModel.cs                 # RENAME/EXTEND ProvinceCantonCascadeViewModel (+District)
    ViewModels/AddSupplierViewModel.cs                     # EDIT — AddBranchInputViewModel FK ids
    ViewModels/Admin/*EditBranch*                          # EDIT — FK ids on AdminEditBranchViewModel
    Views/Shared/_LocationCascade.cshtml                   # RENAME/EXTEND _ProvinceCantonCascade (3 selects)
    Views/Supplier/_LookupEmpty.cshtml                     # EDIT — render partial (NewSupplier.FirstBranch.*)
    Views/Supplier/_BranchPicker.cshtml                    # EDIT — render partial (NewBranch.*)
    Views/Admin/Suppliers/Detail.cshtml                    # EDIT — replace Provincia text input with partial
    wwwroot/js/location-cascade.js                         # RENAME/GENERALIZE province-canton-cascade.js (data-driven)
  FundingPlatform.Database/
    Tables/dbo.Districts.sql                               # NEW — mirrors dbo.Cantons.sql
    Tables/dbo.SupplierBranches.sql                        # EDIT — DistrictId column + FK
    PostDeployment/02_SeedDistricts.sql                    # NEW — MERGE-idempotent ~488 rows
    PostDeployment/Script.PostDeployment.sql               # EDIT — :r include 02_SeedDistricts.sql (verify ordering)
tests/
  FundingPlatform.Tests.Unit/        # SetLocation 3-tier invariant; display-string composition
  FundingPlatform.Tests.Integration/ # /api/districts; seed count + FK integrity; create/edit branch persists DistrictId
  FundingPlatform.Tests.E2E/         # US1/US2/US3 cascade journeys (Page Object Model)
```

**Structure Decision**: Existing Clean Architecture web layout (per CLAUDE.md). The feature is a fourth tier layered onto the spec-021 Province/Cantón infrastructure plus form wiring; no structural change.

## Phase 0 — Research (`research.md`)

Resolve the one real unknown — the authoritative **distrito dataset** (count + source + canton-code reconciliation) — via the backgrounded research task, plus record the settled design decisions (cascade-JS generalization, display-string format, SetLocation arity, validation aggregation). Output: `research.md`.

## Phase 1 — Design & Contracts

- `data-model.md`: `District` entity + table; `SupplierBranch` delta (`DistrictId` + nav); `SetLocation` extended invariant; `AddBranchInput`/view-model deltas; display-string rule.
- `contracts/districts-api.md`: `GET /api/districts?cantonId={id}` → `[{ id, name }]`, anonymous, `Cache-Control: public, max-age=3600`, ordered by name.
- `quickstart.md`: run AppHost, navigate the applicant Supplier/Add flow, verify the three-tier narrowing + persistence, and the admin path.
- Update agent context (CLAUDE.md SPECKIT markers) to point at this plan.

## Phase 2 — (deferred to /speckit-tasks)

Task breakdown ordered: DB (table+column+seed) → Domain (District + SetLocation) → Infrastructure (config + DbSet + reader) → API → ViewModels/DTOs → cascade JS + partial → wire 3 surfaces + controller validation/compose → tests (unit, integration, E2E). Each user story independently testable.
