# Quickstart — Spec 017 Admin UX/UI Facelift

**Branch**: `017-admin-ux-facelift` · **Date**: 2026-05-08

How to build, run, and validate the spec 017 work locally. Assumes `dotnet 10` SDK + Aspire bits are installed (per `CLAUDE.md`).

## 1. Build & boot

```bash
# whole solution
dotnet build FundingPlatform.slnx

# dev with persistent SQL data + auto-deployed dacpac
dotnet run --project src/FundingPlatform.AppHost
```

Then open: `http://localhost:5078/Admin`.

Sentinel admin login (ephemeral mode): `admin@FundingPlatform.com` / `Sentinel123!`. In dev, the standard demo seeded admin (`admin@demo.com`) also works.

## 2. Verify dashboard (US1)

Land on `/Admin`. Expect to see, top-down:
1. Page header ("Panel de administración" or new title — per voice-guide pass).
2. **KPI strip** with 4 tiles (Pending suppliers / Pending legacy quotations / Aging applications / Active users). Each animates from 0 → final on first paint.
3. **Three capability sections** with the cards listed in FR-004 (Usuarios y acceso → Users + Groups; Catálogo → Suppliers + Currencies + Exchange Rates + Impact Templates; Operaciones → Reports + Legacy Quotations + System Configuration).
4. **Activity feed** (only when `AdminAuditEvent` has ≥ 1 row in the last 30 days; otherwise hidden — no empty rail).

### Click-walk every KPI tile

| Tile | Expected destination |
|---|---|
| Pending suppliers | `/Admin/Suppliers?status=PendingReview` |
| Pending legacy quotations | `/Admin/LegacyQuotations` |
| Aging applications | `/Admin/Reports/Aging` |
| Active users | `/Admin/Users?status=Active` |

Each MUST return HTTP 200.

### Click-walk every capability card

| Card | Expected destination |
|---|---|
| Users | `/Admin/Users` |
| Groups | `/Admin/Groups` |
| Suppliers | `/Admin/Suppliers` |
| Currencies | `/Admin/Currencies` |
| Exchange Rates | `/Admin/ExchangeRates` |
| Impact Templates | `/Admin/ImpactTemplates` |
| Reports | `/Admin/Reports` |
| Legacy Quotations | `/Admin/LegacyQuotations` |
| System Configuration | `/Admin/Configuration` |

Each MUST return HTTP 200.

### Reduced-motion check

```bash
# Chromium devtools → Rendering → Emulate CSS media feature prefers-reduced-motion: reduce
```

KPI tickers MUST render their final values immediately. No animation observed.

## 3. Verify route normalization (US5)

```bash
# old paths return 404
curl -I http://localhost:5078/Admin/AdminCurrencies        # 404
curl -I http://localhost:5078/Admin/AdminExchangeRates     # 404
curl -I http://localhost:5078/Admin/AdminLegacyQuotations  # 404

# new paths return 200 (after auth)
# log in via the standard login first, then:
curl -I http://localhost:5078/Admin/Currencies             # 200
curl -I http://localhost:5078/Admin/ExchangeRates          # 200
curl -I http://localhost:5078/Admin/LegacyQuotations       # 200
```

Sidebar links MUST navigate to the normalized routes. Inspect via DOM: `document.querySelectorAll('[data-testid^=sidebar-entry-]')` and confirm `currencies` / `exchange-rates` / `legacy-quotations` href values match the normalized paths.

## 4. Verify sidebar grouping (US4)

Log in as Admin. Sidebar MUST show:
- Top-level: Inicio · Cola de revisión · Bandeja de firmas
- "Administración" section header (linked to `/Admin`, slug `admin-section`)
- Indented under it: Users · Groups · Suppliers · Reports · Currencies · Exchange Rates · Legacy Quotations

Log out, log in as Reviewer or Applicant. Sidebar MUST NOT show the section header or any admin sub-entry.

DOM check (any role):
```js
[...document.querySelectorAll('[data-testid^=sidebar-entry-]')].map(e => e.dataset.testid)
```

Admin: includes `admin-section`, `sidebar-entry-users`, `sidebar-entry-groups`, …, `sidebar-entry-legacy-quotations`.
Reviewer / Applicant: zero admin-related slugs.

## 5. Verify sub-surface sweep (US2 + US3)

For each surface in the inventory (FR-008), open and verify:

```text
☐ No raw hex literals in the cshtml (grep)
☐ No inline style= attributes (grep)
☐ Status displays use _StatusPill
☐ Empty states use _EmptyState with an illustration scene from the spec 011 set
☐ Action groups use _ActionBar
☐ Destructive actions use _ConfirmDialog
☐ Page heading uses --font-display + --type-heading-* tokens
☐ Body copy uses --font-body
☐ Voice-guide-compliant es-CR copy
☐ Semantic locators present (ARIA roles + accessible names; data-testid as fallback)
```

Greppable verification:

```bash
# raw hex outside tokens.css and PDF carve-outs
grep -rEn '#[0-9a-fA-F]{3,8}' src/FundingPlatform.Web/Views/Admin/ \
  src/FundingPlatform.Web/Views/AdminUsers/ \
  src/FundingPlatform.Web/Views/AdminGroups/ \
  src/FundingPlatform.Web/Views/AdminSuppliers/ \
  src/FundingPlatform.Web/Views/AdminReports/ \
  src/FundingPlatform.Web/Views/AdminCurrencies/ \
  src/FundingPlatform.Web/Views/AdminExchangeRates/ \
  src/FundingPlatform.Web/Views/AdminLegacyQuotations/ \
  --include='*.cshtml' || echo "no hex"

# inline style=
grep -rn 'style=' src/FundingPlatform.Web/Views/Admin/ \
  src/FundingPlatform.Web/Views/Admin*/ \
  --include='*.cshtml' || echo "no inline style"
```

Both MUST return zero rows.

## 6. Verify empty states (US3)

For each admin table, force an empty fixture (the E2E `AspireFixture` zero-of-everything scenario) and observe:

| Surface | Illustration scene | Copy hint |
|---|---|---|
| Users index | `folders-stack` | "Aún no hay usuarios" + CTA "Crear usuario" |
| Groups index | `folders-stack` | "Cree su primer grupo" + CTA |
| Suppliers index (default `PendingReview`) | `folders-stack` | "Sin proveedores pendientes" — no CTA |
| Currencies index | `folders-stack` | "Aún no hay monedas registradas" + CTA |
| Exchange Rates index | `folders-stack` | "Aún no hay tipos de cambio" + CTA |
| Legacy Quotations index | `calm-horizon` | "Sin cotizaciones pendientes" — no CTA |
| Impact Templates index | `folders-stack` | "Cree su primera plantilla" + CTA |
| Reports default | `soft-bar-chart` | "Aún no hay datos" — no CTA |

Filtered-search-no-results MUST render `magnifier-on-empty` with "Sin coincidencias" / "Pruebe con otros filtros".

## 7. Verify Reports tab UX (US6)

Visit `/Admin/Reports`. Tabs MUST render as pill chips matching the reviewer-queue filter chips:
- Selected: `--color-primary-subtle` background + `--color-primary` text
- Unselected: subtle outline / muted text
- Reflow on click MUST be smooth (`--motion-base`); no full page reload

KPI tiles in any report tab MUST animate from 0 → final value over `--motion-slow` on mount; reduced-motion suppresses.

## 8. Verify activity feed (US7)

### With events

Seed at least one `AdminAuditEvent` (e.g., as Admin, edit a Group → triggers `group.rename`). Reload `/Admin`. Activity feed MUST render the event with:
- Actor display name
- es-CR action copy ("renombró el grupo {target}")
- Relative timestamp
- Deep-link to `/Admin/Groups/{id}/Edit`

### Without events

Seed scenario with zero events in the last 30 days. Reload `/Admin`. Activity feed section MUST be hidden entirely. No empty rail. DOM: no element with `data-testid="admin-activity-feed"`.

## 9. Run the test suites

```bash
# unit (projections + copy provider + sub-projection failure mode)
dotnet test tests/FundingPlatform.Tests.Unit

# integration (DB-backed projection counts)
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (Playwright; spec 017 surfaces)
dotnet test tests/FundingPlatform.Tests.E2E --filter "Category=Spec017"
```

All MUST pass. The reduced-motion E2E test runs as part of the spec017 filter.

## 10. Verify schema-unchanged (SC-016)

```bash
git diff --stat src/FundingPlatform.Database/
# expected: zero output
```

If anything appears, escalate via `/speckit-spex-evolve` per FR-027.

## 11. Verify PDF identity (SC-017)

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "Category=PdfIdentity"
```

Funding Agreement PDF MUST be byte-identical to the stored reference. This spec touches no PDF surface; the test is a regression check.

## 12. Verify wire weight (SC-020)

```bash
scripts/asset-budget-check.sh
```

Combined incremental wire weight added by spec 017 MUST be < 30 KB gzipped (no new fonts, no new libraries; only new partials + projection code).

---

## What "done" looks like

- [ ] All 9 capability cards click to a 200 OK surface (SC-003).
- [ ] All 4 KPIs render correct counts for all 4 reference fixtures (SC-002).
- [ ] Reduced-motion test passes (SC-004).
- [ ] Greps return zero raw hex / inline style (SC-005, SC-006).
- [ ] ADMIN-SWEEP-CHECKLIST.md walked end to end (SC-007).
- [ ] All admin empty states show the right illustration (SC-008).
- [ ] Filtered-no-results uses `magnifier-on-empty` (SC-009).
- [ ] Sidebar admin section header + sub-entries render with stable testids (SC-010).
- [ ] Non-Admin sees zero admin sidebar entries (SC-011).
- [ ] Old routes 404 / new routes 200 (SC-012).
- [ ] Reports tabs styled as pill chips + tickers animate (SC-013).
- [ ] Activity feed visible/hidden per data presence (SC-014).
- [ ] axe-playwright passes WCAG AA on dashboard + a reports tab + Users index + Suppliers index + Reports default (SC-015).
- [ ] `git diff --stat src/FundingPlatform.Database/` empty (SC-016).
- [ ] PDF identity preserved (SC-017).
- [ ] Voice-guide checklist clean (SC-018).
- [ ] Full E2E suite green; new tests + reduced-motion test pass (SC-019).
- [ ] Wire weight < 30 KB gzipped (SC-020).
- [ ] Designer/product sign-off recorded in PR description (SC-021).
