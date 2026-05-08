# ADMIN-SWEEP-CHECKLIST — Spec 017

**Branch**: `017-admin-ux-facelift` · **Generated**: 2026-05-08
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

- [ ] `Views/Admin/Index.cshtml` — replaced with `_AdminDashboard` partial composition

7 criteria — single view, special case (it IS the wow moment):
- [ ] 1. tokens only
- [ ] 2. no inline style=
- [ ] 3. partials: `_PageHeader`, new `_AdminDashboard`, new `_CapabilityCard`, `_KpiTile`, `_EventTimeline` (when feed visible)
- [ ] 4. voice-guide es-CR
- [ ] 5. typography roles
- [ ] 6. structure restructured from 3-card legacy
- [ ] 7. semantic locators (testids: `admin-kpi-{slug}`, `admin-capability-{slug}`, `admin-activity-feed`)

### `/Admin/Users` (spec 009)

- [ ] `Views/AdminUsers/Index.cshtml`
- [ ] `Views/AdminUsers/Create.cshtml`
- [ ] `Views/AdminUsers/Edit.cshtml`
- [ ] `Views/AdminUsers/ResetPassword.cshtml` (or partial, depending on shape)

7 criteria each.

### `/Admin/Groups` (spec 016)

- [ ] `Views/AdminGroups/Index.cshtml`
- [ ] `Views/AdminGroups/Create.cshtml`
- [ ] `Views/AdminGroups/Edit.cshtml`
- [ ] `Views/AdminGroups/Detail.cshtml` (if exists)

7 criteria each.

### `/Admin/Suppliers` (spec 013)

- [ ] `Views/AdminSuppliers/Index.cshtml`
- [ ] `Views/AdminSuppliers/Detail.cshtml`
- [ ] Edit / approve flow views

7 criteria each.

### `/Admin/Reports` (spec 010, driven by US6)

- [ ] `Views/AdminReports/Dashboard.cshtml` (or `Index.cshtml`)
- [ ] `Views/AdminReports/Applications.cshtml`
- [ ] `Views/AdminReports/Applicants.cshtml`
- [ ] `Views/AdminReports/Aging.cshtml`
- [ ] `Views/AdminReports/FundedItems.cshtml`

Plus the partials they consume:

- [ ] `Views/Shared/Components/_ReportSubTabs.cshtml` (re-templated per FR-021)
- [ ] `Views/Shared/Components/_KpiTile.cshtml` (re-templated per FR-022)

7 criteria each.

### `/Admin/Currencies` (spec 015, route normalized)

- [ ] `Views/AdminCurrencies/Index.cshtml`
- [ ] `Views/AdminCurrencies/Create.cshtml`
- [ ] `Views/AdminCurrencies/Edit.cshtml`

7 criteria each. Route attribute change covered separately (US5).

### `/Admin/ExchangeRates` (spec 015, route normalized)

- [ ] `Views/AdminExchangeRates/Index.cshtml`
- [ ] `Views/AdminExchangeRates/Create.cshtml`

7 criteria each. Route attribute change covered separately (US5).

### `/Admin/LegacyQuotations` (spec 015, route normalized)

- [ ] `Views/AdminLegacyQuotations/Index.cshtml`
- [ ] `Views/AdminLegacyQuotations/Detail.cshtml`

7 criteria each. Route attribute change covered separately (US5).

### `/Admin/ImpactTemplates`

- [ ] `Views/Admin/ImpactTemplates.cshtml`
- [ ] `Views/Admin/CreateTemplate.cshtml`
- [ ] `Views/Admin/EditTemplate.cshtml`

7 criteria each.

### `/Admin/Configuration`

- [ ] `Views/Admin/Configuration.cshtml`

7 criteria.

## Empty-state coverage (per FR-012)

| Surface | Illustration scene |
|---|---|
| `/Admin/Users` index | `folders-stack` |
| `/Admin/Groups` index | `folders-stack` |
| `/Admin/Suppliers` index | `folders-stack` |
| `/Admin/Currencies` index | `folders-stack` |
| `/Admin/ExchangeRates` index | `folders-stack` |
| `/Admin/LegacyQuotations` index | `calm-horizon` |
| `/Admin/ImpactTemplates` | `folders-stack` |
| `/Admin/Reports` default | `soft-bar-chart` |
| Any filtered-search-no-results | `magnifier-on-empty` |

## Sidebar (US4)

- [ ] `Views/Shared/_Layout.cshtml` — admin entries collapse under "Administración" section header; section header's `data-testid` is `admin-section`; all prior admin slugs preserved.

## POM rewrites (per FR-010)

For each Playwright POM that exercises a swept admin surface:

- [ ] POM rewritten against the new HTML
- [ ] Semantic actions exposed (e.g., `usersList.SearchFor(text)`, `groupForm.SubmitWith(name)`) over raw locators
- [ ] ARIA roles + accessible names preferred; `data-testid` only where role/name are insufficient
- [ ] Tests assert the new UX (not just "page loaded")

## Final pass

- [ ] Greppable verification — `/#[0-9a-fA-F]{3,8}/` and `style=` both return zero across every cshtml in the inventory.
- [ ] axe-playwright clean on dashboard, Users index, Suppliers index, Reports default, one Reports tab.
- [ ] All E2E tests green; new spec-017 tests pass; reduced-motion test passes.
- [ ] Voice-guide review pass on every swept view.
- [ ] PR description records the designer/product sign-off per SC-021.

When every checkbox in this document is ticked, SC-007 holds.
