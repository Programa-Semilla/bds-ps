# Implementation Notes: 027 Review & Funding-Agreement UX Refinements

Technical anchors gathered during brainstorming. The spec stays WHAT/WHY; this file captures the HOW context so the plan phase starts grounded. File:line refs are accurate as of branch `027-review-funding-ux` (2026-05-26) — verify before editing.

## US1 — Generator name (bug)

- Bug: `GeneratedByDisplayName` is fed the raw `GeneratedByUserId` (GUID) at:
  - `src/FundingPlatform.Application/.../SignedUploadService.cs:154`
  - `src/FundingPlatform.Application/.../FundingAgreementService.cs:70`
- Fix path: resolve via `IUserStoreReader.GetDisplayNameAsync` (interface `src/FundingPlatform.Application/Services/IUserStoreReader.cs:20`, impl `src/FundingPlatform.Infrastructure/Identity/UserStoreReader.cs:32-51`). Already used for commission members in `FundingAgreementController.cs:741-746`.
- Render site: `src/FundingPlatform.Web/Views/Applications/_FundingAgreementPanel.cshtml:33` (`por @Model.GeneratedByDisplayName`).

## US2 — Confirm signed-convenio actions

- Buttons/forms: `_FundingAgreementPanel.cshtml:127-151` (`data-testid="signed-upload-approve"` / `signed-upload-reject`). Both immediate POST, no confirm.
- Actions: `FundingAgreementController.cs:459-484` (Approve, "Convenio ejecutado."), `:486-511` (Reject, requires comment, "Carga rechazada…").
- Reuse spec 024 confirm-dialog infra (toast/confirm). Memory: Tabler does not expose `window.bootstrap` — drive via project JS + CSS, not `new bootstrap.Modal`.

## US3 — Richer applicant detail

- Page: action `FundingAgreementController.Details` `:539-567` → view `Views/FundingAgreement/Details.cshtml` → includes `Views/Applications/_FundingAgreementPanel.cshtml`.
- Currently shows company (`Details.cshtml:65`) + representative (`:68`, composed at `FundingAgreementController.cs:728-730`).
- Available-but-unshown on `Applicant` (`Domain/Entities/Applicant.cs`): `LegalId`, `IdentificationType`, `Email`, `Phone`. Group via spec 016 membership. Submission date on Application.
- Caution: `FundingAgreementDocumentViewModel` comment (lines 9-10) notes email/phone/legalId were de-scoped from the **PDF** per spec 018 — that de-scope stays for the PDF; US3 adds them to the **screen** only.

## US4 — Consistent decision summary (5 stages)

Current per-stage state (from exploration):
1. Reviewer item review — `Controllers/ReviewController.cs:223-270` → `Views/Review/Review.cshtml:109-387`. Already shows product, category, **technical specs** (`:125`), supplier, price (multi-currency), valid-until. Richest surface — treat as the reference shape.
2. Applicant accept/reject — `Controllers/ApplicantResponseController.cs:33-44` → `Views/ApplicantResponse/Index.cshtml:59-126`. Shows name/status/supplier/amount/comment. **Missing `TechnicalSpecifications`** (not in `ItemResponseDto`, `Application/DTOs/ItemResponseDto.cs`).
3. Reviewer generate-agreement — VM builder `FundingAgreementController.cs:721-865`; the preview/Details table is **approved-only** and lacks specs.
4. Applicant signing screen — same Details/preview surface; approved-only.
5. Reviewer signed review — `ReviewController.SigningInbox :114-152` → `Views/Review/SigningInbox.cshtml` (no line detail at all); Details preview approved-only (`Details.cshtml:72-99`).

Domain fields available: `Item.ProductName`, `Item.CategoryName`, `Item.TechnicalSpecifications`, `Item.ReviewStatus` (Pending/Approved/Rejected/NeedsInfo), `Item.ReviewComment`, `Item.LineCode`, `Item.SelectedSupplierId`; `Quotation.Price/Currency/ConvertedCrcAmount/ValidUntil/Supplier.Name`.

Design intent: build ONE shared projection (Application layer) + ONE reusable partial (Web) carrying the full line shape (incl. specs, all quoted suppliers/amounts for rejected lines) and consume it on all five surfaces. PDF partials (`Views/FundingAgreement/Partials/_RequestedResourcesPage.cshtml`, `_CommitteeResultsPage.cshtml`) are **not** in scope to change (spec 018).

## US5 — Reviewer-assigned applicant code

- Field exists: `ApplicationUser.CodigoPersonal` (`Domain/Entities/ApplicationUser.cs:18`), column on `Database/Tables/dbo.AspNetUsers.sql` (NVARCHAR(40)).
- Today referenced only by: `Application/Identity/UpdateProfileCommand.cs`, `Web/Controllers/AccountController.cs`, `Web/ViewModels/ProfileViewModel.cs`, `Web/Views/Account/Profile.cshtml` (read-only "administrado"), `Web/Localization/021.es-CR.resx`. **No write surface anywhere** (no Admin controller/view, no user create/edit DTO). Dangling.
- Add write surface on the first review screen (`Review.cshtml` / `ReviewController`). Keep read-only on profile. No schema change.

## US6 — Required-field markers

- Existing pattern (ad-hoc): `<span class="text-danger" aria-label="campo obligatorio">*</span>` — e.g. `Views/Application/Edit.cshtml:56,83,136,144,151,164`. Other forms rely only on HTML5 `required`.
- Centralize into one shared mechanism (tag helper or partial); sweep all forms (applicant + admin + reviewer).

## US7 — HTML tooltips

- Scaffold (inert): `Domain/Attributes/HintAttribute.cs` (`ResourceKey`), `Web/ViewModels/HintTooltipModel.cs`, `Web/Views/Shared/_HintTooltip.cshtml` (renders `<span class="form-hint">`, `ResolveCopy()` returns null at `:48-57` — "OQ-8 copy deferred"; no `IStringLocalizer` injected). No `[Hint]`-decorated properties; text-only span; no icon, no hover.
- Work: turn it into an icon-triggered, HTML-capable hover tooltip; wire to applicant fields; author es-CR draft copy (resx). Tabler-driven, no `window.bootstrap`.

## US8 — Sidebar restructure

- File: `Web/Views/Shared/_Layout.cshtml` — sidebar data inline at `:19-66`, render `:111-176`; `Models/SidebarEntry.cs` (Slug/Label/Url/Icon/AllowedRoles). Visibility `IsEntryVisible()` `:68-83`; supplier-admin-only variant `:121-136`.
- **Current full item set (must all survive, regrouped — FR-022):**
  - Top-level: Inicio (`/`), Mis solicitudes (`/Application`, Applicant), Cola de revisión (`/Review`, Reviewer+Admin), Generar convenio (`/Review/GenerateAgreement`, Reviewer+Admin), Bandeja de firmas (`/Review/SigningInbox`, Reviewer+Admin).
  - Admin section: Procesos (`/Admin/Processes`), Plantillas (`/Admin/Plantillas`), Plantillas de impacto (`/Admin/ImpactTemplates`), Usuarios (`/Admin/Users`), Grupos (`/Admin/Groups`), Proveedores/Empresas proveedoras (`/Admin/Suppliers`), Reportes (`/Admin/Reports`), Monedas (`/Admin/Currencies`), Tipos de Cambio (`/Admin/ExchangeRates`), Cotizaciones Pendientes (`/Admin/LegacyQuotations`), Configuración del sistema (`/Admin/Configuration`).
  - Supplier-admin-only variant: Empresas proveedoras (`/Admin/Suppliers`).
- Proposed grouping (confirmed with stakeholder): reviewer/applicant items stay top-level/Inicio area; Administración = Empresas proveedoras, Plantillas base, Reportes, Monedas, Tipos de cambio, Usuarios, Configuración; Proceso (headed by Procesos) = Grupos, Starters, Reportes (process-scoped), Plantillas, Plantillas de impacto, Cotizaciones pendientes.
- "Starters": no standalone list controller exists — `AdminApplicationsController` is only a soft-delete endpoint (`Controllers/Admin/AdminApplicationsController.cs`); the actual applications listing lives as a Reports sub-tab (`Views/Admin/Reports/Applications.cshtml`). Surface that listing as the Starters nav item, filterable by Process. `Application` has no direct `ProcessId`/`GroupId`; Process link runs `Group.ProcessId` (`Domain/Entities/Group.cs:27`) via group membership.

## Decisions log

- One consolidated spec (not split, not per-thread) — stakeholder choice.
- Decision-summary expansion is **on-screen only**; PDF document content unchanged (spec 018 preserved).
- Reviewer code = reuse `CodigoPersonal`, per-user, no schema change.
- Tooltip copy: I author first-pass es-CR HTML copy; stakeholder refines later.
- Required markers: every form in the app (admin/reviewer included), not just applicant.
- Menu: zero removals; the shared tree was an example, not exhaustive.
