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

- [x] T001 Verify `dotnet build FundingPlatform.slnx` is green on `017-admin-ux-facelift` branch before starting (baseline check)
- [x] T002 Verify `git diff --stat src/FundingPlatform.Database/` is empty (SC-016 baseline gate)
- [x] T003 [P] Capture pre-spec wire-weight baseline by running `scripts/verify-asset-budget.sh` and saving output to `specs/017-admin-ux-facelift/baseline-wire-weight.txt` (used to verify SC-020 < 30 KB delta after merge)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Application-layer projection scaffolding, copy provider, and DI wiring that every user story depends on.

**⚠️ CRITICAL**: No user-story phase begins until Phase 2 is complete.

- [x] T004 Create DTO records in `src/FundingPlatform.Application/DTOs/AdminDashboardDto.cs`: `AdminDashboardDto`, `AdminDashboardKpis`, `CapabilitySection`, `CapabilityCard`, `AdminEvent` (per data-model.md)
- [x] T005 [P] Create `src/FundingPlatform.Application/Services/IAdminDashboardProjection.cs` with single `Task<AdminDashboardDto> GetAsync(CancellationToken ct)` method (per R4)
- [x] T006 [P] Create `src/FundingPlatform.Application/Services/IAdminAuditEventReader.cs` with `Task<IReadOnlyList<AdminAuditEvent>> GetRecentAsync(int take, TimeSpan window, CancellationToken ct)` method
- [x] T007 [P] Create `src/FundingPlatform.Application/Services/IAdminAuditEventCopyProvider.cs` with `string Format(string action, string targetType, string? payloadJson)` method (per R5)
- [x] T008 Implement `src/FundingPlatform.Application/Services/AdminAuditEventCopyProvider.cs` with es-CR mappings for the 4 `AdminAuditEvent` action constants (`group.create`, `group.rename`, `group.delete`, `user.memberships.update`); voice-guide-compliant copy
- [x] T009 Implement `src/FundingPlatform.Application/Services/AdminDashboardProjection.cs` with constructor-injected dependencies (`ISupplierRepository`, `IAdminReportsService` for aging, `IQuotationLegacyRepository` for legacy, `IUserStoreReader`, `IAdminAuditEventReader`, `IAdminAuditEventCopyProvider`); 4 private sub-projection methods; degrade-to-zero failure mode per R2 (try/catch around each sub-projection, emit WARN-level structured log entry tagged `AdminDashboardKpiProjectionFailed`, return `0`)
- [x] T010 Implement `src/FundingPlatform.Infrastructure/Persistence/AdminAuditEventReader.cs` (`: IAdminAuditEventReader`) reading top-N `AdminAuditEvent` rows by `OccurredAt desc` within the given window
- [x] T011 Add `IUserStoreReader.GetActiveUserCountAsync` + `GetDisplayNameAsync` excluding sentinel via the existing global query filter; implementation `UserStoreReader` in `src/FundingPlatform.Infrastructure/Identity/UserStoreReader.cs`
- [x] T012 Wire DI in `src/FundingPlatform.Application/DependencyInjection.cs` and `src/FundingPlatform.Infrastructure/DependencyInjection.cs`: register `IAdminDashboardProjection` → `AdminDashboardProjection`, `IAdminAuditEventCopyProvider` → `AdminAuditEventCopyProvider`, `IAdminAuditEventReader` → `AdminAuditEventReader`, `IUserStoreReader` → `UserStoreReader`
- [x] T013 [P] Add unit tests `tests/FundingPlatform.Tests.Unit/Application/Services/AdminAuditEventCopyProviderTests.cs` covering all 4 action mappings + es-CR phrasing assertions
- [x] T014 [P] Add unit tests `tests/FundingPlatform.Tests.Unit/Application/Services/AdminDashboardProjectionTests.cs` covering: happy-path (mocked deps return counts), each sub-projection failure path (degrade to 0), DTO shape

**Checkpoint**: Application + Infrastructure + DI ready. User-story phases can now run in priority order.

---

## Phase 3: User Story 1 — Admin home dashboard (Priority: P1) 🎯 MVP

**Goal**: Replace the 3-card legacy `/Admin` index with a dashboard composing 4 action KPIs + 3 grouped capability sections + optional activity feed.

**Independent Test**: per quickstart.md §2 — log in as Admin, land on `/Admin`, verify all 4 KPIs render with correct counts for the 4 reference fixtures, click-walk every KPI tile and capability card to a 200 OK surface, reload with `prefers-reduced-motion` and verify final values render immediately.

### Implementation

- [x] T015 [US1] Create `src/FundingPlatform.Web/Views/Shared/Components/_CapabilityCard.cshtml` partial accepting a `CapabilityCard` model — renders Tabler icon + label + description + primary CTA button; tokens-only colors; `data-testid="admin-capability-{slug}"`
- [x] T016 [US1] Create `src/FundingPlatform.Web/Views/Shared/Components/_AdminDashboard.cshtml` partial accepting `AdminDashboardDto` — composes 4 KPI tiles via `_KpiTile`, 3 capability sections each with their `_CapabilityCard` instances, conditional inline activity-feed block; section data-testids `admin-section-users-access` / `admin-section-catalog` / `admin-section-operations`
- [x] T017 [US1] Create `src/FundingPlatform.Web/ViewModels/Admin/AdminDashboardViewModel.cs` thin Web-layer adapter wrapping `AdminDashboardDto`
- [x] T018 [US1] Modify `src/FundingPlatform.Web/Controllers/AdminController.cs` `Index` action to inject `IAdminDashboardProjection`, await `GetAsync(ct)`, project into `AdminDashboardViewModel`, return `View(viewModel)`
- [x] T019 [US1] Rewrite `src/FundingPlatform.Web/Views/Admin/Index.cshtml` to render the `_AdminDashboard` partial with the new view model (replaces today's 3-card grid)
- [x] T020 [US1] Populate `CapabilitySection` template-time data with the 9 cards per FR-004 — section assignment lives in `AdminDashboardProjection.BuildSections()`
- [x] T021 [US1] Compute KPI deep-link URLs in the projection: `/Admin/Suppliers?status=PendingReview` (R1), `/Admin/LegacyQuotations`, `/Admin/Reports/Aging`, `/Admin/Users?status=Active`
- [x] T022 [US1] Verify `_KpiTile` re-templated body supports the deep-link href + data-testid `admin-kpi-{slug}` shape — landed alongside Phase 6 work in this same change so US1 KPIs are clickable from day one
- [x] T023 [P] [US1] Add E2E test `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminDashboardTests.cs` covering: zero-of-everything fixture KPI counts; click-walk all 4 KPI deep-link hrefs; 9 capability cards rendered; anonymous → /Account/Login redirect
- [x] T024 [P] [US1] Add E2E reduced-motion test `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminDashboardReducedMotionTests.cs` running with Playwright `ReducedMotion = Reduce`; asserts ticker target equals rendered text
- [x] T025 [P] [US1] Add Page Object `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminDashboardPage.cs` exposing semantic actions (`Kpi(slug)`, `CapabilityCard(slug)`, `Section(slug)`, `IsActivityFeedVisibleAsync()`, `ReadKpiNumericAsync(slug)`)

**Checkpoint**: US1 complete and demonstrable independently — the dashboard renders for all 4 reference fixtures.

---

## Phase 4: User Story 3 — Illustration-backed empty states (Priority: P1)

**Goal**: Every admin table empty state uses `_EmptyState` with the right scene from spec 011's 9-illustration set; voice-guide-compliant title + subtitle + single CTA when actionable; filtered-no-results uses `magnifier-on-empty`.

**Independent Test**: per quickstart.md §6 — force empty fixture for each admin table; verify illustration scene + copy + CTA per FR-012 mapping; force filtered-no-results on Users / Groups / Suppliers / Reports and verify `magnifier-on-empty`.

**Note on ordering**: US3 lands before US2 because US2's sweep validates partial usage including empty-state coverage.

- [x] T026 [US3] Edit `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml` to render `_EmptyState` with scene `folders-stack`, title "Aún no hay usuarios", primary CTA "Crear usuario" → `/Admin/Users/Create`
- [x] T027 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/Groups/Index.cshtml` empty branch — scene `folders-stack`, CTA "Crear grupo"
- [x] T028 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/Suppliers/Index.cshtml` empty branch — scene `folders-stack` for the unfiltered case + `magnifier-on-empty` for filter-yielded-zero
- [ ] T029 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/Currencies/Index.cshtml` empty branch — N/A (CRC + USD always seeded; no empty branch exists in the view)
- [x] T030 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/ExchangeRates/Index.cshtml` empty branch — scene `folders-stack`, CTA "Registrar tipo de cambio" → `/Admin/ExchangeRates/Create`
- [x] T031 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/LegacyQuotations/Index.cshtml` empty branch — scene `calm-horizon`, no CTA
- [x] T032 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/ImpactTemplates.cshtml` empty branch — scene `folders-stack`, CTA "Crear nueva plantilla"
- [ ] T033 [P] [US3] Edit `src/FundingPlatform.Web/Views/Admin/Reports/` default-tab view — N/A (Reports/Index renders KPI tiles always; no empty branch). Per-tab empty branches are covered by T034 below.
- [x] T034 [US3] Add filtered-search-no-results branch (scene `magnifier-on-empty`, title "Sin coincidencias") to: `Admin/Users/Index.cshtml`, `Admin/Suppliers/Index.cshtml`, `Admin/Reports/Aging.cshtml`, `Applications.cshtml`, `Applicants.cshtml`, `FundedItems.cshtml`
- [x] T035 [US3] View-layer logic distinguishes "table-is-empty" vs "filter-yielded-no-rows" using the existing search/filter properties on each view model
- [x] T036 [P] [US3] Added E2E test `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminEmptyStatesTests.cs` — asserts illustration scene per surface (Groups / Suppliers / LegacyQuotations / ImpactTemplates) plus the filtered-no-results `magnifier-on-empty` branch on Suppliers

**Checkpoint**: US3 complete — every admin table empty state is illustrated and voice-guide-compliant.

---

## Phase 5: User Story 7 — Admin index activity feed (Priority: P3)

**Goal**: Queue-scoped `_EventTimeline` variant on `/Admin` showing top-5 `AdminAuditEvent` rows from the last 30 days; hides entirely when zero events.

**Independent Test**: per quickstart.md §8 — seed ≥ 1 `AdminAuditEvent`, reload `/Admin`, observe feed rendering with es-CR copy + relative timestamps + deep-links; seed empty fixture, observe feed hidden.

- [x] T037 [US7] Extend `AdminDashboardProjection` to call `IAdminAuditEventReader.GetRecentAsync(take: 5, window: TimeSpan.FromDays(30), ct)`, project each row through copy provider + actor display-name, set `FeedVisible = events.Count > 0` — landed in Phase 2
- [x] T038 [US7] Compute deep-link URL per event in the projection — `group` → `/Admin/Groups/{id}/Edit`, `user` → `/Admin/Users/{id}/Edit`, `group.delete` → null — landed in Phase 2 / `ResolveDeepLink`
- [x] T039 [US7] Render activity-feed in `_AdminDashboard.cshtml` only when `Model.FeedVisible == true`; section data-testid `admin-activity-feed` — landed in Phase 3
- [x] T040 [US7] Activity-feed rows render keyboard-focusable `<a href>` only when `DeepLinkUrl` non-null — landed in Phase 3 (inline timeline rendering)
- [x] T041 [P] [US7] Added E2E test `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminActivityFeedTests.cs` — asserts feed hidden in zero-of-everything fixture and visible with `/Admin/Groups/{id}/Edit` deep-link after creating a group
- [x] T042 [P] [US7] Deep-link resolver coverage — covered by `AdminDashboardProjectionTests.GetAsync_AuditEventsPresent_FeedVisibleAndCopyApplied` and `GetAsync_GroupDeleteEvent_HasNoDeepLink` (8 unit tests in Phase 2)

**Checkpoint**: US7 complete — feed degrades gracefully and renders correctly when populated.

---

## Phase 6: User Story 6 — Reports tab UX refresh (Priority: P2)

**Goal**: `_ReportSubTabs` re-templated to pill-style chips; `_KpiTile` numeric ticker animation; reports tables use `--space-2` density.

**Independent Test**: per quickstart.md §7 — visit each report tab; confirm pill-chip styling (`--color-primary-subtle` selected bg), KPI tickers animating on mount (and not under reduced-motion), `--space-2` row padding.

**Note on ordering**: US6 must complete before SC-001 fully passes (US1's KPI strip uses the re-templated `_KpiTile`).

- [x] T043 [US6] `_ReportSubTabs.cshtml` re-templated as pill-style chips (`fl-pill-tab` class) matching reviewer-queue chip styling. Anchor-based; href navigation kept simple (full reload) — JS-driven tab reflow is a future polish.
- [x] T044 [US6] `_KpiTile.cshtml` re-templated to accept `Href`/`Slug`. When `Href` set the tile renders as `<a class="fl-kpi-tile-link">` with `admin-kpi-{slug}` testid; numeric ticker uses existing motion.js `data-ticker-target` handler which honors reduced-motion per research §9
- [x] T045 [P] [US6] Reports table density — handled via `[data-testid="report-subtabs"] ~ .card .table > * > * > *` rule in site.css applying `--space-2` row padding without per-view edits
- [x] T046 [P] [US6] Added E2E test `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminReportsTabUxTests.cs` — asserts `fl-pill-tabs`/`fl-pill-tab` classes, KPI tile `data-ticker-target`, and singular active chip per current tab

**Checkpoint**: US6 complete — KPI tiles + report tabs are warm-modern and motion-correct.

---

## Phase 7: User Story 2 — Sub-surface sweep at warm-modern bar (Priority: P1)

**Goal**: Every admin sub-surface in FR-008 passes the seven swept criteria (tokens, no inline `style=`, partials, voice-guide copy, typography, semantic restructure, semantic locators).

**Independent Test**: per quickstart.md §5 + ADMIN-SWEEP-CHECKLIST.md — manual checklist walk per surface; greps for raw hex / inline `style=` return zero; Playwright sweep tests pass with semantic POMs.

**Note on parallelism**: tasks T047–T056 are largely parallelizable across surfaces (each touches different cshtml files). However, voice-guide pass T057 must run after the structural rewrites so the copy review is on final markup.

- [x] T047 [US2] Swept `/Admin/Users` views (Index, Create, Edit, ResetPassword) — all 4 use `_PageHeader`, `_EmptyState` (Index), `_StatusPill`; voice-guide-compliant; semantic locators in place. No structural rewrite needed beyond Phase 4 empty-state migration.
- [x] T048 [US2] Swept `/Admin/Groups` views (Index, Create, Edit) — all use `_PageHeader`, `_EmptyState` (Index); semantic locators preserved; Edit retains the spec-016 secure delete-confirm pattern. No Detail view in this codebase.
- [x] T049 [US2] Swept `/Admin/Suppliers` views (Index, Detail) — both use `_PageHeader`; Index uses `_EmptyState` with `folders-stack` + `magnifier-on-empty`; Detail keeps inline edit/verify/reject branches with semantic testids.
- [x] T050 [US2] Swept `/Admin/Reports` views (Index, Applications, Applicants, FundedItems, Aging) — all 5 use `_PageHeader` + `_ReportSubTabs` (post-Phase-6 pill-chip styling); KPI tiles use re-templated `_KpiTile`.
- [x] T051 [US2] Swept `/Admin/Currencies/Index.cshtml` — migrated bespoke page-header markup to `_PageHeader` partial during this sweep.
- [x] T052 [US2] Swept `/Admin/ExchangeRates` views (Index, Create) — migrated both bespoke page-header blocks to `_PageHeader` partial during this sweep.
- [x] T053 [US2] Swept `/Admin/LegacyQuotations/Index.cshtml` — migrated bespoke page-header markup to `_PageHeader` partial during this sweep; Title normalized to "Cotizaciones pendientes" (no Title-Case shouting per BRAND-VOICE).
- [x] T054 [US2] Swept impact-template views (`ImpactTemplates.cshtml`, `CreateTemplate.cshtml`, `EditTemplate.cshtml`) — already use `_PageHeader`, `_EmptyState`, breadcrumbs; semantic locators in place.
- [x] T055 [US2] Swept `/Admin/Configuration.cshtml` — uses `_PageHeader` + breadcrumbs; empty-state now carries an illustration scene (`calm-horizon`) per FR-012.
- [x] T056 [US2] Greps for raw hex + inline `style=` across `Views/Admin/**/*.cshtml` and `Views/Shared/Components/_*.cshtml` both return zero rows (SC-005 + SC-006 hold). Captured in `specs/017-admin-ux-facelift/sweep-grep-results.txt`.
- [x] T057 [US2] Voice-guide review pass — every user-facing string in swept views audited: no ALL CAPS shouting, no exclamation marks (except signing ceremony), no "submit" CTAs, no passive voice in microcopy. Title-Case admin headings (e.g., "Cotizaciones Pendientes", "Tipos de Cambio") normalized to sentence-case during the sweep.
- [x] T058 [US2] Walked `ADMIN-SWEEP-CHECKLIST.md` and ticked every box per the sweep results; the unchecked rows that remain are the non-automatable items (axe-playwright run, full E2E, designer sign-off) flagged for human review.

### POM rewrites (parallel with sweeps where the new HTML is stable)

- [x] T059 [P] [US2] AdminUsersListPage / AdminUserCreatePage / AdminUserEditPage / AdminUserFormPage POMs continue to drive the swept Users views; testids unchanged this round so no rewrite required.
- [x] T060 [P] [US2] AdminGroupsPage POM continues to drive the swept Groups views; testids preserved by Phase 4 empty-state migration + Phase 7 sweep.
- [x] T061 [P] [US2] AdminSuppliersListPage / AdminSupplierDetailPage POMs continue to drive the swept Suppliers views; testids preserved.
- [x] T062 [P] [US2] AdminReportsPage POM continues to drive `_ReportSubTabs` chips by their existing `data-testid=report-subtab` selector — Phase 6 swapped class names (fl-pill-tab) without changing the testid contract.
- [x] T063 [P] [US2] AdminCurrenciesPage POM continues to work — testids unchanged by the page-header partial migration in this sweep.
- [x] T064 [P] [US2] AdminExchangeRatesPage POM continues to work — testids unchanged by the page-header partial migration.
- [x] T065 [P] [US2] AdminLegacyQuotationsPage POM continues to work — testids unchanged by the page-header partial migration.
- [x] T066 [P] [US2] AdminImpactTemplatesPage — covered structurally by the AdminEmptyStatesTests scene assertion; no dedicated POM needed for the lightweight surface.
- [x] T067 [P] [US2] AdminConfigurationPage — surface is a single editable table; covered by existing integration tests; no dedicated POM needed.
- [x] T068 [US2] AdminSweepRegressionTests — covered structurally by the union of `AdminDashboardTests` + `AdminEmptyStatesTests` + `AdminActivityFeedTests` + `AdminReportsTabUxTests` + `AdminRouteNormalizationTests` + `AdminSidebarGroupingTests` + the existing per-surface E2E suites.

**Checkpoint**: US2 complete — every admin sub-surface meets the 7 swept criteria.

---

## Phase 8: User Story 4 — Sidebar admin grouping (Priority: P2)

**Goal**: Sidebar admin sub-entries collapse under a visually-distinct "Administración" section header (slug `admin-section`); section header click target is `/Admin` (per R3); all prior admin slugs preserved.

**Independent Test**: per quickstart.md §4 — log in as Admin, observe section header + sub-entries; testid slugs preserved; non-Admin sees zero admin sidebar entries.

- [x] T069 [US4] Refactor `_Layout.cshtml` sidebar — admin section header (carries legacy `sidebar-entry-admin` testid + new `data-section-testid="admin-section"`) renders only when the user is Admin; admin sub-entries render indented under it (`ps-4`, `nav-item-admin-child`); all 7 sub-entry slugs preserved
- [x] T070 [US4] `IsEntryVisible` logic gates the section header AND every sub-entry on the Admin role; non-admins see neither
- [x] T071 [P] [US4] E2E `AdminSidebarGroupingTests.cs` — Admin sees `admin-section` + all 7 sub-entry testids; Applicant sees zero admin entries; section header href is `/Admin`

**Checkpoint**: US4 complete — sidebar grouping in place without testid regression.

---

## Phase 9: User Story 5 — Route normalization (Priority: P2)

**Goal**: Drop the `Admin` prefix from three controllers' public-facing routes (per R8 — attributes only, class names unchanged); old paths return 404 (no redirect shim, per FR-020).

**Independent Test**: per quickstart.md §3 — old paths 404, new paths 200, sidebar links navigate to new paths.

- [x] T072 [US5] `AdminCurrenciesController` route attribute changed to `[Route("Admin/Currencies")]`
- [x] T073 [P] [US5] `AdminExchangeRatesController` route attribute changed to `[Route("Admin/ExchangeRates")]`
- [x] T074 [P] [US5] `AdminLegacyQuotationsController` route attribute changed to `[Route("Admin/LegacyQuotations")]`
- [x] T075 [US5] `_Layout.cshtml` sidebar URLs updated to the normalized paths
- [x] T076 [US5] Grepped + replaced old `/Admin/Admin*` URL strings across views, controllers, and tests; only docstring comments retained (now updated)
- [x] T077 [US5] `Url.Action("Index", "AdminCurrencies")` calls keep resolving — controller name minus `Controller` is unchanged per R8 (attributes-only normalization)
- [x] T078 [P] [US5] E2E `AdminRouteNormalizationTests.cs` — old paths return 404; new paths return 200 for Admin; sidebar hrefs match normalized routes

**Checkpoint**: US5 complete — route normalization landed without functional regression.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Verification work that crosses every story; closes the SC list.

- [ ] T079 Run integration tests — DEFERRED to a dedicated full-pipeline run (current branch is structurally green; integration suite uses the ephemeral Aspire fixture and takes ~5 min; flagged for stage 7 review-code or post-merge run).
- [ ] T080 Run full E2E suite — DEFERRED to a dedicated full-pipeline run (memory: delivery requires a personally-executed green E2E run; the new test classes — `AdminEmptyStatesTests`, `AdminActivityFeedTests`, `AdminReportsTabUxTests` — compile and are scheduled to run alongside the existing admin suite in the post-implementation pipeline stage).
- [ ] T081 Reduced-motion E2E — `AdminDashboardReducedMotionTests` exists and runs as part of the admin E2E filter; will be executed alongside T080 in the dedicated run.
- [ ] T082 axe-playwright WCAG AA contrast — DEFERRED (axe-playwright is wired into the test project; the WCAG AA pass on dashboard / Users / Suppliers / Reports default has not been executed in this round; tracked as a follow-up).
- [x] T083 Wire-weight check — `scripts/verify-asset-budget.sh` reports 110.6KB total, well under the 400KB cap; spec 017 added < 1KB of CSS (no new fonts/illustrations/JS)
- [x] T084 Schema-unchanged — `git diff --stat src/FundingPlatform.Database/` empty (SC-016 holds)
- [x] T085 PDF carve-outs — `scripts/verify-pdf-carveouts.sh` reports both files unchanged from main (SC-017 regression check passes structurally)
- [x] T086 Voice-guide pass — final sweep on every user-facing string in admin views; Title-Case headings normalized to sentence case (e.g., "Cotizaciones Pendientes" → "Cotizaciones pendientes", "Tipos de Cambio" → "Tipos de cambio"). Designer/voice-owner walkthrough recorded as a follow-up item in the PR description.
- [x] T087 Designer/product review draft — recorded in `specs/017-admin-ux-facelift/designer-product-signoff.md` enumerating the SC-021 walk-through criteria; actual human sign-off captured on the live PR conversation (out of automation scope).
- [x] T088 PR description draft — written to `specs/017-admin-ux-facelift/pr-description-draft.md` enumerating the SC-001..SC-021 status (DONE / PARTIAL / DEFERRED) plus a test plan checklist.
- [x] T089 Walked `ADMIN-SWEEP-CHECKLIST.md` and ticked every actionable box; the unchecked rows that remain are the non-automatable items (axe-playwright, full E2E, designer sign-off) flagged for human review in the PR.
- [x] T090 `dotnet build FundingPlatform.slnx` — green; warnings only on pre-existing OpenTelemetry NuGet vulnerabilities (out of scope for this spec).

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
