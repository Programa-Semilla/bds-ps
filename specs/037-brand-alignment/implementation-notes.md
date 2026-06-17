# Implementation Notes: Programa Semilla Official Brand Alignment (037)

> Technical context captured during brainstorming. The spec holds the WHAT/WHY (brand contracts,
> requirements, success criteria). This file holds the HOW (seams, file paths, token math,
> trade-offs) so the spec stays stable and future implementers understand the decisions.

## Why this is a "re-alignment", not a from-scratch facelift

The platform is **already token-driven**. Spec 019 (`019-programa-semilla-brand`) established:
- `wwwroot/css/tokens.css` as the ONLY file allowed to carry raw hex (semantic token names).
- A Tabler bridge (`--tblr-*`) that overrides Tabler defaults because `tokens.css` loads *before*
  `tabler.min.css`.
- Shared partials: `_Layout.cshtml`, `_BrandSidebarHeader.cshtml`, the footer sponsor strip
  (`_SponsorStrip.cshtml`), `_PageHeader.cshtml`, `_StatusPill.cshtml`.

So the bulk of this feature is **remapping ~15 CSS custom properties + swapping brand asset files +
a few component-structure changes (dark sidebar, de-zebra, kebab, footer image)**. This is exactly
the spec-019 OQ-001 path ("if the brand book pins a different value, designer override at the
sign-off gate").

## Primary seams (from the codebase exploration)

| Concern | File(s) |
|---|---|
| Color/type/spacing tokens | `src/FundingPlatform.Web/wwwroot/css/tokens.css` (PRIMARY) |
| Minimal project overrides | `src/FundingPlatform.Web/wwwroot/css/site.css` |
| App shell (sidebar + topbar + body + footer) | `Views/Shared/_Layout.cshtml` |
| Auth shell (hero + card) | `Views/Shared/_AuthLayout.cshtml` |
| Sidebar logo/wordmark | `Views/Shared/_BrandSidebarHeader.cshtml` |
| Footer partner strip | `Views/Shared/_SponsorStrip.cshtml` |
| Standardized page header | `Views/Shared/Components/_PageHeader.cshtml` |
| Role/status pills | `Views/Shared/Components/_StatusPill.cshtml` |
| Users page (reference treatment) | `Views/Admin/Users/Index.cshtml` |
| Brand assets | `wwwroot/lib/brand/` (mark.svg, wordmark.svg → retire; favicons/) + new official logos |
| PDF brand chrome | `wwwroot/lib/brand/pdf/` (header-seedling.png, footer-partners-strip.png) |
| Sidebar accordion JS | `wwwroot/js/site.js` (no styling change expected) |

## Official assets (source: `seeds/facelift-2/`)

| Provided file | Role | Target context |
|---|---|---|
| `logo 2019 (1).png` | horizontal lockup (icon + "PROGRAMA Semilla") | expanded sidebar (inside white container), optional topbar |
| `logo 2019 (3).png` | vertical lockup | auth hero |
| `logo-2019-(2).png` | teal icon disc | collapsed sidebar / favicon |
| `logo-icono-semilla.png` | yellow icon disc | optional decorative accent |
| `Fooder-general.png` | combined partner strip | footer (single image) |
| `paleta-de-color.png` | palette reference | confirms `#008A9E`/`#42AFA8`/`#F9A61C`/`#FFC729` |

Proposed destination: `wwwroot/images/brand/` (per client guideline) OR reuse existing
`wwwroot/lib/brand/`. Pin during planning; keep one location for consistency. Provided files are
**raster PNGs** — used as-is/optimized (NFR-005); no SVG tracing. Watch the vertical auth-hero logo
for softness at large sizes (OQ-001).

## Token math / decisions

- **Primary** `#1FA0A0` → `#008A9E`; **hover/strong** `#15807F` → `#007789`;
  **subtle** `#D7EDED` → derive a new light tint or use `#42AFA8` at low alpha.
- **`--color-primary-light: #42AFA8`** is NEW — supporting teal for badges/hover/secondary.
- **Accent** yellow `#F2C014` → `#FFC729`; **NEW** `--color-accent-orange: #F9A61C`.
- **Page bg** moves `#FFFFFF` → `#F6F8FA` (cards/surfaces stay `#FFFFFF`). Audit any surface that
  assumed pure-white page bg.
- **Border** `#E2E5E5` → `#DDE5E8`; **text** `#1A1A1A` → `#1F2933`; **muted** consolidates to
  `#64748B`.
- **Status**: success `#157A3F` → `#168A4A`; danger `#B0271E` → `#D92D20`. Keep warning/info AA.
- **Remove** `--color-table-zebra: #FFF3E5`. Tables: white rows + `#EFF8F8` hover + `#E5ECEF`
  bottom-border separators. Grep for `--color-table-zebra` consumers before deleting the token.
- **Dark sidebar** is the biggest structural change: current sidebar is a light Tabler vertical
  navbar; new is `--color-sidebar-bg: #12343B`, hover `#174A53`, text `#D9E6E8`, active =
  teal-tint bg + white text + 4px left border `#42AFA8`. The Tabler `navbar-dark` class may help,
  but verify the active-state and hover tokens override Tabler cleanly.
- Retune `--shadow-glow-primary` to the new primary RGB (`0,138,158`).

## Dark-sidebar logo contrast

The horizontal logo artwork is teal mark + dark "PROGRAMA" + teal "Semilla" on transparent — it
will not read on `#12343B`. Per the client guideline, wrap it in a **white (or very light) rounded
container**. OQ-002 pins full-bleed white pill vs. subtle off-white card. The collapsed-state icon
disc (teal) also needs the white container, OR swap to a white/yellow icon variant.

## Kebab actions column (Users page)

Current: 4 inline `btn btn-sm btn-outline-*` buttons (Editar / Reenviar invitación / Restablecer /
Inhabilitar). Target: `Editar` visible + Tabler dropdown (`⋯`) exposing the other three; Inhabilitar
red-outline. **Constraints:**
- Each item keeps its existing route/POST action — POST items (Reenviar, Inhabilitar) need their
  forms preserved inside the menu, or the menu triggers the form. No route/verb change.
- Keyboard + screen-reader operable (Tabler dropdown is Bootstrap-based — already a11y-capable).
- This **changes E2E selectors** for the relocated actions — POM rewrites budgeted (project
  convention: UX quality > selector stability). Apply the same kebab pattern to other admin tables
  with multi-action rows for consistency during the sweep.

## "Limpiar filtros" affordance

Add a clear-filters action beside the existing "Aplicar". Simplest no-backend approach: a link to
the filter action route with no query params (resets to defaults), or a small JS reset. No new
server endpoint — preserves FR-033.

## PDF re-tint (FR-023) — narrow exception to spec 019 FR-039

Only the **asset colors/files** under `wwwroot/lib/brand/pdf/` change to `#008A9E`. Do NOT touch the
Syncfusion generation pipeline, Razor PDF templates' layout, or body content. Verification: regenerate
a fixture PDF and diff against a pre-facelift fixture — expect identical layout/content, only color +
creation-timestamp differences (SC-013). OQ-004: leave the PDF partner-strip composition as-is
(teal re-tint only) unless planning argues for adopting the new official partner set too.

## Accessibility carry-overs (from spec 019)

- Yellow `#FFC729` ≈ 1.5:1 on white → decorative-only; dark-text overlay on yellow badges; grep/lint
  gate keeps it non-semantic. Orange `#F9A61C` carries "pending/attention" but always with text/icon.
- Dark sidebar text `#D9E6E8` on `#12343B` must pass AA (verify ≈ 8:1, comfortable).
- Visible focus = official-teal ring; preserve keyboard nav; status never color-only (pills keep
  icon + label, already true via `_StatusPill`).

## Verification tooling (reuse spec 019 scripts where possible)

- Hex-audit / token-only grep gate under `scripts/` (extend the spec 019/011 audit).
- `axe-playwright` AA on ≥5 surfaces; reduced-motion test stays green; visual snapshots updated for
  ≥4 surfaces + Users page; asset-weight budget (≤400 KB gz); perf baseline re-captured.
- Delivery bar: filtered E2E green for the swept test classes (full suite only if cross-cutting,
  per CLAUDE.md).

## Rejected / deferred

- **Keep individual sponsor SVGs** (rejected): client chose the single official combined image; the
  partner set intentionally changes (drops "10 años", adds "De la mano con su PYME").
- **Admin-only scope** (rejected): the shared dark sidebar + tokens make a full re-sweep the natural
  unit; user chose full re-sweep like spec 019.
- **Accept UI/PDF teal delta** (rejected): user chose "it must match now" → PDF asset re-tint in scope.
- **SVG tracing of logos** (deferred): raster-as-provided per NFR-005; revisit only if crispness fails
  (OQ-001).
