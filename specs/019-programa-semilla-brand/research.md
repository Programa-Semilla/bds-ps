# Research — Spec 019 Programa Semilla Brand Pivot

**Branch**: `019-programa-semilla-brand` · **Date**: 2026-05-09

Resolves the planning-deferred decisions from `REVIEW-SPEC.md` and the open questions in `spec.md` (OQ-001..OQ-009). Each section names the open thread, pins the decision, cites evidence, and records alternatives considered.

## R1 — Teal hex value (resolves OQ-001)

**Open thread**: Spec samples `#1FA0A0` from the spec-018 PDF logo disc; if a Programa Semilla brand book pins a different value, designer override at SC-015.

**Decision**: Adopt `#1FA0A0` as `--color-primary` and ship to the SC-015 user sign-off gate as-is. Strong (`#15807F`) and subtle (`#D7EDED`) variants are derived as `oklch` darken / lighten of the primary; values are pinned in `tokens.css`.

**Evidence**:
- The PDF brand assets under `src/FundingPlatform.Web/wwwroot/lib/brand/pdf/` (spec 018) carry `#1FA0A0` as the seedling-disc fill. Sampling agrees with the spec.
- No formal Programa Semilla brand book is available to the project team at planning time (sponsor coordination is out-of-scope per spec assumption "Sponsor brand-usage permissions are assumed to be the same as those already exercised by the funding-agreement PDF").
- Spec 018 has shipped `#1FA0A0` in production-bound PDFs for ≥ 1 sprint without retraction.

**Rationale**: The PDF is the visual ground-truth artifact already shared with sponsors. Sampling from the same artifact is the highest-fidelity choice short of a brand book. Sign-off at SC-015 catches a subsequent sponsor override without forcing a re-sample now.

**Alternatives considered**:
- Hand-pick a teal from a Material/Tailwind palette (rejected — disconnected from the PDF).
- Wait for a sponsor brand book (rejected — sponsor coordination is OOS for this spec; would block all 30+ surface sweeps indefinitely).

## R2 — Sponsor logo sources (resolves OQ-002)

**Open thread**: Extract sponsor logos from PDF assets (low fidelity, fast) versus request originals from sponsors (high fidelity, slow).

**Decision**: Extract initial sponsor SVGs from the existing PDF asset strip (`src/FundingPlatform.Web/wwwroot/lib/brand/pdf/footer-partners-strip.png`) and re-trace each logo as a clean SVG using a vector-tracing tool (e.g., `inkscape --export-type=svg --export-area-page` after upscaling). Commit one SVG per sponsor under `wwwroot/lib/brand/sponsors/{sbd,crocus,nexo,programa-semilla,10-anos}.svg`. Flag a follow-up task to request original vector sources from sponsors and replace in place once received (no spec change required — same file paths).

**Rationale**:
- The spec-018 PDF strip is already in production and visually accepted; matching its fidelity is sufficient for "brand presence is felt continuously" (spec rationale).
- Re-tracing to SVG (not embedding raster PNGs) keeps the asset-budget inside the 400 KB gz cap (NFR-002) and stays sharp at all zoom levels.
- File-path stability means the planned follow-up logo replacement is a drop-in commit, no code change.

**Alternatives considered**:
- Inline the raster `footer-partners-strip.png` directly in the layout (rejected — no per-logo control for narrow-viewport stacking; raster would also break the 400 KB gz cap if other assets grew).
- Block on sponsor coordination (rejected — see R1; would freeze the sweep).

## R3 — Login hero composition (resolves OQ-003)

**Open thread**: Login hero — large seedling mark only, or a commissioned scene?

**Decision**: Mark-only hero (large seedling mark + "Programa Semilla" wordmark + tagline copy from the voice guide). Reuse the regenerated `mark.svg` at 240×240 px on desktop; auto-shrink to 96×96 px below 480 px viewport (per FR-004 + spec edge case "Auth narrow viewport").

**Rationale**:
- Budget — a commissioned hero scene would add 40–80 KB raster + an asset-acquisition delay, conflicting with NFR-002 (≤ 400 KB gz total) and the pre-prod aggressive-scope bar.
- The spec-018 PDF hero treatment is also mark + wordmark (no scene); matching the PDF reinforces "brand presence is felt continuously" (US1).
- A future commissioned scene can drop in at the same Razor partial without spec change.

**Alternatives considered**:
- Commissioned scene (rejected — budget + timeline + low marginal user-value).
- Hero hidden on Login (rejected — would remove the most direct brand introduction surface for new users; conflicts with FR-004).

## R4 — Sidebar collapsed-state breakpoint (resolves OQ-004)

**Open thread**: Use Tabler default 992 px or a custom value?

**Decision**: Tabler default `992 px`. No override.

**Rationale**:
- Tabler's vendored layout already collapses the sidebar at `992 px` via its `@media (max-width: 991.98px)` rules. Overriding would require a parallel media-query block in `tokens.css` (or a `_Layout` style override), violating the "tokens.css holds visual values; partials read tokens" contract by introducing structural CSS.
- 992 px maps cleanly to the iPad portrait threshold; the team has not observed user complaints at this breakpoint in spec 011 / 017 production traffic.
- Spec FR-025 only mandates that the collapsed state shows the seedling mark with a hover tooltip "Programa Semilla" — it does not pin a breakpoint.

**Alternatives considered**:
- 1024 px (rejected — would split tablets unevenly; no observed pain).
- Custom value via CSS variable (rejected — tokens.css would gain a structural breakpoint variable, which is out of contract).

## R5 — Confetti palette specifics (resolves OQ-005)

**Open thread**: Teal + yellow only, or teal + yellow + cream + danger-soft?

**Decision**: 4-color palette — `#1FA0A0` (teal), `#F2C014` (yellow), `#FFFFFF` (neutral white), `#D7EDED` (primary subtle). Cream is dropped (page background is white now, so cream particles read as warm-out-of-place); danger-soft is dropped (semantically inappropriate for a celebratory ceremony).

**Implementation**:
- The single confetti palette constant lives in the existing JS module that drives the ceremony (no scattered color literals — spec edge case "Confetti library palette" + voice clause).
- Constants reference `tokens.css` values via a JS bootstrapped variable read on page load (`getComputedStyle(document.documentElement).getPropertyValue('--color-primary')`), keeping `tokens.css` the single source of raw hex (FR-009 invariant from spec 011).

**Rationale**:
- 4 colors give enough particle variety to feel celebratory without muddying the palette read.
- Pinning to teal + yellow + neutrals enforces the spec's brand-continuity claim with the PDF (which uses exactly those three families — teal disc, gold rule, white surface).

**Alternatives considered**:
- 2-color (teal + yellow) (rejected — too sparse; spec-011 ceremony uses 4 particles for visual density).
- 5-color including a cream (rejected — clashes with white page bg and the spec's "retire the warm cream" language).

## R6 — Email signature layout (resolves OQ-006)

**Open thread**: Text-only signature, or inline seedling mark (compatibility risk in some clients)?

**Decision**: Text-only signature block. No inline images. Enforces NFR-005 verbatim.

**Rationale**:
- Email-client compatibility for inline `<img>` tags varies wildly (Outlook strips inline references, Gmail clips beyond ~102 KB, Apple Mail and Thunderbird render fine) — a text-only block renders consistently everywhere.
- Sender display name carries the brand identity for the user; the in-app surface is the high-fidelity brand surface, not the email body.
- Spec 011 / 017 / 018 set no email-rendering precedent that requires images; text-only is the simplest safe default.

**Alternatives considered**:
- Inline mark in the signature (rejected — see compatibility above).
- HTML-with-CID attached image (rejected — implementation cost + Outlook still inconsistent).

## R7 — 10 años badge retirement (acknowledges OQ-007)

**Open thread**: When "10 años" stops being current, what's the graceful-retirement plan?

**Decision (this spec)**: Out of scope. Ship the badge as-is. Document the future-spec trigger.

**Future-spec trigger**: When the program crosses its anniversary boundary (presumably mid-2026), open a new spec to swap or hide the `10-anos.svg` asset. The replacement is a drop-in SVG at the same file path; no code change required. Sponsor strip layout already accommodates partner-logo addition / removal.

**Rationale**: Spec 019 is already a wide pivot. Adding date-aware badge logic would expand scope without short-term value. The future-spec swap is small, isolated, and well-scoped.

## R8 — `BRAND-VOICE.md` canonical location (resolves OQ-008)

**Open thread**: Repo root, new spec directory, or replace spec 011's in place?

**Decision**: Move the canonical file to `BRAND-VOICE.md` in the repo root. Mark `specs/011-warm-modern-facelift/BRAND-VOICE.md` as `# (HISTORICAL — see /BRAND-VOICE.md)` in a single-line top banner, leaving its body untouched as the spec-011 historical artifact.

**Rationale**:
- Brand voice is project-wide, not feature-scoped. Pinning it to a spec directory misleads future contributors into thinking voice is owned by spec 011 (or 019). Repo-root placement matches `CLAUDE.md` and `README.md` conventions and is the first place a new contributor will look.
- Keeping the spec-011 file in place (with a single redirect banner) preserves git history and prevents stale links from spec 011's other artifacts.
- Single canonical file means the brand-grep gate (`scripts/brand-grep-gate.sh`) can target one path and assert "Forge" / "Capital Semilla" are absent there.

**Alternatives considered**:
- Replace spec 011's file in place (rejected — spec 011 review artifacts and the spec-011 PR commit history reference it; mutating it post-merge is bad commit hygiene).
- Place under `specs/019-programa-semilla-brand/` (rejected — voice is not a 019-only artifact).

## R9 — Visual-regression tooling (resolves OQ-009)

**Open thread**: Continue with Playwright screenshot comparison (baseline carried from spec 011) or adopt Percy/Chromatic?

**Decision**: Continue with Playwright screenshot comparison. Refresh the 4 baseline images (applicant home, reviewer queue, admin index, login) committed under this spec dir alongside the existing baseline images carried from spec 011.

**Rationale**:
- The team already runs Playwright screenshots in CI; switching to Percy/Chromatic would add a new managed dependency (NuGet / npm), per-image upload cost, and an external SaaS contract — none of which is justified by the marginal review UX benefit at this scale (4 surfaces).
- Spec 011 baseline carried forward without operational pain. Same expectations apply post-pivot.
- Snapshot diffs are reviewed manually on PR (FR-036 / SC-012), which is sufficient at single-tenant pre-prod scale.

**Alternatives considered**:
- Adopt Percy (rejected — managed-dep cost; per-snapshot pricing; no current pain).
- Adopt Chromatic (rejected — same as Percy; tighter Storybook coupling, which the project does not use).

## R10 — Display + heading weight values (pins FR-014)

**Open thread**: FR-014 mandates bumping `--type-display-*-weight` and `--type-heading-*-weight` to recover visual weight contrast lost when serif display drops in favor of sans Inter. Spec leaves the final values "pinned by sign-off gate SC-015."

**Decision (planning recommendation, subject to SC-015 sign-off)**:
- `--type-display-1-weight`: `700`
- `--type-display-2-weight`: `700`
- `--type-display-3-weight`: `700`
- `--type-heading-1-weight`: `600`
- `--type-heading-2-weight`: `600`
- `--type-heading-3-weight`: `600`
- `--type-heading-4-weight`: `600`
- `--type-body-weight`: `400` (unchanged)
- `--type-body-emphasis-weight`: `600` (unchanged)

**Rationale**:
- Inter at 700 is the standard "bold display" weight in the Inter design system; it reads visibly heavier than 600 even at large sizes (40–48 px display).
- 600 for headings preserves the spec-011 visual contrast between display and heading levels, which the `Fraunces 500` → `Inter 600` jump otherwise loses.
- These values are within the vendored Inter weight files already on disk (`Inter-VariableFont` covers 100–900); no new font file is required.
- Subject to SC-015: the user reviews the live applicant home + admin index renderings before merge and pins (or overrides) these values.

**Alternatives considered**:
- 800 display (rejected — too heavy at smaller display sizes; "shouty").
- 500 headings (rejected — no recovery of weight contrast; defeats FR-014's purpose).

## R11 — Yellow-accent grep gate enforcement (pins NFR-003 enforcement)

**Open thread**: NFR-003 states "yellow accent MUST NOT carry semantic meaning"; how is this enforced in CI?

**Decision**: Extend `scripts/brand-grep-gate.sh` (new — see plan) with a heuristic grep that fails the build when `--color-accent` (or its underlying hex `#F2C014`) appears inside any of:
- `color:` rule on a rule that targets `<a>`, `<button>`, an icon class with semantic intent (e.g., classes containing `icon-status-`, `icon-warning-`, `icon-error-`), or any selector containing the keywords `error`, `danger`, `warning`, `info`, `status`, `valid`, `invalid`.
- `border-*-color:` on the same set.
- `outline-color:` (which would imply focus-state semantics).
- `fill:` / `stroke:` on SVGs flagged via class as `icon-*-meaningful`.

The script greps `src/FundingPlatform.Web/wwwroot/css/`, all `.cshtml` partials, and inline `<style>` blocks (which should be zero per spec-011 invariant; the grep doubles as a regression check).

**Rationale**:
- The yellow accent fails AA contrast on white (1.7:1, NFR-003); using it semantically would make critical UI elements unreadable for low-vision users.
- A grep gate is cheaper than a runtime contrast check and runs in <1 s in CI.
- Decorative use (dividers, badge backgrounds with dark text overlay where AA holds via the dark text) is allowed; the grep only flags semantic contexts via the keyword heuristics above.

**Alternatives considered**:
- Runtime axe contrast check only (rejected — would catch the issue but only after CI runs the full Playwright suite; cheap grep gate fails fast).
- Manual code review (rejected — `BRAND-PIVOT-SWEEP-CHECKLIST.md` has a per-surface row, but a grep gate enforces invariant across future commits).

## R12 — Cache-bust strategy for `tokens.css` (resolves spec edge case)

**Open thread**: Spec edge case "User session active across deploy: cached `tokens.css` may serve old palette mid-session — cache-busting query string applied to `tokens.css` reference in `_Layout`."

**Decision**: Append a build-time hash query parameter to the `tokens.css` link in `_Layout.cshtml`: `<link rel="stylesheet" href="/css/tokens.css?v=@FundingPlatformBuildInfo.Hash" />`. The hash comes from a single-line generated source file written during `dotnet build` (an MSBuild target reads `git rev-parse --short HEAD`, falls back to the build timestamp ticks if git is unavailable, and emits `BuildInfo.g.cs`).

**Rationale**:
- File-content hash (e.g., MD5 of `tokens.css`) would force a recompile to refresh — overkill for a stylesheet that ships once per deploy.
- A git-hash querystring invalidates per deploy, which is the actual cache-bust unit (spec language: "across deploy"). Stale browsers fetch the new file on next page load.
- Pattern is reusable — additional cache-sensitive vendored assets (`canvas-confetti`, illustrations) can layer the same query if a future spec needs it.

**Alternatives considered**:
- Webpack-style content hashes in filename (rejected — no bundler in the project; vendored CSS pipeline is direct copy).
- HTTP cache-control headers only (rejected — relies on hosting infra, not portable across dev / Aspire / production environments).

## R13 — Surfaces requiring print-stylesheet adjustments (pins spec edge case)

**Open thread**: Spec edge case "Print stylesheet: sponsor strip kept on auth pages (low cost) but hidden on application detail / reviewer queue print views (clutter); print-only test asserts."

**Decision**: Add a single `@media print` block to `tokens.css` that scopes `display: none` to `[data-print-hide="sponsor-strip"]`. The applicant detail surfaces, reviewer queue, and reviewer detail views set the `data-print-hide="sponsor-strip"` attribute on the sponsor-strip partial wrapper. Auth surfaces (Login / Register / Reset / Confirm) and the bare _Layout do not set the attribute, so the strip prints there.

**Verification**: A new Playwright test (`Tests.E2E/Brand/PrintLayoutTests.cs`) emulates `media=print` and asserts (a) sponsor strip is absent on `/Application/{id}` + reviewer queue, (b) sponsor strip is present on `/Account/Login`.

**Rationale**:
- Single CSS rule + per-surface attribute is cheaper than per-surface stylesheet overrides.
- The attribute name is greppable (`data-print-hide=`), so future surfaces can opt-in / opt-out with a one-line change.

**Alternatives considered**:
- Per-surface print stylesheet (rejected — proliferates rules and conflicts with the "tokens.css is the only file with raw values" contract).
- Hide globally and conditionally show on auth (rejected — inverted default makes auth surfaces the special case, which is the wrong polarity given most prints are reports/agreements).

## R14 — `--space-2` vs `--space-4` density rule (preserves FR-031)

**Open thread**: Spec 011 FR-060 set reviewer cell padding `--space-2` (8 px) and applicant `--space-4` (16 px); FR-031 forbids regression. Tokens.css rewrite touches the surface-level partials.

**Decision**: Density is enforced in the `_Tables` partial via `data-density` attribute on `<table>` elements:
- `data-density="reviewer"` selector applies `--space-2` cell padding (vertical).
- `data-density="applicant"` selector applies `--space-4` (default if attribute absent or set to `applicant`).

The retuned `_Tables` partial (FR-019) preserves both selectors verbatim from spec 011's CSS; only the visual treatment (header band color, zebra stripe color) changes.

**Rationale**:
- `data-density` is already the spec-011 mechanism; reusing it preserves the FR-031 invariant without re-architecting partials.
- A CSS-level decision keeps density a styling concern and not a layout / Razor concern, so all reviewer surfaces inherit the rule via attribute presence.
- E2E test US2 #1 measures cell vertical padding ≈ 8 px on the reviewer queue and contrasts with applicant table padding ≈ 16 px (`--space-4`, the spec-011 FR-060 canonical applicant default). The padding measurements re-run unchanged.

**Alternatives considered**:
- Add a separate `_ReviewerTable` partial (rejected — duplicates the `_Tables` partial just for one rule; harder to maintain).

## R15 — `axe-playwright` representative-surface set (pins FR-035 / SC-005)

**Open thread**: FR-035 / SC-005 mandate ≥ 5 representative surfaces for `axe-playwright` AA; spec lists "applicant home, reviewer queue, admin index, login, signing ceremony."

**Decision**: Adopt the spec-listed 5 verbatim. No additions for this spec. Future specs can layer additional surfaces if scope warrants.

**Rationale**:
- The 5 cover one applicant surface (home), one reviewer surface (queue), one admin surface (index), one auth surface (login), and one transient ceremony view (signing). They span the full role-and-state matrix at minimum density.
- Adding more surfaces is cheap (10–20 min per new test) but yields diminishing returns; CI stays under the existing E2E budget.
- `axe-playwright` is already wired in the project (carried from spec 011); no new tooling.

**Alternatives considered**:
- Test all ~30 swept surfaces (rejected — CI runtime + maintenance cost; the 5-surface sample matches the user's instruction "at least 5" floor).
- Test fewer (4) (rejected — spec mandates a floor of 5).
