# Review Brief: Supplier Recommendation Algorithm Rewrite

**Spec:** specs/039-supplier-recommendation/spec.md
**Generated:** 2026-06-18

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Replaces the reviewer's price-dominant 4-point supplier score with the client's explicit seven-criterion, deterministic, **explainable** recommendation (price, delivery lead time, warranty, Hacienda/CCSS/SICOP status, PME/PYME). Each criterion gives every eligible provider a base 1 point and the winner(s) 2; the highest total wins, so the lowest price no longer automatically prevails. Two new mandatory quote fields (delivery lead time, warranty — value + days/months) feed the algorithm. CCSS `sin inscripción` is a hard block (excluded from scoring; the application can't advance while such a provider is selected). The add-item form is reordered so the product name comes first. The spec-020 AI comparison is kept unchanged as a separate, optional aid.

## Scope Boundaries

- **In scope:** the 7-criterion algorithm + explainable breakdown UI; two new required quote fields; CCSS `sin inscripción` eligibility exclusion + reviewer-advance progression gate; final-score tie → manual selection; item-line field reorder (product name first); es-CR copy.
- **Out of scope:** persisted score history; auditor workflow stage + PDF move (slice C); regulatory freshness blocking + Hacienda API (slice D); warning creation/governance (slice A); any block beyond CCSS `sin inscripción`.
- **Why these boundaries:** §14 is the algorithm; slices C/D own the workflow and enforcement layers that build on slice A's audit/timestamp fields. Slice B stays a pure, self-contained scoring rewrite.

## Critical Decisions

### Keep the AI comparison alongside the new deterministic recommendation (Approach A)
- **Choice:** the deterministic /14 algorithm is the recommendation; spec-020 AI comparison remains an optional deeper-analysis aid.
- **Trade-off:** two comparison surfaces coexist (some conceptual overlap) vs. lowest-risk, nothing removed.
- **Feedback:** is "keep both" the right long-term posture, or should AI be demoted/retired once the transparent score exists?

### Compute scores live, do not persist
- **Choice:** recompute on read (current value-object pattern); §22.8's field list is the shape of the computed DTO, not a table.
- **Trade-off:** no historical snapshot of "score at decision time" vs. no invalidation logic and no migration.
- **Feedback:** is the lack of a stored score-at-decision snapshot acceptable?

### CCSS `sin inscripción` = progression gate (not just recommendation exclusion)
- **Choice:** reviewer may still select such a provider, but the advance action is blocked until the selection changes; anchored at today's reviewer step, re-anchored by slice C.
- **Trade-off:** introduces a workflow gate inside a scoring slice vs. honoring the client's explicit "cannot move forward" intent now.
- **Feedback:** is gating the advance (vs. preventing selection outright) the right ergonomics?

## Areas of Potential Disagreement

### Final-score tie → manual selection (§28.3)
- **Decision:** on a top-score tie, no provider is auto-recommended; the tied set is flagged "selección manual requerida."
- **Why this might be controversial:** some would prefer a single deterministic winner (e.g., lowest-price tiebreak) for less reviewer friction.
- **Alternative view:** lowest-price tiebreak — but that reintroduces the price primacy this feature exists to remove.
- **Seeking input on:** comfort with co-equal tied providers requiring a manual pick.

### Warranty direction (§28.2)
- **Decision:** longer warranty = better.
- **Why this might be controversial:** the source notes flag it as unconfirmed by the client.
- **Alternative view:** none expected, but it is an assumption.
- **Seeking input on:** explicit business confirmation.

### Month → days normalization = 30 days
- **Decision:** 1 month = 30 days for delivery/warranty comparison.
- **Why this might be controversial:** calendar months vary; a different constant could change a close ranking.
- **Alternative view:** calendar-month math or a configurable constant.
- **Seeking input on:** is 30 days acceptable for scoring comparison?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Hard-blocked provider state | *bloqueado* | Shown in breakdown for CCSS `sin inscripción` |
| Tie outcome | "selección manual requerida" | Shown when eligible providers tie at the top |
| No-eligible-provider state | "ningún proveedor elegible" | Shown when all candidates are blocked |
| Delivery/warranty units | `días` / `meses` | Quote-level value + unit |

## Open Questions

- [ ] Confirm warranty direction (longer = better) with the business.
- [ ] Confirm month→days = 30 for scoring comparison.
- [ ] Confirm "keep both" posture for the AI comparison long-term.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Progression gate placed in slice B then moved by slice C | Med | Localize the advance-guard so slice C re-anchors with minimal churn (note in plan) |
| Seed-data update for new required fields missed → existing seeded quotes fail validation | Med | Treat the dacpac seed-data update as a first-class task; greenfield no-backfill confirmed |
| Two tie rules (price→1, delivery/warranty→2) implemented uniformly by mistake | Med | Explicit per-criterion tests; called out in edge cases |
| Price compared on raw amount instead of normalized CRC across currencies | Med | Reuse spec-015 CRC-normalized amount; dedicated mixed-currency test |

---
*Share with reviewers before implementation.*
