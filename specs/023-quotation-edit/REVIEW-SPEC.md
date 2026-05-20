# Spec Review: In-place Quotation Field Edit

**Spec:** specs/023-quotation-edit/spec.md
**Date:** 2026-05-20
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** **SOUND**

**Summary:** Spec is plannable as-is. Scope is well-bounded, requirements are concrete and testable, and every functional requirement maps to a measurable success criterion or acceptance scenario. Two constitutional touch-points (optimistic concurrency, per-story E2E coverage) need to surface in the plan's Constitution Check / Complexity Tracking — neither is a spec defect.

## Completeness: 5/5

### Structure
- All required sections present (Purpose embedded in Input + Stories; FRs, SCs, Error Handling embedded in stories/edges).
- Recommended sections included (NFRs, Edge Cases, Key Entities, Dependencies, Out of Scope, Open Questions, Assumptions).
- No placeholder text. Zero `[NEEDS CLARIFICATION]` markers.

### Coverage
- 11 functional requirements covering routing, persistence, authorization, lifecycle gating, AI-cache invalidation, conversion preview, entry points, and legacy-flag handling.
- 5 non-functional requirements covering i18n, accessibility, performance, idempotency, dependency posture.
- 8 success criteria, all measurable and tied to FRs.
- Edge cases section covers: deleted quote between GET/POST, state transition mid-edit, two-tab last-write-wins, atomic currency+price change, `LegacyNeedsReview` flagged rows, double-click.

**Issues:** none.

## Clarity: 5/5

### Language Quality
- Requirements use MUST / MUST NOT consistently; no "should" / "might" / "could" leakage.
- All thresholds carry numbers (Price > 0; ValidUntil ≥ today; render ≤ 200 ms p50; round-trip ≤ 500 ms p50).
- es-CR error copy is quoted verbatim ("La moneda 'X' está deshabilitada.", "Sucursal no válida para este proveedor.", "El estado de la solicitud cambió, recarga la página.").
- Cross-spec primitive references (`EditAmount`, `ChangeCurrencyAsync`, `ComparisonArtifact`, `LegacyNeedsReview`) are project vocabulary from accepted specs 013/015/020 — not implementation detail leaked into spec.

**Ambiguities Found:** none.

## Implementability: 4/5

### Plan Generation
- Spec is plannable: route shape, persistence routing, authorization, lifecycle gate, and reuse contract with Supplier/Add are all explicit.
- Dependencies on specs 013/015/020/021 are enumerated; each is already shipped.
- Scope is single-surface (one controller + one view + one extracted partial + Application/Edit row affordance). Manageable.

**Issues:**
- **I-1**: Spec opts out of optimistic concurrency ("last-write-wins") via the Assumptions section. The project Constitution (1.0.0) lists *"Optimistic concurrency MUST be used for entities with concurrent edit risk"* as a quality gate. The Application owner is a single actor, so concurrent-edit risk is two-tabs-same-user — arguably below the threshold. The plan MUST address this in its Constitution Check section: either justify the deviation in Complexity Tracking, or add an OC token.
- **I-2**: Constitution mandates per-user-story Playwright E2E coverage. Spec does not explicitly require E2E for each of the three user stories (SC-005 only references the *existing* Supplier/Add suite staying green). The plan should add tasks ensuring each US has at least one E2E covering the golden path.

## Testability: 5/5

### Verification
- Every FR maps to one or more SCs or acceptance scenarios.
- SCs include concrete values (1500 → 1750, CRC → USD, HTTP 403/422/400).
- Acceptance scenarios use Given/When/Then with seedable preconditions.
- Edge cases each describe both trigger and expected behavior.

**Issues:** none.

## Constitution Alignment

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | OK | UI surface lives in Web; persistence routes through domain primitives on the entity. |
| II. Rich Domain Model | OK | FR-006 routes mutations through `Quotation.EditAmount` / `ChangeCurrencyAsync`, not through service-side raw state edits. |
| III. End-to-End Testing | **Needs plan-level decision** (see I-2) — spec does not block, but plan must add per-US E2E coverage. |
| IV. Schema-First DB | OK | "No schema change" called out in Key Entities. |
| V. Specification-Driven Development | OK | Workflow followed; brainstorm → spec → (next: plan). |
| VI. Simplicity & Progressive Complexity | OK | Out-of-Scope section explicitly defers: cross-supplier swap, admin/reviewer/SupplierAdmin editing, OC tokens, file-Replace consolidation, deep-link CTA. |

Quality-gate side: "All validation errors MUST be collected and displayed at once." Spec lists field-level errors as separate paths but does not preclude collection. Plan should ensure server returns a `ModelState`-style collection rather than fail-fast on the first error.

## Recommendations

### Critical (Must Fix Before Implementation)
- *(none)*

### Important (Should Fix)
- **R-1** *(plan-phase)*: In `plan.md` Constitution Check, document the optimistic-concurrency deviation in the Complexity Tracking table — single-actor edits, two-tabs-same-user risk, project precedent (Item/Edit). Decide explicitly: justify or add token.
- **R-2** *(plan-phase)*: Add per-user-story Playwright E2E tasks in `tasks.md`: at minimum one golden-path test per US (P1 edit-price, P1 edit-after-return, P2 edit-currency). Reuse Page Object pattern.
- **R-3** *(plan-phase)*: Plan must specify how validation errors aggregate (one round-trip, all field errors surfaced — `ModelState` convention).

### Optional (Nice to Have)
- **R-4**: When picking up OQ-1 in a future iteration, consider hoisting the *Replace file* affordance into the Edit page for one-stop editing. Out of scope for v1 by design.
- **R-5**: Consider an SC explicitly asserting that AI comparison `ComparisonArtifact.Hash` changes after an Edit — testable via DB inspection.

## Conclusion

The spec is implementable and well-bounded. Three improvements (R-1..R-3) belong in `plan.md` and `tasks.md`, not in `spec.md`. No spec rewrite is required.

**Ready for implementation:** Yes, after `/speckit-plan` addresses R-1..R-3.

**Next steps:**
1. `/speckit-clarify` is optional — spec has no `[NEEDS CLARIFICATION]` markers and OQ-1..OQ-3 each carry a stated default.
2. `/speckit-plan` is the next workflow step. Constitution Check must surface R-1 and R-3 explicitly.
