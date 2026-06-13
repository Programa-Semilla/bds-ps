# Implementation Plan: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Branch**: `035-line-item-category-templates` | **Date**: 2026-06-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/035-line-item-category-templates/spec.md`

## Summary

Reshape the applicant submission flow around the line item: (1) each submission **category** owns an admin-configured field set that renders dynamically when an applicant picks it (replacing the free-text `Item.TechnicalSpecifications`); (2) **impact** relocates from a single application-wide choice down to a per-line-item choice, selectable from any active impact template (the Plantilla no longer gates it); (3) a multi-product vendor **quotation** is captured as one line item per product, with the vendor + uploaded document reused across sibling items (each keeping its own price). The obsolete application-level impact wiring and the Plantilla impact-template gating are removed with no dead code (verified by search). All application-render surfaces (applicant Details/Review, reviewer detail, funding-agreement PDF, AI comparison context) show per-item category values + impact. Greenfield flow → no data migration.

Technical approach: re-key the proven `ImpactTemplate → parameter → EAV-value` pattern into `Category → CategoryField → CategoryFieldValue` (value keyed by `Item`); re-key `ImpactParameterValues` from `ApplicationId` to `ItemId` and move impact members from `Application` to `Item`; implement quotation reuse purely in the application layer (no schema change — the `Document` model already supports sharing) with reference-counted blob retention; surgically strip the Plantilla impact-template machinery while keeping min-quotations + required-field flags. Full research and decisions in [research.md](./research.md); model in [data-model.md](./data-model.md); interface deltas in [contracts/interfaces.md](./contracts/interfaces.md).

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, Syncfusion HtmlToPdfConverter, Anthropic.SDK (AI comparison, existing), Tabler.io (vendored). **No new managed dependencies.**
**Storage**: SQL Server (dacpac schema source-of-truth; EF for data access only). Object storage via `IObjectStorage` (Azurite dev / Azure Blob prod / LocalFilesystem fallback).
**Testing**: NUnit + Playwright for .NET (E2E, Page Object Model); xUnit/NUnit unit + integration (integration hits a real DB, no mocks).
**Target Platform**: Linux server (Aspire-orchestrated); browser UI (server-rendered Razor, es-CR).
**Project Type**: Web application (ASP.NET MVC, Clean Architecture: Domain / Application / Infrastructure / Web + Database dacpac).
**Performance Goals**: Interactive web; dynamic category/impact field forms render client-side from a small JSON descriptor fetch; no batch/throughput target.
**Constraints**: es-CR default culture, no English-only copy; no CDN (assets vendored); schema-first (no EF migrations); all validation errors shown at once; greenfield flow (no migration). Delivery bar = filtered E2E green.
**Scale/Scope**: ~2 new tables, 1 re-keyed table, 1 altered table, 1 dropped table + 1 dropped column; net-new admin Category CRUD; restructured applicant item form; quotation-reuse path; 4 converted + 2 augmented render surfaces; Plantilla gating teardown.

## Constitution Check

*GATE: must pass before Phase 0 and re-checked after Phase 1.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | PASS | New entities/VOs in Domain; commands/services/DTOs in Application; EF configs + services + dacpac in Infrastructure/Database; controllers/views/VMs in Web. Dependencies point inward; no Domain→outer references. |
| II. Rich Domain Model | PASS | New behavior on entities: `Item.SetImpact`, `Item.SetCategoryFieldValues`, `Item.ChangeCategory` (clears values), `Category.AddField/ClearFields/Update/Activate`, `Application.Validate` per-item gates + `CountQuotationsReferencingDocument`. Cross-aggregate blob I/O stays in the service (it needs storage); which-required-cell-is-blank validation stays service-layer **consistent with the existing impact pattern** (documented continuity, not a new violation). |
| III. E2E (NON-NEGOTIABLE) | PASS | Each user story gets Playwright E2E (POM): category-field admin, per-item category+impact, quotation reuse, cross-surface display. Unit + integration (real DB) complement. Delivery bar = filtered E2E. |
| IV. Schema-First (dacpac) | PASS | All schema via `.sql` edits; no EF migrations/EnsureCreated. Greenfield → no backfill scripts; seed scripts updated. |
| V. Specification-Driven | PASS | spec → plan → tasks → implement; this plan + research + data-model + contracts produced before code. |
| VI. Simplicity / Progressive Complexity | PASS | Reuses existing patterns (impact-template admin UI, EAV, `ParameterDataType`, `_QuoteFields`/`IQuoteFieldsModel`, dynamic-field JSON endpoint); category owns fields 1:1 (no standalone catalog); no new field types; no new deps; quotation reuse needs no schema change. Deferred items (conditional fields, file/dropdown types, cross-app reuse, `ValidationRules`) explicitly out of scope. |

**Result: PASS (initial and post-design).** No violations → Complexity Tracking empty.

One **flagged refinement** (not a violation): FR-009 lists the AI quote-comparison context as a surface showing category values + per-item impact. Research D6 recommends including category values (scrubbed) but **excluding impact** there (impact is irrelevant to comparing supplier quotes, and free-text must pass PII redaction). Pending user confirmation; does not block the rest of the plan.

## Project Structure

### Documentation (this feature)

```text
specs/035-line-item-category-templates/
├── plan.md              # This file
├── spec.md              # Feature spec
├── research.md          # Phase 0 — 12 decisions (D1–D12)
├── data-model.md        # Phase 1 — entities, tables, FKs, validation
├── contracts/
│   └── interfaces.md    # Phase 1 — command/service/route/endpoint deltas
├── quickstart.md        # Phase 1 — manual walkthrough + gates
├── checklists/requirements.md
├── REVIEW-SPEC.md
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/
│   │   ├── Category.cs              # + Fields, mutators (AddField/ClearFields/Update/Activate)
│   │   ├── CategoryField.cs         # NEW (mirrors ImpactTemplateParameter)
│   │   ├── CategoryFieldValue.cs    # NEW (EAV, keyed by Item)
│   │   ├── Item.cs                  # + ImpactTemplateId/nav, ImpactParameterValues,
│   │   │                            #   CategoryFieldValues, SetImpact, SetCategoryFieldValues,
│   │   │                            #   ChangeCategory(clears values); − TechnicalSpecifications
│   │   ├── Application.cs           # − impact members/SetImpact; Validate per-item gates;
│   │   │                            #   + CountQuotationsReferencingDocument
│   │   ├── Plantilla.cs             # − impact-template members + AssignTo guard/snapshot
│   │   ├── ProcessPlantilla.cs      # − ImpactTemplateIdsCsv/ImpactTemplateIds()
│   │   └── Impact.cs (stale entity) # DELETE if present (dead code)
│   ├── Enums/ParameterDataType.cs   # reused as-is
│   └── Interfaces/ICategoryRepository.cs  # + Add/Update/Save/GetByIdWithFields/GetAll
├── FundingPlatform.Application/
│   ├── Admin/Commands/             # NEW Create/UpdateCategoryCommand + CategoryFieldDefinition
│   ├── Applications/Commands/      # AddItem/UpdateItemCommand (− TechSpecs, + category/impact);
│   │                               #   − SetApplicationImpactCommand
│   ├── DTOs/                       # ItemDto.Impact populated + CategoryFields; − ApplicationDto.Impact;
│   │                               #   ReviewItemDto per-item; FundingAgreementItemRowDto + fields;
│   │                               #   CategoryDetailDto/ReusableQuotationDto NEW
│   ├── Services/ApplicationService # AddItem/UpdateItem rewire; ReuseQuotationAsync;
│   │                               #   ref-counted retention; − SetApplicationImpactAsync
│   ├── Services/AdminService       # + Category CRUD
│   ├── Services/ReviewService      # MapToReviewDto → real per-item impact + category fields
│   ├── Plantillas/IPlantillaService# − ImpactTemplateIds/Count
│   └── AiComparison/SupplierAssembler  # + product + category fields (scrubbed); impact excluded (D6)
├── FundingPlatform.Infrastructure/
│   ├── Persistence/Configurations/ # CategoryConfiguration(+Fields), CategoryFieldConfiguration NEW,
│   │                               #   CategoryFieldValueConfiguration NEW, ItemConfiguration (+ImpactTemplateId,
│   │                               #   −TechSpecs), ImpactParameterValueConfiguration (re-key Item),
│   │                               #   ApplicationConfiguration (−impact), delete PlantillaImpactTemplateConfiguration,
│   │                               #   ProcessPlantillaConfiguration (−CSV)
│   ├── Persistence/Repositories/   # CategoryRepository (+writes), ApplicationRepository includes
│   ├── Persistence/AppDbContext.cs # + DbSet<CategoryField>, <CategoryFieldValue>
│   ├── Services/PlantillaService.cs# gut attach/reconcile
│   ├── Services/SubmitApplicationHandler.cs # submit gate now per-item (via Application.Validate)
│   ├── Services/GetApplicationReviewProjection.cs # per-item includes + projection
│   ├── AiComparison/...            # redaction of new free-text category values
│   └── DocumentGeneration/...      # PDF per-line category+impact block
├── FundingPlatform.Database/Tables/
│   ├── dbo.CategoryFields.sql            # NEW
│   ├── dbo.CategoryFieldValues.sql       # NEW
│   ├── dbo.ImpactParameterValues.sql     # re-key ApplicationId→ItemId
│   ├── dbo.Items.sql                     # +ImpactTemplateId, −TechnicalSpecifications
│   ├── dbo.ProcessPlantillas.sql         # −ImpactTemplateIdsCsv
│   ├── dbo.PlantillaImpactTemplates.sql  # DELETE
│   └── (post-deploy seed scripts updated for new shape)
└── FundingPlatform.Web/
    ├── Controllers/AdminController.cs            # + Categories/CreateCategory/EditCategory
    ├── Controllers/ApplicationController.cs      # − Impact actions, − inline AddItem/RemoveItem
    ├── Controllers/ItemController.cs             # Add/Edit host dynamic category fields + impact
    ├── Controllers/SupplierController.cs         # quotation reuse mode
    ├── Controllers/Admin/AdminPlantillasController.cs  # − impact-template options
    ├── ViewModels/                              # Category admin VMs NEW; AddItem/EditItem VMs reshaped;
    │                                            #   AddSupplier VM + reuse; − ImpactViewModel;
    │                                            #   Plantilla VMs − impact options
    ├── Views/Admin/Categories|CreateCategory|EditCategory.cshtml  # NEW (mirror impact-template views)
    ├── Views/Item/Add|Edit.cshtml               # dynamic category fields + impact picker
    ├── Views/Application/Details|Review|Edit.cshtml  # impact → per-item + category fields
    ├── Views/Review/Review.cshtml               # per-item impact/category from real data
    ├── Views/Supplier/Add.cshtml                # reuse picker
    ├── Views/Admin/Plantillas/Create|Edit|Index.cshtml  # − impact-template block/column
    ├── Views/FundingAgreement/Partials/...      # per-line category+impact
    └── wwwroot/js/                              # shared dynamic-field renderer (extracted), reuse-picker toggle

tests/
├── FundingPlatform.Tests.Unit          # Item/Application/Category domain behavior
├── FundingPlatform.Tests.Integration   # Category CRUD, ref-counted retention, per-item impact (real DB)
└── FundingPlatform.Tests.E2E           # CategoryField*, PerItemImpact*, QuotationReuse*, LineItemDisplay*
```

**Structure Decision**: Existing Clean-Architecture layout (per CLAUDE.md). No new projects. Changes distribute across the four layers + the dacpac as shown, following established per-spec conventions.

## Complexity Tracking

No constitution violations — no entries.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |

## Phase notes / sequencing hint (for `/speckit-tasks`)

Natural build order honoring independent-story testability:
1. **Schema + domain foundation** (dacpac tables, re-key, entity members) — unblocks everything.
2. **US1** admin category fields (CRUD) — independently demoable.
3. **US2** per-item category fields + impact in the item form + submit gates + Plantilla teardown.
4. **US3** quotation reuse + reference-counted retention.
5. **US4** cross-surface display (Details/Review/reviewer/PDF/AI) + SC-003 teardown-verification check.

Risk hot-spots flagged for tasks: the `Plantilla.AssignTo` guard/snapshot removal (breaks assignment if missed), the two blob-delete sites (reference count), the `ReviewService.MapToReviewDto` duplication fix, and the AI redaction of new free-text (D6).
