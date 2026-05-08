---
description: "Task list for spec 017 admin UX/UI facelift"
---

# Tasks: Admin UX/UI Facelift

**Input**: Design documents in `specs/017-admin-ux-facelift/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md, ADMIN-SWEEP-CHECKLIST.md
**Tests**: REQUIRED per Constitution Principle III (E2E NON-NEGOTIABLE) and FR-026.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: Which user story this task belongs to (US1..US7); omitted for Setup / Foundational / Polish
- Exact file paths in descriptions

## Path Conventions

Single-tree layout (matches `CLAUDE.md`):

- `src/FundingPlatform.Domain/` (no edits — schema-locked)
- `src/FundingPlatform.Application/` (DTOs, services, interfaces)
- `src/FundingPlatform.Infrastructure/` (EF Core impls, file/blob storage)
- `src/FundingPlatform.Web/` (controllers, views, view models, wwwroot)
- `tests/FundingPlatform.Tests.{Unit,Integration,E2E}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No new project scaffolding required (existing solution + Aspire host). This phase records the prep checks each implementer runs once.

- [ ] T001 Verify `dotnet build FundingPlatform.slnx` is green on `017-admin-ux-facelift` branch before starting (baseline check)
- [ ] T002 Verify `git diff --stat src/FundingPlatform.Database/` is empty (SC-016 baseline gate)
- [ ] T003 [P] Capture pre-spec wire-weight baseline by running `scripts/asset-budget-check.sh` and saving output to `specs/017-admin-ux-facelift/baseline-wire-weight.txt` (used to verify SC-020 < 30 KB delta after merge)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Application-layer projection scaffolding, copy provider, and DI wiring that every user story depends on.

**⚠️ CRITICAL**: No user-story phase begins until Phase 2 is complete.

- [ ] T004 Create DTO records in `src/FundingPlatform.Application/DTOs/AdminDashboardDto.cs`: `AdminDashboardDto`, `AdminDashboardKpis`, `CapabilitySection`, `CapabilityCard`, `AdminEvent` (per data-model.md)
- [ ] T005 [P] Create `src/FundingPlatform.Application/Services/IAdminDashboardProjection.cs` with single `Task<AdminDashboardDto> GetAsync(CancellationToken ct)` method (per R4)
- [ ] T006 [P] Create `src/FundingPlatform.Application/Services/IAdminAuditEventReader.cs` with `Task<IReadOnlyList<AdminAuditEvent>> GetRecentAsync(int take, TimeSpan window, CancellationToken ct)` method
- [ ] T007 [P] Create `src/FundingPlatform.Application/Services/IAdminAuditEventCopyProvider.cs` with `string Format(string action, string targetType, string? payloadJson)` method (per R5)
- [ ] T008 Implement `src/FundingPlatform.Application/Services/AdminAuditEventCopyProvider.cs` with es-CR mappings for the 4 `AdminAuditEvent` action constants (`group.create`, `group.rename`, `group.delete`, `user.memberships.update`); voice-guide-compliant copy
- [ ] T009 Implement `src/FundingPlatform.Application/Services/AdminDashboardProjection.cs` with constructor-injected dependencies (`ISupplierRepository`, `AdminLegacyQuotationsService` or its repo equivalent, `IApplicationRepository` for aging, `IUserStoreReader`, `IAdminAuditEventReader`, `IAdminAuditEventCopyProvider`); 4 private sub-projection methods; degrade-to-zero failure mode per R2 (try/catch around each sub-projection, emit WARN-level structured log entry tagged `AdminDashboardKpiProjectionFailed { kpi, reason }`, return `0`)
- [ ] T010 Implement `src/FundingPlatform.Infrastructure/Persistence/AdminAuditEventReader.cs` (`: IAdminAuditEventReader`) reading top-N `AdminAuditEvent` rows by `OccurredAt desc` within the given window
- [ ] T011 Add `IUserStoreReader.GetActiveUserCountAsync` (or equivalent) excluding sentinel; implement in the existing user-store reader infrastructure consistent with spec 009 sentinel-exclusion predicate
- [ ] T012 Wire DI in `src/FundingPlatform.Application/DependencyInjection.cs` and `src/FundingPlatform.Infrastructure/DependencyInjection.cs`: register `IAdminDashboardProjection` → `AdminDashboardProjection`, `IAdminAuditEventReader` → `AdminAuditEventReader`, `IAdminAuditEventCopyProvider` → `AdminAuditEventCopyProvider`
- [ ] T013 [P] Add unit tests `tests/FundingPlatform.Tests.Unit/Application/Services/AdminAuditEventCopyProviderTests.cs` covering all 4 action mappings + es-CR phrasing assertions
- [ ] T014 [P] Add unit tests `tests/FundingPlatform.Tests.Unit/Application/Services/AdminDashboardProjectionTests.cs` covering: happy-path (mocked deps return counts), each sub-projection failure path (degrade to 0 + log emitted), DTO shape

**Checkpoint**: Application + Infrastructure + DI ready. User-story phases can now run in priority order.

---

## Phase 3: User Story 1 — Admin home dashboard (Priority: P1) 🎯 MVP

**Goal**: Replace the 3-card legacy `/Admin` index with a dashboard composing 4 action KPIs + 3 grouped capability sections + optional activity feed.

**Independent Test**: per quickstart.md §2 — log in as Admin, land on `/Admin`, verify all 4 KPIs render with correct counts for the 4 reference fixtures, click-walk every KPI tile and capability card to a 200 OK surface, reload with `prefers-reduced-motion` and verify final values render immediately.

### Implementation

- [ ] T015 [US1] Create `src/FundingPlatform.Web/Views/Shared/Components/_CapabilityCard.cshtml` partial accepting a `CapabilityCard` model — renders Tabler icon + label + description + primary CTA button; tokens-only colors; `data-testid="admin-capability-{slug}"`
- [ ] T016 [US1] Create `src/FundingPlatform.Web/Views/Shared/Components/_AdminDashboard.cshtml` partial accepting `AdminDashboardDto` — composes page header (via `_PageHeader`), 4 KPI tiles (via `_KpiTile` — re-templated by US6), 3 capability sections each with their `_CapabilityCard` instances, conditional activity feed (via `_EventTimeline` — added by US7); section header data-testids `admin-section-users-access` / `admin-section-catalog` / `admin-section-operations`
- [ ] T017 [US1] Create `src/FundingPlatform.Web/ViewModels/Admin/AdminDashboardViewModel.cs` thin Web-layer adapter wrapping `AdminDashboardDto`
- [ ] T018 [US1] Modify `src/FundingPlatform.Web/Controllers/AdminController.cs` `Index` action to inject `IAdminDashboardProjection`, await `GetAsync(ct)`, project into `AdminDashboardViewModel`, return `View(viewModel)`
- [ ] T019 [US1] Rewrite `src/FundingPlatform.Web/Views/Admin/Index.cshtml` to render the `_AdminDashboard` partial with the new view model (replaces today's 3-card grid)
- [ ] T020 [US1] Populate `CapabilitySection` template-time data with the 9 cards per FR-004: Usuarios y acceso → Users (`ti ti-users`, `/Admin/Users`), Groups (`ti ti-users-group`, `/Admin/Groups`); Catálogo → Suppliers (`ti ti-building-store`, `/Admin/Suppliers`), Currencies (`ti ti-coin`, `/Admin/Currencies`), Exchange Rates (`ti ti-arrows-exchange`, `/Admin/ExchangeRates`), Impact Templates (`ti ti-template`, `/Admin/ImpactTemplates`); Operaciones → Reports (`ti ti-chart-line`, `/Admin/Reports`), Legacy Quotations (`ti ti-history`, `/Admin/LegacyQuotations`), System Configuration (`ti ti-settings`, `/Admin/Configuration`); section assignment lives in `AdminDashboardProjection` or a static composer per plan structure
- [ ] T021 [US1] Compute KPI deep-link URLs in the projection: `/Admin/Suppliers?status=PendingReview` (R1), `/Admin/LegacyQuotations`, `/Admin/Reports/Aging`, `/Admin/Users?status=Active`
- [ ] T022 [US1] Verify `_KpiTile` re-templated body (US6 task in Phase 6) supports the deep-link href + data-testid `admin-kpi-{slug}` shape; if Phase 6 not yet run, leave a TODO comment in `_AdminDashboard` referencing US6 — but DO NOT block US1 completion (Phase 6 must complete before SC-001 can pass)
- [ ] T023 [P] [US1] Add E2E test `tests/FundingPlatform.Tests.E2E/AdminDashboardTests.cs` covering: 4 reference fixtures (zero-of-everything / mixed-mid / all-thresholds-tripped / prod-like) → assert KPI counts; click-walk all 4 KPIs; click-walk all 9 capability cards; non-Admin (Reviewer/Applicant/anonymous) returns 403/redirect
- [ ] T024 [P] [US1] Add E2E reduced-motion test `tests/FundingPlatform.Tests.E2E/AdminDashboardReducedMotionTests.cs` running with Playwright `reducedMotion: 'reduce'` option; assert KPI tickers render final values immediately (no animation observed)
- [ ] T025 [P] [US1] Add Page Object `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminDashboardPage.cs` exposing semantic actions (`AwaitingPendingSuppliersCount()`, `ClickKpi(slug)`, `ClickCapability(slug)`, `IsActivityFeedVisible()`)

**Checkpoint**: US1 complete and demonstrable independently — the dashboard renders for all 4 reference fixtures.

---

## Phase 4: User Story 3 — Illustration-backed empty states (Priority: P1)

**Goal**: Every admin table empty state uses `_EmptyState` with the right scene from spec 011's 9-illustration set; voice-guide-compliant title + subtitle + single CTA when actionable; filtered-no-results uses `magnifier-on-empty`.

**Independent Test**: per quickstart.md §6 — force empty fixture for each admin table; verify illustration scene + copy + CTA per FR-012 mapping; force filtered-no-results on Users / Groups / Suppliers / Reports and verify `magnifier-on-empty`.

**Note on ordering**: US3 lands before US2 because US2's sweep validates partial usage including empty-state coverage.

- [ ] T026 [US3] Edit `src/FundingPlatform.Web/Views/AdminUsers/Index.cshtml` to render `_EmptyState` with scene `folders-stack`, title "Aún no hay usuarios", subtitle voice-guide-compliant, primary CTA "Crear usuario" linking to `/Admin/Users/Create`
- [ ] T027 [P] [US3] Edit `src/FundingPlatform.Web/Views/AdminGroups/Index.cshtml` empty branch — scene `folders-stack`, title "Aún no hay grupos", CTA "Crear grupo" → `/Admin/Groups/Create`
- [ ] T028 [P] [US3] Edit `src/FundingPlatform.Web/Views/AdminSuppliers/Index.cshtml` empty branch (default `PendingReview` filter) — scene `folders-stack`, title "Sin proveedores pendientes", no CTA (passive state)
- [ ] T029 [P] [US3] Edit `src/FundingPlatform.Web/Views/AdminCurrencies/Index.cshtml` empty branch — scene `folders-stack`, title "Aún no hay monedas registradas", CTA "Agregar moneda" → `/Admin/Currencies/Create`
- [ ] T030 [P] [US3] Edit `src/FundingPlatform.Web/Views/AdminExchangeRates/Index.cshtml` empty branch — scene `folders-stack`, title "Aún no hay tipos de cambio", CTA "Registrar tipo de cambio" → `/Admin/ExchangeRates/Create`
- [ ] T031 [P] [US3] Edit `src/FundingPlatform.Web/Views/AdminLegacyQuotations/Index.cshtml` empty branch — scene `calm-horizon`, title "Sin cotizaciones pendientes", no CTA (passive state)
- [ ] T032 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/ImpactTemplates.cshtml` empty branch — scene `folders-stack`, title "Aún no hay plantillas", CTA "Crear plantilla" → `/Admin/CreateTemplate`
- [ ] T033 [P] [US3] Edit `src/FundingPlatform.Web/Views/AdminReports/` default-tab view (e.g., `Dashboard.cshtml` or `Index.cshtml`) empty branch — scene `soft-bar-chart`, title "Aún no hay datos", no CTA
- [ ] T034 [US3] Add filtered-search-no-results branch (scene `magnifier-on-empty`, title "Sin coincidencias", subtitle "Pruebe con otros filtros", no CTA) to: `AdminUsers/Index.cshtml` (when search yields zero), `AdminGroups/Index.cshtml` (when search yields zero), `AdminSuppliers/Index.cshtml` (when filter yields zero), and Reports tabs that support filtering
- [ ] T035 [US3] Update each affected Razor view's view model (or the empty-state branch's controller logic) to distinguish "table-is-empty" vs "filter-yielded-no-rows"
- [ ] T036 [P] [US3] Add E2E test `tests/FundingPlatform.Tests.E2E/AdminEmptyStatesTests.cs` covering: empty fixture for each surface → assert illustration scene rendered; filtered-no-results case for the search-supporting surfaces → assert `magnifier-on-empty`

**Checkpoint**: US3 complete — every admin table empty state is illustrated and voice-guide-compliant.

---

## Phase 5: User Story 7 — Admin index activity feed (Priority: P3)

**Goal**: Queue-scoped `_EventTimeline` variant on `/Admin` showing top-5 `AdminAuditEvent` rows from the last 30 days; hides entirely when zero events.

**Independent Test**: per quickstart.md §8 — seed ≥ 1 `AdminAuditEvent`, reload `/Admin`, observe feed rendering with es-CR copy + relative timestamps + deep-links; seed empty fixture, observe feed hidden.

- [ ] T037 [US7] Extend `src/FundingPlatform.Application/Services/AdminDashboardProjection.cs` to call `IAdminAuditEventReader.GetRecentAsync(take: 5, window: TimeSpan.FromDays(30), ct)` and project each row through `IAdminAuditEventCopyProvider.Format(...)` plus `IUserStoreReader.GetDisplayName(actorUserId)` to populate `AdminEvent` records; sets `FeedVisible = events.Count > 0`
- [ ] T038 [US7] Compute deep-link URL per event in the projection: `targetType=group` → `/Admin/Groups/{targetId}/Edit`, `targetType=user` → `/Admin/Users/{targetId}/Edit`; if the target was deleted (`group.delete`), set `DeepLinkUrl = null`
- [ ] T039 [US7] Edit `src/FundingPlatform.Web/Views/Shared/Components/_AdminDashboard.cshtml` to render the activity-feed section only when `Model.FeedVisible == true`; uses `_EventTimeline` partial with the projected `AdminEvent` collection; section data-testid `admin-activity-feed`
- [ ] T040 [US7] Verify `_EventTimeline` partial accepts the `AdminEvent` shape (or add a thin overload); each event row keyboard-focusable, `<a href>` to `DeepLinkUrl` when non-null, plain text when null; relative-timestamp helper uses Spanish (e.g., via existing humanizer / `RelativeTime` helper consistent with spec 011)
- [ ] T041 [P] [US7] Add E2E test `tests/FundingPlatform.Tests.E2E/AdminActivityFeedTests.cs` covering: zero-event fixture → feed hidden (no `data-testid="admin-activity-feed"` element); ≥ 1 event fixture → feed rendered with correct copy + deep-link; deleted-target case → row rendered without link
- [ ] T042 [P] [US7] Add unit test `tests/FundingPlatform.Tests.Unit/Application/Services/AdminAuditEventDeepLinkResolverTests.cs` covering URL resolution per `targetType` and the deleted-target null case

**Checkpoint**: US7 complete — feed degrades gracefully and renders correctly when populated.

---

## Phase 6: User Story 6 — Reports tab UX refresh (Priority: P2)

**Goal**: `_ReportSubTabs` re-templated to pill-style chips; `_KpiTile` numeric ticker animation; reports tables use `--space-2` density.

**Independent Test**: per quickstart.md §7 — visit each report tab; confirm pill-chip styling (`--color-primary-subtle` selected bg), KPI tickers animating on mount (and not under reduced-motion), `--space-2` row padding.

**Note on ordering**: US6 must complete before SC-001 fully passes (US1's KPI strip uses the re-templated `_KpiTile`).

- [ ] T043 [US6] Re-template `src/FundingPlatform.Web/Views/Shared/Components/_ReportSubTabs.cshtml` to render pill-style chips matching spec 011 reviewer-queue filter chips: selected chip uses `--color-primary-subtle` background + `--color-primary` text, unselected uses muted outline; chip click reflows tab content over `--motion-base` with no full page reload (use Tabler tab JS or a thin custom handler if needed)
- [ ] T044 [US6] Re-template `src/FundingPlatform.Web/Views/Shared/Components/_KpiTile.cshtml` to: (a) animate the numeric value from 0 to final over `--motion-slow` on mount, capped at 60 frames, using existing `motion.js` ticker pattern from spec 011; (b) suppress under `prefers-reduced-motion` (final value renders immediately); (c) accept an optional `Href` field that, when set, makes the tile keyboard-focusable + clickable; (d) emit `data-testid="kpi-tile-{slug}"`
- [ ] T045 [P] [US6] Edit each report-tab view (`AdminReports/Dashboard.cshtml`, `Applications.cshtml`, `Applicants.cshtml`, `Aging.cshtml`, `FundedItems.cshtml`) to apply `--space-2` row padding to their `_DataTable` instances — either via a partial parameter or by re-templating the table style
- [ ] T046 [P] [US6] Add E2E test `tests/FundingPlatform.Tests.E2E/AdminReportsTabUxTests.cs` covering: pill-chip styling assertion (DOM class + computed CSS for selected chip); KPI tile ticker animation on mount; reduced-motion final-value rendering; `--space-2` padding on the table rows

**Checkpoint**: US6 complete — KPI tiles + report tabs are warm-modern and motion-correct.

---

## Phase 7: User Story 2 — Sub-surface sweep at warm-modern bar (Priority: P1)

**Goal**: Every admin sub-surface in FR-008 passes the seven swept criteria (tokens, no inline `style=`, partials, voice-guide copy, typography, semantic restructure, semantic locators).

**Independent Test**: per quickstart.md §5 + ADMIN-SWEEP-CHECKLIST.md — manual checklist walk per surface; greps for raw hex / inline `style=` return zero; Playwright sweep tests pass with semantic POMs.

**Note on parallelism**: tasks T047–T056 are largely parallelizable across surfaces (each touches different cshtml files). However, voice-guide pass T057 must run after the structural rewrites so the copy review is on final markup.

- [ ] T047 [P] [US2] Sweep `/Admin/Users` views per ADMIN-SWEEP-CHECKLIST.md: `AdminUsers/Index.cshtml`, `Create.cshtml`, `Edit.cshtml`, `ResetPassword.cshtml` — apply 7 criteria; replace inline `style=` with token-based classes; `_PageHeader` / `_DataTable` / `_FormSection` / `_StatusPill` / `_ActionBar` / `_ConfirmDialog` (for ResetPassword + Disable) usage; semantic locators
- [ ] T048 [P] [US2] Sweep `/Admin/Groups` views: `AdminGroups/Index.cshtml`, `Create.cshtml`, `Edit.cshtml`, `Detail.cshtml` (if exists) — 7 criteria
- [ ] T049 [P] [US2] Sweep `/Admin/Suppliers` views: `AdminSuppliers/Index.cshtml`, `Detail.cshtml`, edit/approve flow views — 7 criteria
- [ ] T050 [P] [US2] Sweep `/Admin/Reports` views: 5 tabs already touched by Phase 6; verify 7 criteria plus the US6-specific changes — 7 criteria
- [ ] T051 [P] [US2] Sweep `/Admin/Currencies` views: `AdminCurrencies/Index.cshtml`, `Create.cshtml`, `Edit.cshtml` — 7 criteria
- [ ] T052 [P] [US2] Sweep `/Admin/ExchangeRates` views: `AdminExchangeRates/Index.cshtml`, `Create.cshtml` — 7 criteria
- [ ] T053 [P] [US2] Sweep `/Admin/LegacyQuotations` views: `AdminLegacyQuotations/Index.cshtml`, `Detail.cshtml` — 7 criteria
- [ ] T054 [P] [US2] Sweep impact-template views: `Admin/ImpactTemplates.cshtml`, `Admin/CreateTemplate.cshtml`, `Admin/EditTemplate.cshtml` — 7 criteria
- [ ] T055 [P] [US2] Sweep `/Admin/Configuration` view: `Admin/Configuration.cshtml` — 7 criteria
- [ ] T056 [US2] Run greps for raw hex (`/#[0-9a-fA-F]{3,8}/`) and inline `style=` across `Views/Admin/**/*.cshtml` and `Views/AdminUsers/`, `Views/AdminGroups/`, `Views/AdminSuppliers/`, `Views/AdminReports/`, `Views/AdminCurrencies/`, `Views/AdminExchangeRates/`, `Views/AdminLegacyQuotations/` — both MUST return zero (SC-005, SC-006); record results in `specs/017-admin-ux-facelift/sweep-grep-results.txt` (uncommitted)
- [ ] T057 [US2] Voice-guide review pass on every swept view's user-facing strings against spec 011's `BRAND-VOICE.md`; rewrite any that violate (no ALL CAPS shouting, no exclamation marks, no "submit" CTAs, no passive voice in microcopy) — record any controversial copy changes in PR description for designer/voice owner review (SC-018)
- [ ] T058 [US2] Walk `ADMIN-SWEEP-CHECKLIST.md` in the spec dir and tick every checkbox; commit the ticked checklist as proof of SC-007

### POM rewrites (parallel with sweeps where the new HTML is stable)

- [ ] T059 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminUsersPage.cs` against new HTML; semantic actions
- [ ] T060 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminGroupsPage.cs`
- [ ] T061 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminSuppliersPage.cs`
- [ ] T062 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminReportsPage.cs`
- [ ] T063 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminCurrenciesPage.cs`
- [ ] T064 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminExchangeRatesPage.cs`
- [ ] T065 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminLegacyQuotationsPage.cs`
- [ ] T066 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminImpactTemplatesPage.cs`
- [ ] T067 [P] [US2] Rewrite `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminConfigurationPage.cs`
- [ ] T068 [US2] Add a sweep regression smoke test `tests/FundingPlatform.Tests.E2E/AdminSweepRegressionTests.cs` that opens every swept surface as Admin and asserts the page renders with the expected partials' data-testids present (cheap "everything still loads" check)

**Checkpoint**: US2 complete — every admin sub-surface meets the 7 swept criteria.

---

## Phase 8: User Story 4 — Sidebar admin grouping (Priority: P2)

**Goal**: Sidebar admin sub-entries collapse under a visually-distinct "Administración" section header (slug `admin-section`); section header click target is `/Admin` (per R3); all prior admin slugs preserved.

**Independent Test**: per quickstart.md §4 — log in as Admin, observe section header + sub-entries; testid slugs preserved; non-Admin sees zero admin sidebar entries.

- [ ] T069 [US4] Edit `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` to refactor the sidebar entries list: split admin entries from top-level entries; render the admin entries inside a `<li>` group with a section header (also an `<a>` linking to `/Admin`) carrying `data-testid="admin-section"`; preserve all 7 admin sub-entry slugs (`sidebar-entry-users`, `sidebar-entry-groups`, `sidebar-entry-suppliers`, `sidebar-entry-reports`, `sidebar-entry-currencies`, `sidebar-entry-exchange-rates`, `sidebar-entry-legacy-quotations`); apply token-based visual treatment (subtle divider, indent, label typography)
- [ ] T070 [US4] Verify `IsEntryVisible` logic still gates non-Admin users out of the admin section header AND every admin sub-entry; if needed, add a "section visible iff any sub-entry visible" check
- [ ] T071 [P] [US4] Add E2E test `tests/FundingPlatform.Tests.E2E/AdminSidebarGroupingTests.cs` covering: Admin sees `admin-section` + all 7 sub-entry testids; Reviewer sees zero admin testids; Applicant sees zero admin testids; section header `<a>` href is `/Admin`

**Checkpoint**: US4 complete — sidebar grouping in place without testid regression.

---

## Phase 9: User Story 5 — Route normalization (Priority: P2)

**Goal**: Drop the `Admin` prefix from three controllers' public-facing routes (per R8 — attributes only, class names unchanged); old paths return 404 (no redirect shim, per FR-020).

**Independent Test**: per quickstart.md §3 — old paths 404, new paths 200, sidebar links navigate to new paths.

- [ ] T072 [US5] Edit `src/FundingPlatform.Web/Controllers/Admin/AdminCurrenciesController.cs` to add `[Route("Admin/Currencies")]` attribute on the controller (overrides default conventional route); leave class name as `AdminCurrenciesController`
- [ ] T073 [P] [US5] Edit `src/FundingPlatform.Web/Controllers/Admin/AdminExchangeRatesController.cs` to add `[Route("Admin/ExchangeRates")]`
- [ ] T074 [P] [US5] Edit `src/FundingPlatform.Web/Controllers/Admin/AdminLegacyQuotationsController.cs` to add `[Route("Admin/LegacyQuotations")]`
- [ ] T075 [US5] Update `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` sidebar URL strings from `/Admin/AdminCurrencies` etc. to `/Admin/Currencies` etc. (already partially done by US4 task T069 if pursued atomically; verify here)
- [ ] T076 [US5] Grep for any remaining references to the old path strings (`AdminCurrencies` / `AdminExchangeRates` / `AdminLegacyQuotations` in URLs only; not class names) across `Views/`, `wwwroot/`, controllers' `Url.Action` calls, and breadcrumbs — replace each
- [ ] T077 [US5] Verify `Url.Action("Index", "AdminCurrencies")` calls (if any) still resolve correctly because `Url.Action` uses the controller name minus `Controller`, not the route attribute — test with the full app boot
- [ ] T078 [P] [US5] Add E2E test `tests/FundingPlatform.Tests.E2E/AdminRouteNormalizationTests.cs` covering: old paths return 404 (no redirect); new paths return 200 for Admin; sidebar admin entries href values match the normalized paths

**Checkpoint**: US5 complete — route normalization landed without functional regression.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Verification work that crosses every story; closes the SC list.

- [ ] T079 Run integration tests `dotnet test tests/FundingPlatform.Tests.Integration` and confirm green (constitution III bar; no mocks)
- [ ] T080 Run E2E suite `dotnet test tests/FundingPlatform.Tests.E2E` end-to-end and confirm every previously-passing test still passes plus new spec-017 tests pass (SC-019)
- [ ] T081 Run reduced-motion E2E test specifically and assert the documented degradations on the dashboard + reports tabs
- [ ] T082 Run axe-playwright WCAG AA contrast check on: `/Admin` dashboard, one Reports tab, `/Admin/Users`, `/Admin/Suppliers`, `/Admin/Reports` default — all MUST pass (SC-015)
- [ ] T083 Run wire-weight check: `scripts/asset-budget-check.sh` and confirm the delta vs baseline (T003) is < 30 KB gzipped (SC-020)
- [ ] T084 Final schema-unchanged verification: `git diff --stat src/FundingPlatform.Database/` MUST be empty (SC-016); fail the PR if anything appears
- [ ] T085 Run PDF identity regression test `dotnet test tests/FundingPlatform.Tests.E2E --filter "Category=PdfIdentity"` and confirm Funding Agreement PDF visually identical to stored reference (SC-017)
- [ ] T086 Final voice-guide pass on every swept view's user-facing strings (closes SC-018); record changes in PR description
- [ ] T087 Designer/product review of `/Admin` dashboard against the 4 reference fixtures from SC-002; sign-off recorded in PR description per SC-021
- [ ] T088 Update PR description to enumerate: SC-001..SC-021 status, designer/product sign-off, voice-guide pass, axe-playwright result, wire-weight delta, schema-diff confirmation, reduced-motion test confirmation
- [ ] T089 Optional: tick every box in `specs/017-admin-ux-facelift/ADMIN-SWEEP-CHECKLIST.md` and commit the ticked file (SC-007)
- [ ] T090 Optional: run `dotnet build FundingPlatform.slnx` warning-clean as a final check before merge

---

## Dependencies

```text
Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1 dashboard MVP)
                                          → Phase 4 (US3 empty states)
                                          → Phase 5 (US7 activity feed)
                                          → Phase 6 (US6 reports tab UX)  ← required before SC-001
                                          → Phase 7 (US2 sub-surface sweep)
                                          → Phase 8 (US4 sidebar grouping)
                                          → Phase 9 (US5 route normalization)
                                          → Phase 10 (Polish)
```

- US1, US3, US6, US7 form the "dashboard core" — US6 must complete before US1 fully passes SC-001 (because the dashboard's KPI tiles use the re-templated `_KpiTile`).
- US2 sweep is parallelizable across the 9 sub-surfaces (T047–T055 all `[P]`).
- US4 + US5 + US7 are largely independent of US2 internals (each touches its own files).
- Polish (Phase 10) waits for all stories to land.

## MVP scope

US1 alone is a viable MVP: replace the 3-card landing with a capability-complete dashboard. US3 + US6 land alongside to keep visual consistency, but if release pressure forced a smaller cut, US1 + the deep-link fixes would deliver the user's primary unblock.

## Parallel opportunities

- T013, T014 in Phase 2 ([P])
- T023, T024, T025 in Phase 3 ([P])
- T027–T033, T036 in Phase 4 ([P])
- T041, T042 in Phase 5 ([P])
- T045, T046 in Phase 6 ([P])
- T047–T055, T059–T067 in Phase 7 ([P]) — biggest concurrent burst (9 sub-surfaces × {sweep, POM rewrite})
- T071 in Phase 8 ([P])
- T073, T074, T078 in Phase 9 ([P])

If the spex-teams extension is enabled, the dispatch granularity in Phase 7 maps cleanly to "one team per sub-surface" with shared partial inventory.

## Format validation

Every task above:

- ✅ Starts with `- [ ]` checkbox
- ✅ Has a sequential `Txxx` ID (T001..T090)
- ✅ Has `[P]` marker only when parallelizable
- ✅ Has `[USx]` story label only on user-story phase tasks (T015..T078); Setup / Foundational / Polish carry no story label
- ✅ Includes an exact file path
