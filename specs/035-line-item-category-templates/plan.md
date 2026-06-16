# Implementation Plan: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Branch**: `035-line-item-category-templates` | **Date**: 2026-06-16 (re-planned) | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/035-line-item-category-templates/spec.md`

> **Re-plan note (2026-06-16).** This plan supersedes the original 035 plan after the impact-model evolution (see the spec's **Evolution Log**). The **category-field** design (US1/US3) and **quotation-reuse** design (US4) are unchanged and already implemented. Only the **impact dimension** is re-designed: impact data collection moves to the **application level with one or more impacts**; line items carry a **multi-impact attribution** + a single **short justification** instead of their own impact field values. Research decisions D1, D4, D5 stand; D2, D8, D9, D11 are superseded by D13–D16 (see research.md).

## Summary

Reshape applicant submission around three pillars sharing the "add a line item" flow:

1. **Category-driven fields** (US1/US3, *built, unchanged*) — each `Category` owns an admin-configured ordered field set (`CategoryField` → item-keyed `CategoryFieldValue`, EAV mirroring impact templates), replacing the free-text `Item.TechnicalSpecifications`.
2. **Application-level impacts + per-item attribution** (US2/US3, *re-designed*) — an application declares **one or more** impacts (`ApplicationImpact` = chosen `ImpactTemplate` + its `ImpactParameterValue`s, re-keyed from the old per-item shape to `ApplicationImpactId`). Each line item is **attributed** to one or more of those declared impacts (`ItemImpact` join) and carries a single required **`Item.ImpactJustification`** (≤300 chars). The Plantilla impact-template gating remains removed (min-quotations + required-field flags preserved).
3. **Quotation reuse** (US4, *built, unchanged*) — reuse a sibling line item's supplier + uploaded `Document` with a per-item price; reference-counted blob retention; no schema change.

All render surfaces show, per item, category values + attributed impact name(s) + justification, and at the application level the declared impacts + their values. Greenfield (branch not merged → destructive schema edits, no migration); no new managed dependencies.

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: ASP.NET Identity, .NET Aspire, Syncfusion HtmlToPdfConverter, Tabler.io (vendored), Anthropic.SDK (existing AI path) — **no new managed deps**
**Storage**: SQL Server (Aspire-managed); schema source-of-truth = `FundingPlatform.Database` dacpac (Constitution IV). Blob via `IObjectStorage` (Azurite/Azure Blob/local)
**Testing**: xUnit (Unit + Integration against real SQL), Playwright + `AspireFixture` (E2E, Page Object Model)
**Target Platform**: Linux container (Aspire-orchestrated)
**Project Type**: Web application (Clean Architecture: Domain / Application / Infrastructure / Web)
**Performance Goals**: interactive web; no new hot paths (dynamic-field endpoints are small JSON reads)
**Constraints**: es-CR default culture (Constitution + spec FR-015); all assets local (no CDN)
**Scale/Scope**: impact-dimension re-work of a mostly-built feature; ~5 schema objects added/changed, ~2 removed; reuses existing impact-template + EAV machinery

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture** — PASS. New behavior placed per layer: domain invariants on `Application`/`Item` aggregates; orchestration in Application/Infrastructure services; DTOs in Application; controllers/views/VMs in Web. No inward-pointing violations. es-CR reason strings: per the spec-034 precedent, service-produced reasons may live in Application; Web owns view copy.
- **II. Rich Domain Model** — PASS. Impact declaration (`Application.AddImpact`/`RemoveImpact`), attribution (`Item.AttributeImpacts`), justification (`Item.SetImpactJustification`), and the extended `Application.Validate(minQuotations)` (app-has-≥1-impact, per-impact required values, per-item ≥1 attribution + non-empty justification + required category fields, attribution-targets-are-declared) all live on the entities. Cross-aggregate I/O (blob delete) stays in services.
- **III. E2E (NON-NEGOTIABLE)** — PASS. Each user story carries Playwright E2E (POM) + unit + real-DB integration. Delivery bar = filtered E2E for touched classes.
- **IV. Schema-First (dacpac)** — PASS. All schema edits in `FundingPlatform.Database` `.sql` files; no EF migrations / `EnsureCreated`. Greenfield → no backfill scripts.
- **VI. Simplicity / YAGNI** — PASS. Re-keys the proven impact-template EAV pattern rather than inventing parallel machinery; no speculative guards (e.g., last-active-template deactivation).

**No violations → Complexity Tracking empty.**

## Project Structure

### Documentation (this feature)

```text
specs/035-line-item-category-templates/
├── plan.md              # This file (re-planned 2026-06-16)
├── research.md          # Phase 0 — D1–D12 + evolution D13–D16
├── data-model.md        # Phase 1 — evolved schema
├── quickstart.md        # Phase 1 — manual walkthrough (updated for app-level impacts)
├── contracts/
│   └── interfaces.md    # Phase 1 — command/service/route/endpoint contracts
└── tasks.md             # Phase 2 (/speckit-tasks) — regenerated for the evolution delta
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/         Application.cs, Item.cs, ApplicationImpact.cs (NEW), ItemImpact.cs (NEW),
│   │                     Category.cs, CategoryField.cs, CategoryFieldValue.cs, ImpactParameterValue.cs (re-keyed)
│   └── Interfaces/        ICategoryRepository, IImpactTemplateRepository (existing)
├── FundingPlatform.Application/
│   ├── Applications/Commands/  AddItemCommand, UpdateItemCommand (reshaped), AddApplicationImpactCommand (NEW), RemoveApplicationImpactCommand (NEW)
│   ├── Admin/Commands/   CreateCategoryCommand, UpdateCategoryCommand (built, unchanged)
│   ├── DTOs/             ItemDto, ApplicationDto (impacts back), ReviewApplicationDto, FundingAgreementItemRowDto
│   └── Services/         ApplicationService, ReviewService, PlantillaService, AdminService
├── FundingPlatform.Infrastructure/
│   ├── Persistence/      AppDbContext, Configurations/*, Repositories/*
│   └── AiComparison/     SupplierAssembler
├── FundingPlatform.Database/   Tables/*.sql (dacpac — source of truth) + post-deploy seeds
└── FundingPlatform.Web/
    ├── Controllers/      ApplicationController (impacts step), ItemController (attribution+justification), SupplierController, AdminController
    ├── Views/            Application/*, Item/*, Supplier/*, Admin/Categories*, FundingAgreement/Partials/*, Emails/*
    └── wwwroot/js/       dynamic-fields renderer, submit-gate.js
tests/
├── FundingPlatform.Tests.Unit/
├── FundingPlatform.Tests.Integration/
└── FundingPlatform.Tests.E2E/    + PageObjects/
```

**Structure Decision**: Existing 4-layer Clean Architecture web app; no structural change. The evolution touches the impact slice across all four layers plus tests.

## Phase 0 — Research

See `research.md`. **Unchanged & still authoritative:** D1 (category-field EAV), D3 (any active template, no Plantilla gate — now applied at the application level), D4 (Plantilla teardown), D5 (quotation reuse + ref-counted retention), D7 (no-active-templates empty-state — now on the impacts step), D10 (dacpac/greenfield), D12 (testing strategy).

**Superseded by the evolution, replaced by D13–D16:**
- **D13** (replaces D2) — Impact at the **application level, multiple impacts**: new `ApplicationImpact` aggregate-child; `ImpactParameterValues` re-keyed to `ApplicationImpactId` (not `ItemId`). `Item.ImpactTemplateId` and per-item impact values are removed.
- **D14** (new) — **Per-item attribution + justification**: `ItemImpact` join (Item ↔ ApplicationImpact) with `ApplicationImpactId` FK set **NO ACTION** to avoid SQL Server multiple-cascade-path error; `Item.ImpactJustification NVARCHAR(300)`. Removing a declared impact removes its attributions in the domain (SC-007).
- **D15** (replaces D8) — Flow: an **application-level impacts step** (multi-impact manager) + an item form whose impact part is a **multi-select attribution** (options = declared impacts) + a short justification textarea. No impact template/values in the item form.
- **D16** (replaces D9/D11 impact parts) — Display: application-level impacts card (declared impacts + values) **plus** per-item attributed-impact names + justification on every surface; validation invariants relocated to `Application.Validate` + service for required-value detail.

## Phase 1 — Design & Contracts

- **Data model**: `data-model.md` (evolved). New `dbo.ApplicationImpacts`, `dbo.ItemImpacts`; `dbo.ImpactParameterValues` re-keyed to `ApplicationImpactId`; `dbo.Items` drops `ImpactTemplateId`, adds `ImpactJustification`. Category + quotation schema unchanged.
- **Contracts**: `contracts/interfaces.md` (evolved) — new `AddApplicationImpactCommand`/`RemoveApplicationImpactCommand` + service methods; reshaped `AddItemCommand`/`UpdateItemCommand` (drop per-item impact template/values; add `ApplicationImpactIds` + `ImpactJustification`); routes for the impacts step; DTO/projection changes for display.
- **Agent context**: CLAUDE.md "Active plan" / "Recent Changes" updated to reflect the evolved 035 after tasks regenerate (T065, post-merge).

**Post-design Constitution re-check**: PASS — design stays within the existing patterns; the only notable schema nuance (NO-ACTION on `ItemImpacts.ApplicationImpactId` + domain-driven attribution cleanup) is documented and test-covered (SC-007).

## Complexity Tracking

> No constitution violations — section intentionally empty.
