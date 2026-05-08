# Feature Specification: Admin UX/UI Facelift — Capability-Complete Dashboard + Sub-Surface Sweep

**Feature Branch**: `017-admin-ux-facelift`
**Created**: 2026-05-08
**Status**: Draft
**Input**: User description: "Admin UX/UI facelift — capability-complete dashboard at /Admin (action KPIs + grouped capability cards), warm-modern sweep across all admin sub-surfaces, illustration-backed empty states everywhere, sidebar grouping, route normalization, refreshed Reports tab UX. Schema unchanged. PDF carve-outs preserved."

## Overview

After spec 009 admin area shipped (Users + Reports stub), three more admin capabilities landed — spec 010 Reports content, spec 015 multi-currency (Currencies + Exchange Rates + Legacy Quotations queue), spec 016 Groups — each adding sidebar entries and routes but never updating the `/Admin` landing page. Today the landing page is a 3-card grid (Impact Templates / System Configuration / Suppliers) frozen pre-warm-modern; admins discover the rest via sidebar-spelunking. Spec 011 facelift sweep listed Admin in its inventory but didn't reach the new specs' surfaces and didn't lift the landing page itself into a wow moment.

This spec elevates the entire admin area to spec 011 quality — capability-complete dashboard at `/Admin` (action KPIs + grouped capability cards), warm-modern sweep across all admin sub-surfaces, illustration-backed empty states everywhere, sidebar grouping, route normalization, refreshed Reports tab UX. Schema unchanged. PDF carve-outs preserved. Display brand and tokens from spec 011 are consumed as-is — this spec adds no new fonts, no new illustrations, no new motion catalog entries.

**Three baked-in scope calls**: (a) Schema is locked closed (per FR-027); KPIs and projections are query-time. (b) No new admin capabilities — pure facelift + capability-completeness. (c) Old admin routes (`/Admin/AdminCurrencies`, `/Admin/AdminExchangeRates`, `/Admin/AdminLegacyQuotations`) get hard-removed without a redirect shim — pre-prod, no real bookmarks.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin home dashboard: capability map + action KPIs (Priority: P1)

As an admin signing in to the platform, I want to land on a dashboard that immediately tells me what needs my attention and exposes every admin capability in one scan, so that I don't discover features by sidebar-spelunking.

**Why this priority**: Primary unblock. Today admins discover non-card capabilities (Users / Groups / Reports / Currencies / Exchange Rates / Legacy Quotations) only via the sidebar; new admin onboarding suffers and existing admins miss surfaces they'd otherwise use. The dashboard is the single most visible upgrade for admin operators and the spec's "wow moment" peer to spec 011's applicant home and reviewer queue dashboards.

**Independent Test**: Log in as Admin, land on `/Admin`. Confirm all 4 KPIs render with correct counts against a seeded fixture; click each KPI and confirm correct deep-link to a filtered surface. Confirm all 9 capability cards are visible above-fold (or with one scroll on a 1366×768 viewport) and each navigates to a 200 OK surface. Reload with `prefers-reduced-motion` and confirm KPI tickers render final values immediately.

**Acceptance Scenarios**:

1. **Given** an Admin lands on `/Admin`, **When** the page renders, **Then** it shows: page header, 4 KPI tiles (Pending suppliers / Pending legacy quotations / Aging applications / Active users), 3 capability sections (Usuarios y acceso, Catálogo, Operaciones) containing 9 cards total, optional activity feed.
2. **Given** an Admin views the KPI strip, **When** the page mounts, **Then** each count animates from 0 to its final value over `--motion-slow`. **Given** the user has reduced-motion enabled, counts render their final values immediately with no animation.
3. **Given** an Admin clicks a KPI tile (e.g., Pending suppliers), **When** the navigation completes, **Then** the user lands on the correct filtered surface (e.g., `/Admin/Suppliers?status=Pending`).
4. **Given** an Admin clicks a capability card (e.g., Groups), **When** the navigation completes, **Then** the user lands on the corresponding admin sub-surface (e.g., `/Admin/Groups`) with HTTP 200.
5. **Given** a non-Admin user requests `/Admin`, **Then** the request returns 403 Forbidden (unauthenticated → redirect to login). Existing role gate preserved.

---

### User Story 2 — Sub-surface sweep at warm-modern quality bar (Priority: P1)

As any user navigating an admin sub-surface, I see a consistent warm-modern experience — every view inherits spec 011's tokens and partials, every empty state shows an illustration, every copy string is voice-guide compliant.

**Why this priority**: Without the sweep, the new dashboard lands on top of inconsistent sub-surfaces — visual contradiction. The sweep is mechanical labor that delivers consistency at scale. Same role this story plays as spec 011 US6 played for the rest of the app.

**Independent Test**: For every sub-surface in the inventory, walk an `ADMIN-SWEEP-CHECKLIST.md` and verify each surface passes the seven swept criteria from spec 011 FR-017. Run greps for raw hex / inline `style=` across `Views/Admin/**` and admin view folders for renamed controllers — both return zero. Run Playwright sweep tests with semantic POMs.

**Acceptance Scenarios**:

1. **Given** every view in the sweep inventory, **When** a developer applies the checklist uniformly, **Then** each view passes all seven criteria from spec 011 FR-017: tokens-only colors, no inline `style=`, partial usage (`_PageHeader` / `_DataTable` / `_StatusPill` / `_EmptyState` / `_ActionBar` / `_ConfirmDialog`), voice-guide-compliant es-CR copy, correct typography roles, semantically restructured HTML where it improves UX, semantic locators present.
2. **Given** a grep of `Views/Admin/**/*.cshtml` and the admin view folders for renamed controllers for raw hex (`/#[0-9a-fA-F]{3,8}/`), **Then** zero results.
3. **Given** a grep for inline `style=` attributes across the same set, **Then** zero results.
4. **Given** the Playwright suite, **When** run after sweep, **Then** every previously-passing test passes; new admin sweep tests pass; semantic POMs are in place for every restructured surface.

---

### User Story 3 — Illustration-backed empty states across admin tables (Priority: P1)

As an admin viewing a table with no data (or filtered to no matches), I see an illustration that orients and warms the moment instead of a generic "No data" row, with copy that tells me what to do next.

**Why this priority**: Empty admin tables today render generic placeholders or empty rows — visual dead-zone. Empty states are wayfinding (spec 008 principle, reinforced by spec 011). This is the cheapest illustrated UX win across the admin area and the only thing keeping the sub-surface sweep from feeling complete.

**Independent Test**: Force an empty fixture for each admin table (Users, Groups, Suppliers, Reports default, Currencies, Exchange Rates, Legacy Quotations, Impact Templates) and confirm correct illustration + copy + CTA. Force a filtered-search-no-results on the surfaces that support search and confirm `magnifier-on-empty` with distinct copy.

**Acceptance Scenarios**:

1. **Given** an admin table with zero rows, **When** the empty state renders, **Then** `_EmptyState` is used with the illustration scene mapped per FR-012, voice-guide-compliant title + subtitle, and a single primary CTA when an actionable next step exists.
2. **Given** an admin table with rows but a filter producing zero results, **When** the empty state renders, **Then** it uses `magnifier-on-empty` with copy along the lines of "Sin coincidencias" / "Pruebe con otros filtros" — distinct from the initial-empty state.
3. **Given** any admin empty state, **When** the user has reduced-motion enabled, **Then** the entrance animation is suppressed and the illustration renders in its final state (consistent with spec 011 FR-065).

---

### User Story 4 — Sidebar admin grouping (Priority: P2)

As any signed-in user, I want the sidebar to feel scannable rather than a flat list of 11 entries, so that role-irrelevant admin entries don't crowd my workspace.

**Why this priority**: Sidebar is the platform's primary navigation; 11 flat entries cost legibility for every admin signed in. Grouping is a small UX investment with broad payoff. P2 because the dashboard delivers most of the discovery win — the sidebar grouping is a follow-up polish.

**Independent Test**: Log in as Admin, observe sidebar — admin sub-entries appear under a visually-distinct "Administración" section header; non-admin entries (Inicio, Cola de revisión, Bandeja de firmas) remain at the top level. Log in as Reviewer / Applicant and confirm no admin section appears. Run E2E suite and confirm all admin `sidebar-entry-*` testids still resolve.

**Acceptance Scenarios**:

1. **Given** a signed-in Admin, **When** the sidebar renders, **Then** an "Administración" section header appears followed by exactly the admin sub-entries (Users, Groups, Suppliers, Reports, Currencies, Exchange Rates, Legacy Quotations).
2. **Given** the sidebar, **When** inspected for `data-testid` slugs, **Then** all prior admin slugs (`sidebar-entry-users`, `sidebar-entry-groups`, `sidebar-entry-suppliers`, `sidebar-entry-reports`, `sidebar-entry-currencies`, `sidebar-entry-exchange-rates`, `sidebar-entry-legacy-quotations`) remain present and findable.
3. **Given** a non-Admin user (Reviewer or Applicant), **When** they view the sidebar, **Then** no admin section header and no admin sub-entries appear.

---

### User Story 5 — Route normalization (Priority: P2)

As a developer or admin reading URLs and breadcrumbs, I want admin routes to follow a consistent style so that "AdminCurrencies", "AdminExchangeRates", and "AdminLegacyQuotations" stop diverging from "Users", "Groups", "Reports", and "Suppliers".

**Why this priority**: Route inconsistency leaks into breadcrumbs, copy, and developer cognitive load. Cheap to fix in pre-prod. P2 because it's not user-blocking — it's hygiene that prevents future debt.

**Independent Test**: Hit `/Admin/Currencies`, `/Admin/ExchangeRates`, `/Admin/LegacyQuotations` as Admin — all 200. Hit `/Admin/AdminCurrencies` etc. — all 404. Sidebar links navigate to the new paths.

**Acceptance Scenarios**:

1. **Given** an Admin requests `/Admin/Currencies`, `/Admin/ExchangeRates`, or `/Admin/LegacyQuotations`, **Then** each returns 200.
2. **Given** an Admin requests `/Admin/AdminCurrencies`, `/Admin/AdminExchangeRates`, or `/Admin/AdminLegacyQuotations`, **Then** each returns 404 (no redirect shim).
3. **Given** the sidebar, **When** rendered for an Admin, **Then** admin entries link to the normalized routes.

---

### User Story 6 — Reports sub-tab UX refresh (Priority: P2)

As an admin opening any Reports tab, I see the same warm-modern treatment that the rest of the app got in spec 011 — pill-style filter chips, animated KPI tiles, density discipline.

**Why this priority**: Reports is the largest admin surface set (5 tabs); its UX bleeds into every admin's daily flow. P2 because the dashboard ships first; once the dashboard is in, the reports tabs become the next visible inconsistency.

**Independent Test**: Visit each report tab — confirm pill-chip tab styling, KPI tickers animating on mount (and not under reduced-motion), table density at `--space-2`. Visual regression against spec 011 reviewer-queue filter chips.

**Acceptance Scenarios**:

1. **Given** the Reports tab strip, **When** rendered, **Then** tabs use pill-style chips matching reviewer-queue filter-chip styling (`--color-primary-subtle` selected background, `--color-primary` selected text, `--motion-base` reflow on selection, no full page reload).
2. **Given** a Reports tab loads, **When** `_KpiTile` mounts, **Then** numeric values animate from 0 to final over `--motion-slow`. **Given** reduced-motion is enabled, the final value renders immediately.
3. **Given** any Reports table, **When** rendered, **Then** row padding uses `--space-2`.

---

### User Story 7 — Admin index activity feed (Priority: P3)

As an admin landing on the dashboard, I see a short feed of recent admin-relevant events so I can quickly orient on what's happened lately without spelunking the audit log.

**Why this priority**: Nice-to-have. Useful for admin situational awareness but not blocking dashboard value. P3 because data-source coverage may be sparse in dev/early-prod and the feed must degrade gracefully to hidden when zero events exist — meaning it adds risk to the P1 dashboard story unless treated independently.

**Independent Test**: Seed fixture with ≥ 1 `AdminAuditEvent` rows; confirm feed renders ≤ 5 most recent with deep-links. Seed empty fixture; confirm feed is hidden entirely (no empty rail). Each event keyboard-focusable and clickable.

**Acceptance Scenarios**:

1. **Given** the admin dashboard, **When** `AdminAuditEvent` yields ≥ 1 event in the last 30 days, **Then** a queue-scoped `_EventTimeline` renders the 5 most recent events, formatted "{actor} {action} {target}" with relative timestamps.
2. **Given** each event in the feed, **When** the user focuses or clicks it, **Then** it deep-links to the relevant surface (e.g., user-edited → `/Admin/Users/{id}/Edit`, group-changed → `/Admin/Groups/{id}/Edit`).
3. **Given** zero events in the last 30 days, **When** the dashboard renders, **Then** the feed is hidden entirely; no empty rail appears.

---

### Edge Cases

- **Admin with zero of every action item** (no pending suppliers, no aging apps, no pending legacy quotations, no events in 30 days): KPI tiles render zero; capability cards render unaffected; activity feed hidden.
- **Admin role demoted while viewing the dashboard**: on next render, layout switches to the appropriate role's landing surface; no in-flight action is corrupted.
- **Sub-surface link target temporarily unavailable** (e.g., reports endpoint 500): the dashboard card still renders; navigation produces the standard error page; dashboard isn't blocked by sub-surface health.
- **A new admin capability lands after this spec without a card on `/Admin`**: dashboard becomes incomplete again. Mitigation: planning produces a checklist requiring future admin-feature specs to add a capability card (governance, not code).
- **`AdminAuditEvent` table is empty in a fresh dev seed**: feed is hidden gracefully (no empty rail).
- **Pending-supplier count source is missing or stale**: KPI shows the best available count; planning resolves the source-of-truth path and the failure mode (zero vs. error tile).
- **A user with both Reviewer and Admin roles**: sidebar shows both review-queue/signing-inbox at top level AND the admin section; established role-gating logic preserved.
- **Filtered-search yields zero AND the table is unfiltered-empty**: the unfiltered-empty illustration wins (filter is a no-op on empty data); copy reflects "no data" rather than "no matches".
- **Old route hits from external bookmarks** (none expected pre-prod): 404 with the standard error page.
- **Sentinel admin** (per spec 009): excluded from Active Users count; never appears in capability cards or in any admin listing the dashboard links to.
- **Capability sections without applicable features in a constrained seed** (e.g., a tenant where currencies are disabled — not yet a real case): all 9 cards still render in the dashboard; clicking a card whose feature isn't seeded leads to that feature's normal empty-state surface.

## Requirements *(mandatory)*

### Functional Requirements

**Admin home dashboard (US1)**

- **FR-001**: System MUST replace the current 3-card `/Admin` index view with a dashboard layout: page header → KPI strip (4 tiles) → grouped capability sections (3 sections, 9 cards) → activity feed (US7, optional).
- **FR-002**: KPI strip MUST display 4 action-oriented tiles: **Pending suppliers** (suppliers awaiting admin approval per spec 013), **Pending legacy quotations** (legacy queue count per spec 015), **Aging applications** (applications past `AgingThresholdDays` per spec 010), **Active users** (count of non-sentinel Active users per spec 009). Each tile MUST animate count from 0 to final value over `--motion-slow` on mount; reduced-motion suppresses.
- **FR-003**: Each KPI tile MUST be keyboard-focusable and clickable, deep-linking to the relevant filtered surface (e.g., Pending suppliers → `/Admin/Suppliers?status=Pending`, Aging applications → `/Admin/Reports/Aging`, Pending legacy quotations → `/Admin/LegacyQuotations`, Active users → `/Admin/Users?status=Active`).
- **FR-004**: Capability cards MUST be grouped under three section headers — **Usuarios y acceso** (Users, Groups), **Catálogo** (Suppliers, Currencies, Exchange Rates, Impact Templates), **Operaciones** (Reports, Legacy Quotations, System Configuration). Section headers MUST use `--font-display` and `--type-heading-md` per spec 011 typography roles.
- **FR-005**: Each capability card MUST render: a Tabler icon, capability label, one-line voice-guide-compliant es-CR description, primary CTA button navigating to the surface. No KPIs inside cards — KPIs live only in the top strip.
- **FR-006**: A new partial `_AdminDashboard` MUST be introduced for the dashboard composition. A new partial `_CapabilityCard` MUST be introduced for the per-capability card. Reused KPI tile = the existing `_KpiTile` from spec 010 (re-templated by US6).
- **FR-007**: Admin dashboard data MUST surface through Application-layer projections without schema changes. Aggregations are query-time. Data dependencies: pending-supplier count from `SupplierService` (or equivalent), pending-legacy-quotation count from `AdminLegacyQuotationsService`, aging-application count reusing the spec-010 `AgingThresholdDays` configuration, active-user count reusing spec-009 user-store predicates excluding the sentinel.

**Sub-surface sweep (US2)**

- **FR-008**: The sweep inventory MUST cover: `/Admin` (US1 dashboard), `/Admin/Users` (Index, Create, Edit, ResetPassword), `/Admin/Groups` (Index, Create, Edit, Detail), `/Admin/Suppliers` (Index, Detail, edit/approve flows), `/Admin/Reports` (Dashboard, Applications, Applicants, Aging, FundedItems — driven by US6), `/Admin/Currencies` (Index, Create, Edit), `/Admin/ExchangeRates` (Index, Create), `/Admin/LegacyQuotations` (Index, Detail), `/Admin/ImpactTemplates` (Index, Create, Edit), `/Admin/Configuration`.
- **FR-009**: Every view in the inventory MUST satisfy the seven swept criteria from spec 011 FR-017: (1) no raw hex/px outside tokens; (2) no inline `style=`; (3) status displays use `_StatusPill`, empty states use `_EmptyState` with a US3 illustration, action groups use `_ActionBar`, destructive actions use `_ConfirmDialog`; (4) voice-guide compliant copy; (5) page heading uses `--font-display` + the appropriate `--type-heading-*` token, body uses `--font-body`; (6) HTML restructured where it improves UX; (7) stable semantic locators present (ARIA roles + accessible names preferred; `data-testid` where role/name are insufficient).
- **FR-010**: Page Object Model classes for every swept admin surface MUST be rewritten against the new HTML and selector strategy, exposing semantic actions over raw locators (consistent with spec 011 FR-021).
- **FR-011**: User-facing copy on every swept admin view MUST be reviewed against `BRAND-VOICE.md` (spec 011) and rewritten where it violates the guide. Copy is es-CR; localization layer remains deferred to a future spec.

**Empty states (US3)**

- **FR-012**: Every admin table empty state MUST use the `_EmptyState` partial with the appropriate illustration scene from the spec 011 9-illustration set: **Users / Groups / Impact Templates / Suppliers** index → `folders-stack`; **Legacy Quotations** queue empty → `calm-horizon`; **Reports** default → `soft-bar-chart`; **Currencies / Exchange Rates** index empty → `folders-stack`; any **filtered-no-results** → `magnifier-on-empty`.
- **FR-013**: Empty-state title and subtitle MUST be voice-guide-compliant es-CR copy. When the empty state corresponds to an actionable next step (e.g., zero groups → "Cree su primer grupo"), `_EmptyState` MUST render a single primary CTA. Otherwise no CTA.
- **FR-014**: Filtered-search-no-results MUST be distinct from initial-empty: a search returning zero rows uses `magnifier-on-empty` with copy along the lines of "Sin coincidencias" / "Pruebe con otros filtros"; an unfiltered table with no data uses the entity-appropriate scene.

**Sidebar grouping (US4)**

- **FR-015**: When the signed-in user holds the Admin role, the sidebar MUST render a visually-distinct "Administración" section header above the admin sub-entries. The current top-level "Administración" entry linking to `/Admin` MUST become the section header itself, with `/Admin` reachable via clicking the section header (or via a "Panel" entry inside the section — choice deferred to planning).
- **FR-016**: Sidebar slugs and `data-testid` values MUST remain stable English identifiers per `_Layout.cshtml` NFR-001; the admin section header receives a new slug `admin-section`. All prior admin slugs (`sidebar-entry-users`, `sidebar-entry-groups`, `sidebar-entry-suppliers`, `sidebar-entry-reports`, `sidebar-entry-currencies`, `sidebar-entry-exchange-rates`, `sidebar-entry-legacy-quotations`) MUST remain.
- **FR-017**: Non-Admin users MUST see no admin section at all (existing role-gating behavior preserved).

**Route normalization (US5)**

- **FR-018**: The following controllers MUST be renamed to drop the `Admin` prefix in their public-facing route: `AdminCurrenciesController` → route `/Admin/Currencies`, `AdminExchangeRatesController` → `/Admin/ExchangeRates`, `AdminLegacyQuotationsController` → `/Admin/LegacyQuotations`. Class names MAY remain prefixed (Clean Architecture / namespace clarity) but route attributes MUST emit the normalized URLs.
- **FR-019**: Sidebar URLs in `_Layout.cshtml` and any breadcrumbs / `Url.Action` calls referring to the affected controllers MUST be updated to point at the normalized routes.
- **FR-020**: No HTTP redirects from old paths to new paths MUST be added (pre-prod, no real bookmarks; prevents accidental dependency on a temporary shim).

**Reports tab UX (US6)**

- **FR-021**: `_ReportSubTabs` MUST be re-templated to render pill-style chips matching the reviewer-queue filter-chip styling from spec 011 (`--color-primary-subtle` selected background, `--color-primary` selected text, `--motion-base` reflow on selection, no full page reload).
- **FR-022**: `_KpiTile` numeric values MUST animate from 0 to final value over `--motion-slow` on mount (number ticker), capped at 60 frames; reduced-motion suppresses and renders the final value immediately.
- **FR-023**: Reports tables MUST adopt density discipline `--space-2` row padding (matching reviewer queue from spec 011 FR-060).

**Activity feed (US7, optional)**

- **FR-024**: When `AdminAuditEvent` (spec 016) yields ≥ 1 event in the last 30 days, the dashboard MUST render a queue-scoped `_EventTimeline` variant showing the 5 most recent events. Each event MUST be keyboard-focusable and clickable, deep-linking to the relevant surface (e.g., user-edited → `/Admin/Users/{id}/Edit`, group-changed → `/Admin/Groups/{id}/Edit`). When no events exist in that window, the feed MUST be hidden entirely (no empty rail).
- **FR-025**: Event copy MUST be voice-guide-compliant es-CR; format is "{actor} {action} {target}" with a relative timestamp (e.g., "hace 3 minutos"). Action vocabulary MUST be drawn from the existing `AdminAuditEvent` action enum without expansion.

**Cross-cutting**

- **FR-026**: Every user story MUST be covered by Playwright end-to-end tests covering the golden path and key error scenarios. Constitution principle III — no exceptions.
- **FR-027**: Schema MUST remain unchanged. Zero edits to `src/FundingPlatform.Database/`. If planning surfaces an unavoidable need, the change MUST be surfaced via `/speckit-spex-evolve` for explicit approval before any dacpac edit.
- **FR-028**: PDF carve-out files MUST remain byte-identical; this spec touches no PDF surface (regression check, not a positive change).
- **FR-029**: WCAG AA color-contrast MUST hold on the new dashboard, the re-templated reports tabs, and every swept admin surface. Verified by axe-playwright on the dashboard and a representative reports tab.
- **FR-030**: The combined incremental wire weight added by this spec (new partials, no new fonts, no new libraries — only re-templating) MUST be < 30 KB gzipped.

**Out of Scope**

- **OOS-1**: Real-time push (SignalR/websockets) on the admin dashboard. KPIs refresh on page load. Mirrors spec 011 FR-068.
- **OOS-2**: New admin capabilities (e.g., audit-log viewer surface, bulk user actions, saved queue views). This spec is pure facelift + capability-completeness.
- **OOS-3**: Schema changes (per FR-027).
- **OOS-4**: Localization layer changes (es-CR copy is the only locale; future spec owns multi-locale).
- **OOS-5**: Public marketing surface, login/register restyle (already covered by spec 011).
- **OOS-6**: Dark mode (deferred per spec 011 FR-070).
- **OOS-7**: Audit-log retention/policy changes — this spec only consumes existing `AdminAuditEvent` rows, doesn't expand the model.
- **OOS-8**: Removing admin capabilities. The 9 sub-surfaces all stay.
- **OOS-9**: Reports new content. Current report tabs stay; only their UX gets refreshed.
- **OOS-10**: Sentinel/admin-role rules from spec 009. Untouched.

### Key Entities

- **Capability card**: a per-admin-surface card on the `/Admin` dashboard (icon + label + one-line description + primary CTA). Rendered via the new `_CapabilityCard` partial.
- **Capability section**: a grouping of capability cards under one of three section headers (Usuarios y acceso / Catálogo / Operaciones). Section assignment is fixed at template time, not data-driven.
- **Action KPI tile**: an instance of `_KpiTile` (spec 010) on the admin dashboard rendering a deep-linkable count with a number-ticker mount animation.
- **Admin sub-surface**: a view file under the admin area scoped by FR-008. One row in the sweep inventory.
- **Sweep inventory entry**: an admin sub-surface scoped by FR-008 against the seven swept criteria from spec 011 FR-017.
- **Admin event**: an `AdminAuditEvent` row (spec 016) projected into the activity-feed view model with actor, action, target, timestamp, and a deep-link URL.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `/Admin` renders the new dashboard layout: page header → 4 KPI tiles → 3 grouped capability sections containing all 9 admin capability cards → activity feed (when ≥ 1 event exists). All 9 capabilities are visible above the fold or with one scroll on a 1366×768 viewport.
- **SC-002**: All 4 KPI tiles render correct counts for the 4 reference fixtures (zero of everything / mixed mid-state / all-thresholds-tripped / pre-existing prod-like dataset). Each KPI tile click navigates to the correct filtered surface.
- **SC-003**: Each of the 9 capability cards links to a 200 OK page when clicked by an Admin user. Verified by an automated walk.
- **SC-004**: KPI counts animate from 0 to final value on first paint over `--motion-slow`. Under `prefers-reduced-motion`, counts render their final values immediately. Verified by a dedicated reduced-motion Playwright run.
- **SC-005**: A grep for raw hex color literals (`/#[0-9a-fA-F]{3,8}/`) outside `wwwroot/css/tokens.css` and PDF carve-outs across every swept admin view returns zero results.
- **SC-006**: A grep for inline `style=` attributes across `Views/Admin/**/*.cshtml` and admin view folders for the renamed controllers returns zero results.
- **SC-007**: Every view in the sweep inventory passes a manual review against an `ADMIN-SWEEP-CHECKLIST.md` deliverable that lists each view + the seven swept criteria as check items.
- **SC-008**: Every admin table renders the correct illustration scene for its empty state (per FR-012). Verified by snapshot or DOM-locator assertion across all 9 sub-surfaces.
- **SC-009**: Filtered-search-no-results renders `magnifier-on-empty` with the correct copy on Users (search), Groups (search), Suppliers (filter), Reports (filtered-no-results) — verified per surface.
- **SC-010**: When the signed-in user holds the Admin role, the sidebar renders an "Administración" section header followed by exactly the admin sub-entries; all `data-testid` slugs from prior specs (per FR-016) are still present and findable.
- **SC-011**: Non-Admin users (Reviewer / Applicant / unauthenticated) see no admin section at all; a count of admin-related sidebar entries returns zero for those roles.
- **SC-012**: A request to `/Admin/AdminCurrencies`, `/Admin/AdminExchangeRates`, or `/Admin/AdminLegacyQuotations` returns 404 (no redirect shim, per FR-020). The new normalized routes `/Admin/Currencies`, `/Admin/ExchangeRates`, `/Admin/LegacyQuotations` return 200 for an Admin user.
- **SC-013**: Reports sub-tabs render as pill-style chips matching reviewer-queue filter-chip styling (visual + DOM-class assertion). KPI tiles in reports animate tickers on mount; reduced-motion suppresses.
- **SC-014**: Admin activity feed renders ≤ 5 most recent events with deep-links when `AdminAuditEvent` yields events; renders nothing when zero events exist (verified by both fixtures).
- **SC-015**: WCAG AA color-contrast holds on the dashboard, reports tabs, and a representative sample of swept admin surfaces (Users index, Suppliers index, Reports default). Verified by axe-playwright.
- **SC-016**: Schema is unchanged. `git diff --stat` against `src/FundingPlatform.Database/` is empty.
- **SC-017**: PDF identity is preserved — Funding Agreement PDF visually identical to a stored reference (regression check; this spec touches no PDF surface).
- **SC-018**: Voice-guide compliance — every user-facing string in views touched by the sweep passes the `BRAND-VOICE.md` checklist (no ALL CAPS shouting, no exclamation marks except the signing ceremony, no "submit" CTAs, no passive voice in microcopy).
- **SC-019**: Every Playwright test passing before this spec passes after; the net-new admin-dashboard + reports-tabs + sweep tests pass; the reduced-motion test passes; semantic POMs are in place for every renamed/restructured admin surface.
- **SC-020**: Combined incremental wire weight added by this spec is < 30 KB gzipped (no new fonts, no new libraries).
- **SC-021**: Designer/product review of `/Admin` signs off that — for the four reference dashboard fixtures of SC-002 — the KPI strip, capability sections, and activity feed (when present) are all identifiable on first paint without scrolling. Recorded as an explicit review item in the PR description.

## Assumptions

- The platform remains pre-production. Aggressive scope, route renames without HTTP redirects, and full POM rewrites are acceptable (mirrors spec 011 / 008 stance).
- Spec 011's design tokens (`tokens.css`), partial library, motion catalog, illustration set (9 SVGs), and `BRAND-VOICE.md` are in place and consumable. This spec adds no tokens, no fonts, no illustrations beyond the existing 9-scene set.
- Spec 010's `_ReportSubTabs` and `_KpiTile` partials are the right place to land report-tabs UX changes. No new reports partial.
- Spec 015's currency / exchange-rate / legacy-quotation aggregates are queryable from existing services (`AdminLegacyQuotationsService`, currency/exchange-rate services or equivalent). Pending-legacy-quotation count surfaces from a single service call.
- Spec 016's `AdminAuditEvent` is queryable for an activity feed projection; if rows are sparse in dev, the feed is hidden gracefully (per FR-024).
- The four KPIs (Pending suppliers / Pending legacy quotations / Aging applications / Active users) are the right set for v1. Future iterations may revise; this spec doesn't lock the set.
- Pending-supplier count is sourced from supplier status — the existing supplier-status enum (per spec 013 if shipped, or from current `AdminSuppliersController` semantics) has a discoverable "pending review" state. Planning resolves the exact enum value.
- Aging-applications threshold is the spec-010 `AgingThresholdDays` configuration, single source of truth.
- Active-user count excludes the sentinel admin (per spec 009 FR-019).
- The currently-existing `Views/Admin/Index.cshtml` 3-card layout has zero callers other than the sidebar's "Administración" entry; deleting/replacing it doesn't break a tracked navigation path other than that one entry. Verified at planning by grep.
- POM rewrites are budgeted; per the durable feedback memory, UX/UI quality wins over selector stability for facelift work.
- Reports content (the actual data each tab renders) is unchanged; only its UX shell is touched.
- Sub-surfaces that already passed spec 011's sweep get re-verified, not re-swept; the inventory applies the same seven criteria a second time, primarily to catch admin-area routes that the spec 011 sweep missed (e.g., empty-state illustration coverage in admin-table empty states, post-spec-011 surfaces from specs 015 and 016 that never received the sweep).
- The sentinel admin's exclusion from listings (spec 009 FR-019) and modification rejection (FR-020) carry through unchanged; this spec does not relax or extend either.
- A grep audit at planning confirms which `Views/Admin/**/*.cshtml` files currently violate the seven swept criteria; the planning artifact maps each violation to a tasks.md task.
