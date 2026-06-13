---
description: "Task list for 035 line-item category templates, per-item impact, quotation reuse"
---

# Tasks: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Input**: Design documents from `specs/035-line-item-category-templates/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D12), data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: REQUIRED — Constitution III makes Playwright E2E non-negotiable; each user story carries unit/integration/E2E tasks.

**Organization**: Tasks grouped by user story. Note: the impact relocation (Application→Item) + `Item.TechnicalSpecifications` removal + Plantilla impact-gating teardown is an **atomic refactor** — the solution will not compile mid-way — so every build-breaking schema/domain/EF/plumbing change lives in **Phase 2 (Foundational)**. User-facing capability layers on top per story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: US1–US4 (user-story phases only)
- All paths relative to repo root `/mnt/D/repos/bds-ps`

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline before the refactor.

- [ ] T001 Confirm baseline: `dotnet build FundingPlatform.slnx` is green and AppHost boots (`dotnet run --project src/FundingPlatform.AppHost`); note the current passing E2E classes that touch the applicant item/impact/quotation flow so regressions are detectable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema + domain + EF + service/DTO plumbing for the atomic refactor. **No user story can begin until the solution compiles green at the end of this phase.**

**⚠️ CRITICAL**: This phase removes application-level impact, re-keys impact to the item, drops `Item.TechnicalSpecifications`, and tears out the Plantilla impact-template gating. These are mutually-dependent and break compilation until all land together.

### Schema (dacpac — `src/FundingPlatform.Database/Tables/`)

- [ ] T002 [P] Add `dbo.CategoryFields.sql` (Id PK; CategoryId FK→Categories CASCADE; Name NVARCHAR(200); DisplayLabel NVARCHAR(300); DataType INT; IsRequired BIT DEFAULT 1; SortOrder INT DEFAULT 0; index `IX_CategoryFields_CategoryId`) per data-model.md.
- [ ] T003 [P] Add `dbo.CategoryFieldValues.sql` (Id PK; ItemId FK→Items CASCADE; CategoryFieldId FK→CategoryFields NO ACTION; Value NVARCHAR(MAX) NULL; `UX_CategoryFieldValues_ItemId_FieldId UNIQUE(ItemId,CategoryFieldId)`; `IX_CategoryFieldValues_ItemId`).
- [ ] T004 Re-key `dbo.ImpactParameterValues.sql`: replace `ApplicationId` with `ItemId` (FK→Items CASCADE); rename unique index → `UX_ImpactParamValues_ItemId_ParamId(ItemId,ImpactTemplateParameterId)`; rename `IX_…_ApplicationId` → `IX_…_ItemId`.
- [ ] T005 Alter `dbo.Items.sql`: add `ImpactTemplateId INT NULL` (FK→ImpactTemplates NO ACTION); drop `TechnicalSpecifications`.
- [ ] T006 [P] Delete `dbo.PlantillaImpactTemplates.sql` (drop the M2M join table).
- [ ] T007 [P] Drop the `ImpactTemplateIdsCsv` column from `dbo.ProcessPlantillas.sql`.
- [ ] T008 Update post-deploy seed scripts (`src/FundingPlatform.Database/`) for the new shape: demo categories gain example `CategoryField`s; demo items get per-item impact + category values; remove any Plantilla impact-template seeding.

### Domain (`src/FundingPlatform.Domain/`)

- [ ] T009 [P] Add `Entities/CategoryField.cs` (private-set props + ctor `(name, displayLabel, dataType, isRequired, sortOrder)`), mirroring `ImpactTemplateParameter`.
- [ ] T010 [P] Add `Entities/CategoryFieldValue.cs` (ctor `(categoryFieldId, value)`, nav to `CategoryField`).
- [ ] T011 Extend `Entities/Category.cs`: add `Fields` (read-only), `Update(name,description)`, `Activate()`, `Deactivate()`, `AddField(...)`, `ClearFields()` — mirror `ImpactTemplate`.
- [ ] T012 Rework `Entities/Item.cs`: add `ImpactTemplateId`/`ImpactTemplate` nav, `ImpactParameterValues`, `CategoryFieldValues` collections; add `SetImpact(template,values)`, `SetCategoryFieldValues(values)`, `ChangeCategory(newCategoryId)` (clears category values on change); remove `TechnicalSpecifications` (field, ctor param, `Update` param); add per-item `Impact` VO getter.
- [ ] T013 Rework `Entities/Application.cs`: remove `ImpactTemplateId`/`ImpactTemplate`/`ImpactParameterValues`/`Impact`/`SetImpact`; extend `Validate(minQuotations)` to collect per-item missing-impact + missing-required-category-field errors (all-at-once); remove the `ImpactTemplateId is null` check from `Submit`; add `CountQuotationsReferencingDocument(int documentId)`.
- [ ] T014 Tear out Plantilla impact gating in `Entities/Plantilla.cs` + `Entities/ProcessPlantilla.cs`: remove `_impactTemplates`/`ImpactTemplates`/`AttachImpactTemplate`/`DetachImpactTemplate`; in `AssignTo` remove the `_impactTemplates.Count==0` guard **and** the CSV snapshot; remove `ProcessPlantilla.ImpactTemplateIdsCsv` + `ImpactTemplateIds()`.
- [ ] T015 Relocate the `ValueObjects/Impact.cs` getter to `Item`; delete the stale `Entities/Impact.cs` (and any `dbo.Impacts.sql`) if present (dead code, SC-003).

### EF Core (`src/FundingPlatform.Infrastructure/Persistence/`)

- [ ] T016 [P] Add `Configurations/CategoryFieldConfiguration.cs` + `Configurations/CategoryFieldValueConfiguration.cs`.
- [ ] T017 Update `Configurations/CategoryConfiguration.cs`: `HasMany(c=>c.Fields).WithOne().HasForeignKey(f=>f.CategoryId).OnDelete(Cascade)`.
- [ ] T018 Update `Configurations/ItemConfiguration.cs`: map `ImpactTemplateId` FK; map `ImpactParameterValues` + `CategoryFieldValues` collections; remove `TechnicalSpecifications`.
- [ ] T019 Re-key `Configurations/ImpactParameterValueConfiguration.cs`: shadow/explicit FK `ItemId`, unique index `(ItemId,ImpactTemplateParameterId)`; remove vestigial `Ignore(ImpactId)`/`Ignore(Impact)`.
- [ ] T020 Update `Configurations/ApplicationConfiguration.cs`: remove impact mappings.
- [ ] T021 Delete `Configurations/PlantillaImpactTemplateConfiguration.cs`; update `Configurations/ProcessPlantillaConfiguration.cs` (remove `ImpactTemplateIdsCsv` mapping).
- [ ] T022 `Persistence/AppDbContext.cs`: add `DbSet<CategoryField>` + `DbSet<CategoryFieldValue>`.
- [ ] T023 `Domain/Interfaces/ICategoryRepository.cs` + `Persistence/Repositories/CategoryRepository.cs`: add `AddAsync`, `UpdateAsync`, `SaveChangesAsync`, `GetByIdWithFieldsAsync`, `GetAllAsync`.

### Service / DTO plumbing (keep the build green + data flowing)

- [ ] T024 DTOs (`src/FundingPlatform.Application/DTOs/`): populate `ItemDto.Impact` + add `ItemDto.CategoryFields` (`CategoryFieldValueDto(Label,Value)`); remove `ApplicationDto.Impact`; make `ReviewItemDto` impact per-item + add category fields; add `CategoryDetailDto` + `ReusableQuotationDto`; add category-fields + impact to `FundingAgreementItemRowDto`.
- [ ] T025 Update EF includes: `Repositories/ApplicationRepository.GetByIdWithDetailsAsync` and `Services/GetApplicationReviewProjection` move impact includes onto `Items` and add `Item.CategoryFieldValues.ThenInclude(CategoryField)`.
- [ ] T026 `Services/ApplicationService`: rewire `AddItemAsync`/`UpdateItemAsync` to resolve category fields + impact template (any active, no Plantilla gate), validate required values per item (aggregated), build value lists, call `item.SetCategoryFieldValues(...)` + `item.SetImpact(...)`; remove `SetApplicationImpactAsync`; build per-item impact + category DTOs in the Details/Edit projection.
- [ ] T027 `Services/ReviewService.MapToReviewDto`: feed per-item impact + category fields from real per-item data (remove the "same per-application value on every item" placeholder).
- [ ] T028 Plantilla service/web teardown: remove `ImpactTemplateIds` from `Plantillas/IPlantillaService` commands + `PlantillaDetail`, `ImpactTemplateCount` from `PlantillaListRow`; gut attach/reconcile in `Services/PlantillaService` (Create/Edit/Get/List); remove `LoadImpactTemplateOptionsAsync` + impact options from `Controllers/Admin/AdminPlantillasController` and `ViewModels/Admin/AdminPlantillaViewModels` (drop `AdminPlantillaImpactTemplateOption`); remove the "Plantillas de impacto disponibles" block from `Views/Admin/Plantillas/Create.cshtml` + `Edit.cshtml` and the `ImpactTemplateCount` column from `Index.cshtml`. (Keep min-quotations + required-field flags.)
- [ ] T029 `Infrastructure/Services/SubmitApplicationHandler`: submit gate now flows entirely through `Application.Validate` (per-item impact + required fields); remove any app-level impact reference.
- [ ] T030 **Checkpoint**: `dotnet build FundingPlatform.slnx` green; AppHost boots; existing non-035 E2E unaffected at the model level.

---

## Phase 3: User Story 1 — Admin configures category fields (Priority: P1) 🎯 MVP

**Goal**: Admin defines the ordered field set (label/key/type/required/order) a category collects; add/edit/reorder/remove.

**Independent Test**: Create a category, add fields of each data type (some required), reorder, edit a label, remove one; confirm persistence + sort order + per-type input control.

### Tests (US1)

- [ ] T031 [P] [US1] Integration tests `tests/FundingPlatform.Tests.Integration/Admin/CategoryAdministrationTests.cs` (real DB): create category with fields, full-replace update, deactivate, `GetByIdWithFieldsAsync`, hard-delete blocked when in use.
- [ ] T032 [P] [US1] E2E `tests/FundingPlatform.Tests.E2E/CategoryFieldAdminTests.cs` + POM `PageObjects/CategoryAdminPage.cs`: create/edit/reorder/remove fields; es-CR labels.

### Implementation (US1)

- [ ] T033 [US1] `Application/Admin/Commands/CreateCategoryCommand.cs` + `UpdateCategoryCommand.cs` (+ `CategoryFieldDefinition`); `Services/AdminService`: `GetAllCategoriesAsync`, `GetCategoryByIdAsync`, `CreateCategoryAsync`, `UpdateCategoryAsync` (ClearFields + re-add).
- [ ] T034 [US1] `Web/Controllers/AdminController.cs`: `Categories` (GET), `CreateCategory` (GET/POST), `EditCategory` (GET/POST) — mirror ImpactTemplates actions.
- [ ] T035 [P] [US1] `Web/ViewModels/` category admin VMs: `CategoryAdminViewModel`, `CreateCategoryViewModel`, `EditCategoryViewModel`, `CategoryFieldDefinitionViewModel`.
- [ ] T036 [US1] `Web/Views/Admin/Categories.cshtml`, `CreateCategory.cshtml`, `EditCategory.cshtml`: mirror the impact-template index-based repeating-row + JS-clone/`reindex` pattern; fix the clone label to es-CR `"Campo"` (the impact view leaks English `"Parameter"`).
- [ ] T037 [US1] Add "Categorías" sidebar link near `/Admin/ImpactTemplates` in `Web/Views/Shared/_Layout.cshtml`; wire the hard-delete-blocked-when-in-use es-CR message.

**Checkpoint**: Admin can fully manage a category's fields, independently testable.

---

## Phase 4: User Story 2 — Applicant captures line item via category fields + per-item impact (Priority: P1)

**Goal**: Adding a line item = select category → dynamic category fields → product name → per-item impact (any active template) + values; `TechnicalSpecifications` gone; submit blocked on missing required category/impact values.

**Independent Test**: Add an item, pick category, fill required fields + impact, save; blank a required field → submit blocked with es-CR message; complete → submit allowed; impact is per-item (no application-wide impact).

### Tests (US2)

- [ ] T038 [P] [US2] Unit tests `tests/FundingPlatform.Tests.Unit/Domain/`: `Item.SetImpact`/`SetCategoryFieldValues`; `Item.ChangeCategory` clears category values; `Application.Validate` per-item missing-impact + missing-required-field aggregation.
- [ ] T039 [P] [US2] Integration tests `tests/FundingPlatform.Tests.Integration/Applications/PerItemImpactCategoryTests.cs` (real DB): per-item impact + category values persist and round-trip.
- [ ] T040 [P] [US2] E2E `tests/FundingPlatform.Tests.E2E/PerItemImpactCategoryTests.cs` + POM updates: golden path (category→fields→product→impact→save) + submit-blocked-on-missing-required.

### Implementation (US2)

- [ ] T041 [US2] New JSON endpoint `GET /Application/{appId}/Item/Category/{categoryId}/Fields` (returns `{id,name,displayLabel,dataType,isRequired}`) in `Web/Controllers/ItemController.cs`; keep the existing `Impact/TemplateParameters/{templateId}` endpoint for the per-item impact picker.
- [ ] T042 [P] [US2] Extract the duplicated `DataType→input control` switch from `Views/Application/Impact.cshtml` into one reusable client-side renderer in `Web/wwwroot/js/` consumed by both the category-field and impact-parameter dynamic forms.
- [ ] T043 [US2] Reshape `Application/Applications/Commands/AddItemCommand.cs` + `UpdateItemCommand.cs`: drop `TechnicalSpecifications`; add `CategoryFieldValues` (by CategoryFieldId) + `ImpactTemplateId` + `ImpactParameterValues` (by ParameterId). Remove `SetApplicationImpactCommand.cs`.
- [ ] T044 [US2] `Web/Controllers/ItemController.cs` Add/Edit: category-first form hosting dynamic category fields + impact-template picker (active templates) + impact values; reshape `AddItemViewModel`/`EditItemViewModel`.
- [ ] T045 [US2] `Web/Views/Item/Add.cshtml` + `Edit.cshtml`: category select drives dynamic fields; impact picker + dynamic impact values; remove `TechnicalSpecifications`; es-CR copy + empty-state when no active impact templates exist (research D7).
- [ ] T046 [US2] Remove the application-level impact step: delete `ApplicationController.Impact` GET/POST, `Views/Application/Impact.cshtml`, `ViewModels/ImpactViewModel.cs`; remove the inline `AddItem`/`RemoveItem` actions + the inline add form on `Views/Application/Edit.cshtml`; route "Agregar línea" to `ItemController.Add` (canonical single add path, research D8).
- [ ] T047 [US2] Update `Web/wwwroot/js/submit-gate.js` + `Edit.cshtml` `data-*` attrs so the "Revisar y enviar" gate reflects per-item completeness (every item has required category fields + impact).

**Checkpoint**: Applicant can build line items with category-driven fields + per-item impact; submit gating works; no application-level impact remains.

---

## Phase 5: User Story 3 — Quotation reuse within an application (Priority: P2)

**Goal**: Reuse a sibling line item's supplier + uploaded document, with this item's own price/currency/validity; shared document retained until its last reference is removed; reuse scoped to the same application.

**Independent Test**: Add quotation+PDF on item A; on item B reuse A's quotation (pre-filled vendor+doc, own price); edit B's price → A unchanged; reuse offers only same-application quotations; delete A's quotation → doc survives while B references it; remove B → blob deleted.

### Tests (US3)

- [ ] T048 [P] [US3] Integration tests `tests/FundingPlatform.Tests.Integration/Applications/QuotationReuseTests.cs` (real DB + storage): reuse creates a new row sharing `DocumentId`; reference-counted retention (delete originating quotation → blob kept; remove last reference → blob deleted).
- [ ] T049 [P] [US3] E2E `tests/FundingPlatform.Tests.E2E/QuotationReuseTests.cs` + POM: reuse flow, per-item price independence, reuse list scoped to the application.

### Implementation (US3)

- [ ] T050 [US3] `Services/ApplicationService.ReuseQuotationAsync(appId,itemId,sourceQuotationId,price,currency,validUntil)`: validate source belongs to same application (FR-008); construct `Quotation` with source's `DocumentId`+supplier+branch → `SetCurrencyAndAmountAsync` → `item.AttachQuotation`; no upload/new Document. Add `GetReusableQuotationsAsync(appId,excludeItemId)` → `ReusableQuotationDto`.
- [ ] T051 [US3] `Services/ApplicationService`: make `RemoveQuotationAsync` + `ReplaceQuotationDocumentAsync` delete the blob only when `application.CountQuotationsReferencingDocument(documentId)==0` after detach (reference-counted retention, research D5).
- [ ] T052 [US3] `Web/Controllers/SupplierController.cs` + `ViewModels/AddSupplierViewModel.cs`: add "reuse" mode (`SourceQuotationId`, `ReusableQuotations`, optional `QuotationFile`); POST dispatches to `ReuseQuotationAsync` vs the add-new path.
- [ ] T053 [US3] `Web/Views/Supplier/Add.cshtml`: "Reutilizar cotización existente" picker; on select, hide upload + supplier lookup, show editable Price/Currency/ValidUntil via `_QuoteFields.cshtml`; es-CR copy.

**Checkpoint**: Quotation reuse works with correct retention and application scoping.

---

## Phase 6: User Story 4 — Category values + per-item impact on every surface (Priority: P3)

**Goal**: Every application-render surface shows each line item's category field values + per-item impact.

**Independent Test**: Submit an app with populated category fields + per-item impact; verify each surface (applicant Details/Review, reviewer detail, funding-agreement PDF, AI context) shows the data, in es-CR.

### Tests (US4)

- [ ] T054 [P] [US4] E2E `tests/FundingPlatform.Tests.E2E/LineItemDisplayTests.cs`: Details, Review, and reviewer detail each show per-item category values + impact; assert the funding-agreement PDF generation path includes them.

### Implementation (US4)

- [ ] T055 [P] [US4] `Web/Views/Application/Details.cshtml`: move impact from the single application card into each line-item block; add the item's category field label/value list (reuse the `dl.row` pattern).
- [ ] T056 [P] [US4] `Web/Views/Application/Review.cshtml` (+ `GetApplicationReviewProjection`/`ApplicationReviewViewModel`): per-item impact + category fields rows; update the submit-gate summary.
- [ ] T057 [P] [US4] `Web/Views/Review/Review.cshtml` + `ViewModels/ReviewApplicationViewModel`: render real per-item impact + category fields.
- [ ] T058 [P] [US4] `Web/Views/Application/Edit.cshtml`: replace the per-application impact card with a per-item summary (draft view).
- [ ] T059 [US4] Funding-agreement PDF: add a per-line category-fields + impact block (`Web/Views/FundingAgreement/Partials/_RequestedResourcesPage.cshtml` or a new partial) fed by `FundingAgreementItemRowDto`; verify Syncfusion render.
- [ ] T060 [US4] AI comparison context (`Infrastructure/AiComparison/SupplierAssembler`): add product name + category field label/values to the item context, scrubbed via the PII regex (`RedactFileText` patterns) before assembly; **exclude impact** (research D6 — pending user confirmation; if strict FR-009 is chosen, also add impact through the same scrub).

**Checkpoint**: All listed surfaces render per-item category values + impact.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T061 SC-003 teardown verification: a test/script asserting `grep -rIn "TechnicalSpecifications" src/`, `"ImpactTemplateIdsCsv|PlantillaImpactTemplates|AttachImpactTemplate" src/`, and application-level impact members return zero results (quickstart.md §5).
- [ ] T062 [P] es-CR copy review across all new admin + applicant UI (categories editor, item form, reuse picker, empty-states) — no English-only strings; consistent with existing conventions.
- [ ] T063 Run `quickstart.md` manual walkthrough end-to-end against the Aspire stack.
- [ ] T064 Run filtered E2E (delivery bar): `dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~CategoryFieldAdmin|FullyQualifiedName~PerItemImpactCategory|FullyQualifiedName~QuotationReuse|FullyQualifiedName~LineItemDisplay"` plus the rewired existing classes touched by the item/impact/quotation refactor; confirm green.
- [ ] T065 Update `CLAUDE.md` Recent Changes with the shipped 035 summary (after merge).

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → depends on Setup; **blocks all user stories**; must end build-green (T030).
- **US1 (P3)** → depends on Foundational (needs `CategoryField` entity + repo writes). Independent of US2–US4.
- **US2 (P4)** → depends on Foundational (per-item impact + category-value model). Best after US1 so categories have fields to render, but testable with seeded fields independently.
- **US3 (P5)** → depends on Foundational (`CountQuotationsReferencingDocument`). Independent of US1/US2/US4.
- **US4 (P6)** → depends on Foundational + benefits from US2/US3 producing data; the render changes themselves are independent.
- **Polish (P7)** → after desired stories complete.

### Within Foundational

Schema (T002–T008) → Domain (T009–T015) → EF (T016–T023) → Plumbing (T024–T029) → Checkpoint (T030). The `[P]` schema/domain/EF-config files are parallel within their group; the cross-cutting reworks (T012/T013/T014/T024/T026/T028) touch shared files and are sequential.

### Parallel opportunities

- T002, T003, T006, T007 (separate `.sql` files) in parallel; T009, T010, T016 in parallel.
- After Foundational: US1, US3 can proceed fully in parallel; US2 and US4 can overlap (US4 render tasks T055–T058 are `[P]` across separate views).
- All per-story test tasks marked `[P]` run in parallel.

---

## Implementation Strategy

### MVP (US1)

Setup → Foundational → US1 → validate admin category-field management independently → demo.

### Incremental delivery

Foundational (build-green) → US1 (admin fields) → US2 (applicant per-item flow, the core) → US3 (quotation reuse) → US4 (display everywhere) → Polish (SC-003 teardown verification + filtered E2E gate).

### Notes

- Single PR (user chose one cohesive spec); commit per task/logical group (Constitution commit discipline).
- The atomic refactor (Foundational) is the riskiest block — keep T030 build-green as a hard gate before any story.
- Risk hot-spots: `Plantilla.AssignTo` guard/snapshot removal (T014 — breaks Process assignment if missed); reference-counted blob retention (T051); `ReviewService` duplication fix (T027); AI redaction of new free-text (T060, D6).
- D6 (AI context: category values only vs strict FR-009 incl. impact) — default is category-values-only, scrubbed; confirm with the user before T060.
