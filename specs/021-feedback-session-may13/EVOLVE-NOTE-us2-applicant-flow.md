# EVOLVE NOTE: US2 applicant draft flow — UI never implemented

**Date**: 2026-05-16
**Spec**: 021-feedback-session-may13 / US2 / FR-005, FR-016, FR-017
**Trigger**: Debugging session — "the applicant is not going through the
described flow as expected, and the impact still resides at the item level;
disabling the button does not work; nothing on the US was actually addressed."
**Resolution**: Option B — Fix Code. Spec is correct and authoritative.

---

## Summary

US2 commit `b1c1ae2` ("impl(021): US2 applicant flow") migrated the **domain**
(`Impact` moved off `Item` onto the `Application` aggregate) but never rebuilt
the **applicant UI flow**. `Application/Edit.cshtml` is the pre-021 per-item
item-list page with three cosmetic additions whose comments claim "US2's
contract is satisfied" — it is not. The US2 E2E was hollowed out to pass.

### What is correct (domain layer) — no change needed

| Element | State |
|---|---|
| `Item` entity / `dbo.Items` | no `Impact` column — relocated ✅ |
| `Application.SetImpact` + `ImpactTemplateId` + `ImpactParameterValues` | ✅ |
| `ApplicationService.SetItemImpactAsync` | writes through to `application.SetImpact` (`ApplicationService.cs:553`) ✅ |
| `Application.Submit` / `Validate` submit guards (impact set, ≥1 item, min quotations) | `Application.cs:326-393` ✅ |
| `GetApplicationReviewProjection` + `Views/Application/Review.cshtml` | renders Impact / Items / Totals / FX disclaimer / "Confirmar y enviar"; `CanSubmit` computed server-side ✅ |

---

## Mismatch 1: Impact presented and routed at the Item level (MAJOR — FR-005)

**Spec requires:**
- **FR-005**: "System MUST relocate `Impact` from `Item` to `Application`.
  Application creation MUST capture Impact (one `ImpactTemplate`) upfront, then
  accept items inline without leaving the screen."
- **Edge case**: "Empty Impact on Application … Impact step is first in the flow."
- **US2 Independent Test**: "completes draft creation → impact → items →
  quotations → submit → review → confirm."

**Code does:**

| Layer | State | Verdict |
|---|---|---|
| Impact route | `GET/POST Application/{appId}/Item/{id}/Impact` — requires an **Item id** | ❌ |
| Impact view | `Views/Item/Impact.cshtml` — title `"Evaluación de impacto - {ItemProductName}"`, breadcrumb `"Ítem: {ProductName}"` | ❌ |
| `ImpactViewModel` | carries `ItemId` + `ItemProductName` — item-shaped | ❌ |
| `Edit.cshtml` Impact card | gated: `Model.Items.Count == 0` → no link, only text *"Agregue al menos un ítem para iniciar la captura del impacto"* — **items required before impact** | ❌ |
| `Edit.cshtml:107-113` Impact link | links to `Item/Impact` passing `firstItem.Id` — abuses the per-item route | ❌ |

The flow is **inverted**: spec says impact-first then items; code requires
≥1 item before impact is even reachable, and reaches it through an item id.
The applicant correctly perceives impact as an item-level concept because the
UI literally presents it that way. `SetItemImpactAsync` keeps a vestigial
`ItemId` parameter (`ApplicationService.cs:552` `_ = item;`).

**Type**: Behavioral + UX. **Severity**: Major — breaks the primary user
journey and FR-005's explicit ordering.

## Mismatch 2: Items added on a separate full page, not inline (MAJOR — FR-005)

**Spec requires:** "accept items inline without leaving the screen."

**Code does:** `Edit.cshtml:121` "Agregar ítem" is an `<a>` to
`GET Application/{appId}/Item/Add` (`ItemController.Add` → `return View`) — a
separate full-page form; POST redirects to `Application/Details`. The applicant
leaves the draft editor for every item.

## Mismatch 3: Submit gating not implemented (MAJOR — FR-017)

**Spec requires:** "Submit MUST be disabled until the Application has ≥ 1 Item,
Impact defined, and every required field complete." US2 Acceptance #1: "the
submit button … is disabled with a tooltip listing what is missing."

**Code does:** `Edit.cshtml:207-213` — submit is an `<a>` anchor,
`class="btn btn-success"`, `data-submit-gate="enabled"` (a hardcoded literal),
with a comment promising "disabled until JS enables it."

- **No submit-gate JS exists.** `wwwroot/js/` has no module reading
  `data-submit-gate`; `Edit.cshtml`'s `@section Scripts` loads none.
- An `<a>` ignores the `disabled` attribute regardless.
- The static `title="Revise antes de enviar"` enumerates nothing.

The gate is decorative. The anchor is always live. **FR-017 unimplemented.**

## Mismatch 4: US2 E2E hollowed out to tolerate Mismatches 1–3 (MAJOR — test integrity)

**Spec / T087**: E2E "drives the real user journey … draft → autosave → impact +
items + quotations → /review → 'Confirmar y enviar'."

**Code does:** `US2_ApplicantE2E.cs` never adds an item, never sets impact,
never opens `/review`, never clicks "Confirmar y enviar", never tests the gate.
It asserts the autosave indicator is merely *attached* (`:63`), then crawls for
forbidden strings. It uses `Page.GotoAsync` deep-links (`:39,57,69`) — directly
violating the project rule (and the comment at its own `:9-11`) that E2E must
drive the real journey via clicked links. The "224 green" baseline therefore
does **not** evidence US2 compliance.

---

## Resolution: Option B — Fix Code

Spec is correct; the US2 UI layer was never built. No `spec.md` change → no
`/speckit-plan` or `/speckit-tasks` regeneration. This note is the authoritative
record of the work that closes the gap.

### Design decisions (HOW — not spec changes)

- **Impact-first.** `Application/Create` POST redirects to a new
  **Application-level** Impact step (`GET Application/{id}/Impact`), not to
  `Details`. Impact is per-Application, never item-routed.
- **Inline items.** Item add/remove on the draft editor happen via `fetch` to
  JSON endpoints with client-side row append/remove — no navigation. The
  standalone `Item/Add` page is retained (other E2E suites drive it) but is no
  longer the US2 path.
- **Real gated submit.** The submit `<a>` becomes a `<button disabled>` whose
  click navigates to `/review`; `submit-gate.js` recomputes completeness on
  load + input and toggles `disabled` + a `title` enumerating what is missing.
- **Autosave scope.** `CompanyName` is the only free-text Application-level
  draft field; autosave on it satisfies FR-016 for the draft form. Item / impact
  capture happens through their own explicit save actions, not blur-autosave.

### Execution plan

1. **`ImpactViewModel`** — drop `ItemId` / `ItemProductName`; add `PublicCode`.
   New `Views/Application/Impact.cshtml` (Application-scoped copy of the impact
   picker; reuses the template-parameter AJAX from `Item/Impact.cshtml`).
2. **`ApplicationController`** — add `GET/POST Application/{id}/Impact`
   (Application-scoped). `POST` success → redirect to `Edit`. `Create` POST →
   redirect to `Impact` instead of `Details`.
3. **Retire** `ItemController.Impact` GET/POST + `Views/Item/Impact.cshtml`.
   Simplify `SetItemImpactCommand` → `SetApplicationImpactCommand` (drop
   `ItemId`); rename `SetItemImpactAsync` → `SetApplicationImpactAsync`.
4. **Inline items API** — `POST /api/applications/{publicCode}/items` (add) and
   `POST /api/applications/{publicCode}/items/{itemId}/delete`, returning JSON.
5. **`wwwroot/js/draft-items.js`** — reveal inline add form, `fetch` create,
   append table row; `fetch` delete, remove row.
6. **`wwwroot/js/submit-gate.js`** — read `data-item-count` / `data-impact-set`
   + required autosave fields; toggle the submit `<button disabled>` + `title`.
7. **`Views/Application/Edit.cshtml`** — rebuilt: (a) Impact summary card first
   with "Editar impacto" → Impact step; (b) inline Items card; (c) gated submit
   `<button>`. Load `draft-items.js` + `submit-gate.js`.
8. **`US2_ApplicantE2E.cs`** — rewritten to drive the full journey via clicked
   links; update `ApplicationDraftPage` / add an impact page object.
9. Build + unit + integration + **full Playwright E2E suite green** (NFR-004 /
   SC-016) before this work is considered delivered.

### Known deviation

Between `Create` and the first Impact save, the draft row has a transient null
`ImpactTemplateId`. The domain permits this (`ImpactTemplateId` is `int?`); the
UI never exposes an items surface until impact is set, and the submit guard
rejects null impact. Hardening the `Application` constructor to require impact
is deferred — out of scope for this fix.
