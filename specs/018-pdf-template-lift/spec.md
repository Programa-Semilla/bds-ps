# Feature Specification: PDF Template Lift — Branded Funding Agreement

**Feature Branch**: `018-pdf-template-lift`
**Created**: 2026-05-08
**Status**: Draft
**Input**: User description: Replace the current generic "Convenio de Financiamiento" Funding Agreement PDF with a fully-branded, multi-section document that pixel-matches the canonical "Informe de evaluación de solicitudes de desembolso" seed template at `brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`. Adds reviewer-side line-code capture and applicant-side company-name capture as enabling data inputs. Removes dead Funder configuration left over from the prior generic template.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Branded, restructured Funding Agreement PDF (Priority: P1)

As a funder operator, I open the Funding Agreement view for a reviewed Application and download a PDF that visually and structurally matches the canonical "Informe de evaluación de solicitudes de desembolso" template — Programa Semilla branding (header logo, partner-logo footer strip, brand teal palette, Fraunces serif headings, Inter body), 6-section flow (cover with applicant + commission, intro, requested resources, committee results, supplier verification, sworn declaration with embedded approved-lines table and signature box) — ready for the applicant to sign digitally and the funder to archive.

**Why this priority**: The PDF is the single artifact the applicant signs and the funder retains as evidence of approval. Today's generic-looking output undermines program credibility with applicants and external stakeholders. This is the user-visible deliverable; everything else in the spec is enabling data plumbing.

**Independent Test**: Render the PDF for a fixture Application that already has approved/rejected `ItemResponse` rows, suppliers with compliance status, and reviewer action history. Confirm by visual side-by-side against the seed PDF that branding chrome, section ordering, table contents, and sworn declaration copy match. Can be tested with seeded LineCodes + CompanyName before US2/US3 are wired up.

**Acceptance Scenarios**:

1. **Given** an Application with at least one approved item, one rejected item, distinct selected suppliers, and at least one reviewer action, **When** the funder operator triggers PDF generation, **Then** the PDF contains a cover page with `Empresa solicitante`, `Representante`, `Fecha de emisión`, and a `Comisión evaluadora` list of distinct action-takers, followed by an intro page, a `Recursos solicitados` table covering all items, a `Resultados comisión` section with summary paragraph + approved-table + rejected-table-with-motivo, an `Información empresas proveedoras` table for distinct approved-line suppliers, and a sworn declaration page closing with an empty signature box.
2. **Given** the same Application, **When** the PDF is opened, **Then** every page (cover through declaration) shows the seedling header logo at the top and the partner-logo composite strip at the bottom, with brand teal section headings and consistent typography (Fraunces serif for headings, Inter for body).
3. **Given** the generated PDF, **When** the existing digital-signature ceremony (spec 006) runs against it, **Then** the signature stamp lands inside the rounded-rectangle signature box on the declaration page without breaking the ceremony flow.
4. **Given** an Application whose committee has zero rejected items, **When** the PDF is generated, **Then** the rejected-bullets list, the rejected-lines table, and the "2. Líneas no aprobadas" header are all omitted (no empty sections rendered).
5. **Given** an Application with mixed-currency items (CRC + USD), **When** the PDF is generated, **Then** per-line currency-conversion notes (existing spec 015 format) appear inside the new tables unchanged.

---

### User Story 2 - Reviewer captures line code per item (Priority: P2)

As a reviewer evaluating an Application, I assign a free-text identifier (e.g., `T1-1`, `T1-2`) to each item I review, so the resulting Funding Agreement PDF carries the canonical line code in its `Variable` column and the committee-results summary references each line by code.

**Why this priority**: Without this, the `Variable` column in the Funding Agreement PDF is blank or fixture-driven and the committee-results summary cannot reference lines unambiguously ("Se aprueban las líneas T1-2, T1-3, T1-4, T1-6 …"). The line code is reviewer-assigned, not auto-derived from item position, because reviewers may group/renumber lines per their evaluation tract.

**Independent Test**: A reviewer opens an item-review form, attempts to submit without filling the line-code input, observes a validation error, fills the input, and submits successfully. The captured code persists with the `ItemResponse` and is queryable for downstream PDF rendering.

**Acceptance Scenarios**:

1. **Given** a reviewer is reviewing an item, **When** they submit their decision (approve or reject) without entering a line code, **Then** the system rejects the submission with a user-facing validation error indicating the line code is required, and no decision is persisted.
2. **Given** a reviewer enters a non-blank line code (length ≤ 16 characters) and submits, **Then** the decision and code persist together and the next item in the queue becomes available.
3. **Given** every item in an Application has been reviewed and assigned a line code, **When** the resulting Funding Agreement PDF is generated, **Then** the `Variable` column in the `Recursos solicitados` table and the `Detalle` column in the `Líneas aprobadas`/`Líneas no aprobadas` tables show the reviewer-assigned codes verbatim.
4. **Given** two items within the same Application are assigned the same line code, **When** the second submission is attempted, **Then** the system rejects it with a duplicate-code validation error.

---

### User Story 3 - Applicant captures company name on Application (Priority: P3)

As an applicant submitting an Application, I provide my company's commercial name (`Empresa solicitante`, e.g., "Sazón Vegetariano") in addition to my personal legal name (`Representante`), so the resulting Funding Agreement PDF shows both correctly on its cover page.

**Why this priority**: Without this, the `Empresa solicitante` field on the PDF cover is blank or duplicated from the applicant's personal name, which misrepresents the legal subject of the funding. The legal-personal vs commercial-entity distinction matters for downstream auditing and signing.

**Independent Test**: An applicant opens the application form, attempts to submit without filling the company-name input, observes a validation error, fills the input, and submits successfully. The captured name persists with the Application and renders on the PDF cover page.

**Acceptance Scenarios**:

1. **Given** an applicant is filling the application form, **When** they submit without entering a company name, **Then** the system rejects the submission with a user-facing validation error indicating the company name is required, and no Application is persisted.
2. **Given** an applicant enters a company name (length ≤ 200 characters) and submits a complete application, **Then** the Application persists with the supplied name, and downstream PDF generation renders it verbatim in the `Empresa solicitante` cover-page field.

---

### Edge Cases

- **Zero rejected items** → "2. Líneas no aprobadas" header, bulleted reasons list, and rejected-lines table all omitted; no empty sections rendered.
- **Zero approved items** → cover, intro, requested-resources, and rejected sections still render; supplier-verification table omitted (no distinct approved suppliers); sworn-declaration approved-lines table omitted; summary paragraph adapts to "No se aprueban líneas en este tracto." (or equivalent committee-decision language).
- **Single reviewer who took action** → "Comisión evaluadora" lists one name; no plural-count language adaptation required.
- **Reviewer takes action then is unassigned from the Application** → still appears on the committee list (action history is the source of truth, not current assignment).
- **Mixed-currency lines (CRC + USD)** → existing per-line currency-conversion notes (spec 015 format) render inside the new tables unchanged.
- **Page break inside a table** → the table header band repeats on the continuation page so the reader never sees a headerless table fragment.
- **Long product name or rejection reason** → row height grows to accommodate the wrapped text; no truncation; footer logo strip stays anchored to the bottom of the page.
- **Applicant submits with leading/trailing whitespace in `CompanyName` or reviewer submits whitespace-only `LineCode`** → values are trimmed; if the trimmed result is empty, the same required-field validation fires.
- **Applicant tries to edit an already-submitted Application's `CompanyName`** → out-of-scope mutation rules; this spec only governs initial submission. Existing Application-edit permissions apply.

## Requirements *(mandatory)*

### Functional Requirements

**Branding chrome**

- **FR-001**: System MUST render a header element at the top of every page of the Funding Agreement PDF showing the brand seedling logo asset (`header-seedling.png`) at approximately 60pt diameter, vertically centered above the content area.
- **FR-002**: System MUST render a footer element at the bottom of every page of the Funding Agreement PDF showing the partner-logo composite asset (`footer-partners-strip.png`, gold dotted divider + Banca para el Desarrollo SBD + CROCUS + nexo + Programa Semilla + 10 años badge baked into a single image) spanning the content width at approximately 50pt tall.
- **FR-003**: System MUST size the PDF page as A4 portrait with 20mm top and bottom margins and 18mm left and right margins.
- **FR-004**: System MUST apply the brand color palette (brand teal sampled from the seed for headings + table-header band, body text in near-black, alternating cream row shading, gold footer divider) and brand typography (Fraunces serif for titles + section headings, Inter for body + table cells) using locally-vendored font files only — no CDN dependencies.

**Document structure (cover → declaration)**

- **FR-005**: System MUST render Page 1 as a cover page containing: the brand title "Informe de evaluación de solicitudes de desembolso" in Fraunces serif at approximately 32pt left-aligned with a horizontal teal divider beneath it; an applicant block (`Empresa solicitante: <Application.CompanyName>`, `Representante: <Application.Applicant.LegalName>`, `Fecha de emisión: <generation date in es-CR long format>`); and a `Comisión evaluadora:` block listing one name per line.
- **FR-006**: System MUST populate the `Comisión evaluadora` list with the distinct names of users who took at least one review action (approval or rejection) on this Application; reviewers who were assigned but took no action MUST NOT appear.
- **FR-007**: System MUST render Page 2 as an intro page containing the centered subtitle "Informe de evaluación de solicitudes de desembolso" (smaller, bold sans-serif) and three fixed Spanish paragraphs that introduce the disbursement review process (copy hardcoded verbatim from the seed template page 2).
- **FR-008**: System MUST render a `Recursos solicitados` section listing every item on the Application (both approved and rejected) in a table with columns: `Tipo` (item product name), `Descripción` (item category name), `Variable` (reviewer-assigned line code), `Monto` (selected supplier quotation total in es-CR currency format with ₡ prefix), `Empresa seleccionada` (selected supplier commercial name).
- **FR-009**: System MUST render a `Resultados comisión` section containing: an auto-composed summary paragraph that lists approved line codes and the summed approved disbursement total ("Se aprueban las líneas <csv> por un monto total de ₡<sum>, …"); a bulleted list of rejected lines, one bullet per line in the form "Línea <code>: <rejection reason>"; a `Líneas aprobadas` subtable with columns `Acuerdo`/`Detalle`/`Variable`/`Tipo`/`Empresa proveedora`/`Desembolso`; and a `Líneas no aprobadas` subtable with the same first 4 columns plus a `Motivo` column.
- **FR-010**: System MUST render an `Información empresas proveedoras` section in a table with columns `Fecha de revisión`, `Empresa proveedora`, `Hacienda`, `CCSS`, `SICOP`, with one row per distinct supplier referenced in the approved-lines set, sourcing the compliance-status values from the existing supplier catalog (spec 013).
- **FR-011**: System MUST render a sworn declaration page containing: hardcoded preamble and clauses (PRIMERO/SEGUNDO/TERCERO/CUARTO/QUINTO) verbatim from the seed template referencing "Sistema de Banca para el Desarrollo" + "Fondo CROCUS de Programa Semilla"; a re-embedded approved-lines table with column order `Acuerdo`/`Detalle`/`Tipo`/`Variable`/`Empresa`/`Desembolso`; closing line "Leída la presente declaración y consciente de su alcance legal, firmo digitalmente"; and an empty rounded-rectangle signature box positioned where the existing digital-signature ceremony will stamp.

**Reviewer-side data capture**

- **FR-012**: System MUST require the reviewer to provide a non-blank line-code value for each item before the per-item review decision can be persisted; submissions missing a line code MUST be rejected with a user-facing validation error and no state change.
- **FR-013**: System MUST treat line-code values as free-text bounded to 16 characters maximum, scoped uniquely within a single Application (two items in the same Application MUST NOT share a line code).
- **FR-014**: System MUST trim leading/trailing whitespace from line-code input before validation and storage; whitespace-only input MUST fail the non-blank check.

**Applicant-side data capture**

- **FR-015**: System MUST require the applicant to provide a non-blank company-name value at Application submission; submissions missing it MUST be rejected with a user-facing validation error.
- **FR-016**: System MUST persist the company-name value as a required (non-nullable) field on the Application, bounded to 200 characters maximum, and MUST trim leading/trailing whitespace before validation and storage.

**Renderer + asset edit ergonomics**

- **FR-017**: System MUST replace the entire current Funding Agreement PDF rendering pipeline (current `Document.cshtml` and partials) with the new structure; no parallel renderer, no version flag, no toggle.
- **FR-018**: Developers MUST be able to swap the header-logo asset by replacing one file (`wwwroot/lib/brand/pdf/header-seedling.png`) and the footer composite by replacing one file (`wwwroot/lib/brand/pdf/footer-partners-strip.png`) with no code change.

**Cleanup of legacy generic-template artifacts**

- **FR-019**: System MUST remove all configuration keys for funder identity (`FundingAgreement:Funder:LegalName`, `Funder:TaxId`, `Funder:Address`, `Funder:ContactEmail`, `Funder:ContactPhone`) from configuration files (`appsettings*.json`), options binding classes, dependency-injection registrations, and project documentation (specifically the configuration-knobs table in `CLAUDE.md`); funder identity in the new document is fully hardcoded inside the sworn declaration copy.
- **FR-020**: System MUST remove the funder-data-transfer block from the Funding Agreement document view model and delete its associated DTO/view-model types.
- **FR-021**: System MUST stop rendering applicant fields that the new document does not display (e.g., applicant email, applicant phone, applicant legal id) from the PDF view chain; the upstream entity/DTO is preserved if other (non-PDF) screens still consume it.
- **FR-022**: System MUST stop rendering the agreement-reference identifier on any page of the PDF; the identifier is retained internally for storage, file naming, and audit only.
- **FR-023**: System MUST remove the prior partials and CSS classes that supported the legacy parties block, terms-and-conditions placeholder, signature blocks, and document-reference banner; no orphan CSS rule MAY remain in the layout stylesheet.
- **FR-024**: System MUST retire the prior spec-005 R-005 placeholder rule (the visible "MARCADOR DE POSICIÓN — NO ES VERSIÓN FINAL" banner) for this document; the sworn declaration is the canonical legal block.

### Key Entities *(include if feature involves data)*

- **Application**: Gains a required, non-nullable `CompanyName` field (≤ 200 chars) representing the commercial entity name that distinct from the applicant representative's legal name. Existing relationships unchanged.
- **Item**: Gains a required `LineCode` field (≤ 16 chars, free text, unique within the parent Application) representing the reviewer-assigned identifier that surfaces in the PDF `Variable`/`Detalle` columns.
- **ItemResponse** (existing, no shape change): supplies approval/rejection state and rejection reason for the `Resultados comisión` and `Líneas no aprobadas` tables.
- **Supplier** (existing, no shape change): supplies compliance-status fields (Hacienda/CCSS/SICOP) for the `Información empresas proveedoras` table.
- **ApplicationUser / Review action history** (existing, no shape change): supplies the distinct-action-takers set for the `Comisión evaluadora` list.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer or designer with the seed template (`brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`) open side-by-side against the generated PDF identifies no missing element, no extra element, and no positional element off by more than five typographic points across the cover, intro, requested-resources, committee-results, supplier-verification, and sworn-declaration pages. (This ±5pt tolerance also governs the "approximately" sizing language in FR-001/FR-002/FR-005.)
- **SC-002**: For the seed scenario (applicant "Sazón Vegetariano" / representative "Daniel Centeno Bejarano" / six items coded T1-1 through T1-6 / committee Paola Rodríguez + Milena Arias + Aldo Protti), the generated PDF reproduces the seed verbatim (modulo content driven by genuine database values that match the seed dataset).
- **SC-003**: Reviewers cannot persist a per-item review decision without a non-blank line code; unique-within-Application enforcement prevents duplicate codes; both rules are demonstrated by integration tests against a real database.
- **SC-004**: The `Comisión evaluadora` cover-page list contains exactly the distinct users who took at least one review action on the Application; assigned-but-no-action reviewers are absent. Demonstrated by an integration test where three reviewers are assigned, only two take an action, and exactly two names render.
- **SC-005**: Replacing only the header asset file and only the footer asset file (without any code change) yields the new images in the next-generated PDF; verified by manual swap-and-render in development.
- **SC-006**: The visible legacy placeholder banner ("MARCADOR DE POSICIÓN — NO ES VERSIÓN FINAL") is absent from the text layer of every generated PDF.
- **SC-007**: A PDF generated by the new pipeline passes through the existing digital-signature ceremony (spec 006) without breaking, and the signature stamp lands inside the declaration-page signature box.
- **SC-008**: Applicant cannot submit an Application without a non-blank company name; the captured value renders verbatim on the PDF cover page; demonstrated by an integration test against a real database.
- **SC-009**: PDF generation completes within three seconds at the 95th percentile for an Application with up to thirty items and ten distinct suppliers, on the Aspire dev environment baseline (developer workstation running the AppHost-orchestrated stack with the SQL Server container). The same scenario MUST also stay within the existing repo's perf-baseline scripts (`scripts/perf/`) if those gates apply to PDF generation.
- **SC-010**: A Playwright E2E test (per Constitution III) drives the funder operator from the Application detail page through PDF generation and download, then asserts that the downloaded PDF text layer contains all expected section headings: "Recursos solicitados", "Resultados comisión", "Información empresas proveedoras", and "DECLARO BAJO LA FE DEL JURAMENTO". Covers US1 golden path.
- **SC-011**: A Playwright E2E test (per Constitution III) drives a reviewer through the per-item review form, attempts submission without a line code (asserts the user-facing required-field error appears), then submits with a non-blank code (asserts the decision persists and the next item becomes available). Also covers the duplicate-code rejection scenario from US2 acceptance scenario 4. Covers US2 golden + key-error paths.
- **SC-012**: A Playwright E2E test (per Constitution III) drives an applicant through the application form, attempts submission without a company name (asserts the user-facing required-field error appears), then submits with a non-blank name (asserts the Application persists and the cover page of the subsequently generated PDF renders the supplied value). Covers US3 golden + key-error paths.

## Assumptions

- **No production data**: This feature ships before the platform sees production users, so no migration shim is required for either the new `CompanyName` field on Application or the new `LineCode` field on Item; both are non-nullable from day one and any seed/fixture data will be regenerated to match.
- **Sworn declaration copy is canonical**: The Spanish text on the seed template's pages 5–6 (preamble + PRIMERO–QUINTO clauses + closing) is treated as Legal-approved canonical copy and hardcoded in the Razor partial. If Legal subsequently revises it, this spec's FR-011 + SC-006 are revisited. *(see [NEEDS CLARIFICATION])*
- **Color hex codes sampled from PDF**: Brand teal, cream, and gold values are sampled from the seed asset and may differ from a future-published brand guideline by 1–2 hex values; the rendered PDF is the visual contract. If a brand guideline supersedes the sampled values, NFR-001 is revisited.
- **Footer is one composite image in v1**: Adding/removing/reordering individual partner logos requires externally re-cutting `footer-partners-strip.png`; per-logo `<img>`-level edit ergonomics are deferred until clean per-logo source files are sourced (out of scope for this spec).
- **`Application.CompanyName` may be surfaced on existing list/detail screens**: Decision deferred to the planning phase; this spec mandates only the applicant-form capture and PDF cover-page rendering.
- **Line-code uniqueness scope = per-Application**: Two items in the same Application cannot share a line code; codes may be repeated freely across different Applications.
- **Existing renderer reused**: The HTML→PDF rendering engine (Syncfusion Blink) is reused unchanged; this spec changes only the HTML/CSS input to the engine and the engine's page-margin settings.
- **Brand assets already extracted**: Three branding assets have been pulled from the seed PDF and stored at `src/FundingPlatform.Web/wwwroot/lib/brand/pdf/`: `header-seedling.png` (61KB), `footer-partners-strip.png` (58KB, composite), `signature-box.png` (1.8KB). Whether the signature box is rendered as a PNG or as a CSS-drawn rounded rectangle is a planning-phase decision.
- **Vendored fonts present**: Fraunces, Inter, and JetBrains Mono are already vendored under `wwwroot/lib/fonts/` and require no new dependency.
- **Localization unchanged**: es-CR culture configuration via the existing `EsCrCultureFactory` continues to govern currency (`₡` prefix, `1,234.56` format) and date formatting (`dd/MM/yyyy` for emission line, `dd-MM-yyyy` for table `Acuerdo` column).
- **Multi-currency conversion notes carry through**: Per-line currency-conversion notes from spec 015 render inside the new tables in their existing format.
- **Validation placement (per Constitution II)**: Required-field invariants for `Application.CompanyName` (FR-015/FR-016) and `Item.LineCode` (FR-012/FR-013/FR-014) MUST live on the entities themselves (e.g., as behavior methods like `Application.SetCompanyName(string)` and `Item.AssignLineCode(string)`), not in controllers or services. Plan phase MUST honor this in its Constitution Check.

## Dependencies

- **Spec 002 (review-approval workflow)**: provides the per-item approval/rejection decisions, rejection reasons, and reviewer-action history that feed the `Comisión evaluadora` list and the committee-results section.
- **Spec 005 (funding-agreement-generation)**: existing controller, service, and renderer entry point are reused; the spec-005 R-005 placeholder rule is retired for this document.
- **Spec 006 (digital-signatures)**: downstream consumer; the new sworn-declaration page exposes the signature box that the digital-signature ceremony stamps.
- **Spec 012 (es-cr-localization)**: `EsCrCultureFactory` continues to govern currency + date formatting in the new layout.
- **Spec 013 (supplier-catalog)**: provides Hacienda/CCSS/SICOP compliance-status fields on each Supplier that feed the supplier-verification table.
- **Spec 015 (multi-currency)**: per-line conversion notes feed the new tables unchanged.

## Out of Scope

- Admin UI for managing the logo set (file-swap on disk is sufficient).
- Multi-tract data model (line code is free-text; no `Tract` entity is introduced).
- Localization beyond es-CR.
- Branded PDF for any document type other than the Funding Agreement.
- Database-backed storage of the legal copy (sworn declaration text remains hardcoded in the Razor partial).
- Visual differential testing automation (visual fidelity verified by manual side-by-side per SC-001).
- Backfilling `Application.CompanyName` for legacy data (no production data exists).
- Sourcing five separate per-partner logo files (composite footer is sufficient for v1).
- Broader applicant-form revisions beyond adding the company-name input.
- Broader reviewer-form revisions beyond adding the line-code input.

## Open Clarifications

- **[NEEDS CLARIFICATION]** Is the sworn-declaration copy on the seed template (preamble + PRIMERO–QUINTO clauses + closing) canonical Legal-approved text, or is the seed itself a draft? If draft, FR-011 + FR-024 + SC-006 must keep a visible placeholder banner until Legal signs off, mirroring the prior spec-005 R-005 rule. Default assumption (until answered): canonical.
