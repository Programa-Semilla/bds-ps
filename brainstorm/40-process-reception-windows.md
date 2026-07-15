# Brainstorm: Fund Process Reception Windows + Applicant Timing UX

**Date:** 2026-06-21
**Status:** spec-created
**Spec:** specs/044-process-reception-windows/

## Problem Framing

feedback-3 Slice E (next slice after D `043` shipped). The client wants fund Processes to accept applications only during admin-configured, absolute-date **reception windows** (e.g. Nexus: Mar 1–Jun 1 and Aug 1–Sep 1), with hard date/time enforcement, a clear reason for every blocked submission, and a professional applicant countdown/notice experience. Master sections §3, §22.1/22.2/22.2A, §24.1/24.2, §26.1–26.3, plus open decisions §28.11 (inclusivity) and §28.12 (timezone).

Today the platform has no absolute-date windows — submission timing is a per-application *duration* gate (`SolicitudWindowDays`, relative to `StageEnteredAt`, evaluated by `StageExpiryEvaluator`). The applicant only sees a single active-stage countdown. All timestamps are UTC `DateTimeOffset`; no timezone conversion exists; culture es-CR.

## Approaches Considered (decisions made per fork)

### Stage-model boundary
- **A (chosen):** Replace only the Solicitud submission gate with reception windows; leave Revisión/Facturación duration windows + `StageExpiryEvaluator` + reviewer/signing banners untouched. Tightest scope, lowest blast radius.
- B: Rewrite the whole stage-window model (reaches into reviewer/auditor flows — out of slice).
- C: Reception windows coexist *with* the Solicitud duration window (confusing double-gate).

### §3.7 Process Calendar Events scope
- A: Reception windows only, no general event model.
- B: Full generic calendar-event system now.
- **C (chosen):** Reception-window *behavior* now, but shape the schema as a general `ProcessEvent` (`eventType`, `controlsSubmissionAvailability`, etc.) so informational/milestone events are a thin future slice.

### Timezone (§28.12)
- **A (chosen):** Fixed CR business timezone (`America/Costa_Rica`, UTC−6, no DST), one config knob, never per-fund. Admins enter CR local time; store UTC; evaluate + display in CR.
- B: Per-fund timezones (speculative complexity). C: pure UTC (bad admin UX).

### Inclusivity (§28.11)
- **Chosen:** start-inclusive / end-exclusive (`start ≤ now < end`), admin enters an **explicit closing instant**, UI shows the precise close time so "through June 1" ambiguity disappears. (Rejected: dates-only with inclusive-to-end-of-day.)

### Global process dates
- **Chosen (user correction):** There is **no** global process start/end. The "overall process period" in the source requirement was a misreception. The Process is defined solely by its reception windows; if a span is ever needed, derive it from min/max window dates.

### No-windows behavior (backward compat)
- **A (chosen):** Zero windows configured ⇒ submission open (subject to other existing rules). Keeps existing data + the large submission E2E suite valid; window tests seed their own windows (spec-031 throwaway pattern). (Rejected: mandatory windows / backfill.)

### Draft creation
- **A + user refinement (chosen):** Drafts always editable; only *new-draft creation* and *submission* are window-gated. New-draft creation blocked when the Process has windows but **none current or future** (avoids dead-end drafts that can never submit). No windows configured ⇒ creation allowed.

## Decision

Spec `044-process-reception-windows` created (5 user stories, 17 FRs, 7 SCs). Resolved decisions recorded in the spec's Assumptions: global dates dropped; start-incl/end-excl with explicit close time; fixed `America/Costa_Rica`; `SolicitudWindowDays` dropped (FR-008 removes its only consumer); submission is point-in-time (no retroactive effect of later config changes, FR-017). Spec-review gate: **SOUND** (no critical/important issues). review_brief.md generated.

## Open Threads

- Plan-time: confirm no residual reader of `SolicitudWindowDays` before dropping the column.
- Plan-time: decide client-side ticking vs. server-rendered remaining-time for the countdown (data is fully pinned by FR-011/FR-012).
- Future slice: informational/deadline/milestone `ProcessEvent` *behavior* (banners/milestones) — schema is ready, behavior deferred.
- Resolves §28.11 and §28.12 for the whole feedback-3 round.
