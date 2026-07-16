# Spec Review: Financial Disbursement Core

**Spec:** specs/045-financial-disbursement-core/spec.md
**Date:** 2026-07-15
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A tightly-scoped walking-skeleton spec for the first slice of the financial-execution program. Requirements are testable, scope is explicitly bounded with every parked concern mapped to a later slice, and the balance/ledger model is internally consistent. One real gap (over-disbursement vs. balance projection) was found and fixed during review.

## Completeness: 5/5

### Structure
- Mandatory sections present: User Scenarios & Testing, Requirements (Functional + Key Entities), Success Criteria. Purpose is carried by the leading program-context note + per-story "Why this priority". Error handling is covered via Edge Cases + FR-003/004/005/009/015/029/031.
- Recommended sections present: Assumptions, Out of Scope (slice-mapped), Key Entities.
- No placeholder/TBD text; no `[NEEDS CLARIFICATION]` markers.

### Coverage
- 31 functional requirements across Disbursement / Evidence / Reconciliation / Ledger & Balance / Roles & Workflow / Audit.
- Reference cases AC-001 (₡72) and AC-005 (missing invoice) both encoded as acceptance scenarios and success criteria.
- Edge cases: zero/negative, non-CRC, edit-after-evidence, exact-consume, cancel-pending, concurrency, both-amounts-differ, replace-file, negative-Available.

## Clarity: 5/5

No red-flag vagueness ("fast", "user-friendly", "handle appropriately", trailing "etc."). MUST/MUST NOT used consistently for normative statements. Amounts are concrete (CRC, one-colón detection).

**Ambiguities found and resolved during review:**
1. **Over-disbursement vs. balance projection** — the spec did not say whether a recorded-but-blocked over-disbursing disbursement counts toward `Paid` (driving `Available` negative). Resolved: FR-020 now states it counts (money left the bank) and `Available` presents negative rather than clamping; added a matching edge case.
2. **Allocation currency** — clarified in Assumptions that the executed agreement total is taken as its CRC total (agreements can be multi-currency per spec 015).
3. **Over-disbursement discrepancy attribution** — clarified in Assumptions that the agreement-level result is attributed as a blocking discrepancy on the disbursement that first crosses the ceiling.

## Implementability: 5/5

- Every capability maps to an existing platform seam named in Assumptions (executed FundingAgreement, IObjectStorage/upload guards, Auditor-style role+group scoping, version-history/admin-audit). No new managed dependencies; dacpac-only schema. This is directly plannable.
- Scope is a genuine thin vertical slice; no conflicting requirements; no unknown dependencies.

## Testability: 5/5

- All eight success criteria are objective and verifiable (exact amounts, blocked/allowed transitions, presence in audit trail, group-scoped visibility).
- Each user story carries an Independent Test and Given/When/Then acceptance scenarios, matching the platform's filtered-E2E delivery bar.

## Constitution Alignment

Aligned with the FundingPlatform constitution:
- **Clean Architecture** — spec stays at WHAT level; reuse of existing layers assumed, no dependency-rule violations implied.
- **Rich Domain Model** — state machine (Recorded/Inconsistent/Validated/Cancelled), immutability boundary, and invariants (no double-count, exact decimal, append-only ledger) are expressed as domain rules, not service/controller concerns.
- **es-CR** copy mandated. Integration-tests-hit-real-DB and filtered-E2E delivery bar are satisfiable from the acceptance scenarios.

No violations.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None outstanding (the over-disbursement/balance gap was fixed during this review).

### Optional (Nice to Have)
- [ ] During `/speckit-plan`, decide the precise data shape of the agreement-level over-disbursement discrepancy (attached-to-latest-disbursement vs. a distinct agreement-scoped record) — noted in Assumptions, safe to defer to planning.
- [ ] Consider an explicit FR for optimistic-concurrency on disbursement edits (currently only in Edge Cases) if the planner wants it first-class.

## Conclusion

The spec is sound, bounded, and implementable as the program's first slice. The parked-scope table gives future sessions a clean resume point for slices P2–P9.

**Ready for implementation:** Yes (after `/speckit-plan`).

**Next steps:** User review of the spec, then `/speckit-plan`.
