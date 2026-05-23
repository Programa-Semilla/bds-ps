# Tasks: Supplier Branch Location Cascade (Provincia → Cantón → Distrito)

**Input**: Design documents from `specs/025-supplier-location-cascade/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/districts-api.md

**Tests**: INCLUDED — constitution Principle III (E2E non-negotiable) + plan requires unit/integration/E2E.

**Organization**: Phase 2 builds the shared catalog/schema/domain/API (all stories depend on it). Each user story then wires exactly one surface and is independently testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (user-story phases only)

---

## Phase 1: Setup (data sourcing)

**Purpose**: De-risk the seed by pinning the authoritative distrito enumeration before any SQL is written.

- [X] T001 Compile + reconcile the authoritative distrito enumeration to the existing 84-cantón catalog and save it as an intermediate data file `specs/025-supplier-location-cascade/contracts/districts-seed-data.md` (or `.csv`): one row per distrito keyed `'PP_CC_DD'` with name + parent cantón code. Source per research.md Decision 5 (josuenoel gist for bulk + INEC/Wikipedia Anexo reconciliation). MUST satisfy per-province totals 123/116/51/47/61/60/30, Golfito `06_07`=3, Monteverde `06_12`=1, Puerto Jiménez `06_13`=1, and every `PP_CC` prefix must equal an existing cantón Code in `01_SeedProvincesCantons.sql`. No code.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user-story wiring can begin until this phase is complete. This builds the shared catalog, schema, domain invariant, API, cascade asset, and DTO seam used by all three surfaces.

### Database (schema-first)

- [X] T002 [P] Create `src/FundingPlatform.Database/Tables/dbo.Districts.sql` mirroring `dbo.Cantons.sql`: `Id` identity PK, `CantonId INT NOT NULL`, `Code CHAR(8) NOT NULL`, `Name NVARCHAR(80) NOT NULL`, `UX_Districts_Code` unique, `FK_Districts_Cantons` (ON DELETE NO ACTION), `IX_Districts_CantonId`.
- [X] T003 Add `[DistrictId] INT NULL` column + `CONSTRAINT [FK_SupplierBranches_Districts] FOREIGN KEY ([DistrictId]) REFERENCES [dbo].[Districts]([Id]) ON DELETE NO ACTION` to `src/FundingPlatform.Database/Tables/dbo.SupplierBranches.sql`.
- [X] T004 Create `src/FundingPlatform.Database/PostDeployment/02_SeedDistricts.sql` — MERGE-idempotent (mirror `01_SeedProvincesCantons.sql` shape), ~488 rows from T001 data, resolving `CantonId` via cantón `Code` lookups (not identity).
- [X] T005 Add `:r .\02_SeedDistricts.sql` include to `src/FundingPlatform.Database/PostDeployment/Script.PostDeployment.sql` AFTER the cantones seed (verify ordering so cantón Codes exist).

### Domain

- [X] T006 [P] Create `src/FundingPlatform.Domain/Entities/District.cs` mirroring `Canton.cs` (`Id`, `CantonId`, `Code`, `Name`, `Canton?` nav; ctor `District(int cantonId, string code, string name)` with guards).
- [X] T007 Extend `src/FundingPlatform.Domain/Entities/SupplierBranch.cs`: add `DistrictId` (`int?`) + `DistrictRef` (`District?`); extend `SetLocation` to `(int? provinceId, int? cantonId, int? districtId, Canton? canton, District? district)` per data-model.md (keep province+cantón both-or-neither; add district-consistent-if-set).

### Infrastructure

- [X] T008 [P] Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/DistrictConfiguration.cs` mirroring `CantonConfiguration.cs` (`Code` `HasMaxLength(8).IsFixedLength()`, `Name` `HasMaxLength(80)`, `UX_Districts_Code`, `IX_Districts_CantonId`).
- [X] T009 Add to `src/FundingPlatform.Infrastructure/Persistence/Configurations/SupplierBranchConfiguration.cs`: `Property(b => b.DistrictId)` + `HasOne(b => b.DistrictRef).WithMany().HasForeignKey(b => b.DistrictId).OnDelete(DeleteBehavior.NoAction)`.
- [X] T010 Add `DbSet<District> Districts => Set<District>();` to `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs`.
- [X] T011 [P] Create `ILocationCatalogReader` in `src/FundingPlatform.Application/Abstractions/Location/` (returns the district→cantón→province chain incl. entities, or null) + `LocationCatalogReader` impl in `src/FundingPlatform.Infrastructure/Location/` over `AppDbContext`; register in DI.

### API + shared Web assets

- [X] T012 [P] Create `src/FundingPlatform.Web/Controllers/DistrictsApiController.cs` mirroring `CantonsApiController` — `GET /api/districts?cantonId={id}` → `[{id,name}]` ordered by name, `[AllowAnonymous]`, `Cache-Control: public, max-age=3600` (per contracts/districts-api.md).
- [X] T013 Generalize `src/FundingPlatform.Web/wwwroot/js/province-canton-cascade.js` into data-driven `location-cascade.js` (read `data-cascade-endpoint`/`data-cascade-param`/`data-cascade-placeholder`; chain bubbling `change` so province→cantón→distrito reset propagates); update the `<script>` reference(s); preserve province→cantón behavior exactly.
- [X] T014 Rename/extend `ProvinceCantonCascadeViewModel` → `LocationCascadeViewModel` (`src/FundingPlatform.Web/ViewModels/`) adding `DistrictFieldName`/`Districts`/`SelectedDistrictId`; rename/extend `Views/Shared/_ProvinceCantonCascade.cshtml` → `_LocationCascade.cshtml` with a third `<select>` (district select id derived from field name; cantón select gains `data-cascade-source="canton"` + endpoint/param/target attrs).
- [X] T015 [P] Add `int? ProvinceId / CantonId / DistrictId` to `AddBranchInput` in `src/FundingPlatform.Application/Applications/Commands/AddSupplierQuotationCommand.cs`.
- [X] T016 Update `CreateSupplierBranchHandler` (Infrastructure) to the new `SetLocation` signature (pass `districtId: null, district: null`) so it compiles — orphaned inline path, deviation per plan Decision 6.

### Foundational tests

- [X] T017 [P] Unit tests in `tests/FundingPlatform.Tests.Unit` for `SetLocation` 3-tier: all-three-valid sets refs; district set without cantón throws; `district.CantonId != cantonId` throws; province+cantón without district allowed; all-null allowed.
- [X] T018 [P] Integration test in `tests/FundingPlatform.Tests.Integration` (real DB): `GET /api/districts?cantonId={id}` returns that cantón's distritos ordered by name with the public cache header; unknown id → empty array.
- [X] T019 [P] Integration test (real DB, the SC-007 oracle): every one of the 84 cantones has ≥1 district; per-province totals 123/116/51/47/61/60/30; Golfito `06_07`=3, Monteverde `06_12`=1, Puerto Jiménez `06_13`=1; every district `Code` is `'PP_CC_DD'` whose `PP_CC` prefix is an existing cantón Code.

**Checkpoint**: Catalog seeded + validated, domain invariant + API + cascade asset ready. Surfaces can now be wired in parallel.

---

## Phase 3: User Story 1 — Applicant new supplier (Priority: P1) 🎯 MVP

**Goal**: New-supplier panel on `/Application/{id}/Item/{itemId}/Supplier/Add` collects the principal branch location via the 3-tier cascade; submit persists `ProvinceId/CantonId/DistrictId`.

**Independent Test**: As an applicant, trigger the Nuevo proveedor panel, pick Provincia→Cantón→Distrito (each narrows the next), submit → branch persisted with the three FKs + composed display string; incomplete location rejected.

- [X] T020 [US1] Add a provinces-SelectList loader + `LocationCascadeViewModel` builder helper; thread it through `SupplierController.Add` (GET) and `Search` so the `_LookupEmpty` partial receives provinces with field prefix `NewSupplier.FirstBranch.` (`src/FundingPlatform.Web/Controllers/SupplierController.cs`).
- [X] T021 [US1] Replace the Provincia text input in `src/FundingPlatform.Web/Views/Supplier/_LookupEmpty.cshtml` with the `_LocationCascade` partial; add `int? ProvinceId/CantonId/DistrictId` to `AddBranchInputViewModel` in `src/FundingPlatform.Web/ViewModels/AddSupplierViewModel.cs` (drop the free-text Province binding).
- [X] T022 [US1] In `SupplierController` POST new-supplier path: resolve+validate the submitted chain via `ILocationCatalogReader`, add aggregated `ModelState` errors for missing/inconsistent levels (es-CR copy per data-model.md), compose the `"Distrito, Cantón, Provincia"` display string, and pass location to the service.
- [X] T023 [US1] Extend `Supplier.CreateDraft` (`Domain/Entities/Supplier.cs`) + `SupplierCatalogService.CreateDraftWithBranchAsync` to accept the location (ids + Canton + District) and call `branch.SetLocation(...)`; pass the composed string as the legacy `province`.
- [X] T024 [US1] Add client-side required validation on the three selects in the new-supplier panel (no silent submit; aligns with server rules).
- [X] T025 [P] [US1] E2E in `tests/FundingPlatform.Tests.E2E` (Page Object): applicant journey — open Supplier/Add, force Nuevo proveedor, assert Cantón narrows on Provincia and Distrito narrows on Cantón, submit success + persistence; and the incomplete-location rejection path.

**Checkpoint**: US1 fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 — Applicant new branch on existing supplier (Priority: P2)

**Goal**: The add-new-branch panel for an existing supplier uses the same 3-tier cascade.

**Independent Test**: Look up a Verified supplier, open Agregar nueva sucursal, complete the cascade, submit → new branch carries the location; incomplete rejected.

- [X] T026 [US2] Render the `_LocationCascade` partial in the new-branch panel of `src/FundingPlatform.Web/Views/Supplier/_BranchPicker.cshtml` with field prefix `NewBranch.` (replacing the Provincia text input); ensure the host supplies provinces (extend the `Search`/`Add` `_LookupHit`/branch-picker model path or a child-action render).
- [X] T027 [US2] In `SupplierController` POST new-branch path: resolve+validate+compose+pass location (reuse the helper from T022).
- [X] T028 [US2] Extend `Supplier.AddBranch` + `SupplierCatalogService.AddBranchUnderExistingSupplierAsync` to accept the location and call `SetLocation`; pass the composed string as legacy `province`.
- [X] T029 [P] [US2] E2E (Page Object): applicant add-branch-on-existing-supplier cascade journey (narrowing + persist + incomplete rejected).

**Checkpoint**: US1 + US2 both independently functional.

---

## Phase 5: User Story 3 — Admin branch edit (Priority: P3)

**Goal**: Admin supplier Detail branch-edit form uses the same cascade, pre-selected to current values.

**Independent Test**: As admin, open a branch edit, see Provincia/Cantón/Distrito pre-selected (when set), change + save → branch reflects new location; incomplete rejected.

- [X] T030 [US3] Add `int? ProvinceId/CantonId/DistrictId` to `AdminEditBranchViewModel`; in `AdminSuppliersController.Detail` build a per-branch `LocationCascadeViewModel` including the pre-selected province + its cantones + the cantón's distritos (so the edit form renders selected) (`src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs`).
- [X] T031 [US3] Replace the Provincia text input in the branch-edit form of `src/FundingPlatform.Web/Views/Admin/Suppliers/Detail.cshtml` with the `_LocationCascade` partial.
- [X] T032 [US3] In `AdminSuppliersController.EditBranch`: resolve+validate+compose+pass location; extend `Supplier.EditBranch` to accept the location and call `SetLocation`.
- [X] T033 [P] [US3] E2E (Page Object): admin branch-edit cascade journey (pre-selected values, change, save, incomplete rejected).

**Checkpoint**: All three surfaces independently functional.

---

## Phase 6: Polish & Cross-Cutting

- [X] T034 Confirm display continuity: `_BranchPicker.cshtml` (~L45) and admin `Detail.cshtml` (~L160) render the composed location string with no change needed; fix if any surface reads a now-stale value.
- [X] T035 Run Unit + Integration suites green (`dotnet test tests/FundingPlatform.Tests.Unit`, `...Tests.Integration`).
- [ ] T036 Run the FULL E2E suite personally and confirm green (delivery bar, CLAUDE.md) — record passed/failed counts in the commit.
- [X] T037 Add the `SetLocation` arity deviation (province+cantón without distrito vs FR-006) to a REVIEW-CODE entry for the feature; confirm it is tracked for post-merge evolve.
- [ ] T038 Run the STAMP/verify gate (tests, code hygiene, spec compliance, drift check).

---

## Dependencies & Execution Order

- **Phase 1 (T001)** → blocks T004 (seed).
- **Phase 2** blocks all user stories. Within it: DB (T002→T003→T004→T005); Domain T006→T007; Infra T008/T010/T011 [P], T009 after T007; T012/T013/T014/T015 [P]-ish; T016 after T007; tests T017 after T007, T018/T019 after T002–T005.
- **US1 (T020–T025)**, **US2 (T026–T029)**, **US3 (T030–T033)** each depend only on Phase 2; can run in parallel once foundation is done. T022's validate/compose helper is reused by T027/T032 (do US1 first to establish it).
- **Phase 6** after the desired stories.

### Parallel opportunities

- Phase 2: T002, T006, T008, T011, T012, T015 are `[P]` (distinct files). T017/T018/T019 `[P]`.
- Across stories: US1/US2/US3 are independent surfaces; their E2E tasks (T025/T029/T033) are `[P]`.

## Implementation Strategy

1. Phase 1 → Phase 2 (foundation: catalog seeded + validated, domain + API + cascade asset).
2. **US1 = MVP** (the reported surface). STOP and validate independently.
3. Add US2, then US3 — each tested independently.
4. Phase 6: full E2E green (delivery bar) + STAMP.

## Notes

- The single biggest risk is T001/T004 (seed correctness); T019 is its proof — do not mark T004 done until T019 is green.
- `[P]` = different files, no incomplete-task dependency.
- Commit after each task or logical group; commit/push at phase checkpoints (project convention).
