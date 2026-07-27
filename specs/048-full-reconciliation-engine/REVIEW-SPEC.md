# Spec Review: Full Reconciliation Engine

**Spec:** specs/048-full-reconciliation-engine/spec.md
**Date:** 2026-07-17
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A complete, well-bounded spec for the program-keystone slice (financial-execution P4). Requirements are testable and unambiguous, the zero-colón money guarantee is explicitly preserved, and scope is decomposed into four independently-testable user stories. Two minor clarity issues and one implementability check were found and fixed inline before stamping; nothing blocks planning.

## Completeness: 5/5

### Structure
- All required sections present: Overview/Purpose, Functional Requirements (grouped), Success Criteria, error handling (via Edge Cases + refusal FRs), Non-functional (folded into Assumptions per project convention), Dependencies, Out of Scope, Open Questions.
- Four prioritized, independently-testable user stories (P1 spine ×2, P2 dashboard, P3 notifications) with Given/When/Then acceptance scenarios.
- No placeholder/TBD text.

### Coverage
- Detection/materialization, severity, lifecycle, multi-level, dashboard, notifications each covered by explicit FRs.
- Edge cases cover the genuinely hard parts of model C: re-run stability, auto-resolve + recurrence, waived-warning invalidation, money-gate race, tolerance boundary, empty scope.

## Clarity: 5/5

### Language Quality
- Consistent MUST phrasing; warning starter set enumerated; stable-identity rule stated precisely.

**Ambiguities Found (all fixed inline):**
1. FR-010(b) "outside the process window" — the spec-044 reception window gates *submission*, not payment/evidence dates, so the anchor was ambiguous. **Fixed:** redefined to two concrete existing anchors — evidence dated after its related payment date, or before the funding-agreement execution date.
2. FR-013 "within an authorized tolerance" read oddly given no tolerance can be authorized in P4 (config UI is P5). **Fixed:** reworded to "within the rule's tolerance (0 CRC by default in this slice)."

## Implementability: 5/5

### Plan Generation
- Open Questions section already isolates the four real plan-time decisions: entity/scope-key shape (OQ-1), refactor-vs-materializer (OQ-2), dashboard placement (OQ-3), concurrency token (OQ-4).
- Dependencies on P1/P2/P3/spec-021/spec-040 are explicit; reuses existing roles, evaluators, outbox, storage — no new managed deps; additive dacpac-only schema.

**Implementability check raised (added as OQ-5):**
- FR-010(a) requested-vs-approved variance assumes the platform retains a distinct "requested" amount separate from the executed allocation at a scope surviving into execution. Added **OQ-5** requiring the plan to confirm the data exists, and to drop/redefine the warning rather than ship it hollow if it does not. This is the one warning rule whose computability is not self-evidently guaranteed by shipped data.

**Scope note (non-blocking):** This is the largest slice in the program (new engine + lifecycle + dashboard + notifications). The spec acknowledges this and the P1/P2 stories form a viable MVP spine on their own, satisfying the constitution's independent-deliverability principle. Recommend landing US1 and US2 as separate story checkpoints (mirrors the P3 guidance to keep regression green per checkpoint).

## Testability: 5/5

- SC-001…SC-007 are all measurable and map cleanly to acceptance scenarios.
- SC-004 explicitly gates on the P1–P3 money-gate regression (SC-006 family) staying green — the critical safety property.
- Role-scoping (SC-005), waive asymmetry (SC-006), and single-assignment-notification (SC-007) are each concretely verifiable, consistent with the constitution's E2E-primary quality gate.

## Constitution Alignment

- **Clean Architecture / Rich Domain (I, II):** Assumptions place the engine core on the existing pure evaluators and reuse aggregate-mediated behavior — aligned.
- **E2E-primary (III):** Success criteria + acceptance scenarios are E2E-shaped; mail capture (smtp4dev) named for SC-007.
- **Schema-first dacpac (IV):** Assumptions specify additive dacpac-only tables; TINYINT `HasConversion<byte>()` gotcha called out.
- **SDD (V):** Four independently-deliverable prioritized stories.
- **Simplicity / YAGNI (VI):** Tolerance config UI, external bank/agency legs, and the "Approved" state are explicitly deferred with named target slices rather than speculatively built.
- **Quality gate — optimistic concurrency:** FR-018 + OQ-4 require a RowVersion on the new Discrepancy record — directly satisfies the constitution's concurrent-edit rule.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- All resolved inline (FR-010(b), FR-013) or captured as a mandatory plan-time confirmation (OQ-5).

### Optional (Nice to Have)
- [ ] Consider splitting US1 (persisted discrepancies + severity) and US2 (lifecycle + history) into distinct implementation checkpoints to keep the P1–P3 regression green incrementally.

## Conclusion

The spec is sound, complete, and implementable. The zero-colón control guarantee is preserved by design (model C: persisted snapshot for visibility + fresh recompute at the money gate), and the non-blocking warning tier + lifecycle are well-scoped with clear deferrals to P5/P6/P7/P8/P9.

**Ready for implementation:** Yes (after `/speckit-plan`, which must resolve OQ-1…OQ-5).

**Next steps:** User review of the written spec, then `/speckit-plan`.
