# Feature Specification: Consistent In-App Notifications & Confirmation Dialogs

**Feature Branch**: `024-toast-confirm-dialogs`
**Created**: 2026-05-22
**Status**: Draft
**Input**: User description: "Every result message, confirmation or alike, currently uses either fixed messages in the html on top of the page, and also window.alert. I want this to be changed and have a consistent behavior across all pages, and all roles. Using toast notification or similar to inform of success messages or error, and modal dialogs when needed."

## Overview

Today, result and confirmation messaging in the platform is inconsistent. Server-side outcomes render as fixed banner alerts pinned to the top of the page body (carried via TempData and rendered in the shared layouts). Client-side/AJAX errors use the native browser `window.alert`. Destructive-action confirmations use the native browser `confirm()` dialog. The visual style, screen placement, dismissal behavior, and accessibility of these messages differ from page to page and role to role.

This feature unifies them into **one** consistent system across every page and every role (applicant, reviewer, admin, and unauthenticated/auth pages):

- **Toast notifications** for transient operation results (success, error, warning, info).
- **Styled modal dialogs** for confirmations and any blocking message.
- **Inline field validation stays inline**, but a failed submit additionally raises a single summary toast.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consistent toast for server-side operation results (Priority: P1)

A user on any page and in any role performs an action that completes server-side (e.g. saves a record, submits an application, archives an item). Instead of a banner alert pinned to the top of the page, they see a toast notification appear in a consistent location (top-right), styled consistently, conveying success or failure. Success/info toasts fade away automatically; error/warning toasts stay until dismissed.

**Why this priority**: This is the most common feedback path in the system (≈120 TempData success/error messages across controllers). Unifying it delivers the bulk of the consistency value and is the foundation the other stories build on.

**Independent Test**: Trigger any action that sets `TempData["SuccessMessage"]` (e.g. a successful save) as an applicant, then as an admin, then on an auth page; in every case a consistently-styled success toast appears top-right and auto-dismisses. Trigger an action that sets `TempData["ErrorMessage"]`; an error toast appears and persists until dismissed. Confirm no top-of-page banner alert renders.

**Acceptance Scenarios**:

1. **Given** an applicant completes an action that sets a success message, **When** the resulting page loads, **Then** a success toast appears top-right and auto-dismisses after ~5 seconds, and no top-of-page banner alert is shown.
2. **Given** an admin completes an action that sets an error message, **When** the resulting page loads, **Then** an error toast appears top-right and remains until the user dismisses it.
3. **Given** a single request produces both a success and an error message, **When** the page loads, **Then** two toasts appear stacked, each dismissible independently.
4. **Given** a message was shown via post-redirect-get, **When** the user refreshes the page, **Then** the toast does NOT reappear (shown exactly once).
5. **Given** a funding-agreement action sets `FundingAgreementSuccess`/`FundingAgreementError`, **When** the page loads, **Then** the message appears as a toast (not a panel-embedded alert block).

---

### User Story 2 - Styled confirmation dialog for destructive actions (Priority: P1)

A user (most often an admin, but also applicants and reviewers) initiates a destructive or irreversible action — deleting an item, disabling a user, resetting a password, overwriting an agreement, archiving a template, returning an application, withdrawing an upload. Instead of the native browser `confirm()` box, a consistently-styled modal dialog appears with a clear title, an explanation, a confirm button, and a cancel button. The action proceeds only if the user confirms; cancelling aborts with no side effect.

**Why this priority**: Destructive actions are the highest-risk interactions, and the native `confirm()` is the most visually jarring inconsistency. Replacing all ~16 existing call sites with one styled modal both improves consistency and reduces accidental data loss.

**Independent Test**: For each existing confirm() call site, trigger the action; verify a styled modal appears with the correct (es-CR) title/body, that confirming proceeds with the action, and that cancelling aborts without any state change.

**Acceptance Scenarios**:

1. **Given** an admin clicks "Inhabilitar usuario", **When** the click is intercepted, **Then** a styled modal appears asking to confirm, and the user is disabled only after the user presses the confirm button.
2. **Given** the confirmation modal is open, **When** the user presses Cancel or Esc, **Then** the modal closes and the guarded action does not execute (no request is sent / no form submitted).
3. **Given** a destructive action's call site supplies custom copy, **When** the modal opens, **Then** it displays that title, body, confirm label, cancel label, and variant (e.g. danger styling).
4. **Given** a destructive action's call site supplies no copy, **When** the modal opens, **Then** it displays a safe default es-CR title/body and confirm/cancel labels.
5. **Given** the wrapper JavaScript fails to load, **When** the user triggers a destructive action, **Then** a native `confirm()` fallback still guards the action so it is never executed unguarded.

---

### User Story 3 - Toasts for client-side / AJAX outcomes (Priority: P2)

A user triggers an action handled client-side without a full page reload (e.g. enqueuing an AI comparison run). Instead of `window.alert`, the outcome surfaces as a toast raised through a shared JavaScript API, matching the look and behavior of server-driven toasts.

**Why this priority**: Fewer call sites than the server path, but required to fully eliminate `window.alert` and achieve the "no inconsistent dialogs anywhere" goal.

**Independent Test**: Trigger the AI-comparison enqueue flow and force an error response; verify an error toast appears via the JS API (no `window.alert`), styled identically to server-driven error toasts, and persists until dismissed.

**Acceptance Scenarios**:

1. **Given** an AJAX action fails, **When** the failure is handled client-side, **Then** an error toast is raised via the shared JS API and no `window.alert` is shown.
2. **Given** an AJAX action succeeds, **When** the success is handled client-side, **Then** a success toast may be raised via the same API, styled and timed like server-driven success toasts.

---

### User Story 4 - Validation summary toast on failed submit (Priority: P2)

A user submits a form with invalid fields. Inline field-level validation messages still appear at the offending fields (unchanged). Additionally, a single summary toast (e.g. "Corrige los campos marcados") informs the user that the submit did not go through and directs attention to the highlighted fields.

**Why this priority**: Improves discoverability of validation failures without detaching errors from their fields. Lower priority because inline validation already works; this is an additive cue.

**Independent Test**: Submit a form (e.g. application edit) with an invalid required field; verify the inline field error still renders AND exactly one summary toast appears.

**Acceptance Scenarios**:

1. **Given** a form submit fails server-side ModelState validation, **When** the page re-renders, **Then** inline field errors render at the fields AND exactly one summary error toast is shown.
2. **Given** a form submit succeeds, **When** the page loads, **Then** no validation summary toast is shown.

---

### User Story 5 - Accessible, non-blocking, consistent presentation (Priority: P3)

A keyboard or screen-reader user receives the same feedback as a mouse user: toasts are announced via a live region and do not trap or block interaction; the confirmation modal traps focus while open, can be dismissed with Esc, and returns focus to the triggering control when closed.

**Why this priority**: Accessibility is a cross-cutting quality bar. Separated as its own story so it is explicitly tested rather than assumed.

**Independent Test**: With a screen reader, trigger a success and an error toast and confirm both are announced (success politely, error assertively). Open the confirmation modal with the keyboard, confirm focus is trapped, Esc cancels, and focus returns to the trigger.

**Acceptance Scenarios**:

1. **Given** a screen-reader user, **When** a success toast appears, **Then** it is announced via a polite live region; **When** an error toast appears, **Then** it is announced assertively.
2. **Given** the confirmation modal is open, **When** the user tabs through controls, **Then** focus stays within the modal; **When** the modal closes, **Then** focus returns to the element that opened it.
3. **Given** a toast is on screen, **When** the user interacts with the page beneath it, **Then** the page remains fully interactive and the layout does not shift.

### Edge Cases

- **Message shown exactly once**: A message carried via post-redirect-get must surface once and not reappear on refresh (TempData one-read semantics preserved).
- **Multiple messages in one request**: All surface as separate stacked toasts.
- **Empty/missing message**: No toast is rendered.
- **Long message text**: Wraps gracefully within the toast without breaking layout.
- **Confirmation modal with no configured copy**: Falls back to safe es-CR default copy.
- **Only one confirmation modal at a time**: Triggering a second confirmation while one is open does not stack competing modals.
- **JavaScript unavailable / wrapper fails to load**: Destructive actions fall back to native `confirm()`; they are never executed without any guard.
- **Toast raised during AJAX**: Appears without a page reload, identical in style to server-driven toasts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a single toast notification mechanism, available on all pages and to all roles (applicant, reviewer, admin, and unauthenticated/auth pages), supporting four variants: success, error, warning, and info.
- **FR-002**: Server-side outcomes currently carried via TempData (`SuccessMessage`, `ErrorMessage`, `FundingAgreementSuccess`, `FundingAgreementError`, and the validation-summary message) MUST surface as toasts after the post-redirect-get, replacing the top-of-page banner alert blocks currently rendered in the shared main layout and the auth layout.
- **FR-003**: Client-side and AJAX outcomes MUST be able to raise toasts through a shared client-side API, and the existing `window.alert` usage(s) MUST be replaced by this API.
- **FR-004**: Success and info toasts MUST auto-dismiss after a short interval (~5 seconds); warning and error toasts MUST persist until the user dismisses them.
- **FR-005**: Toasts MUST stack when multiple are shown, MUST be manually dismissible, MUST appear in the top-right of the viewport, and MUST NOT block interaction with the page beneath them.
- **FR-006**: The system MUST provide a single reusable confirmation modal that replaces every existing native `confirm()` call site and guards any destructive/irreversible action. It MUST present a title, an explanatory body, a confirm action, and a cancel action; the guarded action MUST execute only on confirm, and cancel MUST abort with no side effect.
- **FR-007**: Confirmation copy (title, body, confirm label, cancel label, and visual variant such as danger) MUST be configurable per call site, default to es-CR copy when not supplied, and preserve existing resource-based confirmation strings (e.g. `AdminSuppliersResources.Verify_Confirm`).
- **FR-008**: Form field validation (ModelState / inline field validation) MUST remain displayed inline at the offending fields; on a failed-validation submit, the system MUST additionally raise exactly one summary toast.
- **FR-009**: Persistent status UI that is NOT a result message — the autosave indicator, the stage-countdown banner, and the supplier lookup-rejected notice — MUST remain unchanged and is out of scope for this feature.
- **FR-010**: All toast and confirmation-modal copy MUST be es-CR and localizable; no English-only strings may be introduced.
- **FR-011**: Each TempData-carried message MUST surface exactly once and MUST NOT reappear on page refresh (TempData one-read semantics preserved).
- **FR-012**: The confirmation modal MUST be accessible: it MUST trap focus while open, support Esc to cancel, and return focus to the triggering control when closed; only one confirmation modal may be open at a time.
- **FR-013**: Toasts MUST be announced to assistive technology via a live region — politely for success/info and assertively for warning/error.

### Non-Functional Requirements

- **NFR-001**: The feature MUST NOT introduce any new managed (NuGet), CDN, or external runtime dependency. It MUST be built on the already-vendored UI component library (Bootstrap 5 / Tabler toast + modal) plus a thin first-party wrapper and a server→toast bridge in the shared layout.
- **NFR-002**: Added client assets MUST stay within the project asset budget (verified by the existing asset-budget check).
- **NFR-003**: Toasts MUST overlay in a fixed region and MUST NOT cause layout shift when they appear or dismiss.
- **NFR-004**: Destructive actions MUST degrade safely: if the wrapper script fails to load, a native `confirm()` fallback MUST still guard them.

### Key Entities

- **Toast notification**: A transient message with a variant (success/error/warning/info), text body, and lifetime (auto-dismiss vs. sticky). Rendered top-right, stackable, dismissible.
- **Confirmation request**: A blocking prompt tied to a destructive action, carrying a title, body, confirm label, cancel label, and variant; resolves to confirm (proceed) or cancel (abort).
- **Server message channel**: The existing TempData keys (`SuccessMessage`, `ErrorMessage`, `FundingAgreementSuccess`, `FundingAgreementError`, validation summary) that the layout bridges into toasts. The controller-side contract for setting these is unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the JS-enabled path, no `window.alert` or native `window.confirm` dialog is shown — the AJAX path raises a toast and all 15 confirmation call sites open the styled modal. A native `confirm()` remains in the markup *only* as the no-JS fallback required by NFR-004 (it never fires when the wrapper script loads). *(Evolved during implementation: original wording "no window.confirm invocations remain" contradicted NFR-004; the fallback is intentional and safety-critical.)*
- **SC-002**: TempData success/error/funding-agreement/validation messages render as toasts on every role's pages, and the top-of-page banner alert blocks are removed from both shared layouts.
- **SC-003**: A toast looks and behaves identically (style, top-right placement, dismissal rules) regardless of page or role, verified across at least one applicant page, one reviewer page, one admin page, and one auth page.
- **SC-004**: Every destructive action shows the styled confirmation modal and proceeds only on confirm; cancel aborts with no observable side effect.
- **SC-005**: A failed form submit shows inline field errors AND exactly one summary toast.
- **SC-006**: Toasts are announced to a screen reader (politely for success/info, assertively for warning/error); the confirmation modal traps focus, Esc cancels, and focus returns to the trigger.
- **SC-007**: The full E2E suite passes, including new coverage that exercises toast appearance and confirmation-modal confirm/cancel across applicant, reviewer, and admin roles.

## Assumptions

- The platform's existing post-redirect-get pattern and TempData usage in controllers remain the source of server-side messages; this feature changes only how those messages are *presented*, not how controllers set them.
- The vendored Bootstrap 5 / Tabler component library provides toast and modal primitives sufficient for the required behavior, so no new dependency is needed.
- "All roles / all pages" covers the applicant, reviewer, admin, and unauthenticated/auth surfaces that share the two existing layouts; surfaces not using these layouts (if any) are brought under the same mechanism.
- Toast screen position is top-right of the viewport (decided during brainstorming).
- The `warning` variant is included now (not deferred).
- Existing localized confirmation strings are reused where present; new copy is authored in es-CR.

## Dependencies

- Vendored Tabler / Bootstrap 5 static assets (no CDN).
- The existing TempData / post-redirect-get controller contract (unchanged).
- The es-CR localization infrastructure (spec 012).

## Out of Scope

- Email notifications (specs 019 / 021) — a separate delivery channel.
- Real-time / push / SignalR notifications.
- A notification center, notification history, or persistence of past toasts.
- The persistent status UI listed in FR-009 (autosave indicator, stage-countdown banner, supplier lookup-rejected notice).
- Changing *which* actions require confirmation — only the confirmation *mechanism* changes; all current `confirm()` sites adopt the modal, and no new confirmations are added beyond destructive actions already guarded.
