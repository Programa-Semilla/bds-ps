# Implementation Plan: Review & Funding-Agreement UX Refinements

**Branch**: `027-review-funding-ux` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/027-review-funding-ux/spec.md`

## Summary

Eight consolidated stakeholder refinements to the reviewer/applicant journeys, delivered as eight independently testable user stories with **no database schema change** and **no change to the generated PDF document** (spec 018 preserved). The connective work is US4: one shared Application-layer projection (`IDecisionSummaryProjection` → `DecisionSummaryLineDto`) and one read-only Web partial (`_DecisionSummary.cshtml`) rendered consistently on the five interaction surfaces, so the per-line decision data (line code, product, category, technical specs, supplier, amount + CRC conversion note, status; rejected lines also show reason + all quoted suppliers) is identical everywhere. The remaining stories are targeted UI fixes/wirings: resolve a display name (US1), gate two consequential actions behind the spec-024 confirm dialog (US2), enrich the FA applicant block (US3), give the dangling `CodigoPersonal` a reviewer/admin write surface (US5), centralize required-field markers (US6), wire an own-JS HTML hover tooltip onto applicant fields (US7), and regroup the sidebar into Inicio/Administración/Proceso with zero removals (US8). es-CR throughout; delivery gated on a green full E2E run.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, Syncfusion HtmlToPdfConverter (untouched here), vendored Tabler.io + own interaction JS (confirm-dialog.js / notifications.js pattern)
**Storage**: SQL Server (dacpac schema source of truth) — **no schema change this feature**
**Testing**: xUnit (Unit), Integration (real DB, no mocks), Playwright for .NET (E2E, Page Object Model)
**Target Platform**: Linux server (Aspire-orchestrated), server-rendered MVC
**Project Type**: Web application (Clean Architecture: Domain / Application / Infrastructure / Web)
**Performance Goals**: No new hot path; the US4 projection is in-memory mapping over an already-loaded aggregate (no extra query for the lean shape)
**Constraints**: es-CR only (no `IStringLocalizer`; inline/static copy per NFR-003); all assets vendored (no CDN); no `window.bootstrap` dependence; PDF document body unchanged (FR-009)
**Scale/Scope**: 8 user stories; ~3 physical UI surfaces for US4 (+ inbox), 1 new Application projection, 2 new shared partials, 1 new JS module, 1 new ReviewController POST, ~20-form required-marker sweep, sidebar restructure

## Constitution Check

*GATE: must pass before Phase 0 and re-checked after Phase 1.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | PASS — `DecisionSummaryLineDto` + `IDecisionSummaryProjection` live in Application; partial/view models in Web; display-name via existing Application `IUserStoreReader`; `CodigoPersonal` write via Identity (`UserManager`) at the Web boundary. No inward-pointing violations. |
| **II. Rich Domain Model** | PASS — no anemic logic added. US5 sets an existing scalar via the established Identity write path (no new domain behavior needed). US4 is a read projection, not domain logic. |
| **III. E2E (NON-NEGOTIABLE)** | PASS — each story gets Playwright coverage driving the real journey (SC-008); US4 gets a single five-screen parity test. No deep-link shortcuts (project convention). |
| **IV. Schema-First** | PASS — FR-027: no schema change; US5 reuses `dbo.AspNetUsers.CodigoPersonal`. No EF migrations. |
| **V. Spec-Driven** | PASS — spec → plan → tasks → implement; this plan + contracts precede code. |
| **VI. Simplicity / YAGNI** | PASS with two tracked deferrals — the US4 projection is deliberately lean (no AI/scores/impact); process-scoped Reportes/Plantillas surfaces are **not** built (deferred; see Complexity Tracking). |

**Quality gates (constitution Development Workflow):** validation errors aggregated and shown together (US5 form, US2 reject comment); authorization verifies resource ownership/group-overlap (US5 review-screen predicate, spec 016); optimistic concurrency considered for US5 — judged unnecessary for a single low-contention scalar (last-write-wins, documented in spec edge cases).

**Result: PASS** (pre-Phase-0 and post-Phase-1). No unjustified violations.

## Project Structure

### Documentation (this feature)
```text
specs/027-review-funding-ux/
├── plan.md              # this file
├── spec.md
├── research.md          # Phase 0 — D1..D7 decisions
├── data-model.md        # Phase 1 — entities (unchanged) + new projection shapes
├── quickstart.md        # Phase 1 — run + per-story verification
├── contracts/           # Phase 1
│   ├── decision-summary.md   # US4 projection + partial contract
│   ├── ui-surfaces.md        # US1/2/3/5/6/7/8 markup + route contracts
│   └── sidebar-structure.md  # US8 before→after, zero removals
├── implementation-notes.md   # file:line anchors (from brainstorm)
├── review_brief.md
├── REVIEW-SPEC.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source code (touched paths)
```text
src/FundingPlatform.Application/
├── DTOs/DecisionSummaryLineDto.cs            # NEW (US4)
└── Services/
    ├── IDecisionSummaryProjection.cs         # NEW (US4)
    ├── DecisionSummaryProjection.cs          # NEW (US4)
    ├── SignedUploadService.cs                # EDIT :154 (US1)
    └── FundingAgreementService.cs            # EDIT :70 (US1)

src/FundingPlatform.Web/
├── Controllers/
│   ├── ReviewController.cs                   # EDIT — new ApplicantCode POST (US5)
│   ├── FundingAgreementController.cs         # EDIT — applicant block + decision summary (US3/US4)
│   └── ApplicantResponseController.cs        # EDIT — feed decision summary (US4)
├── Views/
│   ├── Shared/_DecisionSummary.cshtml        # NEW (US4)
│   ├── Shared/_RequiredMark.cshtml           # NEW (US6)
│   ├── Shared/_HintTooltip.cshtml            # EDIT — icon + HTML copy (US7)
│   ├── Shared/_Layout.cshtml                 # EDIT — sidebar regroup + hint-tooltip.js (US8/US7)
│   ├── Applications/_FundingAgreementPanel.cshtml  # EDIT — confirm attrs (US2), name (US1)
│   ├── FundingAgreement/Details.cshtml       # EDIT — applicant block + summary (US3/US4)
│   ├── Review/Review.cshtml                  # EDIT — código field + summary (US5/US4)
│   ├── ApplicantResponse/Index.cshtml        # EDIT — decision summary (US4)
│   └── <form views across app>               # EDIT — required-marker sweep (US6)
├── Localization/ or Resources/HintCopy.cs    # NEW — static es-CR hint copy provider (US7)
└── wwwroot/js/hint-tooltip.js                # NEW (US7)

tests/
├── FundingPlatform.Tests.Unit/               # projection mapping, display-name fallback
├── FundingPlatform.Tests.Integration/        # CodigoPersonal write (real DB)
└── FundingPlatform.Tests.E2E/                # per-story Playwright + 5-screen parity
```

**Structure Decision**: Existing four-layer web app. New code lands in Application (projection) and Web (partials, JS, controller edits); the only "new types" are presentation/projection DTOs. No Infrastructure/Domain schema work.

## Complexity Tracking

| Deferral / Decision | Why | Simpler alternative chosen |
|---|---|---|
| Process-scoped Reportes/Plantillas surfaces NOT built (US8) | No process-scoped routes exist; building net-new aggregation surfaces is out of this feature's lean intent | Place each existing surface once under its best-fit group; defer true process-scoping to a future spec (research D7 / sidebar-structure.md open decision). **Needs user confirmation in plan review.** |
| "Starters" reuses the existing applications listing (US8) | No standalone applications-list controller exists today | Thin nav-reachable route to the existing Reports Applications view filtered by Process, rather than a brand-new surface. **Route form needs user confirmation.** |
| US5 last-write-wins (no concurrency token) | Single low-contention scalar (`CodigoPersonal`); concurrent reviewer edits are rare and non-destructive | Skip optimistic concurrency here (constitution quality-gate exception is documented in spec edge cases) rather than add a rowversion for one field |

## Open items surfaced for the user (plan review)
1. Confirm the **Starters** route form (deep-link to Reports Applications tab w/ `processId` vs a thin dedicated action).
2. Confirm **deferral** of process-scoped Reportes/Plantillas (recommended) vs build-now (expands scope).
3. Confirm US2 reject-comment UX interplay with the confirm dialog (gate confirm on filled comment vs rely on server-side enforcement).

## Phase 2 note
`/speckit-tasks` generates `tasks.md` (per-user-story, dependency-ordered). Not created by this command.
