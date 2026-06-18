# Implementation Plan: Supplier Recommendation Algorithm Rewrite

**Branch**: `039-supplier-recommendation` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/039-supplier-recommendation/spec.md`

## Summary

Replace the price-dominant /4 `SupplierScore` with the client's seven-criterion (§14), deterministic, **explainable** recommendation: each criterion gives every eligible provider a base 1 point and the winner(s) 2; the highest total (7–14) is recommended. Add two required quote fields — delivery lead time and warranty (value + days/months, normalized to days at 30 days/month) — modelled as a `TimeDuration` value object on `Quotation`. A provider with CCSS `sin inscripción` is excluded from scoring and **cannot be used to approve an item** (the per-item reviewer progression gate). A top-score tie yields no auto-recommendation ("selección manual requerida"). The add-item form is reordered (product name first). Scores are **computed live** (no new table); the spec-020 AI comparison is untouched. No new managed dependencies.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, .NET Aspire, ASP.NET Identity (all existing; **no new managed deps**)
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`); EF Core for data access only (no migrations)
**Testing**: xUnit/NUnit unit + integration (real DB), Playwright E2E via `AspireFixture`
**Target Platform**: Linux container (Aspire-orchestrated web app)
**Project Type**: Server-rendered ASP.NET MVC web application (Clean Architecture: Domain / Application / Infrastructure / Web)
**Performance Goals**: Recommendation computed in-memory per item on each reviewer-page render; negligible (pure arithmetic over a handful of quotations) — no perf budget change
**Constraints**: es-CR copy; dacpac schema-first; live computation (no persisted score, no invalidation); migration-safe NOT NULL adds (DEFAULT placeholder) on the persistent dev volume
**Scale/Scope**: One entity extended (`Quotation`), one enum + one value object added, one domain value object rewritten, one domain guard added, two VMs + one shared partial + one item view + the reviewer view touched, dacpac + seed update

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | PASS — scoring + `TimeDuration` + `DurationUnit` in Domain; DTO mapping in Application; EF config in Infrastructure; views/VMs in Web. Dependencies point inward; no Web/Infra leak into Domain. |
| **II. Rich Domain Model** | PASS — scoring stays a Domain value object (`SupplierScore`); the eligibility/progression gate is a domain guard on `Item.Approve` (un-bypassable), with the Application layer owning only es-CR translation. `Quotation` enforces `Value > 0` / valid unit invariants on its own fields. |
| **III. E2E Testing (NON-NEGOTIABLE)** | PASS (planned) — each user story gets Playwright coverage: explainable recommendation (incl. non-lowest-price winner), required quote fields, CCSS block + advance gate, tie → manual selection, item-field order. Filtered E2E is the delivery bar. |
| **IV. Schema-First DB** | PASS — new columns added to `dbo.Quotations.sql`; no EF migrations; seed via post-deploy scripts. Migration-safe NOT NULL + DEFAULT placeholder (research D8). |
| **V. Spec-Driven** | PASS — spec.md (5 prioritized stories, 26 FRs, success criteria) → this plan → tasks next. |
| **VI. Simplicity/YAGNI** | PASS — live computation (no score table, no invalidation); reuse `_QuoteFields.cshtml`, the spec-015 CRC amount, slice-A statuses; one `TimeDuration` VO reused for both fields; AI comparison reused, not rebuilt. No speculative abstraction. |

**Result: PASS** (no violations; Complexity Tracking empty).

## Project Structure

### Documentation (this feature)

```text
specs/039-supplier-recommendation/
├── spec.md              # approved
├── plan.md              # this file
├── research.md          # Phase 0 — 12 decisions
├── data-model.md        # Phase 1 — entity/enum/VO + computed result shape
├── contracts/
│   └── interfaces.md     # Phase 1 — scoring fn, domain gate, form + surface contracts
├── quickstart.md        # Phase 1
├── checklists/
│   └── requirements.md   # spec quality (all pass)
├── REVIEW-SPEC.md       # SOUND
├── review_brief.md      # reviewer guide
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Enums/DurationUnit.cs                                 # NEW
│   ├── ValueObjects/TimeDuration.cs                          # NEW
│   ├── ValueObjects/SupplierScore.cs                         # REWRITE (7-criterion + eligibility + tie)
│   └── Entities/
│       ├── Quotation.cs                                      # + DeliveryLeadTime, Warranty (TimeDuration)
│       └── Item.cs                                           # Approve(): CCSS sin-inscripción guard
├── FundingPlatform.Application/
│   ├── DTOs/ReviewApplicationDto.cs                          # expand ReviewQuotationDto
│   └── Services/ReviewService.cs                             # map new result; translate gate error
├── FundingPlatform.Infrastructure/
│   └── Persistence/Configurations/QuotationConfiguration.cs  # OwnsOne TimeDuration ×2
├── FundingPlatform.Database/
│   └── Tables/dbo.Quotations.sql                             # + 4 columns (NOT NULL + DEFAULT) + post-deploy seed update
└── FundingPlatform.Web/
    ├── ViewModels/AddSupplierViewModel.cs                    # + 4 quote fields (and the spec-023 edit VM)
    ├── ViewModels/ReviewApplicationViewModel.cs              # expand ReviewQuotationViewModel + item tie flag
    ├── Views/Shared/_QuoteFields.cshtml                      # + delivery/warranty inputs
    ├── Views/Item/Add.cshtml                                 # reorder: ProductName → Category → dynamic
    ├── Views/Review/Review.cshtml                            # total+breakdown, bloqueado, tie, no-eligible; drop /5
    └── Resources/SuppliersResources.cs (+ IUserFacingErrorTranslator)  # es-CR copy

tests/
├── FundingPlatform.Tests.Unit          # SupplierScore algorithm matrix, TimeDuration, tie/price-tie, eligibility
├── FundingPlatform.Tests.Integration   # quote capture required fields; Item.Approve CCSS gate (real DB)
└── FundingPlatform.Tests.E2E           # 5 user-story flows (Page Object Model)
```

**Structure Decision**: Existing 4-layer Clean Architecture solution; this feature touches each layer along established seams. No new projects.

## Phase 0 — Research

Complete: `research.md` (D1–D12). All decisions resolved; no `NEEDS CLARIFICATION` remain. Highlights: rewrite `SupplierScore` in place (D1); two tie rules (D2); tie → manual selection (D3); CCSS `sin inscripción` exclusion + domain `Item.Approve` gate, null ≠ block (D4); `TimeDuration` VO + `DurationUnit`, 30-days/month (D5); price compared on CRC-normalized amount, fixing a latent raw-`Price` bug (D6); live computation (D7); NOT NULL + DEFAULT placeholder + seed update (D8); AI comparison untouched (D9); DTO/VM expansion + drop `/4`,`/5` (D10); es-CR placement (D11); item-line reorder is markup-only (D12).

## Phase 1 — Design & Contracts

Complete: `data-model.md`, `contracts/interfaces.md`, `quickstart.md`. Agent context (`CLAUDE.md` SPECKIT block) updated to point at this plan.

**Post-design Constitution re-check: PASS** — design introduces no new dependencies, keeps scoring + gate in the domain, persists nothing derived, and stays on schema-first. Complexity Tracking remains empty.

## Phase 2 — Task generation (next)

`/speckit-tasks` will generate `tasks.md`. Anticipated phases:
1. **Foundational** — `DurationUnit`, `TimeDuration` (+ unit tests); `Quotation` fields + EF `OwnsOne`; dacpac columns + seed update.
2. **US2 (P1)** — quote capture/edit required fields (`_QuoteFields.cshtml`, VMs, handlers) + integration tests.
3. **US1 (P1)** — rewrite `SupplierScore.ComputeForItem`; expand DTO/VM; reviewer surface total+breakdown; unit tests (algorithm matrix incl. non-lowest-price winner, both tie rules) + E2E.
4. **US3 (P2)** — `Item.Approve` CCSS guard + `ReviewService` translation + `bloqueado`/no-eligible UI; integration + E2E.
5. **US4 (P3)** — top-score tie → manual selection UI + unit/E2E.
6. **US5 (P3)** — `Item/Add.cshtml` reorder + E2E.

## Complexity Tracking

No constitution violations. Table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
