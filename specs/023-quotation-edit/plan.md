# Implementation Plan: In-place Quotation Field Edit

**Branch**: `023-quotation-edit` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/023-quotation-edit/spec.md`

## Summary

Expose a per-quotation in-place Edit affordance to the Application owner while the Application is `Draft` (the reviewer `SendBack` path returns a returned application to `Draft`; the codebase has no distinct `ReturnedForChanges` state — see spec FR-008). Editable fields: `Price`, `Currency`, `ValidUntil`, `SupplierBranchId` (same supplier only). Persistence routes through the existing domain primitives `Quotation.EditAmount` and `Quotation.ChangeCurrencyAsync` (spec 015), preserving the multi-currency snapshot contract. Implementation extracts a shared `_QuoteFields.cshtml` partial from the Supplier/Add form, adds `Edit` GET + POST endpoints on the existing `QuotationController`, surfaces quotation rows with action affordances under each Item in `Application/Edit.cshtml`, and silently invalidates the `ComparisonArtifact` cache (spec 020) on success. Per-US Playwright E2E coverage is mandatory (constitution III).

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity (reusing existing). No new managed dependencies (NFR-005).
**Storage**: SQL Server (Aspire-managed). No schema change (spec, Key Entities section).
**Testing**: NUnit + Playwright (E2E), `AspireFixture` for full-stack ephemeral runs.
**Target Platform**: Linux server, Aspire-orchestrated.
**Project Type**: Web (ASP.NET MVC, server-rendered Tabler.io UI).
**Performance Goals**: render p50 ≤ 200 ms; save round-trip p50 ≤ 500 ms (NFR-003).
**Constraints**: es-CR copy, keyboard-navigable form, idempotent POST (NFR-001/002/004).
**Scale/Scope**: one controller (+2 endpoints), one Razor view, one extracted partial, one service method, one view-model, three E2E test classes (one per US).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | **PASS** | Edit GET/POST live in Web; persistence routed through `Quotation.EditAmount` / `ChangeCurrencyAsync` on the entity; service layer orchestrates state-gate + branch invariant + cache invalidation; no Domain reference to Web/Infrastructure. |
| II. Rich Domain Model | **PASS** | Mutations flow through entity behavior methods; controller never assigns `Price`, `Currency`, `Snapshot`, `ConvertedCrcAmount` directly. Branch reassignment is added as a new `Quotation.ChangeBranch(SupplierBranch)` method that enforces the SupplierId invariant in the entity (rather than in the service). |
| III. End-to-End Testing | **PASS w/ plan obligation (R-2)** | Every US gets at least one Playwright E2E: `QuotationEditPriceTests`, `QuotationEditAfterReturnTests`, `QuotationEditCurrencyTests` — each driven from the landing page through Application/Edit to the Edit form (memory `feedback_e2e_must_drive_real_user_journey.md`). |
| IV. Schema-First DB | **PASS** | No dacpac change; no new columns, no new tables, no new indexes. |
| V. Specification-Driven Development | **PASS** | Workflow: brainstorm → spec → REVIEW-SPEC → plan (this) → tasks → implement. |
| VI. Simplicity & Progressive Complexity | **PASS w/ deviation (R-1)** | One deliberate deviation: no optimistic-concurrency token. See Complexity Tracking. |

### Quality-gate cross-checks

- **Validation aggregation (R-3)**: Server returns all field errors at once via `ModelState`. POST validates Price / Currency / ValidUntil / SupplierBranchId server-side and returns `View(vm)` with all `ModelState` errors set on the same round-trip. The es-CR copy for branch-belongs-to-supplier (*"Sucursal no válida para este proveedor."*), missing rate (existing `IUserFacingErrorTranslator` translation), and state-changed (*"El estado de la solicitud cambió, recarga la página."*) follow the same `ModelState` aggregation; no fail-fast.
- **Authorization**: `[Authorize(Roles = "Applicant")]` already on `QuotationController`. Ownership check reuses `VerifyOwnershipAsync(appId)` (existing helper). Non-owner Applicant requests → 403 (FR-007, SC-003).
- **AI cache invalidation**: After persistence commits, the service emits a synchronous `IComparisonCacheInvalidator.InvalidateForItemAsync(itemId, ct)`. The reviewer's next *Generar todo* picks up the cache miss (spec 020 contract).

## Project Structure

### Documentation (this feature)

```text
specs/023-quotation-edit/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── quotation-edit-endpoint.md   # GET/POST contract
├── checklists/
│   └── requirements.md  # (pre-existing)
├── REVIEW-SPEC.md       # (pre-existing)
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/Entities/
│   └── Quotation.cs                                 # +ChangeBranch(SupplierBranch)
│
├── FundingPlatform.Application/
│   ├── Abstractions/Comparison/
│   │   └── IComparisonCacheInvalidator.cs           # new — narrow seam for spec 020 cache hook
│   ├── Applications/Commands/
│   │   └── EditQuotationCommand.cs                  # new command DTO
│   └── Services/
│       └── ApplicationService.cs                    # +EditQuotationAsync(EditQuotationCommand)
│
├── FundingPlatform.Infrastructure/
│   └── Comparison/
│       └── ComparisonCacheInvalidator.cs            # impl over existing ComparisonArtifact repo (spec 020)
│
└── FundingPlatform.Web/
    ├── Controllers/
    │   └── QuotationController.cs                   # +Edit (GET), +Edit (POST)
    ├── ViewModels/
    │   └── EditQuotationViewModel.cs                # new
    └── Views/
        ├── Quotation/
        │   └── Edit.cshtml                          # new
        ├── Supplier/
        │   └── Add.cshtml                           # refactor: consume _QuoteFields partial
        ├── Application/
        │   └── Edit.cshtml                          # render quotation rows under each item w/ Editar | Reemplazar | Eliminar
        └── Shared/
            └── _QuoteFields.cshtml                  # new — shared Price/Currency/ValidUntil block

tests/
├── FundingPlatform.Tests.Unit/
│   └── Domain/Entities/Quotation_ChangeBranchTests.cs              # new
├── FundingPlatform.Tests.Integration/
│   └── ApplicationServiceEditQuotationTests.cs                     # new — covers branch invariant, state gate, cache hook, idempotency
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/
    │   └── Application/QuotationEditPage.cs                        # new
    └── Tests/
        └── Application/
            ├── QuotationEditPriceTests.cs                          # new — US1 golden + zero-price error
            ├── QuotationEditAfterReturnTests.cs                    # new — US2 branch swap + same-supplier rejection
            └── QuotationEditCurrencyTests.cs                       # new — US3 CRC→USD snapshot + cache hash flip
```

**Structure Decision**: Default project layout (Clean Architecture, four-layer .NET). No new project. The Application/Edit surface already exposes `ItemViewModel.Quotations: List<QuotationSummaryViewModel>` — the data is in place; only the view rendering needs the per-row action affordances.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| **No optimistic-concurrency token on Quotation Edit POST** (R-1, deviation from constitution quality gate *"Optimistic concurrency MUST be used for entities with concurrent edit risk"*) | The Application owner is the single actor with Edit access (FR-007). The only concurrent-edit scenario is two browser tabs of the same user (Edge Cases bullet 3). Project precedent on `Item/Edit` and the autosave flow is also last-write-wins. Adding a `RowVersion` column would require a dacpac change, a service-side conflict-handling branch, and an extra E2E case — disproportionate to the actor model. | A `RowVersion` token guards against two-actor concurrency, which is precisely what FR-007 already prevents. The two-tabs-same-user case is acceptably resolved by last-write-wins; the second tab's stale read carries no integrity risk because both writes pass through the same domain invariants (state gate, branch-belongs-to-supplier, legacy-flag guard). |

## Phase 0: Outline & Research

See `research.md` for the resolved technical decisions:

- Edit affordance placement (Application/Edit nested rows vs. Item/Edit) — chosen: Application/Edit.
- Shared partial extraction shape (`_QuoteFields.cshtml`) — content + binding contract.
- Branch picker data source — eager-loaded from the quotation's current Supplier.Branches.
- `ComparisonArtifact` invalidation seam — new `IComparisonCacheInvalidator` interface (Application) with EF-based implementation (Infrastructure), kept narrow to avoid coupling Application to spec 020 read models.
- Validation aggregation pattern — `ModelState` with field-level errors set on the same round-trip.

## Phase 1: Design & Contracts

See `data-model.md` for entity touch-points and the new `EditQuotationCommand` DTO. See `contracts/quotation-edit-endpoint.md` for the GET/POST contract (route, accepted inputs, status codes, error shapes). See `quickstart.md` for the developer-onboarding walkthrough.

Agent context (`CLAUDE.md`) updated to point at this plan for spec 023.
