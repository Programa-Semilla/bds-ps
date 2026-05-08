# Implementation Plan: Admin UX/UI Facelift

**Branch**: `017-admin-ux-facelift` | **Date**: 2026-05-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/017-admin-ux-facelift/spec.md`

## Summary

`/Admin` becomes a capability-complete dashboard (4 action KPIs + 9 grouped capability cards + optional activity feed); 10 admin sub-surfaces are swept at the spec 011 quality bar; legacy `AdminCurrencies` / `AdminExchangeRates` / `AdminLegacyQuotations` routes are normalized; sidebar admin entries collapse under a section header; Reports tabs and `_KpiTile` are re-templated for warm-modern. Schema unchanged. PDF carve-outs preserved.

**Technical approach**: Two new partials (`_AdminDashboard`, `_CapabilityCard`); a new Application-layer projection (`IAdminDashboardProjection`) exposing four sub-projections (pending suppliers, pending legacy quotations, aging applications, active users) with a deliberate degrade-to-zero failure mode (R2); a new `IAdminAuditEventReader` + `IAdminAuditEventCopyProvider` for the activity feed; in-place re-template of `_KpiTile` + `_ReportSubTabs`; route-attribute renames on three controllers; sidebar layout refactor to add a section header without disturbing testid slugs; full POM rewrite across the swept surfaces.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire 9, Tabler.io vendored CSS/JS, Playwright for .NET, axe-playwright (existing). No new managed dependencies.
**Storage**: SQL Server (Aspire-managed dev container; persistent in real envs). dacpac is the schema source of truth — **unchanged** by this spec (FR-027 / SC-016).
**Testing**: xUnit (Unit + Integration), Playwright .NET (E2E with NUnit runner per existing convention), Page Object Model pattern.
**Target Platform**: Linux server (Aspire orchestration); modern evergreen browsers for the UI.
**Project Type**: Server-rendered MVC web application (single project tree under `src/`).
**Performance Goals**: Dashboard first-paint identifies KPI strip + capability sections + activity feed (when present) above the 1366×768 fold (SC-021). KPI projection completes in < 100 ms p95 for the prod-like fixture (matches spec 011 reviewer queue posture; not a separate FR — internal target).
**Constraints**: Schema-locked (FR-027 / SC-016). No new fonts / illustrations / managed deps. Combined wire weight added by spec 017 < 30 KB gzipped (FR-030 / SC-020). PDF carve-out files byte-identical (FR-028 / SC-017).
**Scale/Scope**: 10 admin sub-surfaces in the sweep inventory; ~30 cshtml files; ~10 POM classes (Playwright); 1 new projection + 4 sub-projections + 2 new application-layer providers. No new domain entities.

## Constitution Check

*Gate evaluation against `.specify/memory/constitution.md` v1.0.0.*

| Principle | Status | Evidence |
|---|---|---|
| **I. Clean Architecture** | ✅ PASS | New projection lives in `FundingPlatform.Application/Services/`. Web depends on Application; Application depends only on Domain. Sub-projection helpers are private methods on the projection class. No Web→Domain shortcut. |
| **II. Rich Domain Model** | ✅ N/A — PASS | Spec touches no Domain entity behavior. New types are DTOs / view models / application services only. The existing `AdminAuditEvent` entity (consumed read-only) already enforces its own invariants via the `Record` factory. |
| **III. E2E NON-NEGOTIABLE** | ✅ PASS | FR-026 requires Playwright E2E per user story; SC-019 enforces; quickstart.md §9 lists the run commands; ADMIN-SWEEP-CHECKLIST.md tracks POM rewrites per surface. Reduced-motion test runs via Playwright reduce-motion option. |
| **IV. Schema-First (dacpac)** | ✅ PASS | FR-027 + SC-016 forbid Database project edits; explicit `/speckit-spex-evolve` escape hatch; verification script in quickstart.md §10. Zero migrations. Zero `EnsureCreated`. |
| **V. Specification-Driven** | ✅ PASS | This plan exists; spec.md ratified SOUND on first review pass. |
| **VI. Simplicity / YAGNI** | ✅ PASS | 10 OOS clauses cap scope. No speculative abstraction (e.g., the four sub-projections are private methods rather than separate services because the public surface only ever calls one method). Activity feed (US7) is P3 with explicit hide-when-empty fallback so it doesn't block delivery. Two new partials, no new tokens, no new fonts, no new libraries — reuse-first by construction. |

**Gate verdict**: PASS pre-Phase 0. Will be re-evaluated after Phase 1 design (see end of this document).

No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/017-admin-ux-facelift/
├── spec.md                       Feature spec (already approved)
├── plan.md                       This file
├── research.md                   Phase 0 — resolves R1..R10
├── data-model.md                 Phase 1 — DTOs / projection interfaces, no schema delta
├── quickstart.md                 Phase 1 — local build/run/verify walkthrough
├── ADMIN-SWEEP-CHECKLIST.md      Phase 1 — per-view criteria walk per FR-008 / SC-007
├── REVIEW-SPEC.md                spec review report (SOUND)
├── review_brief.md               reviewer guide
├── checklists/requirements.md    spec quality checklist
├── tasks.md                      Phase 2 (NOT created by /speckit-plan)
└── contracts/                    (intentionally absent — see below)
```

**No `contracts/` directory** for this spec: the project exposes no public API / CLI / external interface change. The new "interfaces" are internal Razor partials (`_AdminDashboard`, `_CapabilityCard`) plus C# interfaces (`IAdminDashboardProjection`, `IAdminAuditEventReader`, `IAdminAuditEventCopyProvider`) which are documented in `data-model.md`. Per the plan-template guidance: *"Skip if project is purely internal."*

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/                 ── (no changes — schema-locked)
├── FundingPlatform.Application/
│   ├── DTOs/
│   │   └── AdminDashboardDto.cs            ── NEW (and AdminDashboardKpis, CapabilitySection,
│   │                                          CapabilityCard, AdminEvent records)
│   └── Services/
│       ├── IAdminDashboardProjection.cs    ── NEW (interface)
│       ├── AdminDashboardProjection.cs     ── NEW (4 sub-projections inside)
│       ├── IAdminAuditEventReader.cs       ── NEW (interface)
│       ├── IAdminAuditEventCopyProvider.cs ── NEW (interface)
│       └── AdminAuditEventCopyProvider.cs  ── NEW (es-CR copy mappings for 4 actions)
├── FundingPlatform.Infrastructure/
│   └── Persistence/
│       └── AdminAuditEventReader.cs        ── NEW (EF Core impl over DbContext.AdminAuditEvents)
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   ├── AdminController.cs              ── MODIFIED (Index calls IAdminDashboardProjection)
│   │   └── Admin/
│   │       ├── AdminCurrenciesController.cs    ── MODIFIED ([Route("Admin/Currencies")])
│   │       ├── AdminExchangeRatesController.cs ── MODIFIED ([Route("Admin/ExchangeRates")])
│   │       └── AdminLegacyQuotationsController.cs ── MODIFIED ([Route("Admin/LegacyQuotations")])
│   ├── Views/
│   │   ├── Admin/
│   │   │   ├── Index.cshtml                ── REWRITTEN (composes _AdminDashboard partial)
│   │   │   ├── Configuration.cshtml        ── SWEPT
│   │   │   ├── ImpactTemplates.cshtml      ── SWEPT
│   │   │   ├── CreateTemplate.cshtml       ── SWEPT
│   │   │   └── EditTemplate.cshtml         ── SWEPT
│   │   ├── AdminUsers/                     ── SWEPT (Index / Create / Edit / ResetPassword)
│   │   ├── AdminGroups/                    ── SWEPT (Index / Create / Edit / Detail)
│   │   ├── AdminSuppliers/                 ── SWEPT (Index / Detail / edit-approve flow)
│   │   ├── AdminReports/                   ── SWEPT (5 tabs + driven by US6)
│   │   ├── AdminCurrencies/                ── SWEPT (Index / Create / Edit)
│   │   ├── AdminExchangeRates/             ── SWEPT (Index / Create)
│   │   ├── AdminLegacyQuotations/          ── SWEPT (Index / Detail)
│   │   └── Shared/
│   │       ├── _Layout.cshtml              ── MODIFIED (sidebar admin section header)
│   │       └── Components/
│   │           ├── _AdminDashboard.cshtml  ── NEW
│   │           ├── _CapabilityCard.cshtml  ── NEW
│   │           ├── _KpiTile.cshtml         ── RE-TEMPLATED (FR-022 ticker + tokens)
│   │           └── _ReportSubTabs.cshtml   ── RE-TEMPLATED (FR-021 pill chips)
│   └── ViewModels/Admin/
│       └── AdminDashboardViewModel.cs      ── NEW (thin Web-layer adapter on AdminDashboardDto)
└── FundingPlatform.Database/               ── ZERO CHANGES (SC-016)

tests/
├── FundingPlatform.Tests.Unit/
│   ├── Application/Services/
│   │   ├── AdminDashboardProjectionTests.cs           ── NEW
│   │   └── AdminAuditEventCopyProviderTests.cs        ── NEW
├── FundingPlatform.Tests.Integration/
│   ├── AdminDashboardProjectionIntegrationTests.cs    ── NEW (real DB, fault-injected sub-projection)
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/Admin/                              ── REWRITTEN POMs for every swept surface
    ├── AdminDashboardTests.cs                          ── NEW (US1 — 4 fixtures + reduced-motion + click-walk)
    ├── AdminSidebarGroupingTests.cs                    ── NEW (US4)
    ├── AdminRouteNormalizationTests.cs                 ── NEW (US5)
    ├── AdminReportsTabUxTests.cs                       ── NEW (US6)
    ├── AdminEmptyStatesTests.cs                        ── NEW (US3 — illustration map per surface)
    ├── AdminActivityFeedTests.cs                       ── NEW (US7 — visible/hidden cases)
    └── AdminSweepRegressionTests.cs                    ── NEW (US2 — POM smoke per surface)
```

**Structure decision**: Existing single-tree layout per `src/FundingPlatform.*` (matches CLAUDE.md). No new project; everything lands inside the four existing assemblies that mirror Clean Architecture. Tests split across the three existing test projects per type.

## Phase 0 — Research

Output: [research.md](./research.md). Resolves R1–R10:

| ID | Topic | Resolution |
|---|---|---|
| R1 | Pending-supplier source enum | `SupplierVerificationStatus.PendingReview` |
| R2 | Pending-supplier failure mode | Render `0` + WARN log (no "—" placeholder, no error tile) |
| R3 | FR-015 sidebar implementation | Section header click target navigates to `/Admin`; no "Panel" sub-entry |
| R4 | Projection naming + placement | `IAdminDashboardProjection` / `AdminDashboardProjection` / `AdminDashboardDto` in Application |
| R5 | Activity feed copy mapping | `IAdminAuditEventCopyProvider` with es-CR for 4 audit actions |
| R6 | Sweep audit deliverable | `ADMIN-SWEEP-CHECKLIST.md` per FR-008 / SC-007 |
| R7 | Partial inventory | 2 new partials; `_KpiTile` and `_ReportSubTabs` re-templated in place |
| R8 | Route normalization scope | Attributes only; class names + namespaces unchanged |
| R9 | Reduced-motion contract | Reuse spec 011 contract; no new motion catalog entry |
| R10 | Schema-unchanged enforcement | CI-style verify in quickstart §10 |

All NEEDS CLARIFICATION resolved.

## Phase 1 — Design & Contracts

### Data model

[data-model.md](./data-model.md). Recap:

- `AdminDashboardDto` (Kpis + Sections + RecentEvents + FeedVisible)
- `AdminDashboardKpis` (4 `int` counts + 4 deep-link URLs)
- `CapabilitySection` × 3 (template-time fixed)
- `CapabilityCard` × 9 (slug, label, description, icon, CTA url+label)
- `AdminEvent` (OccurredAt, ActorDisplayName, Copy, DeepLinkUrl?)
- New interfaces: `IAdminDashboardProjection`, `IAdminAuditEventReader`, `IAdminAuditEventCopyProvider`
- Existing entities consumed read-only: `Supplier`, `Application`, `User`, `Currency`, `ExchangeRate`, `LegacyQuotation`, `Group`, `AdminAuditEvent`, `ImpactTemplate`, `SystemConfiguration`

Schema delta: empty.

### Contracts

Intentionally absent. The project does not gain a new public/external interface in this spec. Internal interfaces are documented in `data-model.md`.

### Quickstart

[quickstart.md](./quickstart.md). 12-step local walk-through covering build → boot → US1 verification → US5 route normalization → US4 sidebar grouping → US2/US3 sweep + empty states → US6 reports tabs → US7 activity feed → run all 3 test suites → schema-unchanged check → PDF identity check → wire-weight check.

### Sweep checklist

[ADMIN-SWEEP-CHECKLIST.md](./ADMIN-SWEEP-CHECKLIST.md). Single source of truth for the manual walk that satisfies SC-007.

### Agent context update

The CLAUDE.md `Recent Changes` and `Active Technologies` sections will be updated to reference spec 017 between the `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->` markers (when present) or by appending a one-line "017-admin-ux-facelift: …" entry to those sections. Performed after this plan lands.

## Phase 1 Constitution Check (post-design)

Re-evaluation after data-model + quickstart + sweep checklist authored:

| Principle | Status | Evidence |
|---|---|---|
| I. Clean Architecture | ✅ PASS | All new code lives in Application + Web layers per planned structure; Infrastructure adds only the EF Core read of `AdminAuditEvent`. |
| II. Rich Domain Model | ✅ N/A | No domain changes. |
| III. E2E NON-NEGOTIABLE | ✅ PASS | E2E tests planned per user story (7 new test files) + reduced-motion run + POM rewrites enumerated in checklist. |
| IV. Schema-First | ✅ PASS | data-model.md states "Schema delta: empty". Verification step in quickstart §10. |
| V. SDD | ✅ PASS | spec → research → data-model → plan all in place. |
| VI. Simplicity / YAGNI | ✅ PASS | Sub-projections collapsed into a single class; no new fonts / libs / illustrations; activity feed degrades to hidden when empty so it doesn't block delivery. Two new partials, both multi-host (`_CapabilityCard` × 9 instances; `_AdminDashboard` is the single composition point). |

**Post-design gate verdict**: PASS. No deviations recorded; Complexity Tracking remains empty.

## Phase 2 — Tasks (NOT generated by /speckit-plan)

Phase 2 lands when `/speckit-tasks` runs. The task list will be derived from this plan + spec + data-model + quickstart + sweep checklist, organized by user story (US1 → US7), with explicit dependencies and parallel opportunities flagged via `[P]`.

Anticipated task themes (in dependency order):

1. **Foundations** — projection scaffolding (`AdminDashboardDto`, interfaces, copy provider), DI registration, controller wiring.
2. **US1** — `_AdminDashboard` + `_CapabilityCard` partials, `AdminController.Index` wiring, KPI tile + capability card rendering, click-walk fixture seeding.
3. **US3** — illustration-backed `_EmptyState` migrations across all admin tables (drives partial usage in US2 sweep).
4. **US7** — activity feed projection + reader + copy provider + view rendering with hidden-when-empty.
5. **US6** — `_KpiTile` and `_ReportSubTabs` re-template; reports table density.
6. **US2** — per-surface sweep walk (parallelizable across surfaces `[P]`).
7. **US4** — sidebar layout refactor; testid stability tests.
8. **US5** — route-attribute renames; old-path-404 + new-path-200 tests.
9. **POMs** — rewrite per swept surface, semantic locators, reduced-motion test.
10. **Verification** — axe-playwright suite, wire-weight check, schema-diff check, voice-guide pass, designer/product sign-off recorded in PR.

## Open follow-ups carried into Phase 2

None. All open threads from REVIEW-SPEC and the brainstorm are resolved by `research.md`. Tasks.md will reference R1–R10 where the source decisions are needed.

---

**Plan complete.** Ready for `/speckit-tasks` to produce `tasks.md` (Phase 2).
