# Research: Programa Semilla Official Brand Alignment (037)

**Date:** 2026-06-17
**Inputs:** spec.md, implementation-notes.md, codebase exploration (tokens.css, `_Layout`,
`_BrandSidebarHeader`, `_AuthLayout`, `_SponsorStrip`, Admin/Users/Index, `_PageHeader`/`_ActionBar`,
PDF FundingAgreement views, `scripts/`, `tests/.../Brand/`).

This phase resolves the spec's open questions (OQ-001…OQ-004) and pins the exact seams. Format per
decision: **Decision / Rationale / Alternatives**.

---

## D1 — The sidebar is ALREADY dark; re-tint to `#12343B` is a scoped override, not a restructure

**Decision:** Keep the existing `<aside class="navbar navbar-vertical navbar-expand-lg navbar-dark"
data-bs-theme="dark" data-testid="sidebar">` markup unchanged. Introduce new tokens
`--color-sidebar-bg: #12343B`, `--color-sidebar-hover: #174A53`, `--color-sidebar-text: #D9E6E8`,
and apply them via a **scoped block in `site.css` targeting `[data-testid="sidebar"]`** (background,
nav-link text/hover, active-state). The active item gets `background: rgba(66,175,168,.16)`, white
text, and `border-left: 4px solid #42AFA8`.

**Rationale:** Tabler's `navbar-dark` already supplies a dark scheme, but its default dark background
is not `#12343B`; token bridges alone don't reach Tabler's navbar internals reliably. A small scoped
override on the stable `[data-testid="sidebar"]` selector is the minimal, durable approach. The
hex values live as named tokens in `tokens.css`; `site.css` only references the tokens (no raw hex),
keeping `tokens-audit.sh` green. The spec's "dark sidebar for all roles" is automatically satisfied
because `_Layout` is the single shared shell for every role (the SupplierAdmin variant is the same
`<aside>`).

**Alternatives:** (a) Swap to a custom non-Tabler sidebar — rejected, large blast radius, breaks
accordion/responsive. (b) Token-only with no scoped CSS — rejected, doesn't reliably override
Tabler navbar background.

**Spec impact:** FR-015's premise "dark sidebar" is a re-tint, not a new structure. No spec change
needed; implementation-notes already anticipated the scoped override.

---

## D2 — Exact token remap (tokens.css is the only raw-hex file)

**Decision:** Remap these live declarations in `tokens.css` (current → new):

| Token | Current | New |
|---|---|---|
| `--color-primary` | `#1FA0A0` | `#008A9E` |
| `--color-primary-strong` | `#15807F` | `#007789` |
| `--color-primary-subtle` | `#D7EDED` | `#D6EEF1` (light tint of #008A9E) |
| `--color-primary-rgb` | `31, 160, 160` | `0, 138, 158` |
| `--color-accent` | `#F2C014` | `#FFC729` |
| `--color-accent-subtle` | `#FBEBA6` | `#FFEFB8` |
| `--color-bg-page` | `#FFFFFF` | `#F6F8FA` |
| `--color-bg-surface` | `#FFFFFF` | `#FFFFFF` (unchanged) |
| `--color-bg-surface-raised` | `#F7F8F8` | `#FFFFFF` (cards stay pure white over the off-white page) |
| `--color-border` | `#E2E5E5` | `#DDE5E8` |
| `--color-text-primary` | `#1A1A1A` | `#1F2933` |
| `--color-text-secondary` | `#5A5A5A` | `#64748B` |
| `--color-text-muted` | `#8A8A8A` | `#64748B` |
| `--color-success` | `#157A3F` | `#168A4A` |
| `--color-danger` | `#B0271E` | `#D92D20` |
| `--color-table-zebra` | `#FFF3E5` | **removed** (see D3) |

**New tokens to add:** `--color-primary-light: #42AFA8`, `--color-accent-orange: #F9A61C`,
`--color-sidebar-bg: #12343B`, `--color-sidebar-hover: #174A53`, `--color-sidebar-text: #D9E6E8`,
`--color-table-hover: #EFF8F8`, `--color-table-separator: #E5ECEF`.

**Keep:** warning (`#8C5A0B`) and info (`#1F5BA8`) and their `-subtle` variants — they remain AA-safe
on white and are not in the official palette deltas; only retune if the post-design contrast check
flags them. `--shadow-glow-primary` updates automatically via `--color-primary-rgb`. The Tabler
bridge (`--tblr-primary`, `--tblr-primary-rgb`, `--tblr-link-color`, etc.) all reference the color
tokens via `var()` EXCEPT `--tblr-primary-rgb: 31, 160, 160;` which is a **literal** — update it to
`0, 138, 158`.

**Rationale:** All consumers already read these tokens; remapping the values cascades the official
palette platform-wide in one edit. Subtle/derived values (`-subtle`, `primary-strong`) are chosen as
light/dark variants of `#008A9E` consistent with the official hover `#007789`.

**Alternatives:** Renaming tokens — rejected (needless churn across all consumers).

---

## D3 — Remove cream zebra; tables go white + light-teal hover

**Decision:** Delete the `--color-table-zebra` token and rewrite its single consumer
`.fl-table tbody tr:nth-child(even) td { background: var(--color-table-zebra); }` (tokens.css ~L655).
Replace with: body rows stay `--color-bg-surface` (white); add
`.fl-table tbody tr:hover td { background: var(--color-table-hover); }` and
`.fl-table tbody td { border-bottom: 1px solid var(--color-table-separator); }`. Header band stays
`background: var(--color-primary)` (now official teal) with white text. Reviewer/applicant density
(`data-density`) rules are untouched.

**Rationale:** Exactly one consumer of the zebra token exists, so removal is clean. Hover + soft
separators carry row distinction without alternating color (guideline: "Avoid beige alternating rows").

**Alternatives:** Keep zebra in a lighter teal — rejected, the client explicitly wants no alternating
stripes.

---

## D4 (OQ-004 + PDF nuance) — PDF brand re-tint = swap the two PNG assets only; print-CSS palette stays

**Decision:** Satisfy FR-023 by **replacing the two PDF brand PNGs** with official-teal versions:
- `wwwroot/lib/brand/pdf/header-seedling.png` ← derived from the official icon disc (`#008A9E`).
- `wwwroot/lib/brand/pdf/footer-partners-strip.png` ← the official combined partner strip
  (`Fooder-general.png`), which already carries official partner colors.

Do **NOT** edit `Views/FundingAgreement/_FundingAgreementLayout.cshtml` or `Document.cshtml`. Their
isolated print palette (`--brand-teal: #1f6363`, `--brand-gold: #c8a85b`, cream rows) is a
deliberate print-legibility choice, is NOT one of the web legacy hexes, and is guarded
byte-identical by `verify-pdf-carveouts.sh`. The brand-image swap is a pure file replacement that the
carve-out script (which diffs the `.cshtml` files, not the PNGs) does not flag.

**OQ-004 resolution:** Adopting the official partner strip image for the PDF footer **does** change
the PDF partner set to match the web footer — acceptable and desirable (UI/PDF reconverge). The
header disc moves to official teal. The print heading/table teal `#1f6363` stays.

**Rationale:** This is the literal reading of FR-023 ("only asset colors/files change; pipeline,
layout, body content unchanged"). It reconverges the most recognizable PDF brand chrome (logo disc +
partner strip) with the official teal while keeping the byte-carve-out gate green and not reopening
the print-typography palette.

**Alternatives:** (a) Also re-tint the print CSS `#1f6363` → `#008A9E` — rejected for this spec: it
edits carve-out-guarded layout files (would require updating the carve-out baseline) and `#1f6363`
is a print-tuned teal, not a brand asset; out of scope per FR-023. Flag as a future micro-spec if
stakeholders want the printed body headings to match exactly. (b) Leave PDF entirely untouched —
rejected, the user chose "it must match now."

**Open dependency:** Producing the recolored `header-seedling.png` needs the official icon as a
source; `logo-2019-(2).png` (#008A9E disc) is the source. Partner strip = `Fooder-general.png`
sized to the PDF strip dimensions. Pin exact dimensions during tasks.

---

## D5 (OQ-003) — `#F9A61C` orange is a reserved decorative/fill accent, NOT wired to existing status

**Decision:** Add `--color-accent-orange: #F9A61C` as a **reserved decorative/fill token** (dark-text
overlay when used as a badge fill). Do NOT remap any existing status to it. Existing warning-tone
statuses (`AppealOpen`, item `NeedsInfo` → `bg-warning`; supplier `PendingReview` → `bg-yellow-lt`)
keep their current tone; `--color-warning` stays the AA-safe amber `#8C5A0B` for warning *text*.

**Rationale:** `#F9A61C` fails AA as text on white (like the yellow), so it cannot be a semantic text
color. Re-pointing a status to orange would be a behavior/semantics change the spec forbids
(visual-only, no status meaning change). Holding it in reserve matches the guideline ("use carefully,
not as main UI colors") and the no-color-only-meaning rule. If a future spec introduces a true
"pending/attention" surface, it can opt in.

**Alternatives:** Replace `--color-warning` with orange — rejected (orange isn't AA as text; would
regress warning legibility and silently recolor AppealOpen/NeedsInfo).

---

## D6 — Brand assets: file mapping, formats, and the favicon/wordmark contract

**Decision:** Place the official logos and swap references:

| Target reference | Current | New asset (from `seeds/facelift-2/`) |
|---|---|---|
| `_BrandSidebarHeader` `mark.svg` | placeholder seedling SVG | official **horizontal** logo (`logo 2019 (1).png`) shown in a **white rounded container**; keep the `Programa Semilla` wordmark `<span>` text for the E2E assertion (visually hidden if the logo includes the wordmark) |
| `_AuthLayout` hero `mark.svg` + `wordmark.svg` | placeholders | official **vertical** logo (`logo 2019 (3).png`) |
| `_SponsorStrip` (5 SVGs) | individual sponsor SVGs | single official **footer image** (`Fooder-general.png`) — see D7 |
| `favicons/favicon.svg` (`<link rel=icon>` in `_Layout`) | placeholder | official **icon disc** (`logo-2019-(2).png`) → favicon (PNG `rel=icon` or converted) |
| Collapsed sidebar | mark only | official icon disc in white container |

Destination folder: **reuse `wwwroot/lib/brand/`** (existing convention; the guideline's
`wwwroot/images/brand/` is a suggestion, not a constraint — staying in `lib/brand/` keeps the
asset-budget script's paths and the `~/lib/brand/...` references coherent). Provided files are
**raster PNGs**, used as-is/optimized (NFR-005, no SVG tracing).

**Rationale:** Reusing `lib/brand/` minimizes reference churn and keeps the audit/budget scripts
pointed at one tree. Keeping the `Programa Semilla` wordmark text (even if visually hidden) preserves
`BrandPresence*Tests` (`ToContainText("Programa Semilla")`) and `GetByAltText("Programa Semilla")`.

**Alternatives:** New `wwwroot/images/brand/` tree — rejected (splits asset locations, churns scripts
and many `~/lib/brand` refs for no benefit).

**OQ-001 (raster vs vector):** Use raster-as-provided. The auth-hero vertical logo (`logo 2019 (3)`)
is high-res; size it with CSS max-width and verify crispness on high-DPI in the visual-regression
pass. If soft, request a vector original (post-design follow-up). Defaults to raster.

**OQ-002 (white container):** Use a full-bleed **white rounded card** behind the sidebar logo
(`background:#fff; border-radius: var(--radius-md); padding: var(--space-2)`) — simplest reliable
contrast on `#12343B`. Pin the exact padding during tasks.

---

## D7 — Footer: one official image, preserve `sponsor-strip` testid + print-hide contract

**Decision:** Rewrite `_SponsorStrip.cshtml` to render a single
`<img src="~/lib/brand/partners-footer.png" alt="Banca para el Desarrollo · CROCUS · nexo · De la
mano con su PYME · Programa Semilla" data-sponsor="partners" />` inside the existing
`<footer class="fl-sponsor-strip" data-testid="sponsor-strip" data-print-hide=...>`. Keep the
`data-testid="sponsor-strip"`, the `HideOnPrint` reflection logic, and `aria-label`. Add a
`#FFC729` **3px top border** via the `.fl-sponsor-strip` rule (token `--color-accent`) and center the
image responsively (`max-width: 1100px; width: 100%; height: auto`). Drop the per-`data-sponsor`
SVG rows and the `@media` rule that hides `[data-sponsor="10-anos"]`.

**Rationale:** Preserving the `data-testid="sponsor-strip"` and the print-hide attribute keeps
`BrandPresence*Tests` and `PrintLayoutTests` green; only `VisualRegressionTests` snapshots refresh.
The yellow top border is the guideline's signature footer treatment.

**Alternatives:** Keep individual SVGs — rejected by the user (chose the official combined image;
partner-set change accepted, OQ-B).

**E2E impact:** Any assertion on individual `data-sponsor` values or the `10-anos` hide breaks —
audit `tests/.../Brand/` for those (the presence tests assert the strip container, not individual
logos, so they pass). The copyright `<footer>` line and yellow border are new snapshot content.

---

## D8 — Kebab actions column on the Users page (and other multi-action admin tables)

**Decision:** Replace the inline `btn-list` row actions with a Tabler/Bootstrap dropdown: keep
`Editar` as a visible `<a>` button, then a `⋯` toggle (`<button class="btn btn-sm" data-bs-toggle=
"dropdown">` with `ti ti-dots-vertical`) opening a `<div class="dropdown-menu dropdown-menu-end">`.
Inside the menu:
- **Reenviar invitación** — `<form method=post asp-action=ResendInvitation>` wrapping a
  `<button class="dropdown-item" data-testid="row-action-resend-invite">`.
- **Restablecer** — `<a class="dropdown-item" data-testid="row-action-reset-password" href=...>`.
- **Inhabilitar** — `<form method=post asp-action=Disable>` wrapping a
  `<button class="dropdown-item text-danger" data-testid="row-action-disable" data-confirm ...>`
  (preserve all `data-confirm-*` attributes); render red-outline emphasis via a `text-danger`
  dropdown-item style. **Habilitar** (when Disabled) stays the same pattern with
  `data-testid="row-action-enable"`.

**Critical constraints:**
- **Preserve every `data-testid`** (`row-action-edit`, `-resend-invite`, `-reset-password`,
  `-disable`, `-enable`) so E2E only needs a "open the kebab first" step, not new selectors.
- **No route/verb change** — the same `<form asp-action>`/`<a href>` targets, antiforgery tokens, and
  `data-confirm` modal wiring move verbatim into the menu.
- Tabler JS (already loaded) provides the dropdown behavior; Bootstrap dropdowns are keyboard- and
  SR-accessible (FR-020).

**Reusable component:** Extract a `_RowActionsMenu` partial (or a small Razor helper) so the same
kebab pattern applies to other admin tables with ≥2 row actions during the sweep, keeping the
treatment consistent (US3 AC-4). Pin the partial name in tasks.

**Rationale:** Matches guideline Option A; the existing forms/links carry their behavior unchanged;
preserving testids contains the E2E churn to "click kebab" wrappers.

**Alternatives:** Option B (restyle inline) — rejected by the user.

**E2E impact:** `AdminUserLifecycleTests`, `AdminUserCodeTests`, `UserInvitationTests`,
`AdminResetPasswordTests`, `SentinelImmutabilityTests`, etc. that click `row-action-*` now need a
preceding "open kebab" interaction in their page object (`AdminUsersListPage`). Update the page
object once; selectors stay stable.

---

## D9 — Filter card + "Limpiar filtros" (no backend change)

**Decision:** Wrap the Users filter `<form method=get>` in a `<div class="card fl-filter-card">`
(white, `--color-border`, 12px radius, 16px padding) above the table card; keep every input/select +
the `Aplicar` button and the cascading fund filter unchanged. Add a **"Limpiar filtros"** affordance
as an `<a class="btn btn-link" href="@Url.Action("Index")" data-testid="admin-users-filter-clear">`
(navigates to the param-less list = reset to defaults). Consistent control heights via a small
`.fl-filter-card` rule.

**Rationale:** A GET link to the param-less action is the simplest reset with zero new server
behavior (FR-033). The card is presentational. Other admin filter forms get the same wrapper during
the sweep where they aren't already carded.

**Alternatives:** JS-driven field reset — rejected (a param-less GET is simpler and bookmarkable).

---

## D10 — Page header / buttons already token-driven; primary becomes official teal automatically

**Decision:** No structural change to `_PageHeader`/`_ActionBar`. `ActionClass.Primary` →
`btn btn-primary` which reads `--tblr-primary` (now `#008A9E`), so all primary CTAs become official
teal once D2 lands ("no blue primaries" — SC-007 satisfied by the token remap). Confirm the Users
page already passes `Crear usuario` (Primary) + `Crear por lote` (Secondary) with the exact copy
(it does). Typography sizes (title 22–24/600 etc.) already match the `fl-type-*` ramp; verify, adjust
only if off.

**Rationale:** The blue primary today is actually Tabler's default only where a view bypasses the
token bridge; the bridge remap makes teal the global primary. Most "make it teal" work is the D2 edit.

**Alternatives:** Per-button restyle — unnecessary given the token bridge.

---

## D11 — Topbar logout teal (token), not blue

**Decision:** The topbar logout is `<button class="btn btn-link nav-link">`. Add a scoped rule so the
topbar `nav-link`/`btn-link` color reads `--color-primary` (official teal) instead of the Tabler/Bootstrap
link blue. Scope to `[data-testid="topbar"]` to avoid touching the dark sidebar's links.

**Rationale:** Minimal scoped override on a stable testid; keeps the sidebar (also `nav-link`) dark-themed.

**Alternatives:** Global link recolor — rejected (would hit the dark sidebar links).

---

## D12 — Verification scripts: extend, don't rewrite

**Decision:** Update the existing gate scripts:
- `scripts/brand-grep-gate.sh` — add the **spec-019 palette** (`#1FA0A0`, `#15807F`, `#D7EDED`,
  `#F2C014`, `#FBEBA6`, `#FFF3E5`) to the "legacy hex must not appear outside tokens.css history" list;
  ensure the official palette is allowed only inside `tokens.css`; keep the yellow-not-semantic check,
  now keyed to `#FFC729`/`--color-accent`.
- `scripts/tokens-audit.sh` / `verify-tokens.sh` — still assert tokens.css is the only raw-hex file
  (the new `site.css` sidebar/topbar/filter rules must reference tokens, NOT raw hex). The white
  rounded-container `#fff` should be expressed via a token or `--color-bg-surface`.
- `scripts/asset-budget-check.sh` / `verify-asset-budget.sh` — update the enumerated asset paths to
  the new official logos + footer image; re-assert ≤ 400 KB gz. (Watch: PNGs are heavier than the old
  SVGs — optimize/resize; this is the main budget risk.)
- `scripts/verify-pdf-carveouts.sh` — must STILL pass (we only swap PNGs, not the `.cshtml` files).
- `scripts/capture-perf-baseline.mjs` / `compare-perf.mjs` — re-capture the baseline (NFR-001).

**Rationale:** The spec-019 gate infrastructure already enforces the exact invariants we need; this
feature is the next turn of the same crank.

**Alternatives:** New scripts — rejected (duplication).

**Risk:** Asset budget. Five small SVGs (~3 KB total) become PNG logos + a partner-strip PNG. The
combined partner image (`Fooder-general.png`) + logos could approach the budget; optimize to WebP/
optimized-PNG and size to display dims. Measure early (tasks day-1).

---

## D13 — E2E surface: what stays green, what must change

**Decision:** Keep green by construction: preserve `data-testid="sidebar-brand"` + the `Programa
Semilla` wordmark text, `data-testid="sponsor-strip"` + `data-print-hide`, `GetByAltText("Programa
Semilla")`, `UiCopy.BrandName = "Programa Semilla"`, and all `row-action-*` testids.

Must update:
- `AdminUsersListPage` page object — add "open kebab then click action" for the relocated row actions
  (D8); cascade to user-admin E2E classes that exercise row actions.
- `VisualRegressionTests` snapshots — refresh for applicant home, reviewer queue, admin index, login,
  **and add the Users page** (FR-029).
- Any assertion on individual `data-sponsor` SVGs / `10-anos` (D7) — re-point to the single strip image.
- Add per-surface brand assertions where missing (FR-025) — most already exist in `Brand/`.
- `AxeContrastTests` — extend to assert the dark-sidebar light text passes AA; add the Users page.

**Rationale:** The brand suite was built for exactly this kind of pivot (spec 019); the diff is the
footer-image swap, the kebab, and snapshot refresh.

**Alternatives:** Rewrite the brand suite — unnecessary.

---

## Resolved open questions

- **OQ-001** → raster-as-provided (D6); vector fallback is a post-design follow-up only if soft.
- **OQ-002** → full-bleed white rounded card behind the sidebar logo (D6).
- **OQ-003** → `#F9A61C` is a reserved decorative/fill accent, not wired to status (D5).
- **OQ-004** → PDF footer adopts the official partner strip image; PDF header disc → official teal;
  print-CSS palette untouched (D4).

## Residual decisions for tasks (not blocking)

- Exact `--color-primary-subtle` / `-accent-subtle` derived tints (pin to AA against their use).
- Exact PDF PNG dimensions for the recolored header disc + partner strip.
- `_RowActionsMenu` partial name and whether to generalize beyond the Users table this pass.
- White-container padding/radius values for the sidebar logo.
- Whether `warning`/`info` need a retune after the post-design axe pass.
