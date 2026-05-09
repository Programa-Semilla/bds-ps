# Brainstorm: Programa Semilla Brand Pivot

**Date:** 2026-05-09
**Status:** spec-created
**Spec:** specs/019-programa-semilla-brand/

## Problem Framing

The platform's web UI and the Funding Agreement PDF it produces wear two different brands. Spec 011 ("warm-modern facelift") shipped a placeholder visual identity (forest-green primary `#2E5E4E` + warm-amber accent `#D98A1B` + warm cream page bg `#FAF7F2` + Fraunces serif display) under an internal placeholder name *Forge*. Spec 012 ("es-cr-localization") then renamed the web display brand to *Capital Semilla* (FR-006) — but only the display name moved; the warm-modern visual identity stayed in place, and the spec 011 `BRAND-VOICE.md` still carries the *Forge* placeholder. Spec 018 ("pdf-template-lift") then branded the Funding Agreement PDF with the canonical sponsor identity: teal palette + seedling mark + partner-logo footer strip (Banca para el Desarrollo SBD + CROCUS + nexo + Programa Semilla + 10 años) + the *Programa Semilla* wordmark from the seed PDF at `brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`.

Today an applicant lives inside a forest-green *Capital Semilla*-named app and downloads a teal-branded *Programa Semilla*-wordmarked sponsor-bearing PDF. The visual + name divergence undermines program credibility. The user's seed brief: *"Using @Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf as reference, I want all the UI look & feel to match it, i.e. the colors but going beyond that — i want that to become the platform brand reference. I want a UX/UI rework that looks professional and more aligned with the reference."* — followed by the reinforcement: *"make sure UI elements follow the new feeling."*

Session shape: this was a NEW brainstorm (#17), not a revisit of #11 (warm-modern facelift) or #16 (PDF template lift). The user explicitly chose a new doc when offered the revisit option. Spec 011's tokens, partials, wow moments, motion catalog, illustration set, and BRAND-VOICE.md are modified in place by the new spec; spec 011's motion catalog and reduced-motion contract are preserved verbatim; spec 011's placeholder name *Forge* and its successor *Capital Semilla* are both retired in one pass.

## Strategic Decisions Made

### Brand anchor

Three options were considered:

- **A: Full sponsor identity.** Rebrand platform as *Programa Semilla*, adopt seedling mark in sidebar/auth, sponsor logo strip in `_Layout` footer + auth pages.
- **B: Visual language only.** Adopt teal/yellow palette + typography + table style; keep the *Capital Semilla* name; no sponsor logos on web.
- **C: Hybrid.** Visuals + sponsor strip on auth/landing only; authenticated app stays cleaner.

**Decision: A, "full sponsor identity."** End-to-end visual + name parity with the funding-agreement PDF. *Capital Semilla* and the spec 011 placeholder *Forge* both retire. Sponsor strip lands on `_Layout` (every authenticated page) and on Login + Register + Reset + Confirm. Code namespaces, project names, and config keys remain `FundingPlatform` (spec 012 invariant).

### Sweep depth

Three options:

- **A: Full re-sweep at 011/017 bar.** Re-walk every applicant + reviewer + admin + auth surface with the new tokens; retune table chrome to match PDF (teal header + cream zebra); refresh hero/empty states; rewrite POMs as needed. Mirrors saved feedback memory: UX/UI quality > selector stability.
- **B: Token re-anchor + signature surfaces only.** Cascade through 011 partials; targeted refresh on auth, sidebar, landing, footer.
- **C: Tokens-only swap.** Just rewrite tokens.css + replace logo assets + update BRAND-VOICE name.

**Decision: A, "full re-sweep at 011/017 bar."** User explicitly said *"make sure UI elements follow the new feeling"* — every component (cards, tables, buttons, badges, inputs, sidebar, alerts, modals) gets retuned. POM rewrites budgeted across all surfaces.

### Type stack

Three options:

- **A: Sans-only Inter throughout.** Drop Fraunces; Inter for headings + body; JetBrains Mono kept for codes/IDs.
- **B: Keep Fraunces + Inter.** Spec 011 + 018 already declared this stack; web headings get serif voice.
- **C: Identify the PDF's exact font and adopt.** Highest fidelity; licensing + asset-budget risk.

**Decision: A, "sans-only Inter."** The seed PDF reads as a humanist sans for both headings and body — visual reading takes precedence over spec 018's declared stack. Drops ≈ 35 KB of font weight (frees asset-budget headroom under NFR-002 / SC-011). Spec 018's PDF generation continues to use Fraunces, creating a small dual-stack between web and PDF; flagged as an Open Question for designer review.

### Component feel

Three options:

- **A: Airy + crisp (PDF-faithful).** Page bg white (not cream); cards have subtle border, no rest shadow, lift on hover; tables have teal solid header band + cream zebra rows + extra row padding; buttons are teal solid primary, ghost-teal secondary, pill radius; badges teal/yellow/danger filled; inputs 44 px tall, soft border, teal focus ring.
- **B: Keep warm-modern surfaces, swap hues only.** Retain cream page bg + soft shadows + current radii from spec 011; just swap green→teal, amber→yellow.
- **C: Hybrid.** Adopt teal-header tables fully but keep warm cream bg + soft shadows for cards/forms.

**Decision: A, "airy + crisp."** The PDF reads airy + spacious + minimal-shadow + teal-accented; the web should match. Page bg moves from `#FAF7F2` cream to clean white `#FFFFFF`.

### Hex pinning

Two options:

- **A: Pin now from PDF samples.** Sample teal + yellow + supporting neutrals from the seed PDF and write them into the spec; user can override at the SC-015 sign-off gate.
- **B: Defer to a designer pass.** Spec captures intent; hexes pinned during planning.

**Decision: A, "pin now from PDF samples."** Teal `#1FA0A0` (logo disc), accent `#F2C014` (gold rule), table-zebra `#FFF3E5` (cream row). Designer override available at SC-015 if Programa Semilla brand book differs (OQ-001).

### Motion scope

Three options:

- **A: Carry forward, retune.** Keep all four spec-011 wow moments + motion catalog; swap forest-green→teal in motion glow tokens.
- **B: Trim to essentials.** Drop confetti and number tickers; transitions only.
- **C: Defer motion review.**

**Decision: A, "carry forward, retune."** Spec 011 motion catalog tokens (`--motion-instant/fast/base/slow/celebratory`) and spring easings preserved verbatim; reduced-motion contract preserved verbatim. Confetti palette swaps to teal + yellow + neutrals (signing-ceremony wow moment, US4).

### Spec packaging

Three options:

- **A: Single mega-spec.** Mirror 011/017 packaging.
- **B: Two specs sequenced.** Tokens + assets + chrome + signature surfaces first; full sweep + POM rewrites second.
- **C: Foundation-only spec.** Tokens + assets + voice + sponsor chrome; sweep deferred.

**Decision: A, "single mega-spec."** Pre-prod aggressive scope. Same risk profile as spec 011 / 017. One PR, one E2E run, one sign-off gate.

## Mid-Brainstorm Drift Correction

After the spec was drafted and passed the formal `speckit-spex-gates-review-spec` gate (SOUND, all six constitution principles aligned), a verification grep across the actual web codebase revealed the spec's "Forge → Programa Semilla" framing was a generation behind reality. The `_Layout.cshtml` and `_AuthLayout.cshtml` already use *Capital Semilla* (shipped by spec 012 FR-006); the `BRAND-VOICE.md` placeholder *Forge* is documented drift in the brand-voice document, not in the running UI.

Spec was updated to reflect actual state:

- Top "Input" paragraph now describes the spec 011 → spec 012 → spec 018 lineage explicitly.
- US1 "Why this priority" rewords *forest-green "Forge"* to *forest-green "Capital Semilla"*.
- US6 (email templates) acknowledges *Capital Semilla* current sender and *Forge* dangling drift.
- FR-001 says **Capital Semilla → Programa Semilla** in running UI, plus sweep dangling *Forge* in `BRAND-VOICE.md`.
- FR-030 references the dual-name pivot.
- SC-002 grep now targets both *Forge* and *Capital Semilla*.
- Edge case for email templates and BRAND-VOICE drift updated to describe the actual two-name state.

Drift caught before commit, not after. No spec rework cost.

## UX/UI Principles Carried Forward

This brainstorm inherits all principles from spec 008 (status is the spine, etc.) and spec 011 (brand presence is felt not announced; every wow moment earns its motion budget; density per audience). Adds one brand-pivot-specific principle:

11. **Brand consistency is a single-pass invariant.** When the platform's external sponsor identity is canonical (sponsor logos, official wordmark), the web platform must not lag the deliverable artifacts (PDFs) it produces. *Spec mechanism:* SC-002 grep + SC-014 PDF byte-identity check + per-surface E2E brand-presence assertions (FR-033).

## Phased Plan

This brainstorm produces **one spec immediately** (019 Programa Semilla Brand Pivot):

- **Spec 019 (THIS spec, created):** Tokens + sponsor chrome + full sweep + voice rewrite + asset replacement + brand-name pivot in one PR.

No follow-up specs queued from this brainstorm directly. Open Threads below capture deferred items that may surface their own specs later.

## Risks & Anti-Patterns Captured

- **Mega-spec scope creep.** Mitigated by 9 OOS clauses + `BRAND-PIVOT-SWEEP-CHECKLIST.md` deliverable per FR-028.
- **POM rewrite cost overrun across all surfaces.** Saved feedback memory `feedback_ui_quality_over_e2e_stability` accepts the trade-off; planning sequences POM work per surface.
- **WCAG AA regression on yellow accent.** `#F2C014` on white ≈ 1.7:1 — fails AA. Reserved for decorative dividers + filled-badge backgrounds with dark text overlay; linter/grep gate enforces no semantic-meaning yellow (NFR-003).
- **Voice-guide drift survives the sweep.** SC-002 grep targets both *Forge* and *Capital Semilla*; per-string voice review checked off in sweep checklist for every swept view (FR-028 / SC-007).
- **PDF byte-identity break.** SC-014 — regenerated fixture PDF byte-equal to pre-pivot.
- **Schema accidentally touched.** SC-013 — `git diff` on `src/FundingPlatform.Database/` is empty.
- **Asset budget regression.** NFR-002 / SC-011 — ≤ 400 KB gz total; Fraunces removal frees ≈ 35 KB.
- **Reviewer surfaces lose density to sponsor-strip chrome.** Spec 011 FR-060 invariant preserved (reviewer `--space-2`, applicant `--space-4`); flagged as area of potential disagreement in `review_brief.md`.
- **Sponsor logo asset acquisition delay.** OQ-002 + DEP-006 — extract from PDF or request originals; planning-phase task.
- **Brand bikeshedding** stalls planning. SC-015 makes the user sign-off gate explicit and user-owned.
- **Spec 018 PDF / spec 019 web type-stack divergence.** Web drops Fraunces; PDF keeps it. Flagged as area of potential disagreement; designer review at SC-015.

## Decision

A single executable feature was created as `specs/019-programa-semilla-brand/` covering the display-name pivot (Capital Semilla → Programa Semilla, plus sweeping the dangling *Forge* placeholder in `BRAND-VOICE.md`), the full token rewrite, sponsor chrome on `_Layout` + auth pages, component retune (cards / tables / buttons / badges / inputs / sidebar / alerts / modals), full surface sweep across applicant + reviewer + admin + auth, signing-ceremony confetti palette retune, 9-scene illustration retint, BRAND-VOICE.md rewrite, and budgeted E2E POM rewrites. Spec passed `speckit-spex-gates-review-spec` with status SOUND, all 6 constitution principles aligned. Two minor planning-phase guard-rails noted in `REVIEW-SPEC.md` (FR-014 type weight floors; FR-021 yellow-badge dark-text contrast pin). After review, drift correction landed (spec text caught up to actual UI display name) before commit.

User approved the spec and chose to proceed to commit + transition to `/speckit-plan`.

## Open Threads

- **OQ-001:** Exact teal hex — sampled `#1FA0A0` from PDF logo disc; designer override at SC-015 if Programa Semilla brand book differs.
- **OQ-002:** Sponsor logo source — extract from PDF (low fidelity) versus request originals from sponsors. Pinned during planning.
- **OQ-003:** Login hero — large seedling mark only versus a commissioned scene. Defaults to mark-only.
- **OQ-004:** Sidebar collapsed-state breakpoint — Tabler default 992 px versus custom. Pinned during planning.
- **OQ-005:** Confetti palette specifics — teal + yellow only versus include cream + danger-soft. Pinned during planning.
- **OQ-006:** Email signature layout — text-only versus inline seedling mark. Defaults to text-only.
- **OQ-007:** 10 años badge graceful retirement plan when "10 años" stops being current. Future spec.
- **OQ-008:** BRAND-VOICE.md canonical location — repo root, new spec dir, or replace spec 011's in place. Pinned during planning.
- **OQ-009:** Visual-regression tooling — continue Playwright snapshot comparison versus adopt Percy/Chromatic. Defaults to Playwright.
- Spec 018 PDF type-stack (Fraunces serif headings) versus spec 019 web type-stack (sans-only Inter) — designer reconciliation at SC-015 if inconsistency reads as a problem.
- FR-014: pin display + heading weight floors (e.g., ≥ 700 / ≥ 600) during planning so spec is fully testable without depending on SC-015 sign-off.
- FR-021: pin yellow-badge dark-text contrast ratio (e.g., ≥ 4.5:1 against fill) during planning.
- Reviewer surface sponsor-strip chrome density vs. visual real-estate — confirm with reviewer feedback if available.
