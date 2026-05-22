# Code Review: Consistent In-App Notifications & Confirmation Dialogs

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md)
**Date:** 2026-05-22

---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the implementation, focusing on
> high-level questions that need human judgment.

**Changed files:** 2 new JS modules (`notifications.js`, `confirm-dialog.js`), 1 toast CSS block; 3 new Razor partials (`_ToastContainer`, `_NotificationToasts`, `Components/_SharedConfirmModal`); both shared layouts; 1 view-model default; ~10 views (banner removals + 15 `data-confirm` migrations); `comparison.js`; ~9 E2E files (POMs + tests) + 1 new E2E test class.

### Understanding the changes (8 min)

- Start with [`wwwroot/js/notifications.js`](../../src/FundingPlatform.Web/wwwroot/js/notifications.js) and [`wwwroot/js/confirm-dialog.js`](../../src/FundingPlatform.Web/wwwroot/js/confirm-dialog.js): these are the two behavioural cores. Both are deliberately **self-contained** — they reuse Bootstrap's *CSS* (`.toast`, `.modal`, `.modal-backdrop`, `.show`) but drive show/hide/dismiss/focus themselves.
- Then [`Views/Shared/_NotificationToasts.cshtml`](../../src/FundingPlatform.Web/Views/Shared/_NotificationToasts.cshtml): the server→toast bridge that every layout includes.
- Question: The biggest architectural call here is **not using `window.bootstrap`** (see "less certain" below). Given that constraint, is a hand-rolled show/hide for toast + modal the right call, or would you prefer wiring Bootstrap's data-API (`data-bs-toggle`/events) instead?

### Key decisions that need your eyes (12 min)

**Self-contained JS instead of `window.bootstrap`** ([`confirm-dialog.js`](../../src/FundingPlatform.Web/wwwroot/js/confirm-dialog.js), [`notifications.js`](../../src/FundingPlatform.Web/wwwroot/js/notifications.js))
Discovered during E2E that Tabler bundles Bootstrap's data-API but does **not** expose the `bootstrap` JS global, so `new bootstrap.Toast/Modal()` silently no-op'd. The fix drives the `.show` class + a manual `.modal-backdrop` ourselves.
- Question: Is hand-rolling acceptable, or should the project instead expose `window.bootstrap` (one-line Tabler config) and use the official JS API? The latter would also fix the silently-broken `site.js` tooltip init.

**Two confirmation mechanisms coexist** ([`Components/_SharedConfirmModal.cshtml`](../../src/FundingPlatform.Web/Views/Shared/Components/_SharedConfirmModal.cshtml) vs the pre-existing [`Components/_ConfirmDialog.cshtml`](../../src/FundingPlatform.Web/Views/Shared/Components/_ConfirmDialog.cshtml))
The 11 views already on `_ActionBar`/`_ConfirmDialog` are untouched; the 15 stragglers use the new `data-confirm-*` interceptor + shared modal.
- Question ([FR-006](spec.md#functional-requirements)): Accept two entry points sharing one visual style, or consolidate onto one path post-merge?

**Preserving `data-testid="success-banner"/"error-banner"` + `alert-*` marker classes** ([`_NotificationToasts.cshtml`](../../src/FundingPlatform.Web/Views/Shared/_NotificationToasts.cshtml))
Toasts carry the legacy banner test ids and bare `alert-success`/`alert-danger` marker classes (visually inert without an `.alert` base) so ~38 existing E2E selectors keep working.
- Question: Sensible compatibility shim, or misleading naming (a "banner" id on a toast) we should rename now?

**Native `confirm()` kept as the no-JS fallback** (every migrated trigger; [NFR-004](spec.md#non-functional-requirements))
- Question: This is why [SC-001](spec.md#measurable-outcomes) was reworded (see below). Comfortable that a literal grep still finds `confirm(` in markup, given it's the safety fallback?

### Areas where I'm less certain (5 min)

- [`notifications.js`](../../src/FundingPlatform.Web/wwwroot/js/notifications.js) auto-dismiss is a plain `setTimeout`; there's no pause-on-hover. Spec [FR-004](spec.md#functional-requirements) doesn't require it, but a user reading a 5s success toast can't extend it. Acceptable?
- [`_NotificationToasts.cshtml`](../../src/FundingPlatform.Web/Views/Shared/_NotificationToasts.cshtml) emits a `validation-summary-toast` whenever `ModelState` is invalid on any full-page render. I believe this only fires on POST re-renders, but a GET surface that seeds ModelState errors would also trip it. Worth a second look at whether any such surface exists.
- The `validation-summary-toast` and an explicit `ValidationErrors` list are de-duped (else-if), but if a future controller sets both, only the list shows. Intentional, but undocumented in code.

### Deviations and risks (5 min)

- **Spec evolution — [SC-001](spec.md#measurable-outcomes):** original "no `window.confirm` remains" contradicted [NFR-004](spec.md#non-functional-requirements). Reworded to "no native dialog in the JS-enabled path; native `confirm()` remains only as the no-JS fallback." Question: agree with the reconciliation, or should the fallback be dropped to honour the literal SC-001?
- **No deviations from [plan.md](plan.md)'s structure** otherwise — the toast layer, bridge, shared modal, and migration all landed where the plan placed them. The one addition the plan didn't name explicitly: the `alert-*` marker-class compatibility shim (decided at task time to avoid churning 38 selectors).
- Risk: success toasts auto-dismiss at ~5s; any future E2E that acts for >5s before asserting a success toast will flake. The new test re-searches to avoid this; reviewers writing new toast assertions should assert promptly.
