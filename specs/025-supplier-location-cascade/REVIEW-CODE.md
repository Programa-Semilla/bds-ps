# REVIEW-CODE — Supplier Branch Location Cascade (025)

Code-vs-spec compliance log. Tracked deviations to revisit post-merge.

## Status: implemented; full E2E delivery run pending green confirmation.

## Deviations

### Deviation #1 — `SetLocation` arity vs FR-006 (carried from plan Decision 6)
**Spec**: FR-006 states a branch's location is "all three (Provincia/Cantón/Distrito) or none" at the data layer.
**Code**: `SupplierBranch.SetLocation(provinceId, cantonId, districtId, canton, district)` keeps the spec-021 **province+cantón both-or-neither** rule and adds *district-consistent-if-set*, but **permits a province+cantón pair without a distrito** at the domain layer. The all-three-required guarantee is enforced at the form/controller layer for the three wired surfaces (US1 `SupplierController`, US2 `SupplierController`, US3 `AdminSuppliersController`).
**Why**: the only other `SetLocation` caller — `CreateSupplierBranchHandler` (the spec-021 inline `api/applications/suppliers/create-branch` JSON path) — has **no live UI** (EVOLVE-NOTE-us2-applicant-flow) and is out of scope. Keeping the domain permissive lets that orphaned path compile (it passes `districtId: null`) without dragging it into scope or leaving it half-consistent. Mirrors how spec 023 FR-008 was handled.
**Resolution path**: if the inline path is ever rebuilt with a UI, tighten the domain to strict all-three-or-none and wire its distrito tier. Evolve then.

### Deviation #2 — seed file named `04_SeedDistricts.sql` (tasks said `02_`)
**Tasks**: T004/data-model named the seed `PostDeployment/02_SeedDistricts.sql`.
**Code**: named `04_SeedDistricts.sql` — `02_SeedMigracionInicialProcess.sql` and `03_SeedSupplierAdminRole.sql` already exist. The number is cosmetic; **ordering is enforced by the `:r .\04_SeedDistricts.sql` include placed after `01_SeedProvincesCantons.sql` in `SeedData.sql`** (so cantón Codes the `LEFT(Code,5)` lookup resolves against already exist). No behavioral impact.

### Deviation #3 — T018/T019 live in the E2E project, not Integration
**Tasks**: T018/T019 say "Integration test (real DB)".
**Code**: placed in `tests/FundingPlatform.Tests.E2E/Tests/Supplier/DistrictsCatalogE2E.cs`. The Integration project runs on **EF InMemory** (47 of 48 files) and never deploys the dacpac, so the post-deploy distrito seed does not exist there — the SC-007 oracle and the `/api/districts` contract can only be asserted against the AspireFixture's real seeded SQL Server. This matches the project's documented strategy ("the real SQL post-deploy block is exercised end-to-end by the E2E AspireFixture run, not here" — `MigrationTests.cs`).

## Notes (not deviations)

- **Single-branch picker radio dispatch** (pre-existing spec-013 behavior, surfaced by the US2 E2E): when a supplier has exactly one branch, `_BranchPicker` renders the collapsed default-branch radio `checked`, so a new-branch submit also posts `SelectedBranchId` and the controller's existing-branch path wins. The US2 E2E clears the radio to exercise the new-branch cascade. Orthogonal to spec 025 (the cascade itself is correctly wired on that path); flag for product if "add new branch on a single-branch supplier" needs to work without clearing the radio.
- **One quotation per supplier per item** (pre-existing): the US2 E2E quotes the existing supplier's new branch on a *second* item, since a supplier may carry only one quotation per item.
- Display continuity (FR-013) confirmed: `_BranchPicker.cshtml:45` (`@branch.Province`) and admin `Detail.cshtml` (`@b.Province`) read the legacy `Province` string, now server-composed as `"Distrito, Cantón, Provincia"`. No display-surface changes needed (T034).
