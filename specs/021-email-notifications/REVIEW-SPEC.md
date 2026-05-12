# Spec Review: Email Notifications System (021)

**Spec:** specs/021-email-notifications/spec.md
**Date:** 2026-05-11
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Spec is implementable as-is. Eight prioritized user stories cover the five-event v1 workflow plus operational edge cases (provider outage, allowlist guard, role-change predicate). FRs and NFRs are concrete; SCs are measurable. Ten open questions are correctly scoped as planning-time pins, not implementation blockers. No `[NEEDS CLARIFICATION]` markers remain.

## Completeness: 5/5

### Structure
- All required sections present: Purpose (Input paragraph), User Scenarios & Testing, Edge Cases, Functional Requirements, Non-Functional Requirements, Success Criteria, Assumptions, Dependencies, Out of Scope, Open Questions.
- Repo convention sections (Event Catalog, Recipient Rules, Key Entities) included and aligned with the patterns established by specs 011 / 014 / 017 / 019.
- No placeholder text. No TBDs.

### Coverage
- Every event in §Event Catalog is covered by at least one user story (US1–US5).
- Every recipient bucket has explicit acceptance scenarios (applicant, reviewer, participating-admin).
- Operational paths (retry, dead-letter, allowlist) covered by US6–US7.
- Edge-case enumeration (EC-001..EC-015) is exhaustive for v1.

**Issues:** None.

## Clarity: 4.5/5

### Language Quality
- Requirements use `MUST` consistently. No `should` / `might` / `could` ambiguity in FRs.
- Edge cases enumerated with expected behavior, not just symptoms.
- Subject templates are quoted verbatim es-CR strings; no paraphrased descriptions.
- Bucket priority `applicant > reviewer > admin` stated explicitly in FR-012 and §Recipient Rules.

**Ambiguities Found:**

1. NFR-002 — "P95 time-to-send (...) MUST be under 30 seconds under normal load."
   - **Issue**: "normal load" is undefined. The platform has no prod traffic baseline yet.
   - **Why this is acceptable here**: Pre-production v1; no load profile exists. The plan phase will define "normal load" once the worker poll interval + provider RTT are pinned. SC-009 ratifies the metric against the actual E2E suite, which serves as the de-facto load profile for v1.
   - **Suggestion**: Plan phase clarifies "normal load = the load produced by a full E2E suite run with the default `Notifications:Worker:PollIntervalSeconds=5`."

2. FR-026 — "No new MVC routes are introduced. Access control MUST be enforced server-side by the existing authorize attributes on the target controllers."
   - **Issue**: Assertion that `/Reviewer/Applications/Details/{id}` and `/Applications/Details/{id}` already exist is implicit.
   - **Why this is acceptable here**: Specs 001 / 002 / 004 ship the applicant + reviewer detail surfaces; the routes are presumed by Assumptions. Plan phase verifies by `grep`-ing the controller-attribute set.
   - **Suggestion**: Plan phase adds a one-line confirmation that the routes exist; otherwise an evolution gate fires.

3. EC-002 — "demoted admin who *did* take an explicit action stays in the participating-admin bucket."
   - **Issue**: The exact tables consulted by the resolver are not named in the spec.
   - **Why this is acceptable here**: FR-013 says "existing reads (`Application.VersionHistory` + existing audit)"; the plan phase pins the precise table list once spec 002's audit shape is re-read.
   - **Suggestion**: Plan phase enumerates the predicate's exact SQL / EF query.

## Implementability: 5/5

### Plan Generation
- Every FR is locatable to a layer (Domain workflow hook → Application outbox writer / resolver → Infrastructure email sender → Web template).
- Dependencies on specs 002 / 004 / 016 / 019 are explicit; no unknown / speculative dependencies.
- Constraints (zero EF migrations, no inline `<img>`, brand-grep gate green) are enforceable and CI-verifiable.
- Scope is well-bounded: 5 events, 8 user stories, 2 new tables, 9 templates.

**Issues:** None.

## Testability: 5/5

### Verification
- Each FR has a corresponding SC or acceptance scenario.
- SC-001..SC-009 are automatable; SC-010 is the one qualitative criterion and is explicitly marked as such.
- Acceptance scenarios use Given/When/Then format throughout.
- Idempotency, retry, and allowlist behavior all have explicit test recipes in the user stories.

**Issues:** None.

## Constitution Alignment

- **§I Clean Architecture**: Implicit but consistent — recipient resolver is an Application-layer interface; `IEmailSender` lives in Infrastructure; Razor templates live in Web; Domain transition methods stay the trigger. No inverted-direction reference.
- **§II Rich Domain Model**: Workflow transitions (`Submit`, `SendBack`, `Resubmit`, `Approve`, `Reject`) on the `Application` aggregate stay the canonical trigger; the outbox row is written via a unit-of-work hook, not by a controller.
- **§III E2E Mandatory**: FR-031 + FR-032 + SC-001 + SC-005 enforce E2E coverage; AspireFixture extension is the net-new infrastructure.
- **§IV Schema-First (Dacpac)**: NFR-005 + SC-008 enforce dacpac-only schema with a CI grep gate over `**/Migrations/**`. No EF migrations introduced.
- **§V Specification-Driven Development**: User stories are priority-ordered and independently testable per principle.
- **§VI Simplicity / YAGNI**: Domain-event dispatcher abstraction was explicitly rejected (see `implementation-notes.md`); i18n key system rejected; multi-replica worker design deferred; in-app channel out of scope. All YAGNI'd with rationale.

**Violations:** None.

## Cross-Artifact Consistency

Plan and tasks do not yet exist (planning phase pending). Cross-artifact consistency check via `/speckit-analyze` is not yet applicable.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- Plan phase: define "normal load" for NFR-002 in concrete terms (a default poll interval + expected concurrent outbox-row count).
- Plan phase: enumerate the exact tables consulted by the participating-admin predicate (FR-013, EC-002).
- Plan phase: confirm `Application.Folio` (or equivalent) field name and population path; otherwise EC-009's fallback applies.
- Plan phase: pin the exact existing routes referenced by FR-026 (`/Reviewer/Applications/Details/{id}`, `/Applications/Details/{id}`).
- Plan phase: ratify OQ-001..OQ-010 with explicit decisions on each.

## Conclusion

The specification is sound and ready to advance to `/speckit-plan`. The open questions list is appropriately scoped — they are planning-time decisions, not implementation blockers, and the spec carries recommended defaults for each.

**Ready for implementation:** Yes (after `/speckit-plan` ratifies the planning-pin items).

**Next steps:**
1. User reviews the spec in-place (gate per `speckit-spex-brainstorm` skill).
2. Generate `review_brief.md` for stakeholder review (deferred until user OKs spec).
3. Proceed to `/speckit-plan`. The plan-phase pre-hook `speckit.spex-teams.research` is registered as optional; running it in parallel with planning will accelerate the open-question ratification.
