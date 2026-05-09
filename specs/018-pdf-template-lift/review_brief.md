# Review Brief: PDF Template Lift — Branded Funding Agreement

**Spec:** specs/018-pdf-template-lift/spec.md
**Generated:** 2026-05-08

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Replaces the current generic "Convenio de Financiamiento" PDF with a fully-branded, multi-section "Informe de evaluación de solicitudes de desembolso" document that pixel-matches the canonical seed template at `brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`. New layout has six sections (cover with applicant + commission, intro, requested-resources table, committee-results section with approved + rejected subtables, supplier-verification table, sworn-declaration page with signature box). Adds two enabling data captures (reviewer-side `Item.LineCode`, applicant-side `Application.CompanyName`) without which the new tables/cover would be blank. Cleans up the legacy generic-template artifacts (Funder config, parties block, document-reference banner, terms placeholder).

## Scope Boundaries

- **In scope:** Funding Agreement PDF restructure + branding lift; reviewer per-item line-code capture; applicant company-name capture; cleanup of dead Funder config and obsolete partials/CSS; replacement of the visible legacy "MARCADOR DE POSICIÓN" banner with the canonical sworn declaration.
- **Out of scope:** Admin UI for logo management; multi-tract data model; localization beyond es-CR; branded PDF for any other document type; database-backed legal copy; visual differential testing automation; backfilling legacy data; sourcing five separate per-partner logo files; broader applicant- or reviewer-form revisions.
- **Why these boundaries:** Pixel-perfect match to a single canonical seed is the contract; the two new data fields are minimum-viable inputs to make the new tables render correctly; cleanup removes hangover from a generic template that the new doc replaces in full.

## Critical Decisions

### Footer is a single composite PNG (not five separate logo images)

- **Choice:** `footer-partners-strip.png` is one composite image with all five partner logos + gold divider baked in.
- **Trade-off:** Pixel-perfect match in v1 with zero per-logo sourcing work; cost is that adding/removing/reordering individual logos requires externally re-cutting the strip in an image editor.
- **Feedback:** Acceptable v1 trade-off?

### LineCode is reviewer-assigned free-text, not auto-numbered

- **Choice:** Reviewer types e.g. `T1-1`, `T1-2`, `T1-3` per item during review; system enforces non-blank + uniqueness within Application; max 16 chars.
- **Trade-off:** Total reviewer flexibility (groupings, tract conventions) at the cost of one new mandatory input per item review.
- **Feedback:** Is per-Application uniqueness scope correct, or should codes be globally unique / scoped differently?

### Comisión evaluadora = action-takers, not assigned reviewers

- **Choice:** Cover-page committee list is the distinct set of users who actually performed at least one review action on the Application; assigned-but-no-action reviewers are excluded.
- **Trade-off:** Reflects who actually took accountability; but an assigned reviewer who delegated or no-showed is invisible to the funder reading the PDF.
- **Feedback:** Is action-history truly the right source vs. official assignment?

### Sworn declaration copy is hardcoded verbatim from the seed

- **Choice:** Spanish text on seed pages 5–6 (preamble + PRIMERO–QUINTO + closing) treated as Legal-approved canonical, hardcoded in the Razor partial. Replaces the prior visible "MARCADOR DE POSICIÓN — NO ES VERSIÓN FINAL" placeholder banner from spec 005 R-005.
- **Trade-off:** No more dev/test-vs-prod ambiguity in the placeholder, but spec 005 R-005's safety rule is retired for this document.
- **Feedback:** **CRITICAL** — confirm with Legal that the seed copy is the canonical legal text. If it is itself a draft, the placeholder banner stays. (This is the lone `[NEEDS CLARIFICATION]` in the spec.)

### No production data → no migration shim

- **Choice:** Both `Application.CompanyName` and `Item.LineCode` are non-nullable from day one; legacy fixture/seed data is regenerated to match. No nullable shim, no backfill script.
- **Trade-off:** Cleaner code; depends entirely on the "no production users yet" claim being correct.
- **Feedback:** Confirm no production users.

## Areas of Potential Disagreement

### Constitution III mandate met by Playwright E2E only

- **Decision:** Each US has a dedicated Playwright E2E SC (SC-010/011/012) covering golden + key-error paths.
- **Why this might be controversial:** Some teams supplement with snapshot/visual regression tests for the PDF text layer; spec deliberately defers visual diffing automation.
- **Alternative view:** Add a PDF text-layer snapshot test or a `pdfimages`-based regression harness now to catch silent formatting drift.
- **Seeking input on:** Is Playwright + manual side-by-side enough, or do we want automated visual regression?

### Single-renderer cutover (no v1/v2 toggle)

- **Decision:** Old `Document.cshtml` + partials replaced wholesale; no feature flag.
- **Why this might be controversial:** Risk-averse teams might want a flag to roll back if something breaks downstream.
- **Alternative view:** Ship behind `FundingAgreement:RendererVersion=v2` toggle for safety.
- **Seeking input on:** Is the no-toggle path acceptable given there are no production users yet?

### "Empresa solicitante" is a new top-level Application field, not derived from Applicant

- **Decision:** New required `Application.CompanyName` distinct from `Applicant.LegalName`.
- **Why this might be controversial:** Could be modeled as a property of `Applicant` instead of `Application`.
- **Alternative view:** `Applicant.CompanyName` makes more sense if an applicant always represents the same company across multiple Applications.
- **Seeking input on:** One applicant per company forever, or could one person represent multiple companies?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| New PDF document title | "Informe de evaluación de solicitudes de desembolso" | Replaces "Convenio de Financiamiento" everywhere in user-facing PDF copy. |
| New Application field | `CompanyName` | Maps to "Empresa solicitante" on cover. |
| New Item field | `LineCode` | Maps to "Variable" / "Detalle" columns. |
| Header asset | `wwwroot/lib/brand/pdf/header-seedling.png` | Top-of-page brand logo. |
| Footer asset | `wwwroot/lib/brand/pdf/footer-partners-strip.png` | Composite five-logo strip. |
| Signature-box asset | `wwwroot/lib/brand/pdf/signature-box.png` | Empty rounded-rectangle placeholder for digital sig stamp. |
| New error code (reviewer) | `LineCodeRequired` | Validation error when reviewer submits without code. |
| New error code (reviewer) | `LineCodeDuplicate` | Validation error when code is reused within Application. |
| New error code (applicant) | `CompanyNameRequired` | Validation error when applicant submits without company name. |

## Open Questions

- [ ] Is the sworn-declaration copy on the seed Legal-approved canonical text or a draft? (Sole `[NEEDS CLARIFICATION]` in the spec.)
- [ ] Should `Application.CompanyName` surface on existing list/detail admin/reviewer screens beyond the new applicant form? (Defaulted to "yes for consistency, decide in plan phase".)
- [ ] Will brand-guideline color values supersede the spec's PDF-sampled hex values?
- [ ] Per-logo footer source files — do they exist somewhere for a future spec, or do we need to commission them?

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Sworn-declaration copy turns out to be Legal-draft, not canonical | High (legal exposure if signed) | Open `[NEEDS CLARIFICATION]` + default to keeping the placeholder banner if Legal pushes back. |
| Composite footer needs a logo update mid-project | Medium (manual recut required) | Document the recut workflow + asset dimensions in plan phase; defer per-logo edit to a future spec. |
| Reviewer-form changes break existing review E2E suite | Medium (test rewrite cost) | Memory note "UI quality > E2E stability" applies; plan-phase tasks must include test rewrite. |
| Schema changes need dacpac edits, not EF migrations | Medium (slowdown if missed) | Constitution IV mandates dacpac; plan-phase Constitution Check must call this out explicitly. |
| Visual fidelity regresses silently between Blink versions | Low | Manual side-by-side per SC-001; consider automated visual diff in a future spec if regressions become a problem. |

---
*Share with reviewers before implementation.*
