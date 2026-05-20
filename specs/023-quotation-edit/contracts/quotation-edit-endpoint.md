# Contract: Quotation Edit Endpoint

**Owner**: `QuotationController` · **Spec**: [../spec.md](../spec.md) · **Data model**: [../data-model.md](../data-model.md)

## Route

```
{controller route prefix}: Application/{appId}/Item/{itemId}/Quotation

GET   Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit
POST  Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit
```

Both routes require:

- `[Authorize(Roles = "Applicant")]` (inherited from controller).
- The acting user owns `appId` (via existing `VerifyOwnershipAsync(appId)`).

## GET — render Edit form

### Inputs

| Param | Source | Type | Notes |
|---|---|---|---|
| `appId` | route | `int` | |
| `itemId` | route | `int` | |
| `quotationId` | route | `int` | |

### Status codes

| Code | When | Body |
|---|---|---|
| `200 OK` | Quotation found, application in `Draft` or `ReturnedForChanges`, quotation not `LegacyNeedsReview`, user owns application | `Edit.cshtml` rendered with `EditQuotationViewModel` populated |
| `403 Forbidden` | Acting user is not the application's owner Applicant | Standard `Error403` view (`UnauthorizedAccessException` from `VerifyOwnershipAsync`) |
| `404 Not Found` | Quotation, Item, or Application missing or soft-deleted | Standard 404 |
| `422 Unprocessable Entity` | Application state ∉ `{Draft, ReturnedForChanges}` OR `LegacyNeedsReview == true` | Redirect to `Application/Edit/{appId}` with `TempData["ErrorMessage"]` set to the appropriate es-CR copy |

### Response model (`EditQuotationViewModel`)

```csharp
{
  ApplicationId: int,
  ItemId: int,
  QuotationId: int,
  Price: decimal,                       // current quotation price
  Currency: string,                     // 3-letter ISO, current
  ValidUntil: DateOnly,                 // current
  SupplierBranchId: int,                // current
  EnabledCurrencies: CurrencyOption[],  // from dbo.Currencies WHERE IsEnabled = 1
  BranchOptions: SelectListItem[],      // from quotation.Supplier.Branches
  SupplierName: string                  // for the page banner
}
```

## POST — save edits

### Inputs

| Param | Source | Type | Validation |
|---|---|---|---|
| `appId` | route | `int` | |
| `itemId` | route | `int` | |
| `quotationId` | route | `int` | |
| `Price` | form | `decimal` | `> 0` |
| `Currency` | form | `string` | 3-letter, present in `dbo.Currencies WHERE IsEnabled=1` |
| `ValidUntil` | form | `DateOnly` | `≥ today` (es-CR calendar) |
| `SupplierBranchId` | form | `int` | belongs to `quotation.Supplier.Branches` |

Anti-forgery token required (`[ValidateAntiForgeryToken]`).

### Status codes

| Code | When | Behavior |
|---|---|---|
| `303 See Other` | All gates pass; persistence committed (or no-op idempotent repeat) | `Location: /Application/Edit/{appId}`. `TempData["SuccessMessage"]` = *"Cotización actualizada con éxito."* |
| `400 Bad Request` | Field validation failed (Price/Currency/ValidUntil/SupplierBranchId) | `Edit.cshtml` re-rendered with `ModelState` errors. **All** field errors are returned on the same response. |
| `403 Forbidden` | Non-owner Applicant | Standard `Error403` |
| `404 Not Found` | Quotation/Item/Application missing (Edge Case 1) | `Edit.cshtml` re-rendered with `ModelOnly` error *"La cotización ya no existe."* (or redirect to `Application/Edit/{appId}`) |
| `422 Unprocessable Entity` | App state changed mid-POST, OR `LegacyNeedsReview == true`, OR `MissingRate` from `ChangeCurrencyAsync` | `Edit.cshtml` re-rendered with `ModelOnly` error; es-CR copy: <br/>- *"El estado de la solicitud cambió, recarga la página."* (state) <br/>- *"Esta cotización está marcada para revisión administrativa de tipo de cambio."* (legacy) <br/>- existing `IUserFacingErrorTranslator.Translate(UserFacingErrorCode.MissingExchangeRate)` (missing rate) |

### Idempotency (NFR-004)

If `(Price, Currency, ValidUntil, SupplierBranchId)` exactly match the loaded entity:

- No DB write.
- No `ExchangeRate.MarkUsed` (the rate consumption counter stays unchanged).
- No `ComparisonArtifact` invalidation.
- Response is still `303 See Other` with the success copy (the applicant cannot tell the difference; this is the spec-mandated double-click defense).

### Side-effects on Success (non-idempotent path)

1. `Quotation.ChangeCurrencyAsync` runs **iff** the currency changed → resets snapshot, takes fresh rate, marks `ExchangeRate.IsUsed = true` (spec 015 FR-008).
2. `Quotation.EditAmount` runs **iff** the price changed → re-multiplies against the (possibly freshly-applied) snapshot.
3. `Quotation.ChangeBranch` runs **iff** the branch changed → enforces same-supplier invariant.
4. `Quotation.ValidUntil` is set **iff** the date changed (no domain event).
5. `IComparisonCacheInvalidator.InvalidateForItemAsync(itemId, ct)` fires synchronously after `SaveChangesAsync` (FR-009).

## Anti-CSRF / authorization

- `[ValidateAntiForgeryToken]` on the POST (consistent with sibling `Replace` and `Delete` endpoints).
- `[Authorize(Roles = "Applicant")]` on the controller.
- `VerifyOwnershipAsync(appId)` runs before any business logic on both verbs.

## E2E test coverage (constitution III)

| Test class | US | Verb path |
|---|---|---|
| `QuotationEditPriceTests.EditsPriceOnDraft` | US1 golden | Landing → Login → Application/Index → Application/Edit/{appId} → click Editar on quotation row → GET form → POST 1500→1750 → 303 → row reflects 1750, `CreatedAt` unchanged |
| `QuotationEditPriceTests.RejectsZeroPrice` | US1 error | Same path; POST with Price `0`; expect `400` re-render + es-CR field error |
| `QuotationEditAfterReturnTests.SwapsBranchOnReturned` | US2 golden | Same path but Application starts `ReturnedForChanges`; swap branch → row reflects new branch |
| `QuotationEditAfterReturnTests.RejectsCrossSupplierBranch` | US2 error | Same path; POST with a `SupplierBranchId` belonging to a different supplier; expect `400` with *"Sucursal no válida para este proveedor."* |
| `QuotationEditCurrencyTests.CrcToUsdSnapshot` | US3 golden | Same path; CRC quote → USD; verify fresh snapshot + `ExchangeRate.IsUsed = true` |
| `QuotationEditCurrencyTests.InvalidatesComparisonCache` | US3 cache | Seed a `ComparisonArtifact` for the item; perform Edit; assert the row is gone (or stale) |

Each test starts from the landing page (memory `feedback_e2e_must_drive_real_user_journey.md`).
