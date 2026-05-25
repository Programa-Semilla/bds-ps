# Feature Specification: Structured-Field Input Masks

**Feature Branch**: `026-input-masks`
**Created**: 2026-05-24
**Status**: Draft
**Input**: User description: "need masks over any field with a known value structure, like emails, id juridico, id de cedula, y cualquier otro que exista en el sistema actualmente"

## Summary

Every field whose value has a known structure — email, Costa Rica phone, and the Costa Rican identification numbers (cédula física, cédula jurídica, DIMEX, NITE, passport) — should guide the user toward the correct shape as they type and reject malformed values on submit. Today the system has only a phone and an email mask (spec 021 / FR-013), wired on almost no surface, while the identification fields accept any 50-character string. That gap lets malformed cédulas through and makes supplier lookup unreliable because the same legal ID can be stored with or without hyphens. This feature makes masking consistent, identification entry type-aware, and the masking mechanism extensible so future structured fields opt in with a single declaration. It completes spec 021 FR-013.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Type-aware identification entry for people (Priority: P1)

An applicant registering an account (or an admin creating/editing a user, or a user editing their own profile) first chooses the **type** of identification they hold — Cédula física, DIMEX, NITE, or Pasaporte — and the identification field then guides them to that type's exact shape: digits-only with auto-inserted hyphens for the numeric types, free text for a passport. On save, both the type and the value are stored; when the record is edited later, the selector shows the original type and the value renders in its masked shape. A value that does not match the selected type is rejected by the server with a clear Spanish message, regardless of what the browser allowed.

**Why this priority**: This is the headline value and the part most broken today — identification fields currently accept any string, the system cannot represent foreign applicants cleanly, and there is no format guarantee. Delivering this alone already raises data quality on the most important fields.

**Independent Test**: On the applicant Register form, choose each identification type in turn, confirm the field masks to that type, submit a valid value, then reopen the record (admin edit / profile) and confirm the type and masked value round-trip. Submit a deliberately malformed value with client scripting disabled and confirm the server rejects it.

**Acceptance Scenarios**:

1. **Given** the Register form, **When** the user selects "Cédula física" and types `123456789`, **Then** the field displays `1-2345-6789` and accepts it.
2. **Given** the type is "Cédula física", **When** the user types letters, **Then** the letters are rejected as typed and never appear in the field.
3. **Given** the type is "Pasaporte", **When** the user types `A1B2C3`, **Then** the value is accepted as-is with no hyphen formatting.
4. **Given** a saved user with type "DIMEX" and a 12-digit value, **When** an admin opens the edit form, **Then** the selector shows "DIMEX" and the stored value is displayed.
5. **Given** a submitted form where the value does not match the selected type's shape (client validation bypassed), **When** the server processes it, **Then** the submission is rejected with a field-level Spanish error and the entered value is preserved on redisplay.
6. **Given** an optional identification field (admin user create) left entirely blank — no type, no value — **When** the form is submitted, **Then** no identification error is raised.

---

### User Story 2 - Type-aware supplier identification with tolerant lookup (Priority: P2)

When an applicant looks up or registers a supplier, they choose whether the supplier's identification is a Cédula jurídica or a NITE, and the field masks to that shape. The supplier-lookup-by-identification flow finds an existing supplier whether the user types the ID with hyphens, without them, or with spacing differences, because the query is normalized to the same canonical form the system stores.

**Why this priority**: Suppliers are shared across applications; an unreliable lookup creates duplicate supplier records and breaks deduplication. It depends on the same masking mechanism as US1 but targets a distinct surface and adds the normalization guarantee.

**Independent Test**: On the supplier add/lookup surface, select "Cédula jurídica", type a known supplier's ID with hyphens and confirm a match; repeat typing the same digits without hyphens and confirm the same match. Register a new supplier with type "NITE" and confirm the masked value persists.

**Acceptance Scenarios**:

1. **Given** an existing supplier stored as `3-101-123456`, **When** the user types `3101123456` in the lookup, **Then** the existing supplier is found.
2. **Given** the supplier type selector, **When** "NITE" is chosen, **Then** the field masks to the NITE shape.
3. **Given** a new supplier entered with a valid masked identification, **When** it is saved, **Then** the identification persists in canonical form and the type is recorded.

---

### User Story 3 - Consistent, extensible masking across structured fields (Priority: P3)

Email and Costa Rica phone fields are masked/validated everywhere they appear — not just on one orphaned view — so users get the same guidance on every form. The masking mechanism is a declarative registry: a developer can add a mask for any future structured field by adding one registry entry and tagging the input, with no bespoke per-field scripting.

**Why this priority**: It closes the spec-021 gap (the mask script loads on a view with no maskable fields while real email/phone fields elsewhere go unmasked) and delivers the user's "y cualquier otro que exista" intent by making the mechanism reusable. Lower priority because email/phone already have basic server validation; this is consistency and future-proofing.

**Independent Test**: Visit every form that renders an email or phone field and confirm the mask is active. Add a throwaway registry entry for a new mask name, tag an input, and confirm it formats — demonstrating single-entry extensibility (the cédula masks themselves are the real demonstration).

**Acceptance Scenarios**:

1. **Given** any form with an email field, **When** the user blurs an invalid email, **Then** the field shows the Spanish invalid-email feedback.
2. **Given** any form with a phone field, **When** the user types digits, **Then** the value formats to `8888-8888`.
3. **Given** a new structured field type, **When** a developer adds one registry entry and tags the input with the mask name, **Then** the field masks correctly with no other code change.

---

### Edge Cases

- **Paste of a long or garbage string** into a strict field: the mask strips disallowed characters and caps the length to the type's maximum (current phone behavior, generalized).
- **Switching identification type while a value is present**: the value is re-validated against the newly selected type and flagged if incompatible; entered digits are never silently dropped.
- **DIMEX length**: both 11- and 12-digit DIMEX values are valid.
- **All-digit passport**: accepted, because the type is selected explicitly — a 9-digit passport is not misread as a cédula física.
- **Cédula jurídica vs NITE share the same 10-digit shape**: they are distinguished by the persisted type, not by the format; the server validates the shape and stores the chosen type.
- **Value present but type not selected** (where the field is in use): rejected with "Seleccione el tipo de identificación."
- **Type selected but value empty** in a required context: rejected with "La identificación es obligatoria."
- **Pre-existing stored value that no longer matches the strict shape**: displays best-effort through the mask and only blocks on the next submit (no destructive auto-rewrite on read).
- **es-CR culture**: masks operate on raw characters and are independent of decimal/thousands separators — identification numbers are treated as character sequences, not numbers.

## Requirements *(mandatory)*

### Functional Requirements

**Masking mechanism**

- **FR-001**: The system MUST provide a declarative masking mechanism in which an input opts into a named mask, and the mechanism applies that mask to every matching field on the page when the page loads.
- **FR-002**: The masking mechanism MUST be a registry keyed by mask name, where each entry declares its as-you-type formatting behavior (or none), its maximum length, its on-blur validation, and whether it is `strict` or `soft`. Adding a new structured-field mask MUST require only a new registry entry plus tagging the input — no other code change.
- **FR-003**: A `strict` mask MUST strip disallowed characters on every keystroke, auto-insert the type's separators, and cap input length to the type's maximum.
- **FR-004**: A `soft` mask MUST validate on blur, surface an inline Spanish error styled with the existing validation classes, remove that error when the value becomes valid, and treat an empty value as deferring to any Required validator.
- **FR-005**: A server-rendered value MUST be formatted into its masked shape once when the page loads.

**Mask catalogue (v1)**

- **FR-006**: The system MUST provide these masks: `email` (soft, RFC-lax), `phone-cr` (strict, `8888-8888`), `cedula` / cédula física (strict, `0-0000-0000`, 9 digits), `cedula-jur` / cédula jurídica (strict, `3-000-000000`, 10 digits), `dimex` (strict, 11–12 digits), `nite` (strict, `0-000-000000`, 10 digits), and `pasaporte` (soft, free alphanumeric, no separators).
- **FR-007**: Identification values MUST be stored in a single canonical form: the masked hyphenated string for the hyphenated numeric types (cédula física, cédula jurídica, NITE), plain digits for DIMEX, and uppercased alphanumeric for passport.

**Person identification**

- **FR-008**: The system MUST persist an identification **type** for a person alongside the identification value (the person's legal ID is held on the applicant record, not the authentication user), supporting the values Cédula física, DIMEX, NITE, and Pasaporte.
- **FR-009**: A labeled, editable identification-type selector MUST precede the identification field on the applicant Register form, the admin user create form, and the admin user edit form. On the user Profile, the identification type and value MUST be shown **read-only** (with the existing "administrado" badge, consistent with Email/Role) — identity is admin-managed and not self-editable.
- **FR-010**: Selecting an identification type MUST rebind the field to that type's mask and re-validate the current value against the new type.
- **FR-011**: On editing an existing record, the persisted identification type MUST be restored in the selector (Register/admin) or shown as the read-only label (Profile), and the stored value MUST render through that type's mask.

**Supplier identification**

- **FR-012**: The system MUST persist an identification type for a supplier, supporting the values Cédula jurídica and NITE, driving the masked field on the supplier lookup/add surface.
- **FR-013**: Supplier lookup by identification MUST normalize the entered value to the canonical stored form before matching, so an existing supplier is found regardless of how the user typed hyphens or spacing.

**Validation & integrity**

- **FR-014**: The server MUST validate each identification value against the shape implied by its selected type, independently of any client-side masking, and reject a mismatch with a field-level Spanish error while preserving the entered value for redisplay.
- **FR-015**: Where an identification field is optional, type and value MUST be either both present or both absent; a value without a type, and a type without a value in a required context, are each rejected with the appropriate Spanish message.
- **FR-016**: Every form surface that renders a maskable field MUST activate the masking mechanism (closing the spec-021 gap where the script loaded on a surface with no maskable fields).

**Constraints**

- **FR-017**: All identification, masking, and validation copy MUST be in es-CR.
- **FR-018**: An invalid masked field MUST expose its invalid state to assistive technology, and the identification-type selector MUST be labeled.
- **FR-019**: The feature MUST NOT introduce a new managed or vendored dependency; it MUST extend the existing in-repo masking script (honoring the project's vendored-only / no-CDN posture).
- **FR-020**: Seed data and test fixtures MUST be adjusted to the canonical identification form; because the system is not yet in production, no data migration or backfill is performed.

### Key Entities

- **Identification Type**: An enumeration distinguishing the kind of legal identification a person or organization holds. Person context: Cédula física, DIMEX, NITE, Pasaporte. Supplier context: Cédula jurídica, NITE. Persisted alongside the identification value on the applicant record and on the supplier record (the person's legal ID is carried by the applicant record, not the authentication user).
- **Identification Value**: The legal identification string for a person or supplier, stored in canonical form, meaningful only in combination with its Identification Type.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For each identification type, a user entering a structurally valid value sees it formatted to that type's shape and can submit it; for each type, a structurally invalid value cannot be submitted successfully (blocked client-side and rejected server-side).
- **SC-002**: Strict fields reject non-permitted characters as typed — no letters appear in a numeric identification field and no field exceeds its type's maximum length, including on paste.
- **SC-003**: An identification type and value saved on a record are shown identically (type restored, value masked) when that record is reopened for editing, for applicant, admin-user, and supplier records.
- **SC-004**: A supplier stored under a given identification is found by lookup whether the searcher types the identification with hyphens or as bare digits — a 100% match rate across hyphenation variants of the same value.
- **SC-005**: Email and phone fields are masked/validated on 100% of the forms that render them.
- **SC-006**: A new structured-field mask can be added by a single registry entry plus an input tag, with no other code change — demonstrated by the cédula masks added in this feature.
- **SC-007**: All identification and validation messages presented to users are in Spanish.
- **SC-008**: The full end-to-end test suite passes (project delivery bar).

## Assumptions

- The system is **not yet in production**, so existing data need not be migrated; seeds and fixtures are simply updated to the canonical identification form.
- Canonical stored form is **hyphenated** for the grouped numeric types, consistent with the existing phone-storage convention; DIMEX is stored as plain digits (no standard CR hyphenation); passport is stored uppercased.
- **NITE appears on both** the person and supplier type selectors, per stakeholder decision.
- Identification validation checks **shape and length only** — no Hacienda check-digit / checksum verification.
- Cédula jurídica and NITE share the same 10-digit shape; they are differentiated by the persisted type, not by format.
- The existing validation styling (the project's Bootstrap/Tabler invalid-feedback classes) is reused; no new visual component is introduced.
- Currency, price, and date inputs are already constrained by their existing controls and value objects and are explicitly out of scope for masking.

## Dependencies

- The existing in-repo masking script and the existing validation styling classes.
- The model-binding / validation pipeline for server-side identification checks.
- A schema change adding a nullable identification-type column to the applicant record (`dbo.Applicants`) and to the supplier record (`dbo.Suppliers`), managed through the project's schema source of truth (dacpac).
- A new identification-type enumeration in the domain.

## Out of Scope

- Check-digit / checksum validation of cédula, DIMEX, or NITE (shape and length only).
- Bank account, IBAN, and postal-code masks (no such fields exist in the system).
- Reformatting currency, price, or date inputs (already constrained by their controls and value objects).
- Passport format rules beyond non-empty alphanumeric.
- Migration or backfill of existing data (system is pre-production).
