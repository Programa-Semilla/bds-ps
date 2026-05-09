# Spec Review: Programa Semilla Brand Pivot

**Spec:** specs/019-programa-semilla-brand/spec.md
**Date:** 2026-05-09
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** The spec is implementation-ready. It cleanly inherits from specs 011 / 017 / 018 (whose surfaces, tokens, and assets it modifies in place), pins concrete hex values with stated provenance, encodes 42 testable functional requirements + 15 measurable success criteria, and explicitly preserves the spec 011 motion catalog and reduced-motion contract. Two minor planning-phase guard-rails are noted below; neither blocks moving forward to `/speckit-plan`.

## Completeness: 5/5

### Structure
- All required sections present: Purpose / User Scenarios / Edge Cases / Functional Requirements / Success Criteria / Non-Functional Requirements / Assumptions / Dependencies / Out of Scope / Open Questions.
- Recommended sections (Edge Cases, NFRs, Dependencies, OOS) included with substantive content.
- No placeholder text. No `[NEEDS CLARIFICATION]` markers.

### Coverage
- 6 user stories (4 P1, 2 P2, 1 P3) covering applicant + reviewer + admin + signing ceremony + empty states + email surfaces. Each story has Independent Test + Acceptance Scenarios in Given/When/Then form.
- 16 edge cases enumerated across viewport, motion, contrast, asset failure, library palette, print, high-contrast, cache invalidation, iframe handoff, voice-guide drift.
- 42 functional requirements grouped: brand identity & assets (FR-001..006), design tokens (FR-007..017), component retune (FR-018..026), surface sweep (FR-027..031), testing & verification (FR-032..037), out-of-scope guardrails (FR-038..042).
- 15 success criteria + 5 non-functional requirements.
- 9 explicit assumptions + 9 open questions with stated defaults.

**Issues:** None.

## Clarity: 4.5/5

### Language Quality
- Concrete hex values are pinned with provenance ("sampled from PDF logo disc" / "sampled from PDF gold rule" / "sampled from PDF table cream row") and a sign-off gate (SC-015) makes designer override explicit.
- "MUST" used consistently across functional requirements.
- Each FR maps to a verifiable mechanism (grep, axe pass, snapshot, byte-diff, value comparison).

### Ambiguities Found

1. FR-014 — "target: 700 for display levels, 600 for heading levels — final values pinned by sign-off gate SC-015"
   - Issue: Uses "target" rather than "MUST set to," softening the testability of FR-014 alone.
   - Suggestion (optional): During planning, pin a definite floor (e.g., "MUST set display weight ≥ 700 and heading weight ≥ 600 unless designer override at SC-015").
   - Severity: Minor. The sign-off gate (SC-015) catches the deferral; this is intentional rather than vague.

2. FR-021 — "accent yellow (with dark text overlay because `#F2C014` on white fails AA — see NFR-003)"
   - Issue: The dark-text-overlay requirement is stated qualitatively. NFR-003 enforces the linter/grep gate against semantic-meaning yellow, but doesn't pin the dark-text-overlay contrast ratio.
   - Suggestion (optional): During planning, pin "yellow-bg badge text MUST achieve ≥ 4.5:1 contrast against the badge fill."
   - Severity: Minor. NFR-003 + WCAG AA verification (FR-035 / SC-005) catch this in axe runs.

## Implementability: 5/5

### Plan Generation
- Surface inventory enumerated (FR-027), making the sweep checklist (FR-028) directly generatable.
- Token names map 1:1 to existing `tokens.css` symbols, so the retune is a pinpoint replacement, not a redesign.
- Dependencies on spec 011 / 017 / 018 / 012 are explicit; their invariants (spec 011 motion catalog, spec 017 sidebar testids, spec 018 PDF assets, spec 012 localization compatibility) are preserved verbatim.
- No new managed dependencies; canvas-confetti carve-out preserved.
- Asset acquisition (sponsor SVGs) identified as a planning-phase task (OQ-002 / DEP-006).
- Pre-prod aggressive single-mega-spec scope is consistent with established 011 / 017 packaging philosophy.

**Issues:** None.

## Testability: 5/5

### Verification
- SC-001 / SC-002: grep-scriptable.
- SC-003: per-surface E2E assertion enumerated in FR-033.
- SC-004: audit script extends spec 011's existing tooling.
- SC-005: axe-playwright run enumerated against 5 representative surfaces.
- SC-006: visual snapshot diff for each of the 4 spec-011 wow moments.
- SC-007 / SC-008: manual checklist with explicit columns.
- SC-009: full E2E suite (delivery bar — aligned with saved memory).
- SC-010: dedicated reduced-motion Playwright test.
- SC-011: gz wire-weight measurement.
- SC-012: visual-regression snapshot diff on PR.
- SC-013: `git diff` is empty.
- SC-014: byte-equal PDF fixture comparison.
- SC-015: user sign-off gate explicit.

Every requirement has a clear verification path. No subjective criteria.

## Constitution Alignment

All 6 principles aligned:

- **I. Clean Architecture** — Spec scopes web-layer-only changes (tokens.css, _Layout, Razor partials, brand assets, BRAND-VOICE.md, email templates). No Domain / Application / Infrastructure changes. Dependency rule preserved by construction.
- **II. Rich Domain Model** — N/A. No domain or behavior changes.
- **III. End-to-End Testing (NON-NEGOTIABLE)** — FR-032 budgets POM rewrites, FR-033 enumerates per-surface brand-presence assertions, FR-034 preserves the reduced-motion test, FR-035 runs axe-playwright on ≥ 5 surfaces, FR-036 commits ≥ 4 visual regression snapshots, SC-009 makes the full E2E suite the delivery bar.
- **IV. Schema-First Database Management** — FR-038 / SC-013 explicitly forbid schema changes; `git diff main -- src/FundingPlatform.Database/` MUST be empty.
- **V. Specification-Driven Development** — This document IS the spec; user stories are independently testable; planning phase will produce plan.md / tasks.md.
- **VI. Simplicity and Progressive Complexity** — YAGNI honored: public marketing OOS, multi-tenant brand swapping OOS, email-embedded sponsor logos OOS, net-new wow moments OOS. The single-mega-spec packaging is justified by the pre-prod context and matches the established 011 / 017 precedent rather than being net-new complexity.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
None.

### Important (Should Fix)
None.

### Optional (Nice to Have)
- During `/speckit-plan`, pin the FR-014 type weights as concrete floors (e.g., "≥ 700 / ≥ 600") and the FR-021 yellow-badge dark-text contrast (e.g., "≥ 4.5:1 against fill") so the requirements become directly testable in unit/axe tests rather than dependent on the SC-015 sign-off gate.
- Consider pinning the `BRAND-VOICE.md` canonical location (OQ-008) in the plan so it's resolved before any sweep work begins.

## Conclusion

Spec is sound and ready for implementation planning. Inheritance from specs 011 / 017 / 018 is explicit, hex values are concretely sourced from the PDF reference, every requirement has a testable verification mechanism, and the constitution is fully aligned. The two optional refinements above are planning-phase polish, not gating issues.

**Ready for implementation:** Yes (after planning).

**Next steps:**
- Optional: `/speckit-clarify` to surface any underspecified areas surfaced by the clarification skill.
- `/speckit-plan` to produce plan.md, research.md, data-model.md (N/A — no schema), and supporting artifacts.
- `/speckit-tasks` to produce dependency-ordered tasks.md.
