# Feature Specification: Fund Process Reception Windows + Applicant Timing UX

**Feature Branch**: `044-process-reception-windows`
**Created**: 2026-06-21
**Status**: Draft
**Input**: feedback-3 Slice E — master sections §3, §22.1/22.2/22.2A, §24.1/24.2, §26.1–26.3, §28.11/28.12 in `seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md`. Depends on shipped Slice A (`038-auditor-provider-compliance`). Brainstorm: `brainstorm/40-process-reception-windows.md`.

## Summary

Replace the per-application "Solicitud duration" submission gate with **admin-configured, absolute-date reception windows** on a fund Process. An application may be submitted only when the current Costa Rica time falls inside an active reception window. Every blocked action explains why in es-CR, and applicants get a professional countdown/notice experience telling them when they can draft and when they can submit. Reception windows are stored as general **Process Events**, so future informational/milestone calendar items are a thin schema-free add-on.

A Process is defined **solely by its reception windows** — there is no separate process-level "global start/end" envelope (the source requirement's "overall process period" was a misreception confirmed during brainstorming). If a Process has no windows configured, it imposes no submission-timing restriction (open), preserving today's behavior.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin configures reception windows (Priority: P1)

As an administrator, I configure one or more reception windows on a Process so applicants can only submit during those periods. Each window has a name, a start date/time, an end date/time, an optional applicant-facing message, an optional description, an active flag, and a display order. I can add, edit, deactivate/reactivate, and delete windows, and each window shows its computed state (upcoming / open now / closed) in Costa Rica time.

**Why this priority**: Without admin-configurable windows there is nothing to gate submission against. This is the foundation the other stories depend on.

**Independent Test**: On `/Admin/Processes/{id}`, create two non-contiguous windows (e.g., Mar 1–Jun 1 and Aug 1–Sep 1), edit one, deactivate one, delete one, and attempt an invalid window (`end ≤ start`). Verify each window's state badge and the rejection of the invalid window — no applicant flow required.

**Acceptance Scenarios**:

1. **Given** a Process with no windows, **When** the admin adds a window with start `2026-03-01 00:00` and end `2026-06-01 00:00` (CR time), **Then** the window persists and shows the correct upcoming/open/closed state for the current date.
2. **Given** the window-edit form, **When** the admin enters an end date/time on or before the start, **Then** the system rejects it with an es-CR validation message and saves nothing.
3. **Given** an existing window, **When** the admin deactivates it, **Then** it is excluded from submission gating and from "next opens" computation but remains listed and reactivatable.
4. **Given** two overlapping windows, **When** the admin saves them, **Then** both are accepted (no overlap error) and gating treats them as a union.

---

### User Story 2 - Submission gated by reception windows (Priority: P1)

As an applicant, I can submit my application only when the current Costa Rica time falls inside an active reception window on my application's Process. When I cannot submit, the system tells me exactly why in es-CR — not yet open (opens on a date), between windows (next opens on a date), or all windows closed (last closed on a date). A Process with no windows configured stays open, subject to all the existing (non-timing) submission rules.

**Why this priority**: This is the core enforcement the client asked for — hard date/time gating with a clear explanation for every refusal.

**Independent Test**: For a Process with a single window, freeze the clock before / inside / after the window and attempt submission of a complete application each time. Verify allow inside, refuse-with-reason before and after, and that the reason text names the relevant date.

**Acceptance Scenarios**:

1. **Given** a complete application whose Process has an active window covering the current CR time, **When** the applicant submits, **Then** the application is submitted.
2. **Given** the current CR time is before the first window starts, **When** the applicant attempts to submit, **Then** submission is refused with an es-CR message stating the window opens on the configured date/time.
3. **Given** the current CR time is between two windows, **When** the applicant attempts to submit, **Then** submission is refused with an es-CR message stating the next window opens on the configured date/time.
4. **Given** the current CR time is after every window has closed, **When** the applicant attempts to submit, **Then** submission is refused with an es-CR message stating the windows have closed (with the last closed date/time).
5. **Given** a Process with zero configured windows, **When** the applicant submits a complete application, **Then** submission succeeds exactly as it does today (no timing restriction).
6. **Given** the current CR time equals a window's exact start instant, **When** the applicant submits, **Then** submission is allowed; **Given** the current CR time equals a window's exact end instant, **Then** submission is refused (start-inclusive, end-exclusive).

---

### User Story 3 - Applicant timing notices & countdown (Priority: P1)

As an applicant, I see a prominent, professional notice/countdown at the top of the application create and draft-edit screens that reflects the live window state: submission open now (with time remaining until it closes), upcoming (with the date/time the next window opens, and a note that I can prepare a draft meanwhile), or all windows closed. The notice shows the precise close/open instant (date and time in es-CR format), not a bare date, so the inclusivity boundary is unambiguous.

**Why this priority**: The client explicitly asked for a "nice" professional countdown experience, not just plain text. It pairs with the gating so applicants understand *why* the submit button is or isn't available.

**Independent Test**: Render the create and draft-edit screens for a Process with a window in each of the three states (future / current / past) and verify the notice shows the right mode, the right date/time, and a live remaining-time display when open.

**Acceptance Scenarios**:

1. **Given** the current CR time is inside an active window, **When** the applicant opens the create or draft-edit screen, **Then** the notice shows "submission open" with the time remaining until the window's close instant.
2. **Given** the current CR time is before the next window opens, **When** the applicant opens the screen, **Then** the notice shows when submission opens and that drafting is allowed meanwhile.
3. **Given** every window has closed, **When** the applicant opens the screen, **Then** the notice shows a closed state.
4. **Given** any state, **When** the notice displays a boundary instant, **Then** it shows date **and** time in es-CR `dd/MM/yyyy` format, not a bare date.
5. **Given** the submit button is disabled for any reason (timing or otherwise), **When** the applicant views it, **Then** the UI explains why.

---

### User Story 4 - Draft creation guarded against dead-ends (Priority: P2)

As an applicant, I am prevented from starting a **new** draft for a Process whose reception windows have all closed (no current or future window), with a clear es-CR explanation, so I never build an application that can never be submitted. Editing an **existing** draft remains allowed regardless of window state.

**Why this priority**: Protects applicants from wasted effort, but depends on US1/US2 being in place. A guard, not a core flow.

**Independent Test**: For a Process whose only windows are in the past, attempt to create a new application (expect refusal with explanation) and open an existing draft for editing (expect success). For a Process with a current/future window, expect creation to succeed.

**Acceptance Scenarios**:

1. **Given** a Process whose configured windows have all closed, **When** the applicant tries to start a new application, **Then** creation is refused with an es-CR explanation that there are no upcoming reception windows.
2. **Given** the same Process, **When** the applicant opens an existing draft, **Then** editing is allowed (the draft is not trapped), though submission remains gated/refused.
3. **Given** a Process with no windows configured, **When** the applicant starts a new application, **Then** creation succeeds.
4. **Given** a Process with a current or future window, **When** the applicant starts a new application, **Then** creation succeeds.

---

### User Story 5 - Future-proof event model (Priority: P3, schema-only)

As a maintainer, I want the reception window stored as a general Process Event with an `eventType` that admits future values (informational, deadline, milestone) without a schema reshape, so adding non-gating calendar banners later is a thin behavioral add-on.

**Why this priority**: No user-facing behavior in this slice; it is a shape decision that lowers the cost of a future slice. Lowest priority and verifiable structurally.

**Independent Test**: Inspect the Process Event store and confirm a reception window is persisted with `eventType = reception_window` and a `controlsSubmissionAvailability` flag, and that the type field can hold other values without a structural change.

**Acceptance Scenarios**:

1. **Given** a configured reception window, **When** it is persisted, **Then** it is stored as a Process Event of type `reception_window` with `controlsSubmissionAvailability = true`.
2. **Given** the Process Event store, **When** a non-reception event type is recorded, **Then** the schema accepts it without modification (even though no gating/display behavior exists for it in this slice).

---

### Edge Cases

- **Overlapping windows** → union semantics: submission is open if the current time is inside *any* active window; no overlap error is raised.
- **Inactive windows** (`isActive = false`) are ignored by gating and excluded from the "next opens" computation.
- **Window boundary moved while an applicant has the page open** → the next server-side action re-evaluates against the live configuration; the client is never trusted to decide availability.
- **Currently-open window deleted or deactivated mid-session** → the next submission attempt is refused with the appropriate reason.
- **Already-existing draft on a Process whose windows have all passed** → the draft remains editable but not submittable, with a clear closed notice.
- **Clock exactly at a window boundary across the CR/UTC offset** → evaluation is performed once in Costa Rica time to avoid double-conversion drift; the boundary is judged in CR time.
- **Submission blocked by a non-timing rule while a window is open** → the timing explanation composes with (does not replace) existing explanations such as incomplete required fields.

## Requirements *(mandatory)*

### Functional Requirements

#### Reception window configuration (US1)

- **FR-001**: Administrators MUST be able to create, edit, deactivate/reactivate, and delete reception windows on a Process from the Process administration screen (`/Admin/Processes/{id}`).
- **FR-002**: A reception window MUST carry: name, start date/time, end date/time, optional applicant-facing message, optional description, active flag, and display order. It MUST be stored as a Process Event of type `reception_window` with `controlsSubmissionAvailability = true`.
- **FR-003**: Window configuration MUST reject any window whose end date/time is on or before its start date/time, with an es-CR validation message and no partial save. Overlapping windows MUST be allowed (gating uses union/OR semantics).
- **FR-004**: The admin window list MUST surface each window's computed state (upcoming / open now / closed) evaluated in Costa Rica time.
- **FR-005**: Administrators MUST enter window dates/times as Costa Rica local time; the system MUST persist the corresponding absolute instant.

#### Submission gating (US2)

- **FR-006**: Submission MUST be permitted only when the current Costa Rica time satisfies `start ≤ now < end` for at least one **active** reception window on the application's Process (resolved via Application → Group → Process).
- **FR-007**: A Process with **zero** configured reception windows MUST impose no submission-timing restriction (open), subject to all other existing submission rules.
- **FR-008**: The previous Solicitud per-stage duration gate (the `Solicitud` stage-window-closed refusal on submission) MUST be removed from the submission path. The Revisión and Facturación stage-window behavior MUST remain unchanged.
- **FR-009**: When submission is blocked by window timing, the system MUST return a typed reason and an es-CR explanation distinguishing: before the first window (states the open date/time), between windows (states the next open date/time), and all windows closed (states the last closed date/time). This explanation MUST compose with — not replace — existing non-timing submission explanations.
- **FR-010**: All window evaluation (gating and display) MUST use one authoritative Costa Rica business timezone (`America/Costa_Rica`), configurable via a single platform setting and never per-fund.

#### Applicant notices & countdown (US3)

- **FR-011**: The application create and draft-edit applicant screens MUST show a prominent notice/countdown reflecting the live window state: open now (time remaining to close), upcoming (time/date until next open, noting drafting is allowed), or all closed (closed notice).
- **FR-012**: The notice MUST show the precise close/open instant (date **and** time, es-CR `dd/MM/yyyy` format), not a bare date, so the inclusivity boundary is unambiguous.
- **FR-013**: Whenever the submit button is disabled for any reason (timing or otherwise), the UI MUST explain why.

#### Draft creation guard (US4)

- **FR-014**: Starting a **new** draft for a Process MUST be blocked when the Process has configured windows but none is current or future (every window's end ≤ now), with an es-CR explanation. Creation MUST be allowed when no windows are configured, or when a current or future window exists.
- **FR-015**: Editing an **existing** draft MUST always be allowed regardless of window state; only new-draft creation and submission are window-gated.

#### Data model (US5)

- **FR-016**: Reception windows MUST persist in a general Process Event store whose event-type field admits future values (informational, deadline, milestone) without a schema reshape; only the reception-window type has gating/display behavior in this slice.
- **FR-017**: Window configuration changes MUST be point-in-time only — they MUST NOT retroactively affect applications that were already submitted under a prior configuration (a later deactivation/deletion neither revokes nor reopens a completed submission).

### Key Entities

- **Process Event**: A configurable calendar item belonging to a Process. Attributes: type (e.g., `reception_window`, with `informational`/`deadline`/`milestone` reserved for future use), name, optional description, start date/time, end date/time, a flag indicating whether it controls submission availability, an optional applicant-facing message, an active flag, a display order, and audit metadata. Relationship: many Process Events belong to one Process.
- **Reception Window**: The Process Event of type `reception_window` that controls submission availability. The set of active reception windows on a Process, evaluated against the current Costa Rica time, determines whether submission and new-draft creation are allowed and drives the applicant countdown/notice.
- **Process** (existing, spec 029): The fund process that owns reception windows. No global start/end dates are added; the Process is defined by its reception windows. The orphaned Solicitud stage-window duration setting is removed.
- **Application** (existing): Resolves its Process via Group → Process for gating. Submission and new-draft creation are gated by the Process's reception windows; existing draft editing is not.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can configure two non-contiguous windows (e.g., Mar 1–Jun 1 and Aug 1–Sep 1) and both independently open and close submission at the configured Costa Rica instants.
- **SC-002**: At exactly a window's start instant submission is allowed; at exactly its end instant submission is blocked (start-inclusive, end-exclusive), verified at the boundary second.
- **SC-003**: An applicant outside any window sees the correct next-open or all-closed notice and cannot submit; the disabled submit explains why in es-CR.
- **SC-004**: An applicant inside a window sees a live countdown to close and can submit a complete application.
- **SC-005**: A Process with no windows behaves exactly as it does today for submission, so the existing submission end-to-end coverage passes unchanged.
- **SC-006**: New-draft creation is blocked for a Process whose windows have all closed, while editing an existing draft for that Process still works.
- **SC-007**: All dates/times display in Costa Rica time / es-CR format, and gating and display agree at the boundary instant.

## Assumptions

- **Costa Rica is the single business timezone** (`America/Costa_Rica`, UTC−6 year-round, no daylight saving). A platform configuration setting can override the zone, but no per-fund or multi-region timezones are in scope.
- **The "global process period" from the source requirement is dropped** — confirmed during brainstorming as a misreception. The Process is defined solely by its reception windows; no process-level start/end envelope is stored or validated.
- **Inclusivity is start-inclusive / end-exclusive** (`start ≤ now < end`), with administrators entering an explicit closing instant; the UI displays the precise instant to remove the "through date X" ambiguity.
- **No-window means open**: a Process with zero reception windows behaves as today, keeping existing applications and tests valid (greenfield-nullable approach consistent with prior specs 029/032/037).
- **Submission is a point-in-time gate**: later window configuration changes never retroactively affect already-submitted applications.
- **The Solicitud stage-window duration setting becomes orphaned and is removed** in this slice (its only consumer is the submission gate that FR-008 removes); the Revisión and Facturación stage windows are untouched.
- **Drafts are always editable** once created; only new-draft creation and submission are window-gated.

## Dependencies

- Fund → Process → Group → Application chain (spec 029).
- Process administration detail screen and inline-edit conventions (specs 030/031).
- es-CR localization and the toast/confirm-dialog system (specs 012/024).
- The existing submission pipeline and the application creation entry point.

## Out of Scope

- Informational / deadline / milestone Process Event **behavior** — schema only in this slice (US5).
- Reviewer (Revisión) and signing (Facturación) stage-window timing — unchanged.
- Per-user maximum funding amount (feedback-3 Slice F, §4).
- Applicant timeline / percent-progress display (feedback-3 Slice G, §20).
- Per-fund timezones and any multi-region support.
