# Review Guide: Supplier Recommendation Algorithm Rewrite

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-18

---

## What This Spec Does

Today the reviewer's "recommended supplier" is essentially "cheapest with clean compliance" — a 4-point score where price dominates. This rewrite replaces it with the client's seven-criterion scoring (price, delivery lead time, warranty, Hacienda/CCSS/SICOP status, PME/PYME), where each criterion hands every eligible provider a base point and the winner(s) a second, so a pricier provider with faster delivery, longer warranty, and cleaner standing can win. It also adds two now-required quote fields (delivery lead time, warranty), hard-blocks providers that aren't CCSS-registered, and puts the product name first on the add-item form.

**In scope:** the scoring algorithm + an explainable per-criterion breakdown on the reviewer screen; two required quote fields; the CCSS `sin inscripción` exclusion + a per-item reviewer-advance gate; tie → manual selection; item-line field reorder.

**Out of scope:** persisting the score; the auditor workflow stage and PDF-confirmation move (slice C); regulatory freshness blocking + the Hacienda API job (slice D); creating/governing provider warnings (slice A); any block beyond CCSS `sin inscripción`. The spec-020 AI comparison is deliberately left running, untouched.

## Bigger Picture

This is **slice B** of the feedback-3 decomposition (`seeds/feedback-3/00-decomposition.md`). Slice A (spec 038, shipped PR #69) created the regulatory status enums, the PME/PYME flag, the provider warning, and the audit trail that this algorithm now *consumes*. Slice C will turn the auditor into a workflow actor and will **re-anchor the progression gate** this slice introduces; slice D adds freshness enforcement and the Hacienda sync. So the most consequential review question isn't the arithmetic (the source doc §14 pins that down precisely) — it's whether the seams this slice opens are the right ones for C and D to build on.

One notable interaction worth a reviewer's attention: the platform now has **two** comparison surfaces — the new deterministic recommendation and the existing AI quote comparison (spec 020). The spec keeps both ([assumption "AI comparison retained"](spec.md#assumptions)). Is that the right long-term posture, or does a transparent deterministic score eventually make the AI comparison redundant on the recommendation surface?

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md §Functional Requirements FR-005–FR-015](spec.md#functional-requirements) for the algorithm and [research D1](research.md) / [data-model §5](data-model.md) for how it's realized. As you read:

- The algorithm is a pure function recomputed on every read, never stored ([research D7](research.md)). Is "no score-at-decision-time snapshot" acceptable, given an auditor or appeal might later ask "why was this provider recommended on that date?" The audit trail for *status* lives in slice A, but the *composite score* is never persisted — is that a gap?
- Every eligible provider always scores ≥7 and the winner ≤14. Is a 7–14 band meaningful to a reviewer, or should the surface present something more legible than a raw total ([decision below](#key-decisions-that-need-your-eyes))?

### Key decisions that need your eyes (12 min)

**The progression gate lives inside a scoring slice** ([spec.md FR-019](spec.md#functional-requirements), [research D4](research.md), [contracts C2](contracts/interfaces.md))

The decomposition assigned §28.13 to slice B as a *scoring* concern, but the client's "the application cannot move forward" turns it into a workflow gate on `Item.Approve`. We chose to honor it now (anchored at today's per-item reviewer approve) and let slice C re-anchor it.
- Question: is putting an advance-blocking guard in a slice nominally about *scoring* acceptable, or should the gate wait for slice C so the workflow logic lands in one place? The trade-off is shipping the client's stated behavior now vs. a cleaner slice boundary.

**Reviewer can select a blocked provider but can't approve with it** ([spec.md US3](spec.md#user-story-3---ccss-sin-inscripción-disqualifies-a-provider-and-blocks-progress-priority-p2))

We gate the *approve submission*, not the dropdown selection, so the reviewer sees the full picture before being stopped.
- Question: is that the right ergonomics, or would reviewers prefer the blocked provider be un-selectable outright?

**Two different tie rules** ([spec.md FR-008 vs FR-009/FR-010](spec.md#functional-requirements))

Price ties give *all* tied providers 1 (nobody gets the bonus); delivery/warranty ties give *all* tied providers 2. The price rule is the client's explicit instruction; the delivery/warranty rule is the brainstorm default.
- Question: does the asymmetry surprise you? It will surprise a future maintainer — is it documented loudly enough ([data-model §5 algorithm](data-model.md), [research D2](research.md))?

**`null` CCSS status is not a block** ([research D4](research.md), task [T028](tasks.md))

Only the explicit enum value `sin inscripción` blocks; an unreviewed (`null`) provider merely scores 1.
- Question: is it right that an *unreviewed* provider can still be recommended and approved, while a *known-unregistered* one is blocked? That asymmetry is intentional but worth a sanity check against how auditors actually populate these statuses.

**Display: total + breakdown, not a fraction** ([spec.md FR-022/FR-023](spec.md#functional-requirements), [research D10](research.md))

We drop the `/4` (and a stray `/5` bug in the dropdown) in favor of a total plus per-criterion scores.
- Question: the spec leaves the exact presentation open. Should the plan pin it now (e.g., "Total 12 · Precio 2 / Entrega 1 / …") to avoid a late UI decision, or is leaving it to implementation fine?

### Areas where I'm less certain (5 min)

- [data-model §3 / research D8](research.md): I chose `NOT NULL` + `DEFAULT(1)` placeholders for the new quote columns plus a seed-data update, because dev runs a persistent SQL volume and a bare `NOT NULL` add would fail the dacpac publish (the spec-029 lesson). If the team is comfortable wiping the dev volume, simpler nullable-free options exist — I may have over-applied the migration-safety pattern for what is genuinely greenfield data.
- [research D6](research.md): I assert today's algorithm compares *raw* `Price` and that comparing the spec-015 `ConvertedCrcAmount` is a latent-bug fix. That's based on reading `SupplierScore.cs:36`. A reviewer who knows the multi-currency history should confirm this is a real fix and not a behavior someone relied on.
- [tasks.md Phase 2 + US2 coupling](tasks.md#dependencies--execution-order): because the `Quotation` constructor gains required params, Foundational and US2 must be built together to keep the build green. T010 ("compile-driven sweep") is the least crisp task — its true scope depends on how many `new Quotation(...)` sites exist. I flagged it but didn't enumerate them.

### Risks and open questions (5 min)

- If the seed-data update ([T008](tasks.md)) gives every seeded quote identical delivery/warranty, the SC-001 demo (a non-cheapest winner) won't actually show the new behavior — does the seed deliberately *vary* these values across providers on the same item?
- When slice C re-anchors the progression gate ([research D4](research.md)), will the guard placed on `Item.Approve` move cleanly, or will it have grown reviewer-specific assumptions that fight the auditor flow? Is a thinner seam (a single eligibility check the workflow calls) worth it now?
- The recommendation is per-item, but an application has many items. If item A's best provider is blocked and item B's isn't, the *application* can't advance until A is resolved — is that per-item-blocks-whole-application behavior what reviewers expect ([spec.md FR-019/FR-020](spec.md#functional-requirements))?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
