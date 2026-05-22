# Research: Consistent In-App Notifications & Confirmation Dialogs

**Feature:** 024-toast-confirm-dialogs · **Date:** 2026-05-22

This document resolves the technical unknowns for the spec and records the key design decisions, with the codebase findings that drive them.

## Current-state inventory (what we are replacing)

### Server-side result messages (banners → toasts)
- `_Layout.cshtml` (lines ~217–230): renders `TempData["SuccessMessage"]` and `["ErrorMessage"]` as top-of-`page-body` `.alert.alert-success`/`.alert-danger` blocks with `data-testid="success-banner"`/`"error-banner"`.
- `_AuthLayout.cshtml` (lines ~33–44): same two messages rendered as `.fl-alert[data-variant]` blocks, same `data-testid`s.
- `Applications/_FundingAgreementPanel.cshtml` (lines 15–25): `TempData["FundingAgreementError"]`/`["FundingAgreementSuccess"]` rendered as in-panel `.alert` blocks with `data-testid="funding-agreement-error"`/`"-success"`.
- `Application/Details.cshtml` (line ~73): `TempData["ValidationErrors"]` (JSON list) rendered as a "No se puede enviar la solicitud" `.alert-danger` list — a submit-blocking summary.
- Controller usage counts: `SuccessMessage` ×67, `ErrorMessage` ×52, `FundingAgreement*` ×15, `ValidationErrors` ×4. **Controller contract is unchanged** — only presentation changes.

### Client-side / AJAX (window.alert → toast API)
- `wwwroot/js/comparison.js` (line 182): `alert(payload.code || 'Error desconocido al encolar.')` on a failed "Generar todo" enqueue. Single site.

### Confirmation dialogs (native confirm() → styled modal)
15 native `confirm()` call sites (one extra match in `Groups/Edit.cshtml:50` is a code comment):

| # | File:line | Action | Form/trigger shape |
|---|-----------|--------|--------------------|
| 1 | Admin/Groups/Edit.cshtml:57 | Delete group (typed-name guard) | `onclick` on submit, reads `data-confirm-*` |
| 2 | Admin/Plantillas/Index.cshtml:93 | Archive plantilla | `onsubmit` form |
| 3 | Admin/Processes/Details.cshtml:101 | Unassign plantilla | `onsubmit` form |
| 4 | Admin/Processes/Details.cshtml:115 | Force unassign | `onsubmit` form |
| 5 | Admin/Processes/Details.cshtml:301 | Close process | `onsubmit` form |
| 6 | Admin/PublicLanding/Index.cshtml:80 | Delete Reglamento | `onsubmit` form |
| 7 | Admin/PublicLanding/Index.cshtml:138 | Delete cotización example | `onsubmit` form |
| 8 | Admin/Suppliers/Detail.cshtml:98 | Verify supplier (`AdminSuppliersResources.Verify_Confirm`) | `onsubmit` form |
| 9 | Admin/Users/ResetPassword.cshtml:40 | Reset password | `onclick` on submit |
| 10 | Admin/Users/Index.cshtml:181 | Disable user | `onsubmit` form |
| 11 | Applications/_FundingAgreementPanel.cshtml:63 | Overwrite agreement | `onsubmit` form |
| 12 | Applications/_FundingAgreementPanel.cshtml:121 | Withdraw pending upload | `onsubmit` form |
| 13 | Application/Edit.cshtml:232 | Delete item (row) | `onclick` on submit |
| 14 | Application/Edit.cshtml:330 | Delete quotation (row) | `onclick` on submit |
| 15 | Review/Review.cshtml:393 | Return application to applicant | `onclick` on submit |

## Decision 1: Confirmation modal — reuse + extend the existing `_ConfirmDialog`

**Decision:** Do NOT build a new confirm modal. The codebase already has a reusable, styled confirmation modal and an enforcement path:
- `Views/Shared/Components/_ConfirmDialog.cshtml` — Bootstrap `modal modal-blur fade` wrapping a POST form (`asp-controller`/`asp-action`/`asp-all-route-data` + antiforgery), `data-testid="confirm-dialog"`, title, `data-testid="confirm-rationale"` body, `data-testid="cancel-button"` (`data-bs-dismiss`), `data-testid="confirm-button"` (submit). Styled by `ActionClass`.
- `Models/ConfirmDialogViewModel.cs`, `Models/ActionClass.cs` (Primary/Secondary/Destructive/StateLocking), `Models/ActionItem.cs`.
- `Views/Shared/Components/_ActionBar.cshtml` — renders a Destructive/StateLocking action as a `data-bs-toggle="modal"` trigger and **throws** if `ConfirmDialogId` is missing (existing FR-010 invariant). Already adopted by 11 views (ImpactTemplates, ExchangeRates, Groups/Index, Plantillas/Index, Users/Index, Processes/Index, Application/Details, Application/Index, ApplicantDashboard, AccessDenied, _ActionBar).

**Migration approach for the 15 stragglers:** introduce ONE shared, attribute-driven confirm modal element rendered once in the layout, plus a small `confirm-dialog.js` interceptor:
- Any element carrying `data-confirm` (with optional `data-confirm-title`, `data-confirm-body`, `data-confirm-variant`, `data-confirm-label`, `data-confirm-cancel`) has its default action intercepted; the shared modal is populated and shown; on confirm the **originating form is submitted** (or link followed). This preserves each straggler's existing form/payload (hidden fields, route values) with a one-line markup change per site.
- The shared modal is styled identically to `_ConfirmDialog` (`fl-surface`, `btn-danger` for destructive) and carries `data-testid="confirm-dialog"`/`"confirm-button"`/`"cancel-button"` for E2E parity.
- **Native `confirm()` fallback (NFR-004):** the interceptor only suppresses the native dialog when it successfully wires the modal; if `confirm-dialog.js` fails to load, the element retains a `onsubmit`/`onclick` native-`confirm()` guard so destructive actions are never unguarded. Implemented by keeping a minimal inline fallback that the JS disables on successful init.

**Rationale:** Lowest churn for inline/row sites (delete item/quote, table actions) which don't fit the page-header `_ActionBar` pattern; a single visual modal satisfies "one reusable confirmation modal" (FR-006); existing 11 component-based dialogs keep working unchanged. **Alternatives rejected:** (a) migrate every straggler to per-action `_ConfirmDialog` components — heavy for row-level actions and requires threading extra form fields into each dialog's own form; (b) a dedicated dialog library — violates NFR-001 (no new dep).

**Also fix:** `ConfirmDialogViewModel.CancelLabel` defaults to English `"Cancel"` — change default to `"Cancelar"` (FR-010 / spec 012 es-CR), a latent gap surfaced here.

## Decision 2: Toasts — Bootstrap Toast (via Tabler) + thin wrapper, server-rendered bridge

**Decision:** Add a first-party toast layer on the already-vendored Bootstrap 5 (bundled in `tabler.min.js`; `window.bootstrap.Toast` is available — `site.js` already uses `window.bootstrap.Tooltip`).
- **Container:** one fixed `toast-container` region (top-right, `position-fixed top-0 end-0 p-3`) with `aria-live` rendered once per layout (`_Layout` + `_AuthLayout`), so toasts overlay without layout shift (NFR-003) and are announced (FR-013): `aria-live="polite"` region for success/info, `aria-live="assertive"` for warning/error (two regions, or per-toast `role="alert"`/`role="status"`).
- **JS API (`notifications.js`):** `window.Notify.toast({ variant, message, sticky })` building a Bootstrap `.toast` and calling `bootstrap.Toast`. success/info → `autohide:true, delay:5000` (FR-004); warning/error → `autohide:false` (sticky). Stacking is native to the container (FR-005). Manual dismiss via `.btn-close` `data-bs-dismiss="toast"`.
- **Variant→style map:** success→`text-bg-success`, error→`text-bg-danger`, warning→`text-bg-warning`, info→`text-bg-info` (Tabler/Bootstrap utility classes).

**TempData → toast bridge (server-rendered):** replace the banner `.alert` blocks in `_Layout`/`_AuthLayout` (and the FA-panel + ValidationErrors blocks) with toast markup rendered server-side inside the container when the corresponding TempData key is present, then shown on load by `notifications.js`. Server-rendering (vs. a JSON data-island built client-side) keeps the toast element present in the initial DOM, which is more robust for E2E and degrades better.

**E2E selector preservation:** the success/error toast elements MUST keep `data-testid="success-banner"` and `data-testid="error-banner"` (≈29 E2E files assert these). The FA toasts keep `data-testid="funding-agreement-success"`/`"-error"`. The single class-based assertion (`AuthenticatedTestBase.cs:313` → `.alert-success`) is updated to the toast testid (or the toast carries a compat class) — decided at task time; preference: keep testids, update that one class assertion.

**Rationale:** No new dependency (NFR-001); reuses the exact pattern `site.js` already established for Bootstrap components; preserves the controller TempData contract and most E2E selectors. **Alternatives rejected:** client-side JSON data-island (worse no-JS/E2E behavior); a toast library (NFR-001).

## Decision 3: Validation summary toast (US4 / FR-008)

**Decision:** Keep inline field validation (`asp-validation-for` / `asp-validation-summary`) exactly as-is. Additionally, when a full-page form re-render occurs with an invalid `ModelState`, emit exactly one error toast ("Corrige los campos marcados"). Implement via a shared check in the layout (or a `_ValidationSummaryToast` partial pulled into the layout) that inspects `ViewContext.ViewData.ModelState.IsValid`; emit the toast only when invalid. The existing `Application/Details.cshtml` `ValidationErrors` submit-blocking list becomes a sticky error toast carrying the same messages.

**Rationale:** Preserves error-to-field proximity and the constitution's "show all validation at once" gate while adding the discoverability cue. **Alternative rejected:** converting validation summaries fully to toasts (detaches errors from fields).

## Decision 4: Asset budget (NFR-002)

`scripts/verify-asset-budget.sh` only sums fonts + illustrations + canvas-confetti + brand SVGs (≤400 KB gz) — it does **not** scan `wwwroot/js` or `wwwroot/css`. The new `notifications.js` + `confirm-dialog.js` + toast CSS therefore do not affect that gate. We still keep additions minimal (target: a few KB unminified each, no new libraries). Confirm `scripts/asset-budget-check.sh` has the same scope at task time.

## Decision 5: Testing approach (Constitution III)

- New Playwright E2E coverage (Page Object Model): toast appearance + auto-dismiss vs. sticky across an applicant page, a reviewer page, an admin page, and an auth page (SC-003); confirm-modal confirm/cancel for a representative subset of the 15 migrated sites (SC-004); inline-errors + summary toast (SC-005); a11y assertions for live region + focus return (SC-006).
- Reuse existing `data-testid` selectors (`success-banner`, `error-banner`, `confirm-dialog`, `confirm-button`, `cancel-button`) so most existing assertions survive; rewrite only what genuinely changes (UI quality > selector stability).
- Full E2E suite must be personally executed and green before delivery (SC-007).

## Open items deferred to tasks/implementation

- Exact split of the 15 sites between "shared attribute interceptor" and (where a header `_ActionBar` already exists on the page) the component path.
- Whether to render one combined aria-live region with per-toast roles, or two regions (polite/assertive). Default: per-toast `role` + a single polite container, with assertive toasts setting `aria-live="assertive"` on themselves.
- Final toast auto-dismiss delay (default 5000 ms) and max-width/wrap CSS.
