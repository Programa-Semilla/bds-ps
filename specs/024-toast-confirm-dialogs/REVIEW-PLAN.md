# Review Guide: Consistent In-App Notifications & Confirmation Dialogs

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-22

---

## What This Spec Does

The platform tells users about results and asks for confirmation in three different visual styles depending on which page you're on and what role you have: banner alerts pinned to the top of the page (for server outcomes), the browser's grey `window.alert` box (for one AJAX path), and the browser's `confirm()` box (for 15 destructive actions). This feature replaces all three with one consistent system — toasts for results, a styled modal for confirmations — so feedback looks and behaves the same everywhere.

**In scope:** A toast layer (success/error/warning/info, top-right, success auto-dismisses, errors stay), a TempData→toast bridge in the two shared layouts, replacing `window.alert` in `comparison.js`, migrating the 15 native `confirm()` sites to one styled modal, and an additive validation summary toast. Built on the vendored Bootstrap/Tabler — no new dependency, no schema change.

**Out of scope:** Email notifications (specs 019/021 — a different channel), any real-time/push/SignalR notification inbox, a notification history/center, and the persistent status widgets (autosave indicator, stage-countdown banner, supplier lookup-rejected notice) which are status UI, not result messages. Crucially, the feature does **not** change *which* actions require confirmation — only how the confirmation looks.

## Bigger Picture

This closes a long-running thread: brainstorm #08 (the Tabler UI strategy) asked whether `_ConfirmDialog` should be the baseline for every destructive action. The codebase already grew a reusable `_ConfirmDialog`/`_ActionBar` component (adopted in 11 views) — this feature finishes that job for the 15 stragglers and adds the missing toast half. It's the in-app sibling to spec 021's email notifications: 021 shipped the email channel; the old #08/#11/#19 thread about an in-app notification *inbox/SignalR* remains explicitly deferred and is **not** what this feature is (this is presentation consistency, not a new channel).

The approach leans entirely on Bootstrap 5's `Toast` and `Modal` (bundled in the vendored Tabler JS, already used by `site.js` for tooltips). If you want ecosystem context: Bootstrap Toasts are a stock component with `autohide`/`delay` options and a `.toast-container`; nothing exotic is being introduced.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec Overview](spec.md#overview) and the [plan Summary](plan.md#summary) plus [research Decisions 1–2](research.md#decision-1-confirmation-modal--reuse--extend-the-existing-_confirmdialog). As you read, consider:

- The plan keeps the **controller-side TempData contract unchanged** and only changes rendering. Is "presentation-only, touch no controllers" actually achievable for all ~120 message sites, or are there controllers that render messages in a non-TempData way the inventory missed?
- Toasts are **server-rendered** into the container (not built from a JS data-island). Does that feel right for no-JS resilience and E2E, or would you expect a cleaner pure-client approach?

### Key decisions that need your eyes (12 min)

**Two confirmation entry points, one modal** ([research Decision 1](research.md#decision-1-confirmation-modal--reuse--extend-the-existing-_confirmdialog))
The 11 existing views keep the declarative `_ConfirmDialog`/`_ActionBar` component; the 15 stragglers get a new `data-confirm-*` attribute interceptor backed by a *separate* shared modal. That's two mechanisms with similar-but-not-identical markup.
- Question: Is having both an `_ActionBar`-driven dialog **and** a `data-confirm` interceptor acceptable, or should the stragglers be pushed onto the existing component for a single code path — accepting the extra churn on row-level actions (delete item/quote)?

**Preserving `data-testid="success-banner"/"error-banner"` on toasts** ([research Decision 2](research.md#decision-2-toasts--bootstrap-toast-via-tabler--thin-wrapper-server-rendered-bridge))
~29 E2E files assert these ids; the plan keeps them on the new toast element to avoid mass test churn.
- Question: Is reusing the old `*-banner` testids on a toast a sensible compatibility shim, or misleading naming we'll regret (a "banner" testid on a "toast")? Worth a rename sweep now while we're here?

**Validation: inline + one summary toast** ([spec FR-008](spec.md#functional-requirements), [tasks T027](tasks.md#phase-6-user-story-4--validation-summary-toast-priority-p2))
- Question: The summary toast must not double up with the existing `ValidationErrors` "no se puede enviar" list (which becomes its own toast in [T012](tasks.md#phase-3-user-story-1--server-side-result-toasts-priority-p1--mvp)). Is the de-dup rule in T027 clearly enough specified, or could a form show two error toasts?

**Sticky error toasts** ([spec FR-004](spec.md#functional-requirements))
- Question: Errors never auto-dismiss. On a page that produces several errors over time, is unbounded stacking a problem, or is "user must dismiss" the right safety bias?

### Areas where I'm less certain (5 min)

- [spec FR-002](spec.md#functional-requirements) / [tasks T012](tasks.md#phase-3-user-story-1--server-side-result-toasts-priority-p1--mvp): I treated `Application/Details`'s `ValidationErrors` submit-blocking *list* as a single sticky error toast. A multi-reason list compressed into one toast may read poorly — it could instead stay an inline list. I picked the toast to satisfy FR-002's "ValidationErrors → toast" literally; reasonable people could keep it inline.
- [research Decision 2](research.md#decision-2-toasts--bootstrap-toast-via-tabler--thin-wrapper-server-rendered-bridge): The aria-live design (one polite container vs. per-toast assertive override) is stated as a default but not nailed down; assertive announcements for errors may need a separate live region to actually interrupt. I flagged this for task-time.
- [tasks T022](tasks.md#phase-4-user-story-2--styled-confirm-modal-for-destructive-actions-priority-p1): the two row-level deletes in `Application/Edit.cshtml` assume each row is its own form so the interceptor can submit "the originating form." If those buttons share a single form, the interceptor wiring needs more care than the task implies.

### Risks and open questions (5 min)

- If `confirm-dialog.js` loads but `bootstrap.Modal` is somehow unavailable, does the [native `confirm()` fallback (NFR-004)](spec.md#non-functional-requirements) still trigger, or do we lose the guard? The fallback is described as "active until JS initializes" — is the init/teardown ordering safe against partial failures?
- [SC-007](spec.md#measurable-outcomes) requires the full E2E suite green. Removing the banner blocks touches selectors in ~29 E2E files — is the appetite for that rewrite scope understood, given UI-quality-over-selector-stability is the project's stated posture?
- Does anything outside the two shared layouts render its own result alert (a standalone page, an error view) that the bridge would miss? The inventory found the FA panel and `Details` `ValidationErrors`; are there others?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
