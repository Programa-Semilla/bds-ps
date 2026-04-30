---
description: "Task list for spec 013 — Centralized Supplier Catalog with Multi-Branch Support and Admin-Controlled Compliance"
---

# Tasks: Centralized Supplier Catalog with Multi-Branch Support and Admin-Controlled Compliance

**Input**: Design documents from `/specs/013-supplier-catalog/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: REQUIRED. Constitution Principle III ("End-to-End Testing — NON-NEGOTIABLE") makes Playwright E2E tests mandatory for every user story. Domain unit tests, repository integration tests, and one migration parity integration test are also included where they protect specific invariants.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and demonstrated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file, no incomplete dependency — safe to run in parallel.
- **[Story]**: User story label (US1, US2, …). Setup / Foundational / Polish phases carry no story label.
- Every task description names the exact file path it touches.

## Path Conventions

This is a single-monolith ASP.NET MVC project with Clean Architecture layers:

- `src/FundingPlatform.Domain/`
- `src/FundingPlatform.Application/`
- `src/FundingPlatform.Infrastructure/`
- `src/FundingPlatform.Web/`
- `src/FundingPlatform.Database/` (dacpac)
- `tests/FundingPlatform.Tests.{Unit,Integration,E2E}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Localization assets, the new enum used everywhere, and the resx wiring.

- [X] T001 [P] Add `src/FundingPlatform.Web/Resources/Suppliers.resx` and `Suppliers.es-CR.resx` with the keys listed in `specs/013-supplier-catalog/contracts/http-routes.md` §6 (LookupRejectedMessage, LookupConcurrentBanner, BranchPicker_Title, BranchPicker_AddNew, Branch_Default, PendingVerificationBadge, NewSupplierForm_Hint). [Implemented as `SuppliersResources.cs` static class to match existing es-CR-only inline-string pattern; spec 012 does not use IStringLocalizer/.resx.]
- [X] T002 [P] Add `src/FundingPlatform.Web/Resources/AdminSuppliers.resx` and `AdminSuppliers.es-CR.resx` with the keys listed in `specs/013-supplier-catalog/contracts/http-routes.md` §6 (Page_Title, FilterStatus_*, Verify_Confirm, Reject_RequireReason, RejectedSuppliersBanner). [Implemented as `AdminSuppliersResources.cs` static class.]
- [X] T003 [P] Add `src/FundingPlatform.Domain/Enums/SupplierVerificationStatus.cs` with values `Draft = 0, PendingReview = 1, Verified = 2, Rejected = 3` (byte-backed) per `data-model.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema changes, domain core, EF Core configuration, repository surface, application-service scaffolding, and the migration parity test. Every user story depends on this.

**⚠️ CRITICAL**: No user story work begins until this phase is complete and the migration integration test passes.

### Schema (dacpac)

- [X] T004 Modify `src/FundingPlatform.Database/Tables/dbo.Suppliers.sql` — add `VerificationStatus`, `CreatedByApplicantId`, `VerifiedByUserId`, `VerifiedAt`, `RejectionReason` columns + the two FKs + the `IX_Suppliers_VerificationStatus` and `IX_Suppliers_Name` indexes per `data-model.md` §SQL Schema. Keep `ContactName / Email / Phone / Location / ShippingDetails / WarrantyInfo` declared (nullable) with a `-- TODO[013-cleanup]` comment per research.md R3.
- [X] T005 [P] Add `src/FundingPlatform.Database/Tables/dbo.SupplierBranches.sql` per `data-model.md` §SQL Schema, including the filtered unique index `UX_SupplierBranches_DefaultPerSupplier` on `(SupplierId)` where `IsDefault = 1`.
- [X] T006 [P] Modify `src/FundingPlatform.Database/Tables/dbo.Quotations.sql` — add `SupplierBranchId INT NOT NULL` column, `FK_Quotations_SupplierBranches` constraint (ON DELETE NO ACTION), and `IX_Quotations_SupplierBranchId` index. Leave the existing `UX_Quotations_ItemId_SupplierId` UNIQUE constraint untouched per research.md R1.
- [X] T007 Add `src/FundingPlatform.Database/PostDeployment/Migrations/013_SupplierCatalog.sql` containing the idempotent migration body verbatim from `data-model.md` (sentinel lookup, supplier backfill, branch insert, quotation FK rewire, three THROW assertions, transactional wrap).
- [X] T008 Modify `src/FundingPlatform.Database/PostDeployment/SeedData.sql` — add `:r .\Migrations\013_SupplierCatalog.sql` after the existing seed inclusions.

### Domain

- [X] T009 [P] Add `src/FundingPlatform.Domain/Entities/SupplierBranch.cs` with the field set, constructor, and `Edit(...)` method described in `data-model.md`. Constructor must be `internal` so only `Supplier.AddBranch` can create branches.
- [X] T010 Modify `src/FundingPlatform.Domain/Entities/Supplier.cs` — drop the moved properties (`ContactName / Email / Phone / Location / ShippingDetails / WarrantyInfo`), add the new lifecycle properties (`VerificationStatus`, `CreatedByApplicantId`, `VerifiedByUserId`, `VerifiedAt`, `RejectionReason`), backing-field `_branches` collection + `Branches` read-only navigation, and these methods: `CreateDraft(...)` (static factory), `SubmitForReview()`, `Verify(string verifierUserId)`, `Reject(string verifierUserId, string reason)`, `RenameByApplicant(string newName)`, `EditByAdmin(...)`, `AddBranch(...)`, `EditBranch(int branchId, …)`. Enforce the "exactly one default" invariant inside `AddBranch`.
- [X] T011 [P] Add `tests/FundingPlatform.Tests.Unit/Domain/SupplierTests.cs` covering: factory creates Draft with `IsCompliantCCSS/Hacienda/SICOP/HasElectronicInvoice = false`; `SubmitForReview` is idempotent on non-Draft; `Verify` from `Draft` throws; `Verify` from `PendingReview/Verified/Rejected` succeeds and updates verifier+timestamp; `Reject` requires non-empty reason; `RenameByApplicant` throws unless `Draft`; `AddBranch` rejects a second `IsDefault = true`.
- [X] T012 [P] Add `tests/FundingPlatform.Tests.Unit/Domain/SupplierBranchTests.cs` covering: constructor sets fields and timestamps; `Edit` updates fields and `UpdatedAt`.

### Score signature change

- [X] T013 Modify `src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs` — change `ComputeForItem` signature to accept `List<(Quotation, Supplier, SupplierBranch)>`, add `IsSupplierVerified` + `IsSupplierRejected` to the record, and update `IsRecommended` to `Total == maxScore && !IsSupplierRejected` per research.md R5. Score math is unchanged.
- [X] T014 Modify `tests/FundingPlatform.Tests.Unit/Domain/SupplierScoreTests.cs` — update existing tests for the new signature and add cases asserting `IsRecommended` is `false` for a Rejected supplier even if it ties the max score.

### Repository surface

- [X] T015 Modify `src/FundingPlatform.Domain/Interfaces/ISupplierRepository.cs` — add `Task<Supplier?> GetByLegalIdWithBranchesAsync(string legalId)`, `Task<Supplier?> GetByIdWithBranchesAsync(int id)`, `Task<(IReadOnlyList<Supplier> Items, int Total)> ListForAdminAsync(SupplierAdminFilter filter, int page, int pageSize)`, `Task<int> CountReferencingApplicationsAsync(int supplierId)`. Keep the existing `GetByLegalIdAsync`, `AddAsync`, `GetByIdAsync` for backwards-compat callers. Add `Task UpdateAsync(Supplier supplier)` if not present.
- [X] T016 [P] Add `src/FundingPlatform.Application/Suppliers/Queries/SupplierAdminFilter.cs` (record / class) with fields: `SupplierVerificationStatus? Status`, `string? LegalIdContains`, `string? NameContains`, `bool? HasIncompleteCompliance`.
- [X] T017 Modify `src/FundingPlatform.Infrastructure/Persistence/Repositories/SupplierRepository.cs` to implement the four new methods, eager-loading `Branches` where required and using `Where`-style filters that translate to SQL.
- [X] T018 Modify `src/FundingPlatform.Infrastructure/Persistence/Configurations/SupplierConfiguration.cs` — drop the moved property mappings, add the lifecycle property mappings (`VerificationStatus.HasConversion<byte>()`), call `Ignore("ContactName")` etc. for the legacy columns, configure `HasMany(s => s.Branches)` with backing field `_branches` per `data-model.md` §EF Core Configuration Sketches.
- [X] T019 [P] Add `src/FundingPlatform.Infrastructure/Persistence/Configurations/SupplierBranchConfiguration.cs` per `data-model.md` (filtered unique index on `(SupplierId)` where `IsDefault = 1`).
- [X] T020 [P] Modify `src/FundingPlatform.Infrastructure/Persistence/Configurations/QuotationConfiguration.cs` — add `Property(q => q.SupplierBranchId).IsRequired()` + `HasIndex(q => q.SupplierBranchId)`.
- [X] T021 Modify `src/FundingPlatform.Domain/Entities/Quotation.cs` — add `int SupplierBranchId { get; private set; }` and require it in the constructor used by `Item.AddQuotation`. Update `Item.AddQuotation` accordingly.
- [X] T022 [P] Add `tests/FundingPlatform.Tests.Integration/Persistence/SupplierRepositoryTests.cs` covering: `GetByLegalIdWithBranchesAsync` returns supplier + branches; lookup is case-insensitive after normalization; `ListForAdminAsync` filters by status, legalId substring, name substring, and has-incomplete-compliance.

### Application services

- [X] T023 Add `src/FundingPlatform.Application/Suppliers/DTOs/SupplierLookupResultDto.cs` and `SupplierBranchDto.cs` (mirror `SupplierLookupResultViewModel` shape from contracts).
- [X] T024 Add `src/FundingPlatform.Application/Suppliers/Queries/SearchSupplierByLegalIdQuery.cs` + handler in `src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs` (skeleton): normalizes legal ID, applies the visibility rules from contracts §1 (`/Search`), returns one of three discriminated results (`Hit`, `Empty`, `Rejected`).
- [X] T025 Add `src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs` — full skeleton with method signatures only (`AddBranchUnderExistingSupplierAsync`, `CreateDraftWithBranchAsync` returning `Result<int>` with `RetryWithExisting(int)`, `AssertEditableByApplicant`). Bodies left as `throw new NotImplementedException()` for stories to fill.
- [X] T026 Modify `src/FundingPlatform.Application/Applications/Commands/AddSupplierQuotationCommand.cs` — replace flat supplier fields with the three-branch payload shape from contracts §1 (`SelectedBranchId?`, `NewBranch?`, `NewSupplier?`, plus the existing `Price/Currency/ValidUntil/File*` fields). Old fields removed.
- [X] T027 Modify `src/FundingPlatform.Application/Services/ApplicationService.cs` — replace `AddSupplierQuotationAsync` with three new methods: `AddQuotationToExistingBranchAsync`, plus call-throughs to `SupplierCatalogService.AddBranchUnderExistingSupplierAsync` and `CreateDraftWithBranchAsync`. Old method removed (callers updated in story phases).

### Migration parity test (SC-003)

- [X] T028 Add `tests/FundingPlatform.Tests.Integration/Persistence/SupplierMigrationTests.cs` covering: (a) seed the OLD schema state (legacy columns populated) via raw SQL; (b) run the migration; (c) assert every supplier is `Verified` with sentinel verifier; (d) assert every supplier has exactly one default branch with `BranchName = N'Sede principal'`; (e) assert every quotation has a non-null `SupplierBranchId` matching its supplier; (f) compute `SupplierScore` for every existing item before-and-after migration and assert byte-for-byte parity per SC-003; (g) capture wall-clock duration of the migration block and assert it completes in under 60 seconds against the test dataset, per SC-006. Test fails if either assertion (parity or timing) is not met.

### Controller shells

- [X] T029 [P] Modify `src/FundingPlatform.Web/Controllers/SupplierController.cs` — add empty action stubs (`Search`, `EditDraft`, `Branch.{Edit}`) returning `StatusCode(501)` so the routes exist for tests to negotiate against. Keep existing `GET/POST Add` stubs.
- [X] T030 [P] Add `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs` with `[Authorize(Roles = "Admin")]` and empty stubs (`Index`, `Detail`, `Edit`, `Branch.Edit`, `Verify`, `Reject`) returning `StatusCode(501)`. Do NOT add a `Delete` action and do NOT add a `Create` action — FR-036 (forbid delete with quotation refs) and FR-037 (forbid direct admin create) are enforced by absence; add a class-level XML doc comment stating "FR-036 / FR-037: this controller intentionally exposes no Delete or Create actions in v1".

**Checkpoint**: Schema + domain + score + repository + scaffolding + migration test green. User stories can begin in parallel after this phase.

---

## Phase 3: User Story 1 — Reuse a Verified Supplier with an Existing Branch (Priority: P1) 🎯 MVP

**Goal**: An applicant on a draft application searches by legal ID, lands on an existing Verified supplier, picks one of its branches, and saves a quotation in seconds — with no compliance checkboxes shown and no supplier-table writes performed.

**Independent Test**: Seed a Verified supplier with two branches. As an applicant on a draft application, search by legal ID, pick the second branch, save quotation. Verify the quotation references that branch, no `Suppliers` or `SupplierBranches` row was created or modified, and the applicant was never asked for compliance values.

### Tests for User Story 1 ⚠️

> **NOTE**: Write E2E test FIRST, ensure it FAILS before implementation.

- [ ] T031 [US1] Add `tests/FundingPlatform.Tests.E2E/PageObjects/AddQuotationPage.cs` updates: `SearchByLegalIdAsync(string legalId)`, `SelectBranchAsync(int branchIndex)`, `AssertSupplierReadOnlyAsync(string name, bool ccss, bool hacienda, bool sicop, bool eInvoice)`. Maintain existing selectors per spec 011 conventions (data-testid where adopted).
- [ ] T032 [US1] Add `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/ApplicantReusesVerifiedSupplierTests.cs` covering all three acceptance scenarios from spec User Story 1 (lookup hit + read-only flags, branch selection persists, whitespace/case normalization).

### Implementation for User Story 1

- [X] T033 [US1] Implement `SupplierCatalogService.SearchByLegalIdAsync(string legalId, int currentApplicantId, int currentApplicationId)` body — normalize, route through visibility rules (Verified visible to all, PendingReview visible only to creator, Draft same, Rejected returns `Rejected` discriminator) per contracts/permission-matrix.md.
- [X] T034 [US1] Implement `SupplierController.GET /Application/{appId}/Item/{itemId}/Supplier/Search?legalId=...` — calls the service, picks the right partial (`_LookupHit.cshtml` / `_LookupEmpty.cshtml` / `_LookupRejected.cshtml`), enforces `VerifyOwnershipAsync(appId)`. Path: `src/FundingPlatform.Web/Controllers/SupplierController.cs`.
- [X] T035 [US1] Add `src/FundingPlatform.Web/Views/Supplier/_LookupHit.cshtml` partial — renders supplier name, electronic-invoice and three compliance flags as read-only Tabler badges, branch picker (radio list) reusing `_BranchPicker.cshtml`, and the "Pendiente de verificación" badge when applicable.
- [X] T036 [US1] Add `src/FundingPlatform.Web/Views/Supplier/_BranchPicker.cshtml` partial — renders one radio per `BranchSummary`, collapses to a single "Use Sede principal" line when only one branch (per spec edge case "Single-branch suppliers"), and an "Agregar nueva sucursal" button stub (US2 fills the body).
- [X] T037 [US1] Implement `ApplicationService.AddQuotationToExistingBranchAsync(int appId, int itemId, int branchId, decimal price, string currency, DateOnly validUntil, Stream fileStream, string fileName, string contentType, long fileSize)` — load supplier (with branches), validate branch belongs to the supplier, write Quotation with both `SupplierId` and `SupplierBranchId` from the same loaded branch (preserves invariant). Path: `src/FundingPlatform.Application/Services/ApplicationService.cs`.
- [X] T038 [US1] Modify `src/FundingPlatform.Web/Controllers/SupplierController.cs` `POST /Add` — branch the request handler on `model.SelectedBranchId.HasValue` and dispatch to `AddQuotationToExistingBranchAsync`. Keep stub paths for US2/US3 (501 NotImplemented).
- [X] T039 [US1] Modify `src/FundingPlatform.Web/Views/Supplier/Add.cshtml` — rewrite as a step-flow: legal-ID input + 250ms-debounce JS hook (vanilla, ~10 lines) that fetches the `/Search` partial; lookup-result region renders `_LookupHit.cshtml` for hits, `_LookupEmpty.cshtml` for misses, `_LookupRejected.cshtml` for rejected. Reuses Tabler form classes per spec 008 conventions.
- [X] T040 [US1] Modify `src/FundingPlatform.Web/ViewModels/AddSupplierViewModel.cs` per contracts/http-routes.md §1 (drop compliance + e-invoice + flat contact fields, add `LookupResult`, `SelectedBranchId?`, `NewBranch?`, `NewSupplier?`, keep `Price/Currency/ValidUntil/QuotationFile`).

**Checkpoint**: User Story 1 fully functional. E2E test passes.

---

## Phase 4: User Story 2 — Add a New Branch under an Existing Supplier (Priority: P1)

**Goal**: An applicant lands on an existing Verified supplier, finds no matching branch, opens "Add new branch", fills branch fields, and saves the quotation against the new branch — without editing the parent supplier.

**Independent Test**: Seed a Verified supplier with one branch. As an applicant on a draft application, search, click "Agregar nueva sucursal", fill fields, save. Verify a new branch row exists under the supplier with `IsDefault = false` and `CreatedByApplicantId = current applicant`, the quotation references the new branch, and the parent supplier is unchanged.

### Tests for User Story 2 ⚠️

- [X] T041 [US2] Add `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/ApplicantAddsNewBranchTests.cs` covering all three acceptance scenarios from spec User Story 2 (new branch persists with `CreatedByApplicantId`, quotation links to new branch, supplier-level fields are not touched even if the form contains them).

### Implementation for User Story 2

- [X] T042 [US2] Implement `SupplierCatalogService.AddBranchUnderExistingSupplierAsync(int supplierId, AddBranchInput input, int createdByApplicantId)` body — loads `Supplier` aggregate via `GetByIdWithBranchesAsync`, calls `Supplier.AddBranch(...)` with `IsDefault = false`, persists, returns new branch `Id`. Path: `src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs`.
- [X] T043 [US2] Modify `src/FundingPlatform.Web/Views/Supplier/_BranchPicker.cshtml` — make the "Agregar nueva sucursal" button reveal the `AddBranchInputViewModel` form (collapsible panel) using the existing Tabler accordion pattern; ensure form fields bind to `model.NewBranch.*`.
- [X] T044 [US2] Modify `src/FundingPlatform.Web/Controllers/SupplierController.cs` `POST /Add` — add the branch dispatch path: when `model.NewBranch != null && model.LookupResult?.SupplierId is int sid`, call `AddBranchUnderExistingSupplierAsync(sid, ...)`, get the returned branch ID, then call `AddQuotationToExistingBranchAsync(...)`.
- [X] T045 [US2] Update `src/FundingPlatform.Web/ViewModels/AddBranchInputViewModel.cs` (new file or extracted from `AddSupplierViewModel`) with the data-annotations specified in contracts §1.

**Checkpoint**: User Stories 1 AND 2 work independently. E2E for both green.

---

## Phase 5: User Story 3 — Create a Brand-New Supplier in Draft (Priority: P1)

**Goal**: An applicant looks up an unknown legal ID, fills the new-supplier form (no compliance, no e-invoice), and saves. The supplier is created in `Draft` status, owned by the applicant, invisible to other applicants/admins/reviewers until submission.

**Independent Test**: As an applicant on a draft application, search a legal ID that does not exist, fill supplier + first-branch fields, save. Verify the new `Suppliers` row has `VerificationStatus = Draft` + `CreatedByApplicantId`, exactly one branch with `IsDefault = true`, and a second applicant searching the same legal ID does not see it.

### Tests for User Story 3 ⚠️

- [X] T046 [US3] Add `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/ApplicantCreatesDraftSupplierTests.cs` covering all three acceptance scenarios (Draft with applicant ownership, cross-applicant invisibility, edit-while-draft permitted).
- [X] T047 [US3] Add `tests/FundingPlatform.Tests.Integration/Web/SupplierController_DraftCreationTests.cs` covering the `SqlException 2627` recovery path (R4): WebApplicationFactory + simulated unique-constraint collision, assert 303 redirect with `?supplierId={existing}&banner=concurrent` query string.

### Implementation for User Story 3

- [X] T048 [US3] Implement `SupplierCatalogService.CreateDraftWithBranchAsync(string legalId, string name, AddBranchInput firstBranch, int createdByApplicantId)` body — normalize legal ID, call `Supplier.CreateDraft(...)` with the first branch as default, persist; on `DbUpdateException` whose inner is `SqlException(Number == 2627)`, query the existing supplier and return `Result.RetryWithExisting(existingSupplierId)`. Path: `src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs`.
- [X] T049 [US3] Add `src/FundingPlatform.Web/Views/Supplier/_LookupEmpty.cshtml` partial — renders the new-supplier form (name + the `_NewBranchForm.cshtml` partial scoped to `model.NewSupplier.FirstBranch`). Hide compliance and e-invoice fields completely (do not render them, even hidden).
- [X] T050 [US3] Modify `src/FundingPlatform.Web/Controllers/SupplierController.cs` `POST /Add` — add the new-supplier dispatch path: when `model.NewSupplier != null`, call `CreateDraftWithBranchAsync(...)`. On `Result.Success(int supplierId)`, load the default branch, call `AddQuotationToExistingBranchAsync(...)`. On `Result.RetryWithExisting(int existingId)`, redirect 303 to `Add?supplierId={existingId}&banner=concurrent`.
- [X] T051 [US3] Modify `src/FundingPlatform.Web/Controllers/SupplierController.cs` `GET /Add` — handle `?supplierId={int}&banner=concurrent` query: pre-load the existing supplier, render the lookup hit partial, and surface the localized `LookupConcurrentBanner` string above the form.
- [X] T052 [US3] Add `src/FundingPlatform.Web/Controllers/SupplierController.cs` `POST /{supplierId}/EditDraft` action with `[Authorize(Roles = "Applicant")]` — guards that supplier is `Draft`, applicant is creator, parent application is `Draft`. Calls `Supplier.RenameByApplicant`. View: `src/FundingPlatform.Web/Views/Supplier/EditDraft.cshtml` (small form with only `Name`).
- [X] T053 [US3] Add `src/FundingPlatform.Web/Controllers/SupplierController.cs` `POST /Branch/{branchId}/Edit` action — guards that branch was created by applicant + parent application is `Draft` + supplier is `Draft`. Calls `Supplier.EditBranch(...)`. View: `src/FundingPlatform.Web/Views/Supplier/EditBranch.cshtml`.
- [X] T054 [US3] Add `src/FundingPlatform.Web/ViewModels/NewSupplierInputViewModel.cs` (or co-locate in `AddSupplierViewModel.cs`) per contracts §1 — name + first-branch payload only.

**Checkpoint**: User Stories 1, 2, and 3 all work. Cross-applicant invisibility verified. Draft edits work.

---

## Phase 6: User Story 4 — Application Submission Locks Draft Suppliers and Routes to Admin (Priority: P1)

**Goal**: On `Application.Submit`, every owned Draft supplier flips atomically to `PendingReview`, applicant edit access is revoked, the supplier surfaces in the admin queue, and the reviewer can begin scoring with pending suppliers contributing zero compliance points and showing a "Pending verification" badge.

**Independent Test**: Seed an application in Draft state with a Draft supplier the applicant created. Submit. Verify the supplier's status flipped to `PendingReview`, the applicant cannot edit it, the supplier appears in the admin queue (default filter), and the reviewer sees a "Pending verification" badge plus a `Total = 1` (price-only) score on the related quotation.

### Tests for User Story 4 ⚠️

- [X] T055 [US4] Add `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/SubmitFlipsDraftToPendingTests.cs` covering all four acceptance scenarios (status flip atomic with submit, applicant edit revoked, admin queue surfaces it, reviewer-side pending badge + zero-compliance score).

### Implementation for User Story 4

- [X] T056 [US4] Modify `src/FundingPlatform.Application/Services/ApplicationService.cs` `SubmitAsync` — before the existing submit transition, walk every quotation's supplier; for each `(Status == Draft && CreatedByApplicantId == application.ApplicantId)`, call `supplier.SubmitForReview()` and update via repository. All inside the existing submission transaction.
- [X] T057 [US4] Modify `src/FundingPlatform.Application/Services/ReviewService.cs` — change the data-load query to project `(Quotation, Supplier, SupplierBranch)` triples, pass triples to `SupplierScore.ComputeForItem`. Path: `src/FundingPlatform.Application/Services/ReviewService.cs`.
- [X] T058 [US4] Add `src/FundingPlatform.Web/Views/Review/_PendingVerificationBadge.cshtml` partial — Tabler-style badge with localized "Pendiente de verificación" copy. Conditional render in `Review/Details.cshtml` next to existing recommendation/preselect badges when `score.IsSupplierVerified == false && supplier.VerificationStatus != Rejected`.
- [X] T059 [US4] Modify `src/FundingPlatform.Web/Views/Review/Details.cshtml` (and any partials it uses for quotation rows) — render `_PendingVerificationBadge.cshtml` per the rule above. Existing pre-select / recommended badge logic untouched (only `IsRecommended` rule changed inside `SupplierScore`).

**Checkpoint**: User Story 4 fully functional. Submit-time atomicity verified. Reviewer view shows pending badge correctly.

---

## Phase 7: User Story 5 — Admin Verifies, Edits, or Rejects a Pending Supplier (Priority: P1)

**Goal**: An admin opens the Suppliers admin page, drills into a `PendingReview` supplier, edits the four admin-only flags + supplier name, and clicks Verify or Reject. The decision takes effect immediately. Verified suppliers become reusable; rejected suppliers surface a banner on referencing applications.

**Independent Test**: Seed a `PendingReview` supplier created by US4. As admin, open the Suppliers admin page, click into the supplier, toggle the four flags on, click Verify. Verify status becomes `Verified` with `VerifiedByUserId` + `VerifiedAt`, and a different applicant searching that legal ID can now reuse it. Reject a different supplier without reason — assert validation error. Reject with reason — assert status `Rejected`, banner shows up on referencing application.

### Tests for User Story 5 ⚠️

- [X] T060 [US5] Add `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminSuppliersListPage.cs` with `OpenSupplierAsync(int supplierId)`, `SetStatusFilter(SupplierVerificationStatus s)` actions.
- [X] T061 [US5] Add `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminSupplierDetailPage.cs` with `ToggleComplianceAsync(string flag)`, `VerifyAsync()`, `RejectAsync(string reason)` actions.
- [X] T062 [US5] Add `tests/FundingPlatform.Tests.E2E/Tests/Admin/Suppliers/AdminVerifiesPendingTests.cs` covering all four acceptance scenarios from spec User Story 5 (verify path persists name + verifier+timestamp, reject without reason blocked, reject with reason persists + banner appears, edits to a Verified supplier reflect on next reviewer render).
- [X] T063 [US5] Add `tests/FundingPlatform.Tests.Integration/Web/AdminSuppliersController_AuthorizationTests.cs` — assert 403 for non-admin role on every admin route per contracts/permission-matrix.md.

### Implementation for User Story 5

- [X] T064 [US5] Implement `AdminSuppliersController.GET /Admin/Suppliers/{supplierId:int}` (Detail) — calls `ISupplierRepository.GetByIdWithBranchesAsync` + `CountReferencingApplicationsAsync`. Renders `Views/Admin/Suppliers/Detail.cshtml`. Path: `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs`.
- [X] T065 [US5] Implement `AdminSuppliersController.POST /Admin/Suppliers/{supplierId}/Verify` — loads the aggregate, calls `Supplier.Verify(currentAdminUserId)`, persists. Returns 303 to `Detail`.
- [X] T066 [US5] Implement `AdminSuppliersController.POST /Admin/Suppliers/{supplierId}/Reject` with `AdminRejectSupplierViewModel { SupplierId, [Required, MaxLength(1000)] Reason }` — guards reason non-empty, calls `Supplier.Reject(currentAdminUserId, reason)`. Returns 303 to `Detail`. ModelState error → re-render Detail with inline error.
- [X] T067 [US5] Add `src/FundingPlatform.Web/Views/Admin/Suppliers/Detail.cshtml` — top: identity panel (read-only by default; edit form for US6 lands here too); branches table (read-only by default; edit modal lands here in US6); referencing-applications list; bottom: Verify / Reject action bar with reason textarea.
- [X] T068 [US5] Add `src/FundingPlatform.Web/Views/Review/_RejectedSuppliersBanner.cshtml` partial — Tabler alert with the localized "Esta postulación referencia {count} proveedor(es) rechazado(s)…" copy. Render at the top of `Review/Details.cshtml` when `Model.RejectedSupplierCount > 0`.
- [X] T069 [US5] Modify `src/FundingPlatform.Application/Services/ReviewService.cs` — surface `RejectedSupplierCount` on the application detail view-model so the banner renders. Update view-model accordingly.
- [X] T070 [US5] Add a controller-side guard on `SupplierController.POST /Add` (and the QuotationController equivalent if any) refusing to write a Quotation whose target Supplier `VerificationStatus == Rejected`. UI also blocks the path: `_LookupRejected.cshtml` does not offer a save action.
- [X] T071 [US5] Add `src/FundingPlatform.Web/Views/Supplier/_LookupRejected.cshtml` partial — error alert with the localized `LookupRejectedMessage` copy and a "contactar al equipo" hint. No form actions.

**Checkpoint**: User Story 5 fully functional. Admin can verify and reject. Banner appears on the reviewer screen. Rejected suppliers cannot be reused.

---

## Phase 8: User Story 6 — Admin Edits a Verified Supplier on Applicant's Behalf (Priority: P2)

**Goal**: An applicant contacts the admin out-of-band about wrong supplier data; the admin edits the supplier or branch in the admin area, save takes effect immediately for everyone.

**Independent Test**: Seed a Verified supplier with a typo in a branch email. As admin, edit the email and save. Verify the corrected email shows on the applicant's quotation detail page and on the next reviewer render.

### Tests for User Story 6 ⚠️

- [X] T072 [US6] Add `tests/FundingPlatform.Tests.E2E/Tests/Admin/Suppliers/AdminEditsVerifiedTests.cs` covering the single acceptance scenario.

### Implementation for User Story 6

- [X] T073 [US6] Implement `AdminSuppliersController.POST /Admin/Suppliers/{supplierId}/Edit` with `AdminEditSupplierViewModel { SupplierId, Name, HasElectronicInvoice, IsCompliantCCSS, IsCompliantHacienda, IsCompliantSICOP }` — calls `Supplier.EditByAdmin(...)`, persists. Returns 303 to Detail.
- [X] T074 [US6] Implement `AdminSuppliersController.POST /Admin/Suppliers/{supplierId}/Branch/{branchId}/Edit` with `AdminEditBranchViewModel` (full branch field set) — calls `Supplier.EditBranch(...)`. Returns 303 to Detail.
- [X] T075 [US6] Modify `src/FundingPlatform.Web/Views/Admin/Suppliers/Detail.cshtml` — wire identity-panel inline-edit form to `Edit` action; add per-branch edit modal/inline-form bound to `Branch.Edit` action.
- [X] T076 [US6] Add `src/FundingPlatform.Web/ViewModels/Admin/AdminEditSupplierViewModel.cs` and `AdminEditBranchViewModel.cs`.

**Checkpoint**: User Stories 1–6 work end-to-end.

---

## Phase 9: User Story 7 — Admin Sees Filterable Queue of Suppliers Needing Attention (Priority: P2)

**Goal**: The admin lands on `/Admin/Suppliers` with a default `PendingReview` filter; can switch to `Verified` / `Rejected` / All; can search by partial legal ID or partial name; can filter to "has at least one false admin-only flag". Pagination follows the spec 010 convention.

**Independent Test**: Seed three suppliers (one Pending, one Verified, one Rejected). Open the admin page; default view shows only Pending. Switch to Verified; list updates. Search by partial legal ID; matching results show.

### Tests for User Story 7 ⚠️

- [X] T077 [US7] Add `tests/FundingPlatform.Tests.E2E/Tests/Admin/Suppliers/AdminFiltersQueueTests.cs` covering all three acceptance scenarios.

### Implementation for User Story 7

- [X] T078 [US7] Implement `AdminSuppliersController.GET /Admin/Suppliers` — parses query string into `SupplierAdminFilter` + `page` + `pageSize` (default 25 per spec 010), calls `ISupplierRepository.ListForAdminAsync`, returns paged view-model.
- [X] T079 [US7] Add `src/FundingPlatform.Web/ViewModels/Admin/AdminSupplierListViewModel.cs` with `Items, Filter, Page, TotalCount, PageSize`.
- [X] T080 [US7] Add `src/FundingPlatform.Web/Views/Admin/Suppliers/Index.cshtml` — Tabler-table layout, top-of-page filter form (status select, legal-ID input, name input, has-incomplete-compliance toggle), bottom `_PaginationFooter` partial reused from spec 010.
- [X] T081 [US7] Add a "Suppliers" entry to the admin sidebar/navbar partial (existing in Views/Shared/Admin or equivalent — confirm during work; matches the spec 009 pattern). File: `src/FundingPlatform.Web/Views/Shared/_AdminSidebar.cshtml` or whichever the project uses.

**Checkpoint**: All 7 user stories functional and independently demonstrable.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T082 [P] Walk through every step of `specs/013-supplier-catalog/quickstart.md` against the running app; capture any divergence as a follow-up issue.
- [ ] T083 [P] Verify NFR-004: confirm the supplier-search lookup on `Add.cshtml` debounces at 250ms client-side (use Playwright timing assertion or instrument via DevTools Performance).
- [ ] T084 [P] Verify the existing global IP rate limiter from spec 008 covers `/Application/{appId}/Item/{itemId}/Supplier/Search` — inspect `Program.cs` rate-limit registration and add a comment if the route relies on the global default.
- [ ] T085 [P] Run `dotnet test tests/FundingPlatform.Tests.Unit` — all unit tests green.
- [ ] T086 [P] Run `dotnet test tests/FundingPlatform.Tests.Integration` — all integration tests green, including the migration parity test (T028).
- [ ] T087 Run `dotnet test tests/FundingPlatform.Tests.E2E` — all E2E tests across all 7 user stories green. (The constitution makes this the primary delivery gate.)
- [ ] T088 [P] Run `/speckit-analyze` to verify spec ↔ plan ↔ tasks consistency.
- [ ] T089 [P] Run `/speckit-spex-gates-stamp` for the final gate check before declaring delivery.
- [ ] T090 [P] Open follow-up issue to drop the legacy `Suppliers.{ContactName, Email, Phone, Location, ShippingDetails, WarrantyInfo}` columns one release after this ships (research.md R3 / TODO[013-cleanup]).
- [ ] T091 [P] NFR-001 verification: add `tests/FundingPlatform.Tests.Unit/Application/SupplierCatalogService_NoExternalCallsTests.cs` asserting (via reflection or static analysis) that `SupplierCatalogService` and `AdminSuppliersController` do NOT depend on `HttpClient`, `IHttpClientFactory`, or any other outbound-network type. Documents the negative requirement and prevents a future PR from silently introducing external CCSS / Hacienda / SICOP integration without a spec.
- [ ] T092 [P] NFR-002 verification: add a CI check or Polish-phase task that runs `dotnet list package` at `main` and at `HEAD`, diffs the two, and fails if any new managed dependency was introduced by this branch. If a CI script is overkill, document the manual `git diff -- '**/*.csproj'` check in the PR description template under the spec-013 PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T003 enum is referenced by T010 entity refactor + T013 score change). **BLOCKS all user stories.**
- **User Stories (Phase 3+)**: All depend on Foundational completion.
  - US1, US2, US3 share `SupplierController.cs` and `Add.cshtml` — **must run sequentially**.
  - US4 depends on US3 (Draft creation must work to flip a Draft to Pending).
  - US5 can start once US4 reaches the point where a `PendingReview` supplier exists (or by seeding one directly in fixtures).
  - US6 depends on US5 (the admin Detail page exists).
  - US7 depends on US5 (the admin Index page shell exists).
- **Polish (Phase 10)**: Depends on all user stories.

### User Story Dependencies (concrete)

- **US1** depends on Phase 2 only.
- **US2** shares `SupplierController.cs` + `Add.cshtml` with US1; sequence US1 → US2.
- **US3** shares same files with US1/US2; sequence US2 → US3. R4 SQL recovery test (T047) requires the DbContext setup from T028.
- **US4** depends on US3 for Draft creation; reviewer-screen badge piece can be developed in parallel with US3 if a fixture seeds a `PendingReview` supplier directly.
- **US5** depends on Phase 2 only for the controller shell (T030); admin Detail wiring shares files within US5 but does not collide with US1–US4.
- **US6** depends on US5 (extends the same Detail view).
- **US7** depends on US5 (Index view + sidebar entry); the filter logic itself can be drafted alongside US5 if developer capacity allows.

### Within Each User Story

- Tests written BEFORE implementation, expected to FAIL on first run.
- Models / domain methods land before services.
- Services land before controller actions.
- Controller actions land before view templates.
- Commit after each task or each logical group.

### Parallel Opportunities

- All Phase 1 Setup tasks ([P]) can run together.
- Within Phase 2 Foundational:
  - T004, T005, T006 (different SQL files) can run in parallel.
  - T009, T011, T012 (new entity + unit tests) parallel after T003.
  - T015–T021 form a chain (interface → tests → impl → EF config), but tests T011, T012, T022 are [P] vs each other.
  - T023, T024, T025 can run in parallel.
- US-level: US5/US7 admin tasks can be developed in parallel by a second developer once Phase 2 is done (US6 waits on US5 for the same Detail file).

---

## Parallel Example: Phase 2 Foundational

```bash
# Schema changes — three independent SQL files, can land in one PR step:
Task: "Modify src/FundingPlatform.Database/Tables/dbo.Suppliers.sql per data-model.md"
Task: "Add src/FundingPlatform.Database/Tables/dbo.SupplierBranches.sql"
Task: "Modify src/FundingPlatform.Database/Tables/dbo.Quotations.sql"

# Domain core — entity + unit tests can land in parallel after the enum:
Task: "Add SupplierBranch entity in src/FundingPlatform.Domain/Entities/SupplierBranch.cs"
Task: "Add SupplierTests.cs lifecycle invariant suite"
Task: "Add SupplierBranchTests.cs"
```

## Parallel Example: User Story 1

```bash
# Test-first parallel pair:
Task: "Add AddQuotationPage POM updates"
Task: "Add ApplicantReusesVerifiedSupplierTests.cs"

# Implementation chain (largely sequential because of SupplierController.cs ownership):
# T033 -> T034 -> T035 -> T036 -> T037 -> T038 -> T039 -> T040
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) — three small files.
2. Complete Phase 2 (Foundational) — schema + domain + EF + repository + migration test. **This is the heavy lift.** Stop here and run `dotnet test` against the foundational layer; the migration parity test (T028) is the primary green-light signal.
3. Complete Phase 3 (User Story 1) — applicant search + branch picker + reuse-verified-supplier flow.
4. **STOP and VALIDATE**: run `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/ApplicantReusesVerifiedSupplierTests.cs`. Demo to the team.

### Incremental Delivery

Order: Setup → Foundational → US1 → US2 → US3 → US4 → US5 → US6 → US7 → Polish.

Per-checkpoint, run that user story's E2E test plus the migration parity test. Commit after each task or logical group per the constitution.

### Parallel Team Strategy

With two developers post-Phase 2:

- Dev A: US1 → US2 → US3 → US4 (single-applicant story arc, all in `SupplierController.cs` + `Add.cshtml`).
- Dev B: US5 → US6 → US7 (admin story arc, all in `AdminSuppliersController.cs` + `Views/Admin/Suppliers/`).

The reviewer-screen pieces (US4's badge + US5's banner) intersect with `Review/Details.cshtml`; coordinate during US4/US5 to avoid merge conflicts.

---

## Notes

- Constitution III makes E2E tests non-negotiable; tasks T031, T032, T041, T046, T055, T060–T063, T072, T077 carry the project gate.
- NFR-001 (no external API integration) and NFR-002 (no new managed dependencies) are negative requirements verified by T091 / T092 in Polish.
- SC-006 (migration <60s) is asserted inside T028 alongside SC-003 byte-for-byte parity.
- Constitution IV makes dacpac the schema source of truth; tasks T004–T008 are the only place schema changes live.
- Constitution II makes the Supplier aggregate the single owner of branch CRUD; T010's `AddBranch` / `EditBranch` enforces invariants. Application code never instantiates `SupplierBranch` directly.
- Each task names exact file paths so an implementer can pick up any task in isolation.
- Verify each E2E test FAILS first (run before implementation lands), then PASSES after the corresponding implementation task.
- Migration parity test (T028) is the primary protection for SC-003 byte-for-byte recommendation parity; do not weaken it.
- After Phase 9 closes US7, run T087 (full E2E suite) — per the testing-conventions block in `CLAUDE.md`, E2E green is the delivery gate.
