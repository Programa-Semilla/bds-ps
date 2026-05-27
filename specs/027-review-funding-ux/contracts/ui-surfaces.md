# Contract: UI surface changes (US1, US2, US3, US5, US6, US7, US8)

Internal UI contracts — markup hooks, routes, and partials. (US4 has its own contract.)

## US1 — generator name (display)
- `_FundingAgreementPanel.cshtml:33` renders `por @Model.GeneratedByDisplayName`.
- `GeneratedByDisplayName` MUST be the resolved display name. Fix the two producers to call `IUserStoreReader.GetDisplayNameAsync(GeneratedByUserId)`:
  - `SignedUploadService.cs:154`
  - `FundingAgreementService.cs:70`
- Contract: never emit a GUID; fallback ladder name → email → stable label.

## US2 — confirm signed-convenio actions
Add to the two submit buttons in `_FundingAgreementPanel.cshtml:127-151` (no JS change):
```html
<!-- Aprobar / ejecutar -->
<button type="submit" data-testid="signed-upload-approve"
        data-confirm data-confirm-variant="statelocking"
        data-confirm-title="Ejecutar convenio"
        data-confirm-body="Esto ejecuta el convenio."
        data-confirm-label="Ejecutar">Aprobar</button>

<!-- Rechazar -->
<button type="submit" data-testid="signed-upload-reject"
        data-confirm data-confirm-variant="destructive"
        data-confirm-title="Rechazar carga"
        data-confirm-body="Esto rechaza la carga; el solicitante podrá enviar otra."
        data-confirm-label="Rechazar">Rechazar</button>
```
- Confirm flows to `form.requestSubmit(trigger)`; existing actions (`FundingAgreementController.Approve :459`, `Reject :486`) unchanged.
- Mandatory reject comment stays; server-side enforcement is the backstop. Plan task: verify the comment's HTML5 `required` still blocks before/at confirm; if the confirm intercepts first, gate confirm on a non-empty comment.

## US3 — applicant detail block on the FA page
- `FundingAgreementController.Details` / `BuildDocumentViewModelAsync` populates an applicant block: company, representative name, legal id + type (spec 026 formatting), email, phone, `CodigoPersonal` (US5), group (spec 016), submission date.
- Rendered on `FundingAgreement/Details.cshtml`. Missing optional fields → neutral placeholder "—".
- PDF document body unchanged (FR-009).

## US5 — reviewer-assigned applicant code
- New POST on `ReviewController`, e.g. `POST /Review/{id:int}/ApplicantCode`, `[Authorize(Roles="Reviewer,Admin")]`, `[ValidateAntiForgeryToken]`, body `{ string? CodigoPersonal }`.
- MUST verify the reviewer's group-overlap authorization for the application (mirror the existing review-screen predicate, spec 016).
- Resolves applicant user via `application.Applicant.UserId`; sets `CodigoPersonal` (≤40 chars) via `UserManager<ApplicationUser>.FindByIdAsync` + `UpdateAsync`.
- Field rendered on `Review/Review.cshtml` (input "Código del solicitante", required-marked per US6). Read-only on `Account/Profile.cshtml` (unchanged).
- es-CR success TempData (e.g. "Código del solicitante actualizado.").

## US6 — required-field marker partial
- New `Views/Shared/_RequiredMark.cshtml`:
```html
<span class="text-danger" aria-label="campo obligatorio">*</span>
```
- Consumed `@await Html.PartialAsync("_RequiredMark")` after each required field label across the form inventory (research D5). Replace ad-hoc markers; add where only HTML5 `required` exists. Optional fields: no marker.

## US7 — HTML field tooltips
- `Views/Shared/_HintTooltip.cshtml` extended: render `<span class="form-hint-icon" data-hint="…html…" tabindex="0"><i class="ti ti-info-circle"></i></span>` beside the field; `ResolveCopy()` reads from a static es-CR copy provider (`HintCopy`) keyed by the `[Hint]` resource key (no `IStringLocalizer`).
- New `wwwroot/js/hint-tooltip.js` (registered in `_Layout.cshtml` after `confirm-dialog.js`): on `mouseover`/`focus` of `[data-hint]`, show an HTML bubble (`@Html.Raw`-safe, curated copy); hide on `mouseout`/`blur`. Own JS — no `window.bootstrap`.
- Applied to applicant-facing fields (research D6 field set). Copy authored es-CR (first pass by Claude; stakeholder refines). A field with no copy renders no icon.

## US8 — sidebar regroup
- See `sidebar-structure.md` for the full before→after table and the Proceso section header. `_Layout.cshtml` data block + render loop only; reuse existing `nav-item-section-header` / `fl-sidebar-section-header` CSS; add `data-section-testid="proceso-section"`. Zero removals; `AllowedRoles` preserved; supplier-admin-only variant preserved.
