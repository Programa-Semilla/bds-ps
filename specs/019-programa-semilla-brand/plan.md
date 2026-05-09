# Implementation Plan: Programa Semilla Brand Pivot

**Branch**: `019-programa-semilla-brand` | **Date**: 2026-05-09 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/019-programa-semilla-brand/spec.md`

## Summary

Re-anchor the FundingPlatform web identity from the spec-011 placeholder *Forge / Capital Semilla* (forest-green `#2E5E4E` + amber `#D98A1B` + warm cream `#FAF7F2` + Fraunces-serif display) to the canonical sponsor-program identity *Programa Semilla* under Sistema de Banca para el Desarrollo (teal `#1FA0A0` + yellow `#F2C014` + white airy surfaces + sans-only Inter), matching the spec-018 Funding Agreement PDF. Single mega-spec sweep across applicant / reviewer / admin / auth surfaces and shared `_Layout` chrome. `tokens.css` is rewritten in place; partials are retuned (buttons, tables, cards, badges, inputs, alerts, modals); sponsor partner-logo strip lands on `_Layout` footer + auth pages; sidebar header gains the seedling mark + Programa Semilla wordmark; all 9 empty-state SVG illustrations are regenerated with teal strokes; confetti palette swaps to teal + yellow + neutrals; favicons and brand assets are replaced; email-template sender display + signature block update text-only. E2E POM rewrites are budgeted; visual snapshots refresh on at least 4 surfaces; dedicated reduced-motion test stays green; `axe-playwright` AA verified on at least 5 surfaces. Schema and PDF generation pipeline untouched (FR-038 / FR-039); Tabler.io vendored bundle not upgraded (FR-042); localization layer (spec 012) preserved (FR-041); code namespaces / project names / config keys remain `FundingPlatform`.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, Tabler.io (vendored, version unchanged), Inter + JetBrains Mono (vendored — Fraunces removed), `canvas-confetti` (vendored, palette config retuned), Syncfusion HtmlToPdfConverter (untouched — spec 018 invariant)
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`) — schema delta zero (FR-038 / SC-013)
**Testing**: xUnit (Unit + Integration), Playwright (E2E) on AspireFixture; `axe-playwright` for WCAG AA contrast pass; Playwright screenshot comparison for visual regression
**Target Platform**: Linux server (Aspire-orchestrated); ASP.NET MVC server-side rendering; last 2 evergreen browsers + iOS Safari (NFR-004)
**Project Type**: Web (Aspire-orchestrated stack — AppHost → Web → SQL Server); single-tenant
**Performance Goals**: LCP and TBT on applicant home and reviewer queue MUST NOT regress versus the spec-011 baseline `specs/011-warm-modern-facelift/perf-baseline.json` (NFR-001). New baseline captured + committed under this spec dir.
**Constraints**: Total brand-related asset wire weight ≤ 400 KB gz (NFR-002 / FR-037 / SC-011); WCAG AA on every swept surface (NFR-003); yellow accent `#F2C014` reserved for decorative-only use (linter/grep gate); no CDN — all assets vendored; reduced-motion contract verbatim from spec 011 (FR-017).
**Scale/Scope**: ~30 swept surfaces across applicant / reviewer / admin / auth / shared chrome; 9-scene empty-state illustration set; 4 spec-011 wow moments re-walked; 4+ visual-regression snapshots refreshed.

## Constitution Check

Constitution v1.0.0 evaluated against this plan:

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Clean Architecture** | ✓ pass | Pure presentation sweep. No domain, application, or infrastructure changes. Web-layer view files, view models, partials, `wwwroot/lib/brand/**`, and email templates only. Dependency direction unchanged. |
| **II. Rich Domain Model** | ✓ pass | No entity, aggregate, or invariant changes. |
| **III. E2E NON-NEGOTIABLE** | ✓ pass | FR-032 / FR-033 / FR-034 / FR-035 / FR-036 cover sweep, brand presence, reduced motion, contrast, and visual regression. Each user story (US1–US6) is independently testable per its "Independent Test" clause. Delivery bar (memory: green local E2E run) gates merge. |
| **IV. Schema-First (dacpac)** | ✓ pass | FR-038 / SC-013: `git diff main -- src/FundingPlatform.Database/` MUST be empty after this spec lands. No `.sql` edits. No EF migrations. |
| **V. Spec-Driven** | ✓ pass | Spec → plan → tasks workflow honored. This plan precedes tasks/implement. |
| **VI. Simplicity / YAGNI** | ✓ pass | Tokens cascade through existing partials (no parallel renderer). One file (`tokens.css`) holds raw hex (FR-009 invariant from spec 011). Single sponsor-strip partial, single confetti palette constant, single voice-guide file. No new managed dependencies. Single mega-spec is justified by scope coupling: palette + name + voice + chrome must land together to retire the divergence (rationale embedded in spec). |

**Violations:** None. **Complexity Tracking** below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/019-programa-semilla-brand/
├── plan.md                              # This file
├── research.md                          # Phase 0 output (resolves OQ-001..OQ-009)
├── data-model.md                        # Phase 1 output (no schema delta — sentinel)
├── quickstart.md                        # Phase 1 output
├── BRAND-PIVOT-SWEEP-CHECKLIST.md       # Per-surface verification matrix (FR-028 / SC-008)
├── perf-baseline.json                   # New post-pivot baseline (NFR-001)
├── spec.md                              # Existing
├── REVIEW-SPEC.md                       # Existing
├── review_brief.md                      # Existing
└── tasks.md                             # Phase 2 output (/speckit-tasks — NOT created here)
```

> **No `contracts/` directory.** This spec exposes no new external interface (no API, no CLI, no contract-style consumer). Brand identity is internal presentation; the spec-018 PDF interface and the spec-012 localization resource keys are unchanged. Per the plan template: *"Skip if project is purely internal."*

### Source Code (repository root)

```text
src/
├── FundingPlatform.Web/
│   ├── wwwroot/
│   │   ├── favicon.ico                                            # REPLACE — seedling mark variant (FR-005)
│   │   ├── lib/
│   │   │   ├── brand/
│   │   │   │   ├── mark.svg                                       # REPLACE — teal seedling (FR-002)
│   │   │   │   ├── wordmark.svg                                   # REPLACE — Programa Semilla wordmark (FR-002)
│   │   │   │   ├── seal.svg                                       # REPLACE — teal seal variant (FR-002)
│   │   │   │   ├── favicons/                                      # REPLACE all sized PWA favicons (FR-005)
│   │   │   │   ├── illustrations/                                 # REGENERATE all 9 SVGs with teal strokes (FR-026)
│   │   │   │   ├── sponsors/                                      # NEW — partner logos: sbd.svg, crocus.svg, nexo.svg, programa-semilla.svg, 10-anos.svg
│   │   │   │   └── pdf/                                           # UNCHANGED (FR-002 / FR-039 invariant)
│   │   │   ├── fonts/
│   │   │   │   ├── inter/                                         # KEEP (vendored)
│   │   │   │   ├── jetbrains-mono/                                # KEEP (vendored)
│   │   │   │   └── fraunces/                                      # DELETE — spec 011 carve-out retired (FR-013)
│   │   │   └── canvas-confetti/                                   # KEEP (vendored, palette read from tokens / single JS module)
│   │   └── css/
│   │       └── tokens.css                                         # REWRITE — only file holding raw hex (FR-007..FR-016 surfaces token deltas)
│   ├── Views/
│   │   ├── Shared/
│   │   │   ├── _Layout.cshtml                                     # NEW chrome: brand sidebar header, sponsor footer strip, cache-bust query on tokens.css
│   │   │   ├── _SponsorStrip.cshtml                               # NEW partial — sponsor logos + 10 años badge (FR-003)
│   │   │   ├── _BrandSidebarHeader.cshtml                         # NEW partial — seedling mark + Programa Semilla wordmark + collapsed tooltip (FR-025)
│   │   │   ├── _Buttons / _Tables / _Cards / _Badges / _Inputs / _Alerts / _Modals partials  # RETUNE (FR-018..FR-024)
│   │   │   └── _StatusPill.cshtml / _EmptyState.cshtml / _ActionBar.cshtml / _ConfirmDialog.cshtml / _KpiTile.cshtml  # RETUNE token references (no behavior change)
│   │   ├── Account/                                               # Login / Register / ResetPassword / ConfirmEmail — hero left-rail + sponsor strip (FR-004)
│   │   ├── Application/                                           # home, dashboard, journey, appeal, signing — RE-WALK at new bar (FR-027 / FR-029)
│   │   ├── Review/                                                # queue, detail, signing inbox, history — RE-WALK preserving --space-2 density (FR-031)
│   │   ├── Admin/ + AdminUsers/ + AdminGroups/ + AdminCurrencies/ + AdminExchangeRates/
│   │   │   + AdminLegacyQuotations/ + AdminSuppliers/ + AdminReports/ + AdminAudit/         # RE-WALK 10 admin surfaces from spec 017
│   │   └── FundingAgreement/                                      # UNCHANGED (FR-039)
│   └── EmailTemplates/                                            # confirm + reset templates — sender display + signature block text-only update (FR-006 / NFR-005)
└── FundingPlatform.Database/                                      # UNCHANGED (FR-038 / SC-013)

specs/011-warm-modern-facelift/
└── BRAND-VOICE.md                                                 # ARCHIVE in place (mark as historical) — canonical file moves per OQ-008 (research R8)

scripts/
├── asset-budget-check.sh                                          # EXISTING — re-asserts < 400 KB gz post-pivot
├── tokens-audit.sh                                                # EXISTING — re-asserts tokens.css is the only raw-hex file (extends spec 011 tooling)
└── brand-grep-gate.sh                                             # NEW — fails build if "Forge" / "Capital Semilla" / legacy hex / yellow-in-semantic-context appear (SC-001 / SC-002 / NFR-003)

tests/
├── FundingPlatform.Tests.Unit/                                    # No new unit tests required (no domain delta)
├── FundingPlatform.Tests.Integration/                             # Email-template sender-name assertion if a fixture is wired (otherwise covered by E2E SMTP capture)
└── FundingPlatform.Tests.E2E/
    ├── Brand/
    │   ├── BrandPresenceTests.cs                                  # NEW — per-surface assertions (FR-033)
    │   ├── ReducedMotionTests.cs                                  # KEEP green (FR-034)
    │   ├── AxeContrastTests.cs                                    # NEW — 5 surfaces (FR-035 / SC-005)
    │   ├── VisualRegressionTests.cs                               # REFRESH baselines (FR-036 / SC-012)
    │   ├── SigningCeremonyConfettiTests.cs                        # RETUNE palette assertion (US4)
    │   └── EmailTemplateSenderTests.cs                            # NEW — SMTP capture (US6)
    ├── Pages/                                                     # POMs rewritten across all swept surfaces (FR-032)
    └── (existing per-spec test fixtures — POM updates only)

CLAUDE.md                                                          # No structural change. Mention that the brand display name is "Programa Semilla" (running text); confirm config-knob keys remain FundingPlatform-prefixed.
brainstorm/17-programa-semilla-brand.md                             # Existing input; not edited by this spec.
```

**Structure Decision**: Single-solution four-layer Clean Architecture stack (Domain / Application / Infrastructure / Web) plus Aspire orchestration and a dacpac for schema. The pivot is contained to `src/FundingPlatform.Web/` (views, view-shared partials, vendored brand assets, email templates) and the audit/grep tooling under `scripts/`. Tokens cascade through partials, so the only CSS file edited at the file-level is `wwwroot/css/tokens.css`; partials read tokens via existing `var(--*)` references. The empty-state SVG set under `wwwroot/lib/brand/illustrations/` is regenerated in place. Brand-related E2E tests live in a new `tests/FundingPlatform.Tests.E2E/Brand/` namespace; existing per-spec POMs are rewritten in place to address the FR-032 budget without forking new test directories. Schema is untouched (`src/FundingPlatform.Database/` git-clean).

## Complexity Tracking

> No Constitution Check violations to justify. This section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
