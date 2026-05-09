# Brainstorm: PDF Template Lift — Branded Funding Agreement

**Date:** 2026-05-08
**Status:** spec-created
**Spec:** specs/018-pdf-template-lift/

## Problem Framing

The current Funding Agreement PDF is a generic-looking document (gray header bar, plain table, no logos) emitted by `SyncfusionFundingAgreementPdfRenderer` from a thin Razor view. The user has a canonical "Machote" template (`brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`) that is six pages of fully-branded Programa Semilla / Sistema de Banca para el Desarrollo content: header logo, partner-logo footer strip, brand teal palette, Fraunces serif headings, multi-section flow (cover → intro → requested-resources → committee-results → supplier-verification → sworn-declaration with signature box). Goal: pixel-match the seed and structure the codebase so the developer can edit branding (especially logos) without touching code.

Two scope expansions surfaced during brainstorming, both required to make the new layout render meaningful data:

1. **Reviewer-side line code** — the seed's `Variable` / `Detalle` columns show codes like `T1-1`, `T1-2`. These are reviewer-assigned identifiers, not item positions; system must enforce capture during review.
2. **Applicant-side company name** — the seed's cover separates `Empresa solicitante` (commercial entity) from `Representante` (personal legal name). Today the Application stores only the personal name; needs a new required `CompanyName` field on the Application.

A third concern raised by the user: **no hanging strings**. The current template carries `FundingAgreement:Funder:*` config (LegalName, TaxId, Address, Email, Phone) used by the parties block + signature block. The seed has no parties block — funder identity is hardcoded inside the sworn declaration. So all that config + DTO + view-model code is dead and must be removed.

## Approaches Considered

### A: Lift current FA in-place + add fields (chosen)

- Replace `Document.cshtml` + partials wholesale with new seed-shaped partials (`_PdfBrandingHeader`, `_PdfBrandingFooter`, `_CoverPage`, `_RequestedResourcesTable`, `_CommitteeResults`, `_SupplierVerification`, `_SwornDeclaration`).
- Extend view model + DTOs with `CompanyName`, `LineCode`, `ApprovedLines`, `RejectedLinesWithReasons`, `SupplierComplianceRows`, `CommitteeMembers`.
- Add `Item.LineCode` field + reviewer-side validation. Add `Application.CompanyName` field + applicant-side validation. Both non-nullable from day one (no production data).
- Reuse existing `SyncfusionFundingAgreementPdfRenderer` (Blink/Chromium HTML→PDF) unchanged.
- Cleanup pass removes Funder config, parties block, document-reference banner, terms placeholder + CSS classes.

**Pros:** One coherent diff. Existing service entry point + tests scaffold reused. Single PR.
**Cons:** Big single PR (acceptable given no production users + no migration risk).

### B: Parallel "v2" renderer behind a flag

- Build new renderer alongside old; toggle via `FundingAgreement:RendererVersion=v2`.

**Pros:** Safer rollout, A/B compare.
**Cons:** Two renderers + two test surfaces; no production users to protect, so flag is overhead.

### C: Decompose into chrome-only spec + content-restructure spec

- Ship branding chrome over current sections first; restructure to seed sections in a follow-up.

**Pros:** Smallest first PR.
**Cons:** Conflicts with user's "chrome + restructure" depth choice; two specs touching the same files; net more work.

## Decision

**Approach A (in-place lift + add fields + cleanup).** No production data → no migration concerns; non-nullable from day one; legacy fixtures will be regenerated. Single coherent PR is the pragmatic path.

Key clarifications captured during the session:

- **Footer is one composite PNG** (extracted from seed: 1914×312, 58KB, all five partner logos + gold divider baked in). Per-logo edit ergonomics deferred until clean per-logo source files are sourced.
- **Logo edit model** = swap PNG file (developer edit). User explicitly chose hardcoded `<img>` partial over config-list and convention-folder approaches.
- **Comisión evaluadora source** = distinct users who actually took at least one review action on the Application (not assigned reviewers).
- **LineCode** = reviewer-assigned free-text, ≤ 16 chars, unique within Application; system blocks review submission without it.
- **Sworn declaration copy** = hardcoded verbatim from seed pages 5–6; replaces spec 005's R-005 placeholder rule. (One open `[NEEDS CLARIFICATION]` for Legal sign-off.)

Brainstorm-skill formal spec review (`speckit-spex-gates-review-spec`) ran successfully on iteration 2 after applying Important fixes (added Playwright E2E success criteria SC-010/011/012 per Constitution III; tightened SC-001 + SC-009 wording; cited `CLAUDE.md` in cleanup FR; added entity-level validation-placement assumption per Constitution II). Final review status: SOUND, 5/5 across completeness/clarity/implementability/testability.

## Open Threads

- Is the sworn-declaration copy on the seed Legal-approved canonical text, or is the seed itself a draft? (Sole `[NEEDS CLARIFICATION]` in spec; default = canonical.)
- Should `Application.CompanyName` surface on existing list/detail admin/reviewer screens beyond the new applicant form? Decision deferred to plan phase.
- Source of clean per-partner logo files for future per-logo footer edit — out of scope for v1, but blocks any "add 6th logo without recut" workflow until resolved.
- Brand-guideline hex codes — sampled from PDF; if a real brand guideline exists with different values, NFR-001 needs revisit.
- Whether to add automated PDF visual-diff regression harness in a future spec (e.g., `pdfimages` + image-hash comparison against a golden-PDF fixture).

## Brand Assets Extracted

Three unique images pulled from the seed PDF and stored at `src/FundingPlatform.Web/wwwroot/lib/brand/pdf/`:

- `header-seedling.png` (61KB, 1581×1384) — teal seedling logo, top-of-page on every page.
- `footer-partners-strip.png` (58KB, 1914×312) — composite footer with gold dotted divider + Banca para el Desarrollo SBD + CROCUS + nexo + Programa Semilla + 10 años badge.
- `signature-box.png` (1.8KB, 291×138) — empty rounded-rectangle for digital sig stamp; whether to use this PNG or render the box in CSS is a plan-phase decision.
