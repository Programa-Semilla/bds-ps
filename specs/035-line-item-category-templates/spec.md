# Feature Specification: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Feature Branch**: `035-line-item-category-templates`
**Created**: 2026-06-12
**Status**: Draft
**Input**: User description: "Reshape the applicant submission flow so line-item detail is captured through category-driven dynamic fields, impact is assigned per line item (not globally), and a vendor's multi-product quotation can be reused across line items in the same application without re-uploading. Replace the free-text technical-specifications field with structured, admin-configured, per-category fields. Tear out the obsolete application-level impact wiring and the Plantilla impact-template gating, leaving no dead code."

## Overview

Today an applicant describes each line item of a funding application with a single free-text "technical specifications" field, declares one **impact** for the whole application, and must re-enter a vendor and re-upload the vendor's quotation document for every line item — even when one vendor quote covers several products.

This feature restructures the line-item experience around three changes that share the same "add a line item" flow:

1. **Category-driven fields** — each submission category carries an admin-configured set of fields. When the applicant picks a category for a line item, those fields appear and capture the line's detail. This replaces the free-text technical-specifications field.
2. **Per-line-item impact** — impact moves from a single application-wide choice to a per-line-item choice. Each line item declares its own impact.
3. **Quotation reuse within an application** — a vendor quote that lists several products is captured as one line item per product, but the vendor and the uploaded document are entered once and reused by the sibling line items, each keeping its own price.

Because this submission flow is not yet in production, there is no data to migrate. The obsolete application-level impact wiring and the now-unused mechanism that limited which impact templates a process could offer are removed entirely, with no vestigial code left behind.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin configures the fields a category collects (Priority: P1)

An administrator opens a submission category and defines the ordered set of fields that applicants must complete when they choose that category for a line item. Each field has a user-facing label, an internal key, a data type (text, decimal, integer, or date), a required/optional flag, and a display order. The administrator can add, edit, reorder, and remove fields.

**Why this priority**: Without configured category fields, the applicant flow has nothing to render. This is the foundation the rest of the feature builds on, and it is independently demonstrable.

**Independent Test**: Create a category, add several fields of different data types (some required), reorder them, edit a label, remove one, and confirm the category's field set persists and renders in the expected order with the correct input control per data type.

**Acceptance Scenarios**:

1. **Given** an existing category with no fields, **When** the admin adds a required decimal field labeled "Costo unitario" and a optional text field labeled "Marca", **Then** both fields are saved on the category in the defined order with their data types and required flags.
2. **Given** a category with three fields, **When** the admin reorders them, **Then** the new display order is persisted and reflected wherever the category's fields render.
3. **Given** a category field that is no longer needed, **When** the admin removes it, **Then** it no longer appears for new line items.
4. **Given** the admin is configuring fields, **When** they save, **Then** all labels and validation messages are presented in es-CR.

---

### User Story 2 - Applicant captures a line item via category fields and per-item impact (Priority: P1)

An applicant building a draft application adds a line item. They first select a category; the category's configured fields appear and the applicant completes them (replacing the old free-text technical-specifications entry). They enter the line's product name, then select an **impact** for that line item from the available active impact templates and complete its required values. The applicant repeats this for each line item. At submission, the application is accepted only when every line item has its required category fields completed and an impact assigned with its required values.

**Why this priority**: This is the core of the feature — the restructured line-item entry and the relocation of impact to the line item. It delivers the primary applicant value and exercises the teardown of the old application-level impact.

**Independent Test**: Create a draft application, add a line item, pick a category, complete its required fields, select an impact and complete its values, and confirm the line item saves. Attempt to submit with a required category field or required impact value blank and confirm submission is blocked with an es-CR message; complete the fields and confirm submission succeeds.

**Acceptance Scenarios**:

1. **Given** a draft application, **When** the applicant adds a line item and selects a category, **Then** that category's configured fields are displayed with the input control matching each field's data type.
2. **Given** a line item with a selected category, **When** the applicant leaves a required category field blank and attempts to submit, **Then** submission is blocked and the missing field is reported in es-CR.
3. **Given** a line item, **When** the applicant selects an impact and completes its required values, **Then** the impact is stored against that line item (not the application).
4. **Given** a line item with no impact selected, **When** the applicant attempts to submit, **Then** submission is blocked because per-item impact is required.
5. **Given** an applicant changes a line item's category after filling fields, **When** they confirm the change, **Then** the previous category's field values are cleared and the new category's fields are presented empty.
6. **Given** a submitted application, **When** anyone reads it, **Then** there is no application-wide impact — impact exists only per line item.

---

### User Story 3 - Applicant reuses a multi-product vendor quotation across line items (Priority: P2)

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

### User Story 4 - Category values and per-item impact are visible on every application surface (Priority: P3)

Everywhere an application is rendered — the applicant's own detail view, the reviewer queue and detail screens, administrative views, the generated funding-agreement document, and the AI quote-comparison context — each line item shows the values the applicant entered for its category fields and the impact assigned to that line item.

**Why this priority**: The captured data is only useful if every stakeholder sees it. Depends on US1–US2 producing the data first.

**Independent Test**: Submit an application with populated category fields and per-item impact, then open each rendering surface and confirm each line item's category values and impact appear, in es-CR.

**Acceptance Scenarios**:

1. **Given** a line item with completed category fields, **When** any application surface renders that line item, **Then** the field labels and entered values are shown.
2. **Given** a line item with an assigned impact, **When** any application surface renders that line item, **Then** the line's impact is shown for that line (not as an application-wide value).
3. **Given** the funding-agreement document is generated, **When** it is produced, **Then** each line item's category values and impact are included.

---

### Edge Cases

- **Category fields edited after items reference the category**: live edits apply going forward. Already-submitted applications keep the values they captured. Newly added required fields do **not** retroactively block already-submitted applications, but they **do** apply to drafts still being completed.
- **Category deleted or deactivated while in use**: a hard delete is blocked when any line item references the category. Deactivation (marking the category inactive) is allowed — it is hidden from new line items, while existing line items keep their captured values.
- **Applicant changes a line item's category after filling fields**: the previous category's field values are cleared (they no longer apply); the new category's fields are presented empty.
- **Required category field or required impact value missing at submit**: submission is blocked with es-CR validation messages identifying the line item and the missing field.
- **Reusing a quotation then deleting the originating line item**: the shared uploaded document survives as long as any quotation references it; it is removed only when the last referencing quotation is gone.
- **Quote/item count mismatch**: a vendor quote listing N products requires N separate line items. The applicant cannot lump multiple quoted products into one generic line item.
- **No active impact templates exist**: the applicant cannot complete a line item; the flow surfaces this clearly rather than allowing submission without impact.

## Requirements *(mandatory)*

### Functional Requirements

**Admin — category field configuration**

- **FR-001**: Each submission category MUST own an ordered set of fields. Each field has a user-facing label, an internal key, a data type drawn from the existing set {text, decimal, integer, date}, a required/optional flag, and a display order.
- **FR-002**: Administrators MUST be able to add, edit, reorder, and remove a category's fields, consistent with how impact-template fields are managed today.
- **FR-003**: Each field's data type MUST determine the input control presented to the applicant (text → text input, decimal/integer → numeric input, date → date picker), reusing the existing impact convention. Per-field custom validation rules are out of scope and remain a dormant capability.

**Applicant — restructured line-item flow**

- **FR-004**: Adding a line item MUST require selecting a category first, after which the category's configured fields are presented dynamically.
- **FR-005**: The applicant MUST complete the category fields (required ones enforced at submission) together with the line's product name. The previous free-text technical-specifications field MUST be removed from the entry form and from the line-item data.
- **FR-006**: The applicant MUST select an impact for each line item, choosing from any active impact template (no per-process restriction), and complete its required values. A line item without an assigned impact MUST block submission.
- **FR-007**: When attaching quotation information to a line item, the applicant MUST be able to either add a new quotation (vendor, branch, price, currency, validity, uploaded document) or reuse a quotation already present in the same application. Reuse MUST carry over the vendor and the uploaded document while letting the applicant set that line's own price, currency, and validity. Editing one line item's reused quotation MUST NOT alter the others.
- **FR-008**: Quotation reuse MUST be scoped to the same application only; quotations belonging to other applications MUST never be offered.

**Cross-surface display**

- **FR-009**: Every surface that renders an application MUST display, per line item, the entered category-field values and the assigned impact. This includes the applicant detail view, the reviewer queue and detail views, administrative views, the generated funding-agreement document, and the AI quote-comparison context.

**Removal of obsolete behavior (no dead code)**

- **FR-010**: Application-wide impact MUST be removed entirely (the application no longer carries an impact selection or application-keyed impact values); impact MUST exist only per line item.
- **FR-011**: The mechanism that restricted which impact templates a process could offer MUST be removed in full, including its catalog association, its per-process snapshot of allowed impact templates, and the related administrative picker. The minimum-quotations-per-item rule and the required-field flags that share that catalog MUST be preserved.
- **FR-012**: No reference to the removed technical-specifications field, application-wide impact, or impact-template gating may remain anywhere in the codebase after this feature ships.

**Localization**

- **FR-013**: All applicant-facing and admin-facing copy, labels, and validation messages introduced or changed by this feature MUST be in es-CR.

### Key Entities

- **Category**: an admin-managed catalog entry an applicant assigns to a line item. Gains an ordered collection of **category fields** (its "template"). Retains its active/inactive state for deactivation.
- **Category field**: the definition of one piece of data a category collects — label, key, data type, required flag, display order. Belongs to exactly one category.
- **Category field value**: the applicant-entered value for one category field on one line item. Keyed by line item and field.
- **Line item**: a single requested product within an application. Gains a per-item impact selection and per-item impact values; loses the free-text technical-specifications field; retains its category assignment, product name, quotations, and review state.
- **Impact selection (per line item)**: the impact template chosen for a line item plus its entered values. Replaces the former application-wide impact.
- **Quotation**: a vendor's quoted price for a line item, with vendor/branch, price, currency, validity, and an uploaded document. Remains owned by a single line item, but a reused quotation shares the same uploaded document (and vendor) with a sibling line item's quotation while keeping its own price/currency/validity.
- **Process catalog (Plantilla)**: retains the minimum-quotations-per-item rule and required-field flags. Loses its association to impact templates and its per-process snapshot of allowed impact templates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An applicant can submit an application in which every line item has a category, all its required category fields completed, an impact assigned with required values, and at least the minimum number of quotations — including at least one reused quotation — without re-uploading any document.
- **SC-002**: A single five-product vendor quotation is captured as five line items that share one uploaded document and one vendor, each carrying its own price.
- **SC-003**: A codebase search finds no remaining reference to the removed technical-specifications field, application-wide impact, or impact-template gating.
- **SC-004**: Each of the five rendering surfaces (applicant detail, reviewer queue, reviewer detail, funding-agreement document, AI quote-comparison context) displays every line item's category values and per-item impact.
- **SC-005**: 100% of new and changed applicant- and admin-facing copy is in es-CR.
- **SC-006**: Submission is blocked, with a clear es-CR message naming the line item and missing field, whenever a required category field or required per-item impact value is incomplete.

## Assumptions

- This submission flow is greenfield (not in production); no migration of existing application, item, impact, or quotation data is required.
- A category owns its field set one-to-one; field sets are not shared across categories and there is no separate standalone category-template catalog.
- The existing data-type set {text, decimal, integer, date} is sufficient for category fields; no new data types (e.g., file upload, dropdown, conditional fields) are introduced.
- Per-item impact is required for submission, mirroring today's requirement that an application declare an impact.
- The reviewer-assigned line code, supplier catalog, multi-currency quotation snapshotting, and funding-agreement generation continue to work as they do today; this feature changes what line-item detail they read, not those subsystems' own rules.
- No new managed (NuGet) dependencies are needed; existing vendored UI and patterns are reused.

## Out of Scope

- Migration of any existing application data.
- Reusing quotations across different applications.
- Category fields with conditional/dependent logic, file-type values, dropdown/enumerated values, or custom per-field validation rules.
- A standalone reusable category-template catalog shared by multiple categories.
- Any change to the minimum-quotations-per-item rule or the required-field flags carried by the process catalog (only the impact-template association is removed).
