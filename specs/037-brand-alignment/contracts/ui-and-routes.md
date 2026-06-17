# UI & Route Contracts: Programa Semilla Official Brand Alignment (037)

This feature exposes no new HTTP routes, controllers, actions, or external APIs. Its "contracts" are
(1) the **design-token vocabulary**, (2) **stable UI identifiers** that downstream E2E tests depend
on, and (3) the **invariant routes/actions** that must NOT change. Anything an automated check or a
human reviewer can assert against lives here.

## A. Route / action invariants (MUST NOT change)

No controller, action name, HTTP verb, route template, or `data-confirm` behavior changes. In
particular, the relocated Users row actions keep their exact endpoints:

| Action | Verb | Controller/Action | Route values |
|---|---|---|---|
| Editar | GET | `AdminUsers/Edit` | `{ id }` |
| Reenviar invitación | POST | `AdminUsers/ResendInvitation` | `{ id }` + antiforgery |
| Restablecer | GET | `AdminUsers/ResetPassword` | `{ id }` |
| Inhabilitar | POST | `AdminUsers/Disable` | `{ id }` + antiforgery + `data-confirm` |
| Habilitar | POST | `AdminUsers/Enable` | `{ id }` + antiforgery |
| Filtros (Aplicar) | GET | `AdminUsers/Index` | existing query params |
| Limpiar filtros | GET | `AdminUsers/Index` | **no** params (reset) — NEW affordance, existing route |

`Account/Logout` (POST) unchanged. Cascading fund filter params (`fundFilter`/`processFilter`/
`groupFilter`) unchanged.

## B. Design-token contract (`tokens.css` is the only raw-hex file)

Live token values after this feature (see research.md D2/D3 for the full remap):

```
--color-primary:        #008A9E
--color-primary-strong: #007789
--color-primary-light:  #42AFA8   (new)
--color-primary-subtle: #D6EEF1
--color-primary-rgb:    0, 138, 158
--color-accent:         #FFC729
--color-accent-subtle:  #FFEFB8
--color-accent-orange:  #F9A61C   (new, reserved decorative/fill — not status-wired)
--color-bg-page:        #F6F8FA
--color-bg-surface:     #FFFFFF
--color-border:         #DDE5E8
--color-text-primary:   #1F2933
--color-text-secondary: #64748B
--color-text-muted:     #64748B
--color-success:        #168A4A
--color-danger:         #D92D20
--color-sidebar-bg:     #12343B   (new)
--color-sidebar-hover:  #174A53   (new)
--color-sidebar-text:   #D9E6E8   (new)
--color-table-hover:    #EFF8F8   (new)
--color-table-separator:#E5ECEF   (new)
--tblr-primary-rgb:     0, 138, 158   (literal — must be updated)
```

Removed: `--color-table-zebra` (and its `nth-child(even)` consumer).

**Contract checks:**
- `scripts/tokens-audit.sh` / `verify-tokens.sh`: raw hex appears ONLY in `tokens.css` (+ PDF
  carve-outs). New `site.css` rules reference tokens via `var(...)`, never raw hex.
- `scripts/brand-grep-gate.sh`: the spec-019 palette (`#1FA0A0 #15807F #D7EDED #F2C014 #FBEBA6
  #FFF3E5`) returns zero hits outside `tokens.css` history comments + git history; `#FFC729` /
  `--color-accent` carries no semantic meaning (decorative-only).

## C. Stable UI identifiers (E2E depends on these — MUST be preserved)

| Identifier | Element | Note |
|---|---|---|
| `data-testid="sidebar"` | sidebar `<aside>` | dark `#12343B` after re-tint |
| `data-testid="sidebar-brand"` | sidebar logo link | must still contain text "Programa Semilla" |
| `data-testid="sidebar-entry-*"` | every nav entry | all slugs preserved (home, users, funds, …) |
| `data-testid="topbar"` | topbar `<header>` | logout link recolored teal |
| `data-testid="sponsor-strip"` | footer strip | now a single official image; keep `data-print-hide` |
| `data-testid="page-header"` / `page-title` / `page-subtitle` | page header | unchanged |
| `data-testid="admin-users-filter-form"` | Users filter form | now inside `.fl-filter-card` |
| `data-testid="admin-users-filter-submit"` | Aplicar button | unchanged |
| `data-testid="admin-users-filter-clear"` | Limpiar filtros | **NEW** |
| `data-testid="row-action-edit"` | Editar | stays visible |
| `data-testid="row-action-resend-invite"` | Reenviar invitación | moves into kebab, testid kept |
| `data-testid="row-action-reset-password"` | Restablecer | moves into kebab, testid kept |
| `data-testid="row-action-disable"` | Inhabilitar | moves into kebab, testid + `data-confirm-*` kept |
| `data-testid="row-action-enable"` | Habilitar | moves into kebab, testid kept |
| `UiCopy.BrandName` | E2E constant | stays `"Programa Semilla"` |

New (kebab): `data-testid="row-actions-menu-<userId>"` for the `⋯` toggle (so E2E can open it).

## D. Component contracts

- **Sidebar (`_Layout` + `site.css`):** dark `#12343B`; nav-link text `--color-sidebar-text`; hover
  `--color-sidebar-hover`; active item = teal-tint bg + white text + `4px` left border
  `--color-primary-light`. Markup/classes/testids unchanged.
- **Brand header (`_BrandSidebarHeader`):** official horizontal logo in a white rounded container;
  retains a `Programa Semilla` text node (visually hidden if redundant) and `alt`/`title`.
- **Auth hero (`_AuthLayout`):** official vertical logo; tagline copy unchanged.
- **Footer (`_SponsorStrip`):** single official partner image, centered, `3px` `#FFC729` top border,
  `data-testid="sponsor-strip"` + `data-print-hide` preserved; copyright line unchanged.
- **Tables (`.fl-table`):** teal header band + white text; white body rows; `--color-table-hover` on
  `:hover`; `--color-table-separator` bottom borders; no zebra; `data-density` rules preserved.
- **Row actions (`_RowActionsMenu`):** `Editar` visible + `⋯` dropdown wrapping the existing
  forms/links verbatim (verbs, antiforgery, `data-confirm-*`, testids unchanged). Keyboard/SR
  operable.
- **Filters (`.fl-filter-card`):** white card wrapper; existing controls + `Aplicar`; new
  `Limpiar filtros` link to the param-less route.
- **Buttons:** `btn btn-primary` → official teal via the token bridge (no blue primaries). Topbar
  logout → teal via scoped `[data-testid="topbar"]` rule.
- **Favicon:** official icon disc.

## E. PDF brand-asset contract (FR-023)

- Swap only `wwwroot/lib/brand/pdf/header-seedling.png` (official-teal disc) and
  `footer-partners-strip.png` (official partner strip). 
- `Views/FundingAgreement/_FundingAgreementLayout.cshtml` and `Document.cshtml` are byte-identical to
  `main` (`scripts/verify-pdf-carveouts.sh` MUST pass). Print palette `#1f6363` / `#c8a85b` unchanged.
- A regenerated fixture PDF differs from a pre-facelift fixture only in brand-asset color + creation
  timestamp (SC-013).

## F. Out-of-contract (explicitly NOT touched)

Schema, controllers/actions/routes, permissions/roles, localization resources (es-CR copy),
Tabler bundle, managed dependencies, PDF generation pipeline/layout/body, public marketing surface.
