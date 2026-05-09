# Implementation Plan: PDF Template Lift — Branded Funding Agreement

**Branch**: `018-pdf-template-lift` | **Date**: 2026-05-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/018-pdf-template-lift/spec.md`

## Summary

Replace the current generic Funding Agreement PDF with a fully-branded, six-section document that pixel-matches the canonical "Informe de evaluación de solicitudes de desembolso" seed (Programa Semilla branding, Fraunces serif headings, Inter body, brand teal palette, locally-vendored fonts). Add two domain inputs that feed the new layout: `Application.CompanyName` (captured on the applicant's Create form) and `Item.LineCode` (captured on the reviewer's per-item review form, unique-within-Application). Remove dead `FundingAgreement:Funder:*` configuration, the `FunderOptions` binding, the funder DTO/view-model, and the placeholder banner that the old generic template required. The HTML→PDF engine (Syncfusion Blink) is reused unchanged; only the HTML/CSS input and page margins change.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, Syncfusion HtmlToPdfConverter (Blink), Tabler.io (vendored), Fraunces / Inter / JetBrains Mono (vendored)
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`); Azurite/AzureBlob for the rendered PDF blob (spec 014)
**Testing**: xUnit (Unit + Integration), Playwright (E2E) on AspireFixture
**Target Platform**: Linux server (Aspire-orchestrated); ASP.NET MVC server-side rendering
**Project Type**: Web (Aspire-orchestrated stack — AppHost → Web → SQL Server)
**Performance Goals**: PDF generation completes within three seconds at the 95th percentile for an Application with up to 30 items and 10 distinct suppliers (SC-009)
**Constraints**: A4 portrait page; 20mm top/bottom + 18mm left/right margins; ±5pt visual tolerance against seed; no CDN dependencies (all assets vendored)
**Scale/Scope**: Single Funding Agreement document type per Application; up to ~30 items per Application is the spec'd ceiling

## Constitution Check

Constitution v1.0.0 evaluated against this plan:

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Clean Architecture** | ✓ pass | Domain gains `Application.CompanyName`, `Application.SetCompanyName(string)`, `Item.LineCode`, `Application.AssignLineCodeToItem(int, string)`. Application layer projection (`FundingAgreementService`) is updated. Web/Infrastructure render the new layout. Dependency direction unchanged. |
| **II. Rich Domain Model** | ✓ pass | All new invariants live on entities. `Application.SetCompanyName(string)` enforces required + ≤200 + trim. `Application.AssignLineCodeToItem(int, string)` enforces required + ≤16 + trim + per-Application uniqueness. Controllers/services are thin pass-throughs. |
| **III. E2E NON-NEGOTIABLE** | ✓ pass | SC-010 (US1: funder operator → PDF download → text-layer assertions), SC-011 (US2: reviewer → required + duplicate line-code + advance), SC-012 (US3: applicant → required company name + cover-page rendering) cover golden + key error paths per US. |
| **IV. Schema-First (dacpac)** | ✓ pass | Schema changes happen in `dbo.Applications.sql` (add `CompanyName NVARCHAR(200) NOT NULL`) and `dbo.Items.sql` (add `LineCode NVARCHAR(16) NULL` + filtered unique index `UX_Items_Application_LineCode` `WHERE LineCode IS NOT NULL`). No EF migrations. No production data → no shim per spec assumption. |
| **V. Spec-Driven** | ✓ pass | Spec → plan → tasks workflow honored; this plan is being written before tasks/implementation. |
| **VI. Simplicity / YAGNI** | ✓ pass | Reuse Syncfusion Blink unchanged; reuse `EsCrCultureFactory`; reuse spec-015 currency-conversion rendering. Footer is a single composite image (per-logo split deferred). Sworn declaration text hardcoded in Razor (no DB-backed legal copy table). No version flag / parallel renderer (FR-017). |

**Violations:** None. **Complexity Tracking** below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/018-pdf-template-lift/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── README.md        # Phase 1 output (interface contracts the feature exposes)
├── spec.md              # Existing
├── REVIEW-SPEC.md       # Existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   └── Entities/
│       ├── Application.cs                                    # +CompanyName, +SetCompanyName, +AssignLineCodeToItem
│       └── Item.cs                                           # +LineCode (private setter via aggregate root only)
├── FundingPlatform.Application/
│   ├── Applications/
│   │   └── Commands/
│   │       ├── CreateApplicationCommand.cs                   # +CompanyName arg
│   │       └── ReviewItemCommand.cs                          # +LineCode arg
│   ├── DTOs/
│   │   ├── FundingAgreementDto.cs                            # add cover/committee/supplier shape; drop funder block
│   │   └── FundingAgreementItemRowDto.cs                     # +LineCode
│   ├── Options/
│   │   └── FunderOptions.cs                                  # DELETE
│   └── Services/
│       ├── FundingAgreementService.cs                        # rewrite projection: cover + intro + tables + declaration model
│       └── ReviewService.cs                                  # ReviewItemAsync threads LineCode through to AssignLineCodeToItem
├── FundingPlatform.Infrastructure/
│   ├── Persistence/Configurations/
│   │   ├── ApplicationConfiguration.cs                       # +CompanyName max length 200, required
│   │   └── ItemConfiguration.cs                              # +LineCode max length 16, required, composite index
│   └── DocumentGeneration/
│       └── SyncfusionFundingAgreementPdfRenderer.cs          # margins → 20mm/18mm; header/footer page-level wiring
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   ├── ApplicationController.cs                          # accept CompanyName on Create POST
│   │   └── ReviewController.cs                               # ReviewItem action: bind LineCode
│   ├── ViewModels/
│   │   ├── CreateApplicationViewModel.cs                     # +CompanyName
│   │   ├── ReviewItemViewModel.cs                            # +LineCode (existing input value)
│   │   ├── FundingAgreementDocumentViewModel.cs              # rewrite (drop funder/email/phone/legalId/agreementRef)
│   │   └── FundingAgreementPanelViewModel.cs / DetailsVM     # incidental cleanup if AgreementReference goes from cover
│   └── Views/
│       ├── Application/
│       │   └── Create.cshtml                                 # +CompanyName <input>
│       ├── Review/
│       │   └── Review.cshtml                                 # +LineCode <input> on per-item review form
│       └── FundingAgreement/
│           ├── Document.cshtml                               # rewrite: cover → declaration sequence
│           ├── _FundingAgreementLayout.cshtml                # rewrite: A4 page CSS, header/footer @page rules
│           └── Partials/
│               ├── _BrandHeader.cshtml                       # NEW
│               ├── _BrandFooter.cshtml                       # NEW
│               ├── _CoverPage.cshtml                         # NEW (applicant block + commission)
│               ├── _IntroPage.cshtml                         # NEW (3 fixed paragraphs)
│               ├── _RequestedResourcesPage.cshtml            # NEW (table)
│               ├── _CommitteeResultsPage.cshtml              # NEW (summary + bullets + 2 tables)
│               ├── _SupplierVerificationPage.cshtml          # NEW (Hacienda/CCSS/SICOP)
│               ├── _SwornDeclarationPage.cshtml              # NEW (PRIMERO–QUINTO + signature box)
│               ├── _FundingAgreementHeader.cshtml            # DELETE
│               ├── _FundingAgreementItemsTable.cshtml        # DELETE
│               ├── _FundingAgreementSignatureBlocks.cshtml   # DELETE
│               └── _FundingAgreementTermsAndConditions.cshtml # DELETE
│   └── wwwroot/
│       └── lib/brand/pdf/
│           ├── header-seedling.png                           # EXISTS (61KB)
│           ├── footer-partners-strip.png                     # EXISTS (58KB)
│           └── signature-box.png                             # EXISTS (1.8KB) — used or replaced with CSS rounded rectangle (research decision R-002)
├── FundingPlatform.AppHost/
│   └── AppHost.cs                                            # remove funderLegalName / TaxId / Address / ContactEmail / ContactPhone variables + their `WithEnvironment` calls
└── FundingPlatform.Database/
    ├── Tables/
    │   ├── dbo.Applications.sql                              # +[CompanyName] NVARCHAR(200) NOT NULL
    │   └── dbo.Items.sql                                     # +[LineCode] NVARCHAR(16) NULL + UX_Items_Application_LineCode (filtered UNIQUE WHERE LineCode IS NOT NULL)

tests/
├── FundingPlatform.Tests.Unit/
│   ├── Domain/
│   │   ├── ApplicationCompanyNameTests.cs                    # NEW (FR-015/016, edge cases)
│   │   └── ItemLineCodeTests.cs                              # NEW (FR-012/013/014, duplicate guard)
├── FundingPlatform.Tests.Integration/
│   ├── Applications/
│   │   └── CompanyNameRequiredTests.cs                       # NEW (DB-level)
│   ├── Reviews/
│   │   └── LineCodeRequiredAndUniqueTests.cs                 # NEW (DB-level)
│   └── FundingAgreement/
│       └── BrandedDocumentProjectionTests.cs                 # NEW (committee distinct-actors, omits when zero rejected, etc.)
└── FundingPlatform.Tests.E2E/
    ├── PdfTemplate/
    │   └── FundingAgreementPdfDownloadTests.cs               # NEW (SC-010 — text-layer headings)
    ├── Reviews/
    │   └── LineCodeReviewFlowTests.cs                        # NEW (SC-011 — required + duplicate)
    └── Applications/
        └── CompanyNameApplicationFlowTests.cs                # NEW (SC-012 — required + cover-page rendering)

CLAUDE.md                                                      # remove FundingAgreement:Funder:* rows from configuration-knobs table (FR-019)
```

**Structure Decision**: Single solution, four-layer Clean Architecture (Domain / Application / Infrastructure / Web) plus Aspire orchestration and a dacpac for schema. Renderer change is contained to `FundingPlatform.Web/Views/FundingAgreement/` Razor partials and the projection in `FundingAgreementService`. Schema change goes through the dacpac per Constitution IV. The two new entity invariants live in `Domain/Entities/{Application,Item}.cs` per Constitution II. E2E tests live in `tests/FundingPlatform.Tests.E2E/` and run against the AspireFixture-orchestrated stack per Constitution III.

## Complexity Tracking

> No Constitution Check violations to justify. This section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
