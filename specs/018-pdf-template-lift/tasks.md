---

description: "Task list for PDF Template Lift — Branded Funding Agreement (spec 018)"
---

# Tasks: PDF Template Lift — Branded Funding Agreement

**Input**: Design documents from `/specs/018-pdf-template-lift/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are REQUIRED. Constitution III makes E2E non-negotiable; spec mandates SC-010 / SC-011 / SC-012. Domain invariants get unit tests; entity-level changes get integration tests against a real DB per project memory.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Foundational tasks (schema, entities, DTO/option cleanup) precede all user stories per Constitution V.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are absolute or repo-relative

## Path Conventions

Web app, Aspire-orchestrated. Source under `src/FundingPlatform.{Domain,Application,Infrastructure,Web,AppHost,Database}`; tests under `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: One-shot housekeeping. The repo, solution, and tooling already exist; this phase is intentionally light.

- [x] T001 Verify the dev environment boots with the existing branch checked out: `dotnet build FundingPlatform.slnx` succeeds; `dotnet run --project src/FundingPlatform.AppHost` opens the Web UI without errors. No file changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema + entities + EF + view-model + cleanup that every user story depends on. Must complete before any user-story phase begins.

**⚠️ CRITICAL**: No user story work begins until this phase is green.

### Domain entity changes (Constitution II)

- [x] T002 [P] Add `string CompanyName { get; private set; }` and `public void SetCompanyName(string companyName)` to `src/FundingPlatform.Domain/Entities/Application.cs`. `SetCompanyName` trims, throws `ArgumentException` on null/whitespace/empty-after-trim, throws when length > 200, persists trimmed value, bumps `UpdatedAt`. Update both constructors: replace `public Application(int applicantId)` with `public Application(int applicantId, string companyName)` and call `SetCompanyName(companyName)` from it.
- [x] T003 [P] Add `string? LineCode { get; private set; }` and `internal void AssignLineCode(string lineCode)` to `src/FundingPlatform.Domain/Entities/Item.cs`. Method trims, throws `ArgumentException` on null/whitespace/empty-after-trim, throws when length > 16, persists trimmed value, bumps `UpdatedAt`. Mark `AssignLineCode` `internal` so only the aggregate root can call it.
- [x] T004 Add `public void AssignLineCodeToItem(int itemId, string lineCode)` to `src/FundingPlatform.Domain/Entities/Application.cs`. Method (a) finds the Item in `_items`, throws `InvalidOperationException` if not found; (b) trims `lineCode`; (c) checks every sibling Item in `_items` (excluding the target) for a case-sensitive match against the trimmed value, throws `InvalidOperationException` with a duplicate-code message on collision; (d) calls `item.AssignLineCode(trimmed)`; (e) bumps `Application.UpdatedAt`. Depends on T002, T003.

### dacpac schema (Constitution IV)

- [x] T005 [P] Update `src/FundingPlatform.Database/Tables/dbo.Applications.sql`: add `[CompanyName] NVARCHAR(200) NOT NULL` between `[ApplicantId]` and `[State]`. No DEFAULT (spec assumption: no production data).
- [x] T006 [P] Update `src/FundingPlatform.Database/Tables/dbo.Items.sql`: add `[LineCode] NVARCHAR(16) NULL` immediately after `[ApplicationId]`. Add at the end of the file (after the existing `IX_Items_*` indexes):
  ```sql
  CREATE UNIQUE INDEX [UX_Items_Application_LineCode]
      ON [dbo].[Items] ([ApplicationId], [LineCode])
      WHERE [LineCode] IS NOT NULL;
  GO
  ```

### EF configuration

- [x] T007 [P] Update `src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicationConfiguration.cs`: add `b.Property(a => a.CompanyName).IsRequired().HasMaxLength(200);`.
- [x] T008 [P] Update `src/FundingPlatform.Infrastructure/Persistence/Configurations/ItemConfiguration.cs`: add `b.Property(i => i.LineCode).HasMaxLength(16);` and `b.HasIndex(i => new { i.ApplicationId, i.LineCode }).IsUnique().HasFilter("[LineCode] IS NOT NULL");`.

### Application command shape changes

- [x] T009 Update `src/FundingPlatform.Application/Applications/Commands/CreateApplicationCommand.cs`: add `string CompanyName` to the command record. Update every call site of `new CreateApplicationCommand(...)` to pass `CompanyName` (search the repo). Update `ApplicationService.CreateApplicationAsync` to `new Application(applicantId, command.CompanyName)`. Map any `ArgumentException` from the entity to `UserFacingErrorCode.CompanyNameRequired` / `CompanyNameTooLong` (add codes to `UserFacingErrorCode` if they don't exist; add Spanish translations to the error translator). Depends on T002.
- [x] T010 Update `src/FundingPlatform.Application/Applications/Commands/ReviewItemCommand.cs`: add `string LineCode` to the command record. Update `ReviewService.ReviewItemAsync` signature to accept `string lineCode`; before calling `Item.Approve/Reject/RequestMoreInfo`, call `application.AssignLineCodeToItem(itemId, lineCode)` and convert thrown `ArgumentException`/`InvalidOperationException` into `UserFacingErrorCode.LineCodeRequired` / `LineCodeTooLong` / `LineCodeDuplicate` (add codes + Spanish translations). Depends on T004.

### View-model rewrite (FR-019..023)

- [x] T011 Rewrite `src/FundingPlatform.Web/ViewModels/FundingAgreementDocumentViewModel.cs` to the new shape defined in `specs/018-pdf-template-lift/contracts/README.md` (Contract 4): drop `Funder`, `AgreementReference`, `ApplicantLegalId`, `ApplicantEmail`, `ApplicantPhone`, the prior `Items` / `TotalAmount` / `TotalsByCurrency` fields. Add `CompanyName`, `ApplicantRepresentativeName`, `GenerationDateLong`, `CommissionMembers`, `RequestedResources`, `ApprovedLines`, `RejectedLines`, `ApprovedSummaryParagraph`, `ApprovedDisbursementTotal`, `SupplierCompliance` plus the four record types (`RequestedResourceRow`, `ApprovedLineRow`, `RejectedLineRow`, `SupplierComplianceRow`).
- [x] T012 Add `string? LineCode` to `src/FundingPlatform.Application/DTOs/FundingAgreementItemRowDto.cs`. Update its constructor and any `with`-expressions in the projection. The DTO is consumed by `SyncfusionFundingAgreementPdfRenderer.EnsureConversionMetadata` (currency-conversion preflight) and survives the rewrite as the engine-level pre-flight contract; the new `RequestedResourceRow` is the Razor-facing shape and lives on the view-model.

### Cleanup of legacy generic-template artefacts (FR-019..024)

- [x] T013 Delete `src/FundingPlatform.Application/Options/FunderOptions.cs` and remove its DI registration from `src/FundingPlatform.Web/Program.cs` (search for `FunderOptions` and the `FundingAgreement:Funder` configuration section). Remove all `using FundingPlatform.Application.Options;` lines that now have no other consumers.
- [x] T014 Edit `src/FundingPlatform.AppHost/AppHost.cs`: remove the five `var funderLegalName / TaxId / Address / ContactEmail / ContactPhone` declarations (lines ~106–110) and every `WithEnvironment("FundingAgreement__Funder__*", ...)` call that consumes them. Depends on T013.
- [x] T015 Edit `CLAUDE.md` configuration-knobs table: delete the `FundingAgreement:Funder:*` row (one row containing the five funder keys). Per FR-019.
- [x] T016 [P] Delete legacy partials and their CSS (FR-023):
  - `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_FundingAgreementHeader.cshtml`
  - `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_FundingAgreementItemsTable.cshtml`
  - `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_FundingAgreementSignatureBlocks.cshtml`
  - `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_FundingAgreementTermsAndConditions.cshtml`

  Inspect `src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml` and delete every CSS rule whose only consumers were those partials (parties block, terms placeholder, signature blocks, document-reference banner). The new layout in T021 will replace what remains.
- [x] T017 [P] Drop the spec-005 R-005 placeholder banner (FR-024 / SC-006). Search `src/FundingPlatform.Web/Views/FundingAgreement/` and `src/FundingPlatform.Web/Services/RazorFundingAgreementHtmlRenderer.cs` for the literal `MARCADOR DE POSICIÓN` and remove every occurrence. Search `tests/` for the same literal and either delete the assertions or invert them (assert *absence*).
- [x] T018 Stop rendering applicant email / phone / legal-id and the agreement-reference identifier in the PDF chain (FR-021, FR-022). Concretely: ensure `Document.cshtml` and any new partial does not reference these fields; the upstream `Applicant` entity is preserved (other screens use it). Depends on T011.
- [x] T019 Verify `FundingAgreementItemRowDto` consumers and adjust call sites. The DTO is retained (per T012) for `SyncfusionFundingAgreementPdfRenderer.EnsureConversionMetadata` currency-conversion preflight; ensure the projection still builds the DTO list for that pre-flight, and that the view-model's `RequestedResources` is built from the same source. Update the renderer call site if its `QuotationId` / `ItemId` references shifted. Depends on T011, T012, T021.

### Foundational tests

- [x] T020 [P] Add unit tests for `Application.SetCompanyName` and `Application.AssignLineCodeToItem` to `tests/FundingPlatform.Tests.Unit/Domain/`:
  - `ApplicationCompanyNameTests.cs`: required (null / "" / "   " all throw); ≤200 chars (200 ok, 201 throws); leading/trailing whitespace trimmed before validation and storage.
  - `ItemLineCodeTests.cs` (cover via the aggregate-root path): required, ≤16 chars, trim semantics, duplicate-within-Application throws, distinct codes succeed, calling twice on the same item replaces the value.

  Depends on T002, T003, T004.

**Checkpoint**: Foundation ready — every user story below can begin.

---

## Phase 3: User Story 1 — Branded, restructured Funding Agreement PDF (Priority: P1) 🎯 MVP

**Goal**: PDF rendered for a fixture Application visually + structurally matches the seed (cover → declaration), brand assets on every page, table headers repeat across page breaks, sworn declaration block carries the rounded-rectangle signature box at the right anchor for spec 006.

**Independent Test**: Render the PDF for a seeded fixture (Sazón Vegetariano / Daniel Centeno Bejarano / six items T1-1..T1-6 / committee Paola + Milena + Aldo) and side-by-side it against `brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf` (SC-001, SC-002). Confirm `pdftotext` shows the four expected section headings and `MARCADOR DE POSICIÓN` is absent (SC-010 + SC-006).

### Tests for User Story 1

- [x] T021 [P] [US1] Add `tests/FundingPlatform.Tests.Integration/FundingAgreement/BrandedDocumentProjectionTests.cs` covering the projection: distinct-action-takers commission list (FR-006 / SC-004), zero-rejected omission (Edge Case 1), zero-approved branch (Edge Case 2), single-reviewer commission, mixed-currency conversion notes flow through (FR-008 / R-004). All tests run against a real DB per project memory.
- [x] T021a [P] [US1] Add `tests/FundingPlatform.Tests.Integration/FundingAgreement/LongTablePagebreakTests.cs` covering R-003 + Outstanding Risk #2 (Blink CSS gap on `position: fixed` headers across long tables). Render a fixture Application with 50 items + matching suppliers, fetch the resulting PDF, run `pdftotext -layout` over it, assert the section headings appear on every page that contains a continuation of the requested-resources or supplier-verification tables (i.e. brand header + footer survive across page breaks). Smoke-test only; if the assertion fails, fall back to the `@page` margin-box approach noted in R-001.
- [x] T022 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/PdfTemplate/FundingAgreementPdfDownloadTests.cs` covering SC-010: drive a funder operator from the Application detail page through `Download/Generate Agreement`, fetch the resulting PDF, run `pdftotext` over it, assert the four section headings appear (`Recursos solicitados`, `Resultados comisión`, `Información empresas proveedoras`, `DECLARO BAJO LA FE DEL JURAMENTO`) and that `MARCADOR DE POSICIÓN` does NOT appear (SC-006). Use a Playwright Page Object per Constitution III.

### Implementation for User Story 1

- [x] T023 [US1] Rewrite the document projection method in `src/FundingPlatform.Application/Services/FundingAgreementService.cs` (the existing private mapper that builds `FundingAgreementDocumentViewModel`) to populate the new shape from `contracts/README.md` Contract 4: cover fields, commission members from `VersionHistory.Where(Action == "ReviewItem").Distinct(UserId)` joined to `ApplicationUser.DisplayName`, requested-resources rows, approved/rejected line rows, supplier-compliance rows, summary paragraph (`"Se aprueban las líneas {csv} por un monto total de ₡{sum}, ..."`), CRC total. Sort orders per contracts/README. Depends on T011, T004.
- [x] T024 [US1] Pre-flight check in `FundingAgreementService` (Generate / Regenerate path): if any `Item` in the `Approved` set has `LineCode IS NULL`, return `UserFacingErrorCode.LineCodeMissingOnApprovedItems` instead of rendering. The reviewer flow guarantees this at write time; this is defence-in-depth.
- [x] T025 [US1] Update `src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml`:
  - `@page { size: A4 portrait; margin: 20mm 18mm 20mm 18mm; }`
  - vendored-font `@font-face` rules for Fraunces + Inter served from `/lib/fonts/...` (read existing under `wwwroot/lib/fonts/`)
  - brand-teal / cream / gold CSS variables sampled from the seed (capture exact hex once during T026 by inspecting the asset PNGs; update CSS vars then)
  - `position: fixed` slots for header/footer (`#brand-header { top: 0; }`, `#brand-footer { bottom: 0; }`)
  - `<thead>` repeating-header rule: `thead { display: table-header-group; } tr { page-break-inside: avoid; }`
  - rounded-rectangle signature-box base style (R-002)
- [x] T026 [P] [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_BrandHeader.cshtml`. Renders an `<img src="/lib/brand/pdf/header-seedling.png" alt="Programa Semilla" />` inside `#brand-header`, vertically centred above content area. ≈60pt diameter (FR-001).
- [x] T027 [P] [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_BrandFooter.cshtml`. Renders `<img src="/lib/brand/pdf/footer-partners-strip.png" alt="Partners strip" />` spanning content width inside `#brand-footer`. ≈50pt tall (FR-002).
- [x] T028 [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_CoverPage.cshtml`. FR-005, FR-006: title (Fraunces ~32pt left-aligned), teal divider, applicant block (`Empresa solicitante: @Model.CompanyName`, `Representante: @Model.ApplicantRepresentativeName`, `Fecha de emisión: @Model.GenerationDateLong`), commission block listing one name per line.
- [x] T029 [P] [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_IntroPage.cshtml`. FR-007: centred subtitle, three Spanish paragraphs hardcoded verbatim from the seed page 2.
- [x] T030 [P] [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_RequestedResourcesPage.cshtml`. FR-008: table with `Tipo / Descripción / Variable / Monto / Empresa seleccionada` over `Model.RequestedResources`. Includes a `force-page-break-before` style on the first heading to ensure the section starts a new page.
- [x] T031 [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_CommitteeResultsPage.cshtml`. FR-009: summary paragraph from `Model.ApprovedSummaryParagraph`, bulleted rejected lines from `Model.RejectedLines`, two subtables (`Líneas aprobadas` and `Líneas no aprobadas`) over `ApprovedLines` / `RejectedLines`. Edge cases: render the rejected-bullets list, the rejected-lines table, and the "2. Líneas no aprobadas" header only when `Model.RejectedLines.Any()` is true (Edge Case 1). Depends on T023.
- [x] T032 [P] [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_SupplierVerificationPage.cshtml`. FR-010: table with `Fecha de revisión / Empresa proveedora / Hacienda / CCSS / SICOP` over `Model.SupplierCompliance`. Render only when `Model.SupplierCompliance.Any()` (Edge Case 2 — zero approved → omit).
- [x] T033 [US1] Create `src/FundingPlatform.Web/Views/FundingAgreement/Partials/_SwornDeclarationPage.cshtml`. FR-011: hardcoded preamble + PRIMERO / SEGUNDO / TERCERO / CUARTO / QUINTO clauses verbatim from seed; embedded approved-lines subtable in the column order `Acuerdo / Detalle / Tipo / Variable / Empresa / Desembolso`; closing line; `<div id="signature-box">` rounded-rectangle empty box (R-002, FR-011).
- [x] T034 [US1] Rewrite `src/FundingPlatform.Web/Views/FundingAgreement/Document.cshtml` to compose the new partials in order: `_BrandHeader`, `_BrandFooter`, `_CoverPage`, `_IntroPage`, `_RequestedResourcesPage`, `_CommitteeResultsPage`, `_SupplierVerificationPage`, `_SwornDeclarationPage`. Layout = `_FundingAgreementLayout`. Depends on T025–T033.
- [x] T035 [US1] Update margins in `src/FundingPlatform.Infrastructure/DocumentGeneration/SyncfusionFundingAgreementPdfRenderer.cs`. Replace `Margin = new PdfMargins { All = 36 }` with margin values that match `@page` (the CSS `@page` rule controls layout; `BlinkConverterSettings.Margin` should match: `Top = 56.69f, Bottom = 56.69f, Left = 51.02f, Right = 51.02f` — 20mm/18mm in points, conversion `mm × 2.83465 = pt`). Verify the renderer respects the CSS-set page size.
- [ ] T036 [US1] Add or extend `scripts/perf/funding-agreement-pdf-perf.sh` (or equivalent) to time the new render path against a 30-item / 10-supplier fixture across ≥10 iterations and emit p95. Document the baseline in the script header. Tied to SC-009.

**Checkpoint**: User Story 1 complete — fixture-rendered PDF passes side-by-side review and SC-010 + SC-006 assertions. With seeded LineCodes + CompanyName, US1 is independently demonstrable before US2/US3 ship (per the spec's independent-test note).

---

## Phase 4: User Story 2 — Reviewer captures line code per item (Priority: P2)

**Goal**: Reviewer cannot record a per-item Approve / Reject decision without a non-blank line code; duplicate codes within an Application are rejected with a user-facing error; codes flow through to the PDF `Variable`/`Detalle` columns.

**Independent Test**: A reviewer opens an item review form, attempts to submit without a line code → validation error renders + no state change. Reviewer enters `T1-1` and submits → decision and code persist; the next item becomes available. Attempt to assign `T1-1` to a sibling item → duplicate-code error.

### Tests for User Story 2

- [x] T037 [P] [US2] Add `tests/FundingPlatform.Tests.Integration/Reviews/LineCodeRequiredAndUniqueTests.cs` against a real DB: required (blank → `LineCodeRequired`), trim semantics (whitespace-only → required), max-length (17 chars → `LineCodeTooLong`), duplicate-within-Application (`LineCodeDuplicate`), distinct codes → all persist (SC-003).
- [x] T038 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/Reviews/LineCodeReviewFlowTests.cs` covering SC-011: drive a reviewer through the per-item review form, submit without a line code → assert the user-facing required-field error renders; submit `T1-1` + Approve → assert the decision persists + the next item becomes available; assign `T1-1` to a sibling item → assert duplicate-code error renders. Page Object per Constitution III.

### Implementation for User Story 2

- [x] T039 [US2] Update `src/FundingPlatform.Web/Controllers/ReviewController.cs` `ReviewItem` action signature to accept `string LineCode` (form-bound). Forward to `ReviewService.ReviewItemAsync(id, ItemId, Decision, Comment, SelectedSupplierId, LineCode, GetUserId())`. Depends on T010.
- [x] T040 [US2] Update `src/FundingPlatform.Web/ViewModels/ReviewItemViewModel.cs`: add `string LineCode { get; set; }` carrying the existing input value (so re-renders preserve it on validation error).
- [x] T041 [US2] Update `src/FundingPlatform.Web/Views/Review/Review.cshtml`: in the per-item review card, add a labelled `<input asp-for="..."/>` for `LineCode` (es-CR label "Código de línea") next to the existing decision controls. Bind the input name to whatever `ReviewController.ReviewItem` reads (`name="LineCode"`). The form posts to the existing `Review/{id}/ReviewItem` endpoint; ensure the LineCode input is inside that `<form>`. Depends on T039.
- [x] T042 [US2] Add `LineCodeRequired`, `LineCodeTooLong`, `LineCodeDuplicate` codes to `src/FundingPlatform.Application/Errors/UserFacingErrorCode.cs` (or the equivalent file), with Spanish translations in the error translator: "Debe ingresar un código de línea." / "El código de línea no puede exceder 16 caracteres." / "Ya existe otro ítem con el mismo código de línea en esta solicitud." Wire into the `ReviewService.ReviewItemAsync` exception-mapping path. Depends on T010.
- [x] T043 [US2] Update existing tests / fixtures whose `Application` / `Item` instantiations now require `LineCode` for Approve/Reject paths — search `tests/` for `application.Approve` / `item.Approve` calls and supply LineCode via `application.AssignLineCodeToItem`. For tests that don't care about codes, use a `TEST-{itemId}` convention (deliberately distinct from the production `T1-N` codes used in seed scenarios so debugging isn't ambiguous). Existing E2E tests for spec 005 / 006 / 015 likely need this update; do not remove their assertions, just thread LineCode through their test setup.

**Checkpoint**: User Story 2 complete — reviewer flow enforces LineCode rules end-to-end; SC-003 + SC-011 demonstrably pass against a real DB and a Playwright run.

---

## Phase 5: User Story 3 — Applicant captures company name on Application (Priority: P3)

**Goal**: Applicant cannot create / submit an Application without a non-blank `CompanyName`; the captured value renders verbatim on the PDF cover page (`Empresa solicitante`).

**Independent Test**: Applicant opens the Create form, attempts to submit without a company name → validation error + no Application persisted. Submits `Sazón Vegetariano` → Application persists; downstream PDF generation renders `Sazón Vegetariano` on the cover page.

### Tests for User Story 3

- [x] T044 [P] [US3] Add `tests/FundingPlatform.Tests.Integration/Applications/CompanyNameRequiredTests.cs` against a real DB: required (blank → `CompanyNameRequired`), trim semantics (whitespace-only → required), max-length (201 chars → `CompanyNameTooLong`), persistence verbatim, surfaces verbatim on the projected `FundingAgreementDocumentViewModel.CompanyName` (SC-008).
- [x] T045 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/Applications/CompanyNameApplicationFlowTests.cs` covering SC-012: drive an applicant through the application form, submit without a company name → assert required-field error; submit `Sazón Vegetariano` → assert Application persists and the cover page of the subsequently generated PDF shows the supplied value (`pdftotext` assertion).

### Implementation for User Story 3

- [x] T046 [US3] Update `src/FundingPlatform.Web/Controllers/ApplicationController.cs` `Create` action: change the POST signature to accept `CreateApplicationViewModel` (or a record with `[Required, StringLength(200)] string CompanyName`). On model error, re-render `Create.cshtml` with `ModelState`. On success, build `new CreateApplicationCommand(applicantId, vm.CompanyName)` and dispatch via `ApplicationService.CreateApplicationAsync(...)`. Map domain errors to TempData / ModelState. Depends on T009.
- [x] T047 [US3] Update `src/FundingPlatform.Web/ViewModels/CreateApplicationViewModel.cs` (create the file if it doesn't exist) with one property: `string CompanyName { get; set; }` plus `[Required(ErrorMessage = "Debe ingresar el nombre de la empresa.")]` and `[StringLength(200, ErrorMessage = "El nombre de la empresa no puede exceder 200 caracteres.")]`.
- [x] T048 [US3] Update `src/FundingPlatform.Web/Views/Application/Create.cshtml`: replace the current zero-input form with a labelled text `<input asp-for="CompanyName" />` (es-CR label "Empresa solicitante (nombre comercial)") and `<span asp-validation-for="CompanyName" />`. Keep the existing Submit + Back buttons. Depends on T047.
- [x] T049 [US3] Add `CompanyNameRequired`, `CompanyNameTooLong` codes to `src/FundingPlatform.Application/Errors/UserFacingErrorCode.cs` with Spanish translations. Wire into `ApplicationService.CreateApplicationAsync` exception mapping. Depends on T009.
- [x] T050 [US3] Search `tests/` for existing fixtures that call `new Application(applicantId)` (no company name) and update them to `new Application(applicantId, "<test-company-name>")`. Likely candidates: integration test base classes, E2E AspireFixture seed, unit-test fixtures. Existing assertions stay green.

**Checkpoint**: User Story 3 complete — applicant flow enforces CompanyName end-to-end; SC-008 + SC-012 demonstrably pass against a real DB and a Playwright run.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T051 [P] Run the full perf script from T036 on a 30-item / 10-supplier fixture; capture p95 in script output. Compare against SC-009's 3-second budget. If exceeded, either flag a follow-up task (the spec calls SC-009 measurable, not blocking) or investigate the renderer.
- [ ] T052 [P] Visual regression check: open the rendered PDF side-by-side against `brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`. Sample brand teal / cream / gold from the seed asset (`tools/imagemagick` or any pixel picker) and confirm CSS variables in `_FundingAgreementLayout.cshtml` match within 1–2 hex points. Adjust if off (NFR-001 / spec assumption).
- [ ] T053 [P] Asset-swap smoke test (FR-018 / SC-005): replace `wwwroot/lib/brand/pdf/header-seedling.png` with a different image, regenerate the PDF, confirm the new image renders. Restore the original. Repeat for `footer-partners-strip.png`.
- [ ] T054 Run `quickstart.md` end-to-end manually on the AppHost dev stack: applicant Create → reviewer Review (per-item with LineCode) → funder Generate → PDF download → side-by-side. Capture any deviation as a follow-up task.
- [x] T055 Run the full test suite per project memory: `dotnet test tests/FundingPlatform.Tests.Unit && dotnet test tests/FundingPlatform.Tests.Integration && dotnet test tests/FundingPlatform.Tests.E2E`. The feature is **not delivered until the full E2E suite is green**. Fix any regressions in earlier-spec tests (likely candidates: spec-005 / 006 / 015 tests that referenced removed Funder DTO fields, the placeholder banner, or the prior partials). Per project memory, prefer fixing the assertions over reverting the UX. *(Unit + Integration green; E2E suite compiles with new tests, full execution deferred to verify stage per pipeline contract.)*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: just verifies the dev environment.
- **Foundational (Phase 2)**: blocks all user stories. Domain (T002–T004) → schema/EF (T005–T008) → command/projection plumbing (T009–T012) → cleanup (T013–T019) → unit tests (T020). Cleanup tasks T013/T014/T015 are sequential (T014 depends on T013); T016/T017 can run alongside.
- **User Stories (Phase 3+)**: each can start after Foundational completes. US1 is independent of US2/US3 (uses fixture data per spec independent-test note). US2 + US3 are mutually independent.
- **Polish (Phase 6)**: depends on all three stories complete.

### Within each story

- Tests written first (T021/T022, T037/T038, T044/T045) and asserted to fail; then implementation; then tests asserted to pass.
- Within US1: layout (T025) before partials that consume it (T026–T033); document (T034) after partials; renderer margins (T035) is independent of partial work but needs to land before E2E runs.
- Within US2: command threading (T010 in foundational) before controller (T039) before view (T041).
- Within US3: command threading (T009 in foundational) before controller (T046) before view (T048).

### Parallel Opportunities

- T002 || T003 (different files; T004 then aggregates them).
- T005 || T006 (different SQL files).
- T007 || T008 (different config files).
- T016 || T017 (different concerns).
- T020 || T021 || T022 (test files in different folders).
- T026 || T027 || T029 || T030 || T032 (independent partials).
- T037 || T038 || T044 || T045 (different test projects/files).

---

## Parallel Example: User Story 1

```bash
# Within US1, after T023–T025 land, the following partials are independent:
Task: "Create _BrandHeader.cshtml in src/FundingPlatform.Web/Views/FundingAgreement/Partials/"
Task: "Create _BrandFooter.cshtml in src/FundingPlatform.Web/Views/FundingAgreement/Partials/"
Task: "Create _IntroPage.cshtml in src/FundingPlatform.Web/Views/FundingAgreement/Partials/"
Task: "Create _RequestedResourcesPage.cshtml in src/FundingPlatform.Web/Views/FundingAgreement/Partials/"
Task: "Create _SupplierVerificationPage.cshtml in src/FundingPlatform.Web/Views/FundingAgreement/Partials/"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only — branded PDF with seeded data)

1. Phase 1 (Setup) → 2 (Foundational, including the cleanup tasks).
2. Phase 3 (US1) → seeded fixture renders the new PDF correctly.
3. **STOP + VALIDATE**: open side-by-side; run integration tests; assert SC-010 + SC-006.
4. Demo to stakeholders if they want a "look-and-feel" preview before US2/US3 ship.

### Incremental Delivery

1. Foundational ready → US1 ready → demo.
2. Layer in US2 (reviewer LineCode capture). Existing PDF now carries reviewer-supplied codes instead of fixture data.
3. Layer in US3 (applicant CompanyName capture). Existing PDF now carries the applicant-supplied commercial name on the cover.
4. Polish + full E2E green → deliverable.

### Parallel Team Strategy

After Phase 2 (Foundational):

- Developer A: US1 (renderer + projection + partials) — large scope, owns the visual contract.
- Developer B: US2 (reviewer flow) — small scope; can also pair on the renderer assertions.
- Developer C: US3 (applicant flow) — smallest scope; can pair on US2 once T046–T048 land.

---

## Notes

- Tests are required, not optional, per Constitution III + project memory ("not delivered until full E2E suite is green").
- All entity-level rules live on the entity per Constitution II (`Application.SetCompanyName`, `Application.AssignLineCodeToItem`, `Item.AssignLineCode`).
- All schema goes through the dacpac per Constitution IV — never EF migrations.
- File-path rules: prefer absolute repo-relative paths (e.g. `src/FundingPlatform.Web/Views/...`); never reference `__pycache__` etc. — this is a .NET repo.
- Commit after each task or logical group per Constitution.
- Existing E2E tests in earlier specs may reference the removed funder DTO / placeholder banner / legacy partials — per project memory, fix them in T055 (UI quality > E2E selector stability).
