---
description: "Task list for 035 — line-item category templates, application-level impacts with per-item attribution, quotation reuse (EVOLVED 2026-06-16)"
---

# Tasks: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Input**: Design documents from `specs/035-line-item-category-templates/`
**Prerequisites**: plan.md (re-plan), spec.md (Evolution Log), research.md (D1–D12 + D13–D16), data-model.md (evolved), contracts/interfaces.md (evolved), quickstart.md

**Tests**: REQUIRED — Constitution III makes Playwright E2E non-negotiable; the impact delta carries unit/integration/E2E tasks.

---

## Evolution note (2026-06-16)

The original 035 task list (**T001–T065**, preserved in git history at commit `d53c0c0`) implemented the feature with **per-item impact** (`Item.ImpactTemplateId` + per-item `ImpactParameterValues`). A late requirement (spec Evolution Log; research D13–D16) moves impact to the **application level with one or more impacts**, and gives each line item a **multi-impact attribution + a short justification** instead of its own impact field values.

**Superseded prior tasks** (their per-item-impact work is undone/reshaped by this list): the impact portions of **T004, T005 (ImpactTemplateId add), T012, T013, T015, T018, T019, T024–T029, T038–T047, T054–T060** and the impact assertions in **T061, T064**. The **category-field** (T002–T003, T009–T011, T016–T017, T022–T023, T031–T037, T041–T042) and **quotation-reuse** (T048–T053) and **Plantilla-teardown** (T006–T007, T014, T021, T028) work **stands as built**.

This list (re-numbered **TE001+**) covers only the **impact delta** plus the retained-work checklist.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: US1–US5 per the evolved spec (US2 = app-level impacts; US3 = per-item attribution + justification; US5 = display)
- All paths relative to repo root `/mnt/D/repos/bds-ps`

---

## Phase 0: Retained from prior implementation (DONE — do not redo)

These were delivered by T001–T065 and are **unchanged** by the evolution.

- [X] R1 Category-field schema: `dbo.CategoryFields`, `dbo.CategoryFieldValues` (was T002–T003).
- [X] R2 Category domain + EF: `CategoryField`/`CategoryFieldValue` entities, `Category.Fields`/mutators, configurations, `DbSet`s, `ICategoryRepository` additions (was T009–T011, T016–T017, T022–T023).
- [X] R3 US1 category-field admin: commands, `AdminService`, `AdminController` Categories/Create/Edit, VMs, views, sidebar link (was T033–T037), + integration/E2E (was T031–T032).
- [X] R4 Shared dynamic category-field renderer + `GET /Application/{appId}/Item/Category/{categoryId}/Fields` (was T041–T042).
- [X] R5 `Item.TechnicalSpecifications` removed from form + entity + `Item.ChangeCategory` clears category values (the non-impact part of T005/T012).
- [X] R6 Plantilla impact-template gating teardown: drop `PlantillaImpactTemplates`, `ProcessPlantillas.ImpactTemplateIdsCsv`, attach/snapshot logic, admin picker (was T006–T007, T014, T021, T028).
- [X] R7 Quotation reuse (US4): `ReuseQuotationAsync`/`GetReusableQuotationsAsync`, reference-counted blob retention, `SupplierController` reuse mode + view (was T048–T053), + integration/E2E.
- [X] R8 Category-field display + AI category context: per-item category fields on Details/Review/reviewer Review/Edit/PDF + `SupplierAssembler` category fields (the non-impact part of T055–T060).

---

## Phase 1: Setup

- [ ] TE001 Confirm baseline: `dotnet build FundingPlatform.slnx` green on the current branch; run the **retained** filtered E2E (`CategoryFieldAdmin`, `QuotationReuse`) to confirm the kept work is still green before the impact rework begins.

---

## Phase 2: Foundational — impact-model atomic refactor (BLOCKING)

**⚠️ CRITICAL**: dropping `Item.ImpactTemplateId`, re-keying `ImpactParameterValues` to `ApplicationImpactId`, and adding the new aggregates is mutually dependent and **breaks compilation until all land together**. No user story can begin until this phase compiles green.

### Schema (dacpac — `src/FundingPlatform.Database/Tables/`)

- [ ] TE002 [P] Add `dbo.ApplicationImpacts.sql` (Id PK; `ApplicationId` FK→Applications ON DELETE CASCADE; `ImpactTemplateId` FK→ImpactTemplates ON DELETE NO ACTION; `UX_ApplicationImpacts_AppId_TemplateId UNIQUE(ApplicationId,ImpactTemplateId)`; `IX_ApplicationImpacts_ApplicationId`).
- [ ] TE003 [P] Add `dbo.ItemImpacts.sql` (Id PK; `ItemId` FK→Items ON DELETE CASCADE; `ApplicationImpactId` FK→ApplicationImpacts **ON DELETE NO ACTION**; `UX_ItemImpacts_ItemId_AppImpactId UNIQUE(ItemId,ApplicationImpactId)`; `IX_ItemImpacts_ApplicationImpactId`). Comment the NO-ACTION reason (multi-cascade-path avoidance).
- [ ] TE004 Re-key `dbo.ImpactParameterValues.sql`: replace `ItemId` (the prior-035 key) with `ApplicationImpactId` (FK→ApplicationImpacts ON DELETE CASCADE); unique index → `UX_ImpactParamValues_AppImpactId_ParamId(ApplicationImpactId,ImpactTemplateParameterId)`; index → `IX_ImpactParameterValues_ApplicationImpactId`.
- [ ] TE005 Alter `dbo.Items.sql`: **drop** `ImpactTemplateId` (+ its FK); **add** `ImpactJustification NVARCHAR(300) NULL`.
- [ ] TE006 Update post-deploy seeds (`src/FundingPlatform.Database/`): demo applications declare ≥1 `ApplicationImpact` with values; demo items get ≥1 `ItemImpact` attribution + a justification; remove per-item impact-template/value seeding.

### Domain (`src/FundingPlatform.Domain/`)

- [ ] TE007 [P] Add `Entities/ApplicationImpact.cs` (ctor `(impactTemplateId)`; `ImpactTemplate` nav; `_parameterValues` + `SetValues(...)`; relocated `Impact` value-object getter for display).
- [ ] TE008 [P] Add `Entities/ItemImpact.cs` (ctor `(applicationImpactId)`).
- [ ] TE009 Rework `Entities/Item.cs`: remove `ImpactTemplateId`/`ImpactTemplate` nav/`ImpactParameterValues`/`SetImpact`; add `_itemImpacts`+`ItemImpacts` (read-only), `AttributeImpacts(IEnumerable<int> applicationImpactIds)` (replace-all), `ImpactJustification` + `SetImpactJustification(string?)` (trim, ≤300 guard). Keep category-field members.
- [ ] TE010 Rework `Entities/Application.cs`: add `_impacts`+`Impacts` (read-only); `AddImpact(ImpactTemplate, IEnumerable<ImpactParameterValue>)` (reject duplicate template); `RemoveImpact(int applicationImpactId)` (also remove every `ItemImpact` across items referencing it — SC-007). Extend `Validate(minQuotations)`: ≥1 declared impact; per-item ≥1 attribution; per-item non-empty justification; per-item every attribution targets a declared impact; (required-value detail stays service-side). Remove any leftover per-item-impact references.
- [ ] TE011 Delete the stale `Entities/Impact.cs` / `ValueObjects` duplication if the per-item relocation left one (dead code, SC-003).

### EF (`src/FundingPlatform.Infrastructure/Persistence/`)

- [ ] TE012 [P] Add `Configurations/ApplicationImpactConfiguration.cs` (`HasMany(ParameterValues)`, `HasOne(ImpactTemplate)`) + `Configurations/ItemImpactConfiguration.cs` (FKs per TE003, NO ACTION on ApplicationImpactId).
- [ ] TE013 Re-key `Configurations/ImpactParameterValueConfiguration.cs`: owning FK `ApplicationImpactId`, unique `(ApplicationImpactId,ImpactTemplateParameterId)`; remove vestigial `Ignore(ImpactId)`/`Ignore(Impact)`.
- [ ] TE014 Update `Configurations/ItemConfiguration.cs` (drop `ImpactTemplateId`/impact-values mapping; map `ItemImpacts`; map `ImpactJustification`) and `Configurations/ApplicationConfiguration.cs` (map `Impacts`).
- [ ] TE015 `Persistence/AppDbContext.cs`: add `DbSet<ApplicationImpact>` + `DbSet<ItemImpact>`; remove any per-item impact-value set added by prior 035 if distinct.
- [ ] TE016 Re-point EF includes in `Repositories/ApplicationRepository.GetByIdWithDetailsAsync` and `Services/GetApplicationReviewProjection`: include `Application.Impacts`→`ParameterValues`→`ImpactTemplateParameter` and →`ImpactTemplate`; `Item.ItemImpacts`→`ApplicationImpact`→`ImpactTemplate`; keep `Item.CategoryFieldValues.ThenInclude(CategoryField)`.

### Checkpoint

- [ ] TE017 **Checkpoint**: `dotnet build FundingPlatform.slnx` green; AppHost boots; retained category/quotation E2E unaffected at the model level.

---

## Phase 3 (US2): Application-level impacts manager

**Goal**: applicant declares one or more impacts (active templates), each with its own validated values; add/list/remove.
**Independent test**: add two impacts, complete required values, remove one; submit blocked with zero impacts or a missing required value.

- [ ] TE018 [P] [US2] Unit tests (`tests/FundingPlatform.Tests.Unit/Domain/`): `Application.AddImpact` (append + duplicate-template rejection); `Application.RemoveImpact` strips dependent `ItemImpact` attributions; `Application.Validate` ≥1-impact + per-impact-required-value gates.
- [ ] TE019 [P] [US2] Integration test (`tests/FundingPlatform.Tests.Integration/Applications/ApplicationImpactsTests.cs`, real DB): declare multiple impacts round-trip; re-keyed `ImpactParameterValues` persist on `ApplicationImpactId`; `RemoveImpact` deletes attributions (no orphan/cascade error from the NO-ACTION FK).
- [ ] TE020 [P] [US2] E2E (`tests/FundingPlatform.Tests.E2E/ApplicationImpactsTests.cs` + POM): declare two impacts, remove one, submit-blocked-on-zero-impacts / missing-required-value; es-CR copy + no-active-templates empty-state.
- [ ] TE021 [US2] Application layer: `AddApplicationImpactCommand`/`RemoveApplicationImpactCommand`; `ApplicationService.AddApplicationImpactAsync` (resolve active template via `IImpactTemplateRepository.GetAllActiveAsync`, validate required values, `application.AddImpact(...)`), `RemoveApplicationImpactAsync`, `GetActiveImpactTemplatesAsync`.
- [ ] TE022 [US2] Web: re-introduce `ApplicationController` impacts manager — `Impacts` GET, `AddImpact` POST, `RemoveImpact` POST (`/Application/{id}/Impacts...`); `ApplicationImpactsViewModel`; views reusing the kept `GET .../Impact/TemplateParameters/{templateId}` endpoint + shared dynamic renderer. es-CR empty-state when no active templates (D7).
- [ ] TE023 [US2] Wire the "Impactos" step into the draft application navigation/flow (link from Edit/Details); ensure it is visually distinct from the per-item attribution (UX expectation).

---

## Phase 4 (US3): Per-item impact attribution + short justification

**Goal**: each line item attributes to ≥1 declared impact (multi-select) + a required ≤300-char justification.
**Independent test**: with an app that declares ≥1 impact, create an item, attribute impacts, write justification, save; submit blocked on zero attributions or empty justification; attribution options limited to the app's declared impacts.

- [ ] TE024 [P] [US3] Unit tests: `Item.AttributeImpacts` replace-all; `Item.SetImpactJustification` trim + ≤300 guard; `Application.Validate` per-item ≥1-attribution + non-empty-justification + attribution-targets-declared.
- [ ] TE025 [P] [US3] Integration test (`tests/FundingPlatform.Tests.Integration/Applications/ItemImpactAttributionTests.cs`, real DB): attribution round-trips; attributing to an impact from another application is rejected; deleting an item cascades its `ItemImpact` rows.
- [ ] TE026 [P] [US3] E2E (`tests/FundingPlatform.Tests.E2E/ItemImpactAttributionTests.cs` + POM): attribute multi-impact, justification with char counter/maxlength 300, submit-blocked-on-missing-attribution/justification, options limited to declared impacts, empty-state when app has no declared impacts.
- [ ] TE027 [US3] Application layer: reshape `AddItemCommand`/`UpdateItemCommand` (drop per-item impact template/values; add `ApplicationImpactIds` + `ImpactJustification`); `ApplicationService.AddItemAsync`/`UpdateItemAsync` validate attribution subset-of-declared, call `item.AttributeImpacts(ids)` + `item.SetImpactJustification(text)` (+ existing category-field handling). Remove the prior per-item `SetImpact` wiring.
- [ ] TE028 [US3] Web: reshape `ItemController` Add/Edit + `AddItemViewModel`/`EditItemViewModel` — replace impact-template/params with `DeclaredImpacts` (multi-select source), `SelectedApplicationImpactIds`, `ImpactJustification` (textarea maxlength 300 + counter); empty-state linking to the impacts step when none declared.
- [ ] TE029 [US3] Update `Views/Item/Add.cshtml` + `Edit.cshtml`: attribution multi-select (es-CR), justification textarea + live counter; remove the prior per-item impact-template picker/dynamic impact values.
- [ ] TE030 [US3] Update `wwwroot/js/submit-gate.js` + Edit `data-*` so the "Revisar y enviar" gate reflects: app has ≥1 impact AND every item attributed + justified + category-complete.

---

## Phase 5 (US5): Cross-surface display

**Goal**: app-level declared impacts + per-item attributed names + justification visible everywhere (es-CR).
**Independent test**: submit a populated app, open each surface, confirm the application impacts card + per-item attribution/justification render; PDF includes them.

- [ ] TE031 [P] [US5] E2E (`tests/FundingPlatform.Tests.E2E/ImpactDisplayTests.cs`): Details, applicant Review, reviewer Review each show the app impacts card + per-item attributed names + justification; assert the funding-agreement PDF path includes them.
- [ ] TE032 [US5] DTOs/projection: `ApplicationDto.Impacts` (list); `ItemDto.AttributedImpactNames` + `ItemDto.ImpactJustification` (remove prior per-item `ItemDto.Impact`); `ReviewItemDto` + `ApplicationReviewViewModel` (app impacts card + per-row attribution/justification); `FundingAgreementItemRowDto` (+ attributed names + justification; + app-level impacts block).
- [ ] TE033 [P] [US5] `Views/Application/Details.cshtml` + `Edit.cshtml`: application-level "Impactos" card (declared impacts + values) + per-item attributed names + justification block (reuse `dl.row`).
- [ ] TE034 [P] [US5] `Views/Application/Review.cshtml` + `Views/Review/Review.cshtml` (+ projection): app impacts card + per-item attribution/justification rows; update the submit-gate summary.
- [ ] TE035 [US5] Funding-agreement PDF (`Web/Views/FundingAgreement/Partials/...`): app-level impacts block + per-line attributed names + justification fed by `FundingAgreementItemRowDto`; verify Syncfusion render.
- [ ] TE036 [US5] AI comparison (`Infrastructure/AiComparison/SupplierAssembler`): add per-item `ImpactJustification` (scrubbed via the PII regex) alongside the already-added product name + category fields; **exclude** raw impact parameter values (D16).

---

## Phase 6: Polish & cross-cutting

- [ ] TE037 SC-003 teardown verification: update the grep check — assert **zero** results for `TechnicalSpecifications`, `ImpactTemplateIdsCsv`/`PlantillaImpactTemplates`/`AttachImpactTemplate`, **and** the prior per-item-impact members (`Item.ImpactTemplateId`, per-item `SetImpact`/`ImpactParameterValues` on `Item`).
- [ ] TE038 [P] es-CR copy review across the new impact surfaces (impacts manager add/remove, attribution multi-select, justification field + counter, empty-states) — no English-only strings; consistent with conventions (FR-015).
- [ ] TE039 Rewire the prior per-item-impact E2E/integration suites (`PerItemImpactCategory*`, the impact parts of `LineItemDisplay`) to the app-level-impacts + attribution model; delete obsolete per-item-impact assertions.
- [ ] TE040 Run filtered E2E (delivery bar): `ApplicationImpacts | ItemImpactAttribution | ImpactDisplay` + retained `CategoryFieldAdmin | QuotationReuse` + the rewired classes; confirm green.
- [ ] TE041 Run `quickstart.md` manual walkthrough end-to-end against the Aspire stack (declare impacts → add items with attribution + justification → reuse quotation → submit → surfaces).
- [ ] TE042 Update `CLAUDE.md` Recent Changes with the shipped evolved-035 summary (after merge).

---

## Dependencies & order

- **Phase 2 (TE002–TE017)** is a blocking atomic refactor — complete before any US phase.
- **US2 (Phase 3)** before **US3 (Phase 4)**: a line item can only attribute to impacts the application has declared.
- **US5 (Phase 5)** after US2+US3 produce the data.
- Within a phase, `[P]` tasks (different files) may run in parallel; tests `[P]` may be written before their implementation task.

## Suggested MVP

Phase 2 + US2 + US3 (declare impacts → attribute + justify per item → submit gating). US5 display + polish follow.
