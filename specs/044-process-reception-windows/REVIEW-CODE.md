# Code Review — Fund Process Reception Windows (044)

**Spec:** [spec.md](spec.md) · **Date:** 2026-06-22 · **Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall: 100% (17/17 FR), with 2 minor interpretation notes.**

- Configuration (US1): [FR-001](spec.md#reception-window-configuration-us1)–[FR-005](spec.md#reception-window-configuration-us1) — Compliant
- Submission gating (US2): [FR-006](spec.md#submission-gating-us2)–[FR-010](spec.md#submission-gating-us2) — Compliant (FR-009 note below)
- Notices (US3): [FR-011](spec.md#applicant-notices--countdown-us3)–[FR-013](spec.md#applicant-notices--countdown-us3) — Compliant (FR-013 note below)
- Draft guard (US4): [FR-014](spec.md#draft-creation-guard-us4)–[FR-015](spec.md#draft-creation-guard-us4) — Compliant
- Data model (US5): [FR-016](spec.md#data-model-us5)–[FR-017](spec.md#data-model-us5) — Compliant

**Tests:** Unit 738/0, Integration 462/0, filtered E2E `ReceptionWindow` 7/7.

**Interpretation notes (not deviations):**
- [FR-009](spec.md#submission-gating-us2) "compose with — not replace": the reception gate short-circuits before the item/quotation validators, so a timing-blocked submit shows *only* the timing reason (you cannot submit regardless). Non-timing explanations still surface in full when timing permits. Composition holds across scenarios, not within a single blocked attempt.
- [FR-013](spec.md#applicant-notices--countdown-us3): the disabled-submit explanation is rendered for the *timing* reason (`reception-submit-disabled-note`). Other pre-existing disable reasons (incomplete draft) keep their existing UX.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes, focusing on high-level questions that need human judgment.

**Changed files:** ~30 across all layers — Domain (`ProcessEvent`, `ReceptionWindowEvaluation`, enums, exception, `Process` edit), Application (2 interfaces + DTOs, `IBusinessTimeZone`, error code), Infrastructure (2 service impls, `BusinessTimeZone`, EF config, DI, handler/autosave/stage-expiry edits), Database (1 table, column drop + post-deploy script), Web (controller actions + 2 views + 2 partials/VMs + filter + translator + 2 resources), plus unit/integration/E2E tests.

### Understanding the changes (8 min)

- Start with `src/FundingPlatform.Domain/ReceptionWindows/ReceptionWindowEvaluation.cs`: the entire gating policy is this one pure function (`Evaluate(windows, nowUtc)`). Everything else feeds it snapshots or renders its result.
- Then `src/FundingPlatform.Infrastructure/Services/SubmitApplicationHandler.cs:75` area: the enforcement seam — it loads active windows (Application→Group→Process) and throws `ReceptionWindowClosedException` before item validation.
- Question: the gate is enforced in the handler (cross-aggregate), while evaluation is a pure domain function. Does that split read cleanly, or would you expect the gate inside the `Application` aggregate? (Mirrors how the removed Solicitud `stageClosesAt` was resolved in the handler.)

### Key decisions that need your eyes (12 min)

**Legacy Solicitud gate removed across 3 consumers** (`Application.Submit`, `AutosaveFieldHandler`, `StageExpiryEvaluator`; relates to [FR-008](spec.md#submission-gating-us2)/[FR-015](spec.md#draft-creation-guard-us4))

The 4-arg `Submit` overload + the autosave window throw + the StageExpiry Solicitud arm were deleted, and `SolicitudWindowDays` dropped (dacpac + `09_DropSolicitudWindowDays.sql`).
- Question: a non-obvious consequence — `StageExpiryReminderService` now excludes `Draft`/`AppealOpen` (they had no window but the Solicitud arm previously gave them one). Is "no stage-expiry reminders for drafts" the intended behavior?

**Timezone only at the Web boundary** (`src/FundingPlatform.Infrastructure/Time/BusinessTimeZone.cs`, relates to [FR-010](spec.md#submission-gating-us2))

Gating is pure UTC instant comparison; `IBusinessTimeZone` is used only for admin `datetime-local`→UTC and UTC→CR display, with a fixed −06:00 fallback if the zone id is absent on the host.
- Question: is the fixed −06:00 fallback acceptable, given CR observes no DST?

**Draft-creation refusal keyed to the model-level summary** (`src/FundingPlatform.Web/Controllers/ApplicationController.cs`, [FR-014](spec.md#draft-creation-guard-us4))

The refusal is added with an empty ModelState key (not `GroupId`) so it shows in the `ModelOnly` validation summary for both single-group (hidden input, no field span) and multi-group layouts.
- Question: acceptable, or should the create view gain a `GroupId` field-level span instead?

**Window admin errors via TempData toast** (`AdminProcessesController` reception actions)

End≤start / name errors surface as `TempData["ErrorMessage"]` toasts (matching the sibling `AssignPlantilla`/`StageOverride` actions), not inline ModelState spans.
- Question: consistent with the page's other inline cards (Rename uses an inline span)? Acceptable divergence?

### Areas where I'm less certain (5 min)

- `ReceptionWindowQuery.GetAvailabilityForApplicationAsync` ([FR-006](spec.md#submission-gating-us2)): resolves the Process via `Application→Group→Process`. A null/missing Process returns `Unrestricted` (fail-open). Is fail-open the right posture if the chain is somehow broken?
- E2E submission gating uses a crafted authenticated POST to `/Application/{id}/Submit` rather than driving the full review→confirm UI ([FR-006](spec.md#submission-gating-us2)/[SC-004](spec.md#measurable-outcomes)). The full happy-path submit during an open window is asserted only to *not* hit the reception 422; the complete submit ceremony is covered by other suites. Is that coverage sufficient?
- `_ReceptionWindowNotice` countdown is server-rendered static (no client tick) — an open page goes stale until reload (a planning-time choice). Acceptable for v1?

### Deviations and risks (5 min)

Deviations from [plan.md](plan.md)/[tasks.md](tasks.md), framed as questions:

- Post-deploy script named `09_DropSolicitudWindowDays.sql` (not `07_` per [T010](tasks.md)) — `07_`/`08_` were already taken. Question: acceptable rename?
- Service "integration" tests (`ReceptionWindowServiceTests`, `ProcessEventSchemaTests`, `ReceptionWindowSubmissionTests`) use EF InMemory, following the shipped `FundServiceTests`/`ProcessRenameServiceTests` precedent; real-SQL coverage is via E2E. Question: acceptable given CLAUDE.md's "integration hits real DB" rule, or should these move to the real-SQL fixture?
- E2E consolidated into 2 files (`ReceptionWindowAdminTests`, `ReceptionWindowApplicantTests`) rather than the 4 files named in [T024](tasks.md)/[T029](tasks.md)/[T034](tasks.md)/[T036](tasks.md), to share the isolated-process setup. Question: acceptable consolidation?
- Risk: `ReceptionWindowSeed` (E2E) inserts windows on an isolated Process; if a future suite relied on the shared demo Process having no windows, it is unaffected (isolation by construction). Question: any shared-fixture pollution concern I missed?

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes what
> was checked, what was found, and what remains for human review.

**Date:** 2026-06-22 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 6 | completed |
| Architecture & Idioms | 5 | completed |
| Security | 2 | completed |
| Production Readiness | 3 (+5 verifications) | completed |
| Test Quality | 7 | completed |
| CodeRabbit (external) | — | skipped (not installed) |
| Copilot (external) | — | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 7 | 7 | 0 |
| Minor | 16 | 9 | 7 (documented) |

### What was fixed automatically

The high-impact cluster was the **Review submit surface**, which the original crafted-POST E2E had masked: a field-complete draft with a closed/upcoming window rendered an *enabled* submit button and, on POST, raw 422 JSON. Fixed by making `/Applications/{publicCode}/Review` reception-aware (notice + timing-gated button + composed reasons), catching `ReceptionWindowClosedException` in `Submit`, and suppressing the (now-mislabeled) Solicitud stage banner on Draft review. **Security:** reception actions are now `Admin`-only (FR-001) and window mutations are scoped to the route Process (no IDOR). **Tests:** added `BusinessTimeZone` conversion coverage, an admin CR→UTC persistence read-back, a genuine open-window happy-path submit, and a non-tautological FR-017 proof. Plus minor sweeps (no-op audit short-circuit, concurrency catch, gate keyed on `ControlsSubmissionAvailability`, unused `CurrentOffset` removed, clock discipline, doc fix).

### What still needs human attention

All Critical and Important findings were resolved. 7 Minor findings remain, documented as accepted in [review-findings.md](review-findings.md) (FINDING-17…23) — e.g. the gating projection key-lookup (tiny row counts), `DATETIMEOFFSET(0)` second precision (benign given minute-granular input), and the US5 schema round-trip being InMemory-structural (real TINYINT materialization covered by E2E). Reviewers may glance at these but they are not blocking.

### Recommendation

All findings addressed. Code is ready for human review with no known blockers. See [review-findings.md](review-findings.md) for the full finding-by-finding record and [spec.md](spec.md) for requirements.
