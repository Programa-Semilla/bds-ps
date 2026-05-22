# Phase 1 Data Model: In-place Quotation Field Edit

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)

## 1. Domain layer

### 1.1 `Quotation` entity — additions

| Member | Kind | Purpose |
|---|---|---|
| `ChangeBranch(SupplierBranch branch)` | **new method** | Reassign `SupplierBranchId` and the `SupplierBranch` navigation. Asserts `branch.SupplierId == this.SupplierId`; throws `ArgumentException("Sucursal no válida para este proveedor.")` on mismatch. No exchange-rate side-effects. |

**No state mutation** to `Price`, `Currency`, `Snapshot`, `ConvertedCrcAmount`, `LegacyNeedsReview`. Branch reassignment is independent of the multi-currency rails (spec 015 untouched).

**Existing primitives reused unchanged**:

- `EditAmount(decimal newPrice)` — amount-only edit, snapshot stays pinned.
- `ChangeCurrencyAsync(CurrencyCode newCurrency, IConversionService conversion, CancellationToken ct)` — resets snapshot, re-applies the fresh rate against the current `Price`.

### 1.2 Invariants enforced on the entity (no schema change)

- `branch.SupplierId == quotation.SupplierId` (FR-004, spec 013).
- `Price > 0` (existing in `EditAmount` + `SetCurrencyAndAmountAsync`).
- `Currency.Length == 3` and uppercase (existing `NormalizeCurrency`).
- `LegacyNeedsReview = false` to edit amount (existing in `EditAmount`); extended logically to *any* Edit POST via the service-side guard (FR-011).

### 1.3 No new entities, no new tables, no new columns

Confirmed against spec Key Entities section and constitution Principle IV. The dacpac does **not** change in this spec.

## 2. Application layer

### 2.1 New command DTO

```csharp
// src/FundingPlatform.Application/Applications/Commands/EditQuotationCommand.cs
public sealed record EditQuotationCommand
{
    public int ApplicationId { get; init; }
    public int ItemId { get; init; }
    public int QuotationId { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly ValidUntil { get; init; }
    public int SupplierBranchId { get; init; }
    public int ApplicantId { get; init; }  // resolved from the current user in the controller
}
```

### 2.2 New service method

```csharp
// ApplicationService.cs — signature
public Task<EditQuotationResult> EditQuotationAsync(
    EditQuotationCommand command,
    CancellationToken ct = default);
```

**Result envelope**:

```csharp
public sealed record EditQuotationResult(
    EditQuotationOutcome Outcome,
    IReadOnlyDictionary<string, string>? FieldErrors = null,
    string? GlobalError = null);

public enum EditQuotationOutcome
{
    Success,                // 303 See Other to Application/Edit/{id}
    NotFound,               // 404 — quotation/item missing (Edge Case 1)
    Forbidden,              // 403 — non-owner Applicant (FR-007)
    StateChanged,           // 422 — Application not in Draft|ReturnedForChanges (FR-008)
    LegacyFlagged,          // 422 — LegacyNeedsReview = true (FR-011)
    ValidationFailed,       // 400 — FieldErrors populated (FR-005)
    MissingRate,            // 422 — translated by IUserFacingErrorTranslator (Edge Case currency)
}
```

### 2.3 Service-method orchestration order

1. Load Quotation with `Item.Application`, `Supplier.Branches`, `Snapshot` (single query, `Include` chain).
2. **403** if `Application.ApplicantId != command.ApplicantId`.
3. **404** if Quotation/Item not found or soft-deleted.
4. **422 StateChanged** if `Application.State ∉ {Draft, ReturnedForChanges}`.
5. **422 LegacyFlagged** if `Quotation.LegacyNeedsReview == true`.
6. Field validation → **400 ValidationFailed** with `FieldErrors`:
   - Price ≤ 0 → `"Price" → "El precio debe ser mayor a cero."`
   - Currency not in enabled Currencies → `"Currency" → "La moneda 'X' está deshabilitada."` (or *no está configurada* per `Convert` precedent)
   - ValidUntil < today (es-CR cal) → `"ValidUntil" → "La fecha de vigencia debe ser hoy o futura."`
   - SupplierBranchId not in `Supplier.Branches` → `"SupplierBranchId" → "Sucursal no válida para este proveedor."`
7. **Idempotency short-circuit** (NFR-004): if `(Price, Currency, ValidUntil, SupplierBranchId)` exactly match the loaded entity → no-op `Success`.
8. Apply mutations through entity methods (in order: ChangeCurrency, EditAmount, ChangeBranch, then `ValidUntil` direct set — `ValidUntil` has no domain method today; add it inline or `Quotation.SetValidUntil`).
9. `SaveChangesAsync()` (transactional).
10. **MissingRate** caught from `ChangeCurrencyAsync` → translated 422.
11. After commit: `IComparisonCacheInvalidator.InvalidateForItemAsync(itemId, ct)` (FR-009).
12. Return `EditQuotationOutcome.Success`.

### 2.4 New abstraction: `IComparisonCacheInvalidator`

```csharp
// src/FundingPlatform.Application/Abstractions/Comparison/IComparisonCacheInvalidator.cs
namespace FundingPlatform.Application.Abstractions.Comparison;

public interface IComparisonCacheInvalidator
{
    Task InvalidateForItemAsync(int itemId, CancellationToken ct = default);
}
```

Implementation in `FundingPlatform.Infrastructure/Comparison/ComparisonCacheInvalidator.cs` — uses the existing `ComparisonArtifact` `DbSet` (spec 020) and removes the row(s) keyed on `(ItemId, *)`. Wiring: registered in `AppHost.cs` (or `Program.cs`) as `services.AddScoped<IComparisonCacheInvalidator, ComparisonCacheInvalidator>()`.

### 2.5 New / refactored ViewModels (Web layer cross-ref)

```csharp
// src/FundingPlatform.Web/ViewModels/IQuoteFieldsModel.cs — marker
public interface IQuoteFieldsModel
{
    decimal Price { get; set; }
    string Currency { get; set; }
    DateOnly ValidUntil { get; set; }
    IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; }
}
```

`AddSupplierViewModel` implements `IQuoteFieldsModel` (no new property — interface aligns to existing names; `EnabledCurrencies` setter relaxed to settable).

```csharp
// src/FundingPlatform.Web/ViewModels/EditQuotationViewModel.cs
public class EditQuotationViewModel : IQuoteFieldsModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }
    public int QuotationId { get; set; }

    [Required, Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a cero.")]
    [Display(Name = "Precio")]
    public decimal Price { get; set; }

    [Required, Display(Name = "Moneda")]
    public string Currency { get; set; } = string.Empty;

    [Required, Display(Name = "Vigente hasta")]
    public DateOnly ValidUntil { get; set; }

    [Required, Display(Name = "Sucursal del proveedor")]
    public int SupplierBranchId { get; set; }

    public IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; } = [];
    public IReadOnlyList<SelectListItem> BranchOptions { get; set; } = [];
    public string SupplierName { get; set; } = string.Empty;  // banner: "Editando cotización de {supplierName}"
}
```

## 3. Persistence

- No EF model change. No migration. No dacpac edit.
- The EF `Include` chain on the GET handler: `Quotations → Supplier → Branches`, `Item → Application`.

## 4. Audit / observability

No new audit event. Constitution Principle V + assumption 5 of the spec ("All quotation field changes are auditable through the existing application-event stream; no new admin-audit event type is introduced in v1").

Logging: the service writes one structured log line on each Edit attempt with `ApplicationId`, `QuotationId`, outcome, and (for Success) which fields changed. No PII.

## 5. State-flow summary

```
┌─────────────────────────────┐
│ GET  …/Quotation/{id}/Edit  │
└────────────┬────────────────┘
             ▼
   Load Quotation + Item + App + Supplier.Branches
             │
             ▼   (any of these → redirect to Application/Edit with TempData["ErrorMessage"])
   ┌──────────────────────────┐
   │  403 / 404 / 422 gates   │
   └────────────┬─────────────┘
                ▼
   Render Edit.cshtml (uses _QuoteFields.cshtml)

────────────────────────────────────────────────────

┌──────────────────────────────┐
│ POST …/Quotation/{id}/Edit   │
└────────────┬─────────────────┘
             ▼
   ApplicationService.EditQuotationAsync(...)
             ├─→ 403 / 404 / 422 gates (same as GET, plus ModelState collect)
             ├─→ Idempotency short-circuit (no-op Success)
             ├─→ ChangeCurrencyAsync? → EditAmount? → ChangeBranch? → SetValidUntil
             ├─→ SaveChangesAsync
             └─→ IComparisonCacheInvalidator.InvalidateForItemAsync(itemId)
             ▼
   303 → Application/Edit/{appId}
```
