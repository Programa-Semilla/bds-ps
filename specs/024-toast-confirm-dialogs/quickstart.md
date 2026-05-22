# Quickstart: Notifications & Confirmation Dialogs

**Feature:** 024-toast-confirm-dialogs

How to use the unified notification + confirmation system as a developer on this codebase.

## Show a result message after a server action (most common)

In a controller, after a POST, set TempData and redirect (post-redirect-get) — exactly as today. The layout bridge turns it into a toast automatically.

```csharp
TempData["SuccessMessage"] = "Solicitud enviada correctamente.";
return RedirectToAction(nameof(Details), new { id });
// → top-right success toast, auto-dismisses after 5 s.

TempData["ErrorMessage"] = "No se pudo enviar la solicitud.";
return RedirectToAction(nameof(Edit), new { id });
// → top-right error toast, sticky until dismissed.
```

No view changes needed — `_Layout`/`_AuthLayout` render the toast.

## Show a message from client-side JS (AJAX)

```js
// After notifications.js has loaded:
window.Notify.error('Error desconocido al encolar.');
window.Notify.success('Comparación encolada.');
window.Notify.toast({ variant: 'warning', message: '…', sticky: true });
```

## Guard a destructive action with the confirmation modal

### Option 1 — page-header action (existing component path)
Use `_ActionBar` with an `ActionItem(Class: ActionClass.Destructive, ConfirmDialogId: "...")` plus a `_ConfirmDialog`. `_ActionBar` enforces that destructive actions carry a `ConfirmDialogId`. (See `Application/Details.cshtml` for a worked example.)

### Option 2 — inline / row action (attribute path)
Add `data-confirm` to the existing submit button or form; keep the form intact:

```html
<form asp-action="DeleteItem" asp-route-id="@item.Id" method="post" class="d-inline">
  @Html.AntiForgeryToken()
  <button type="submit"
          data-confirm
          data-confirm-title="Eliminar ítem"
          data-confirm-body="¿Está seguro de que desea eliminar este ítem?"
          data-confirm-variant="destructive"
          data-confirm-label="Eliminar">
    Eliminar
  </button>
</form>
```

`confirm-dialog.js` intercepts the submit, opens the shared styled modal, and submits the form only on confirm. If JS fails to load, a native `confirm()` fallback still guards the action.

## Validation

Keep `asp-validation-for` / `asp-validation-summary` inline as usual. When a form re-renders with an invalid `ModelState`, a single "Corrige los campos marcados" error toast is raised automatically — no extra code per form.

## Run the tests

```bash
dotnet test tests/FundingPlatform.Tests.E2E   # toast + confirm-modal coverage (full suite must be green — SC-007)
```

## Don't

- Don't add `window.alert` / `window.confirm` directly — use `Notify.*` or `data-confirm`.
- Don't add a top-of-page `.alert` banner for results — use TempData (it becomes a toast).
- Don't introduce a toast/dialog library — reuse vendored Bootstrap/Tabler (NFR-001).
