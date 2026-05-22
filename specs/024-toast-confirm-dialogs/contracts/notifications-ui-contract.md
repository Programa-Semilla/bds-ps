# UI Contract: Notifications & Confirmation Dialogs

**Feature:** 024-toast-confirm-dialogs · **Date:** 2026-05-22

The "interfaces" this feature exposes are UI/DOM contracts (this is a server-rendered MVC app, not an API). They are the stable surface that views, scripts, and E2E tests depend on.

## A. Toast JS API

```js
// Global, available after notifications.js loads.
window.Notify.toast({
  variant: 'success' | 'error' | 'warning' | 'info', // required
  message: 'string (es-CR)',                          // required
  sticky:  true | false                               // optional; default derived from variant
});
// Convenience shorthands:
window.Notify.success(message);  // variant:'success'
window.Notify.error(message);    // variant:'error'
window.Notify.warning(message);
window.Notify.info(message);
```

Behavior:
- `success`/`info` → auto-dismiss after 5000 ms (`autohide:true`). `warning`/`error` → sticky (`autohide:false`) until user dismisses (FR-004).
- Each call appends a new toast to the container; toasts stack newest-near-top, each independently dismissible (FR-005).
- No-op safe: if `window.bootstrap.Toast` is unavailable, fail silently without throwing.

## B. Toast DOM contract

```html
<!-- Rendered once per layout (_ToastContainer.cshtml) -->
<div class="toast-container position-fixed top-0 end-0 p-3" data-testid="toast-container" style="z-index:1090">
  <!-- each toast: -->
  <div class="toast fl-toast text-bg-{success|danger|warning|info}"
       role="{status|alert}" aria-live="{polite|assertive}" aria-atomic="true"
       data-testid="{success-banner|error-banner|funding-agreement-success|funding-agreement-error|validation-summary-toast|toast}"
       data-toast-variant="{success|error|warning|info}">
    <div class="d-flex">
      <div class="toast-body">…message…</div>
      <button type="button" class="btn-close me-2 m-auto" data-bs-dismiss="toast" aria-label="Cerrar"></button>
    </div>
  </div>
</div>
```

Stable hooks for E2E (MUST be preserved):
- `data-testid="success-banner"` and `data-testid="error-banner"` — required (≈29 existing E2E files depend on them).
- `data-testid="funding-agreement-success"` / `"funding-agreement-error"` — preserved from the FA panel.
- `data-testid="toast-container"`, `data-testid="confirm-dialog"`, `data-testid="confirm-button"`, `data-testid="cancel-button"` — for the new tests.
- Variant→class: success→`text-bg-success`, error→`text-bg-danger`, warning→`text-bg-warning`, info→`text-bg-info`.
- aria-live: `polite`+`role=status` for success/info; `assertive`+`role=alert` for warning/error (FR-013).

## C. TempData → toast mapping (server bridge)

`_NotificationToasts.cshtml` (included by both layouts and emitting into the container) renders a toast for each present key. The FA panel and `Application/Details` ValidationErrors route through the same bridge instead of in-place `.alert` blocks.

| Source | Variant | Sticky | testid |
|--------|---------|--------|--------|
| `TempData["SuccessMessage"]` | success | no | `success-banner` |
| `TempData["ErrorMessage"]` | error | yes | `error-banner` |
| `TempData["FundingAgreementSuccess"]` | success | no | `funding-agreement-success` |
| `TempData["FundingAgreementError"]` | error | yes | `funding-agreement-error` |
| `TempData["ValidationErrors"]` (JSON list) | error | yes | `validation-summary-toast` |
| ModelState invalid on form re-render | error | yes | `validation-summary-toast` (single, "Corrige los campos marcados") |

Server-rendered toasts appear in the initial DOM and are shown on load by `notifications.js` (robust for no-JS/E2E). Each renders once per request (FR-011).

## D. Confirmation: `data-confirm-*` interceptor contract

```html
<!-- Straggler migration: add data-confirm to the existing submit/button; keep the existing form intact. -->
<form asp-action="DisableUser" method="post">
  @Html.AntiForgeryToken()
  <button type="submit"
          data-confirm
          data-confirm-title="Inhabilitar usuario"
          data-confirm-body="¿Inhabilitar a usuario@dominio.test? Esto cierra la sesión del usuario."
          data-confirm-variant="destructive"
          data-confirm-label="Inhabilitar"
          data-confirm-cancel="Cancelar"
          onsubmit="return window.__nativeConfirmFallback(this)">  <!-- fallback only -->
    Inhabilitar
  </button>
</form>
```

`confirm-dialog.js`:
1. On load, finds `[data-confirm]` elements, removes/neutralizes the native-`confirm()` fallback, and binds an interceptor.
2. On activate: prevents the default submit/navigation, populates `_SharedConfirmModal` from the `data-confirm-*` attributes, opens it (`bootstrap.Modal`).
3. Confirm → submit the originating form (or follow the link). Cancel/Esc/close → abort (FR-006), focus returns to trigger (FR-012).
4. **Fallback (NFR-004):** if `confirm-dialog.js` does not load/init, the inline native-`confirm()` guard remains active so the destructive action is never unguarded.

Shared modal markup mirrors `_ConfirmDialog` (`modal modal-blur fade`, `fl-surface`, `data-testid="confirm-dialog"`, `confirm-button`/`cancel-button`), `btn-danger` confirm for `destructive`. Only one shared modal instance exists; it is reused per activation (only one open at a time).

The existing component path (`_ConfirmDialog` + `_ActionBar`, 11 views) is unchanged and visually identical; `ConfirmDialogViewModel.CancelLabel` default is corrected to `"Cancelar"`.

## E. Accessibility contract (FR-012/FR-013, SC-006)

- Toasts announced via `aria-live` (polite success/info, assertive warning/error).
- Confirm modal: `role` dialog semantics from Bootstrap modal, focus trapped while open, `Esc` cancels, focus returns to the triggering element on close.
- Toast container overlays with fixed positioning — no layout shift (NFR-003); page beneath stays interactive.
