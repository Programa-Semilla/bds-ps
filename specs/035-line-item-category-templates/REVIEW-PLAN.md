# Review Guide: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-12

---

## What This Spec Does

Today an applicant describes each line of a funding request with one free-text "technical specifications" box, declares a single impact for the whole application, and re-types the vendor and re-uploads the quote PDF on every line. This feature restructures the line item: each **category** carries an admin-defined set of fields that appear when the applicant picks it (replacing the free-text box), **impact** becomes a per-line choice, and a multi-product vendor quote is captured as one line per product while the vendor + uploaded file are entered once and reused. It also removes the now-obsolete application-level impact and the per-process impact-template gating, leaving no dead code.

**In scope:** admin category-field editor; category-driven dynamic line fields; per-item impact (any active template, ungated); quotation reuse within one application; rendering the new per-item data on every surface (applicant, reviewer, funding-agreement PDF, AI comparison); full teardown of application-level impact + Plantilla impact-gating.

**Out of scope:** data migration (greenfield flow); cross-application quotation reuse; new field types (file/dropdown/conditional) or custom per-field validation; a shared category-template catalog; any change to the min-quotations rule or required-field flags.

## Bigger Picture

This reverses a deliberate decision from spec 021, which moved impact *up* from the item to the application. Reviewers who remember that decision should weigh in: [research D2/D3](research.md#d2-impact-relocation-re-key-impactparametervalues-from-application-to-item) explains why we're moving it back down and dropping the per-process gating entirely. The category-field machinery deliberately clones the existing impact-template pattern (entity → parameters → EAV values → index-based admin form), so the surface area is large but the *shapes* are all familiar. The single riskiest structural change is the Plantilla teardown ([research D4](research.md#d4-plantilla-teardown-surgical-removal-of-impact-template-gating-keep-the-rest)): the Plantilla keeps two jobs (min-quotations, required-field flags) but loses its impact-template link — and its `AssignTo` method currently *requires* ≥1 impact template, so that guard must go or Process assignment breaks.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec Overview](spec.md#overview) and [User Story 2](spec.md#user-story-2---applicant-captures-a-line-item-via-category-fields-and-per-item-impact-priority-p1) for the core flow, then [research D8](research.md#d8-applicant-flow-restructure-fold-impact-into-the-item-form-remove-the-standalone-impact-step). As you read:

- The plan folds impact selection *into the item form* and deletes the standalone per-application impact step. Is one canonical item form (category → fields → product → impact → quotation) the right UX, or do you prefer impact to stay a separate step? ([research D8](research.md#d8-applicant-flow-restructure-fold-impact-into-the-item-form-remove-the-standalone-impact-step))
- Category owns its fields 1:1 — no reusable shared template. If two categories genuinely need the same fields, admins define them twice. Acceptable, or will that bite? ([research D1](research.md#d1-category-field-model-re-key-the-impact-template-pattern-owned-by-the-category))

### Key decisions that need your eyes (12 min)

**Impact becomes ungated** ([spec FR-006](spec.md#functional-requirements), [research D3](research.md#d3-impact-gating-removed-any-active-impact-template-is-selectable-per-item))
Previously the Plantilla restricted which impact templates a process could offer; now any active template is pickable per item. This drops a governance lever. Is per-process impact-template restriction something stakeholders actually use, or safe to remove?

**Quotation reuse semantics** ([spec User Story 3](spec.md#user-story-3---applicant-reuses-a-multi-product-vendor-quotation-across-line-items-priority-p2), [research D5](research.md#d5-quotation-reuse-no-schema-change-reference-counted-blob-retention))
Reuse shares the vendor + uploaded document but each line keeps its own price. The shared document is retained until the *last* referencing quotation is removed (reference counting at two delete sites). Is per-line price the correct model for your real quotes, and is the reference-counted retention the behavior you'd expect when an applicant deletes the line that originally uploaded the file?

**AI comparison context — the one place I refined a requirement** ([spec FR-009](spec.md#functional-requirements), [research D6](research.md#d6-ai-quote-comparison-context-fr-009-include-category-description-exclude-impact--refinement-flagged))
FR-009 literally says the AI quote-comparison context should show category values *and* per-item impact. I recommend including **category values** (they describe what's being quoted — useful to the model) but **excluding impact** (it's applicant-evaluation metadata irrelevant to comparing supplier quotes), and scrubbing all new free-text for PII first. Is that refinement acceptable, or do you want strict FR-009 (impact in the AI context too)?

**Funding-agreement PDF gains content** ([spec FR-009](spec.md#functional-requirements), [research D9](research.md#d9-display-surfaces-fr-009-convert-four-render-surfaces-from-per-application-to-per-item-add-two))
Spec 018 deliberately kept the PDF body minimal. This feature adds a per-line category-fields + impact block to it. Is expanding the legal PDF body the right call, or should the PDF stay minimal and the new data live only in the app?

### Areas where I'm less certain (5 min)

- [research D6](research.md#d6-ai-quote-comparison-context-fr-009-include-category-description-exclude-impact--refinement-flagged): my read of FR-009's "AI quote-comparison context" as a place to *exclude* impact is a judgment call. If you read FR-009 strictly, T060 changes.
- [tasks Phase 2](tasks.md#phase-2-foundational-blocking-prerequisites): I treated the whole impact relocation + TechSpecs removal + Plantilla teardown as one atomic, build-breaking block. That makes the Foundational phase large (T002–T030) and the per-story phases lighter than usual. If you'd prefer thinner foundational scope, the natural seam is whether the *view* compile-fixes belong here or in US4 — I put DTO/projection plumbing here and Razor rendering in US4, but that line is debatable.
- The "category fields edited after use" edge case ([spec Edge Cases](spec.md#edge-cases)) relies on `Application.Validate` running only at submit so submitted apps keep their stored values. I believe that's how the current submit path works, but a reviewer familiar with the stage/submit flow should sanity-check it (now covered by T039).

### Risks and open questions (5 min)

- If the `Plantilla.AssignTo` zero-template guard and CSV snapshot aren't both removed ([tasks T014](tasks.md#phase-2-foundational-blocking-prerequisites)), every Process assignment will throw. Is there test coverage proving Process→Plantilla assignment still works after the teardown? (It's exercised by existing Plantilla E2E, but worth confirming it's in the filtered run.)
- Reference-counted blob retention ([tasks T051](tasks.md#phase-5-user-story-3--quotation-reuse-within-an-application-priority-p2)) is the subtle correctness risk — if the count is computed before the row is detached, or across the wrong scope, a shared document could be deleted while still referenced. Does the integration test (T048) cover both the keep and the delete branch?
- Greenfield assumption ([research D10](research.md#d10-dacpac--greenfield-strategy)): re-keying `ImpactParameterValues` and dropping columns is destructive. If any environment already holds real application data, this needs a migration path. Is "no production application data" definitely true for every environment this deploys to?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
