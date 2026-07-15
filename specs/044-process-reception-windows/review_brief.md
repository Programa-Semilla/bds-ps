# Review Brief: Fund Process Reception Windows + Applicant Timing UX

**Spec:** specs/044-process-reception-windows/spec.md
**Generated:** 2026-06-21

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Fund Processes gain admin-configured, absolute-date **reception windows**. An applicant may submit only when the current Costa Rica time falls inside an active window (start-inclusive, end-exclusive); every refusal explains why in es-CR, and applicants see a professional countdown/notice telling them when they can draft and when they can submit. This replaces the old per-application "Solicitud duration" submission gate. Windows are stored as general **Process Events** so future informational/milestone calendar items are a thin add-on. This is feedback-3 Slice E.

## Scope Boundaries

- **In scope:** reception-window CRUD on the Process admin screen; hard date/time submission gating in CR time; typed es-CR refusal reasons; applicant countdown/notice on create + draft-edit screens; new-draft creation guard against closed-forever processes; general `ProcessEvent` schema (reception type only has behavior).
- **Out of scope:** informational/deadline/milestone event *behavior* (schema only); reviewer/signing stage-window timing (untouched); per-user funding limits (Slice F); applicant timeline/% progress (Slice G); per-fund timezones; multi-region.
- **Why these boundaries:** keep the slice single-concern and leave reviewer/auditor/signing timing — which shipped specs depend on — undisturbed.

## Critical Decisions

### Drop the "global process period"
- **Choice:** A Process has **no** global start/end dates; it is defined solely by its reception windows.
- **Trade-off:** Diverges from the literal source requirement (which listed an "overall process period").
- **Feedback:** Confirmed during brainstorming as a misreception — agree this is correct?

### Replace only the Solicitud submission gate
- **Choice:** Reception windows replace the Solicitud per-stage duration gate; Revisión/Facturación stage windows stay as-is.
- **Trade-off:** Two different timing models coexist in the codebase (absolute-date for submission, duration-based for reviewer/signing).
- **Feedback:** Acceptable, or should reviewer/signing timing eventually move to the same model? (Not this slice.)

### Drop `SolicitudWindowDays`
- **Choice:** Remove the now-orphaned column + its platform default + the Solicitud branch of the stage-expiry evaluator.
- **Trade-off:** A schema column removal; needs a check that nothing else reads it.
- **Feedback:** OK to remove now vs. leave dormant?

## Areas of Potential Disagreement

### "No windows configured = open"
- **Decision:** A Process with zero windows imposes no submission-timing restriction.
- **Why this might be controversial:** One could argue a process should require an explicit window to accept anything.
- **Alternative view:** Make windows mandatory and backfill a wide-open window for existing processes.
- **Seeking input on:** Backward-compatibility (keep existing data/tests valid) was the deciding factor — agree?

### Inclusivity = start-inclusive / end-exclusive
- **Decision:** `start ≤ now < end`, with admins entering an explicit closing instant; UI shows date **and** time.
- **Why this might be controversial:** The client wrote "through June 1," which reads as inclusive-of-June-1.
- **Alternative view:** Treat the end date as inclusive-to-end-of-day (closes next midnight).
- **Seeking input on:** We chose explicit closing-time entry so the UI removes the ambiguity — confirm.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| New entity / table | Process Event (`dbo.ProcessEvents`) | General calendar item; reception window is one type |
| Reception event type | `reception_window` | The only type with gating/display behavior this slice |
| Submission-control flag | `controlsSubmissionAvailability` | Distinguishes gating events from cosmetic ones |
| Business timezone setting | `America/Costa_Rica` (config-overridable) | Single authoritative zone, never per-fund |

## Open Questions

- [ ] Confirm the "global process period" drop is correct (vs. the literal source requirement).
- [ ] Confirm removing `SolicitudWindowDays` now vs. leaving it dormant.
- [ ] Confirm "no windows = open" backward-compatibility stance.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Timezone/boundary drift (CR vs UTC) | High | Single CR business clock; evaluate once in CR time; boundary E2E (SC-002) |
| Breaking the large existing submission E2E suite | High | "No windows = open" keeps existing processes/tests valid (SC-005) |
| Removing `SolicitudWindowDays` breaks a hidden consumer | Medium | Plan-time code check before column drop |
| Applicant countdown UX quality (client asked for "nice") | Medium | Dedicated US3 at P1 with explicit display states |

---
*Share with reviewers before implementation.*
