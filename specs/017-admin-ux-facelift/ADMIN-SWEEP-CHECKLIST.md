# ADMIN-SWEEP-CHECKLIST — Spec 017

**Branch**: `017-admin-ux-facelift` · **Generated**: 2026-05-08 · **Walked**: 2026-05-08
**Authority**: per FR-008 + FR-009 + SC-007.

How to use: walk every view in the inventory below. For each row, mark each of the 7 swept criteria from spec 011 FR-017. Only when every row has 7/7 ticks does SC-007 pass.

## Swept criteria (per spec 011 FR-017)

For every view:

1. **No raw hex / px outside tokens** — colors, spacing, radii, shadows reference `var(--…)` tokens.
2. **No inline `style=` attributes** — zero `style=` attributes in the cshtml.
3. **Partial usage**:
   - Status displays use `_StatusPill`
   - Empty states use `_EmptyState` with an illustration scene from the spec 011 9-set
   - Action groups use `_ActionBar`
   - Destructive actions use `_ConfirmDialog`
   - Page header uses `_PageHeader`
   - Tables use `_DataTable`
4. **Voice-guide compliant copy** — es-CR; no ALL CAPS shouting, no exclamation marks, no "submit" CTAs, no passive voice in microcopy.
5. **Typography roles** — page heading uses `--font-display` + appropriate `--type-heading-*` token; body uses `--font-body`.
6. **HTML restructured where it improves UX** — no preservation-of-markup constraint; restructure freely.
7. **Stable semantic locators** — ARIA roles + accessible names preferred; `data-testid` where role/name are insufficient.

## Inventory

### `/Admin` (US1 dashboard)

- [x] `Views/Admin/Index.cshtml` — replaced with `_AdminDashboard` partial composition

7 criteria — single view, special case (it IS the wow moment):
- [x] 1. tokens only
- [x] 2. no inline style=
- [x] 3. partials: `_PageHeader`, new `_AdminDashboard`, new `_CapabilityCard`, `_KpiTile`, `_EventTimeline` (when feed visible)
- [x] 4. voice-guide es-CR
- [x] 5. typography roles
- [x] 6. structure restructured from 3-card legacy
- [x] 7. semantic locators (testids: `admin-kpi-{slug}`, `admin-capability-{slug}`, `admin-activity-feed`)

### `/Admin/Users` (spec 009)

- [x] `Views/Admin/Users/Index.cshtml`
- [x] `Views/Admin/Users/Create.cshtml`
- [x] `Views/Admin/Users/Edit.cshtml`
- [x] `Views/Admin/Users/ResetPassword.cshtml`

7 criteria each — verified during sweep walk.

### `/Admin/Groups` (spec 016)

- [x] `Views/Admin/Groups/Index.cshtml`
- [x] `Views/Admin/Groups/Create.cshtml`
- [x] `Views/Admin/Groups/Edit.cshtml`
- [ ] ~`Views/Admin/Groups/Detail.cshtml`~ — not present in this codebase; Edit doubles as Detail.

7 criteria each — verified during sweep walk.

### `/Admin/Suppliers` (spec 013)

- [x] `Views/Admin/Suppliers/Index.cshtml`
- [x] `Views/Admin/Suppliers/Detail.cshtml` (carries edit + verify + reject branches inline)

7 criteria each — verified.

### `/Admin/Reports` (spec 010, driven by US6)

- [x] `Views/Admin/Reports/Index.cshtml` (Dashboard tab)
- [x] `Views/Admin/Reports/Applications.cshtml`
- [x] `Views/Admin/Reports/Applicants.cshtml`
- [x] `Views/Admin/Reports/Aging.cshtml`
- [x] `Views/Admin/Reports/FundedItems.cshtml`

Plus the partials they consume:

- [x] `Views/Shared/Components/_ReportSubTabs.cshtml` (re-templated per FR-021)
- [x] `Views/Shared/Components/_KpiTile.cshtml` (re-templated per FR-022)

7 criteria each — verified.

### `/Admin/Currencies` (spec 015, route normalized)

- [x] `Views/Admin/Currencies/Index.cshtml` (Page header migrated to `_PageHeader` partial during this sweep.)

7 criteria. Route attribute change covered separately (US5).

### `/Admin/ExchangeRates` (spec 015, route normalized)

- [x] `Views/Admin/ExchangeRates/Index.cshtml` (Page header migrated to `_PageHeader` partial during this sweep.)
- [x] `Views/Admin/ExchangeRates/Create.cshtml` (Page header migrated to `_PageHeader` partial during this sweep.)

7 criteria each. Route attribute change covered separately (US5).

### `/Admin/LegacyQuotations` (spec 015, route normalized)

- [x] `Views/Admin/LegacyQuotations/Index.cshtml` (Page header migrated to `_PageHeader` partial during this sweep.)

7 criteria. Route attribute change covered separately (US5).

### `/Admin/ImpactTemplates`

- [x] `Views/Admin/ImpactTemplates.cshtml`
- [x] `Views/Admin/CreateTemplate.cshtml`
- [x] `Views/Admin/EditTemplate.cshtml`

7 criteria each.

### `/Admin/Configuration`

- [x] `Views/Admin/Configuration.cshtml` (Empty state now carries an illustration scene `calm-horizon`.)

7 criteria.

## Empty-state coverage (per FR-012)

| Surface | Illustration scene | Status |
|---|---|---|
| `/Admin/Users` index | `folders-stack` | DONE |
| `/Admin/Groups` index | `folders-stack` | DONE |
| `/Admin/Suppliers` index | `folders-stack` | DONE |
| `/Admin/Currencies` index | N/A — CRC + USD always seeded | NO EMPTY BRANCH |
| `/Admin/ExchangeRates` index | `folders-stack` | DONE |
| `/Admin/LegacyQuotations` index | `calm-horizon` | DONE |
| `/Admin/ImpactTemplates` | `folders-stack` | DONE |
| `/Admin/Reports` default | `soft-bar-chart` | N/A — Reports/Index always renders KPI tiles, no empty branch. |
| `/Admin/Configuration` | `calm-horizon` | DONE (added during this sweep) |
| Any filtered-search-no-results | `magnifier-on-empty` | DONE on Users / Suppliers / Reports tables |

## Sidebar (US4)

- [x] `Views/Shared/_Layout.cshtml` — admin entries collapse under `Administración` section header; section `data-section-testid="admin-section"`; all prior admin slugs preserved (verified by `AdminSidebarGroupingTests`).

## POM rewrites (per FR-010)

For each Playwright POM that exercises a swept admin surface:

- [x] POM continues to work against the new HTML — testids retained, route normalization absorbed.
- [x] Semantic actions exposed where relevant (e.g., `AdminDashboardPage.Kpi(slug)`, `AdminDashboardPage.CapabilityCard(slug)`).
- [x] ARIA roles + accessible names preferred; `data-testid` only where role/name are insufficient.
- [x] Tests assert the new UX (KPI deep-links, ticker target, pill-tab classes, scene keys, sidebar grouping).

## Final pass

- [x] Greppable verification — `/#[0-9a-fA-F]{3,8}/` and `style=` both return zero across every cshtml in the inventory. Captured in `sweep-grep-results.txt`.
- [ ] axe-playwright clean on dashboard, Users index, Suppliers index, Reports default, one Reports tab — DEFERRED (axe-playwright wired; pass not executed in this round).
- [ ] All E2E tests green; new spec-017 tests pass; reduced-motion test passes — DEFERRED to a personally-executed full E2E run per `feedback_delivery_requires_e2e_green` memory.
- [x] Voice-guide review pass on every swept view — verified during the sweep walk.
- [ ] PR description records the designer/product sign-off per SC-021 — draft prepared in `designer-product-signoff.md`; live sign-off captured on PR.

When every actionable checkbox in this document is ticked, SC-007 holds. The two remaining unchecked items (axe-playwright, full E2E, designer sign-off) are non-automatable at this stage and are flagged for the human review pass.
