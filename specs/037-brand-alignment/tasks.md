---
description: "Task list for Programa Semilla Official Brand Alignment (037)"
---

# Tasks: Programa Semilla Official Brand Alignment

**Input**: Design documents from `/specs/037-brand-alignment/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D13), data-model.md (no entities),
contracts/ui-and-routes.md, quickstart.md

**Tests**: INCLUDED — Constitution III (E2E NON-NEGOTIABLE) + the spec's FR-025/026/028/029 mandate
per-surface brand assertions, axe AA, reduced-motion, and visual-regression. This feature reuses and
extends the existing `tests/FundingPlatform.Tests.E2E/Brand/` suite rather than writing new domain
tests.

**Organization**: By user story (spec priorities). Note: this is a presentation-layer re-skin — the
token remap (Phase 2) cascades the official palette platform-wide, so the per-story phases are mostly
**sweep + verify** of surfaces that inherit the foundational changes. All paths are relative to repo
root `/mnt/D/repos/bds-ps-2/`.

## Path conventions

- Web: `src/FundingPlatform.Web/` (`wwwroot/css`, `wwwroot/lib/brand`, `Views/`)
- Scripts: `scripts/`
- E2E: `tests/FundingPlatform.Tests.E2E/`
- Brand source assets: `seeds/facelift-2/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Capture the perf baseline before any change; stage the official assets.

- [ ] T001 [P] Capture pre-change perf baseline via `node scripts/capture-perf-baseline.mjs` for applicant home + reviewer queue (NFR-001); commit the baseline JSON under `specs/037-brand-alignment/` or the script's default location.
- [ ] T002 Stage the official brand assets into `src/FundingPlatform.Web/wwwroot/lib/brand/` (optimized/resized, raster-as-provided per NFR-005): horizontal logo, vertical logo, icon disc, `partners-footer.png` (from `seeds/facelift-2/Fooder-general.png`), and a favicon from the icon disc. Record the exact filenames chosen (used by later tasks).

**Checkpoint**: Baseline captured; official assets present in `wwwroot/lib/brand/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The token remap + shared-chrome CSS + gate-script updates. This cascades the official
palette, dark sidebar, teal primaries, and de-zebra'd tables across EVERY surface in one pass.

**⚠️ CRITICAL**: No user-story sweep can be meaningfully verified until this phase is complete.

- [ ] T003 Remap color token VALUES in `src/FundingPlatform.Web/wwwroot/css/tokens.css` per research D2: `--color-primary` #008A9E, `-strong` #007789, `-subtle` #D6EEF1, `-rgb` `0, 138, 158`; `--color-accent` #FFC729, `-subtle` #FFEFB8; `--color-bg-page` #F6F8FA, `-surface-raised` #FFFFFF; `--color-border` #DDE5E8; `--color-text-primary` #1F2933, `-secondary`/`-muted` #64748B; `--color-success` #168A4A; `--color-danger` #D92D20. Update the Tabler bridge literal `--tblr-primary-rgb: 0, 138, 158;` (other `--tblr-*` reference tokens via `var()`).
- [ ] T004 Add NEW tokens to `tokens.css` (research D2): `--color-primary-light: #42AFA8`, `--color-accent-orange: #F9A61C` (reserved decorative — D5), `--color-sidebar-bg: #12343B`, `--color-sidebar-hover: #174A53`, `--color-sidebar-text: #D9E6E8`, `--color-table-hover: #EFF8F8`, `--color-table-separator: #E5ECEF`.
- [ ] T005 Remove `--color-table-zebra` from `tokens.css` and rewrite its sole consumer `.fl-table tbody tr:nth-child(even) td` (de-zebra, D3): body rows stay `--color-bg-surface`; add `.fl-table tbody tr:hover td { background: var(--color-table-hover); }` and `.fl-table tbody td { border-bottom: 1px solid var(--color-table-separator); }`. Header band stays `var(--color-primary)` + white. Preserve `data-density` rules.
- [ ] T006 [P] Add the scoped dark-sidebar block to `src/FundingPlatform.Web/wwwroot/css/site.css` targeting `[data-testid="sidebar"]` (research D1): background `var(--color-sidebar-bg)`, nav-link text `var(--color-sidebar-text)`, hover `var(--color-sidebar-hover)`, active item = teal-tint bg + white text + `border-left: 4px solid var(--color-primary-light)`. Reference tokens only (no raw hex).
- [ ] T007 [P] Add the scoped topbar-logout teal rule to `site.css` targeting `[data-testid="topbar"]` so `nav-link`/`btn-link` reads `var(--color-primary)` instead of link-blue (research D11) — must not affect the dark sidebar links.
- [ ] T008 [P] Add `.fl-filter-card` (white, `var(--color-border)`, 12px radius, 16px padding, consistent 38–40px control heights) to `site.css` (research D9).
- [ ] T009 Update `scripts/brand-grep-gate.sh`: add the spec-019 palette (`#1FA0A0 #15807F #D7EDED #F2C014 #FBEBA6 #FFF3E5`) to the "legacy hex must not appear outside tokens.css history" list; key the yellow-not-semantic check to `#FFC729` / `--color-accent`.
- [ ] T010 [P] Confirm `scripts/tokens-audit.sh` + `scripts/verify-tokens.sh` still pass with the new `site.css` rules (raw hex only in `tokens.css` + PDF carve-outs); adjust allow-list comments only if needed.

**Checkpoint**: App builds; every surface inherits official teal primaries, dark `#12343B` sidebar, and de-zebra'd tables. `dotnet build FundingPlatform.slnx` green.

---

## Phase 3: User Story 4 - Real official logos replace placeholders (Priority: P1) 🎯 brand anchor

**Goal**: Official horizontal/vertical/icon logos + the official partner footer image land in every
context; placeholders retired.

**Independent Test**: Render expanded sidebar, collapsed sidebar, auth hero, footer, favicon — each
shows the correct official asset; no placeholder `mark.svg`/`wordmark.svg` referenced.

- [ ] T011 [P] [US4] Update `src/FundingPlatform.Web/Views/Shared/_BrandSidebarHeader.cshtml`: render the official horizontal logo inside a white rounded container; KEEP a `Programa Semilla` text node (visually hidden if the logo already shows the wordmark) and the `alt`/`title`/`data-testid="sidebar-brand"` (preserves `BrandPresence*Tests`).
- [ ] T012 [P] [US4] Add the white rounded-container CSS for the sidebar logo to `site.css` (research D6/OQ-002): `background: var(--color-bg-surface); border-radius: var(--radius-md); padding: var(--space-2)`.
- [ ] T013 [P] [US4] Update `src/FundingPlatform.Web/Views/Shared/_AuthLayout.cshtml` to use the official vertical logo in the hero; KEEP `alt="Programa Semilla"`; tagline copy unchanged.
- [ ] T014 [P] [US4] Replace the favicon `<link rel="icon">` in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` with the official icon disc.
- [ ] T015 [US4] Rewrite `src/FundingPlatform.Web/Views/Shared/_SponsorStrip.cshtml` to render a single official partner image (`~/lib/brand/partners-footer.png`, descriptive `alt` listing the official partner set) inside the existing `<footer class="fl-sponsor-strip" data-testid="sponsor-strip" data-print-hide=... aria-label="Patrocinadores">`; keep the `HideOnPrint` logic. Update `.fl-sponsor-strip` in `tokens.css` for a `3px` `var(--color-accent)` top border + centered responsive image (`max-width: 1100px; width: 100%; height: auto`); drop the per-`data-sponsor` rows and the `10-anos` `@media` hide (research D7).
- [ ] T016 [US4] Run `bash scripts/asset-budget-check.sh` (update enumerated paths to the new assets); optimize/resize so total brand assets ≤ 400 KB gz (SC-015 / NFR-002).
- [ ] T017 [P] [US4] Update `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceLoginTests.cs` (vertical-logo `GetByAltText("Programa Semilla")`) and re-point any per-`data-sponsor` assertions to the single `sponsor-strip` image; run `dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~BrandPresenceLogin|FullyQualifiedName~PrintLayout"`.

**Checkpoint**: Official logos render per context; footer is the official image with a yellow top border; placeholders gone; asset budget green.

---

## Phase 4: User Story 1 - Applicant sees one consistent official brand end-to-end (Priority: P1)

**Goal**: Every applicant surface (login → home → journey → appeal → signing) wears the official
identity inherited from Phases 2–3.

**Independent Test**: Applicant E2E walks login + home + journey + signing; asserts dark sidebar bg,
sidebar logo, teal primaries, de-zebra'd tables, footer image; PDF handoff reads teal (PDF asset in US5).

- [ ] T018 [US1] Sweep applicant surfaces (`Views/Home`, `Views/Applications`, journey/appeal/signing partials) for any view carrying a legacy class, inline hex, or blue primary that bypasses the token bridge; fix to token-driven equivalents. No copy changes (localization invariant FR-034).
- [ ] T019 [P] [US1] Extend/confirm `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceApplicantTests.cs`: assert dark sidebar background, `sidebar-brand` logo, and `sponsor-strip` image across applicant home + detail + journey; run filtered.
- [ ] T020 [P] [US1] Visual check the funding-agreement preview surface (`src/FundingPlatform.Web/Views/Applications/` detail/signing views with the PDF-preview iframe + download CTA) still renders correctly against the new chrome (no layout regression at the iframe border); note the PDF brand asset itself is US5.

**Checkpoint**: Applicant journey is brand-consistent from login through the signing surface.

---

## Phase 5: User Story 2 - Reviewer surfaces lift, density preserved (Priority: P1)

**Goal**: Reviewer queue/detail/inbox/history wear the official identity at reviewer density.

**Independent Test**: Reviewer E2E visits the four surfaces; asserts official-teal table headers,
white (non-zebra) rows, dark sidebar, and that `data-density="reviewer"` padding stays dense while
applicant tables stay roomy.

- [ ] T021 [US2] Sweep reviewer surfaces (`Views/Review`, signing inbox/history) for token cascade + de-zebra correctness; confirm `data-density="reviewer"` preserved on all reviewer tables (FR-019).
- [ ] T022 [P] [US2] Extend/confirm `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceReviewerTests.cs` + `ReviewerDensityTests.cs`: brand presence on all four surfaces + dense cell padding unchanged; run filtered.

**Checkpoint**: Reviewer surfaces match the official brand; density rule intact.

---

## Phase 6: User Story 3 - Admin surfaces lift uniformly; Users page reference treatment (Priority: P1)

**Goal**: Standardized page headers, white filter card + "Limpiar filtros", de-zebra'd tables, and
the `Editar` + `⋯` kebab actions — first on Users, then swept across admin.

**Independent Test**: Admin E2E on `/Admin` + Users + sample sub-surfaces; asserts teal primary CTA,
filter card with Aplicar + Limpiar filtros, de-zebra table, Editar + kebab exposing Reenviar/
Restablecer/Inhabilitar — each still hitting its original route.

- [ ] T023 [US3] Create `src/FundingPlatform.Web/Views/Shared/Components/_RowActionsMenu.cshtml`: renders `Editar` as a visible `<a data-testid="row-action-edit">` + a `⋯` toggle (`data-testid="row-actions-menu-<id>"`, `ti ti-dots-vertical`, `data-bs-toggle="dropdown"`) opening a `dropdown-menu dropdown-menu-end`. Accepts the row's existing forms/links so they move in verbatim (research D8). Keyboard/SR operable.
- [ ] T024 [US3] Apply `_RowActionsMenu` to the actions cell of `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml`: move Reenviar invitación (POST form), Restablecer (`<a>`), Inhabilitar (POST + all `data-confirm-*`), Habilitar (POST) into the kebab as `dropdown-item`s; Inhabilitar styled red (`text-danger`). PRESERVE every `data-testid` (`row-action-edit/-resend-invite/-reset-password/-disable/-enable`), antiforgery tokens, verbs, and routes (contract A). Verify the Users page-header copy stays exact ("Usuarios" / "Administre las cuentas de usuario de la plataforma." / "Crear usuario" / "Crear por lote").
- [ ] T025 [US3] Wrap the Users filter `<form data-testid="admin-users-filter-form">` in `.fl-filter-card` and add a "Limpiar filtros" link `data-testid="admin-users-filter-clear"` → `@Url.Action("Index", "AdminUsers")` (param-less reset, no new endpoint — FR-033/D9). Keep Aplicar + the cascading fund filter unchanged.
- [ ] T026 [P] [US3] Apply `_RowActionsMenu` to the other admin list views whose rows carry ≥2 actions, preserving each view's existing action testids/routes/verbs. Concrete targets (grep `Views/Admin/` for rows with ≥2 `<a>`/`<form>` actions before editing): `Views/Admin/Suppliers/Index.cshtml`, `Views/Admin/Groups/Index.cshtml`, `Views/Admin/Currencies.cshtml`, `Views/Admin/ExchangeRates.cshtml`, `Views/Admin/Funds/Index.cshtml`, `Views/Admin/Processes/Index.cshtml`, `Views/Admin/Categories.cshtml`, `Views/Admin/ImpactTemplates/Index.cshtml` (skip any that have only a single action — leave those inline).
- [ ] T027 [US3] Sweep the remaining admin sub-surfaces (`/Admin` index + the ~18 sub-views) for token cascade + standardized `_PageHeader` teal primary CTA (research D10); fix any blue primary or legacy class.
- [ ] T028 [US3] Update `tests/FundingPlatform.Tests.E2E/PageObjects/AdminUsersListPage.cs` to open the kebab (`row-actions-menu-*`) before clicking the relocated `row-action-*` items; cascade the same helper to other admin list page objects touched by T026.
- [ ] T029 [P] [US3] Run admin/user E2E green: `dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~BrandPresenceAdmin|FullyQualifiedName~AdminUserLifecycle|FullyQualifiedName~AdminUserCode|FullyQualifiedName~UserInvitation|FullyQualifiedName~AdminResetPassword|FullyQualifiedName~SentinelImmutability"`.

**Checkpoint**: Admin area uniform; Users page is the reference treatment; all relocated actions work via their original routes.

---

## Phase 7: User Story 5 - Funding Agreement PDF reconverges with official teal (Priority: P2)

**Goal**: The PDF logo disc + partner strip read official teal; generation pipeline/layout/body untouched.

**Independent Test**: Generate a fixture PDF — brand chrome reads official teal; layout/content
identical to a pre-facelift fixture (only color + timestamp differ).

- [ ] T030 [US5] Replace `src/FundingPlatform.Web/wwwroot/lib/brand/pdf/header-seedling.png` (official-teal disc, sourced from the icon) and `footer-partners-strip.png` (official partner strip, from `Fooder-general.png`), sized to the existing PDF strip/header dimensions (research D4). Do NOT edit `_FundingAgreementLayout.cshtml` or `Document.cshtml`.
- [ ] T031 [US5] Run `bash scripts/verify-pdf-carveouts.sh` (MUST pass — `.cshtml` byte-identical to main); generate a fixture Funding Agreement PDF and confirm the brand chrome reads official teal while layout/content match a pre-facelift fixture (SC-013).

**Checkpoint**: PDF and UI brand chrome reconverged; carve-out gate green.

---

## Phase 8: User Story 6 - Accessibility & responsiveness hold across the new shell (Priority: P2)

**Goal**: AA contrast (incl. dark sidebar), keyboard-operable kebab, responsive wrap/scroll/scale,
reduced-motion green, snapshots refreshed.

**Independent Test**: axe AA on ≥5 surfaces; keyboard + responsive E2E; reduced-motion green.

- [ ] T032 [P] [US6] Extend `tests/FundingPlatform.Tests.E2E/Brand/AxeContrastTests.cs` to ≥5 surfaces (applicant home, reviewer queue, admin index, login, Users page) and add an assertion that the dark-sidebar light text passes AA on `#12343B` (FR-026/SC-008).
- [ ] T033 [P] [US6] Add a keyboard E2E in `tests/FundingPlatform.Tests.E2E/Brand/KeyboardAccessTests.cs`: the `⋯` kebab (`row-actions-menu-*`) is reachable/operable by keyboard with a visible teal focus ring, and status pills carry icon+text (color not the sole signal) (FR-027/SC-009).
- [ ] T034 [P] [US6] Add responsive E2E checks in `tests/FundingPlatform.Tests.E2E/Brand/ResponsiveLayoutTests.cs`: at a narrow viewport → filters wrap, tables horizontal-scroll, footer image scales, sidebar collapses to the icon-only logo (FR-024/SC-010).
- [ ] T035 [P] [US6] Confirm `tests/FundingPlatform.Tests.E2E/Brand/ReducedMotionTests.cs` stays green; no new motion outside the spec 011/019 catalog (FR-028/SC-011).
- [ ] T036 [US6] Refresh `tests/FundingPlatform.Tests.E2E/Brand/VisualRegressionTests.cs` snapshots for applicant home, reviewer queue, admin index, login, and ADD the Users page (FR-029/SC-012); review diffs.

**Checkpoint**: Accessibility + responsive guarantees verified; snapshots updated.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final gates, budgets, perf, and sign-off.

- [ ] T037 [P] Run `bash scripts/brand-grep-gate.sh` + `bash scripts/tokens-audit.sh` + `bash scripts/verify-tokens.sh`: zero spec-019 legacy hex outside `tokens.css` history; raw hex only in `tokens.css`; yellow non-semantic (SC-001/002).
- [ ] T038 [P] Run `bash scripts/asset-budget-check.sh` (and `verify-asset-budget.sh`) — record total brand-asset wire weight ≤ 400 KB gz (SC-015).
- [ ] T039 [P] Run `node scripts/compare-perf.mjs` vs the T001 baseline — no >10% LCP/TBT regression on applicant home + reviewer queue (NFR-001).
- [ ] T040 Confirm `git diff --stat main -- src/FundingPlatform.Database/` is empty (SC-014).
- [ ] T041 Run the filtered E2E delivery bar green: `dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~Brand|FullyQualifiedName~AdminUser|FullyQualifiedName~UserInvitation|FullyQualifiedName~AdminResetPassword"` (SC-016).
- [ ] T042 Run `specs/037-brand-alignment/quickstart.md` acceptance walk; present the palette + dark sidebar + footer image + Users-page reference treatment for the user sign-off gate (SC-017).
- [ ] T043 [P] Update `CLAUDE.md` Recent Changes + Last-updated; mark spec 037 implemented (post-merge: PR number + squash hash).

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **BLOCKS** all user stories (the token remap is what re-tints everything). T003→T004→T005 are sequential (same file `tokens.css`); T006/T007/T008 are `[P]` (same file `site.css` — serialize edits but independent rules); T009/T010 `[P]` (scripts).
- **US4 (Phase 3)**: depends on Foundational. The official assets + chrome (sidebar header, auth hero, footer image) are the visual anchor the other stories inherit.
- **US1/US2/US3 (Phases 4–6)**: depend on Foundational + US4 chrome. Independently testable per role; can proceed in parallel once US4 lands. US3 adds the kebab/filter-card (admin-only).
- **US5 (Phase 7)**: depends only on Setup assets (independent of the web sweep) — can run any time after T002; grouped P2.
- **US6 (Phase 8)**: depends on US1–US4 (verifies the swept shell) and US3 (kebab keyboard test).
- **Polish (Phase 9)**: depends on all desired stories complete.

### Story independence

- US4 is the shared visual anchor; US1/US2/US3 are per-role sweeps that build on it (acceptable single-pass-sweep coupling, matching the spec's full re-sweep scope).
- US5 (PDF) is fully independent of the web sweep.
- US6 is the cross-cutting verification layer.

### Parallel opportunities

- Phase 2: T006/T007/T008 (site.css rules) conceptually parallel; T009/T010 (scripts) parallel.
- Phase 3: T011/T012/T013/T014 (different files) parallel; T015→T016→T017 sequential (footer → budget → test).
- Phases 4/5 can run in parallel (different role views) once US4 is done.
- Phase 8: T032/T033/T034/T035 parallel (independent test files); T036 after them.
- US5 (Phase 7) can run in parallel with Phases 4–6.

---

## Requirement → task coverage (quick map)

- FR-001…FR-008 (tokens/de-zebra/bridge) → T003, T004, T005
- FR-009 (motion unchanged) → T035
- FR-010…FR-014 (assets/logos/footer/favicon/copyright) → T002, T011–T016
- FR-015 (dark sidebar all roles) → T006, T011
- FR-016 (topbar logout teal) → T007
- FR-017 (page header) → T024, T027
- FR-018 (filter card + Limpiar) → T008, T025
- FR-019 (tables de-zebra + density) → T005, T021
- FR-020 (kebab actions) → T023, T024, T026, T028
- FR-021 (buttons teal) → T003 (bridge), T027
- FR-022 (typography) → T027 (verify)
- FR-023 (PDF re-tint) → T030, T031
- FR-024 (surface sweep) → T018, T021, T026, T027
- FR-025 (per-surface brand E2E) → T017, T019, T022, T029
- FR-026 (axe AA incl sidebar) → T032
- FR-027 (keyboard/focus/status) → T033
- FR-028 (reduced motion) → T035
- FR-029 (visual snapshots) → T036
- FR-030 (grep gate) → T009, T037
- FR-031 (asset budget) → T016, T038
- FR-032/033/034/035 (OOS guardrails) → T040 (schema), T024 (routes), T018 (no copy change), Phase 2 (no deps)
- NFR-001 (perf) → T001, T039
- NFR-003 (yellow decorative / sidebar AA) → T009, T032, T037
- SC-017 (user sign-off) → T042

---

## Implementation strategy

1. **Foundation first** (Phases 1–2): baseline + token remap + shared chrome CSS + gate scripts. After this, the whole app already reads official teal, dark sidebar, de-zebra tables — the single highest-leverage step.
2. **Brand anchor** (US4): real logos + official footer image; budget check.
3. **Per-role sweeps + verify** (US1 → US2 → US3), US3 adding the kebab/filter-card reference treatment.
4. **PDF** (US5) — independent, can slot in parallel.
5. **A11y/responsive/verification** (US6) + **Polish** (Phase 9): gates, budget, perf, full filtered E2E, user sign-off.

Commit after each task or logical group (project convention). Stop at any checkpoint to validate the
slice. Delivery bar: filtered E2E green (SC-016).
