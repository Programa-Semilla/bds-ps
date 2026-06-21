# Spec Review: Regulatory Freshness Gating + Hacienda API Sync

**Spec:** specs/043-regulatory-freshness-hacienda-sync/spec.md
**Date:** 2026-06-21
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A well-bounded slice that closes the slice-A compliance loop with two clear capabilities (staleness block + daily Hacienda sync). Requirements are testable, scope is explicit, the external contract is captured from a live call, and the three deferred decisions all have stated defaults so none blocks planning.

## Completeness: 5/5

### Structure
- Overview, four prioritized user stories, edge cases, functional requirements, key entities, success criteria, assumptions, dependencies, out-of-scope, and open questions are all present.
- No TBD/placeholder text remains.

### Coverage
- The two core capabilities (FR-004..FR-009 block; FR-011..FR-017 sync) plus failure handling (FR-018..FR-021), early warning (FR-010), notification (FR-022), and cross-cutting NFRs (FR-023..FR-025) are covered.
- Error/edge cases (never-reviewed field, multi-stale, concurrent edit, API outage spanning the window, already-released apps) are explicit.

**Issues:** None.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; success criteria are quantified (100% of stale attempts blocked, 0% silent failures, etc.).
- The Hacienda→status mapping common cases are pinned in FR-016; only the genuinely uncertain rows are deferred and flagged.

**Ambiguities Found:**
1. "providers the application relies on" (FR-006) — explicitly acknowledged as needing exact selection semantics, captured as Open Question 2 with a proposed default. Acceptable: it is a flagged plan-time confirmation, not a hidden ambiguity.

## Implementability: 5/5

### Plan Generation
- Builds entirely on identified, shipped seams (slice A regulatory model + audit + re-authorize; slice C audit stage + auditor scoping; specs 021/028 outbox). Dependencies section names each.
- The external contract (endpoint, response shape, 404 behavior) is captured concretely, removing the biggest unknown.
- Scope is one coherent slice; no decomposition needed.

**Issues:** None. FR-024/FR-025 reference technical mechanisms (host resilience, optimistic concurrency) but these are constitution-mandated quality gates and reuse-driven, appropriately stated as constraints rather than design.

## Testability: 5/5

### Verification
- Each user story has an Independent Test and Given/When/Then acceptance scenarios.
- The `IHaciendaApiClient` test seam (FR-012) makes the daily job and all gating E2E-testable without the live API — directly supporting Constitution Principle III.
- Success criteria are measurable and technology-agnostic.

**Issues:** None.

## Constitution Alignment

- **I. Clean Architecture** — the integration seam (Application interface + Infrastructure impl), reuse of existing services, and notification pipeline respect layer boundaries.
- **II. Rich Domain Model** — status mapping/refresh expressed as provider behavior consuming slice-A domain methods; no anemic leakage implied.
- **III. E2E Testing (NON-NEGOTIABLE)** — every story is independently testable; the fake-client seam keeps E2E deterministic. ✅
- **IV. Schema-First** — new per-provider last-sync metadata will be a dacpac change (noted as a provider-data addition); no EF migrations implied.
- **V. SDD** — spec created before plan/implementation. ✅
- **VI. Simplicity/Progressive Complexity** — configurable window with default (FR-002), no new managed dependency (Assumptions), out-of-scope explicitly defers E/F/G/H and real-time lookups. ✅

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] At plan time, resolve Open Question 1 (Desinscrito/omiso mapping rows) against the real Hacienda value vocabulary, ideally by sampling a few live identifications, so the mapping table is fully pinned before implementation.
- [ ] At plan time, confirm whether the new per-provider last-sync metadata lives as columns on the Supplier table vs. a small related record, and reflect it in data-model.md.

## Conclusion

The spec is complete, unambiguous where it matters, implementable on identified seams, and fully testable. The three open questions are correctly scoped to planning with safe defaults.

**Ready for implementation:** Yes (after `/speckit-plan`)

**Next steps:** Proceed to `/speckit-plan`; resolve the three Open Questions during planning.
