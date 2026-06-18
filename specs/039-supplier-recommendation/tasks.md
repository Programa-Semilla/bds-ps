# Tasks: Supplier Recommendation Algorithm Rewrite

**Input**: Design documents from `/specs/039-supplier-recommendation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md

**Tests**: Included — the project constitution makes E2E NON-NEGOTIABLE (per user story) and unit/integration complementary. Delivery bar = filtered E2E green.

**Organization**: Tasks grouped by user story (priority order from spec.md). Foundational phase introduces the schema/value-object prerequisites that block all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 (matches spec.md)

## Path Conventions

Clean-Architecture solution under `src/` (Domain / Application / Infrastructure / Database / Web) and `tests/` (Unit / Integration / E2E), per plan.md.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No project initialization needed — existing solution. Only locate the seams.

- [X] T001 Confirm branch `039-supplier-recommendation`; locate the dacpac seed scripts that INSERT seed quotations (`src/FundingPlatform.Database/` post-deploy) and the E2E quote-seeding path (`/Account/SeedUser` and any `QuotationSeeder`/fixture that creates quotations) so the new required fields can be supplied there in Foundational/US2.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `DurationUnit`/`TimeDuration` types, the `Quotation` fields, their EF mapping, and the dacpac columns + seed update. **Blocks every user story.** Because the `Quotation` constructor gains required parameters, this phase is built together with US2 (and the call-site sweep, T010) to keep the solution compiling.

- [X] T002 [P] Create `DurationUnit` enum (`Days=1`, `Months=2`) in `src/FundingPlatform.Domain/Enums/DurationUnit.cs` (data-model §1).
- [X] T003 [P] Create `TimeDuration` value object (`int Value`, `DurationUnit Unit`, computed `InDays` = Months ? Value*30 : Value; invariants Value>0 and defined unit) in `src/FundingPlatform.Domain/ValueObjects/TimeDuration.cs` (data-model §2).
- [X] T004 [P] Unit tests for `TimeDuration` (InDays for days/months @30; reject Value≤0; reject undefined unit) in `tests/FundingPlatform.Tests.Unit/`.
- [X] T005 Add `DeliveryLeadTime` and `Warranty` (`TimeDuration`) to `src/FundingPlatform.Domain/Entities/Quotation.cs`: new required constructor parameters + a mutator (`SetDeliveryAndWarranty(...)`) used by the edit path; reject null/invalid (data-model §3).
- [X] T006 Map both `TimeDuration` fields as `OwnsOne` → columns `DeliveryLeadTimeValue/Unit`, `WarrantyValue/Unit` (Unit `HasConversion<byte>`, required) in `src/FundingPlatform.Infrastructure/Persistence/Configurations/QuotationConfiguration.cs` (data-model §3, mirrors the `Snapshot` OwnsOne).
- [X] T007 Add the four columns to `src/FundingPlatform.Database/Tables/dbo.Quotations.sql` as `NOT NULL` with placeholder `DEFAULT(1)` + CHECK (`DeliveryLeadTimeValue>0`, `WarrantyValue>0`, units `IN (1,2)`) (data-model §3, research D8).
- [X] T008 Update the dacpac post-deploy seed script(s) so seeded quotations carry **varied** realistic delivery/warranty values (enabling the SC-001 non-lowest-price-winner demo) in `src/FundingPlatform.Database/` (data-model §3).
- [X] T009 Update the E2E quote-seeding path (`/Account/SeedUser` / `QuotationSeeder` / fixtures from T001) to supply delivery/warranty when creating quotations.
- [X] T010 Compile-driven sweep: update every remaining `new Quotation(...)` call site (add-quotation handler, tests, any seeders not covered above) to pass delivery/warranty so the solution builds. (Form-driven values land in US2.)

**Checkpoint**: Solution builds; dacpac deploys onto the persistent dev volume and the ephemeral E2E DB; seeded quotations have varied delivery/warranty.

---

## Phase 3: User Story 1 — Explainable multi-criterion recommendation (P1) 🎯 MVP

**Goal**: The reviewer sees the seven-criterion deterministic recommendation with a full per-criterion breakdown; the highest-total provider is `Recomendado` (not the cheapest by default).

**Independent test**: Seed an item with 3 quotations where a higher-priced provider has shorter delivery, longer warranty, favorable statuses → it is recommended; all seven scores + raw values are shown.

- [X] T011 [US1] Rewrite the `SupplierScore` record (seven criterion scores + `Total` + `IsEligible` + `BlockReason` + `IsRecommended` + `IsTiedAtTop`) and `ComputeForItem` (eligibility filter on CCSS `sin inscripción`; winners over eligible set; price→CRC key `ConvertedCrcAmount ?? Price`; price tie→all 1; delivery shortest / warranty longest tie→all 2; Hacienda/CCSS `al día`, SICOP `sin sanciones`, PME flag → 2; strict-max recommended, else tie) in `src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs` (data-model §5, contracts C1).
- [X] T012 [P] [US1] Unit-test matrix for `ComputeForItem` in `tests/FundingPlatform.Tests.Unit/`: non-lowest-price winner; price-tie→all 1; delivery-tie & warranty-tie→all 2; each status binary; PME; total range 7–14; mixed-currency price uses CRC; mixed-unit normalization @30; single strict winner sets `IsRecommended`.
- [X] T013 [US1] Expand `ReviewQuotationDto` (replace `Score`+4 bools with the 7 scores, `Total`, `IsRecommended`, `IsEligible`, `BlockReason`, raw `DeliveryLeadTimeValue/Unit`, `WarrantyValue/Unit`) in `src/FundingPlatform.Application/DTOs/ReviewApplicationDto.cs` (data-model §6).
- [X] T014 [US1] Update `ReviewService` score mapping (`:345-396`): map the new result into the DTO; derive item-level `HasRecommendationTie` + `HasAnyEligible` in `src/FundingPlatform.Application/Services/ReviewService.cs`.
- [X] T015 [US1] Expand `ReviewQuotationViewModel` (mirror DTO) and add `HasRecommendationTie`/`HasAnyEligible` to the item-level VM in `src/FundingPlatform.Web/ViewModels/ReviewApplicationViewModel.cs`.
- [X] T016 [US1] Reviewer surface: replace the `@q.Score/4` cell (`~:255`) with total + per-criterion breakdown (Precio/Entrega/Garantía/Hacienda/CCSS/SICOP/PYME) + raw values, mark the recommended provider `Recomendado`, in `src/FundingPlatform.Web/Views/Review/Review.cshtml` (contracts C4).
- [X] T017 [US1] Fix the supplier-selection dropdown label (`~:427`): use the total, remove the stray `/5`, in `src/FundingPlatform.Web/Views/Review/Review.cshtml`.
- [X] T018 [US1] E2E `SupplierRecommendationTests` (golden path): seeded item where the non-cheapest provider wins; assert it is `Recomendado` and the seven per-criterion scores + raw values render. Page Object Model, in `tests/FundingPlatform.Tests.E2E/`.

**Checkpoint**: US1 independently demonstrable — the recommendation is multi-criterion and explainable (SC-001, SC-002).

---

## Phase 4: User Story 2 — Capture delivery lead time & warranty on every quote (P1)

**Goal**: Adding/editing a quotation requires delivery lead time and warranty (value + días/meses); blank/zero/negative is rejected.

**Independent test**: Submit the add-quote form with delivery and/or warranty blank → rejected with es-CR messages; valid values persist and display.

- [X] T019 [P] [US2] Add `DeliveryLeadTimeValue`, `DeliveryLeadTimeUnit`, `WarrantyValue`, `WarrantyUnit` (with `[Required]` + `[Range(1,…)]` es-CR messages, `[Display]` labels) to `src/FundingPlatform.Web/ViewModels/AddSupplierViewModel.cs` and the spec-023 quotation-edit VM (both `IQuoteFieldsModel`) (contracts C3).
- [X] T020 [US2] Add delivery + warranty inputs (value number + unit días/meses select) to `src/FundingPlatform.Web/Views/Shared/_QuoteFields.cshtml` so Supplier/Add and Quotation/Edit both render them.
- [X] T021 [US2] Wire the add-quotation and edit-quotation command handlers to construct/update `Quotation` with `new TimeDuration(value, unit)` for each field; surface invalid values as collected ModelState errors (all-at-once) in the relevant Application command/handler files.
- [X] T022 [US2] Integration tests (real DB): add-quote rejects missing/zero/negative delivery or warranty; valid values persist and round-trip in `tests/FundingPlatform.Tests.Integration/`.
- [X] T023 [US2] E2E `QuoteFieldsTests`: add-quote form rejects blank delivery/warranty (es-CR), accepts valid values, and edit keeps them required, in `tests/FundingPlatform.Tests.E2E/`.

**Checkpoint**: Quotes cannot be saved without the two fields (SC-004); algorithm now fed by captured data end-to-end.

---

## Phase 5: User Story 3 — CCSS `sin inscripción` disqualifies & blocks progress (P2)

**Goal**: A CCSS `sin inscripción` provider is excluded from scoring (shown `bloqueado`) and an item cannot be approved with it selected; all-blocked item shows "ningún proveedor elegible".

**Independent test**: Item with a `sin inscripción` provider → excluded from scoring, flagged blocked; reviewer cannot approve the item with it selected (es-CR); changing to an eligible provider lets the approval through.

- [X] T024 [US3] Add the eligibility guard to `Item.Approve(supplierId, comment)`: resolve the selected quotation's `Supplier.CcssStatus`; if `SinInscripcion`, throw a domain failure (`SupplierIneligibleException` / `DomainError` code `SUPPLIER_CCSS_SIN_INSCRIPCION`) before setting `Approved`/`SelectedSupplierId`, in `src/FundingPlatform.Domain/Entities/Item.cs` (contracts C2; null ≠ block, research D4).
- [X] T025 [US3] In `ReviewService.ReviewItemAsync` (`:103-193`): ensure `Quotation.Supplier` is loaded for the selected supplier, catch the domain failure, and return the es-CR reviewer error (no approval persisted) in `src/FundingPlatform.Application/Services/ReviewService.cs`.
- [X] T026 [P] [US3] Add es-CR strings — block message ("No se puede aprobar el ítem: el proveedor «{nombre}» no está inscrito en la CCSS."), "ningún proveedor elegible", and the `SUPPLIER_CCSS_SIN_INSCRIPCION` translation — in `src/FundingPlatform.Web/Resources/SuppliersResources.cs` and/or `IUserFacingErrorTranslator` (research D11).
- [X] T027 [US3] Reviewer surface: render `bloqueado` (with reason) for ineligible providers, visually distinct from low-scoring eligible ones, and the per-item "ningún proveedor elegible" state, in `src/FundingPlatform.Web/Views/Review/Review.cshtml` (contracts C4).
- [X] T028 [US3] Integration test (real DB): `Item.Approve` rejects a `sin inscripción` supplier and succeeds for an eligible one; `null` CCSS does not block, in `tests/FundingPlatform.Tests.Integration/`.
- [X] T029 [US3] E2E `SupplierRecommendationBlockTests`: blocked provider excluded + flagged; advance/approve gated with es-CR until selection changes; all-blocked item shows no-eligible state, in `tests/FundingPlatform.Tests.E2E/`.

**Checkpoint**: Hard block enforced end-to-end (SC-003).

---

## Phase 6: User Story 4 — Final-score tie → manual selection (P3)

**Goal**: On a top-score tie, no provider is auto-recommended; the tied set is flagged and "selección manual requerida" is shown.

**Independent test**: Seed two eligible providers tying for the highest total → neither auto-`Recomendado`; tie message + tied set flagged.

- [X] T030 [P] [US4] Unit test: ≥2 eligible providers at max total → all `IsTiedAtTop`, none `IsRecommended`, item `HasRecommendationTie` true; single strict max → exactly one `IsRecommended` (extends T012 matrix) in `tests/FundingPlatform.Tests.Unit/`.
- [X] T031 [US4] Reviewer surface: when `HasRecommendationTie`, suppress the `Recomendado` badge, flag the tied quotations, and show "selección manual requerida", in `src/FundingPlatform.Web/Views/Review/Review.cshtml` (contracts C4).
- [X] T032 [US4] E2E `SupplierRecommendationTieTests`: top-score tie → no recommended badge, tie message shown, in `tests/FundingPlatform.Tests.E2E/`.

**Checkpoint**: Tie behavior verified (SC-005).

---

## Phase 7: User Story 5 — Item-line field order: product name first (P3)

**Goal**: Add-item form renders product name first, then category, then dynamic category fields.

**Independent test**: Open add-item form → product name before category; dynamic fields appear only after a category is selected.

- [X] T033 [US5] Reorder `src/FundingPlatform.Web/Views/Item/Add.cshtml`: ProductName → CategoryId → `#category-fields` (dynamic partial) → remaining fields; keep the `_DynamicFieldWiring.cshtml` AJAX targeting `#category-fields` intact (contracts C5, research D12).
- [X] T034 [US5] E2E `ItemFieldOrderTests`: product-name field precedes the category selector; dynamic category fields render after category selection, in `tests/FundingPlatform.Tests.E2E/`.

**Checkpoint**: Field order verified (SC-006).

---

## Phase 8: Polish & Cross-Cutting

- [X] T035 [P] Sweep for any other supplier-score display sites beyond Review.cshtml (e.g., applicant-facing detail, comparison region neighbors) and align them to total+breakdown or leave AI-comparison surfaces (spec 020) untouched per research D9.
- [X] T036 [P] Verify es-CR copy for all new labels/messages; no English literals; unit selects use the `DurationUnit` display map.
- [X] T037 Run the filtered E2E suite (`SupplierRecommendation*`, `QuoteFields`, `ItemFieldOrder` + affected `Review`/`Quotation`/`Supplier`/`Item` classes) green; run Unit + Integration green. Record counts.
- [X] T038 Update `EVOLUTION.md` (create in the spec dir) with any deviations from this plan (e.g., the latent raw-`Price`→CRC fix, gate placement specifics) for the review-code gate.

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** must complete before any story. Because the `Quotation` ctor change ripples, **Phase 2 + US2 + T010 are implemented together** to keep the build green.
- **US1 (P1)** depends only on Phase 2 (testable with seeded quotes). **US2 (P1)** depends on Phase 2. US1 and US2 are otherwise independent (different files: algorithm/DTO/Review vs. quote-form VMs/partial/handlers).
- **US3 (P2)** depends on US1 (recommendation surface + ComputeForItem eligibility already in place).
- **US4 (P3)** depends on US1 (algorithm already emits tie flags; US4 is UI + tests).
- **US5 (P3)** is fully independent (item-form markup only) — can be done any time after Phase 1.
- **Polish** last.

**Suggested MVP**: Phase 2 + **US1** (+ the US2 capture needed to feed real data). Delivers the explainable multi-criterion recommendation.

## Parallel Opportunities

- Foundational: T002, T003, T004 in parallel (new files).
- US1: T012 (unit tests) parallel with DTO/VM/view work once T011 lands.
- US3: T026 (es-CR strings) parallel with T024/T025.
- US5 can run in parallel with US1–US4 entirely.
- Polish: T035, T036 parallel.

## Task count

38 tasks — Setup 1, Foundational 9, US1 8, US2 5, US3 6, US4 3, US5 2, Polish 4.
