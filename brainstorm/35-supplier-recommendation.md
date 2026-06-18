# Brainstorm: Supplier Recommendation Algorithm Rewrite (feedback-3 slice B)

**Date:** 2026-06-18
**Status:** spec-created
**Spec:** specs/039-supplier-recommendation/

## Problem Framing

Feedback-3 slice B (the supplier recommendation algorithm rewrite), the first foundation-dependent slice after slice A (spec 038) shipped. The current recommendation is a price-dominant 4-point score; the client (§14 of the unified requirements) wants a deterministic, **explainable** seven-criterion algorithm where price, delivery lead time, warranty, three regulatory statuses, and the PME/PYME flag all contribute, so the lowest price no longer automatically wins.

Confirmed first: **slice A is delivered** (PR #69, commit `b9197a1`; full spec lifecycle + gates; Unit 641/641, Integration 404/404, US4 E2E 1/1). Slice A's enums (Hacienda/CCSS/SICOP), PME/PYME flag, warning, and regulatory audit trail are the inputs this algorithm consumes.

§14 is highly prescriptive (the algorithm shape is dictated), so the brainstorm focused on a handful of genuinely-open seams rather than inventing scoring.

## Approaches Considered

### AI comparison (spec 020) coexistence
- **A: keep both, distinct roles** — deterministic /14 is the recommendation; AI comparison stays an optional deeper-analysis aid. **← chosen.** Lowest-risk, additive; §14 never mentions AI.
- B: deterministic replaces AI for recommendation, AI demoted to on-request prose.
- C: retire AI comparison in recommendation surfaces.

### Score persistence
- **A: compute live, don't persist** — keep the value-object pattern; §22.8 field list = shape of computed DTO. **← chosen.** Score is a pure function of stored data; persisting buys only a staleness problem.
- B: persist a `RecommendationScoreDetail` row (auditable snapshot, but needs invalidation like `ComparisonArtifact`).

### Disqualification (§28.13)
- A: scoring-only (no status disqualifies).
- B: block-with-override.
- C: hard block.
- **Chosen: hybrid** — only **CCSS `sin inscripción`** is a hard block; every other status affects scoring only.

### Tie-break (§28.3)
- A: show all tied as co-recommended.
- B: lowest-price tiebreak (rejected — reintroduces the price primacy §14 removes).
- **C: require manual selection** — **← chosen.** Tie → no auto-badge, flag the tied set, reviewer chooses.
- D: configured priority order (over-engineering).

## Decision

Spec 039 created and reviewed **SOUND**. Key resolutions:

- **Keep the AI comparison** (Approach A) as a separate optional aid; the new deterministic algorithm is the recommendation.
- **Seven criteria**, base 1 / win 2, total 7–14, highest wins. Price tie → all tied get 1 (none get 2); delivery/warranty tie → all tied get 2. Hacienda/CCSS `al día` = 2; SICOP `sin sanciones` = 2; PME/PYME flagged = 2.
- **Two new mandatory quote fields**: delivery lead time + warranty (value + days/months), normalized to days for comparison. **No backward compatibility** — seed data is updated; no production backfill (greenfield).
- **Warranty direction**: longer = better (§28.2 confirmed).
- **Month → days normalization = 30 days** (scoring-comparison only; independent of slice D's freshness rule).
- **Price comparison** uses the spec-015 CRC-normalized amount.
- **CCSS `sin inscripción` hard block**: excluded from scoring (winners decided over eligible set only), shown *bloqueado*; reviewer may still select such a provider, but the **advance action is gated** with an es-CR message until the selection changes. All-blocked item → "ningún proveedor elegible". Gate anchored at today's reviewer advance step; **slice C re-anchors** it into the auditor workflow (slice B adds no new workflow states).
- **Final-score tie** → manual selection (no auto-recommended badge).
- **Item-line field reorder** (§6/§24.4): product name → category → dynamic category fields → remaining fields. Owned by B (ships before slice H).
- **Live computation, no new table, no new managed deps; es-CR throughout.**

Slice map updated: A → shipped, B → spec-created (`seeds/feedback-3/00-decomposition.md`).

## Open Threads

- Plan-time: where the progression-gate evaluation lives (a single advance-guard/eligibility service) so slice C re-anchors it with minimal churn.
- Plan-time: display treatment of the total — raw total + breakdown vs. an "X/14" fraction (spec leaves presentation open, FR-022/FR-023).
- Plan-time: introduce the two new quote fields via the dacpac + a post-deploy seed-data update (Constitution IV); ensure every seeded quote is populated so existing seeds don't fail the new required-field validation.
- Confirm with the business: warranty direction (longer = better) and month→days = 30 for scoring.
- Long-term: revisit whether the AI comparison should be demoted/retired once the transparent deterministic score is in place (relates to spec 020, from #18).
- Slices C (auditor workflow stage), D (regulatory freshness + Hacienda API), E–H remain unspecified; C/D depend on slice A. C inherits this slice's progression gate.
