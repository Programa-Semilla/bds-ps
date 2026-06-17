# Feature Specification: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Feature Branch**: `035-line-item-category-templates`
**Created**: 2026-06-12
**Evolved**: 2026-06-16 (impact model revised — see Evolution Log)
**Status**: Draft
**Input**: User description: "Reshape the applicant submission flow so line-item detail is captured through category-driven dynamic fields, the application declares one or more impacts (each with its own template fields), each line item is attributed to one or more of those impacts with a short justification, and a vendor's multi-product quotation can be reused across line items in the same application without re-uploading. Replace the free-text technical-specifications field with structured, admin-configured, per-category fields. Tear out the obsolete impact-template gating, leaving no dead code."

## Overview

Today an applicant describes each line item of a funding application with a single free-text "technical specifications" field, declares one **impact** for the whole application, and must re-enter a vendor and re-upload the vendor's quotation document for every line item — even when one vendor quote covers several products.

This feature restructures the line-item experience around these changes that share the same "add a line item" flow:

1. **Category-driven fields** — each submission category carries an admin-configured set of fields. When the applicant picks a category for a line item, those fields appear and capture the line's detail. This replaces the free-text technical-specifications field.
2. **Application-level impacts (one or more)** — an application declares one or more **impacts**. Each selected impact carries its own set of fields (from its impact template) which the applicant completes; each impact's fields are validated independently. This generalizes the former single application-wide impact to a set of impacts.
3. **Per-line-item impact attribution + justification** — when adding or editing a line item, the applicant selects which of the application's declared impacts that line item supports (a multi-select limited to the application's impacts) and writes a short justification explaining why the item supports the selected impact(s). The line item does **not** re-enter impact field data; it references the application's impacts.
4. **Quotation reuse within an application** — a vendor quote that lists several products is captured as one line item per product, but the vendor and the uploaded document are entered once and reused by the sibling line items, each keeping its own price.

Because this submission flow is not yet in production, there is no data to migrate. The obsolete free-text technical-specifications field and the now-unused mechanism that limited which impact templates a process could offer are removed entirely, with no vestigial code left behind.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin configures the fields a category collects (Priority: P1)

An administrator opens a submission category and defines the ordered set of fields that applicants must complete when they choose that category for a line item. Each field has a user-facing label, an internal key, a data type (text, decimal, integer, or date), a required/optional flag, and a display order. The administrator can add, edit, reorder, and remove fields.

**Why this priority**: Without configured category fields, the applicant flow has nothing to render. This is the foundation the rest of the feature builds on, and it is independently demonstrable.

**Independent Test**: Create a category, add several fields of different data types (some required), reorder them, edit a label, remove one, and confirm the category's field set persists and renders in the expected order with the correct input control per data type.

**Acceptance Scenarios**:

1. **Given** an existing category with no fields, **When** the admin adds a required decimal field labeled "Costo unitario" and an optional text field labeled "Marca", **Then** both fields are saved on the category in the defined order with their data types and required flags.
2. **Given** a category with three fields, **When** the admin reorders them, **Then** the new display order is persisted and reflected wherever the category's fields render.
3. **Given** a category field that is no longer needed, **When** the admin removes it, **Then** it no longer appears for new line items.
4. **Given** the admin is configuring fields, **When** they save, **Then** all labels and validation messages are presented in es-CR.

---

### User Story 2 - Applicant declares the application's impacts (Priority: P1)

An applicant building a draft application declares one or more **impacts** for the application. They select an impact from the available active impact templates; the impact's configured fields appear and the applicant completes them. They can add more impacts, each with its own fields, and remove an impact they no longer want. Each impact's required fields are validated independently. At submission, the application is accepted only when it declares at least one impact and every declared impact has its required values completed.

**Why this priority**: Impact data collection is the backbone the per-line-item attribution depends on. A line item can only be attributed to impacts that exist on the application, so the application's impacts must be declarable first. Independently demonstrable.

**Independent Test**: Create a draft application, add two impacts from active impact templates, complete each impact's required fields, remove one impact, and confirm the remaining impact and its values persist. Attempt to submit with zero impacts, or with a declared impact missing a required value, and confirm submission is blocked with an es-CR message; complete the values and confirm the block clears.

**Acceptance Scenarios**:

1. **Given** a draft application with no impacts, **When** the applicant adds an impact and selects an active impact template, **Then** that template's configured fields are displayed with the input control matching each field's data type.
2. **Given** an application with one declared impact, **When** the applicant adds a second impact, **Then** both impacts appear as distinct sections, each clearly identified by its impact name and its own fields.
3. **Given** a declared impact with a blank required field, **When** the applicant attempts to submit, **Then** submission is blocked and the missing field is reported per-impact in es-CR.
4. **Given** an application with two declared impacts, **When** the applicant removes one, **Then** that impact and its captured values are removed and any line-item attribution to it is also removed.
5. **Given** an application with no declared impacts, **When** the applicant attempts to submit, **Then** submission is blocked because an application must declare at least one impact.
6. **Given** no active impact templates exist, **When** the applicant opens the impacts step, **Then** the flow surfaces this clearly rather than allowing submission without any impact.

---

### User Story 3 - Applicant captures a line item via category fields, impact attribution, and a short justification (Priority: P1)

An applicant building a draft application adds a line item. They first select a category; the category's configured fields appear and the applicant completes them (replacing the old free-text technical-specifications entry). They enter the line's product name. They then select, via a multi-select limited to the application's declared impacts, which impact or impacts that line item supports, and write a short justification explaining why the item supports the selected impact(s). The applicant repeats this for each line item. At submission, the application is accepted only when every line item has its required category fields completed, at least one attributed impact, and a non-empty justification.

**Why this priority**: This is the core of the restructured line-item entry — category-driven detail plus the relocation of impact from a per-item data entry to an attribution against the application's impacts. It delivers the primary applicant value and exercises the teardown of the old per-item impact wiring.

**Independent Test**: With an application that already declares at least one impact, create a line item, pick a category, complete its required fields, attribute the line to one or more impacts, write a justification, and confirm the line item saves. Attempt to submit with a required category field blank, with zero impact attributions, or with an empty justification, and confirm submission is blocked with an es-CR message; complete the fields and confirm submission succeeds.

**Acceptance Scenarios**:

1. **Given** a draft application, **When** the applicant adds a line item and selects a category, **Then** that category's configured fields are displayed with the input control matching each field's data type.
2. **Given** a line item with a selected category, **When** the applicant leaves a required category field blank and attempts to submit, **Then** submission is blocked and the missing field is reported in es-CR.
3. **Given** a line item, **When** the applicant opens the impact attribution control, **Then** the only options are the impacts already declared for the current application (never impacts from other applications, and never raw impact templates that the application has not declared).
4. **Given** a line item with no impact attributed, **When** the applicant attempts to submit, **Then** submission is blocked because each line item must support at least one of the application's impacts.
5. **Given** a line item, **When** the applicant leaves the impact justification empty and attempts to submit, **Then** submission is blocked because a short justification is required.
6. **Given** the impact-justification field, **When** the applicant types, **Then** the input is constrained to a short length (a hard cap of 300 characters) and presented as a short textarea, not a long free-text body.
7. **Given** an applicant changes a line item's category after filling fields, **When** they confirm the change, **Then** the previous category's field values are cleared and the new category's fields are presented empty (the impact attribution and justification are unaffected).
8. **Given** a submitted application, **When** anyone reads it, **Then** each line item shows the application's impacts it is attributed to and its justification — line items do not carry their own impact field values.

---

### User Story 4 - Applicant reuses a multi-product vendor quotation across line items (Priority: P2)

An applicant has a single vendor quotation document that lists five products at five prices. They create five line items. On the first line item they select the vendor (supplier and branch), enter that product's price/currency/validity, and upload the quotation document. On each subsequent line item they choose **reuse an existing quotation** from the same application; the vendor and the already-uploaded document are carried over automatically, and the applicant only enters that line's own price, currency, and validity. The applicant may also add a brand-new quotation on any line item when it belongs to a different vendor or document.

**Why this priority**: Removes a major friction point (re-uploading and re-typing vendor data) while preserving the rule that each quoted product is its own line item. Valuable but layered on top of the core flow.

**Independent Test**: In one application, add a quotation with an uploaded document on line item A, then on line item B choose reuse, confirm the vendor and document are pre-filled and the document is not re-uploaded, enter a different price, and save. Confirm A's price is unchanged when B's price is edited. Confirm quotations from other applications are never offered for reuse.

**Acceptance Scenarios**:

1. **Given** line item A has a quotation with an uploaded document, **When** the applicant adds a quotation to line item B and selects reuse of A's quotation, **Then** B's quotation carries A's supplier, branch, and document while the applicant supplies B's own price, currency, and validity.
2. **Given** B reuses A's document, **When** the applicant edits B's price, **Then** A's price is unaffected.
3. **Given** an applicant is adding a quotation, **When** they view reuse options, **Then** only quotations already present in the same application are offered (never quotations from other applications).
4. **Given** a vendor quote covering five products, **When** the applicant builds the application, **Then** they create five separate line items (the system does not allow one generic line item to represent multiple quoted products).
5. **Given** the line item that originally uploaded a shared document is deleted, **When** another line item still references that document, **Then** the document is retained; it is removed only when the last referencing quotation is removed.

---

### User Story 5 - Category values, impact attribution, and justification are visible on every application surface (Priority: P3)

Everywhere an application is rendered — the applicant's own detail view, the reviewer queue and detail screens, administrative views, the generated funding-agreement document, and the AI quote-comparison context — each line item shows the values the applicant entered for its category fields, the application impacts it is attributed to, and its short justification. The application's declared impacts and their values are also visible at the application level.

**Why this priority**: The captured data is only useful if every stakeholder sees it. Depends on US1–US3 producing the data first.

**Independent Test**: Submit an application with declared impacts, populated category fields, per-item attribution, and justifications, then open each rendering surface and confirm each line item's category values, attributed impacts, and justification appear, and the application's impacts are shown at the application level, in es-CR.

**Acceptance Scenarios**:

1. **Given** a line item with completed category fields, **When** any application surface renders that line item, **Then** the field labels and entered values are shown.
2. **Given** a line item attributed to one or more impacts, **When** any application surface renders that line item, **Then** the attributed impact name(s) and the line's justification are shown for that line.
3. **Given** an application that declares impacts, **When** any application surface renders it, **Then** the application's impacts and their values are shown at the application level (distinct from the per-line-item attribution).
4. **Given** the funding-agreement document is generated, **When** it is produced, **Then** the application's impacts and each line item's category values, attributed impacts, and justification are included.

---

### Edge Cases

- **Category fields edited after items reference the category**: live edits apply going forward. Already-submitted applications keep the values they captured. Newly added required fields do **not** retroactively block already-submitted applications, but they **do** apply to drafts still being completed.
- **Category deleted or deactivated while in use**: a hard delete is blocked when any line item references the category. Deactivation (marking the category inactive) is allowed — it is hidden from new line items, while existing line items keep their captured values.
- **Applicant changes a line item's category after filling fields**: the previous category's field values are cleared (they no longer apply); the new category's fields are presented empty. The line item's impact attribution and justification are unaffected.
- **Application impact removed while line items are attributed to it**: removing a declared impact also removes every line item's attribution to it. A line item left with zero attributions is incomplete and blocks submission until re-attributed.
- **Required category field, zero impact attributions, or empty justification at submit**: submission is blocked with es-CR validation messages identifying the line item and the missing element; per-impact required-value gaps are reported per impact.
- **Impact justification length**: the justification is intentionally brief — a hard cap of 300 characters is enforced; longer input is rejected/truncated at the boundary, presented as a short textarea.
- **Reusing a quotation then deleting the originating line item**: the shared uploaded document survives as long as any quotation references it; it is removed only when the last referencing quotation is gone.
- **Quote/item count mismatch**: a vendor quote listing N products requires N separate line items. The applicant cannot lump multiple quoted products into one generic line item.
- **No active impact templates exist**: the applicant cannot declare any impact, so the application cannot be completed; the flow surfaces this clearly rather than allowing submission without impact.

## Requirements *(mandatory)*

### Functional Requirements

**Admin — category field configuration**

- **FR-001**: Each submission category MUST own an ordered set of fields. Each field has a user-facing label, an internal key, a data type drawn from the existing set {text, decimal, integer, date}, a required/optional flag, and a display order.
- **FR-002**: Administrators MUST be able to add, edit, reorder, and remove a category's fields, consistent with how impact-template fields are managed today.
- **FR-003**: Each field's data type MUST determine the input control presented to the applicant (text → text input, decimal/integer → numeric input, date → date picker), reusing the existing impact convention. Per-field custom validation rules are out of scope and remain a dormant capability.

**Applicant — restructured line-item flow**

- **FR-004**: Adding a line item MUST require selecting a category first, after which the category's configured fields are presented dynamically.
- **FR-005**: The applicant MUST complete the category fields (required ones enforced at submission) together with the line's product name. The previous free-text technical-specifications field MUST be removed from the entry form and from the line-item data.

**Applicant — application-level impacts**

- **FR-006**: An application MUST support one or more **impacts**. The applicant MUST be able to add an impact by selecting from any active impact template (no per-process restriction), complete that impact's required values, add additional impacts, and remove a declared impact. Each declared impact's required values MUST be validated independently and identified by its impact name. An application with zero declared impacts MUST block submission.

**Applicant — per-line-item impact attribution and justification**

- **FR-007**: When creating or editing a line item, the applicant MUST be able to attribute the line to one or more of the impacts the application has declared, via a multi-select whose options are limited to the application's declared impacts. A line item with zero impact attributions MUST block submission.
- **FR-008**: Each line item MUST carry a single short **impact justification** explaining why the item supports its attributed impact(s). The justification MUST be required (non-empty), presented as a short textarea, and constrained to a hard cap of 300 characters. The line item MUST NOT carry its own impact-template field values — it references the application's impacts only.

**Applicant — quotation reuse**

- **FR-009**: When attaching quotation information to a line item, the applicant MUST be able to either add a new quotation (vendor, branch, price, currency, validity, uploaded document) or reuse a quotation already present in the same application. Reuse MUST carry over the vendor and the uploaded document while letting the applicant set that line's own price, currency, and validity. Editing one line item's reused quotation MUST NOT alter the others.
- **FR-010**: Quotation reuse MUST be scoped to the same application only; quotations belonging to other applications MUST never be offered.

**Cross-surface display**

- **FR-011**: Every surface that renders an application MUST display: per line item, the entered category-field values, the attributed impact name(s), and the justification; and at the application level, the declared impacts and their values. This includes the applicant detail view, the reviewer queue and detail views, administrative views, the generated funding-agreement document, and the AI quote-comparison context.

**Removal of obsolete behavior (no dead code)**

- **FR-012**: The free-text technical-specifications field MUST be removed entirely from the line-item entry form and data. No line item may carry its own per-item impact template or per-item impact field values; impact field data exists only at the application level.
- **FR-013**: The mechanism that restricted which impact templates a process could offer MUST be removed in full, including its catalog association, its per-process snapshot of allowed impact templates, and the related administrative picker. The minimum-quotations-per-item rule and the required-field flags that share that catalog MUST be preserved.
- **FR-014**: No reference to the removed technical-specifications field, per-item impact field values, or impact-template gating may remain anywhere in the codebase after this feature ships.

**Localization**

- **FR-015**: All applicant-facing and admin-facing copy, labels, and validation messages introduced or changed by this feature MUST be in es-CR.

### Key Entities

- **Category**: an admin-managed catalog entry an applicant assigns to a line item. Gains an ordered collection of **category fields** (its "template"). Retains its active/inactive state for deactivation.
- **Category field**: the definition of one piece of data a category collects — label, key, data type, required flag, display order. Belongs to exactly one category.
- **Category field value**: the applicant-entered value for one category field on one line item. Keyed by line item and field.
- **Application impact**: an impact the application declares — an impact template chosen for the application plus its entered values. An application has one or more. Replaces the former single application-wide impact.
- **Application impact value**: the applicant-entered value for one field of one declared application impact. Keyed by the application impact and the impact-template field.
- **Line item**: a single requested product within an application. Gains an attribution to one or more of the application's declared impacts and a single short justification; loses the free-text technical-specifications field and any per-item impact field values; retains its category assignment, product name, quotations, and review state.
- **Line-item impact attribution**: the association of a line item with one of the application's declared impacts. A line item has one or more; the set is limited to the application's declared impacts.
- **Impact justification (per line item)**: a single short (≤300 chars) explanation of why a line item supports its attributed impact(s).
- **Quotation**: a vendor's quoted price for a line item, with vendor/branch, price, currency, validity, and an uploaded document. Remains owned by a single line item, but a reused quotation shares the same uploaded document (and vendor) with a sibling line item's quotation while keeping its own price/currency/validity.
- **Process catalog (Plantilla)**: retains the minimum-quotations-per-item rule and required-field flags. Loses its association to impact templates and its per-process snapshot of allowed impact templates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An applicant can submit an application that declares at least one impact (with its required values), in which every line item has a category, all its required category fields completed, at least one attributed impact, a non-empty justification, and at least the minimum number of quotations — including at least one reused quotation — without re-uploading any document.
- **SC-002**: A single five-product vendor quotation is captured as five line items that share one uploaded document and one vendor, each carrying its own price.
- **SC-003**: A codebase search finds no remaining reference to the removed technical-specifications field, per-item impact field values, or impact-template gating.
- **SC-004**: Each of the five rendering surfaces (applicant detail, reviewer queue, reviewer detail, funding-agreement document, AI quote-comparison context) displays every line item's category values, attributed impact name(s), and justification, and the application's declared impacts.
- **SC-005**: 100% of new and changed applicant- and admin-facing copy is in es-CR.
- **SC-006**: Submission is blocked, with a clear es-CR message, whenever the application declares no impact, a declared impact is missing a required value, or a line item is missing a required category field, has zero impact attributions, or has an empty justification.
- **SC-007**: A line item can be attributed to multiple impacts, and removing a declared application impact removes that impact's attributions from every line item.

## Assumptions

- This submission flow is greenfield (not in production); no migration of existing application, item, impact, or quotation data is required.
- A category owns its field set one-to-one; field sets are not shared across categories and there is no separate standalone category-template catalog.
- The existing data-type set {text, decimal, integer, date} is sufficient for category fields; no new data types (e.g., file upload, dropdown, conditional fields) are introduced.
- Impact field data is collected once per declared application impact (not re-entered per line item); the line item only attributes itself to impacts and justifies the attribution.
- At least one declared application impact is required for submission, generalizing today's requirement that an application declare an impact; each line item must be attributed to at least one of those impacts.
- The impact justification is a single field per line item (covering all of that line's attributed impacts), capped at 300 characters.
- The reviewer-assigned line code, supplier catalog, multi-currency quotation snapshotting, and funding-agreement generation continue to work as they do today; this feature changes what line-item and application detail they read, not those subsystems' own rules.
- No new managed (NuGet) dependencies are needed; existing vendored UI and patterns are reused.

## Out of Scope

- Migration of any existing application data.
- Reusing quotations across different applications.
- Category fields with conditional/dependent logic, file-type values, dropdown/enumerated values, or custom per-field validation rules.
- A standalone reusable category-template catalog shared by multiple categories.
- Per-(line-item × impact) justifications, or long-form narrative justification fields.
- Any change to the minimum-quotations-per-item rule or the required-field flags carried by the process catalog (only the impact-template association is removed).

## Evolution Log

- **2026-06-16 — Impact model revised (major).** The originally-specified design relocated impact from the application to each line item (each line item picked one impact template and entered its own impact field values). Per a late stakeholder requirement, impact data collection moves **back to the application level** but generalized to **one or more impacts** per application, each with its own template fields validated independently. The line item no longer enters impact field data; instead it **attributes** itself to one or more of the application's declared impacts (multi-select limited to those impacts) and carries a single short **impact justification** (≤300 chars, required). Category-driven fields (US1/US3) and quotation reuse (US4) are unchanged by this revision. Affected: US2 (new, app-level impacts), US3 (reworked attribution + justification), US5 (display updated), FR-006/FR-007/FR-008/FR-011/FR-012 (reshaped), new SC-007. The codebase change is greenfield (branch not merged), so no data migration is implied.
