# Feature Specification: Admin — Edit Process Name

**Feature Branch**: `030-edit-process-name`
**Created**: 2026-06-10
**Status**: Draft
**Input**: User description: "Admin: edit Process name (inline on the Process Details page)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Correct or relabel a Process name (Priority: P1)

An administrator opens the detail page of a Process (e.g. *"Crocus 2025"*) and needs to
change its name — to fix a typo or to relabel it (e.g. *"Crocus 2025-II"*). Today the name
is set once at creation and is immutable everywhere in the UI; the only "fix" is to create a
new Process, which loses the existing Groups, Plantilla snapshot, and history. This story
gives the admin an inline edit affordance on the Process detail page to change the name in
place.

**Why this priority**: This is the entire feature. Every other Process detail (Fund, stage
windows, Plantilla, Groups, close) is already editable on the detail page — the name is the
lone gap, and an un-fixable typo on a top-level entity is a visible data-quality problem
admins hit directly.

**Independent Test**: Sign in as an admin, open an existing Process's detail page, change the
name in the inline field, save, and confirm the new name appears on the detail page header,
breadcrumb, and the Processes list — and that the change is recorded in the admin audit log.

**Acceptance Scenarios**:

1. **Given** an Active Process named "Crocus 2025", **When** the admin enters "Crocus 2025-II"
   in the inline name field and saves, **Then** the Process name is updated, the detail page
   and Processes list show the new name, a success confirmation is shown, and an audit entry
   recording the actor and the old/new name is written.
2. **Given** a Process detail page, **When** the admin clears the name and saves, **Then** the
   save is rejected with an inline validation message and the name is unchanged.
3. **Given** a Process detail page, **When** the admin enters a name longer than the allowed
   maximum and saves, **Then** the save is rejected with an inline validation message and the
   name is unchanged.
4. **Given** two Processes "A" and "B", **When** the admin renames "A" to "B", **Then** the
   save is rejected with an inline "name already in use" message and "A" keeps its name.
5. **Given** a Process named "Crocus 2025", **When** the admin re-submits the same name
   (unchanged), **Then** nothing is persisted, no audit entry is written, and no error is
   shown.
6. **Given** a **Closed** Process, **When** the admin changes its name and saves, **Then** the
   rename succeeds exactly as it would for an Active Process.

### Edge Cases

- **Same name (no-op)**: Submitting the current name unchanged makes no change and writes no
  audit entry.
- **Surrounding whitespace**: Leading/trailing whitespace is trimmed before the name is
  compared and persisted.
- **Maximum length boundary**: A name exactly at the maximum length (120 characters) is
  accepted; one character over is rejected.
- **Concurrent collision**: If two admins rename two different Processes to the same name at
  the same time, exactly one succeeds and the other receives the "name already in use" error;
  no duplicate names are persisted.
- **Unknown Process**: A rename request targeting a non-existent Process returns "not found".

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Process detail page MUST present an inline, editable Name affordance,
  pre-filled with the Process's current name, consistent with the other inline edit
  affordances already on that page (Fund reassignment, stage-window override).
- **FR-002**: The Name affordance MUST be available regardless of Process status — both Active
  and Closed Processes can be renamed at any time.
- **FR-003**: Saving a changed, valid name MUST persist the new name and record an admin audit
  entry capturing the actor and the old and new names, consistent with how other Process
  administrative actions are audited.
- **FR-004**: The system MUST validate the name as required, trimmed, and no longer than the
  established maximum (120 characters). Invalid input MUST be rejected with an inline,
  Spanish (es-CR) validation message and MUST NOT change the stored name.
- **FR-005**: The system MUST reject a name that duplicates another Process's name, surfacing
  the same Spanish (es-CR) "name already in use" message used when creating a Process, and
  MUST NOT change the stored name.
- **FR-006**: Submitting the current name unchanged MUST be a no-op — no persistence, no audit
  entry, and no error.
- **FR-007**: On a successful rename, the system MUST return the admin to the Process detail
  page with a Spanish (es-CR) success confirmation, and the new name MUST be reflected on the
  detail page header, the breadcrumb, and the Processes list.
- **FR-008**: All user-facing copy introduced by this feature (labels, validation messages,
  success confirmation) MUST be in Spanish (es-CR).

### Key Entities

- **Process**: The top-level annual-cycle entity (e.g. *"Crocus 2025"*). This feature changes
  only its **Name** attribute. Process name is unique across the catalog. No other Process
  attribute, and no related entity (Fund, Group, Plantilla snapshot), is affected.
- **Admin Audit Entry**: An existing record of an administrative action. This feature adds a
  new entry type for a Process rename, carrying the actor and the old/new name, following the
  existing Process audit-event naming convention.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can rename a Process from its detail page and see the new name on the
  detail page and the Processes list, with the change recorded in the audit log — completing
  the task in a single inline edit without leaving the page or recreating the Process.
- **SC-002**: A rename to a name already used by another Process is rejected with an inline
  message and the original name is preserved (0 duplicate names persisted).
- **SC-003**: An empty, whitespace-only, or over-length name is rejected with an inline message
  and nothing is persisted.
- **SC-004**: Renaming succeeds for a Closed Process as well as an Active one.
- **SC-005**: Re-submitting the unchanged name produces no audit entry and no error.

## Assumptions

- **Authorization**: Reuses the existing admin authorization for the Processes area; no new
  roles or permission rules are introduced.
- **No new editable fields**: "Edit details" is scoped to the Name only. Fund, stage windows,
  Plantilla, Groups, and close are already editable on the detail page and are unchanged.
- **No schema change**: The Process Name column and its uniqueness guarantee already exist;
  this feature adds no tables, columns, or indexes.
- **Uniqueness enforcement**: Name uniqueness continues to be guaranteed by the existing
  catalog-wide unique constraint on Process name (the same gate used at creation), including
  under concurrent submissions.
- **No new dependencies**: No new managed (NuGet) packages are introduced; the feature reuses
  the established admin inline-form + validation + toast-confirmation patterns.

## Out of Scope

- Editing any other Process field (Fund, stage windows, Plantilla, Groups, close are already
  editable on the detail page).
- A dedicated separate Edit page for a Process — the edit affordance is inline on the detail
  page by design.
- Renaming a Process inline from the Processes list rows.
- Bulk rename of multiple Processes.
