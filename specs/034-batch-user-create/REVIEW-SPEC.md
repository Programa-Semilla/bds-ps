# Spec Review: Batch user creation (bulk applicant provisioning via CSV)

**Spec:** specs/034-batch-user-create/spec.md
**Date:** 2026-06-12
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** The spec is complete, unambiguous, and implementable. It cleanly reuses established seams (032 admin-create + UserCode uniqueness, 033 invitation onboarding, 016 required group membership, 029 Fund→Process→Group chain, 026 identification/phone rules, 021 email outbox) and bounds scope tightly (CSV-only, applicants-only, creation-only, synchronous, ≤200 rows). No critical or important issues.

## Completeness: 5/5

### Structure
- All required sections present: Purpose (via Input + US value statements), Functional Requirements, Success Criteria, Error Handling (Edge Cases + Error-handling narrative embedded in FRs), plus recommended sections (Edge Cases, Dependencies, Assumptions, Out of Scope, Key Entities).
- No TBD/placeholder text remains.

### Coverage
- Three independently testable user stories (P1 bulk-create, P1 per-row report, P2 chain integrity), each with acceptance scenarios and an Independent Test.
- File-level vs row-level failure modes are distinguished and both fully specified.
- Phone normalization, surname concatenation, in-file duplicates, invalid cédula, wrong chain, partial-then-fail, undelivered email, and the file-rejection set are all enumerated.

**Issues:** None.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; SHOULD is avoided.
- Concrete thresholds: 200-row cap, 50-char UserCode, 72-hour invitation, cédula física type, "first occurrence wins" tiebreak.
- Ambiguity that could exist ("which duplicate wins?", "what identification type?") is explicitly resolved in FR-006/FR-008 and Assumptions.

**Ambiguities Found:** None blocking. Minor note: FR-009 says ambiguous Grupo/Proceso/Fondo names are errors — the planning phase should confirm how names are scoped (e.g., Group name unique within a Process) so "ambiguous" is deterministic; this is an implementation detail, not a spec gap.

## Implementability: 5/5

### Plan Generation
- Touch points are identifiable: a new admin-only page/action under the existing /Admin/Users area, a CSV parser (in-house, no new dep), and orchestration over the existing single-create service path.
- Dependencies are explicit and already present in the codebase (no new managed deps — FR-014).
- Scope is manageable and synchronous (FR-001) given the 200-row cap.

**Issues:** None. Watch-item for the plan: CSV parsing with no new NuGet dep (FR-014) — confirm an in-house minimal parser handles quoted fields/commas/UTF-8 BOM from Excel exports.

## Testability: 5/5

### Verification
- Every SC is measurable and technology-agnostic (counts equal data rows, 0% leakage, 100% duplicate rejection, single rejection message).
- Each user story has an Independent Test; acceptance scenarios are Given/When/Then.
- E2E-friendly: aligns with constitution Principle III (Playwright per story). Duplicate/uniqueness-at-persistence paths (FR-008) may be E2E-only where the in-memory provider can't enforce the filtered unique index — consistent with prior specs (030/032).

**Issues:** None.

## Constitution Alignment

- **I Clean Architecture / II Rich Domain:** Reuses existing application service + domain entities; no inward dependency violation implied.
- **III E2E (non-negotiable):** Three independently testable stories map directly to Playwright classes.
- **IV Schema-first:** No schema change required (Proceso/Fondo are validation-only, not persisted) — aligns; the plan should confirm no dacpac change is needed.
- **V SDD / VI Simplicity:** YAGNI honored (no async/worker, no .xlsx, no report download in v1 — all explicitly deferred in Out of Scope).
- **Quality gate "collect validation errors and display at once":** The succeeded/errored report (FR-012) collects all row errors into one view — aligned.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] In the plan, pin how Grupo/Proceso/Fondo name uniqueness is scoped so FR-009 "ambiguous" is deterministic.
- [ ] In the plan, specify the in-house CSV parser's handling of quoted fields, embedded commas/newlines, and a UTF-8 BOM (Excel CSV exports), per FR-014.
- [ ] Consider whether the file-level rejection (FR-003) should report *all* failing file-level conditions at once vs. the first; the spec's single-message wording is acceptable.

## Conclusion

The spec is sound and ready for planning/implementation. It is well-bounded, reuses proven seams, and its success criteria are objectively verifiable.

**Ready for implementation:** Yes

**Next steps:** Proceed to `/speckit-plan` (the plan should resolve the three optional watch-items above, none of which block).
