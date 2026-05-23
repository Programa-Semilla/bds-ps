# Feature Specification: Supplier Branch Location Cascade (Provincia → Cantón → Distrito)

**Feature Branch**: `025-supplier-location-cascade`
**Created**: 2026-05-22
**Status**: Draft
**Input**: Finish spec 021 FR-014 (wire the never-rendered Provincia → Cantón cascade) and extend it to the full three-level Costa Rica administrative hierarchy by adding a Distrito catalog.

## Background

Spec 021 (FR-014) built a `Province` catalog (7 rows), a `Canton` catalog (~84 rows), foreign-key columns on `SupplierBranch`, a cantón cascade endpoint, a cascade JavaScript helper, and a reusable two-`<select>` cascade partial — but the partial was **never wired into any form**, and the third administrative level (Distrito) was never modeled. As a result the applicant supplier-branch forms at `/Application/{id}/Item/{id}/Supplier/Add` still collect "Provincia" as a free-text input with no Cantón and no Distrito. This feature completes the unfinished wiring and adds the missing Distrito level so location is captured as Costa Rica's real three-level hierarchy: **Provincia → Cantón → Distrito**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Applicant registers a new supplier with a structured location (Priority: P1)

An applicant adding a supplier quotation for an item cannot find the supplier by legal ID, so the form shows the "new supplier" panel. The applicant fills the supplier's principal branch, including its location, by selecting from three dependent dropdowns: first a Provincia, which narrows the Cantón list to that province, then a Cantón, which narrows the Distrito list to that cantón. On submit the branch is persisted with the selected province, cantón, and distrito.

**Why this priority**: This is the exact surface the user reported and the primary place applicants enter supplier locations. It is the minimum viable slice — delivering it alone makes structured location capture work end to end.

**Independent Test**: From the applicant journey, open an item's "Agregar proveedor", trigger the new-supplier panel, pick Provincia → Cantón → Distrito (verifying each pick narrows the next list), submit, and confirm the saved branch carries the three selected catalog values.

**Acceptance Scenarios**:

1. **Given** the new-supplier panel is shown, **When** the applicant selects a Provincia, **Then** the Cantón dropdown is populated with only that province's cantones and the Distrito dropdown is empty/disabled until a Cantón is chosen.
2. **Given** a Provincia and Cantón are selected, **When** the applicant selects a Cantón, **Then** the Distrito dropdown is populated with only that cantón's distritos.
3. **Given** the applicant changes the Provincia after having chosen a Cantón/Distrito, **When** the province changes, **Then** the dependent Cantón and Distrito selections reset to a consistent state (no orphaned cantón/distrito from the previous province).
4. **Given** all other branch fields are valid but one or more of Provincia/Cantón/Distrito is unselected, **When** the applicant submits, **Then** the form is rejected with a validation message and no supplier/branch is created.
5. **Given** valid Provincia + Cantón + Distrito selections, **When** the applicant submits, **Then** the new supplier's principal branch is persisted referencing the chosen province, cantón, and distrito catalog rows.

---

### User Story 2 - Applicant adds a branch to an existing supplier with a structured location (Priority: P2)

An applicant finds an existing approved supplier by legal ID and chooses to add a new branch rather than reuse one. The "add new branch" panel offers the same three-level Provincia → Cantón → Distrito cascade, with the same narrowing and the same all-three-required rule.

**Why this priority**: Same data-quality value as US1 on the second applicant entry path; depends on the same catalog and cascade built in US1, so it is incremental.

**Independent Test**: From the applicant journey, look up an existing supplier, open the "add new branch" panel, complete the cascade, submit, and confirm the new branch carries the three selected catalog values.

**Acceptance Scenarios**:

1. **Given** an existing supplier is matched and the add-new-branch panel is open, **When** the applicant uses the cascade, **Then** Cantón narrows on Provincia and Distrito narrows on Cantón identically to US1.
2. **Given** the cascade is incomplete, **When** the applicant submits the new branch, **Then** the submission is rejected with a validation message.
3. **Given** the cascade is complete, **When** the applicant submits, **Then** the new branch is added to the existing supplier referencing the chosen province, cantón, and distrito.

---

### User Story 3 - Admin edits a supplier branch location (Priority: P3)

An administrator editing a supplier branch on the admin supplier detail page sets the branch location through the same three-level cascade, replacing today's free-text Provincia field.

**Why this priority**: Keeps the admin surface consistent with the applicant surfaces and lets staff correct/complete locations, but it is the least-frequent path and not the surface the user reported.

**Independent Test**: As an admin, open a supplier's detail/edit, change the branch location via the cascade, save, and confirm the branch reflects the new province/cantón/distrito.

**Acceptance Scenarios**:

1. **Given** the admin branch-edit form, **When** the admin opens it for a branch with an existing location, **Then** the Provincia, Cantón, and Distrito dropdowns are pre-selected to the branch's current values.
2. **Given** the admin changes the location via the cascade, **When** they save, **Then** the branch reflects the newly selected province, cantón, and distrito.
3. **Given** the admin leaves any of the three levels unselected, **When** they save, **Then** the save is rejected with a validation message.

---

### Edge Cases

- **Existing branches with no/legacy location**: Branches created before this feature (null FK location, or only the legacy free-text Provincia string) remain valid and display as they do today. They are not backfilled and editing them is not forced.
- **Province changed mid-edit**: Changing Provincia must invalidate a now-inconsistent Cantón, which in turn invalidates a now-inconsistent Distrito, so a submitted location never references a cantón outside its province or a distrito outside its cantón.
- **Cascade data fetch fails (network)**: If the dependent list cannot be fetched, the form must not silently submit an incomplete/blank location; the all-three-required rule still blocks submission.
- **Tampered/forged identifiers**: A submitted distrito that does not belong to the submitted cantón (or cantón not in province) must be rejected server-side, not trusted from the client.
- **Catalog completeness**: Every cantón in the existing catalog has at least one distrito so the third dropdown is never empty for a valid cantón selection.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a Distrito catalog covering every distrito of Costa Rica's current administrative division, with each distrito belonging to exactly one cantón in the existing cantón catalog.
- **FR-002**: The Distrito catalog MUST be sourced from an authoritative Costa Rica administrative-division reference, version-matched to the existing cantón catalog so that every distrito maps to an existing cantón (no orphaned distritos, no missing cantones).
- **FR-003**: The Distrito catalog seed MUST be idempotent (safe to re-run without duplicating rows), consistent with how the Provincia/Cantón catalogs are seeded.
- **FR-004**: A supplier branch MUST be able to reference a Provincia, a Cantón, and a Distrito as structured catalog values (not free text).
- **FR-005**: The system MUST enforce referential consistency on a branch location: the chosen Cantón MUST belong to the chosen Provincia, and the chosen Distrito MUST belong to the chosen Cantón. This MUST be enforced server-side and MUST NOT rely on client-supplied relationships.
- **FR-006**: A branch location MUST be all-or-nothing at the data layer: either all three levels are set and mutually consistent, or none are set (preserving validity of pre-existing branches that have no location).
- **FR-007**: The system MUST expose a way to retrieve the distritos of a given cantón so the dependent dropdown can populate, mirroring how cantones of a province are already retrieved.
- **FR-008**: The location entry control MUST render as three dependent dropdowns — Provincia, Cantón, Distrito — where selecting a Provincia narrows the Cantón options to that province and selecting a Cantón narrows the Distrito options to that cantón.
- **FR-009**: Changing a higher level MUST reset lower levels to a consistent state so a submitted location can never contain a cantón outside its province or a distrito outside its cantón.
- **FR-010**: The three-level cascade MUST be present on all three branch-location surfaces: the applicant new-supplier (principal branch) form, the applicant new-branch-on-existing-supplier form, and the admin supplier branch-edit form (replacing its current free-text Provincia field).
- **FR-011**: When adding or editing a branch on any of those three surfaces, all three levels (Provincia, Cantón, Distrito) MUST be required; submitting with any level unselected MUST be rejected with a clear, localized validation message and MUST NOT create or modify a branch.
- **FR-012**: Required-field validation MUST apply only to the active entry path (the panel the user is actually filling) and MUST NOT block submission because of an inactive/hidden panel on the same page.
- **FR-013**: Existing supplier-display surfaces that today show a branch's location MUST continue to show a human-readable location for branches saved through the new cascade, without those surfaces needing to change how they read the value.
- **FR-014**: Existing branches that already have a location (legacy or structured) MUST continue to display and function unchanged; this feature MUST NOT backfill or rewrite their location.
- **FR-015**: All new user-facing copy (labels, placeholders, validation messages) MUST be in es-CR and MUST use only locally vendored assets (no CDN), consistent with project conventions.

### Key Entities *(include if feature involves data)*

- **Distrito (District)**: A third-level Costa Rica administrative unit. Belongs to exactly one Cantón. Has a stable code consistent with the existing province/cantón coding scheme and a localized name. Read-only catalog data (changes only via legislative redistricting).
- **Supplier Branch location**: The branch's structured location, expressed as references to one Provincia, one Cantón, and one Distrito, subject to the cross-level consistency rule. Coexists with the legacy free-text location value used for display continuity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the applicant new-supplier form, selecting a Provincia repopulates the Cantón dropdown with that province's cantones only, and selecting a Cantón repopulates the Distrito dropdown with that cantón's distritos only — verified for at least one province/cantón/distrito triple end to end.
- **SC-002**: An applicant can register a new supplier whose principal branch is saved with a Provincia, Cantón, and Distrito chosen entirely from dropdowns (zero free-text location entry).
- **SC-003**: An applicant can add a branch to an existing supplier with the same three-level dropdown selection.
- **SC-004**: An administrator can set or change a branch's location through the same three-level dropdowns on the admin supplier edit surface.
- **SC-005**: Submitting any of the three forms with an incomplete location (any of the three levels unselected) is rejected with a localized validation message and produces no create/update.
- **SC-006**: A submitted location whose cantón is not in its province, or whose distrito is not in its cantón, is rejected server-side.
- **SC-007**: Every cantón in the catalog resolves to at least one distrito (the Distrito dropdown is never empty for a valid cantón), and the seeded distrito count matches the authoritative reference for the version-matched cantón catalog.
- **SC-008**: Supplier branches created before this feature continue to display their location and remain editable/usable with no change in behavior.
- **SC-009**: Existing location-display surfaces show a readable location for branches saved via the new cascade without modification to those surfaces.

## Assumptions

- The existing 84-cantón catalog (the spec-021 version, predating the 2022 Puerto Jiménez cantón) is the version the Distrito catalog is matched to; the authoritative distrito list is selected to align exactly with those cantones (~488 distritos). The exact count and source revision are confirmed during planning from the authoritative reference, not from memory.
- Costa Rica's administrative model is exactly three levels for this purpose (Provincia → Cantón → Distrito); no further subdivision (barrio/poblado) is in scope.
- The legacy free-text Provincia value on a branch is retained as a composed, human-readable display string written when a branch is saved through the cascade; the structured references are the source of truth.
- The two existing applicant entry panels and the admin edit form are the only branch-location surfaces in scope. The separate inline branch-creation path referenced from the application edit screen is out of scope unless it is one of the wired forms.
- Province and Cantón catalogs, their FK columns, the cantón retrieval endpoint, the cascade script, and the cascade partial from spec 021 are reused and extended rather than rebuilt.
- No backfill, data migration, or rewrite of existing branch locations is performed.
