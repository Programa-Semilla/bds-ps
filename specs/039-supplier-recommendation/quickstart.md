# Quickstart: Supplier Recommendation Algorithm Rewrite

**Spec:** spec.md | **Plan:** plan.md | **Date:** 2026-06-18

## What this delivers

Replaces the price-dominant /4 supplier score with the client's seven-criterion, deterministic, explainable recommendation; adds required delivery-lead-time and warranty fields to quotations; hard-blocks CCSS `sin inscripción` providers; reorders the add-item form (product name first). The spec-020 AI comparison is unchanged.

## Build & run

```bash
dotnet build FundingPlatform.slnx
dotnet run --project src/FundingPlatform.AppHost     # dev (auto-deploys dacpac incl. new columns + seed update)
```

## Tests (delivery bar = filtered E2E)

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # SupplierScore algorithm, TimeDuration, eligibility
dotnet test tests/FundingPlatform.Tests.Integration   # quote capture w/ new required fields; Item.Approve gate
dotnet test tests/FundingPlatform.Tests.E2E --filter "SupplierRecommendation|QuoteFields|ItemFieldOrder"
```

## Manual smoke (reviewer)

1. Seed an item with three quotations where the lowest-price provider is **not** the best on delivery/warranty/regulatory standing.
2. Open the reviewer Review screen → confirm the higher-total provider is `Recomendado`, with all seven per-criterion scores + raw values shown.
3. Set one provider's CCSS to `sin inscripción` (auditor surface, slice A) → confirm it shows `bloqueado`, is excluded from scoring, and the item cannot be approved with it selected (es-CR message).
4. Arrange a top-score tie → confirm no `Recomendado` badge and "selección manual requerida".

## Manual smoke (applicant)

1. Add a quotation → confirm delivery lead time and warranty (value + días/meses) are required; blank/zero is rejected.
2. Add an item → confirm product name renders before the category selector, dynamic category fields after category selection.

## Key files

- Algorithm: `src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs`
- New types: `Enums/DurationUnit.cs`, `ValueObjects/TimeDuration.cs`
- Entity + schema: `Domain/Entities/Quotation.cs`, `Infrastructure/Persistence/Configurations/QuotationConfiguration.cs`, `Database/Tables/dbo.Quotations.sql` (+ post-deploy seed update)
- Gate: `Domain/Entities/Item.cs` (`Approve`), `Application/Services/ReviewService.cs`
- Forms: `Views/Shared/_QuoteFields.cshtml`, `ViewModels/AddSupplierViewModel.cs` + quotation-edit VM, `Views/Item/Add.cshtml`
- Reviewer surface: `Views/Review/Review.cshtml`, `DTOs/ReviewApplicationDto.cs`, `ViewModels/ReviewApplicationViewModel.cs`
