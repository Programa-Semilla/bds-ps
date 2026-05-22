# Phase 0 Research: In-place Quotation Field Edit

**Date**: 2026-05-20
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

## R0.1 — Where does the Edit affordance render?

**Decision**: Inline quotation rows on `Application/Edit.cshtml`, under each Item card.

**Rationale**:
- FR-001 explicitly names `Application/Edit` (originating bug ticket URL: `/Application/Edit/1003`).
- `ItemViewModel.Quotations: List<QuotationSummaryViewModel>` is already populated by the application-service projection; the data is in place — only the view needs the per-row affordances.
- After the spec 021 commit `07e07c6 (fix(021): US2 — decommission Details as draft editor)`, `Application/Details` is no longer the draft-editor surface. `Application/Edit` is the only applicant-facing draft-edit page, so consolidating quotation Edit there matches the post-spec-021 information architecture.
- The applicant gets a single page for the whole draft (Impact + Items + Quotations) which mirrors the May-13 stakeholder ask for "one screen, no leaving".

**Alternatives considered**:
- *Item/Edit*: Cleaner separation but adds a click and re-renders the breadcrumb stack. Rejected on UX grounds (the originating bug was a one-click expectation).
- *Both Application/Edit and Item/Edit*: Avoidable duplication; Application/Edit is the single source of truth for the in-flight draft. Item/Edit stays scoped to product-name/category/specs.

## R0.2 — Shared partial shape: `_QuoteFields.cshtml`

**Decision**: Extract Price + Currency + ValidUntil inputs (plus the conversion-preview alert block) from `Supplier/Add.cshtml` into `Views/Shared/_QuoteFields.cshtml`. The partial is bound against a small marker interface implemented by both `AddSupplierViewModel` and the new `EditQuotationViewModel`.

**Rationale**:
- FR-003 requires reuse. A marker interface (`IQuoteFieldsModel { decimal Price; string Currency; DateOnly ValidUntil; IReadOnlyList<CurrencyOption> EnabledCurrencies; }`) keeps the partial typed and lets the Razor expression `asp-for="Price"` resolve against the host model's namespace exactly as today.
- Server-rendered preview alert (`<div data-quote-preview hidden>...`) is included so the `quote-conversion-preview.js` hookup (`data-quote-form` + `data-convert-url`) carries over to the Edit form unchanged.

**Alternatives considered**:
- *Duplicate the markup*: rejected — directly contradicts FR-003.
- *Tag helper / view component*: heavier than needed for three fields; defer until a third callsite emerges.

**Binding contract for `_QuoteFields.cshtml`**:
```cshtml
@model FundingPlatform.Web.ViewModels.IQuoteFieldsModel
<div class="row">
  <div class="col-md-4 mb-3">
    <label asp-for="Price" class="form-label"></label>
    <input asp-for="Price" class="form-control" type="number" step="0.01" min="0.01"
           data-testid="quotation-price-input" />
    <span asp-validation-for="Price" class="text-danger"></span>
  </div>
  <div class="col-md-4 mb-3" data-testid="form-section" data-field="Currency">
    <label asp-for="Currency" class="form-label"></label>
    <select asp-for="Currency" class="form-select" data-testid="quotation-currency-input">
      @foreach (var option in Model.EnabledCurrencies)
      {
        <option value="@option.Code">@option.Symbol @option.DisplayName (@option.Code)</option>
      }
    </select>
    <span asp-validation-for="Currency" class="text-danger"></span>
  </div>
  <div class="col-md-4 mb-3">
    <label asp-for="ValidUntil" class="form-label"></label>
    <input asp-for="ValidUntil" class="form-control" type="date" data-testid="quotation-validuntil-input" />
    <span asp-validation-for="ValidUntil" class="text-danger"></span>
  </div>
</div>
<div class="mb-3 alert alert-info d-none" data-quote-preview hidden>
  <div class="fw-semibold">Conversión a colones</div>
  <div data-preview-amount class="fs-4"></div>
  <div data-preview-rate class="text-muted small"></div>
  <div data-preview-status class="small"></div>
</div>
```

## R0.3 — Branch picker data source

**Decision**: The Edit GET handler eager-loads `Quotation.Supplier.Branches` (filtered to `IsActive` if such a flag exists on `SupplierBranch`; otherwise all) and projects them into a `IReadOnlyList<SelectListItem>` on the view-model. The branch `<select>` lists only branches of the quotation's current Supplier (FR-004).

**Rationale**:
- Switching to a different Supplier is not permitted via Edit (FR-004); the picker therefore never exposes cross-supplier branches.
- Server-side validation re-asserts `selectedBranch.SupplierId == quotation.SupplierId` on POST, returning the es-CR error *"Sucursal no válida para este proveedor."* on mismatch. The check lives on `Quotation.ChangeBranch(SupplierBranch)` in the entity so it cannot be bypassed by a future caller.

**Alternatives considered**:
- *AJAX-loaded picker*: unnecessary — the branch list is bounded (typically <10) and known at GET time.

## R0.4 — `ComparisonArtifact` cache-invalidation seam

**Decision**: Introduce `IComparisonCacheInvalidator` in `FundingPlatform.Application.Abstractions.Comparison` with a single method `Task InvalidateForItemAsync(int itemId, CancellationToken ct)`. The Infrastructure implementation deletes the `ComparisonArtifact` row (or sets a stale flag — to be confirmed by the spec 020 read path) keyed on `(ItemId, Hash)`. The service invokes the invalidator after the DB transaction commits but inside the same request — synchronous, fail-fast on error.

**Rationale**:
- FR-009 specifies *silent* invalidation; the applicant sees no UI churn. The next reviewer *Generar todo* picks up the cache miss and regenerates.
- A narrow interface preserves spec 020 internals (the orchestrator + worker stay encapsulated) and avoids importing spec 020 types into spec 023's service contract.
- Synchronous invalidation matches FR-009 plus the success-criterion SC-006 ("cache miss, regenerates, new hash").

**Alternatives considered**:
- *Domain event + handler*: more decoupled but introduces an event bus where none exists in spec 023's surface. Defer until a second cache-invalidation trigger emerges.
- *Async fire-and-forget via the existing comparison BackgroundService*: rejected — race against a reviewer who opens the comparison region the instant after the edit commits.

## R0.5 — Validation aggregation

**Decision**: Server-side validation uses `ModelState` on `EditQuotationViewModel`. Field-level errors (Price ≤ 0, Currency missing/disabled, ValidUntil < today, BranchId not in supplier's branch set) are all collected before the controller returns. The view re-renders with the same VM and surfaces every error via `asp-validation-for=...`.

**Rationale**:
- Constitution quality gate: *"All validation errors MUST be collected and displayed at once."*
- Matches the prevailing pattern across the Web project (Application/Edit, Supplier/Add).
- The state-changed and missing-rate cases are *not* field-level — they re-render the form with a top-level `ModelOnly` summary using the existing es-CR copy and a 422 status code (per spec Edge Cases + FR-008).

**Alternatives considered**:
- *Fail-fast / first-error-wins*: violates the constitution gate.
- *JSON error envelope*: unnecessary for an MVC-rendered form; the existing convention keeps it identical to Item/Edit.

## R0.6 — Branch invariant: entity vs. service?

**Decision**: Add `Quotation.ChangeBranch(SupplierBranch branch)` to the Domain entity. The method asserts `branch.SupplierId == this.SupplierId` and throws `ArgumentException("Sucursal no válida para este proveedor.")` on mismatch. The service translates the exception into a `ModelState` field error keyed to `SupplierBranchId`.

**Rationale**:
- Constitution principle II: invariants belong on the entity, not in services.
- The existing `Quotation` constructor + `AttachLegacyRate` follow the same pattern (entity-owned guards).

**Alternatives considered**:
- *Service-only validation*: would let a future caller bypass the invariant. Rejected.

## R0.7 — Same-POST currency + price atomicity

**Decision**: When the POST changes both Currency and Price (Edge Case 4), the service:
1. Loads the Quotation.
2. If `Currency` changed: calls `quotation.ChangeCurrencyAsync(newCurrency, _conversion, ct)` which resets the snapshot and re-applies the fresh rate against the *old* Price (because `ChangeCurrencyAsync` retains `Price`).
3. If `Price` changed: calls `quotation.EditAmount(newPrice)` which re-multiplies against the freshly-applied snapshot.
4. If only one changed: only that step runs.

This matches the spec's "snapshot is reset to the fresh rate first, then the new price is applied against that snapshot."

**Rationale**: `ChangeCurrencyAsync` + `EditAmount` are already idempotent and the combined order yields the spec-mandated behavior without adding a new entity method.

## R0.8 — Idempotent repeat-POST (NFR-004)

**Decision**: The service short-circuits with no DB write and no rate consumption when the incoming `Price`/`Currency`/`ValidUntil`/`SupplierBranchId` exactly match the current entity values. A duplicate POST is therefore a no-op at the DB level and at the `ExchangeRate.IsUsed` audit level.

**Rationale**: Double-click defense (Edge Cases bullet 6) plus the explicit NFR-004 requirement.

## R0.9 — E2E coverage shape

**Decision**: Three test classes, one per US, each driving the full user journey from the landing page to the Edit form (per memory `feedback_e2e_must_drive_real_user_journey.md`):

| Class | US | Golden + edges |
|---|---|---|
| `QuotationEditPriceTests` | US1 | Edit price 1500→1750 on Draft; zero-price field error |
| `QuotationEditAfterReturnTests` | US2 | Branch swap (same supplier); cross-supplier rejection; comments preserved |
| `QuotationEditCurrencyTests` | US3 | CRC→USD snapshot + `ExchangeRate.IsUsed = true`; ComparisonArtifact cache miss after edit |

Page Object Model: one new `QuotationEditPage.cs` under `tests/FundingPlatform.Tests.E2E/PageObjects/Application/`. Reuses `ApplicationEditPage` for navigation.

## R0.10 — Routing

**Decision**: Reuse the existing route prefix `Application/{appId}/Item/{itemId}/Quotation` already owned by `QuotationController`. The new endpoints are:

- `GET   Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit`  → form
- `POST  Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit`  → save (303 See Other → `Application/Edit/{appId}`)

This keeps the URL shape symmetric with `/Replace` and `/Delete` siblings (lines 134 + 171 of `QuotationController.cs`).
