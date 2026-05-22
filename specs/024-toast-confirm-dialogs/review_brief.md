# Review Brief: Consistent In-App Notifications & Confirmation Dialogs

**Spec:** specs/024-toast-confirm-dialogs/spec.md
**Generated:** 2026-05-22

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Result and confirmation messaging is inconsistent today: server outcomes render as fixed banner alerts at the top of the page (TempData → `_Layout`/`_AuthLayout`), client-side AJAX errors use `window.alert`, and destructive actions use the native browser `confirm()`. This feature unifies all of it into one system across every page and role: **toasts** (success/error/warning/info) for operation results and **styled modal dialogs** for confirmations. Inline field validation stays inline but gains a single summary toast on failed submit. Built on the already-vendored Bootstrap 5 / Tabler toast + modal — no new dependency, no schema change.

## Scope Boundaries

- **In scope:** Toast mechanism (all roles/pages), TempData→toast bridge replacing both layout banner blocks, JS toast API replacing `window.alert`, one reusable confirm modal replacing all ~16 `confirm()` sites, validation summary toast, accessibility (live region + focus trap), graceful native-`confirm` fallback.
- **Out of scope:** Email notifications (019/021), real-time/push/SignalR, notification center/history, the persistent status UI (autosave indicator, stage-countdown banner, lookup-rejected notice), and changing *which* actions require confirmation.
- **Why these boundaries:** Keep the change presentation-only — controllers' TempData/PRG contract is unchanged; only how messages are shown changes.

## Critical Decisions

### Reuse vendored Bootstrap/Tabler (no new dependency)
- **Choice:** Build on the already-vendored toast + modal primitives plus a thin first-party wrapper.
- **Trade-off:** Less out-of-box polish than a dedicated lib (Toastify/SweetAlert2), but zero new dependency, no CDN, fits CLAUDE.md "reuse vendored" posture and asset budget.
- **Feedback:** Agree this is the right posture vs. vendoring a dedicated toast/dialog library?

### Toast lifetime: success/info auto-dismiss, warning/error sticky
- **Choice:** Success/info fade after ~5 s; warning/error persist until dismissed.
- **Trade-off:** Sticky errors can pile up if many occur, but failures are never missed.
- **Feedback:** Is ~5 s the right auto-dismiss interval?

### Validation: inline + one summary toast
- **Choice:** Keep inline field errors AND raise one "corrige los campos marcados" summary toast.
- **Trade-off:** Slight redundancy, but preserves error-to-field proximity (and the constitution's "show all validation at once" gate) while improving discoverability.

## Areas of Potential Disagreement

### All ~16 confirm() sites → modal, including light ones (archive, unassign)
- **Decision:** Every current `confirm()` call site adopts the modal, not just hard-destructive ones.
- **Why this might be controversial:** Some confirmations are low-stakes; a heavier modal could feel like friction vs. the lightweight native prompt.
- **Alternative view:** Reserve the modal for hard-destructive actions, leave light ones inline/native.
- **Seeking input on:** Comfortable applying the modal uniformly to all current confirm() sites?

### Toast position top-right
- **Decision:** Toasts appear top-right.
- **Why this might be controversial:** Some apps prefer bottom-right/bottom-center; top-right can collide with header actions on narrow viewports.
- **Alternative view:** Bottom-right or bottom-center.
- **Seeking input on:** Confirm top-right, or prefer another corner?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Feature dir | `024-toast-confirm-dialogs` | Spec directory |
| Toast variants | success / error / warning / info | Four variants (FR-001) |
| Validation summary copy | "Corrige los campos marcados" | es-CR default (FR-008) |

## Open Questions

- [ ] None blocking. (Toast position and warning-variant inclusion were resolved during brainstorming: top-right; include warning now.)

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Missing a `confirm()`/`alert()` call site → inconsistent behavior persists | Medium | Plan produces an exhaustive call-site coverage matrix; SC-001 verified by static grep |
| E2E selector churn from removing banner blocks / changing dialogs | Medium | UI quality > selector stability (CLAUDE.md); rewrite affected E2E as part of the work |
| Accessibility regressions (focus trap, live region) | Medium | FR-012/FR-013 + SC-006 explicitly require and test a11y behavior |
| Destructive action firing without guard if JS fails | High | NFR-004 native `confirm()` fallback |

---
*Share with reviewers before implementation.*
