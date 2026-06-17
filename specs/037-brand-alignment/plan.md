# Implementation Plan: Programa Semilla Official Brand Alignment

**Branch**: `037-brand-alignment` | **Date**: 2026-06-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/037-brand-alignment/spec.md`

## Summary

A visual-only facelift re-anchoring the FundingPlatform web UI to the **official** Programa Semilla
brand book — exact palette (`#008A9E`/`#42AFA8`/`#F9A61C`/`#FFC729`), real logo assets, dark teal
sidebar, de-zebra'd tables, kebab actions column, official combined-partner footer image, and a
narrow PDF brand-image re-tint — superseding spec 019's PDF-sampled approximations and placeholder
logos. The platform is already token-driven (`tokens.css` is the sole raw-hex file + a Tabler
bridge), so the core is a **~20-token remap + asset swap** with a few component-structure changes
(scoped dark-sidebar CSS, table de-zebra, `_RowActionsMenu` kebab, filter card, footer image). No
backend, schema, route, permission, or functionality change. Full surface re-sweep (applicant +
reviewer + admin + auth), reusing the spec-019 brand E2E suite and gate scripts.

Research resolved all four open questions (research.md D1–D13): the sidebar is **already dark** (so
`#12343B` is a scoped re-tint, not a restructure); the **PDF re-tint is a two-PNG swap** that keeps
the byte-identical carve-out gate green; **`#F9A61C` is a reserved decorative accent** (not
status-wired); and the **footer becomes one official image** while preserving the `sponsor-strip`
testid + print-hide contract.

## Technical Context

**Language/Version**: C# / .NET 10.0; CSS3; vanilla ES5-style JS (no build step)
**Primary Dependencies**: ASP.NET MVC, Tabler.io (vendored, NOT upgraded), Bootstrap 5 (Tabler's),
Inter (vendored). No new managed dependencies.
**Storage**: N/A — no data change (schema frozen, FR-032)
**Testing**: Playwright E2E (NUnit, Page Object Model) — reuse `tests/.../Brand/` suite; xUnit unit;
integration unchanged. `axe-playwright` for AA. Gate scripts under `scripts/`.
**Target Platform**: Last 2 evergreen browsers + iOS Safari (NFR-004)
**Project Type**: Web application (server-rendered MVC) — presentation-layer change only
**Performance Goals**: No LCP/TBT regression vs spec-019 baseline (NFR-001); brand assets ≤ 400 KB gz
(NFR-002 / SC-015)
**Constraints**: Visual-only — no backend logic, schema, routes, permissions, localization,
Tabler-upgrade, or new deps. `tokens.css` is the only raw-hex file. WCAG AA on all swept surfaces.
PDF generation pipeline/layout/body untouched (only two brand PNGs swapped).
**Scale/Scope**: Full surface sweep — applicant (home/journey/appeal/signing), reviewer
(queue/detail/inbox/history), admin (~18 sub-surfaces, Users as reference), auth (login/register/
reset/confirm), shared `_Layout`/`_AuthLayout` chrome. ~6 shared files + tokens.css + site.css +
brand assets + 2 PDF PNGs + page-object/snapshot updates.

## Constitution Check

*GATE: evaluated against `.specify/memory/constitution.md` v1.0.0. Re-checked post-design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | ✅ PASS | Changes confined to Web presentation (`wwwroot/css`, `Views/Shared`, `Views/Admin`, `wwwroot/lib/brand`) + PDF image assets. No Domain/Application/Infrastructure code touched. |
| II. Rich Domain Model | ✅ N/A | No domain behavior added or changed. |
| III. E2E Testing (NON-NEGOTIABLE) | ✅ PASS | Reuses + extends the spec-019 `Brand/` Playwright suite; per-surface brand assertions (FR-025), axe AA (FR-026), reduced-motion (FR-028), visual snapshots (FR-029). Delivery bar = filtered E2E green (SC-016). |
| IV. Schema-First DB | ✅ PASS | No dacpac change; `git diff main -- src/FundingPlatform.Database/` empty (FR-032/SC-014). |
| V. Specification-Driven Development | ✅ PASS | spec → plan → (tasks next) → implement; this plan + research + contracts produced before code. |
| VI. Simplicity & Progressive Complexity | ✅ PASS | Token remap + asset swap reuses the existing token system and gate scripts; no speculative abstraction. The one new partial (`_RowActionsMenu`) serves a concrete current need (consistent kebab across admin tables). |

**Technology Standards:** No new tech; Tabler not upgraded; no new NuGet (FR-035). **Gate result:
PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/037-brand-alignment/
├── spec.md                     # WHAT/WHY (6 stories, 35 FRs, 17 SCs, 5 NFRs)
├── plan.md                     # This file
├── research.md                 # Phase 0 — D1–D13, OQ-001…004 resolved
├── data-model.md               # No entities (schema frozen)
├── contracts/
│   └── ui-and-routes.md        # Token vocabulary, stable testids, route invariants, PDF contract
├── quickstart.md               # Build/run/verify recipe
├── checklists/requirements.md  # Spec quality checklist (passed)
├── REVIEW-SPEC.md              # Spec-review gate (SOUND)
├── review_brief.md             # Reviewer guide
├── implementation-notes.md     # HOW context from brainstorming
└── tasks.md                    # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (repository root) — files this feature touches

```text
src/FundingPlatform.Web/
├── wwwroot/css/
│   ├── tokens.css                       # PRIMARY — ~20-token remap, remove zebra, add sidebar/table/orange tokens, Tabler bridge literal RGB
│   └── site.css                         # scoped dark-sidebar block, topbar logout teal, .fl-filter-card, .fl-table hover/separator
├── wwwroot/lib/brand/
│   ├── (official horizontal/vertical/icon logos — replace mark.svg/wordmark.svg refs)
│   ├── favicons/favicon.*               # official icon disc
│   ├── partners-footer.png              # NEW — official combined strip (from Fooder-general.png)
│   └── pdf/
│       ├── header-seedling.png          # SWAP → official-teal disc
│       └── footer-partners-strip.png    # SWAP → official partner strip
├── Views/Shared/
│   ├── _Layout.cshtml                   # sidebar/topbar tweaks (classes/testids preserved); favicon ref
│   ├── _AuthLayout.cshtml               # vertical logo in hero
│   ├── _BrandSidebarHeader.cshtml       # horizontal logo in white container; keep wordmark text
│   ├── _SponsorStrip.cshtml             # single official image + yellow top border; keep testid/print-hide
│   └── Components/
│       ├── _PageHeader.cshtml           # verify (token-driven; likely no change)
│       └── _RowActionsMenu.cshtml       # NEW — kebab partial wrapping existing forms/links
└── Views/Admin/Users/Index.cshtml       # filter card + Limpiar filtros + kebab row actions (reference treatment)

scripts/
├── brand-grep-gate.sh                   # add spec-019 palette to legacy list; key yellow check to #FFC729
├── tokens-audit.sh / verify-tokens.sh   # still assert tokens.css-only raw hex
├── asset-budget-check.sh / verify-asset-budget.sh  # new asset paths; re-assert ≤400KB gz
├── verify-pdf-carveouts.sh              # must stay green (PNG swap only)
└── capture-perf-baseline.mjs / compare-perf.mjs    # re-capture NFR-001 baseline

tests/FundingPlatform.Tests.E2E/
├── Brand/*.cs                           # extend: AxeContrast (+sidebar AA, +Users), VisualRegression (+Users); presence tests stay green
├── PageObjects/AdminUsersListPage.cs    # open-kebab-then-act for relocated row actions
└── (sponsor-strip assertions re-pointed from per-SVG to the single image)
```

**Structure Decision**: Existing ASP.NET MVC Web project; no new projects. All work lands in
`FundingPlatform.Web` (presentation + assets) plus `scripts/` (gates) and the E2E test project.
Brand assets stay under `wwwroot/lib/brand/` (research D6) to keep audit/budget script paths and
`~/lib/brand/...` references coherent.

## Phase sequencing (for /speckit-tasks)

Suggested task ordering (US priorities from spec):
1. **Foundation (blocks all):** capture perf baseline; token remap in `tokens.css` (D2/D3); update
   gate scripts (D12). → unblocks the whole sweep, makes primaries teal + tables de-zebra globally.
2. **US4 (P1) brand assets:** place official logos, swap favicon, white-container sidebar header,
   vertical-logo auth hero; asset-budget check.
3. **US1/US2/US3 (P1) chrome + components:** scoped dark-sidebar CSS (D1), topbar logout (D11),
   footer image (D7), `_RowActionsMenu` kebab (D8) + Users filter card/Limpiar (D9), page-header
   verify (D10). Sweep remaining admin/reviewer/applicant/auth surfaces.
4. **US5 (P2) PDF:** swap the two PDF PNGs (D4); verify carve-out + fixture PDF.
5. **US6 (P2) a11y/responsive + verification:** axe AA (incl. sidebar), keyboard kebab, responsive
   checks, reduced-motion, page-object updates, visual-snapshot refresh, full filtered-E2E green,
   perf compare.

## Complexity Tracking

No constitution violations — section intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Asset budget: PNG logos + partner image heavier than the old SVGs | Optimize/resize to display dims (WebP/optimized PNG); measure day-1 via `asset-budget-check.sh` (research D12) |
| E2E churn from kebab + footer-image + de-zebra | Preserve all `row-action-*` + `sponsor-strip` testids + wordmark text; update one page object; refresh snapshots (research D13) |
| Dark-sidebar contrast regressions | Light text token `#D9E6E8`; axe AA assertion on the sidebar (FR-026) |
| PDF re-tint accidentally edits carve-out files | Swap PNGs only; `verify-pdf-carveouts.sh` guards the `.cshtml` byte-identity (research D4) |
| Off-white page bg `#F6F8FA` reads muddy on dense tables | Cards stay pure white; verify in visual-regression + user sign-off (SC-017) |
| Raster logo softness at large sizes (auth hero) | NFR-005 sizing; OQ-001 vector fallback if the visual pass flags it |
```
