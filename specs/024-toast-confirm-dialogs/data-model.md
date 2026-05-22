# Data Model: Consistent In-App Notifications & Confirmation Dialogs

**Feature:** 024-toast-confirm-dialogs · **Date:** 2026-05-22

> This feature is **presentation-only**. There are **no persisted entities**, no EF/dacpac changes, and no controller-contract changes. The "model" here is the in-memory/transport shapes the UI layer uses.

## 1. Server message channel (existing — unchanged contract)

Controllers continue to set these `TempData` keys exactly as today; only their *rendering* changes (banner → toast).

| TempData key | Type | Toast variant | Lifetime | testid on toast |
|--------------|------|---------------|----------|-----------------|
| `SuccessMessage` | `string` | success | auto (5 s) | `success-banner` |
| `ErrorMessage` | `string` | error | sticky | `error-banner` |
| `FundingAgreementSuccess` | `string` | success | auto (5 s) | `funding-agreement-success` |
| `FundingAgreementError` | `string` | error | sticky | `funding-agreement-error` |
| `ValidationErrors` | `string` (JSON `List<string>`) | error | sticky | `validation-summary-toast` |

Notes: empty/missing key → no toast (FR; edge case). Multiple keys present in one request → multiple stacked toasts (FR-005). TempData one-read semantics give "exactly once" (FR-011).

## 2. Toast (client transient object — not persisted)

Built by `window.Notify.toast(options)` or server-rendered into the toast container.

| Field | Values | Meaning |
|-------|--------|---------|
| `variant` | `success` \| `error` \| `warning` \| `info` | Visual style + live-region politeness |
| `message` | string (es-CR) | Body text; wraps gracefully |
| `sticky` | bool (derived) | `success`/`info` → false (autohide 5 s); `warning`/`error` → true |

Derived presentation: variant → Bootstrap utility class (`text-bg-success`/`-danger`/`-warning`/`-info`); variant → `aria-live` (`polite` for success/info, `assertive` for warning/error) and `role` (`status`/`alert`).

## 3. Confirmation request

### 3a. Existing component path — `ConfirmDialogViewModel` (reused)
`Models/ConfirmDialogViewModel.cs` (record): `Id`, `Title`, `IrreversibilityRationale`, `ConfirmLabel`, `CancelLabel` (**default changed `"Cancel"` → `"Cancelar"`**), `ConfirmClass` (`ActionClass`), `FormController`, `FormAction`, `FormRouteValues`. Rendered by `Components/_ConfirmDialog.cshtml`; triggered by `_ActionBar` buttons (`data-bs-toggle="modal"`, `data-bs-target="#<Id>"`). Unchanged except the default-label fix. Used by the 11 views already on this pattern.

### 3b. New attribute path — `data-confirm-*` (for the 15 migrated stragglers)
A trigger element (submit button or form) carries:

| Attribute | Required | Default (es-CR) | Meaning |
|-----------|----------|-----------------|---------|
| `data-confirm` | yes | — | Marks the element for interception |
| `data-confirm-title` | no | "Confirmar acción" | Modal title |
| `data-confirm-body` | no | "¿Deseás continuar? Esta acción no se puede deshacer." | Rationale text |
| `data-confirm-variant` | no | `destructive` | `destructive` → `btn-danger`; maps to `ActionClass` styling |
| `data-confirm-label` | no | "Confirmar" | Confirm button label |
| `data-confirm-cancel` | no | "Cancelar" | Cancel button label |

Resolution: confirm → submit the originating form (or follow the link); cancel/Esc/close → abort, no side effect (FR-006). Only one shared modal instance exists (FR; "only one open at a time"). Existing localized strings (e.g. `AdminSuppliersResources.Verify_Confirm`) are passed into `data-confirm-body` so no copy regresses (FR-007).

## 4. State transitions

None. No entity state. Toast lifecycle (shown → auto-hidden | dismissed) and modal lifecycle (opened → confirmed | cancelled) are ephemeral UI states owned by Bootstrap components.
