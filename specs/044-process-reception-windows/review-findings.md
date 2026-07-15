# Deep Review Findings — Fund Process Reception Windows (044)

**Date:** 2026-06-22 · **Branch:** 044-process-reception-windows · **Rounds:** 1 · **Gate Outcome:** PASS · **Invocation:** manual (after_implement)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 7 | 7 | 0 |
| Minor | 16 | 9 | 7 (documented/accepted) |
| **Total** | **23** | **16** | **7** |

**Agents:** 5/5 completed (Correctness, Architecture, Security, Production Readiness, Test Quality). External tools (CodeRabbit, Copilot) not installed — skipped.

**Post-fix verification:** Unit 743/0, Integration 462/0, filtered E2E `ReceptionWindow` 9/9.

---

## Important findings (all fixed in round 1)

### FINDING-1 — Review submit surface was not reception-aware
- **Category:** correctness · **Source:** correctness-agent · **Resolution:** fixed
- **What:** Applicant submission happens from `/Applications/{publicCode}/Review` ("Confirmar y enviar"), whose button is gated only on field-completeness. A field-complete draft with a closed/upcoming window rendered an **enabled** submit button with no timing explanation (FR-013/SC-003 violated on the one surface where submission occurs).
- **Fix:** `ApplicationController.Review` now builds the reception notice + `ReceptionCanSubmit` for Drafts; `Review.cshtml` renders `_ReceptionWindowNotice`, disables the button when `Model.CanSubmit && receptionCanSubmit` is false, and lists the timing reason alongside the field reasons.

### FINDING-2 — Navigational submit POST rendered raw 422 JSON
- **Category:** correctness · **Resolution:** fixed
- **What:** `Submit` caught only `InvalidOperationException`; `ReceptionWindowClosedException` bubbled to the global filter → a `ProblemDetails` JSON body shown in the browser on a navigational POST (newly reachable in 044).
- **Fix:** `Submit` now catches `ReceptionWindowClosedException`, composes the es-CR message (via `ReceptionWindowResources` + `IBusinessTimeZone`), and redirects with `TempData["ValidationErrors"]` — defense-in-depth behind the now-disabled button.

### FINDING-3 — Draft Review showed a bogus "Revisión" stage banner
- **Category:** correctness · **Resolution:** fixed
- **What:** `Review` called `PopulateStageBannerAsync` for Drafts; after the Solicitud arm was removed, `ResolveStageKind(Draft)` defaults to `Revision`, mislabeling a Draft as being in Revisión with a fabricated closing date.
- **Fix:** For Drafts, the reception notice replaces the stage banner; the stage banner is kept only for non-Draft (Revisión/Facturación read views).

### FINDING-4 — `BusinessTimeZone` conversion was untested (FR-005/FR-010/SC-007)
- **Category:** test-quality · **Resolution:** fixed
- **Fix:** Added `BusinessTimeZoneTests` (CR→UTC +6h, UTC→CR −6h, round-trip, unknown-zone fallback to −06:00, missing-config default).

### FINDING-5 — Admin CR-local→UTC persistence not asserted end-to-end
- **Category:** test-quality · **Resolution:** fixed
- **Fix:** `ReceptionWindowAdminTests.CostaRicaLocalInput_PersistsAsAbsoluteUtc` enters `08:00` CR-local via the UI and reads `StartUtc` back from SQL, asserting `14:00 UTC`.

### FINDING-6 — No positive happy-path submit during an open window (SC-004)
- **Category:** test-quality · **Resolution:** fixed
- **What:** All "allowed" assertions were indirect (an item-validation exception, or `!= 422`).
- **Fix:** `ReceptionWindowSubmissionTests.Open_CompleteApplication_Submits_AndStaysSubmittedAfterWindowDeactivated` builds a complete draft (item + 2 quotations + attributed impact) and asserts it reaches `Submitted` through the gate.

### FINDING-7 — FR-017 non-retroactivity test was tautological
- **Category:** test-quality · **Resolution:** fixed
- **What:** The prior test forced `State=Submitted` via an EF entry hack, so the assertion could never fail.
- **Fix:** The new test (FINDING-6) performs a **real** gated submit, then deactivates the window and asserts the state stays `Submitted` — a genuine FR-017 proof.

---

## Minor findings — fixed (9)

- **FINDING-8** (security, IDOR): window Update/SetActive/Delete were not scoped to the route `processId`. Fixed — `WindowBelongsToProcessAsync` guard (404 on mismatch).
- **FINDING-9** (security, least-privilege): reception actions inherited `Admin,Auditor`; FR-001 says Administrators. Fixed — `[Authorize(Roles="Admin")]` on all five actions.
- **FINDING-10** (architecture, false audit): `SetActiveAsync` audited even on a no-op. Fixed — short-circuit when state unchanged (mirrors `ProcessService.RenameAsync`).
- **FINDING-11** (correctness, concurrency): no `DbUpdateConcurrencyException` catch on window Update/Delete/SetActive. Fixed — es-CR "modificado por otra persona" toast.
- **FINDING-12** (architecture, FR-002 fidelity): the gate keyed off `EventType` only. Fixed — added `&& e.ControlsSubmissionAvailability` to the gating query so future event types never gate.
- **FINDING-13** (architecture, YAGNI): `IBusinessTimeZone.CurrentOffset` had no consumers. Fixed — removed from the interface + impl.
- **FINDING-14** (correctness, clock discipline): admin badge used `DateTimeOffset.UtcNow`. Fixed — injected `IStageExpiryClock`.
- **FINDING-15** (production-readiness, doc): `dbo.Processes.sql` referenced `07_…` (actual `09_…`). Fixed.
- **FINDING-16** (test-quality, loose E2E): the open-window submit assertion was `!= 422`. Fixed — reworked to drive the real Review surface (button-state + notice assertions).

## Minor findings — accepted / documented (7)

- **FINDING-17** (correctness, compose-vs-replace, FR-009): the gate short-circuits before item validation in the handler. **Resolved at the display surface** — the Review page now lists the timing reason alongside the field reasons (FINDING-1), so the applicant sees a composed picture.
- **FINDING-18** (architecture): `ReceptionWindowSnapshot.Id`/`Name` are currently unused. Kept — a minimal forward-looking projection (a future refusal could name the window); removing them churns the evaluator + tests for no behavior change.
- **FINDING-19** (architecture): `ReceptionWindowNoticeViewModel.CanSubmit` restates `ReceptionAvailability.CanSubmit`. Low-risk duplication; both derive the same status set. Left documented.
- **FINDING-20** (production-readiness): `IX_ProcessEvents_ProcessId` INCLUDE doesn't cover `StartUtc/EndUtc`, so the gating projection incurs a key lookup. Accepted — a Process has a handful of windows; absolute cost is trivial.
- **FINDING-21** (production-readiness): `DATETIMEOFFSET(0)` second-truncation vs sub-second `now`. Benign — admin input is minute-granular, so stored bounds never carry sub-seconds; comparison against a sub-second `now` is well-defined.
- **FINDING-22** (test-quality): the US5 schema round-trip forces `EventType` via EF entry on InMemory. Accepted as structural-only; the TINYINT `HasConversion<byte>` materialization is exercised by the real-SQL E2E suite (every reception window is a TINYINT round-trip).
- **FINDING-23** (test-quality): `ComputeState` has no inactive-window case. Accepted — `ComputeState` is intentionally time-only; the `IsActive` badge layering is asserted in the admin E2E.

---

## Conclusion

Gate **PASS**: all Critical (0) and Important (7) findings resolved in one fix round; 9 of 16 Minor findings fixed, 7 documented as accepted. The fixes clustered on the **Review submit surface** (a real gap the original crafted-POST E2E masked), the **security posture** (Admin-only + IDOR scoping), and **test rigor** (timezone conversion, a genuine positive submit, and a non-tautological FR-017 proof). Post-fix suites are green across Unit/Integration/E2E.
