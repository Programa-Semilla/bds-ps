# Spec Review: PDF Template Lift — Branded Funding Agreement

**Spec:** specs/018-pdf-template-lift/spec.md
**Date:** 2026-05-08
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND (minor improvements recommended before plan)

**Summary:** Spec is well-structured, plannable, and traceable. Three user stories properly prioritized and independently testable. 24 FRs are concrete and testable. Nine SCs are measurable. One constitution gap (E2E success criteria absent) and a handful of small wording improvements would lift it from "sound" to "exemplary."

## Completeness: 5/5

### Structure
- All mandatory sections present (User Scenarios, Requirements, Success Criteria).
- Recommended sections included (Edge Cases, Assumptions, Dependencies, Out of Scope, Open Clarifications).
- One placeholder remains (`[NEEDS CLARIFICATION]` for sworn-declaration legal status), explicitly tracked under Open Clarifications with a documented default.

### Coverage
- All 24 FRs defined with MUST language.
- 9 edge cases identified.
- 9 measurable SCs defined.
- All 3 user stories carry an Independent Test description and Given/When/Then acceptance scenarios.

**Issues:** None.

## Clarity: 4/5

### Language Quality
- Requirements use MUST/MUST NOT consistently.
- Concrete file paths (`wwwroot/lib/brand/pdf/header-seedling.png`), entity names, and column lists are specified.
- Numeric bounds stated (60pt, 50pt, 32pt, ≤16 chars, ≤200 chars, 20mm, 18mm).

**Ambiguities Found:**

1. SC-001 — "*A trained reviewer comparing the generated PDF…*"
   - Issue: "trained reviewer" is undefined. Whose training? What signal?
   - Suggestion: "*A developer or designer with the seed template open side-by-side identifies…*"

2. SC-009 — "*…on the standard development host.*"
   - Issue: "standard development host" is undefined; could be a workstation, CI runner, or Aspire dev container.
   - Suggestion: "*…on the Aspire dev environment baseline (8GB RAM, 4 vCPU container).*" or pin to CI-runner spec.

3. FR-001/FR-002/FR-005 size language uses "approximately" (60pt, 50pt, 32pt).
   - Issue: pairs with SC-001's ±5pt tolerance, so it is testable, but a reader skimming FRs alone might not connect the two.
   - Suggestion: add a parenthetical "(SC-001 governs tolerance)" on the first occurrence, or hoist tolerance into NFRs.

4. FR-019 — "*…and project documentation;*"
   - Issue: "project documentation" is plural and unscoped.
   - Suggestion: name `CLAUDE.md` config table explicitly (already on the radar from brainstorming notes); cite it in the FR.

## Implementability: 5/5

### Plan Generation
- Spec describes WHAT, file/asset locations are concrete, entities and column names are spelled out.
- Plan-phase decisions called out explicitly: signature box (PNG vs CSS), CompanyName surfacing on admin/reviewer screens.
- Dependencies on six prior specs (002, 005, 006, 012, 013, 015) enumerated with role.
- Brand assets already extracted and saved on disk; no upstream blocker.

**Issues:** None.

## Testability: 4/5

### Verification
- Each FR is verifiable through generated-PDF inspection, integration test, or manual swap.
- SCs map cleanly to FRs except for SC-009 (perf) and SC-001 (visual diff), which need the wording tightening above.

**Issues:**

- SC-001 manual side-by-side is fundamentally subjective; consider supplementing with a structured pixel-diff tool (out of scope per spec, but flag for follow-up if regressions become a problem).
- SC-002 "verbatim reproduction modulo content driven by genuine database values" needs a concrete fixture to evaluate; planning should produce a deterministic seed dataset matching the seed PDF.

## Constitution Alignment

Constitution v1.0.0 (FundingPlatform) reviewed.

- **I. Clean Architecture** — ✓ aligned. Domain gets new fields on `Application` + `Item`; rendering stays in Web/Infrastructure.
- **II. Rich Domain Model** — ⚠ partial. Spec mandates the validation rules but does not specify where they live (entity vs controller). Constitution requires invariants on entities (e.g., `Application.SetCompanyName(...)`, `Item.AssignLineCode(...)` as behavior methods). **Recommend: clarify in plan-phase.**
- **III. E2E NON-NEGOTIABLE** — ⚠ **GAP**. SC-003/SC-004/SC-008 reference "integration tests against a real database" but no SC mandates Playwright E2E coverage of US1/US2/US3 user flows through the browser. Constitution III says E2E is the primary quality gate and "every user story MUST have corresponding E2E tests covering the golden path and key error scenarios." **Recommend: add E2E SCs (see Important section below).**
- **IV. Schema-First (dacpac)** — ✓ implicit. Adding `Application.CompanyName` + `Item.LineCode` requires dacpac edits, not EF migrations. Spec correctly defers schema mechanics to planning, but plan-phase MUST honor dacpac mandate.
- **V. Specification-Driven Development** — ✓ aligned. Spec exists, prioritized USs, acceptance scenarios.
- **VI. Simplicity / YAGNI** — ✓ aligned. Out-of-Scope section explicitly defers admin UI, multi-tract data model, per-logo edit, etc.

**Violations:** One important gap (III: E2E coverage absent from SCs). Easy fix.

## Recommendations

### Critical (Must Fix Before Implementation)

None.

### Important (Should Fix Before Plan)

- [ ] **Add E2E success criteria** to honor Constitution III. Three additions, one per US:
  - **SC-010**: A Playwright E2E test drives the funder operator from the Application detail page through PDF generation and download, then asserts the downloaded PDF contains the expected section headings ("Recursos solicitados", "Resultados comisión", "Información empresas proveedoras", "DECLARO BAJO LA FE DEL JURAMENTO") in its text layer.
  - **SC-011**: A Playwright E2E test drives a reviewer through the per-item review form, attempts submission without a line code (asserts validation error), then submits with a code (asserts persistence + advance-to-next-item).
  - **SC-012**: A Playwright E2E test drives an applicant through the application form, attempts submission without a company name (asserts validation error), then submits with a name (asserts persistence + cover-page rendering on the generated PDF).

- [ ] **Tighten SC-001 wording** ("trained reviewer" → "developer or designer with the seed template open side-by-side") and **SC-009** ("standard development host" → name the actual baseline).

### Optional (Nice to Have)

- [ ] **Cite `CLAUDE.md` explicitly in FR-019** so the cleanup target is unambiguous.
- [ ] **Note on FR-001** that ±5pt tolerance is governed by SC-001, to make the implicit link explicit.
- [ ] **Plan-phase hint: validation placement.** Add a one-line note that LineCode + CompanyName invariants belong on the entities per Constitution II; downstream plan should call this out in its Constitution Check.

## Conclusion

The spec is sound enough to proceed to planning, but resolving the E2E gap before `/speckit-plan` runs will save a round-trip during plan review. The wording tightenings are nice-to-have and could be folded into the same edit pass.

**Ready for implementation:** Yes, after the Important fixes (E2E SCs + two wording fixes).

**Next steps:**

1. Add SC-010/011/012 (E2E coverage per constitution III).
2. Tighten SC-001 and SC-009 wording.
3. Optionally apply Optional improvements.
4. Hand spec to user for the user-review gate.
5. Proceed to `/speckit-clarify` (to resolve the open `[NEEDS CLARIFICATION]` if Legal can be reached) or directly to `/speckit-plan`.

---

## Iteration 2 (2026-05-08)

All Important and two Optional items applied:

- ✓ SC-010 / SC-011 / SC-012 added (one Playwright E2E per US per Constitution III).
- ✓ SC-001 wording tightened (replaced "trained reviewer" with "developer or designer with the seed template open side-by-side"; added explicit pointer that ±5pt tolerance governs FR sizing language).
- ✓ SC-009 wording tightened (named the baseline as "Aspire dev environment" + workstation + AppHost stack; added perf-baseline-script callout if applicable).
- ✓ FR-019 now cites `CLAUDE.md` config-knobs table by name.
- ✓ Assumptions section gained a validation-placement note (Constitution II): invariants on entities, not controllers/services. Plan-phase must honor this in its Constitution Check.

**Updated Status:** SOUND — all Important items resolved.
**Scores:** Completeness 5/5 · Clarity 5/5 · Implementability 5/5 · Testability 5/5
**Constitution alignment:** All six principles aligned (was: gap on III).

**Ready for implementation:** Yes. Proceed to user-review gate, then `/speckit-clarify` (optional, to resolve the lone `[NEEDS CLARIFICATION]` on sworn-declaration legal canonicity) or directly to `/speckit-plan`.
