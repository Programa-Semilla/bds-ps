# Phase 0 Research: 027 Review & Funding-Agreement UX Refinements

Resolves the mechanism-level unknowns surfaced during planning. Each decision: what, why, alternatives. File:line anchors verified on branch `027-review-funding-ux` (2026-05-26).

---

## D1 — US1 generator name resolution

**Decision:** Resolve the generator's display name via the existing `IUserStoreReader.GetDisplayNameAsync` at the two DTO build sites (`SignedUploadService.cs:154`, `FundingAgreementService.cs:70`) instead of passing the raw `GeneratedByUserId`.

**Rationale:** The resolver already exists (`IUserStoreReader.cs:20`, impl `UserStoreReader.cs:32-51`) and already returns `FirstName LastName` → email → userId fallback, and is already used on the same page for commission members (`FundingAgreementController.cs:741-746`). Zero new infrastructure; fixes the bug at the source rather than masking in the view.

**Alternatives rejected:** Resolving in the Razor view (wrong layer, no DI of the reader into views); a DB join in the projection (the reader already encapsulates the fallback ladder).

---

## D2 — US2 confirm step (reuse spec 024)

**Decision:** Wire "Aprobar" and "Rechazar" on `_FundingAgreementPanel.cshtml:127-151` through the spec-024 confirm dialog by adding `data-confirm` attributes to the submit buttons. No JS changes.

**The data-attribute contract** (verbatim, `wwwroot/js/confirm-dialog.js:60-66`):
```
data-confirm                        (marker — routes the click through the modal)
data-confirm-title="…"              (default "Confirmar acción")
data-confirm-body="…"               (default "¿Deseás continuar? …")
data-confirm-label="…"              (default "Confirmar")
data-confirm-cancel="…"             (default "Cancelar")
data-confirm-variant="destructive|primary|secondary|statelocking"
```
On confirm the script calls `form.requestSubmit(trigger)` (`:130`), preserving the button's `formaction`/`formmethod` — so the existing Approve/Reject forms submit unchanged. Modal is hand-driven (`.show` + `modal-backdrop`, no `window.bootstrap`), `:68-81`.

**Wiring:**
- Approve button → `data-confirm-variant="statelocking"` (or primary), `data-confirm-title="Ejecutar convenio"`, `data-confirm-body="Esto ejecuta el convenio."`, `data-confirm-label="Ejecutar"`.
- Reject button → `data-confirm-variant="destructive"`, `data-confirm-title="Rechazar carga"`, `data-confirm-body="Esto rechaza la carga; el solicitante podrá enviar otra."`, `data-confirm-label="Rechazar"`. The mandatory comment textarea stays in the form and is validated server-side as today (`FundingAgreementController.cs:486-511`); HTML5 `required` still fires before the confirm because the browser validates on submit-intent. (Plan task: confirm the comment-required UX still triggers — if the confirm intercepts before validation, gate the confirm on a filled comment or keep server-side enforcement as the backstop.)

**Existing call sites to mirror:** `Views/Admin/Users/Index.cshtml` (disable user), `Views/Application/Edit.cshtml` (delete quotation).

**Alternatives rejected:** A new confirm mechanism (spec 024 is the sanctioned one); `window.confirm()` native (spec 024 explicitly replaced these).

---

## D3 — US4 shared decision-summary projection (CORE)

**Decision:** Introduce one focused, read-only Application-layer projection `DecisionSummaryLineDto` + one Web list partial `_DecisionSummary.cshtml`, consumed by the applicant decision screen, the funding-agreement Details page (which serves the generate / signing / signed-review stages across roles+states), and surfaced/aligned on the reviewer review screen. The reviewer's **interactive** decision controls (radios, supplier dropdown, AI comparison, scoring) stay as-is — the shared block is the consistent read-only summary, not a replacement for the capture UI.

**The line shape (exactly the spec's fields — deliberately lean):**
```
DecisionSummaryLineDto
  LineCode            string?
  ProductName         string
  CategoryName        string
  TechnicalSpecifications string
  ReviewStatus        ItemReviewStatus   (Pending | Approved | Rejected | NeedsInfo)
  ReviewComment       string?            (rejection/needs-info reason)
  ApprovedSupplierName string?           (approved only)
  ApprovedAmount      MoneyView?         (approved only — selected supplier's quote)
  Quotations          IReadOnlyList<DecisionSummaryQuotationView>   (ALL quotes; shown for rejected lines)
  ApplicantDecision   string?            (es-CR label when an applicant response exists)

DecisionSummaryQuotationView
  SupplierName        string
  Amount / Currency / ConvertedCrcAmount
  CurrencyConversionNote string?         (computed; null for CRC)
```
- Approved-line amount = the `SelectedSupplierId`'s quotation.
- Rejected-line = `ReviewComment` + the full `Quotations` list (supplier + amount each), since there is no single approved supplier.
- Pending-line = status only.
- Currency conversion note reuses the existing `BuildConversionNote` logic (`FundingAgreementController.cs:869-881`) / the spec-015 multi-currency display; lift it to the projection so all surfaces format identically.

**Data load:** All fields come from the already-eager-loaded aggregate — `ApplicationRepository.GetByIdWithDetailsAsync` (Items→Category, Items→Quotations→Supplier) and `GetByIdWithResponseAndAppealsAsync` (adds ApplicantResponses→ItemResponses). No new query, no new include for the lean shape. The projection is pure in-memory mapping + the conversion-note computation.

**Why lean (not the maximal DTO):** The first research pass proposed folding in AI comparison, scores, and impact parameters. Rejected per constitution VI (YAGNI) — the spec's US4 enumerates a fixed field set; scores/AI/impact are reviewer-capture-screen concerns already rendered there and would bloat the shared read block and couple it to spec 020.

**Surface mapping (5 conceptual stages → physical surfaces):**
| Stage | Physical surface | Change |
|---|---|---|
| 1 reviewer item review | `Review/Review.cshtml` (`ReviewController.Review`) | already rich; align labels/order; optionally render the shared block read-only alongside the capture UI |
| 2 applicant accept/reject | `ApplicantResponse/Index.cshtml` | render `_DecisionSummary`; **adds technical specs** (today's `ItemResponseDto` lacks it) |
| 3 reviewer generate | `FundingAgreement/Details.cshtml` preview (reviewer/pre-sign state) | render `_DecisionSummary` (today approved-only) |
| 4 applicant signing | `FundingAgreement/Details.cshtml` (applicant/signing state) | render `_DecisionSummary` |
| 5 reviewer signed review | `FundingAgreement/Details.cshtml` (post-upload state) + `Review/SigningInbox.cshtml` list | render `_DecisionSummary` on Details; inbox keeps its link to Details |

**Alternatives rejected:** Per-surface bespoke fixes (the drift this spec exists to kill); extending the PDF row DTOs (those feed the PDF, which is out of scope per spec 018 / FR-009).

---

## D4 — US5 write CodigoPersonal from the review screen

**Decision:** On the reviewer decision screen, add a "Código del solicitante" input that writes `ApplicationUser.CodigoPersonal` of the **applicant who owns the application**, via `UserManager<ApplicationUser>` (resolve user by `application.Applicant.UserId`). A dedicated POST action on `ReviewController` (separate from the per-item `ReviewItem` POST). Read-only on the applicant's profile stays as today.

**Rationale:** `UpdateProfileCommand` is self-service only and deliberately excludes `CodigoPersonal` (`UpdateProfileHandler.cs:12-13`) — it targets the current user, not another user, so it does not fit. The applicant linkage is `Application.Applicant.UserId` → `ApplicationUser.Id` (`Application.cs:91`, `Applicant.cs:11`). `UserManager.FindByIdAsync` + set + `UpdateAsync` is the existing write path for an admin/reviewer editing another user's identity fields. Column is `NVARCHAR(40)` — input is length-bounded to 40.

**Authorization:** the action is `[Authorize(Roles="Reviewer,Admin")]` and must verify the reviewer's group-overlap with the application (mirror the existing review-screen authorization predicate, spec 016).

**Alternatives rejected:** A new command/handler (overkill for a single scalar set; UserManager is the established path for admin-side user edits); putting the field on the admin user form only (spec requires it on the *first review screen*).

---

## D5 — US6 shared required-field marker

**Decision:** Create `Views/Shared/_RequiredMark.cshtml` (renders `<span class="text-danger" aria-label="campo obligatorio">*</span>`), consumed via `@await Html.PartialAsync("_RequiredMark")` immediately after each required field's label. Sweep all forms (applicant + admin + reviewer) to replace ad-hoc markers and add the marker where only HTML5 `required` exists.

**Rationale:** No TagHelpers folder exists; the codebase's established reuse unit is the `_*.cshtml` partial (`_LegalIdField`, `_LocationCascade`, `_QuoteFields`, `_HintTooltip`). A partial matches convention with zero new infrastructure and centralizes future tweaks. Current inconsistency is documented: asterisk-with-aria, asterisk-without-aria, `.form-label.required` CSS (`_LocationCascade.cshtml`), and HTML5-only.

**Alternatives rejected:** A custom label tag helper (new infra folder, heavier than the project's idiom); standardizing on the `.form-label.required` CSS pseudo-element (less explicit for screen readers than the aria-labeled span; not yet universal).

**Form inventory (sweep targets):** applicant — Register, ChangePassword, ForgotPassword, ResetPassword, Profile, Application/Edit, Application/Impact, Supplier/Add, ApplicantResponse/*; admin — Users/Create, Users/Edit, CreateTemplate, EditTemplate, ExchangeRates/Create, Plantillas/Create+Edit, Configuration, PublicLanding; reviewer — Review/Review (incl. the new código field).

---

## D6 — US7 HTML hover tooltips (own JS, not window.bootstrap)

**Decision:** Drive the field tooltips with a dedicated `wwwroot/js/hint-tooltip.js` module (mouseover/mouseout + focus/blur for a11y) that renders an HTML-capable bubble from the icon's data, following the `confirm-dialog.js` own-JS pattern. Extend `_HintTooltip.cshtml` to render a `ti ti-info-circle` info icon beside the field and carry the (HTML) copy. Resolve copy from a static es-CR copy provider class (not `IStringLocalizer`).

**Rationale — why NOT window.bootstrap:** `_Layout.cshtml:239-246` loads `tabler.min.js` but **never loads the bootstrap bundle** (present at `wwwroot/lib/bootstrap/dist/js/` but unreferenced). `site.js:8-32` guards tooltip init on `window.bootstrap && window.bootstrap.Tooltip` and otherwise returns — so that init is effectively dead here, and `data-bs-toggle="tooltip"` markup (e.g. ConversionIndicator) is not actually initialized. This is consistent with the project memory ("Tabler does not expose `window.bootstrap`; drive Toast/Modal/Tooltip via own JS + CSS") and with spec 024 hand-rolling its modal. Own JS is robust whether or not the global exists, and matches the codebase's established interaction-JS pattern.

**Rationale — why static copy provider, not IStringLocalizer:** Localization research confirms no `AddLocalization()`/`IStringLocalizer` registration in `Program.cs`; es-CR copy is delivered inline or via static classes (`UserFacingErrorTranslator`, `SuppliersResources`) — `IStringLocalizer`/`.resx` is explicitly avoided (NFR-003), and `021.es-CR.resx` is dead/unconsumed. So author tooltip copy as a static `HintCopy` provider (key → es-CR HTML string), and have `_HintTooltip.ResolveCopy()` read from it instead of returning null. This matches the memory note on reusing copy providers.

**HTML safety:** copy is curated (authored in the provider), never user-supplied, so rendering it as HTML (`@Html.Raw`) introduces no injection surface.

**Field set (applicant-facing, copy keys to author):** Register (email, password, names, legal id); Application/Edit (company name, impact, item product/category/specs); Application/Impact (template params); Supplier/Add (legal id, price, currency, valid-until). I author first-pass es-CR HTML; stakeholder refines.

**Alternatives rejected:** Loading the bootstrap bundle to use `bootstrap.Popover({html:true})` (adds a heavy global the project deliberately omits, risks data-API double-binding with tabler, contradicts memory); wiring `IStringLocalizer` (contradicts NFR-003 and the project convention; larger blast radius).

---

## D7 — US8 sidebar regroup (zero removals)

**Decision:** Restructure `_Layout.cshtml` sidebar data into three groups using the existing section-header pattern (`nav-item-section-header` + `fl-sidebar-section-header` + `data-section-testid`, as "Administración" already does). Add a "Proceso" section header. Every one of the 18 current entries is placed exactly once; `AllowedRoles` and the supplier-admin-only variant are preserved verbatim.

**AFTER tree** (see `contracts/sidebar-structure.md` for the full before→after table):
- **Inicio** (top-level, no header): Inicio; + role-gated operational items kept top-level for reviewer/applicant: Mis solicitudes (Applicant), Cola de revisión, Generar convenio, Bandeja de firmas (Reviewer/Admin).
- **Administración** (Admin; header → `/Admin`): Empresas proveedoras, Plantillas base (`/Admin/Plantillas`), Reportes (`/Admin/Reports`), Monedas, Tipos de cambio, Usuarios, Configuración del sistema.
- **Proceso** (Admin; header → `/Admin/Processes`): Grupos, Starters, Plantillas de impacto (`/Admin/ImpactTemplates`), Cotizaciones pendientes (`/Admin/LegacyQuotations`).

**Open decisions for the user (flagged, not silently resolved):**
1. **Starters route.** No standalone applications-list controller exists (`AdminApplicationsController` is soft-delete only; the list is the Reports "Applications" sub-tab `Views/Admin/Reports/Applications.cshtml`). **Recommendation (MVP, least new code):** add a thin nav-reachable route that renders that listing filtered by Process (either a small `AdminProcessesController` sub-action `/Admin/Processes/Starters` or a deep link to the Reports Applications tab with a `processId` filter). Building a brand-new Starters surface is deferred.
2. **Process-scoped "Reportes"/"Plantillas" duplication.** The stakeholder confirmed Reportes/Plantillas should appear under both groups as admin-wide vs process-scoped — but **no process-scoped report/template routes exist today**. Per YAGNI, this plan does **not** build net-new process-scoped aggregation surfaces. MVP placement above puts each existing surface once (Reportes + Plantillas base under Administración; Plantillas de impacto under Proceso). **Recommendation:** defer true process-scoped Reportes/Plantillas to a future spec, or confirm building them is in scope now (would expand this spec materially). Surface for user confirmation in the plan report.

**Rationale:** Honors FR-022 (zero removals) and FR-023 (role-gating preserved) while respecting constitution VI (no speculative surfaces). The section-header CSS/markup already exists, so the change is data + render-loop, no new CSS.

**Alternatives rejected:** Duplicating Reportes/Plantillas as two identical-target links (poor UX, no functional difference without process scoping); dropping items that didn't fit the example (violates FR-022).

---

## Cross-cutting

- **No schema change** (FR-027) — every story reuses existing columns/entities. Confirmed for US5 (`CodigoPersonal` already on `dbo.AspNetUsers`).
- **es-CR copy** delivered inline or via static copy classes, never new `IStringLocalizer` machinery (project convention / NFR-003).
- **E2E** is the delivery gate (constitution III, SC-008): each story gets Playwright coverage driving the real user journey (no deep-link shortcuts), per project memory.
