# Phase 1 Data Model: 027 Review & Funding-Agreement UX Refinements

**No database schema change** (FR-027). This feature reuses existing entities/columns and adds Application-layer projection types + Web view models only. This document records the entities touched and the new in-memory shapes.

## Existing entities referenced (unchanged schema)

### ApplicationUser (`dbo.AspNetUsers`)
- `CodigoPersonal NVARCHAR(40) NULL` — **reused by US5.** Already exists; gains a *write surface* (reviewer/admin on the review screen). No column change. Read-only on the owner's profile (unchanged).
- `FirstName`, `LastName`, `Email` — feed display-name resolution (US1) and applicant detail (US3).

### Applicant (`dbo.Applicants`)
- `UserId` → `ApplicationUser.Id` (owner linkage used to resolve the applicant user for US5 and applicant detail for US3).
- `LegalId`, `IdentificationType`, `Email`, `Phone` — surfaced on the funding-agreement page (US3).
- Group membership (spec 016) — applicant's group shown in US3.

### Application
- `Applicant` navigation; `Items`; submission date — feed US3 (group, submission date) and US4 (line list).
- Reaches a Process via `Group.ProcessId` (spec 021) — relevant to US8 "Starters" filtering only.

### Item (`dbo.Items`)
- `ProductName`, `CategoryId`→`Category.Name`, `TechnicalSpecifications`, `LineCode`, `ReviewStatus` (enum Pending|Approved|Rejected|NeedsInfo), `ReviewComment`, `SelectedSupplierId`, `Quotations` — the source of the US4 per-line summary.

### Quotation (`dbo.Quotations`)
- `Price`, `Currency`, `ConvertedCrcAmount`, `Snapshot` (rate value/type/effective — spec 015), `ValidUntil`, `Supplier.Name` — the per-quote detail; all quotes shown for rejected lines (US4).

### FundingAgreement / SignedUpload
- `GeneratedByUserId` — resolved to a display name for US1.
- Signed-upload approve/reject actions — gated behind confirm for US2.

## New projection types (Application layer — `FundingPlatform.Application`)

### `DecisionSummaryLineDto` (record)
Read-only per-line summary; the single contract feeding all US4 surfaces.

| Field | Type | Source | Notes |
|---|---|---|---|
| LineCode | string? | Item.LineCode | reviewer-assigned |
| ProductName | string | Item.ProductName | |
| CategoryName | string | Item.Category.Name | |
| TechnicalSpecifications | string | Item.TechnicalSpecifications | the field missing on the applicant screen today |
| ReviewStatus | ItemReviewStatus | Item.ReviewStatus | Pending/Approved/Rejected/NeedsInfo |
| ReviewComment | string? | Item.ReviewComment | rejection/needs-info reason |
| ApprovedSupplierName | string? | Quotation[SelectedSupplierId].Supplier.Name | approved lines only |
| ApprovedAmount | MoneyView? | Quotation[SelectedSupplierId] | approved lines only |
| Quotations | IReadOnlyList\<DecisionSummaryQuotationView\> | Item.Quotations | shown for rejected lines (all options) |
| ApplicantDecision | string? | latest ApplicantResponse → ItemResponse | es-CR label, null until applicant responds |

### `DecisionSummaryQuotationView` (record)
| Field | Type | Source |
|---|---|---|
| SupplierName | string | Quotation.Supplier.Name |
| Amount | decimal | Quotation.Price |
| Currency | string | Quotation.Currency |
| ConvertedCrcAmount | decimal? | Quotation.ConvertedCrcAmount |
| CurrencyConversionNote | string? | computed (spec-015 `BuildConversionNote`), null for CRC |

### `IDecisionSummaryProjection`
```
IReadOnlyList<DecisionSummaryLineDto> Project(Application application);
```
Pure in-memory mapping over the already-loaded aggregate; computes the conversion note. Ordered by `LineCode` then `Id`. No new repository include for the lean shape.

## New view models / view inputs (Web layer)

- `FundingAgreementDetailsViewModel` gains: an applicant-detail block (company, representative, legal id + type, email, phone, CodigoPersonal, group, submission date — US3) and `IReadOnlyList<DecisionSummaryLineDto>` (US4).
- `ApplicantResponseViewModel` item rows gain `TechnicalSpecifications` (and consume `_DecisionSummary`).
- Review screen view model carries the applicant `CodigoPersonal` value + a bind target for the new POST (US5).

## State / behavior notes

- **US1**: no state change; display-only resolution.
- **US2**: no new state; inserts a client-side confirmation gate before the existing approve/reject POSTs.
- **US5**: `CodigoPersonal` set via `UserManager.UpdateAsync`; last-write-wins (no concurrency token — bounded, low-contention scalar; acceptable per spec edge-case note). Idempotent.
- **US4/US3/US6/US7/US8**: presentation-layer only.
