# Brainstorm: Consistent In-App Notifications & Confirmation Dialogs

**Date:** 2026-05-22
**Status:** spec-created
**Spec:** specs/024-toast-confirm-dialogs/

## Problem Framing

Result and confirmation messaging is inconsistent across the platform and across roles. Three different mechanisms coexist:

- **Server outcomes** → fixed banner `.alert` blocks pinned to the top of the page body, carried via `TempData["SuccessMessage"]/["ErrorMessage"]` (≈120 uses) plus `FundingAgreementSuccess/Error` and `ValidationErrors`, rendered in `_Layout.cshtml` and `_AuthLayout.cshtml`.
- **Client/AJAX outcomes** → native `window.alert` (e.g. `comparison.js`).
- **Destructive-action confirmations** → native browser `confirm()` in ~16 view sites.

Look, placement, dismissal, and accessibility differ per page and role. Goal: one consistent system — toasts for operation results, styled modal dialogs for confirmations — across every page and every role.

## Approaches Considered

### A: Reuse vendored Bootstrap 5 / Tabler Toast + Modal + thin wrapper (CHOSEN)
- Pros: Zero new dependency, no CDN (fits CLAUDE.md "reuse vendored" posture + asset budget); Toast + Modal already shipped with Tabler; consistent with ASP.NET MVC server-rendered stack; small first-party wrapper + a TempData→toast bridge in the layout covers both server and AJAX paths.
- Cons: Less out-of-box polish than a dedicated library; some wrapper code to own and test.

### B: Vendor a dedicated toast/dialog library (Toastify / SweetAlert2)
- Pros: More polish out of the box.
- Cons: New managed/vendored dependency requiring spec approval; added bundle weight; redundant with capabilities already present in Tabler/Bootstrap.

### C: Fully custom toast + modal on existing `fl-*` design tokens
- Pros: Maximum brand fit and control.
- Cons: Most code to build/own/test; reinvents primitives already vendored; slower.

## Decision

Chose **A**. Build on the vendored Bootstrap 5 / Tabler Toast + Modal with a thin first-party JS wrapper (`toast()` API) and a layout-level TempData→toast bridge, plus one reusable confirmation modal.

Shaping decisions locked during the session:

- **Confirmation scope:** All ~16 current `confirm()` sites + any destructive/irreversible action adopt the styled modal.
- **Toast lifetime:** success/info auto-dismiss (~5 s); warning/error sticky until dismissed.
- **Validation:** inline field errors stay inline AND a single summary toast is raised on failed submit.
- **Toast position:** top-right of the viewport.
- **Variants:** success / error / warning / info — `warning` included now (not deferred).
- **Graceful degradation:** native `confirm()` fallback if the wrapper JS fails to load, so destructive actions are never unguarded.
- **Scope guard:** persistent status UI (autosave indicator, stage-countdown banner, supplier lookup-rejected notice) is unchanged; no schema change; controllers' TempData/PRG contract unchanged (presentation-only change).

Spec reviewed (`REVIEW-SPEC.md`): **SOUND**, no critical/important issues, no constitution violations.

## Open Threads

- Exact enumeration of all `confirm()` call sites and TempData message surfaces into a coverage matrix so SC-001/SC-002 are mechanically verifiable — pin during `/speckit-plan`.
- Whether to introduce small toast/confirm tag-helpers or partials to keep call sites DRY — decide during planning.
- Confirm the ~5 s auto-dismiss interval for success/info toasts — tune during planning.
- Confirm top-right placement reads well on narrow viewports vs. header actions — verify during the UI pass.
