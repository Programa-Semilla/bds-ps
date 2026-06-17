# Feature Specification: Programa Semilla Official Brand Alignment

**Feature Branch**: `037-brand-alignment`
**Created**: 2026-06-17
**Status**: Draft
**Input**: User description: Visual facelift only — re-anchor the FundingPlatform web experience to the **official** Programa Semilla brand book (exact palette + real logo assets), superseding the PDF-sampled approximations spec 019 shipped (teal `#1FA0A0`, yellow `#F2C014`, placeholder geometric mark/wordmark SVGs). This realizes spec-019 **OQ-001** (designer override when the brand book pins exact values). Beyond a palette swap, it introduces structural refinements requested by the client: a **dark teal sidebar**, **de-zebra'd tables** (white rows, light-teal hover, no cream stripes), a **kebab actions menu** on data tables, an **official combined partner-logo footer image**, standardized **page headers** with teal primary CTAs, **filter cards** with a clear-filters action, and the **real logo assets** (horizontal / vertical / icon) placed per context. Full surface re-sweep across applicant + reviewer + admin + auth, same shape and quality bar as spec 019. No backend logic, business rules, routes, permissions, data behavior, or schema change. Code namespaces, project names, and config keys remain `FundingPlatform`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Applicant sees one consistent official brand end-to-end (Priority: P1)

An unauthenticated visitor opens the Login page and sees the official Programa Semilla **vertical logo** in the hero. They sign in and every authenticated surface (home, journey, appeal, signing) wears the official identity: a **dark teal sidebar** anchored by the official **horizontal logo** in a white rounded container, a clean white topbar with the user email + teal "Cerrar sesión", official-teal (`#008A9E`) primary buttons (never blue), white data rows with light-teal hover (no cream zebra), and an official **partner-logo footer strip** with a yellow top border. When they download their Funding Agreement PDF, its brand chrome reads the same official teal as the screen they came from.

**Why this priority**: This is the user-visible deliverable and the reason the facelift exists — the current UI uses approximated brand values and placeholder logos that do not match the client's official brand book. P1 closes the loop: official palette + official logos + official footer, consistent from Login through PDF.

**Independent Test**: Run an applicant E2E that lands on Login, signs in, walks home + journey + signing, and downloads the PDF. Assert: Login hero shows the vertical logo; the sidebar background is the dark teal token and carries the horizontal logo; primary buttons render official teal; tables have no cream zebra; footer shows the official partner image with a yellow top border; the downloaded PDF's brand chrome reads official teal.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they open Login, **Then** the hero renders the official Programa Semilla vertical logo and the footer shows the official partner-logo strip with a yellow top border.
2. **Given** a signed-in applicant, **When** they navigate any authenticated page, **Then** the sidebar renders on the dark teal background with the official horizontal logo (in a white rounded container) at the top, and the active menu item shows a teal-tinted background with a left accent border.
3. **Given** any applicant surface, **When** the user looks at buttons, tables, and links, **Then** primary actions are official teal (no blue), data table headers are official teal with white text, body rows are white with a light-teal hover (no cream alternating stripes), and links/focus states read official teal.
4. **Given** the applicant downloads their Funding Agreement PDF, **When** they open it beside the platform UI, **Then** the brand chrome (logo disc / partner strip teal) reads the same official teal as the UI.

---

### User Story 2 - Reviewer surfaces lift to the official identity, density preserved (Priority: P1)

A reviewer signs in to triage their queue, opens an application detail, signs a quotation review, and works the signing inbox. Every reviewer surface wears the official identity at the established reviewer density (dense cell padding, not the applicant's roomier padding). Tables read official teal headers, white rows, light-teal hover, and the action affordances are restyled consistently. The reviewer queue search and all existing filters keep working unchanged.

**Why this priority**: Reviewers spend the most time in the platform; an inconsistent shell between applicant and reviewer would undermine US1's "one consistent brand" claim. The spec-011/019 density rule (reviewer dense, applicant roomy) must not regress.

**Independent Test**: Run a reviewer E2E that visits queue, detail, signing inbox, and history. Assert the dark sidebar + official horizontal logo on each, official-teal table headers, white (non-zebra) rows, and that reviewer cell padding stays dense while applicant tables stay roomy.

**Acceptance Scenarios**:

1. **Given** a signed-in reviewer, **When** they view the queue, **Then** the table renders an official-teal header band, white rows with light-teal hover, dense cell padding, and consistently styled action affordances.
2. **Given** a reviewer in the signing inbox, **When** they open a pending signature, **Then** the modal opens on a white surface with a teal header band and the dark sidebar + official footer remain visible.
3. **Given** a reviewer queue with at least one row, **When** the page is screenshot-tested, **Then** the snapshot matches the committed reference for the official identity.

---

### User Story 3 - Admin surfaces lift uniformly, with the Users page as the reference treatment (Priority: P1)

An admin signs in and manages Users, Groups, Currencies, Exchange Rates, Suppliers, Reports, Configuration, Funds, Processes, Impact/Category Templates, and the rest of the admin inventory. Every admin sub-surface re-walks at the new bar. The **Users page is the reference implementation** for the refined components: a standardized page header ("Usuarios" / "Administre las cuentas de usuario de la plataforma." / primary "Crear usuario" + secondary "Crear por lote"), a **white filter card** with a **"Limpiar filtros"** action added beside the existing "Aplicar", a de-zebra'd table with an official-teal header, and an **actions column** that shows **"Editar"** as the visible action with secondary actions (Reenviar invitación / Restablecer / Inhabilitar) collapsed into a **"⋯" kebab menu** (Inhabilitar styled red-outline).

**Why this priority**: The admin area is the broadest surface inventory and the client's seed framed the facelift around the admin layout and the Users page specifically. Leaving admin on old tokens while applicants/reviewers are on the official brand would re-introduce visible divergence.

**Independent Test**: Run an admin E2E that visits `/Admin` index + the Users page + a representative sample of sub-surfaces. Assert: standardized page header with teal primary CTA; filter card present with both "Aplicar" and "Limpiar filtros"; de-zebra'd official-teal table; "Editar" visible with a kebab menu exposing Reenviar invitación / Restablecer / Inhabilitar; every menu action still posts to its original route.

**Acceptance Scenarios**:

1. **Given** a signed-in admin on the Users page, **When** it renders, **Then** the header shows the title, subtitle, a teal "Crear usuario" primary button, and an outlined "Crear por lote" secondary button, right-aligned.
2. **Given** the Users page filters, **When** the admin opens them, **Then** they are grouped in a white card with consistent input heights, an "Aplicar" button, and a "Limpiar filtros" action that resets the filters; all current filter functionality is preserved.
3. **Given** a user row, **When** the admin opens the actions affordance, **Then** "Editar" is directly visible and a "⋯" menu exposes Reenviar invitación, Restablecer, and Inhabilitar (red-outline), each invoking its existing, unchanged route/POST action.
4. **Given** any other admin sub-surface, **When** rendered, **Then** its chrome, table styling, button vocabulary, and badge styling match the Users-page reference.

---

### User Story 4 - Real official logos replace placeholders in every context (Priority: P1)

Across the platform, the placeholder geometric mark/wordmark from spec 019 are replaced by the **real official logo files**, each used in its correct context: the **horizontal** logo in the expanded sidebar (in a white rounded container for contrast on the dark sidebar), the **icon-only** disc in the collapsed sidebar and as the favicon, and the **vertical** logo in the auth hero. The official combined **partner-logo footer image** replaces the prior set of individual sponsor SVGs (the partner set changes accordingly — "10 años" is dropped, "De la mano con su PYME" is added).

**Why this priority**: Logo correctness is the most recognizable element of brand alignment; placeholders directly contradict the goal. P1 because it is part of the same single-pass sweep and is independently verifiable.

**Independent Test**: Render the expanded sidebar, the collapsed sidebar, the auth hero, the footer, and the browser favicon. Assert each shows the correct official asset for its context and that the placeholder mark/wordmark are no longer referenced anywhere.

**Acceptance Scenarios**:

1. **Given** the expanded sidebar on the dark background, **When** rendered, **Then** the official horizontal logo is shown inside a white rounded container with sufficient contrast.
2. **Given** the collapsed sidebar (or a small/compact context), **When** rendered, **Then** only the official icon-only logo is shown, with a hover tooltip "Programa Semilla".
3. **Given** the footer on any page, **When** rendered, **Then** the official combined partner-logo image is centered above the copyright line with a yellow top border, and scales responsively on mobile.
4. **Given** any browser tab, **When** the site loads, **Then** the favicon is the official seedling icon (placeholder favicon retired).

---

### User Story 5 - The generated Funding Agreement PDF reconverges with the official teal (Priority: P2)

When an agreement is generated, its brand chrome assets (logo disc, partner strip) read the **official `#008A9E` teal** rather than the prior PDF-sampled teal, so the PDF and the UI match again. Only the brand **asset colors/files** change; the PDF generation pipeline, page layout, and body content are untouched.

**Why this priority**: Spec 019's premise was UI-matches-PDF; moving the UI to the official teal without re-tinting the PDF assets would re-open that exact divergence. P2 because the PDF is a downstream artifact rather than a daily working surface, and the change is a narrow asset re-tint.

**Independent Test**: Generate a fixture Funding Agreement PDF and confirm its brand-chrome assets read official teal; confirm the document layout and body content are otherwise unchanged versus a pre-facelift fixture (differing only in brand-asset color and creation timestamp).

**Acceptance Scenarios**:

1. **Given** an agreement is generated, **When** the PDF is produced, **Then** its logo disc and partner-strip chrome read the official teal.
2. **Given** a regenerated fixture PDF, **When** compared to a pre-facelift fixture, **Then** layout and body content are identical (differences limited to brand-asset color and creation timestamp).

---

### User Story 6 - Accessibility and responsiveness hold across the new shell (Priority: P2)

On desktop, tablet, and mobile the new shell stays usable: filters wrap naturally, tables scroll horizontally on small screens, the footer image scales down, and the collapsed sidebar shows the icon only. Keyboard navigation is preserved, focus states are visible in official teal, text/buttons/navigation meet WCAG AA contrast (including light text on the dark sidebar), and status meaning is never conveyed by color alone.

**Why this priority**: Accessibility and responsive behavior are explicit client requirements and a project quality bar; a brand change that regresses either is not shippable. P2 because it is a cross-cutting guarantee verified once across representative surfaces rather than a single user journey.

**Independent Test**: Run `axe`-style contrast checks on representative surfaces (applicant home, reviewer queue, admin index, login, Users page) and resize/keyboard E2E checks; assert AA contrast, visible teal focus, wrapping filters, horizontally scrollable tables, a responsive footer, and icon-only collapsed sidebar.

**Acceptance Scenarios**:

1. **Given** any representative surface, **When** contrast is audited, **Then** all text, buttons, and navigation meet WCAG AA — including light sidebar text on the dark teal background.
2. **Given** a keyboard-only user, **When** they tab through the page, **Then** focus is visible (official-teal ring) and follows a logical order with no functionality lost.
3. **Given** a narrow viewport, **When** the page renders, **Then** filters wrap to multiple rows, the table scrolls horizontally, the footer image scales down, and the sidebar collapses to the icon-only logo.
4. **Given** any status indicator, **When** rendered, **Then** its meaning is carried by text/icon as well as color (color is never the sole signal).

---

### Edge Cases

- **Sidebar collapsed state** (≤ 992 px or user-collapsed): horizontal logo + labels hide, only the icon-only logo is visible; hover tooltip surfaces "Programa Semilla".
- **Horizontal logo contrast on dark sidebar**: the official horizontal logo (teal/dark artwork on transparent) is placed inside a white rounded container so it never disappears against `#12343B`.
- **Yellow `#FFC729` is decorative-only**: it fails AA on white (~1.5:1); it is reserved for the footer top border and filled-badge backgrounds with dark-text overlay, never for meaningful icons, focus rings, or alert text. Orange `#F9A61C` carries "pending/attention" status (with text/icon, not color alone).
- **Cream zebra removed**: removing `--color-table-zebra` must not leave any surface relying on alternating-row color to distinguish rows; row separators (soft bottom borders) carry that load.
- **Kebab actions and keyboard/screen-reader access**: the "⋯" menu must be reachable and operable by keyboard and expose the same actions as before; no action is removed, only relocated, and each still posts to its original route.
- **Footer partner-set change**: using the official combined image intentionally changes the partner set (drops "10 años", adds "De la mano con su PYME"); per-logo E2E assertions that depended on individual sponsor SVGs are replaced by an assertion on the official footer image.
- **Page background shift to `#F6F8FA`**: surfaces that assumed a pure-white page background must still read correctly against the new off-white page token (cards stay `#FFFFFF`).
- **Cached `tokens.css` mid-deploy**: a user session spanning a deploy may serve the old palette; a cache-busting query string on the stylesheet reference prevents a half-old/half-new render.
- **Missing brand asset at request time**: render an empty `<img>` with `alt` fallback ("Programa Semilla" / "Patrocinadores") and log a warning; no broken page.
- **`tokens.css` fails to load**: Tabler defaults render; the page degrades visually but stays operable.
- **Tabler components bypassing partials**: a grep gate before merge catches any element still carrying a legacy hex value or the old blue primary.
- **`forced-colors: active` (high-contrast OS mode)**: focus ring falls back to the system `Highlight` color; verified once.

## Requirements *(mandatory)*

### Functional Requirements

**Design tokens** *(`tokens.css` is the only file allowed to contain raw hex values — spec 011/019 invariant)*

- **FR-001**: System MUST set the primary brand token to the official teal `#008A9E` and introduce a supporting light-teal token `#42AFA8` (for badges, hover, secondary highlights, informational UI), retiring the spec-019 primary `#1FA0A0` and its derived shades.
- **FR-002**: System MUST set the primary hover/strong token to `#007789` and retune `--shadow-glow-primary` to the new primary RGB so focus and hover glows read official teal.
- **FR-003**: System MUST set the accent tokens to yellow `#FFC729` (decorative-only) and a new orange `#F9A61C` (status/attention), retiring the spec-019 accent `#F2C014`.
- **FR-004**: System MUST set neutral tokens to page background `#F6F8FA`, surface/card `#FFFFFF`, border `#DDE5E8`, main text `#1F2933`, and muted text `#64748B`.
- **FR-005**: System MUST introduce dark-sidebar tokens: sidebar background `#12343B`, sidebar hover `#174A53`, and a light sidebar-text token (e.g. `#D9E6E8`) that meets WCAG AA on the dark background.
- **FR-006**: System MUST set status tokens success `#168A4A` and danger `#D92D20` (and keep warning/info retuned so no warm tint is baked in), each meeting WCAG AA on white.
- **FR-007**: System MUST remove the cream table-zebra token entirely; table body rows use the white surface with a light-teal hover (`#EFF8F8`) and soft row separators instead of alternating color.
- **FR-008**: System MUST remap the Tabler `--tblr-*` bridge variables to the new primary, accent, and status values so vendored Tabler components inherit the official palette.
- **FR-009**: System MUST keep the spec 011/019 motion catalog tokens and the reduced-motion contract unchanged in duration and easing.

**Brand assets** *(real official logos replace placeholders)*

- **FR-010**: System MUST replace the placeholder in-app mark/wordmark with the **real official logo assets** and use each in its correct context: horizontal logo in the expanded sidebar, icon-only logo in the collapsed sidebar and favicon, vertical logo in the auth hero.
- **FR-011**: System MUST place the official horizontal logo inside a white (or very light) rounded container in the sidebar so it has sufficient contrast on the dark background.
- **FR-012**: System MUST replace the footer's prior individual sponsor logos with the **official combined partner-logo image** (Banca para el Desarrollo · CROCUS · nexo · De la mano con su PYME · Programa Semilla), centered, with a `#FFC729` yellow top border, scaling responsively on mobile, above the existing copyright line. The partner-set change (drop "10 años", add "De la mano con su PYME") is intentional.
- **FR-013**: System MUST replace the favicon (and any sized PWA favicons) with the official icon-only seedling mark, retiring the placeholder favicon.
- **FR-014**: System MUST retain the copyright line "© 2026 Programa Semilla · Sistema de Banca para el Desarrollo" in the footer.

**Layout & component retune** *(applies across every swept surface)*

- **FR-015**: Sidebar — MUST render on the dark teal background with the official horizontal logo at the top; the active menu item MUST show a teal-tinted background, light/white text, and a left accent border; hover MUST use the sidebar-hover token; icon alignment and spacing MUST be consistent. The existing menu structure, labels, role-aware visibility, and all `data-testid` slugs MUST be preserved. The dark sidebar applies to **all roles**, including applicant.
- **FR-016**: Topbar — MUST render white with a subtle bottom border, keep the current user email and logout action right-aligned, and restyle the logout/link from blue to official teal.
- **FR-017**: Page header — MUST be standardized across pages with a clear title (semibold, ~22–24 px), a muted subtitle (~14 px), and right-aligned actions; the primary action MUST be official teal (never blue) and the secondary action MUST be outlined. The Users page MUST keep its exact copy: title "Usuarios", subtitle "Administre las cuentas de usuario de la plataforma.", primary "Crear usuario", secondary "Crear por lote".
- **FR-018**: Filters — MUST be grouped in a clean white card with consistent input heights and radius; the existing "Aplicar" action MUST be preserved and a "Limpiar filtros" (clear filters) action MUST be added; all current filter functionality MUST be preserved; filters MUST wrap to multiple rows on small screens.
- **FR-019**: Tables — header row MUST render an official-teal band with white semibold text; body rows MUST be white with a light-teal hover and soft separators (no alternating cream zebra, no internal grid lines on body rows); padding/alignment MUST improve; role and status badges MUST remain rounded pills. Reviewer density (dense cell padding) and applicant density (roomier cell padding) MUST be preserved per spec 011/019.
- **FR-020**: Actions column — MUST show "Editar" as the primary visible action and collapse secondary actions (Reenviar invitación / Restablecer / Inhabilitar) into a "⋯" kebab menu; the Inhabilitar (danger) action MUST use a red-outline style. No action MUST be removed — only relocated — and each MUST continue to invoke its existing, unchanged route/POST action. The kebab menu MUST be keyboard- and screen-reader-operable.
- **FR-021**: Buttons — primary MUST render solid official teal with white text; secondary MUST render outlined; danger MUST render the danger color (with a red-outline variant for in-row danger actions); minimum touch target preserved.
- **FR-022**: Typography — MUST use the Inter system stack ("Inter", "Segoe UI", Roboto, Arial, sans-serif), with page title ~22–24 px / 600, section title ~18 px / 600, table header ~13 px / 600, body ~14 px / 400, small ~12 px / 400, buttons ~13 px / 600.

**PDF brand assets** *(narrow, explicit exception to spec 019 FR-039)*

- **FR-023**: System MUST re-tint the PDF brand-chrome assets (logo disc, partner strip) to the official teal `#008A9E` so the generated Funding Agreement PDF reconverges with the UI. Only asset colors/files change; the PDF generation pipeline, page layout, and body content MUST remain unchanged. This is a deliberate, documented exception to spec 019 FR-039 (which froze PDF assets).

**Surface sweep & verification**

- **FR-024**: System MUST apply the new tokens, brand assets, and component retune across the full surface inventory: applicant (home, journey, appeal, signing), reviewer (queue, detail, signing inbox, history), admin (index plus every sub-surface in the current `Views/Admin/` inventory, with the Users page as the reference treatment), and auth (Login, Register/onboarding, Reset/Set Password, Confirm Email), plus the shared `_Layout` chrome.
- **FR-025**: Each swept surface MUST gain at least one E2E brand-presence assertion (e.g., dark sidebar background present, official horizontal logo in the sidebar, official footer image present, vertical logo on Login).
- **FR-026**: WCAG AA contrast MUST be verified on at least 5 representative surfaces (applicant home, reviewer queue, admin index, login, Users page), including light sidebar text on the dark background.
- **FR-027**: Keyboard navigation MUST be preserved with visible official-teal focus states; status meaning MUST never be conveyed by color alone (text/icon accompanies color).
- **FR-028**: A reduced-motion verification MUST remain green; no new motion outside the spec 011/019 catalog is permitted.
- **FR-029**: Visual-regression reference snapshots MUST be updated for at least 4 representative surfaces (applicant home, reviewer queue, admin index, login) plus the Users page; diffs reviewed on PR.
- **FR-030**: A grep/lint gate MUST confirm that legacy palette hex values (`#1FA0A0`, `#15807F`, `#D7EDED`, `#F2C014`, `#FBEBA6`, `#FFF3E5`) return zero hits outside `tokens.css` history comments and git history, and that the yellow accent carries no semantic meaning.
- **FR-031**: Total brand-related asset wire weight (fonts + logos + footer image) MUST stay within the spec 019 budget (≤ 400 KB gz); a new measurement MUST be captured.

**Out-of-scope guardrails**

- **FR-032**: Database schema MUST remain unchanged (`git diff main -- src/FundingPlatform.Database/` empty after this feature lands).
- **FR-033**: No backend business logic, business rules, permissions/roles, or route/action names MUST change; no existing functionality MUST be removed or added (beyond the relocation of actions into the kebab menu and the addition of the "Limpiar filtros" affordance, both of which preserve existing routes/behavior).
- **FR-034**: The localization layer (spec 012) MUST remain unchanged; brand/voice copy MUST stay out of partials' code paths so future-localization compatibility is preserved.
- **FR-035**: No new managed (NuGet) dependencies MUST be added, and the Tabler.io vendored bundle MUST NOT be upgraded. The public marketing surface remains out of scope.

### Key Entities

This feature does not introduce or alter any data entities. (Schema unchanged — FR-032.)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A grep for the legacy palette hex values (`#1FA0A0`, `#15807F`, `#D7EDED`, `#F2C014`, `#FBEBA6`, `#FFF3E5`) returns zero hits outside `tokens.css` history comments and git history.
- **SC-002**: An audit confirms `tokens.css` is the only file containing raw hex color values for the new palette.
- **SC-003**: The official horizontal logo appears in the expanded sidebar (in a white container), the icon-only logo in the collapsed sidebar and favicon, and the vertical logo in the auth hero — verified per context; the placeholder mark/wordmark are no longer referenced anywhere in running views/partials/assets.
- **SC-004**: The official combined partner-logo footer image renders on every authenticated page and on the auth pages with a yellow top border and the unchanged copyright line, verified via a per-surface E2E assertion.
- **SC-005**: On the Users page, the standardized header (teal primary + outlined secondary), the white filter card with both "Aplicar" and "Limpiar filtros", the de-zebra'd official-teal table, and the "Editar" + "⋯" kebab actions are all present and functional; every kebab action invokes its original route.
- **SC-006**: No data table on any swept surface uses cream/beige alternating-row striping; rows are white with a light-teal hover.
- **SC-007**: Primary action buttons render official teal `#008A9E` (no blue primaries remain) on every swept surface.
- **SC-008**: `axe`-style WCAG AA contrast passes on at least 5 representative surfaces (applicant home, reviewer queue, admin index, login, Users page), including light sidebar text on the dark background.
- **SC-009**: A keyboard-only pass reaches every navigation item and every relocated kebab action with a visible official-teal focus ring; no functionality is lost relative to the pre-facelift inline buttons.
- **SC-010**: On a narrow viewport, filters wrap, tables scroll horizontally, the footer image scales down, and the sidebar collapses to the icon-only logo — verified via responsive E2E checks.
- **SC-011**: A reduced-motion verification is green; no new motion has been introduced outside the spec 011/019 catalog.
- **SC-012**: Visual-regression snapshots are committed/updated for at least 4 representative surfaces plus the Users page; diffs reviewed on PR.
- **SC-013**: A regenerated fixture Funding Agreement PDF reads official teal in its brand chrome and is otherwise layout/content-identical to a pre-facelift fixture (differing only in brand-asset color and creation timestamp).
- **SC-014**: `git diff main -- src/FundingPlatform.Database/` is empty.
- **SC-015**: Total brand-related asset wire weight measures ≤ 400 KB gz.
- **SC-016**: The corresponding/filtered E2E tests for the swept surfaces have been executed locally and are green (project delivery bar).
- **SC-017**: User sign-off gate — the official hex palette, the dark-sidebar + logo layout, the footer image, and the Users-page reference treatment (filter card + kebab actions) have been reviewed and approved by the user before merge.

### Non-Functional Requirements

- **NFR-001**: Performance — LCP and TBT on applicant home and reviewer queue MUST NOT regress versus the spec 019 baseline; a new baseline MUST be captured and committed.
- **NFR-002**: Asset budget — total brand-related asset wire weight ≤ 400 KB gz (matches FR-031 / SC-015).
- **NFR-003**: Accessibility — WCAG AA on all swept surfaces. The yellow accent `#FFC729` on white measures ~1.5:1 and is therefore reserved for the footer top border and filled-badge backgrounds with dark-text overlay; a linter/grep gate enforces that yellow carries NO semantic meaning (no meaningful icons, focus rings, or alert text). The dark sidebar's light text MUST meet AA on `#12343B`.
- **NFR-004**: Browser support — last 2 evergreen browsers + iOS Safari (matches spec 011/019).
- **NFR-005**: Provided logo files are used as-is (optimized/resized) without requiring SVG tracing; if an official asset is a raster image, it MUST be sized so it renders crisply at its display dimensions on high-DPI screens within the asset budget.

## Assumptions

- The official Programa Semilla brand book / palette image (`seeds/facelift-2/`) is authoritative and supersedes the spec-019 PDF-sampled values; this feature is the spec-019 OQ-001 designer override.
- The provided official logo files (horizontal, vertical, icon teal, icon yellow) and the official footer partner image are the canonical brand assets; no additional logo sourcing is required.
- The horizontal logo does not contrast adequately on the dark sidebar on its own, so a white rounded container is used (per the client guideline).
- The dark sidebar applies to all roles including applicants (OQ-A resolved during brainstorming).
- The footer partner-set change (drop "10 años", add "De la mano con su PYME") is acceptable because it reflects the official combined image (OQ-B resolved during brainstorming).
- The PDF brand-asset re-tint is in scope and is a narrow, documented exception to spec 019 FR-039; the PDF generation pipeline, layout, and body content remain untouched.
- The platform is pre-production; aggressive single-pass sweep scope and E2E/POM rewrite costs are accepted (UX/UI quality > E2E selector stability, per project conventions).
- Inter is already vendored; no new font acquisition is required.
- The reviewer/applicant table density rule and the motion + reduced-motion catalog from spec 011/019 are carried forward unchanged.

## Dependencies

- **Spec 019 (`019-programa-semilla-brand`)**: This feature modifies in place the `tokens.css`, shared partials (`_Layout`, `_BrandSidebarHeader`, the footer/sponsor strip partial, `_PageHeader`), and brand assets introduced/retuned by spec 019. The motion catalog and reduced-motion contract are preserved verbatim; the palette, page background, sidebar treatment, table striping, footer asset, and logo assets are replaced.
- **Spec 011 (`011-warm-modern-facelift`)** and **Spec 017 (`017-admin-ux-facelift`)**: provide the component vocabulary, density rules, wow moments, and admin surface inventory that are re-walked at the new bar.
- **Spec 018 (`018-pdf-template-lift`)**: the PDF brand-chrome assets re-tinted by FR-023 originate here; PDF generation is otherwise untouched.
- **Spec 012 (`012-es-cr-localization`)**: voice/brand copy MUST stay out of partials' code paths so future-localization compatibility is preserved.
- Official brand assets under `seeds/facelift-2/` (palette image, logo files, footer image). No new managed (NuGet) dependencies; the `canvas-confetti` carve-out from spec 011 is preserved.

## Out of Scope

- Backend logic, business rules, permissions/roles, and route/action renames.
- Database or schema changes.
- PDF generation pipeline behavior, page layout, and body content (only brand-asset color changes — FR-023).
- Localization layer / translation files (spec 012 invariant).
- Tabler.io vendored bundle upgrade.
- New managed (NuGet) dependencies.
- Public marketing surface.
- Removing or adding product functionality (the kebab menu relocates existing actions; "Limpiar filtros" adds a client-side reset affordance over existing filters — neither changes backend behavior).
- Multi-tenant / sister-program brand swapping.

## Open Questions

- **OQ-001**: Sized-raster vs. re-traced-vector for the official logos — the provided files are used as-is per NFR-005; if a logo renders soft at large display sizes (e.g., the auth-hero vertical logo), planning may decide to request a vector original. Defaults to raster-as-provided within the asset budget.
- **OQ-002**: Exact white-container treatment for the sidebar logo (full-bleed white pill vs. subtle off-white card) — pinned during planning against the dark-sidebar contrast check.
- **OQ-003**: Whether the orange `#F9A61C` is wired to any existing status today or only reserved for future "pending/attention" use — pinned during planning after auditing current status usages.
- **OQ-004**: Whether the PDF partner-strip asset should also adopt the new official partner set (to match the footer) or keep its current sponsor composition — defaults to teal re-tint only (FR-023), partner-set change deferred unless planning surfaces a strong argument.
