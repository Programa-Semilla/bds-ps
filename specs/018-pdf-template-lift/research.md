# Phase 0: Research — PDF Template Lift

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md)
**Date:** 2026-05-08

## Open Clarifications Resolution

### CLARIFICATION-1 — Sworn-declaration legal canonicity

- **Source**: spec.md → Open Clarifications → "Is the sworn-declaration copy on the seed template (preamble + PRIMERO–QUINTO clauses + closing) canonical Legal-approved text, or is the seed itself a draft?"
- **Decision**: Treat as **canonical** per the spec's documented default.
- **Rationale**: The spec explicitly defaults to canonical when no answer arrives. The seed template is the only ground truth available for this feature. If Legal subsequently revises, FR-011 + FR-024 + SC-006 are revisited (spec already tracks this re-entry point).
- **Alternatives considered**: Keep the spec-005 R-005 placeholder banner ("MARCADOR DE POSICIÓN — NO ES VERSIÓN FINAL"). Rejected because FR-024 retires the banner for this document and the brand seed has no placeholder. Re-introducing the banner would defeat SC-006.

## Implementation Research

### R-001 — Header / footer rendering on every page (Syncfusion Blink)

- **Question**: How do we make `header-seedling.png` and `footer-partners-strip.png` repeat on every PDF page (cover through declaration) inside Blink-based HTML→PDF?
- **Decision**: Use CSS `@page` margin boxes plus CSS `position: fixed` on the brand header / footer wrappers, then increase `@page` top/bottom margins so content does not overlap. Margins resolved per FR-003: A4 portrait, 20mm top + 20mm bottom + 18mm left/right. Header band consumes ~18mm of the 20mm top margin; footer band consumes ~14mm of the 20mm bottom margin. Header is `position: fixed; top: 0` with the seedling PNG vertically centred; footer is `position: fixed; bottom: 0` with the partner strip spanning the content width.
- **Rationale**: `position: fixed` content is duplicated on every page by Blink's pagination and is the same approach Syncfusion's own samples use. CSS `@page` margins are honoured by Blink in HTML→PDF mode. Avoids touching `BlinkConverterSettings.PdfHeader/PdfFooter` (which require Syncfusion `PdfPageTemplateElement` wiring at the renderer level).
- **Alternatives considered**:
  - `BlinkConverterSettings.PdfHeader` / `PdfFooter` template elements at the renderer level (requires drawing the asset via Syncfusion's `PdfBitmap` from an embedded stream — more code paths, brittler asset wiring, harder to swap). Rejected.
  - HTML `<header>` / `<footer>` tags repeated per page with `@media print` rules (Blink ignores `@media print` for some declarations). Rejected.
- **References**: Syncfusion HTMLConverter "Convert specific HTML to PDF using HTML position technique" + general guidance that fixed-position elements are repeated per page.

### R-002 — Signature box rendering: PNG vs CSS rounded rectangle

- **Question**: Render the signature box as `signature-box.png` (1.8KB) or as a CSS-drawn rounded rectangle on the declaration page?
- **Decision**: **CSS-drawn rounded rectangle** — `border: 1px solid <near-black>; border-radius: 6pt; height: 80pt; width: 100%`.
- **Rationale**:
  1. CSS gives crisp lines at any DPI; the PNG is rasterised and may show edge artefacts at high zoom.
  2. CSS lets us anchor the box exactly where the spec-006 digital-signature ceremony stamps (the ceremony stamps at coordinates derived from a known anchor element with a stable `id`); a `<div id="signature-box">…</div>` is a more reliable anchor than an `<img>` for downstream positioning.
  3. One fewer asset to keep in sync.
  - The PNG remains on disk for now (no harm; deletion deferred to avoid churn) and is unreferenced.
- **Alternatives considered**: Keep `signature-box.png` and embed it via `<img>` inside the declaration. Rejected on the rationale above.

### R-003 — Page break inside tables (header band repeats)

- **Question**: How do we guarantee the table header repeats on the continuation page when a long table breaks?
- **Decision**: Use HTML `<thead>` for the header band and the CSS rule `thead { display: table-header-group; }` plus per-row `tr { page-break-inside: avoid; }` on body rows. Blink honours `display: table-header-group` and repeats `<thead>` on each page.
- **Rationale**: This is the standard CSS+Blink contract for repeating headers; per-row break-avoidance prevents row content from splitting across pages while still allowing tables themselves to span pages.
- **Alternatives considered**: Pre-paginate the table in code (project the rows into per-page buckets and emit one `<table>` per page). Rejected — adds projection complexity and breaks a single semantic table.

### R-004 — Currency formatting in the new tables

- **Question**: How are CRC and USD amounts formatted in the new tables?
- **Decision**: Reuse existing `EsCrCultureFactory` + the spec-015 currency-conversion-note rendering. CRC amounts use `₡1,234.56` format (₡ prefix, comma thousands, dot decimals). Per-line conversion notes from spec 015 — e.g. `($100.00 × ₡520.00 = ₡52,000.00)` — render inside the new tables unchanged. The summary paragraph total in `Resultados comisión` sums approved disbursements in CRC and renders as `₡<sum>` using the same formatter.
- **Rationale**: spec assumption explicitly mandates this. No new formatting code is needed; only the projection mapping changes.
- **Alternatives considered**: Introduce a per-currency total (e.g. "₡X plus US$Y"). Rejected — spec 015 already settled this in CRC for the agreement total.

### R-005 — Distinct committee evaluators source

- **Question**: Where do we read the distinct review-action takers for the `Comisión evaluadora` cover-page list (FR-006)?
- **Decision**: Query `VersionHistory` rows where `Action == "ReviewItem"` for the Application, dedupe by `UserId`, hydrate the user's display name via the existing `ApplicationUser` lookup, and emit one name per line. Reviewers assigned but who took no action are excluded by construction (no `ReviewItem` row → no entry).
- **Rationale**: `VersionHistory` is the existing source of truth for review actions; spec 002 wires it. No new audit table is needed.
- **Alternatives considered**: Query `ItemResponses` (or per-item review state) directly. Rejected — that captures the latest decision per item, not the distinct action history; a reviewer who later got their action overridden by another reviewer would be missed.

### R-006 — Schema migration with no production data

- **Question**: Two NOT NULL columns are being added to existing tables (`Applications.CompanyName`, `Items.LineCode`). How is dacpac deployment handled?
- **Decision**: Add the columns directly as `NOT NULL` with no default; fail-on-data-loss is acceptable because no production data exists (spec assumption). The Aspire dev container is recreated on each `--EphemeralStorage=true` test run; the persistent dev volume can be wiped on developer workstations as a one-shot. No dacpac pre-deployment or backfill script is required.
- **Rationale**: spec assumption explicitly waives the migration shim.
- **Alternatives considered**: Add the columns nullable in v1, then tighten to NOT NULL in v2. Rejected — adds two-pass complexity for a non-existent risk.

### R-007 — Application form: where is `CompanyName` captured?

- **Question**: The current `Application/Create.cshtml` page has zero inputs (just a "Create draft" button). Where does the applicant enter `CompanyName`?
- **Decision**: Add a required `CompanyName` `<input>` to `Application/Create.cshtml`. The Create form becomes "1 input + Submit". `CreateApplicationCommand` gains a `CompanyName` parameter; `ApplicationService.CreateApplicationAsync` passes it through to `new Application(applicantId, companyName)` (constructor signature change). Validation lives on `Application.SetCompanyName(string)` (also called from the constructor's required-field path) so the controller is a thin pass-through per Constitution II.
- **Rationale**: Because `CompanyName` is non-nullable from day one (spec assumption), it cannot exist as "set later" state — the simplest model captures it at Create. Spec FR-015 says "at Application submission"; "submission" here is interpreted as the moment the Application enters durable state for the first time, which aligns with Create in this codebase (the Edit step adds items, not Application-level fields).
- **Alternatives considered**:
  - Capture at Submit-time on `Application/Edit` or `Application/Details` Submit form. Rejected — would require a nullable `CompanyName` until Submit, contradicting "non-nullable from day one".
  - Capture on a new dedicated "Empresa" sub-page. Rejected — extra navigation for one field.

### R-008 — Reviewer form: per-item LineCode input UX

- **Question**: Where does the reviewer enter `LineCode` for each item being reviewed?
- **Decision**: Add a required `LineCode` `<input>` next to the existing per-item review action group on `Review/Review.cshtml`. The existing `ReviewItem` POST signature gains a `LineCode` parameter; the controller forwards to `ReviewService.ReviewItemAsync(applicationId, itemId, decision, comment, selectedSupplierId, lineCode, userId)`. Validation lives on the aggregate root: `Application.AssignLineCodeToItem(int itemId, string lineCode)` enforces non-blank-after-trim, ≤16 chars, and per-Application uniqueness in one method, then delegates to `Item.AssignLineCode(string)` for the field write. The decision call (`Approve`/`Reject`/`RequestMoreInfo`) remains a separate domain method on `Item`. The service composes the two calls — but `LineCode` is required only when `Decision ∈ {Approve, Reject}` (matching spec US2 acceptance scenario 1 and the Contract 2 rule). For `RequestMoreInfo`, the reviewer is iterating on the item; LineCode capture is allowed (the input is still a free-text field on the form) but **not required** — the controller skips the `AssignLineCodeToItem` call when LineCode is blank and the decision is `RequestMoreInfo`. A failure in either step rolls back the whole transaction.
- **Rationale**:
  - Per Constitution II, uniqueness within Application is an aggregate-level invariant, so the check belongs on `Application`, not on `Item`.
  - Coupling both updates in one service call lets us also enforce a "LineCode required at the moment of Approve/Reject" rule in a single transactional boundary (spec FR-012 + US2 acceptance scenario 1). `RequestMoreInfo` is allowed to bypass the LineCode requirement so reviewers can bounce an item for clarification before they have decided on a code.
- **Alternatives considered**:
  - Separate "assign code" endpoint (one round-trip per item before the review decision). Rejected — extra friction for the reviewer; risk of orphaned codes on items the reviewer abandoned.
  - Make LineCode nullable until Approve/Reject. Rejected — entity is non-nullable from day one (spec assumption), same reasoning as R-007.

### R-009 — Cleanup of legacy view-model / DTO / config (FR-019..024)

- **Question**: Inventory of artefacts to remove and the exact removal order so we don't leave compile errors mid-implementation.
- **Decision (removal order)**:
  1. Update Razor (`Document.cshtml` + new partials) to **stop reading** Funder, Email, Phone, LegalId, AgreementReference fields (they remain on the view-model, just unused).
  2. Update `FundingAgreementDocumentViewModel` to drop those properties. Update `FundingAgreementService` projection to stop emitting them.
  3. Delete `FunderOptions` and its DI registration / binding (`FundingAgreement:Funder` section). Delete the AppHost `WithEnvironment` lines for `Funder:LegalName/TaxId/Address/ContactEmail/ContactPhone`.
  4. Remove the `FundingAgreement:Funder:*` rows from the `CLAUDE.md` configuration-knobs table (FR-019 cite).
  5. Delete obsolete Razor partials (`_FundingAgreementHeader`, `_FundingAgreementItemsTable`, `_FundingAgreementSignatureBlocks`, `_FundingAgreementTermsAndConditions`) and any CSS rules whose only consumer was those partials (FR-023).
- **Rationale**: Renderer first, then DTO, then config, then assets — matches dependency direction so the build stays green at each step.
- **Alternatives considered**: Big-bang delete in one commit. Rejected — review burden + harder rollback if any branch was missed.

### R-010 — Performance baseline (SC-009)

- **Question**: How is the 3-second-p95 PDF-generation budget verified?
- **Decision**: Reuse the existing `scripts/perf/` baseline approach — add or extend a perf script that times `IFundingAgreementPdfRenderer.RenderAsync` end-to-end across a 30-item / 10-supplier fixture, runs ≥10 iterations on the AppHost dev stack, and emits p95. Run on the developer workstation; do not block CI on this number (the spec calls it a measurable outcome, not a gate). Capture the baseline number in the perf-script README or an existing baseline JSON.
- **Rationale**: Reuse existing infrastructure (Constitution VI). Existing `scripts/perf/` is the documented home per `CLAUDE.md`.
- **Alternatives considered**: Add a dedicated benchmark project. Rejected — out of scope, not justified for one new render path.

## Outstanding Risks

| # | Risk | Mitigation |
|---|------|------------|
| 1 | Brand teal hex sampled by hand may differ from a future brand-guideline value by 1–2 hex points | NFR-001 + spec assumption already track this; the rendered PDF is the contract until a brand guideline supersedes |
| 2 | Blink CSS support gaps for `position: fixed` headers across page breaks on long tables | Smoke-test in implementation phase against a 50-row-table fixture; fall back to `@page` margin boxes only if `position: fixed` regresses |
| 3 | Sworn declaration is hardcoded copy in Spanish; legal-side change implies a code change | Tracked in spec assumption; out of scope for this spec |
| 4 | Existing E2E tests that reference the deleted partials by selector will need rewrites | Within scope (spec assumption + project memory: "UI quality > E2E selector stability"). Tasks include the rewrite. |
