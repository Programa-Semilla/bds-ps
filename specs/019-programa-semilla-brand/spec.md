# Feature Specification: Programa Semilla Brand Pivot

**Feature Branch**: `019-programa-semilla-brand`
**Created**: 2026-05-09
**Status**: Draft
**Input**: User description: Re-anchor the FundingPlatform web experience to the actual sponsor-program brand identity expressed in the canonical Funding Agreement PDF reference (`brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`) — Programa Semilla under Sistema de Banca para el Desarrollo, with co-sponsors Banca para el Desarrollo SBD, CROCUS, nexo, and the 10 años badge. Spec 011 ("warm-modern facelift") shipped a placeholder visual identity (forest-green primary `#2E5E4E` + warm-amber accent `#D98A1B` + warm cream page bg `#FAF7F2` + Fraunces serif display) under an internal placeholder name *Forge*. Spec 012 ("es-cr-localization") then renamed the web display brand to **Capital Semilla** (FR-006: title, sidebar header, footer copyright) while leaving the warm-modern visual identity in place; the spec 011 `BRAND-VOICE.md` still carries the *Forge* placeholder. Spec 018 ("pdf-template-lift") branded the Funding Agreement PDF with the canonical sponsor identity (teal palette + seedling mark + partner-logo footer strip + "Programa Semilla" wordmark). Today, an applicant inside the **Capital Semilla**-named, forest-green-themed web app downloads a teal-branded **Programa Semilla**-wordmarked sponsor-bearing PDF — the visual + name divergence undermines program credibility. This spec retires the placeholder visual identity, retires the *Capital Semilla* display name in favor of the canonical sponsor name *Programa Semilla*, and retires the dangling *Forge* references in `BRAND-VOICE.md`. It goes beyond palette: page bg moves to clean white; type stack drops Fraunces in favor of sans-only Inter; component vocabulary (cards, tables, buttons, badges, inputs, sidebar, alerts, modals) is retuned to the airy/crisp PDF feel; sponsor logo strip + seedling mark land on the `_Layout` footer and Login/Register hero so brand presence is felt continuously, not only when downloading agreements. Single mega-spec, full applicant + reviewer + admin + auth surface re-sweep at the spec-011 wow-moment quality bar. E2E POM rewrites budgeted. Schema unchanged. PDF generation unchanged. Code namespaces, project names, and config keys remain `FundingPlatform` (spec 012 invariant).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - End-to-end visual continuity for the applicant (Priority: P1)

An applicant arrives at the platform, sees the Programa Semilla seedling mark and sponsor logos on the Login screen, signs in, navigates the application surfaces (home, journey, signing) under a sidebar that names the platform "Programa Semilla," and eventually downloads a Funding Agreement PDF. Every page of the journey carries the same brand: teal primary, yellow decorative accent, white airy surfaces, sans-only Inter, sponsor partner-logo strip in the footer. The PDF the applicant signs already wears this identity (spec 018); now the web platform that produced it does too.

**Why this priority**: This is the user-visible deliverable. Today the applicant lives inside a forest-green "Capital Semilla"-named app and downloads a teal-branded "Programa Semilla"-wordmarked PDF — the visual + name mismatch undermines program credibility. P1 closes the loop and is the single most important outcome of the spec.

**Independent Test**: Run an applicant E2E flow that lands on Login, signs in, walks the application home + journey + signing surfaces, and downloads the Funding Agreement PDF. Confirm by visual inspection (and Playwright snapshot diff) that brand chrome, palette, typography, and sponsor logos read consistently from first page through PDF.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they open the Login page, **Then** the page renders a left-rail hero showing the seedling mark + "Programa Semilla" wordmark + a tagline, and a sponsor partner-logo strip in the footer.
2. **Given** a signed-in applicant, **When** they navigate any authenticated page, **Then** the sidebar header shows the teal seedling mark + "Programa Semilla" wordmark and the page footer shows the sponsor partner-logo strip.
3. **Given** any applicant surface (home, journey, appeal, signing), **When** the user looks at tables, buttons, cards, badges, inputs, **Then** they read airy/crisp/teal-accented (teal-band table headers + cream zebra rows, solid-teal pill primary buttons, ghost-teal secondary, 1 px bordered cards with no rest shadow, pill badges, 44 px inputs with teal focus ring) — visually consistent with the Funding Agreement PDF.
4. **Given** the applicant downloads their Funding Agreement PDF, **When** they open it next to the platform UI, **Then** brand identity is consistent across both surfaces (same teal primary, same seedling mark, same sponsor logos).

---

### User Story 2 - Reviewer surfaces lift to the same identity, density preserved (Priority: P1)

A reviewer logs in to triage their queue, opens an application detail, signs the supplier's quotation review, and works the signing inbox. Every reviewer surface wears the Programa Semilla identity at the spec-011 reviewer density (`--space-2` cell padding rather than the applicant's `--space-4`). Tables remain dense and scannable but read the new brand: teal header bands, cream zebra, ghost-teal action buttons, retuned status badges.

**Why this priority**: Reviewers spend more time in the platform than any other role; an inconsistent brand experience between applicant and reviewer surfaces would undermine the spec's "brand presence is felt continuously" claim from US1. Density rule from spec 011 (FR-060) must not regress.

**Independent Test**: Run a reviewer E2E that visits queue, detail, signing inbox, history. Assert teal-band table headers across all four surfaces. Measure cell vertical padding ≈ 8 px (`--space-2`); contrast with applicant table padding ≈ 12 px (`--space-3`).

**Acceptance Scenarios**:

1. **Given** a signed-in reviewer, **When** they view the queue, **Then** the table renders with teal header band, cream zebra rows, vertical cell padding ≈ 8 px, and ghost-teal secondary action buttons.
2. **Given** a reviewer in the signing inbox, **When** they open a pending signature, **Then** the modal opens on a white surface with a teal header band and the brand sidebar/footer remain visible.
3. **Given** a reviewer's queue with at least one pending application, **When** the page is screenshot-tested, **Then** the snapshot matches the committed reference for the Programa Semilla identity.

---

### User Story 3 - Admin surfaces lift uniformly across the 10 sub-surfaces (Priority: P1)

An admin signs in to manage Users, Groups, Currencies, Exchange Rates, Legacy Quotations, Suppliers, Reports, Audit. The `/Admin` dashboard from spec 017 (capability cards + KPI tiles + sub-tabs + activity feed) and every sub-surface re-walks at the new bar. KPI tickers retain their motion timing but glow color shifts to teal. Reports tab pill chips and sub-tabs read teal/yellow.

**Why this priority**: Spec 017 just shipped (`017-admin-ux-facelift`); leaving its 10 surfaces on warm-modern tokens while applicants and reviewers are on Programa Semilla would re-introduce visible brand divergence. Same risk profile as spec 011 / 017 — pre-prod aggressive scope.

**Independent Test**: Run an admin E2E that visits `/Admin` index + every sub-surface. Assert teal seedling mark in sidebar, sponsor strip in footer, teal-band tables across all admin tables, KPI tile glow uses teal, Reports pill chips use teal active state.

**Acceptance Scenarios**:

1. **Given** a signed-in admin, **When** they open `/Admin`, **Then** the dashboard renders 4 action KPI tiles + 9 grouped capability cards with teal accents and yellow decorative dividers.
2. **Given** any of the 10 admin sub-surfaces, **When** rendered, **Then** brand chrome, table chrome, button vocabulary, and badge styling all match the reviewer surfaces from US2.
3. **Given** the Reports tab, **When** an admin switches sub-tabs, **Then** the active pill chip uses teal background + white text and animated KPI tickers glow teal (motion timing unchanged from spec 017).

---

### User Story 4 - Signing ceremony retuned (Priority: P2)

When a signing ceremony fires (applicant signs, reviewer signs, both sign), the celebratory take-over view (spec 011 wow moment) replays in the new brand: confetti palette swaps to teal + yellow + neutrals, hero illustration recolors from forest-green to teal, body copy stays voice-guide compliant.

**Why this priority**: Signing ceremony is the platform's emotional peak; brand continuity here is symbolically important. Lower than P1 because it's a transient view (≤ 4 seconds typical), not a daily working surface.

**Independent Test**: Trigger a signing ceremony in E2E, capture a snapshot at `--motion-celebratory` peak, assert confetti particle palette uses teal `#1FA0A0` + yellow `#F2C014` + neutral `#FFFFFF` (not amber/forest), assert hero illustration uses teal strokes.

**Acceptance Scenarios**:

1. **Given** a signing event fires, **When** the ceremony plays, **Then** confetti particles use the retuned palette (teal + yellow + neutrals) and the hero illustration uses teal strokes.
2. **Given** the user has `prefers-reduced-motion: reduce`, **When** the ceremony fires, **Then** confetti is suppressed and the take-over uses a static teal-branded card (motion contract preserved verbatim from spec 011).

---

### User Story 5 - Empty-state illustration set retinted (Priority: P2)

Across all surfaces with empty states (applicant home with no applications, reviewer queue empty, admin tables with no rows), the 9-scene SVG illustration set from spec 011 reads as teal stroke art on white surfaces.

**Why this priority**: Empty states are wayfinding moments; their visual tone sets brand impression on first-use surfaces (especially the applicant home pre-submission and admin tables in fresh deployments). P2 because each individual empty state is rare in steady-state operation.

**Independent Test**: For each of the 9 illustrations, render on a white surface and verify strokes use teal `--color-primary` not forest-green `#2E5E4E`. Assert one E2E surface per illustration.

**Acceptance Scenarios**:

1. **Given** a fresh applicant account with zero applications, **When** the user visits the home surface, **Then** the empty-state illustration renders with teal strokes on a white background.
2. **Given** an admin's Currencies table with zero rows, **When** the page renders, **Then** the empty-state illustration uses teal strokes and the supporting copy uses the retuned voice guide.

---

### User Story 6 - Email templates carry the new identity (Priority: P3)

When a user receives an account confirmation, password reset, or any platform-generated email, the sender display name and signature block read "Programa Semilla / Sistema de Banca para el Desarrollo" rather than the current "Capital Semilla" (or the dangling "Forge" if any template still carries the spec 011 placeholder).

**Why this priority**: Emails are off-platform brand touch-points but rare in user lifecycle (typically 1–3 emails per user). P3 because the wider brand pivot (US1–US5) covers the high-impact surfaces; emails are a long-tail polish item. Sponsor logo embedding is deferred (email-client compatibility) — text update only.

**Independent Test**: Trigger an account confirmation send in a test SMTP fixture, inspect the captured email, assert sender display name and signature block carry the new strings.

**Acceptance Scenarios**:

1. **Given** a new account is created, **When** the confirmation email is dispatched, **Then** the sender display name reads "Programa Semilla / Sistema de Banca para el Desarrollo" and the signature block matches.
2. **Given** a password reset is requested, **When** the email is dispatched, **Then** sender display + signature match US6 #1; no embedded sponsor logos (compatibility); prior "Forge" sender name is absent from any active template.

---

### Edge Cases

- **Sidebar collapsed state** (≤ 992 px viewport or user-collapsed): wordmark hides, only the teal seedling mark visible; hover tooltip surfaces "Programa Semilla."
- **Auth narrow viewport** (≤ 480 px): sponsor partner-logo strip wraps to two rows; if still tight, the 10 años badge hides and sponsor logos stack vertically. Minimum: seedling mark + Programa Semilla wordmark always visible.
- **Empty-state SVGs hardcoded forest-green strokes** → spec mandates regeneration of all 9 illustrations with teal strokes; commit replaces files in place.
- **Confetti library palette** currently amber/forest → swap to teal + yellow + neutrals; the JS module that drives the ceremony must export the new palette constant in one place (no scattered color literals).
- **Print stylesheet**: sponsor strip kept on auth pages (low cost) but hidden on application detail / reviewer queue print views (clutter); print-only test asserts.
- **`forced-colors: active`** (high-contrast OS mode): focus ring becomes `Highlight`; verified once per OS.
- **User session active across deploy**: cached `tokens.css` may serve old palette mid-session → cache-busting query string applied to `tokens.css` reference in `_Layout`.
- **Admin Reports KPI tickers** (spec 017): glow color retunes to teal; motion timing untouched.
- **Signing-ceremony hero illustration** uses spec-011 forest-green; recolor in place.
- **PDF preview iframe** shows the already-teal-branded PDF inside teal-branded chrome → verify visual handoff at the iframe border.
- **Tabler vendored components bypassing partials**: grep gate before merge to catch any missed restyle.
- **Email confirm/reset templates** currently carry "Capital Semilla" sender name (or stale "Forge" in any unswept template) → must update + deploy together; pre-merge check on template files.
- **BRAND-VOICE.md** still carries the spec 011 *Forge* placeholder string in title and examples even though the shipped UI is *Capital Semilla* — this is documented drift; spec sweeps both name forms in one pass. Archived spec-011 BRAND-VOICE.md kept as historical artifact; new canonical file location pinned during planning (OQ-008).
- **Missing brand asset at request time** → log warning, render empty `<img>` with `alt` text fallback ("Programa Semilla" / "Patrocinadores"); no broken page.
- **`tokens.css` fails to load** → Tabler defaults render; page degrades visually but stays operable.
- **Yellow accent used in code-meaningful context** → linter / grep gate fails build; yellow accent is decorative-only by contract.

## Requirements *(mandatory)*

### Functional Requirements

**Brand identity & assets**

- **FR-001**: System MUST replace the display name "Capital Semilla" with "Programa Semilla" across every user-facing surface (sidebar header, page `<title>` template, footer copyright line, email from-name, transactional copy). Any dangling "Forge" placeholder strings inherited from spec 011 (e.g., in `BRAND-VOICE.md`) MUST also be replaced with "Programa Semilla". Code namespaces, project names, and config keys remain `FundingPlatform` (spec 012 invariant carries forward).
- **FR-002**: System MUST replace the in-app brand logo assets (`wwwroot/lib/brand/mark.svg`, `wordmark.svg`, `seal.svg`) with seedling-teal variants matching the PDF logo circle and the Programa Semilla wordmark. PDF assets under `wwwroot/lib/brand/pdf/` MUST remain unchanged (spec 018 invariant).
- **FR-003**: System MUST render a sponsor partner-logo footer strip (Banca para el Desarrollo SBD + CROCUS + nexo + Programa Semilla + 10 años badge) on `_Layout`, anchored at the bottom of the content area, full-width, ≤ 56 px tall, above the existing copyright/legal line.
- **FR-004**: System MUST render a hero left-rail (seedling mark + Programa Semilla wordmark + tagline) on the Login and Register pages, plus the sponsor strip in the footer, plus brand-consistent surfaces on Reset Password and Confirm Email.
- **FR-005**: System MUST replace the favicon (`wwwroot/favicon.ico`) and all sized PWA favicons under `wwwroot/lib/brand/favicons/` with seedling-mark variants.
- **FR-006**: System MUST update email-template sender display name and signature block to "Programa Semilla / Sistema de Banca para el Desarrollo." Sponsor logos MUST NOT be embedded into email bodies (compatibility).

**Design tokens** *(`tokens.css` is the only file allowed to contain raw hex values; spec 011 FR-009 invariant)*

- **FR-007**: System MUST set surface tokens to `--color-bg-page: #FFFFFF`, `--color-bg-surface: #FFFFFF`, `--color-bg-surface-raised: #F7F8F8`, retiring the prior warm cream `#FAF7F2`.
- **FR-008**: System MUST set brand tokens to `--color-primary: #1FA0A0` (sampled from PDF logo disc), `--color-primary-strong: #15807F`, `--color-primary-subtle: #D7EDED`, `--color-primary-rgb: 31, 160, 160`, retiring the prior forest-green primary.
- **FR-009**: System MUST set accent tokens to `--color-accent: #F2C014` (sampled from PDF gold rule), `--color-accent-subtle: #FBEBA6`, retiring the prior amber accent.
- **FR-010**: System MUST introduce a new token `--color-table-zebra: #FFF3E5` (sampled from PDF table cream row) for table body row striping.
- **FR-011**: System MUST keep the existing text-color tokens (`#1A1A1A` / `#5A5A5A` / `#8A8A8A`) and verify each maintains WCAG AA contrast on the new white page background.
- **FR-012**: System MUST retune the status palette tokens (`--color-success`, `--color-warning`, `--color-danger`, `--color-info` and their `*-subtle` variants) so that no warm tint is baked in and each maintains WCAG AA contrast on white.
- **FR-013**: System MUST collapse the type-family stack to sans-only: `--font-display = --font-body = "Inter"`. The Fraunces font-face declaration and vendored files MUST be removed. `--font-mono = "JetBrains Mono"` MUST remain.
- **FR-014**: System MUST keep the existing type-scale sizes and line-heights and bump the display + heading weights (`--type-display-*-weight`, `--type-heading-*-weight`) to recover the visual weight contrast lost when serif display is dropped (target: 700 for display levels, 600 for heading levels — final values pinned by sign-off gate SC-015).
- **FR-015**: System MUST remap the Tabler `--tblr-*` bridge variables (`--tblr-primary`, `--tblr-secondary`, etc.) to the new primary and accent values.
- **FR-016**: System MUST retune `--shadow-glow-primary` to use the new primary RGB so focus and hover glows read teal.
- **FR-017**: System MUST keep the spec 011 motion catalog tokens (`--motion-instant`, `--motion-fast`, `--motion-base`, `--motion-slow`, `--motion-celebratory`) and spring-easing tokens unchanged in duration and easing. The reduced-motion contract at the bottom of `tokens.css` MUST be preserved verbatim.

**Component retune** *(must apply across every swept surface; tokens cascade through partials)*

- **FR-018**: Buttons — primary button MUST render solid teal background with white text and pill radius. Secondary MUST render ghost-teal (transparent background, teal border + text). Danger MUST render solid danger color. Minimum touch height 44 px.
- **FR-019**: Tables — header row MUST render a solid teal band with white semibold text. Body MUST zebra-stripe alternating `--color-bg-surface` and `--color-table-zebra`. Cell vertical padding MUST be `--space-3` on applicant surfaces and `--space-2` on reviewer surfaces (FR-031). No internal grid lines on body rows.
- **FR-020**: Cards — `1 px solid --color-border`, no rest shadow, `--shadow-md` on hover/focus, `--radius-md`.
- **FR-021**: Badges — filled with pill radius, semibold weight. Variants: primary teal, accent yellow (with dark text overlay because `#F2C014` on white fails AA — see NFR-003), and the four status colors (success / warning / danger / info) on retuned tokens.
- **FR-022**: Inputs — minimum height 44 px, soft border, teal focus ring (4 px outer, 2 px inner). Validation states MUST use the corresponding status colors.
- **FR-023**: Alerts — left teal/status accent bar, soft tinted background, dark text.
- **FR-024**: Modals — white surface, teal header band, no heavy shadow.
- **FR-025**: Sidebar header restructured — teal seedling mark + "Programa Semilla" wordmark; collapsed state shows mark only with hover tooltip "Programa Semilla". All sidebar `data-testid` slugs from spec 017 FR-016 MUST remain present and findable.
- **FR-026**: Empty-state illustrations — all 9 SVGs from spec 011 MUST be regenerated with teal strokes (replacing forest-green strokes); each MUST be verified visually on white background.

**Surface sweep**

- **FR-027**: System MUST apply the new tokens + component retune across the full surface inventory: applicant (home, dashboard, journey, appeal, signing); reviewer (queue, detail, signing inbox, history); admin (index, Users, Groups, Currencies, Exchange Rates, Legacy Quotations, Suppliers, Reports, Audit); auth (Login, Register, Reset Password, Confirm Email); shared `_Layout` chrome.
- **FR-028**: System MUST ship a manual `BRAND-PIVOT-SWEEP-CHECKLIST.md` deliverable inside the spec directory, with one row per swept surface and columns for each verification axis: visual tokens / component vocabulary / voice-guide compliance / sponsor chrome / motion / accessibility. Every row MUST be checked before merge.
- **FR-029**: System MUST re-walk the four spec-011 wow moments (applicant home dashboard, journey timeline, signing ceremony, reviewer queue dashboard) at the new bar and update each surface's reference visual snapshot.
- **FR-030**: System MUST rewrite `BRAND-VOICE.md` so that the display-name pivot (Forge / Capital Semilla → Programa Semilla) lands in title, examples, and display-name references. Tone, person, and stage-aware patterns from spec 011 MUST remain unchanged.
- **FR-031**: System MUST preserve spec 011 FR-060: reviewer surfaces use `--space-2` cell padding, applicant surfaces use `--space-4`. The density rule MUST NOT regress.

**Testing & verification**

- **FR-032**: System MUST budget E2E Page Object Model rewrites across all swept surfaces. Semantic locator strategy from spec 011 (ARIA roles + accessible names; `data-testid` only where role/name is insufficient) MUST be preserved.
- **FR-033**: Each swept surface MUST gain at least one E2E assertion that proves brand presence (sidebar header text "Programa Semilla", footer sponsor strip image present, sponsor strip on Login).
- **FR-034**: A dedicated reduced-motion Playwright test MUST remain green; no new motion outside the spec 011 catalog is permitted.
- **FR-035**: WCAG AA contrast MUST be verified via `axe-playwright` on at least 5 representative surfaces (applicant home, reviewer queue, admin index, login, signing ceremony).
- **FR-036**: Visual regression — at least 4 reference snapshot images (applicant home, reviewer queue, admin index, login) MUST be updated and committed; diff reviewed on PR.
- **FR-037**: Total brand-related asset wire weight (fonts + logos + illustrations + sponsor strip) MUST be ≤ 400 KB gz.

**Out-of-scope guardrails**

- **FR-038**: Schema MUST remain unchanged. `git diff main -- src/FundingPlatform.Database/` MUST be empty after this spec lands.
- **FR-039**: PDF generation pipeline (spec 018) MUST remain unchanged. The Funding Agreement PDF generator and its assets under `wwwroot/lib/brand/pdf/` MUST NOT be edited.
- **FR-040**: Public marketing surface remains out of scope (spec 011 OOS clause carries forward).
- **FR-041**: Localization layer (spec 012) MUST remain unchanged. Voice-guide rewrites MUST keep copy out of partials' code paths so future-localization compatibility is preserved.
- **FR-042**: Tabler.io vendored bundle MUST NOT be upgraded.

### Key Entities

This spec does not introduce or alter any data entities. (Schema unchanged — FR-038.)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A grep for the legacy palette hex values (`#2E5E4E`, `#1F4438`, `#E1ECE6`, `#D98A1B`, `#FBEED6`, `#FAF7F2`, `#F4EFE6`, `#E5DED2`) returns zero hits outside `tokens.css` history comments and PDF brand assets.
- **SC-002**: Grep for the strings "Forge" and "Capital Semilla" returns zero hits outside git history, the archived spec-011 `BRAND-VOICE.md`, and spec/brainstorm/changelog documents. (Inside running views, layouts, partials, brand SVGs, email templates, and `BRAND-VOICE.md` content, both names MUST be absent.)
- **SC-003**: The sponsor partner-logo strip is rendered on every authenticated page (`_Layout`) and on Login + Register + Reset Password + Confirm Email, verified via a per-surface E2E assertion.
- **SC-004**: An audit script (extending spec 011's existing tooling) confirms `tokens.css` is the only file containing raw hex color values.
- **SC-005**: `axe-playwright` contrast pass on at least 5 representative surfaces (applicant home, reviewer queue, admin index, login, signing ceremony).
- **SC-006**: All 4 spec-011 wow moments are re-walked at the new bar and their visual snapshots updated.
- **SC-007**: `BRAND-VOICE.md` is updated and per-string voice-guide review is checked off in `BRAND-PIVOT-SWEEP-CHECKLIST.md` for every swept view.
- **SC-008**: `BRAND-PIVOT-SWEEP-CHECKLIST.md` is shipped with every row checked.
- **SC-009**: The full E2E suite has been executed locally and is green (delivery bar — saved feedback memory).
- **SC-010**: A dedicated reduced-motion Playwright test is green; no new motion has been introduced outside the spec 011 catalog.
- **SC-011**: Total brand-related asset wire weight measures ≤ 400 KB gz.
- **SC-012**: Visual regression snapshots committed for at least 4 surfaces; diff reviewed on PR.
- **SC-013**: `git diff main -- src/FundingPlatform.Database/` is empty.
- **SC-014**: A regenerated fixture Funding Agreement PDF is byte-equal to a pre-pivot fixture (or differs only in document-creation timestamp).
- **SC-015**: User sign-off gate — the hex palette, sponsor-strip layout, and sidebar header layout have been reviewed and approved by the user before merge.

### Non-Functional Requirements

- **NFR-001**: Performance — LCP and TBT on applicant home and reviewer queue MUST NOT regress versus the spec 011 baseline (`specs/011-warm-modern-facelift/perf-baseline.json`). A new baseline MUST be captured and committed.
- **NFR-002**: Asset budget — total brand-related asset wire weight ≤ 400 KB gz (matches FR-037 / SC-011). Removing Fraunces frees ≈ 35 KB of headroom.
- **NFR-003**: Accessibility — WCAG AA on all swept surfaces. The yellow accent `#F2C014` on white measures ≈ 1.7:1 contrast and is therefore reserved for decorative dividers and filled-badge backgrounds with dark text overlay; a linter/grep gate enforces that yellow MUST NOT carry semantic meaning (icons-with-meaning, focus rings, alert text).
- **NFR-004**: Browser support — last 2 evergreen browsers + iOS Safari (matches spec 011).
- **NFR-005**: Email rendering — sponsor logo strip MUST NOT be embedded in email-template HTML (compatibility); only sender display name and signature block update.

## Assumptions

- The Programa Semilla brand book (if it exists) confirms or is compatible with teal `#1FA0A0` + yellow `#F2C014` sampled from the seed PDF. If not, the user sign-off gate (SC-015) catches the override.
- Sponsor partner-logo strip is composed from individual SVG sources for Banca para el Desarrollo SBD, CROCUS, nexo, Programa Semilla, and the 10 años badge. Acquiring/extracting these sources is a planning-phase task (OQ-002).
- The four spec-011 wow moments + 9-scene illustration set + canvas-confetti integration remain as-is in structure; this spec only retunes their tokens, palette, and voice references.
- The web platform is a single tenant for Programa Semilla. Multi-tenant brand swapping is OOS; if SBD ever wants sister programs, that's a different spec.
- Sponsor brand-usage permissions are assumed to be the same as those already exercised by the funding-agreement PDF (spec 018). A formal legal audit of CROCUS/nexo logo permissions is OOS.
- The 10 años badge is currently in-force; graceful retirement (when "10 años" stops being true) is OOS for this spec but flagged as OQ-007.
- The platform is pre-production. Aggressive single-mega-spec scope and HTML-restructuring + POM-rewrite costs are accepted (saved memory: UX/UI quality > E2E selector stability).
- Brand-VOICE.md location may move (current at `specs/011-warm-modern-facelift/BRAND-VOICE.md`); pinning the canonical location is OQ-008 and decided during planning.
- "Programa Semilla" as the sidebar/page-title display name is the canonical form (Spanish, no English equivalent). Localization layer (spec 012) is unchanged.

## Dependencies

- **Spec 011 (`011-warm-modern-facelift`)**: This spec modifies in place the tokens, partials, wow moments, motion catalog, illustration set, and BRAND-VOICE.md introduced by spec 011. The spec 011 motion catalog and reduced-motion contract are preserved verbatim; the palette, page bg, type stack, and component vocabulary are replaced.
- **Spec 017 (`017-admin-ux-facelift`)**: All 10 admin sub-surfaces are re-walked at the new bar.
- **Spec 018 (`018-pdf-template-lift`)**: PDF generation pipeline and `wwwroot/lib/brand/pdf/` assets are reused as the visual ground truth for sampling teal + yellow + sponsor composite. PDF generation is otherwise untouched.
- **Spec 012 (`012-es-cr-localization`)**: Voice-guide rewrites MUST keep copy out of partials' code paths so future-localization compatibility (resource files + culture middleware) is preserved.
- No new managed (NuGet) dependencies. The spec 011 `canvas-confetti` carve-out is preserved.
- Sponsor logo source files (Banca para el Desarrollo SBD, CROCUS, nexo, Programa Semilla, 10 años) — extract from PDF assets or request originals from sponsors. Acquisition is a planning-phase task.

## Out of Scope

- Public marketing surface (still deferred from spec 011).
- Schema or database changes.
- PDF generation pipeline behavior (spec 018 invariant).
- Localization layer / translation files (spec 012 invariant).
- Tabler.io vendored bundle upgrade.
- Net-new wow moments beyond the four spec 011 originals.
- Email-embedded sponsor logos (compatibility).
- Multi-tenant brand swapping (single-tenant assumption).
- Sponsor brand-usage legal audit beyond what already ships in the funding-agreement PDF.

## Open Questions

- **OQ-001**: Exact teal hex — sampled `#1FA0A0` from the PDF logo disc; if the Programa Semilla brand book pins a different value, designer override at the sign-off gate (SC-015).
- **OQ-002**: Sponsor logo source — extract-from-PDF (low fidelity) versus request originals (slow). Pinned during planning.
- **OQ-003**: Login hero — large seedling mark only versus a commissioned scene. Defaults to mark-only for budget; revisit only if planning surfaces a strong argument.
- **OQ-004**: Sidebar collapsed-state breakpoint — Tabler default 992 px versus a custom value. Pinned during planning.
- **OQ-005**: Confetti palette specifics — teal + yellow only, or teal + yellow + cream + danger-soft. Pinned during planning.
- **OQ-006**: Email signature layout — text-only versus inline seedling mark (compatibility risk in some clients). Defaults to text-only.
- **OQ-007**: 10 años badge — when "10 años" stops being current, what's the graceful-retirement plan? Flagged for future spec; this spec leaves the badge as-is.
- **OQ-008**: BRAND-VOICE.md canonical location — repo root, new spec dir, or replace spec 011's in place. Pinned during planning.
- **OQ-009**: Visual-regression tooling — continue with Playwright screenshot comparison (baseline carried from spec 011) versus adopt Percy/Chromatic. Defaults to Playwright; revisit if planning surfaces a strong argument.
