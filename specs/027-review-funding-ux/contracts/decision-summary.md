# Contract: Decision-Summary Projection & Partial (US4)

The single shared contract that makes the per-line decision data identical across the five interaction surfaces.

## Application contract

```csharp
namespace FundingPlatform.Application.DTOs;

public sealed record DecisionSummaryLineDto(
    string? LineCode,
    string ProductName,
    string CategoryName,
    string TechnicalSpecifications,
    ItemReviewStatus ReviewStatus,
    string? ReviewComment,
    string? ApprovedSupplierName,
    DecisionSummaryQuotationView? ApprovedAmount,
    IReadOnlyList<DecisionSummaryQuotationView> Quotations,
    string? ApplicantDecision);

public sealed record DecisionSummaryQuotationView(
    string SupplierName,
    decimal Amount,
    string Currency,
    decimal? ConvertedCrcAmount,
    string? CurrencyConversionNote);
```

```csharp
namespace FundingPlatform.Application.Services;

public interface IDecisionSummaryProjection
{
    // Pure mapping over an already-loaded Application aggregate (Items→Category,
    // Items→Quotations→Supplier, ApplicantResponses→ItemResponses).
    IReadOnlyList<DecisionSummaryLineDto> Project(Domain.Entities.Application application);
}
```

### Mapping rules
- Order: `LineCode` (ordinal, nulls last) then `Id`.
- `Approved` line: `ApprovedSupplierName` + `ApprovedAmount` from the quotation whose `SupplierId == Item.SelectedSupplierId`.
- `Rejected` line: `ReviewComment` (reason) + the full `Quotations` list (every quoted supplier + amount).
- `Pending`/`NeedsInfo`: status only; `Quotations` may still be listed for context.
- `ApplicantDecision`: es-CR label from the latest `ApplicantResponse.ItemResponses[ItemId].Decision`; null when no response yet.
- `CurrencyConversionNote`: null when `Currency == "CRC"`; else `"Conversión: 1 {CUR} = ₡{rate} (Tipo {Compra|Venta}, vigente desde {yyyy-MM-dd})"` (lift from `FundingAgreementController.BuildConversionNote`).

## Web partial contract

`Views/Shared/_DecisionSummary.cshtml` — `@model IReadOnlyList<DecisionSummaryLineDto>`
- Renders one block per line: header (line code badge + product + category + status badge), technical specifications, then:
  - approved → "Proveedor: {name} — Monto: {amount}{conversion note}";
  - rejected → "Razón: {comment}" + a "Opciones cotizadas" table (supplier | amount);
  - applicant decision label when present.
- Status badges es-CR: Aprobado / Rechazado / Requiere información / Pendiente.
- Read-only. Carries no form controls. Safe to render on reviewer, applicant, and signing surfaces alike.

## Consumers (acceptance: identical fields on each)
1. `Review/Review.cshtml` — render alongside the existing capture UI (capture UI unchanged).
2. `ApplicantResponse/Index.cshtml` — replaces the current item table; **gains technical specs**.
3. `FundingAgreement/Details.cshtml` — replaces the approved-only preview (covers generate / signing / signed-review states).
4. `Review/SigningInbox.cshtml` — keeps its per-application link to Details (where the summary now lives).

## Out of scope
- The generated PDF document (`Views/FundingAgreement/Document.cshtml` + `Partials/*`) is **not** changed (FR-009, spec 018).
- AI comparison (spec 020), supplier scores, and impact parameters are **not** folded into this shared block.
