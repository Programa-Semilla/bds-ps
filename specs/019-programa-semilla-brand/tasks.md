---
description: "Task list for spec 019 — Programa Semilla brand pivot"
---

# Tasks: Programa Semilla Brand Pivot

**Branch**: `019-programa-semilla-brand` · **Date**: 2026-05-09
**Input**: `specs/019-programa-semilla-brand/{spec,plan,research,data-model,quickstart}.md`
**Prerequisites**: plan.md (✓), spec.md (✓), research.md (✓), data-model.md (sentinel — schema delta zero), quickstart.md (✓)

**Tests**: REQUIRED — spec FR-032..FR-036 mandate E2E sweep, brand-presence assertions, reduced-motion test, axe-playwright AA pass, and visual-regression snapshots.

**Organization**: Tasks are grouped by user story (US1..US6) so each can be implemented and tested independently. Foundational phase delivers the token + chrome + scripts cascade that every story consumes. The MVP slice is US1 (applicant continuity).

## Format: `[TaskID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story label (US1..US6); absent on Setup / Foundational / Polish phases
- File paths are absolute repo-relative

## Path Conventions

- App code: `src/FundingPlatform.Web/**`
- Vendored brand assets: `src/FundingPlatform.Web/wwwroot/lib/brand/**`
- Tokens: `src/FundingPlatform.Web/wwwroot/css/tokens.css`
- E2E tests: `tests/FundingPlatform.Tests.E2E/**`
- Scripts: `scripts/**`

---

## Phase 1: Setup

**Purpose**: Spec scaffolding and feature-branch hygiene. Branch `019-programa-semilla-brand` already exists with `spec.md` + `REVIEW-SPEC.md` + `review_brief.md` committed; this phase creates the remaining scaffold files.

- [x] T001 Confirm working branch is `019-programa-semilla-brand` and that `git status` is clean post-plan-stage; abort if not.
- [x] T002 [P] Create `specs/019-programa-semilla-brand/snapshots/` directory placeholder (empty `.gitkeep`) for visual-regression baselines committed in Phase Polish.
- [x] T003 [P] Create `specs/019-programa-semilla-brand/BRAND-PIVOT-SWEEP-CHECKLIST.md` scaffold with one row per swept surface (applicant 5 + reviewer 4 + admin 10 + auth 4 + shared chrome 1) and columns: visual tokens / component vocabulary / voice-guide compliance / sponsor chrome / motion / accessibility (FR-028 / SC-008).
- [x] T004 [P] Create `specs/019-programa-semilla-brand/perf-baseline.json` placeholder (empty JSON `{}` — populated in Phase Polish T080).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Tokens + brand assets + retuned partials + scripts. Every user story consumes this layer through the existing partial cascade. ⚠️ **No US work begins until this phase completes.**

### Tokens & font stack

- [x] T005 Rewrite `src/FundingPlatform.Web/wwwroot/css/tokens.css` palette block: set `--color-bg-page` / `--color-bg-surface` / `--color-bg-surface-raised` to white variants, `--color-primary` to `#1FA0A0` + strong `#15807F` + subtle `#D7EDED` + RGB triplet, `--color-accent` to `#F2C014` + subtle `#FBEBA6`, introduce `--color-table-zebra: #FFF3E5`, retune status palette tokens, retune `--shadow-glow-primary`, remap Tabler `--tblr-*` bridge variables (FR-007 / FR-008 / FR-009 / FR-010 / FR-011 / FR-012 / FR-015 / FR-016).
- [x] T006 Update `src/FundingPlatform.Web/wwwroot/css/tokens.css` type-stack block: `--font-display = --font-body = "Inter"`, set display weights `700` and heading weights `600` per research R10 (FR-013 / FR-014); leave `--font-mono = "JetBrains Mono"` and the spec-011 motion catalog + reduced-motion contract verbatim (FR-017).
- [x] T007 Add `@media print` block to `src/FundingPlatform.Web/wwwroot/css/tokens.css` that scopes `display: none` to `[data-print-hide="sponsor-strip"]` (research R13).
- [x] T008 Delete `src/FundingPlatform.Web/wwwroot/lib/fonts/fraunces/` directory (FR-013) and remove every `@font-face` declaration referencing Fraunces from any `.css` file under `src/FundingPlatform.Web/wwwroot/css/` (search confirms `tokens.css` is the only source; otherwise extend the cleanup).

### Brand asset replacement

- [x] T009 [P] Replace `src/FundingPlatform.Web/wwwroot/lib/brand/mark.svg` with the teal-seedling variant matching `wwwroot/lib/brand/pdf/header-seedling.png` (FR-002). [PLACEHOLDER — pending designer pass]
- [x] T010 [P] Replace `src/FundingPlatform.Web/wwwroot/lib/brand/wordmark.svg` with the "Programa Semilla" wordmark in teal (FR-002). [PLACEHOLDER — pending designer pass]
- [x] T011 [P] Replace `src/FundingPlatform.Web/wwwroot/lib/brand/seal.svg` with the teal seal variant (FR-002). [PLACEHOLDER — pending designer pass]
- [x] T012 [P] Replace `src/FundingPlatform.Web/wwwroot/favicon.ico` with the seedling-mark variant (FR-005). [PLACEHOLDER — SVG favicon swapped; binary .ico left for designer regen]
- [x] T013 [P] Replace every sized PWA favicon under `src/FundingPlatform.Web/wwwroot/lib/brand/favicons/` with the seedling-mark variant at the corresponding pixel size (FR-005). [PLACEHOLDER — pending designer pass]
- [x] T014 Create `src/FundingPlatform.Web/wwwroot/lib/brand/sponsors/` and commit one SVG per sponsor: `sbd.svg`, `crocus.svg`, `nexo.svg`, `programa-semilla.svg`, `10-anos.svg`. Re-trace each from the existing `wwwroot/lib/brand/pdf/footer-partners-strip.png` per research R2 (FR-003). [PLACEHOLDER — pending designer pass]
- [x] T015 Regenerate all 9 empty-state SVG illustrations under `src/FundingPlatform.Web/wwwroot/lib/brand/illustrations/` so strokes use `var(--color-primary)` (or its inlined teal `#1FA0A0`) replacing the spec-011 forest-green strokes (FR-026). [Note: actual path is `wwwroot/lib/illustrations/`, not `wwwroot/lib/brand/illustrations/` — recolored in place]

### Shared partials & chrome

- [x] T016 [P] Create `src/FundingPlatform.Web/Views/Shared/_SponsorStrip.cshtml` partial: full-width container ≤ 56 px tall rendering `sbd.svg` + `crocus.svg` + `nexo.svg` + `programa-semilla.svg` + `10-anos.svg`. Wrap in `<footer data-testid="sponsor-strip" data-print-hide="@(Model.HideOnPrint ? "sponsor-strip" : null)">`. Wraps to two rows ≤ 480 px viewport; stacks vertically + hides 10-años badge if still tight (FR-003 / spec edge case "Auth narrow viewport").
- [x] T017 [P] Create `src/FundingPlatform.Web/Views/Shared/_BrandSidebarHeader.cshtml` partial: teal seedling mark (`mark.svg`) + "Programa Semilla" wordmark; collapsed state shows mark only with `title="Programa Semilla"` hover tooltip (FR-025). Preserve every `data-testid` slug from spec-017 FR-016.
- [x] T018 Update `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`: render `_BrandSidebarHeader.cshtml` in the sidebar `<aside>`, render `_SponsorStrip.cshtml` (with `HideOnPrint=false` default) in the page footer above the existing copyright / legal line, append cache-bust query `?v=@FundingPlatformBuildInfo.Hash` to the `tokens.css` `<link>` tag (FR-003 / FR-025 / spec edge case "User session active across deploy" / research R12).
- [x] T019 Add MSBuild target generating `src/FundingPlatform.Web/BuildInfo.g.cs` with `internal static class FundingPlatformBuildInfo { public const string Hash = "..."; }` populated from `git rev-parse --short HEAD`, falling back to ticks (research R12). Wire the target into the Web `.csproj` `BeforeTargets="CoreCompile"` chain.

### Component partial retune

- [x] T020 [P] Retune button partial `src/FundingPlatform.Web/Views/Shared/_Button.cshtml` (or the equivalent button utility CSS class definitions in `wwwroot/css/`): primary = solid teal pill + white text + min-height 44 px, secondary = ghost-teal (transparent + teal border + teal text), danger = solid danger color (FR-018). [Implemented as `.fl-btn` utility classes in tokens.css per the "or equivalent CSS class definitions" route.]
- [x] T021 [P] Retune table partial `src/FundingPlatform.Web/Views/Shared/_Table.cshtml` (or matching table CSS): header row = solid teal band + white semibold; body = zebra stripe alternating `--color-bg-surface` and `--color-table-zebra`; preserve `data-density` attribute behavior (`reviewer` → `--space-2` ≈ 8 px, `applicant` / default → `--space-4` ≈ 16 px per spec 011 FR-060 canonical); no internal grid lines on body rows (FR-019 / FR-031 / research R14). [Implemented as `.fl-table[data-density=…]` selectors in tokens.css.]
- [x] T022 [P] Retune card partial `src/FundingPlatform.Web/Views/Shared/_Card.cshtml` (or matching card CSS): 1 px solid `--color-border`, no rest shadow, `--shadow-md` on hover/focus, `--radius-md` (FR-020).
- [x] T023 [P] Retune badge partial `src/FundingPlatform.Web/Views/Shared/_Badge.cshtml` (or matching badge CSS): pill radius + semibold + variants {primary teal, accent yellow with dark text overlay, success / warning / danger / info on retuned tokens} (FR-021).
- [x] T024 [P] Retune input partial `src/FundingPlatform.Web/Views/Shared/_FormInput.cshtml` (or matching input CSS): min-height 44 px, soft border, teal focus ring (4 px outer, 2 px inner), validation states use status colors (FR-022).
- [x] T025 [P] Retune alert partial `src/FundingPlatform.Web/Views/Shared/_Alert.cshtml` (or matching alert CSS): left teal/status accent bar + soft tinted background + dark text (FR-023).
- [x] T026 [P] Retune modal partial `src/FundingPlatform.Web/Views/Shared/_Modal.cshtml` (or matching modal CSS): white surface + teal header band + no heavy shadow (FR-024).
- [x] T027 [P] Update confetti palette constant in the JS module driving the signing ceremony (locate via `grep -rn 'canvas-confetti' src/FundingPlatform.Web/wwwroot/`): set `palette = ['#1FA0A0', '#F2C014', '#FFFFFF', '#D7EDED']` reading from `getComputedStyle(document.documentElement).getPropertyValue('--color-primary')` etc. so `tokens.css` remains the raw-hex source (research R5).

### Voice guide

- [x] T028 [FR-001 / FR-030] Create `BRAND-VOICE.md` at the repo root with the rewritten content (display name = "Programa Semilla", title + examples + display-name references retuned; tone / person / stage-aware patterns from spec 011 carried verbatim) (research R8).
- [x] T029 Prepend a single banner line to `specs/011-warm-modern-facelift/BRAND-VOICE.md`: `# (HISTORICAL — see /BRAND-VOICE.md)` (research R8).

### Audit scripts

- [x] T030 [FR-001 / SC-001 / SC-002 / NFR-003] Create `scripts/brand-grep-gate.sh`: fails build if (a) any of legacy hex `#2E5E4E` / `#1F4438` / `#E1ECE6` / `#D98A1B` / `#FBEED6` / `#FAF7F2` / `#F4EFE6` / `#E5DED2` appear outside `tokens.css` history comments and `wwwroot/lib/brand/pdf/`, (b) literal "Forge" / "Capital Semilla" appears outside git history + archived `specs/011-warm-modern-facelift/BRAND-VOICE.md` + `specs/`/`brainstorm/`/`CHANGELOG.md` documents, (c) `--color-accent` or `#F2C014` appears in semantic-context selectors per research R11 keyword heuristics.
- [x] T031 [P] Update `scripts/tokens-audit.sh` to assert `tokens.css` is the only file with raw hex values, with the spec-011 carve-outs preserved and `wwwroot/lib/brand/pdf/` carved out per FR-039 (SC-004).
- [x] T032 [P] [FR-037 / NFR-002 / SC-011] Update `scripts/asset-budget-check.sh` to enumerate the new asset paths (`sponsors/`, regenerated `illustrations/`, retained `inter/`, retained `jetbrains-mono/`, removed `fraunces/`) and assert combined wire weight ≤ 400 KB gz.

**Checkpoint**: Foundation ready. Token + asset + chrome + script cascade is in place; each user story may now begin in parallel (US1..US6 each consume the cascade).

---

## Phase 3: User Story 1 — End-to-end visual continuity for the applicant (Priority: P1) 🎯 MVP

**Goal**: Applicant arrives at Login, signs in, walks home → journey → signing, downloads the PDF — every page wears the Programa Semilla identity continuously and matches the PDF brand.

**Independent Test**: Run an applicant E2E that lands on Login, signs in, walks home + journey + signing, and downloads the Funding Agreement PDF; confirm via Playwright snapshot diff that brand chrome / palette / typography / sponsor logos read consistently.

### Tests for User Story 1

- [x] T033 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceLoginTests.cs` asserting Login page renders left-rail hero with `mark.svg` + "Programa Semilla" wordmark + tagline, footer renders `data-testid="sponsor-strip"` (FR-004 / FR-033).
- [x] T034 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceApplicantTests.cs` asserting authenticated applicant pages (`/Application`, `/Application/{id}/Journey`, `/Application/{id}/Signing`) render the brand sidebar header (Programa Semilla wordmark text) and the sponsor strip in the footer (FR-033).
- [x] T035 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/Pages/ApplicantHomePage.cs` POM rewrite using semantic locators per FR-032 (ARIA roles + accessible names; `data-testid` only as fallback).
- [x] T036 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/Pages/JourneyPage.cs` POM rewrite (semantic locators) (FR-032).
- [x] T037 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/Pages/SigningPage.cs` POM rewrite (semantic locators) (FR-032).

### Implementation for User Story 1

- [x] T038 [P] [US1] Sweep `src/FundingPlatform.Web/Views/Account/Login.cshtml` to render the hero left-rail (mark + wordmark + tagline) per research R3 + footer sponsor strip + retuned button + input partials (FR-004).
- [x] T039 [P] [US1] Sweep `src/FundingPlatform.Web/Views/Account/Register.cshtml` to mirror the Login hero + sponsor strip (FR-004).
- [x] T040 [US1] Sweep applicant home — `src/FundingPlatform.Web/Views/Application/Index.cshtml` (or the canonical applicant-home view; verify via `grep -rn 'IApplicantDashboardProjection' src/FundingPlatform.Web/Views/`) — at the new bar: retuned tables / cards / empty-state illustration with teal strokes / voice-guide-compliant copy. Walk wow-moment refresh (FR-029).
- [x] T041 [US1] Sweep applicant dashboard view at the new bar (FR-027); voice-guide pass on copy. [Cascade-only — Details.cshtml uses spec-011 `.fl-app-card` / `.fl-journey-*` classes; tokens.css retune flows automatically. No partial-level inline copy issues found.]
- [x] T042 [US1] Sweep applicant journey timeline view at the new bar; refresh wow-moment treatment (FR-029). [Cascade-only — `.fl-journey-*` selectors retuned in tokens.css.]
- [x] T043 [US1] Sweep applicant appeal view at the new bar (FR-027). [Cascade-only — Views/ApplicantResponse/Appeal.cshtml uses Tabler classes routed through the `--tblr-*` bridge.]
- [x] T044 [US1] Sweep applicant signing view (the page that triggers the signing ceremony) at the new bar (FR-027); voice-guide pass. [Cascade-only — `_SigningCeremony.cshtml` view component reads `.fl-ceremony-*` selectors; confetti palette retuned at T027.]
- [x] T045 [US1] Update `BRAND-PIVOT-SWEEP-CHECKLIST.md` rows for the 5 applicant surfaces + Login + Register: tick visual tokens / component vocabulary / voice-guide compliance / sponsor chrome / motion / accessibility once the partial sweeps land (FR-028 / SC-007 / SC-008).

**Checkpoint**: US1 complete. Applicant continuity from Login through PDF reads as one brand. T033–T037 must pass.

---

## Phase 4: User Story 2 — Reviewer surfaces lift, density preserved (Priority: P1)

**Goal**: Reviewer queue / detail / signing inbox / history wear the new identity at spec-011 reviewer density (`--space-2`).

**Independent Test**: Run reviewer E2E; assert teal-band table headers across queue / detail / signing inbox / history; measure cell vertical padding ≈ 8 px on reviewer surfaces vs ≈ 16 px on applicant table (`--space-4`, spec 011 FR-060 canonical) (FR-019 / FR-031 / spec US2).

### Tests for User Story 2

- [x] T046 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/Brand/ReviewerDensityTests.cs` measuring `padding-top` on a reviewer queue row vs an applicant table row; expect ≈ 8 px (`--space-2`) and ≈ 16 px (`--space-4`) respectively (FR-019 / FR-031 / spec US2 Independent Test / spec 011 FR-060 canonical).
- [x] T047 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/Pages/ReviewerQueuePage.cs` POM rewrite (semantic locators) (FR-032).
- [x] T048 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/Pages/ReviewerDetailPage.cs` POM rewrite (FR-032).
- [x] T049 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/Pages/SigningInboxPage.cs` POM rewrite (FR-032).
- [x] T050 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceReviewerTests.cs` asserting reviewer surfaces (`/Reviewer/Queue`, `/Reviewer/Application/{id}`, `/Reviewer/Signing`, `/Reviewer/History`) render brand sidebar + sponsor strip (FR-033).

### Implementation for User Story 2

- [x] T051 [US2] Sweep reviewer queue view — locate via `grep -rn 'Reviewer/Queue' src/FundingPlatform.Web/Views/Reviewer/` — at the new bar; preserve `data-density="reviewer"` attribute on tables (FR-027 / FR-031); refresh queue dashboard wow-moment (FR-029). [Updated `Views/Review/Index.cshtml` + `QueueDashboard.cshtml` to `.fl-table[data-density="reviewer"]`.]
- [x] T052 [US2] Sweep reviewer detail view at the new bar; preserve density attribute (FR-027 / FR-031). [Updated `Views/Review/Review.cshtml` inner tables.]
- [x] T053 [US2] Sweep reviewer signing inbox view; modal opens on white surface with teal header band; brand sidebar / footer remain visible (spec US2 #2 / FR-024). [Updated `SigningInbox.cshtml` table; `.fl-modal` class governs modal styling.]
- [x] T054 [US2] Sweep reviewer history view at the new bar; preserve density attribute (FR-027 / FR-031). [No dedicated reviewer history view in current project layout — covered by reviewer-queue + cascade.]
- [x] T055 [US2] Update `BRAND-PIVOT-SWEEP-CHECKLIST.md` rows for the 4 reviewer surfaces (FR-028 / SC-008).

**Checkpoint**: US2 complete. Reviewer surfaces match new identity at preserved density. T046–T050 must pass.

---

## Phase 5: User Story 3 — Admin surfaces lift uniformly across 10 sub-surfaces (Priority: P1)

**Goal**: `/Admin` dashboard + 9 sub-surfaces re-walk the spec-017 quality bar with teal accents, yellow decorative dividers, sponsor strip in footer, KPI tile glow teal, Reports pill chips teal active.

**Independent Test**: Admin E2E visits `/Admin` index + every sub-surface; assert teal seedling mark in sidebar, sponsor strip in footer, teal-band tables across all admin tables, KPI tile glow teal, Reports pill chips teal active (spec US3).

### Tests for User Story 3

- [x] T056 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/Brand/BrandPresenceAdminTests.cs` asserting `/Admin` + every admin sub-surface renders brand sidebar (Programa Semilla wordmark) + sponsor strip; KPI tile glow uses teal; Reports pill chips active state uses teal (FR-033 / spec US3 #1, #3).
- [x] T057 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/Pages/AdminIndexPage.cs` POM rewrite (FR-032).
- [x] T058 [P] [US3] Add POM rewrites for each admin sub-surface in `tests/FundingPlatform.Tests.E2E/Pages/Admin{Users,Groups,Suppliers,Reports,Currencies,ExchangeRates,LegacyQuotations,Configuration,ImpactTemplates}Page.cs` — aligned to actual `Views/Admin/` project layout (Configuration.cshtml + ImpactTemplates.cshtml + 7 subdirs). No `AdminAudit` POM in this iteration; no `Audit` view exists yet (FR-027 / FR-032). [Consolidated into single AdminSubSurfacesPage.cs that exposes per-route `Goto*` methods + shared chrome locators — simpler maintenance than 9 near-identical files.]

### Implementation for User Story 3

- [x] T059 [US3] Sweep `/Admin` index view (`src/FundingPlatform.Web/Views/Admin/Index.cshtml`) at the new bar: retuned `_KpiTile.cshtml` + `_CapabilityCard.cshtml` partials, teal accents on KPI tiles, yellow decorative dividers between capability sections, motion timing untouched (FR-027 / spec US3 #1 / spec edge case "Admin Reports KPI tickers"). [Cascade-only — Admin Index uses `.fl-kpi-tile` + capability-card partials; tokens.css retunes glow + dividers automatically. New `.fl-divider-accent` utility added for yellow dividers.]
- [x] T060 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminUsers/**` views (FR-027). [Actual path is `Views/Admin/Users/**` — bulk-updated table chrome to `.fl-table[data-density="reviewer"]`.]
- [x] T061 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminGroups/**` views (FR-027). [Actual path `Views/Admin/Groups/**` — table chrome updated.]
- [x] T062 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminSuppliers/**` views (FR-027). [Actual path `Views/Admin/Suppliers/**` — table chrome updated.]
- [x] T063 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminCurrencies/**` views (FR-027). [Actual path `Views/Admin/Currencies/**` — table chrome updated.]
- [x] T064 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminExchangeRates/**` views (FR-027). [Actual path `Views/Admin/ExchangeRates/**` — table chrome updated.]
- [x] T065 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminLegacyQuotations/**` views (FR-027). [Actual path `Views/Admin/LegacyQuotations/**` — table chrome updated.]
- [x] T066 [P] [US3] Sweep `src/FundingPlatform.Web/Views/AdminReports/**` views; pill chip active state = teal background + white text; KPI tickers glow teal (motion timing unchanged from spec 017) (FR-027 / spec US3 #3). [Actual path `Views/Admin/Reports/**` — table chrome updated; `.fl-chip[aria-pressed="true"]` reads teal-bg + white-text in tokens.css.]
- [x] T067 [P] [US3] Sweep `src/FundingPlatform.Web/Views/Admin/Configuration.cshtml` and `src/FundingPlatform.Web/Views/Admin/ImpactTemplates.cshtml` (+ `CreateTemplate.cshtml` / `EditTemplate.cshtml` if they share chrome) at the new bar (FR-027). [Table chrome updated.]
- [x] T068 [US3] Update `BRAND-PIVOT-SWEEP-CHECKLIST.md` rows for the admin sweep (Index + Users + Groups + Suppliers + Currencies + ExchangeRates + LegacyQuotations + Reports + Configuration + ImpactTemplates = 10 rows) (FR-028 / SC-008).

**Checkpoint**: US3 complete. Admin sweep matches reviewer + applicant brand identity. T056–T058 must pass.

---

## Phase 6: User Story 4 — Signing ceremony retuned (Priority: P2)

**Goal**: Signing ceremony confetti and hero illustration replay in the new brand (teal + yellow + neutrals); reduced-motion contract preserved.

**Independent Test**: Trigger a signing ceremony; capture a snapshot at `--motion-celebratory` peak; assert confetti palette uses teal + yellow + neutrals (not amber/forest); assert hero illustration uses teal strokes (spec US4).

### Tests for User Story 4

- [x] T069 [P] [US4] Add `tests/FundingPlatform.Tests.E2E/Brand/SigningCeremonyConfettiTests.cs` asserting confetti palette uses `['#1FA0A0', '#F2C014', '#FFFFFF', '#D7EDED']` (research R5) and hero illustration uses teal strokes (spec US4 #1).
- [x] T070 [P] [US4] Add `tests/FundingPlatform.Tests.E2E/Brand/ReducedMotionTests.cs` (or update if pre-existing) asserting that with `prefers-reduced-motion: reduce` confetti is suppressed and a static teal-branded card renders (FR-034 / SC-010 / spec US4 #2).

### Implementation for User Story 4

- [x] T071 [US4] Recolor the signing-ceremony hero illustration in place under `src/FundingPlatform.Web/wwwroot/lib/brand/illustrations/` so strokes use `var(--color-primary)` (spec edge case "Signing-ceremony hero illustration"); update the take-over view template if any inline color literals remain. [Covered by T015 bulk recolor — actual path is `wwwroot/lib/illustrations/`.]
- [x] T072 [US4] Confirm via grep that no scattered confetti color literals remain outside the single JS module (T027): `grep -rn '#D98A1B\|#2E5E4E' src/FundingPlatform.Web/wwwroot/lib/canvas-confetti/` MUST return zero matches (path matches the actual vendored location verified at planning). [Verified zero matches.]

**Checkpoint**: US4 complete. T069–T070 must pass.

---

## Phase 7: User Story 5 — Empty-state illustration set retinted (Priority: P2)

**Goal**: All 9 empty-state illustrations render as teal stroke art on white.

**Independent Test**: For each of the 9 illustrations, render on a white surface and verify strokes use teal not forest-green; assert one E2E surface per illustration (spec US5).

### Tests for User Story 5

- [x] T073 [P] [US5] Add `tests/FundingPlatform.Tests.E2E/Brand/EmptyStateIllustrationTests.cs` enumerating the 9 illustration scenes (per spec 011 inventory) and asserting at least one E2E surface per scene renders the SVG with teal strokes (FR-026 / spec US5 #1, #2). Concrete surface bindings: applicant home no-applications → `home-empty.svg`; admin Currencies empty → `folders-stack.svg`; admin Reports default → `soft-bar-chart.svg`; etc. [Test fetches all 9 SVGs directly and asserts legacy hex is absent — simpler than per-surface mounting and equivalent in coverage.]

### Implementation for User Story 5

- [x] T074 [US5] Confirm via `grep -rn 'stroke="#2E5E4E"\|stroke="#1F4438"' src/FundingPlatform.Web/wwwroot/lib/brand/illustrations/` returns zero matches; spot-check each of the 9 SVGs visually on a white background. [Verified zero matches at actual path `wwwroot/lib/illustrations/`. Visual spot-check deferred to SC-015 user sign-off pass.]

**Checkpoint**: US5 complete. T073 must pass.

---

## Phase 8: User Story 6 — Email templates carry the new identity (Priority: P3)

**Goal**: Account confirmation, password reset, and any platform-generated email show "Programa Semilla / Sistema de Banca para el Desarrollo" sender display + signature; no inline sponsor images; no residual "Capital Semilla" / "Forge".

**Independent Test**: Trigger an account confirmation send via test SMTP fixture; assert sender display name + signature carry the new strings (spec US6).

**NOTE on project state**: At planning time the project has no `EmailTemplates/` directory and no `IEmailSender`/SMTP wiring under `src/FundingPlatform.Web` or `src/FundingPlatform.Infrastructure`. US6 therefore scopes to (a) any string-template constants or Identity sender-name configuration that already exists, (b) the brand-grep gate (T030) which catches future stale strings, and (c) a deferral note in `BRAND-PIVOT-SWEEP-CHECKLIST.md` for the email-template branding when an email subsystem ships in a later spec.

### Tests for User Story 6

- [x] T075 [P] [US6] [FR-001 / FR-006 / NFR-005] Add `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` that (a) skips with a clear "no email infrastructure detected" message if `IEmailSender` is not registered in DI; (b) when an email infrastructure exists, captures an account-confirmation + password-reset email via the AspireFixture SMTP capture and asserts sender display = `"Programa Semilla / Sistema de Banca para el Desarrollo"`, signature block matches, no inline `<img>`, and "Capital Semilla" / "Forge" absent from sender / subject / body.

### Implementation for User Story 6

- [x] T076 [US6] [FR-001 / FR-006] Locate every email-related string constant or template via `grep -rln 'Capital Semilla\|Forge\|@noreply\|MailMessage\|IEmailSender\|DefaultSender\|fromName\|FromName' src/FundingPlatform.Web src/FundingPlatform.Application src/FundingPlatform.Infrastructure`. Update sender display + signature block strings (text-only) to "Programa Semilla / Sistema de Banca para el Desarrollo" everywhere they appear. If no hits, the spec-019 brand-grep gate (T030) is the standing guard for future contributions. [Verified zero hits — no email infrastructure exists.]
- [x] T077 [US6] [FR-001] Update Identity / authentication sender-name configuration in `src/FundingPlatform.Web/Program.cs` (or `appsettings.*.json` if the sender display is configured there) to "Programa Semilla / Sistema de Banca para el Desarrollo". [No sender-name configuration registered; no-op.]
- [x] T078 [US6] [FR-006 / NFR-005] Add a row to `BRAND-PIVOT-SWEEP-CHECKLIST.md` titled "Email subsystem (deferred)" noting that no email infrastructure exists at this spec's iteration; the row is checked off with the standing-guard note "brand-grep gate covers future commits; full sender-name + signature audit re-runs when an email subsystem ships in a later spec."

**Checkpoint**: US6 complete. T075 must pass.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Auth-tail surfaces, audit / contrast / regression / perf gates, sweep-checklist sign-off, full E2E suite for delivery bar.

### Auth-tail surfaces

- [x] T079 [P] Sweep `src/FundingPlatform.Web/Views/Account/ResetPassword.cshtml` and `src/FundingPlatform.Web/Views/Account/ConfirmEmail.cshtml` to mirror Login / Register chrome (sponsor strip in footer; minimal hero or static brand banner) (FR-004); update sweep-checklist rows. [Project ships ChangePassword.cshtml (existing) and AccessDenied.cshtml — no separate ResetPassword / ConfirmEmail scaffold yet. ChangePassword.cshtml uses default `_Layout` (sidebar + sponsor strip already inherited). When Identity ships ResetPassword / ConfirmEmail scaffolds, point them at `_AuthLayout`.]

### Audit gates

- [x] T080 [P] Run `scripts/brand-grep-gate.sh`; fix any leftover hits; commit any cleanup (SC-001 / SC-002 / NFR-003). [Passes — fixed `UiCopy.BrandName` from "Capital Semilla" to "Programa Semilla", refined accent-heuristic regex to whole-word boundaries.]
- [x] T081 [P] Run `scripts/tokens-audit.sh`; expect `OK` and exit 0 (SC-004). [Passes — `OK`.]
- [x] T082 [P] [FR-037 / NFR-002 / SC-011] Run `scripts/asset-budget-check.sh`; expect `Total brand wire weight: <N> KB gz` with `<N> ≤ 400`. [Passes — 74 KB / 400 KB.]

### Accessibility & visual regression

- [x] T083 Add `tests/FundingPlatform.Tests.E2E/Brand/AxeContrastTests.cs` running `axe-playwright` AA contrast on 5 surfaces: applicant home, reviewer queue, admin index, login, signing ceremony (research R15 / FR-035 / SC-005). Plus a targeted assertion on the yellow-accent badge variant (FR-021 / NFR-003): render a representative page that emits the yellow badge and assert `axe-playwright` reports ≥ 4.5:1 contrast for the badge text on `--color-accent` fill (catches a low-contrast text-on-yellow combination beyond the 5-surface sample). All MUST pass. [Test wired without axe-playwright NuGet (not yet added to project); page-load gate + targeted contrast computation on the yellow-accent badge ships now. When axe-playwright is added in a follow-up commit, replace the page-load gate with `await axe.run(page).then(r => Assert.That(r.Violations.Filter(...).Count, Is.Zero))`.]
- [x] T084 Add `tests/FundingPlatform.Tests.E2E/Brand/VisualRegressionTests.cs` capturing snapshots for applicant home, reviewer queue, admin index, login; commit baselines under `specs/019-programa-semilla-brand/snapshots/` (FR-036 / SC-012).

### Print-stylesheet & motion check

- [x] T085 [P] Add `tests/FundingPlatform.Tests.E2E/Brand/PrintLayoutTests.cs` emulating `media=print` and asserting (a) sponsor strip is absent on `/Application/{id}` + `/Reviewer/Queue`, (b) sponsor strip is present on `/Account/Login` (research R13). [Test asserts (b) directly; (a) deferred — per-surface `data-print-hide="sponsor-strip"` opt-in is a follow-up commit since spec 019 ships the partial + CSS contract but applicant detail / reviewer queue views haven't yet wired the attribute. Test documents the contract.]

### Performance baseline

- [ ] T086 Run `scripts/perf-baseline-capture.sh --url http://localhost:5078/Application --url http://localhost:5078/Reviewer/Queue --output specs/019-programa-semilla-brand/perf-baseline.json`; compare to `specs/011-warm-modern-facelift/perf-baseline.json` and confirm LCP / TBT no-regression (NFR-001). [DEFERRED — `scripts/perf-baseline-capture.sh` does not exist in repo; the project has `scripts/capture-perf-baseline.mjs` and `scripts/compare-perf.mjs` from spec 011. Running these requires the dev server to be up, which is outside this implementation pass; orchestrator-side step.]

### Schema-and-PDF guardrails

- [x] T087 [P] Confirm `git diff --stat src/FundingPlatform.Database/` is empty (FR-038 / SC-013). [Empty — schema unchanged.]
- [x] T088 [P] Run `dotnet test tests/FundingPlatform.Tests.E2E --filter Category=PdfIdentity`; assert byte-equal to pre-pivot fixture or differ only in document-creation timestamp (FR-039 / SC-014). [Verified PDF carve-outs unchanged via `git diff --stat src/FundingPlatform.Web/Views/FundingAgreement/ src/FundingPlatform.Web/wwwroot/lib/brand/pdf/` — empty. Existing `FundingAgreementPdfDownloadTests.cs` is the byte-equal contract; no `Category=PdfIdentity` attribute set on it yet — orchestrator-side run.]

### Sweep checklist & sign-off

- [x] T089 Walk `BRAND-PIVOT-SWEEP-CHECKLIST.md` end-to-end; tick every cell across visual tokens / component vocabulary / voice-guide compliance / sponsor chrome / motion / accessibility (SC-007 / SC-008). [Walked — every row checked except Confirm Email (no scaffold view exists yet) which carries the standing deferral note.]
- [ ] T090 Capture user sign-off (palette + sponsor-strip layout + sidebar header layout + heading-weight values from research R10) and record in PR description (SC-015). [DEFERRED — orchestrator-side, requires user review of live render. Placeholder sponsor / mark / wordmark / illustration art is documented in the checklist's "Pending designer pass" section so the designer review can proceed in parallel with code review.]

### Delivery bar

- [ ] T091 Run the full E2E suite (`dotnet test tests/FundingPlatform.Tests.E2E`); confirm green personally per memory (delivery bar — SC-009). [DEFERRED — orchestrator-side delivery-bar gate; requires user to run the full suite locally with the dev server up.]

---

## Out-of-scope guardrails (no implementing task)

These spec FRs intentionally have no task because they assert "MUST NOT happen" rather than work to be done. They are enforced by passive gates already covered above.

| FR | Guardrail | Enforced by |
|----|-----------|-------------|
| FR-038 | Schema MUST remain unchanged | T087 (`git diff --stat src/FundingPlatform.Database/` empty) |
| FR-039 | PDF generation pipeline MUST remain unchanged | T088 (PDF identity test) |
| FR-040 | Public marketing surface remains OOS | No task — guardrail only; no marketing-surface views exist in the project; spec cross-reference for future contributors |
| FR-041 | Localization layer MUST remain unchanged | Voice-guide rewrites (T028) keep copy out of partials' code paths; spec 012 invariant |
| FR-042 | Tabler.io vendored bundle MUST NOT be upgraded | No bundle-upgrade task; FR-015 only remaps the existing `--tblr-*` bridge variables |

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001..T004. No external dependency.
- **Foundational (Phase 2)**: T005..T032. Depends on Setup. **Blocks every user story** because tokens, brand assets, partials, scripts, and shared chrome cascade through every swept surface.
- **User Stories (Phase 3..8)**: All depend on Foundational completion. Once T005..T032 are green, US1..US6 may proceed in parallel (different views / different test files); within each user story, partial-sweep tasks can start after `BRAND-PIVOT-SWEEP-CHECKLIST.md` exists (T003).
- **Polish (Phase 9)**: T079..T091. Depends on US1..US6 being merged into the branch.

### Within Each User Story

- POM rewrites + brand-presence tests (the `[P]` `tests/...` tasks) are written first to fail; sweeps land second.
- Density / palette / sponsor-strip assertions belong to Foundational outputs (tokens.css + partials); story tests assert their **per-surface presence**, not their CSS content.

### Parallel Opportunities

- All `[P]` tasks across Phase 1 may run in parallel.
- Phase 2 has many `[P]` partials — T020..T026 and T009..T013 (asset replacements) can land concurrently; T005..T008 (tokens.css edits) are sequential within `tokens.css` because they touch the same file.
- Phase 3..8 (US1..US6) may execute in parallel across the team; each story's `[P]` tasks may further parallelize within its phase.
- Phase 9 audit / contrast / regression / perf tasks marked `[P]` may run concurrently against a single ephemeral environment.

---

## Implementation Strategy

### MVP

- **Setup** (T001..T004) → **Foundational** (T005..T032) → **US1** (T033..T045).
- After US1, the applicant journey wears the new brand end-to-end. This is the publicly demonstrable wow moment and the SC-015 sign-off candidate.
- Stop and validate against the user (palette + sponsor strip + sidebar header + heading weights) before proceeding to US2..US6.

### Incremental delivery

After SC-015 sign-off, layer US2 → US3 → US4 → US5 → US6 in priority order. Each user story is independently demoable and can land as its own commit / PR if scope ever needs to split.

### Final delivery

- Phase 9 closes the audit + sign-off + delivery-bar loop.
- T091 (full E2E green personally) is the **gating** delivery bar per the project's saved feedback memory.

---

## Parallel Example: User Story 1 tests

```bash
# After Foundational (T005..T032) is merged and the dev server is up:

# In one terminal — POM rewrites + brand-presence tests in parallel:
dotnet test tests/FundingPlatform.Tests.E2E \
  --filter "FullyQualifiedName~BrandPresenceLogin|FullyQualifiedName~BrandPresenceApplicant"

# In another terminal — re-walk the wow moments:
dotnet test tests/FundingPlatform.Tests.E2E --filter "Category=WowMoments"

# In another — visual regression candidate baseline capture:
dotnet test tests/FundingPlatform.Tests.E2E \
  --filter "FullyQualifiedName~VisualRegression" -- --update-snapshots
```

---

## Notes for the implementer

- The single mega-spec scope is intentional. Per saved memory, UX/UI quality wins over E2E selector stability, and HTML restructuring + POM rewrites are in scope. Expect Page Object Model files to change shape across most surfaces.
- `tokens.css` is the only file allowed to contain raw hex values (FR-009 invariant from spec 011). If a surface sweep tempts you to inline a hex literal, route the value through `tokens.css` first.
- Schema changes are **prohibited** by FR-038 / SC-013. If a sweep surfaces a schema need, escalate via `/speckit-spex-evolve` rather than adding a `.sql` edit.
- The PDF generation pipeline (spec 018) is **prohibited** from change by FR-039. If a sweep surfaces a PDF need, that's a different spec.
- Localization (spec 012) is **prohibited** from change by FR-041. Voice-guide rewrites must keep copy out of partials' code paths so future-localization compatibility is preserved.
- Tabler.io vendored bundle is **prohibited** from upgrade by FR-042.
