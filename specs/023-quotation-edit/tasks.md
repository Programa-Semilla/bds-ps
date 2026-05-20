---

description: "Task list for spec 023 — in-place quotation field edit"
---

# Tasks: In-place Quotation Field Edit

**Input**: Design documents from `/specs/023-quotation-edit/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/quotation-edit-endpoint.md](./contracts/quotation-edit-endpoint.md)

**Tests**: Included — constitution III (NON-NEGOTIABLE) + spec REVIEW-SPEC R-2 mandate per-US Playwright E2E. Integration tests are required for service-level orchestration (state gate, branch invariant, idempotency, cache hook).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- File paths are absolute under the repo root.

## Path Conventions

Clean Architecture, four-layer .NET (see [plan.md](./plan.md) §Project Structure):
- Domain: `src/FundingPlatform.Domain/`
- Application: `src/FundingPlatform.Application/`
- Infrastructure: `src/FundingPlatform.Infrastructure/`
- Web: `src/FundingPlatform.Web/`
- Tests: `tests/FundingPlatform.Tests.Unit/`, `tests/FundingPlatform.Tests.Integration/`, `tests/FundingPlatform.Tests.E2E/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pre-existing project — nothing to bootstrap. This feature reuses the established stack (.NET 10, EF Core, Aspire, Playwright). No new managed dependencies (NFR-005).

- [ ] T001 [P] Verify build is green on `023-quotation-edit` baseline: run `dotnet build FundingPlatform.slnx` and confirm 0 errors / 0 warnings.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared building blocks every user story depends on. No story can start until this phase is complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Shared form partial extraction (FR-003)

- [ ] T002 [P] Define `IQuoteFieldsModel` marker interface in `src/FundingPlatform.Web/ViewModels/IQuoteFieldsModel.cs` with properties `decimal Price { get; set; }`, `string Currency { get; set; }`, `DateOnly ValidUntil { get; set; }`, `IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; }`.

- [ ] T003 Update `src/FundingPlatform.Web/ViewModels/AddSupplierViewModel.cs` to implement `IQuoteFieldsModel`. Adjust the `EnabledCurrencies` setter from `init` to `set` if currently `init`; otherwise no behavioral change.

- [ ] T004 [P] Create `src/FundingPlatform.Web/Views/Shared/_QuoteFields.cshtml`. Bind to `IQuoteFieldsModel`. Render Price / Currency `<select>` over `EnabledCurrencies` / ValidUntil inputs + the `data-quote-preview` conversion alert block — markup per [research.md](./research.md) §R0.2.

- [ ] T005 Refactor `src/FundingPlatform.Web/Views/Supplier/Add.cshtml` to consume `<partial name="_QuoteFields" model="Model" />` in place of the inline Price / Currency / ValidUntil markup (lines 79–119 of the current file). Preserve the `data-quote-form` + `data-convert-url` attributes on the form element. Existing `data-testid` selectors remain identical (the partial owns them now).

### Domain primitive (FR-004, Principle II)

- [ ] T006 [P] Add `public void ChangeBranch(SupplierBranch branch)` to `src/FundingPlatform.Domain/Entities/Quotation.cs`. Implementation: assert `branch is not null`; assert `branch.SupplierId == this.SupplierId` else throw `ArgumentException("Sucursal no válida para este proveedor.", nameof(branch))`; assign `SupplierBranchId = branch.Id; SupplierBranch = branch;`. No exchange-rate side-effects.

- [ ] T007 [P] Add unit tests in `tests/FundingPlatform.Tests.Unit/Domain/Entities/Quotation_ChangeBranchTests.cs`: (1) `ChangeBranch_WithSameSupplierBranch_UpdatesBranchAndId`, (2) `ChangeBranch_WithCrossSupplierBranch_ThrowsArgumentException`, (3) `ChangeBranch_WithNull_ThrowsArgumentNullException`, (4) `ChangeBranch_DoesNotMutateCurrencyOrSnapshotOrConvertedCrcAmount`.

### Comparison-cache invalidation seam (FR-009)

- [ ] T008 [P] Create `src/FundingPlatform.Application/Abstractions/Comparison/IComparisonCacheInvalidator.cs`. Single member: `Task InvalidateForItemAsync(int itemId, CancellationToken ct = default);`.

- [ ] T009 Create `src/FundingPlatform.Infrastructure/Comparison/ComparisonCacheInvalidator.cs` implementing `IComparisonCacheInvalidator`. Inject `AppDbContext`. Implementation: delete every `ComparisonArtifact` row where `ItemId == itemId` (or set a stale flag if the spec 020 read path uses one — confirm against `src/FundingPlatform.Domain/Entities/ComparisonArtifact.cs` while implementing). `SaveChangesAsync(ct)`.

- [ ] T010 Register `IComparisonCacheInvalidator` in DI. Edit `src/FundingPlatform.AppHost/AppHost.cs` or the Web project's `Program.cs` (whichever currently registers Application/Infrastructure services for ApplicationService) and add `services.AddScoped<IComparisonCacheInvalidator, ComparisonCacheInvalidator>()`.

### Application service orchestration (FR-005..FR-011, NFR-004)

- [ ] T011 [P] Create `src/FundingPlatform.Application/Applications/Commands/EditQuotationCommand.cs` with the record shape from [data-model.md](./data-model.md) §2.1.

- [ ] T012 [P] Create `src/FundingPlatform.Application/Applications/Commands/EditQuotationResult.cs` containing `EditQuotationResult` record + `EditQuotationOutcome` enum from [data-model.md](./data-model.md) §2.2.

- [ ] T013 Implement `EditQuotationAsync(EditQuotationCommand command, CancellationToken ct = default)` on `src/FundingPlatform.Application/Services/ApplicationService.cs` following the orchestration order in [data-model.md](./data-model.md) §2.3. Inject `IComparisonCacheInvalidator` and reuse the already-injected `IConversionService`. The method MUST: (a) load with `Include(q => q.Supplier).ThenInclude(s => s.Branches)` + `Include(q => q.Item).ThenInclude(i => i.Application)` + `Include(q => q.Snapshot)`, (b) return field errors collected together (FR-005), (c) short-circuit when all four fields match (NFR-004), (d) run `ChangeCurrencyAsync` → `EditAmount` → `ChangeBranch` → `ValidUntil` assignment in that order (research §R0.7), (e) invoke `IComparisonCacheInvalidator.InvalidateForItemAsync` AFTER the save commits (FR-009), (f) catch `MissingRateException` from `ChangeCurrencyAsync` and map to `Outcome = MissingRate`.

- [ ] T014 Add `internal void SetValidUntil(DateOnly newValidUntil)` to `Quotation` (the entity has no such method today; keep the surface tight by making it `internal` since only the Application service in the same assembly need not see it — actually `Application` is a different assembly, so promote to `public` with a guard `if (newValidUntil < DateOnly.FromDateTime(DateTime.UtcNow.Date)) throw new ArgumentException(...);`). Add the validation to fail with the same es-CR copy the Application service uses; keep server-side ModelState aggregation responsible for surfacing it. **File**: `src/FundingPlatform.Domain/Entities/Quotation.cs`.

**Checkpoint**: Foundation ready — `ApplicationService.EditQuotationAsync` is callable; the shared `_QuoteFields.cshtml` partial is consumed by `Supplier/Add.cshtml`. User story implementation can now begin.

---

## Phase 3: User Story 1 — Applicant fixes a typo on a draft-stage quotation (Priority: P1) 🎯 MVP

**Goal**: An applicant on a `Draft` application can click *Editar* on a quotation row, change a price (or ValidUntil), save, and return to `Application/Edit` with the row updated and the quotation's identity (`Id`, `CreatedAt`) preserved.

**Independent Test**: Seed Applicant + Application(Draft) + Item + Quotation@1500 CRC. From the landing page, log in → open the application → click Editar on the quotation row → change Price to 1750 → save. Assert: 303 to `Application/Edit/{appId}`; row shows 1750; `Quotation.Id` and `CreatedAt` unchanged; CRC subtotal recomputed.

### Tests for User Story 1 (FIRST — must fail before implementation)

- [ ] T015 [P] [US1] Create the Page Object `tests/FundingPlatform.Tests.E2E/PageObjects/Application/QuotationEditPage.cs`. Locators by `data-testid`: `quotation-row-edit-{quotationId}`, `quotation-price-input`, `quotation-currency-input`, `quotation-validuntil-input`, `quotation-branch-input`, `quotation-submit-button`, `quotation-edit-validation-summary`. Methods: `EditPriceAsync(decimal)`, `SubmitAsync()`, `WaitForRedirectToApplicationEditAsync(int appId)`.

- [ ] T016 [P] [US1] Create `tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditPriceTests.cs` with two tests: (1) `EditsPriceOnDraft_PreservesIdentity` (golden — drives from landing, asserts price + identity + CRC subtotal), (2) `RejectsZeroPrice_FieldErrorReRendered` (POST Price=0, expects `400`, expects `quotation-edit-validation-summary` visible with es-CR price-must-be-positive copy).

- [ ] T017 [P] [US1] Create `tests/FundingPlatform.Tests.Integration/ApplicationServiceEditQuotationTests.cs` with tests covering: (a) state gate rejects `Submitted`/`UnderReview`/`Approved` with `Outcome.StateChanged`, (b) legacy flag rejects with `Outcome.LegacyFlagged`, (c) non-owner Applicant rejects with `Outcome.Forbidden`, (d) idempotent repeat-POST yields `Outcome.Success` with **no** `ExchangeRate.IsUsed` mutation, (e) Price-only edit preserves `CreatedAt` and re-multiplies CRC equivalent.

### Implementation for User Story 1

- [ ] T018 [US1] Create `src/FundingPlatform.Web/ViewModels/EditQuotationViewModel.cs` per [data-model.md](./data-model.md) §2.5. Implement `IQuoteFieldsModel`. Add `BranchOptions: IReadOnlyList<SelectListItem>` and `SupplierName: string` and `QuotationId` / `ItemId` / `ApplicationId` ints. Field-level `[Range]` / `[Required]` attributes use the es-CR copy quoted in [data-model.md](./data-model.md).

- [ ] T019 [US1] Add `GET Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit` and `POST Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit` to `src/FundingPlatform.Web/Controllers/QuotationController.cs` (placement: right after the `Convert` POST, before `Replace`). Both verbs call `VerifyOwnershipAsync(appId)` first. GET loads the quotation through `_applicationService` (extend or use a new read method `GetQuotationForEditAsync(appId, itemId, quotationId)`); the controller is responsible for the gate redirect when GET-time state is invalid (422 → redirect to `Application/Edit/{appId}` with `TempData["ErrorMessage"]`). POST is `[ValidateAntiForgeryToken]` and dispatches on `EditQuotationOutcome` per the [contract](./contracts/quotation-edit-endpoint.md).

- [ ] T020 [US1] Create `src/FundingPlatform.Web/Views/Quotation/Edit.cshtml`. Layout: page-header partial → form `data-quote-form data-convert-url="@convertUrl"` (same hook as Supplier/Add) → `<partial name="_QuoteFields" model="Model" />` → branch `<select asp-for="SupplierBranchId" asp-items="Model.BranchOptions">` → submit / cancel buttons. Cancel returns to `Application/Edit/{appId}`. Include `@section Scripts { <partial name="_ValidationScriptsPartial" /><script src="~/js/quote-conversion-preview.js" asp-append-version="true"></script> }`. Banner: *"Editando cotización de @Model.SupplierName"*.

- [ ] T021 [US1] Render quotation rows in `src/FundingPlatform.Web/Views/Application/Edit.cshtml`. Inside the existing `<tbody>` items loop (around line 209–233), expand each item row to render a nested table (or row-expander) of `item.Quotations` with columns: Supplier, Price+Currency, CRC equivalent, Vigente hasta, Acciones. Action cell: `Editar` (asp-controller Quotation, asp-action Edit), `Reemplazar` (existing Replace form), `Eliminar` (existing Delete form). The `Editar` button is rendered only when (a) Application.State ∈ {Draft, ReturnedForChanges} AND (b) `!quotation.LegacyNeedsReview` — both already derivable from the model state. data-testid: `quotation-row-edit-{quotationId}`.

- [ ] T022 [US1] Extend `ItemViewModel.Quotations` projection in `src/FundingPlatform.Application/Services/ApplicationService.cs` (or the projection helper that populates `ApplicationViewModel`) to also surface `SupplierBranchId` and `ValidUntil` on `QuotationSummaryViewModel` — needed by the row rendering for the affordance gate. Add the two fields to `src/FundingPlatform.Web/ViewModels/ApplicationViewModel.cs` `QuotationSummaryViewModel`.

**Checkpoint**: US1 fully functional. `QuotationEditPriceTests` E2E pass; `ApplicationServiceEditQuotationTests` integration pass.

---

## Phase 4: User Story 2 — Applicant applies a reviewer-requested correction (Priority: P1)

**Goal**: An applicant on a `ReturnedForChanges` application can change a quotation's branch (same supplier) and resubmit without losing the reviewer's comments tied to that quotation.

**Independent Test**: Seed Application(ReturnedForChanges) with two quotations and a reviewer comment referencing one. Edit that quotation's branch to a different branch of the **same** supplier. Assert: row reflects new branch; reviewer's prior comments on the quotation are still present; no soft-delete cycle occurred.

### Tests for User Story 2

- [ ] T023 [P] [US2] Create `tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditAfterReturnTests.cs` with two tests: (1) `SwapsBranchOnReturned_PreservesReviewerComments` (golden), (2) `RejectsCrossSupplierBranch` (POST a branch belonging to a different supplier; expects `400`, expects `quotation-edit-validation-summary` visible with *"Sucursal no válida para este proveedor."*). Both drive from the landing page.

- [ ] T024 [P] [US2] Extend `ApplicationServiceEditQuotationTests` (file from T017) with: `EditQuotation_BranchChangeOnReturnedForChanges_PersistsAndKeepsAuditTrail`, `EditQuotation_CrossSupplierBranch_ReturnsValidationFailedWithFieldError`.

### Implementation for User Story 2

- [ ] T025 [US2] Ensure `EditQuotationAsync` (from T013) routes branch changes through `Quotation.ChangeBranch` (already accounted for in the orchestration order). Verify that when the branch is the **only** change, no `SaveChangesAsync` writes to `ExchangeRate.IsUsed` and no snapshot reset occurs — this is implicit in the entity-method choice but must be validated by T024 integration tests.

- [ ] T026 [US2] Verify the affordance gate in `Application/Edit.cshtml` (from T021) renders the `Editar` button when `Application.State == ReturnedForChanges` (same code path as `Draft`). If `_StageCountdownBanner` hides the row or constrains it differently for `ReturnedForChanges`, adjust the partial guard.

**Checkpoint**: US2 fully functional. `QuotationEditAfterReturnTests` E2E pass; the two new integration cases pass.

---

## Phase 5: User Story 3 — Applicant changes currency on an attached quotation (Priority: P2)

**Goal**: An applicant changes a quotation's currency (CRC ↔ USD); the system snapshots a fresh rate, marks `ExchangeRate.IsUsed = true` (spec 015 FR-008), recomputes the CRC-equivalent, and silently invalidates the `ComparisonArtifact` cache for the Item (spec 020 / FR-009).

**Independent Test**: Seed Application(Draft) with a CRC quotation@100 and a published USD→CRC rate. Edit currency to USD (Price stays 100). Assert: fresh `ExchangeRateSnapshot` attached; `Quotation.ConvertedCrcAmount` matches `100 * publishedRate.Value` (rounded to 2 places); `ExchangeRate.IsUsed = true` on the consumed rate row; `Quotation.LegacyNeedsReview = false`.

### Tests for User Story 3

- [ ] T027 [P] [US3] Create `tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditCurrencyTests.cs`: (1) `ChangesCurrencyCrcToUsd_SnapshotFresh_RateMarkedUsed`, (2) `InvalidatesComparisonCacheOnEdit` (seed a `ComparisonArtifact` row for the Item before the Edit; after submit, query the DB and assert the row is gone or its hash key is stale per the spec 020 cache-read semantics). Both drive from the landing page.

- [ ] T028 [P] [US3] Extend `ApplicationServiceEditQuotationTests` with: `EditQuotation_CurrencyChange_TakesFreshSnapshotAndMarksRateUsed`, `EditQuotation_MissingRate_ReturnsOutcomeMissingRate`, `EditQuotation_AnyChange_InvokesCacheInvalidatorWithItemId` (use a fake `IComparisonCacheInvalidator` to verify the call).

### Implementation for User Story 3

- [ ] T029 [US3] Verify `EditQuotationAsync` (T013) routes currency changes through `Quotation.ChangeCurrencyAsync(newCurrency, _conversionService, ct)`. Confirm the order against research §R0.7: `ChangeCurrencyAsync` first (resets snapshot, retains old Price), then `EditAmount(newPrice)` if Price also changed (re-multiplies against the fresh snapshot). The `MissingRateException` is caught at the service boundary and mapped to `Outcome.MissingRate` with the `IUserFacingErrorTranslator`'s translation.

- [ ] T030 [US3] Confirm cache invalidation fires only on the non-idempotent success path. The idempotent short-circuit (NFR-004) MUST NOT invoke `IComparisonCacheInvalidator` (assert in T028's `EditQuotation_IdempotentRepeat_DoesNotInvalidateCache` case).

**Checkpoint**: US3 fully functional. `QuotationEditCurrencyTests` E2E pass; cache-invalidation integration tests pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Constitution compliance + delivery-quality checks across all stories.

- [ ] T031 [P] Run `dotnet test tests/FundingPlatform.Tests.Unit` — confirm `Quotation_ChangeBranchTests` green.

- [ ] T032 [P] Run `dotnet test tests/FundingPlatform.Tests.Integration` — confirm `ApplicationServiceEditQuotationTests` green and no regression elsewhere.

- [ ] T033 Run `dotnet test tests/FundingPlatform.Tests.E2E` — confirm the **entire** E2E suite is green, including pre-existing `Supplier/Add` tests (regression for FR-003 / SC-005). Per delivery memory `feedback_delivery_requires_e2e_green.md`, structural readiness is not a substitute.

- [ ] T034 Manual walkthrough of [quickstart.md](./quickstart.md) US1, US2, US3 paths against a fresh `dotnet run --project src/FundingPlatform.AppHost` instance. Confirm es-CR copy, keyboard navigation, and that the live conversion preview shows on the Edit form (FR-010).

- [ ] T035 [P] Verify `Application/Edit.cshtml` accessibility: each Editar button has `aria-label` or visible text; required-field markers per spec 021 convention are present on the Edit view; the conversion-preview alert exposes status updates via `data-preview-status` (NFR-002).

- [ ] T036 [P] Performance sanity-check (NFR-003): wall-clock the Edit GET p50 and POST p50 against an Application with 10 items × 3 quotations each (typical) using the Aspire instrumentation tab. Confirm GET ≤ 200 ms, POST ≤ 500 ms.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → no dependencies.
- **Foundational (Phase 2)** → depends on Setup. BLOCKS all user stories.
- **US1 (Phase 3, P1)** → depends on Phase 2. MVP gate.
- **US2 (Phase 4, P1)** → depends on Phase 2 (independent of US1 logic; can run in parallel with US1 once Phase 2 is complete).
- **US3 (Phase 5, P2)** → depends on Phase 2 (independent of US1 / US2 logic; can run in parallel once Phase 2 is complete).
- **Polish (Phase 6)** → depends on all desired user stories being complete.

### User Story Dependencies

- **US1**: independently testable after Phase 2 + T015..T022. No dependency on US2 or US3.
- **US2**: independently testable after Phase 2 + T023..T026. Reuses the same controller endpoints and service method; the only US2-specific code is the affordance gate for `ReturnedForChanges` (T026 — typically a one-line view conditional) and the two integration cases (T024).
- **US3**: independently testable after Phase 2 + T027..T030. Reuses the same controller endpoints and service method; US3-specific code is the cache-invalidation assertion (T028's fake-invalidator test) plus the E2E that drives a currency change.

### Within Each User Story

- E2E + integration tests are written first (per TDD discipline + constitution III + memory `feedback_delivery_requires_e2e_green.md`).
- Tests MUST fail before implementation; rerun after each implementation task.
- Models / view-models before views.
- Views before controller wiring (so the controller has a target view to render).
- Commit after each task or logical group.

### Parallel Opportunities

- All Phase 2 [P] tasks (T002, T004, T006, T007, T008, T011, T012) can run in parallel — different files, no shared state.
- T003, T005, T009, T010, T013, T014 are sequential (each edits an existing file that an upstream task also edits, or depends on an upstream type).
- Within US1: T015, T016, T017 ([P]) can run in parallel; T018, T020 ([P]) can run in parallel after T015..T017 pass and fail-first. T019 depends on T013 + T018. T021 depends on T022. T022 depends on T013's projection touch.
- Across stories: US1, US2, US3 tests + view tweaks (T023, T026, T027, T030) can be staffed independently once Phase 2 is complete.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Independent foundational tasks — run together once Phase 1 is green:
Task: "T002 [P] Define IQuoteFieldsModel marker interface in src/FundingPlatform.Web/ViewModels/IQuoteFieldsModel.cs"
Task: "T004 [P] Create _QuoteFields.cshtml shared partial in src/FundingPlatform.Web/Views/Shared/_QuoteFields.cshtml"
Task: "T006 [P] Add Quotation.ChangeBranch invariant in src/FundingPlatform.Domain/Entities/Quotation.cs"
Task: "T007 [P] Quotation_ChangeBranchTests unit suite in tests/FundingPlatform.Tests.Unit/Domain/Entities/Quotation_ChangeBranchTests.cs"
Task: "T008 [P] Create IComparisonCacheInvalidator interface"
Task: "T011 [P] Create EditQuotationCommand DTO"
Task: "T012 [P] Create EditQuotationResult + EditQuotationOutcome"
```

## Parallel Example: User Story 1 tests-first

```bash
# Launch US1 test scaffolds together before any US1 implementation lands:
Task: "T015 [P] [US1] QuotationEditPage Page Object"
Task: "T016 [P] [US1] QuotationEditPriceTests E2E suite"
Task: "T017 [P] [US1] ApplicationServiceEditQuotationTests integration suite"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational) — critical, blocks every story.
3. Complete Phase 3 (US1).
4. **STOP and VALIDATE**: `dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~QuotationEditPriceTests"` green. Manual walkthrough of the US1 path from the landing page.
5. MVP demo-ready.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → test → demo (MVP).
3. Add US2 → test → demo.
4. Add US3 → test → demo.
5. Polish phase last (full suite + perf + a11y sweep).

### Parallel Team Strategy

With multiple developers:

1. Team completes Phase 1 + Phase 2 together.
2. Once Foundational is done:
   - Developer A: US1 (Phase 3)
   - Developer B: US2 (Phase 4)
   - Developer C: US3 (Phase 5)
3. Stories integrate via the shared `EditQuotationAsync` service method built in T013. Each story owns its own E2E + integration tests; no cross-story conflicts on test files.
4. Polish phase synchronizes at the end.

---

## Notes

- [P] tasks = different files, no dependencies.
- Each user story is independently testable; the shared service method behaves identically regardless of which fields the test mutates.
- Verify tests fail before implementing.
- Commit after each task or logical group (per CLAUDE.md commit discipline + memory `feedback_speckit_checkpoints.md`).
- E2E tests drive from the landing page (memory `feedback_e2e_must_drive_real_user_journey.md`) — no deep-link shortcuts to `Quotation/{id}/Edit`.
- Avoid premature abstractions: `IComparisonCacheInvalidator` exists because the spec 020 contract is the boundary; do not introduce a second seam (e.g., domain event bus) without spec approval.
