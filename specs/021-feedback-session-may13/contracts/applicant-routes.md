# Applicant + Reviewer Routes — Contracts

**Feature**: 021-feedback-session-may13 | **Surface**: ASP.NET MVC

## Applicant — Application lifecycle

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Applications` | `Index` | Applicant | Lists applicant's own Applications by `PublicCode`. Excludes soft-deleted (FR-021). Greeting *"Hola, {{Nombre}}"*. |
| GET | `/Applications/Create` | `Create` | Applicant | Routes to Impact step first (FR-005). |
| POST | `/Applications/Create` | `Create` | Applicant | Creates draft with `Impact` set; redirects to draft editor at `/Applications/{publicCode}/Edit`. |
| GET | `/Applications/{publicCode}/Edit` | `Edit` | Applicant (owner) | Draft form: Impact-summary card → Items → Quotations → Suppliers. Required fields visibly marked (FR-015). Stage countdown banner at top (FR-024). |
| POST | `/api/applications/{publicCode}/autosave` | `Autosave` | Applicant (owner) | Body: `{ fieldKey, value, etag }`. 200 returns `{ etag, savedAt }`; 409 on stale etag; 422 if stage window closed (`StageWindowClosedException` → `DomainExceptionFilter`). |
| POST | `/Applications/{publicCode}/AddItem` | `AddItem` | Applicant (owner) | Inline add — no page navigation. |
| POST | `/Applications/{publicCode}/AddQuotation/{itemId}` | `AddQuotation` | Applicant (owner) | Includes supplier search / create-branch flow inline. |
| GET | `/Applications/{publicCode}/Review` | `Review` | Applicant (owner) | Read-only `/review` page: items, suppliers, totals (CRC + FX disclaimer), Impact. Submit-time validation listed inline; *"Confirmar y enviar"* button enabled only when all FR-017 conditions met. |
| POST | `/Applications/{publicCode}/Submit` | `Submit` | Applicant (owner) | 422 with enumerated missing required fields if any; 302 → Index on success. PublicCode shown in success banner. |

## Applicant — Profile

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Profile` | `Profile` | Authenticated | Editable: FirstName, LastName, Phone, Address. Read-only with *"administrado"* badge: Email, Role, Group, CodigoPersonal. |
| POST | `/Profile/Update` | `Update` | Authenticated | 422 on validation; 200 with toast on success. |
| POST | `/Profile/ChangePassword` | `ChangePassword` | Authenticated | Strength legend ticks live (FR-027). Eye toggle on every password input (FR-026). |

## Reviewer

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Reviewer/Queue` | `Queue` | Reviewer | Group-overlap predicate (spec 016) extended to Process scope. Lists Applications by PublicCode. Per-row countdown banner. |
| GET | `/Reviewer/Dashboard` | `Dashboard` | Reviewer | Hosts the *Cotizaciones pendientes* tile (moved from admin per FR-033) with count + drill-in link. |
| GET | `/Reviewer/Application/{publicCode}` | `Application` | Reviewer (group-overlap) | Detail view, banner countdown, review actions. |

## Applicant — supplier search (embedded)

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/api/applications/suppliers/search?q={term}` | `Search` | Applicant | Autocomplete on Name + CédulaJurídica from applicant's allowed catalogue. |
| POST | `/api/applications/suppliers/create-branch` | `CreateBranch` | Applicant | Inline new SupplierBranch with Province → Cantón cascade + ContactPersonName. |

## Submit-gating predicate (FR-017)

```pseudo
canSubmit(application) :=
  application.Impact != null
  AND application.Items.Count >= 1
  AND application.Items.All(item =>
      item.Quotations.Count >= application.Process.ProcessPlantilla.MinimumQuotationsPerItem)
  AND application.RequiredFieldFlags.All(flag => application.HasValueFor(flag))
  AND application.AutosaveBannerState != Failed
  AND application.StageWindow.IsOpen()
```

If any clause fails, submit button is `disabled` with a tooltip listing failures by name.

## Stage countdown banner contract

Renders on: applicant draft, reviewer queue row, signing inbox row.

- **State**: open → time remaining `{{d}}d {{h}}h`; danger styling when `< 24h`.
- **State**: closed → red banner *"Vencido — la etapa cerró el {{fecha}}"*; underlying mutation endpoints return 422.

## FX disclaimer placement (FR-022)

Renders below any CRC-converted USD total on: `/Applications/{publicCode}/Edit` item totals, `/Applications/{publicCode}/Review` totals, Funding Agreement PDF totals. String key: `Application.Disclaimer.Fx`.
