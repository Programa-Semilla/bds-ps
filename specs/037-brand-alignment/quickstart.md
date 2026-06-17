# Quickstart: Programa Semilla Official Brand Alignment (037)

How to build, run, and verify this facelift. No schema deploy, no new dependencies.

## Run the app

```bash
dotnet run --project src/FundingPlatform.AppHost
```

Open the dashboard from the Aspire console. Sign in (ephemeral E2E creds, or your dev admin) and
eyeball the sweep:
- **Sidebar** is dark teal `#12343B` with the official horizontal logo in a white rounded card;
  active item shows a teal-tint background + left accent border. (All roles.)
- **Topbar** logout reads teal, not blue.
- **Primary buttons** (e.g. "Crear usuario") are official teal `#008A9E`.
- **Tables** have an official-teal header band, white rows, light-teal hover, no cream zebra.
- **Users page** filters sit in a white card with `Aplicar` + `Limpiar filtros`; row actions show
  `Editar` + a `⋯` kebab (Reenviar invitación / Restablecer / Inhabilitar).
- **Footer** shows the official partner image with a yellow top border + the unchanged copyright.
- **Login** shows the official vertical logo.
- Download a **Funding Agreement PDF** → its logo disc + partner strip read official teal.

## Build & static checks

```bash
dotnet build FundingPlatform.slnx

# Token discipline: raw hex only in tokens.css (+ PDF carve-outs)
bash scripts/tokens-audit.sh
bash scripts/verify-tokens.sh

# Brand grep gate: spec-019 palette gone outside tokens.css history; yellow non-semantic
bash scripts/brand-grep-gate.sh

# Asset budget: official logos + footer image ≤ 400 KB gz
bash scripts/asset-budget-check.sh        # (and/or verify-asset-budget.sh)

# PDF carve-out: FundingAgreement .cshtml byte-identical to main (only PNGs swapped)
bash scripts/verify-pdf-carveouts.sh

# Schema must be untouched
git diff --stat main -- src/FundingPlatform.Database/   # expect empty
```

## Tests (delivery bar = filtered E2E green)

```bash
dotnet test tests/FundingPlatform.Tests.Unit
dotnet test tests/FundingPlatform.Tests.Integration

# Filtered E2E — brand + the touched admin/users surfaces
dotnet test tests/FundingPlatform.Tests.E2E \
  --filter "FullyQualifiedName~Brand|FullyQualifiedName~AdminUser|FullyQualifiedName~UserInvitation|FullyQualifiedName~AdminResetPassword"
```

Expectations:
- `Brand/BrandPresence*Tests` stay green (sidebar-brand text + sponsor-strip testid preserved).
- `Brand/PrintLayoutTests` green (`data-print-hide` preserved on the new footer image).
- `Brand/AxeContrastTests` green incl. dark-sidebar light text AA; Users page added.
- `Brand/VisualRegressionTests` — snapshots refreshed for applicant home, reviewer queue, admin
  index, login, + Users page (review the diff on PR).
- User-admin E2E (`AdminUserLifecycleTests`, `AdminUserCodeTests`, `UserInvitationTests`,
  `AdminResetPasswordTests`, …) green after the `AdminUsersListPage` page object learns to open the
  kebab before clicking a relocated row action.

## Perf baseline (NFR-001)

```bash
node scripts/capture-perf-baseline.mjs   # re-capture
node scripts/compare-perf.mjs            # assert no >10% LCP/TBT regression vs baseline
```

## Acceptance walk (maps to SCs)

| Check | SC |
|---|---|
| No legacy spec-019 hex outside tokens.css history | SC-001/002 |
| Official logos per context; placeholders gone | SC-003 |
| Official footer image + yellow border on every page | SC-004 |
| Users page: card filters + Limpiar + de-zebra table + kebab actions | SC-005 |
| No cream zebra anywhere; white + teal hover | SC-006 |
| All primary buttons teal, zero blue | SC-007 |
| axe AA on ≥5 surfaces incl. sidebar | SC-008 |
| Keyboard reaches kebab actions, teal focus ring | SC-009 |
| Narrow viewport: filters wrap, table scrolls, footer scales, sidebar collapses | SC-010 |
| Reduced-motion green | SC-011 |
| Snapshots updated for ≥4 + Users | SC-012 |
| Fixture PDF reads official teal, layout/content identical | SC-013 |
| Schema diff empty | SC-014 |
| Asset budget ≤ 400 KB gz | SC-015 |
| Filtered E2E green | SC-016 |
| User sign-off on palette/sidebar/footer/Users treatment | SC-017 |
