# Phase 1 Contracts: Supplier Recommendation Algorithm Rewrite

**Spec:** spec.md | **Data model:** data-model.md | **Date:** 2026-06-18

This feature is internal (no new HTTP API). The contracts are: the domain scoring function, the domain eligibility guard, the quote-capture form contract, and the reviewer surface contract.

---

## C1 — Domain scoring contract

`SupplierScore.ComputeForItem` (signature preserved; result shape expanded — see data-model §5).

```csharp
public static List<(int QuotationId, SupplierScore Score)> ComputeForItem(
    List<(Quotation Quotation, Supplier Supplier, SupplierBranch? Branch)> quotations);
```

**Guarantees:**
- Empty input → empty result.
- A candidate with `Supplier.CcssStatus == CcssStatus.SinInscripcion` → `IsEligible=false`, `BlockReason=CcssSinInscripcion`, no criterion scores, `IsRecommended=false`, `IsTiedAtTop=false`. Excluded from all winner comparisons.
- Each **eligible** candidate gets a base 1 on every criterion; winner(s) get 2 (price ties → all 1; delivery/warranty ties → all 2; status criteria binary).
- `Total` ∈ [7,14] for eligible candidates.
- `IsRecommended=true` for exactly one candidate **iff** a single eligible candidate strictly holds the max total; otherwise `false` for all and the tied set carries `IsTiedAtTop=true`.
- Pure function — no I/O, no DB, deterministic. Re-callable on every read.

**Caller:** `ReviewService` (`:345-353`) maps the result into `ReviewQuotationDto`; the per-item VM derives `HasRecommendationTie` / `HasAnyEligible`.

---

## C2 — Domain eligibility guard (progression gate)

`Item.Approve(int supplierId, string? comment)` (`Domain/Entities/Item.cs:281-299`) gains an eligibility guard.

**New behavior:**
- Resolve the selected quotation for `supplierId` (existing invariant: a quotation must exist).
- If that quotation's `Supplier.CcssStatus == CcssStatus.SinInscripcion`, throw a domain failure (`SupplierIneligibleException`, or a `DomainError` with code `SUPPLIER_CCSS_SIN_INSCRIPCION`) **before** setting `ReviewStatus = Approved` / `SelectedSupplierId`.
- All other statuses (and `null`) → approval proceeds unchanged.

**Translation contract:** `ReviewService.ReviewItemAsync` (`Application/Services/ReviewService.cs:103-193`) ensures `Quotation.Supplier` is loaded for the selected supplier, catches the domain failure, and returns the es-CR reviewer error (no approval persisted). `ReviewController.ReviewItem` (`:611-629`) re-renders the review surface with the error. Selecting the provider in the dropdown stays possible; only the Approve submission is rejected (FR-019).

**es-CR message (D11):** e.g. `"No se puede aprobar el ítem: el proveedor «{nombre}» no está inscrito en la CCSS."`

---

## C3 — Quote-capture form contract

The shared `_QuoteFields.cshtml` (`Views/Shared/_QuoteFields.cshtml`) and `IQuoteFieldsModel` gain four bound fields, required on both the add-supplier (`AddSupplierViewModel`) and quotation-edit (spec 023) paths.

| Field | Type | Validation (es-CR) |
|---|---|---|
| `DeliveryLeadTimeValue` | int | Required; `[Range(1, int.MaxValue)]` "El tiempo de entrega debe ser mayor a cero." |
| `DeliveryLeadTimeUnit` | `DurationUnit` | Required; select of días/meses |
| `WarrantyValue` | int | Required; `[Range(1, int.MaxValue)]` "La garantía debe ser mayor a cero." |
| `WarrantyUnit` | `DurationUnit` | Required; select of días/meses |

**Handler contract:** the add-quotation and edit-quotation command handlers construct/update `Quotation` with `new TimeDuration(value, unit)` for each field. Invalid values surface as collected ModelState errors (Constitution quality gate: all validation errors shown at once).

**Rendering note:** new inputs render within `_QuoteFields.cshtml` alongside Price/Currency/ValidUntil, so both Supplier/Add and Quotation/Edit get them automatically.

---

## C4 — Reviewer surface contract (explainability + tie + block)

`Views/Review/Review.cshtml`:

- **Score cell (`~:255`):** replace `@q.Score/4` with the total + a per-criterion breakdown (seven labelled scores: Precio, Entrega, Garantía, Hacienda, CCSS, SICOP, PYME) and the raw values (price, delivery value+unit, warranty value+unit, three statuses, PYME flag). Recommended provider visibly marked `Recomendado`.
- **Blocked provider:** rendered `bloqueado` with reason, visually distinct from a low-scoring eligible provider; not shown as recommended.
- **Tie:** when `HasRecommendationTie`, no `Recomendado` badge; show "selección manual requerida" and flag the tied set.
- **No eligible provider:** when `!HasAnyEligible`, show "ningún proveedor elegible" for the item.
- **Supplier-selection dropdown (`~:427`):** label uses the total (removes the stray `/5`).

---

## C5 — Item-line form order contract (§6/§24.4)

`Views/Item/Add.cshtml`: DOM order becomes ProductName → CategoryId → `#category-fields` (dynamic partial) → remaining fields. The category-change AJAX wiring (`_DynamicFieldWiring.cshtml` → `Item/CategoryFields`) is unchanged; `#category-fields` stays after the category select. No VM/controller/route change.

---

## Out-of-contract (unchanged)

`ComparisonArtifact` / `IComparisonOrchestrator` / comparison worker / `_ComparisonRegion.cshtml` (spec 020) — untouched (D9). No new HTTP endpoints, routes, or managed dependencies.
