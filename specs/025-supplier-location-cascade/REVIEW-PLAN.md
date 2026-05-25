# Review Guide: Supplier Branch Location Cascade (Provincia → Cantón → Distrito)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-22

---

## What This Spec Does

Applicants register supplier locations on the Supplier/Add screen, but today "Provincia" is a free-text box — no Cantón, no Distrito. This feature makes location three dependent dropdowns (pick a province, the cantón list narrows; pick a cantón, the distrito list narrows), matching how Costa Rica addresses actually work. It finishes infrastructure that spec 021 built but never connected to a form, and adds the missing third level.

**In scope:** a new Distrito catalog (488 rows); the same three-tier cascade on three forms — applicant new-supplier, applicant new-branch, admin branch-edit; all three levels required when entering a branch.

**Out of scope:** backfilling location on pre-existing branches; the dormant `CreateSupplierBranchHandler`/`ApplicationController` inline path (it has no live UI). See [spec Assumptions](spec.md#assumptions).

## Bigger Picture

This is the third increment on the spec-021 location work: spec 021 created the Province + Cantón catalogs, FK columns, a `/api/cantons` endpoint, a cascade script, and a reusable partial — then shipped without wiring the partial into any form (documented in `EVOLVE-NOTE-us2-applicant-flow`). So a fair amount of this feature is *connecting* dead-but-correct code, plus mirroring it one level deeper for Distrito. The deliberate "mirror Canton exactly" stance ([plan Key Design Decisions](plan.md#key-design-decisions)) keeps the codebase symmetric, which should make this cheap to review against the existing Cantón code. The one genuinely new external dependency is *data*, not software: Costa Rica's distrito list. There is no turnkey dataset that matches our exact cantón catalog, so the seed is reconciled by hand against authoritative sources and proven by a test ([research Decision 5](research.md#decision-5--distrito-dataset-source-count-reconciliation)).

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec User Scenarios](spec.md#user-scenarios--testing) and [plan Summary](plan.md#summary). As you read:

- The three surfaces are framed as three independent user stories (P1/P2/P3). Does treating the admin edit form (P3) as a fully separate, independently-shippable story match how you'd want this delivered, or is it really one change that should land together?
- Location becomes **required** on every branch-entry form. Existing branches with null location stay valid. Is "required going forward, no backfill" the right line, or will reviewers want a one-time cleanup of old free-text provinces?
- The cascade fetches districts over `/api/districts` per cantón selection. Is an anonymous, 1-hour-cached catalog endpoint ([contract](contracts/districts-api.md)) consistent with how you treat other reference data?

### Key decisions that need your eyes (12 min)

**Domain invariant arity — the deliberate deviation** ([plan Decision 6](plan.md#key-design-decisions), [research Decision 3](research.md#decision-3--domain-invariant-arity-setlocation))

`SetLocation` keeps the spec-021 rule "province + cantón both-or-neither" and adds "district must be consistent if set" — but does **not** force district-whenever-pair at the domain layer. The all-three-required guarantee lives in the form/controller layer instead. This is a conscious deviation from [FR-006](spec.md#functional-requirements) ("all three or none at the data layer"), made so the dormant inline path keeps compiling without being pulled into scope.
- Question for reviewer: is enforcing all-three at the form layer (not the aggregate) acceptable, given the rich-domain-model principle says invariants belong on the entity? Or should we bite the bullet, make the domain strict, and wire/retire the inline path now?

**Legacy `Province` string as a composed display value** ([research Decision 2](research.md#decision-2--display-continuity-via-composed-legacy-province-string))

On save we write `"Distrito, Cantón, Provincia"` into the old `Province` column so every existing display surface keeps working untouched; the FK columns are the truth.
- Question for reviewer: is storing a derived display string acceptable, or does duplicating data into a legacy column invite drift? Would you prefer updating the ~3 display sites to read the FKs instead?

**Seed sourcing + the count** ([research Decision 5](research.md#decision-5--distrito-dataset-source-count-reconciliation))

We target 488 distritos, built from a community gist reconciled against INEC/Wikipedia, validated by an integration test ([T019](tasks.md)).
- Question for reviewer: is 488 the right target for *our* catalog, or do you have an authoritative INEC figure to anchor against? The seed's correctness is the highest-risk part of this feature.

### Areas where I'm less certain (5 min)

- [research Decision 5](research.md#decision-5--distrito-dataset-source-count-reconciliation): I corrected the spec's premise mid-planning — our existing catalog **already contains Puerto Jiménez (`06_13`) and Monteverde (`06_12`)** (verified against `01_SeedProvincesCantons.sql`), so Golfito has 3 distritos, not 4. I'm confident in the *structure* of this correction but not in the exact distrito count (488 vs. a possibly-newer 489–492 official figure). The integration test pins whatever the enumeration actually yields; a reviewer who knows the current INEC DTA could save real time here.
- [tasks.md T026](tasks.md): how the `_BranchPicker` partial (whose model is `SupplierDetailViewDto`) receives the provinces list is left to resolve at implementation (extend the model vs. a child-action render). I picked neither deliberately — if you have a house preference, say so before T026.
- [FR-012](spec.md#functional-requirements): "validate only the active sub-path" relies on the existing controller dispatch on which sub-model is populated. I assumed that dispatch is reliable for the new-branch vs. new-supplier panels; worth a sanity check.

### Risks and open questions (5 min)

- If the chosen gist's cantón ordinals diverge from ours anywhere outside Puntarenas/Golfito, the `'PP_CC_DD'` mapping silently mis-parents districts. Is [T019](tasks.md)'s "every code's `PP_CC` prefix is an existing cantón" check strong enough, or should it also assert specific known districts per cantón?
- The cascade JS is generalized from the existing `province-canton-cascade.js` ([research Decision 1](research.md#decision-1--cascade-js-generalize-dont-duplicate)). If anything else in the app already depends on that file's exact behavior, does renaming/generalizing it risk a regression elsewhere? (Search showed only the orphaned partial uses it.)
- Three forms now hard-require location. Are there any legitimate flows (e.g., a foreign supplier, or a quick draft) where forcing a CR distrito would block a valid use case?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
