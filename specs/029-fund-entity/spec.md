# Feature Specification: Fund (Fondo) Entity

**Feature Branch**: `029-fund-entity`
**Created**: 2026-06-09
**Status**: Draft
**Input**: User description: "Introduce a Fund (Fondo) as the top-level container above Process, carrying name, description, and an optional regulation PDF; each Process must belong to exactly one Fund."

## Overview

The funding hierarchy today starts at **Process** (e.g., "Crocus 2025"), under which sit Groups and their members, with a Plantilla defining submission validations. There is no entity above Process to express *which fund a Process draws from* or *what regulation governs it*.

This feature introduces **Fund** (es-CR: *Fondo*) as the new top-level container: **Fund → Process → Group → members**. Each Process belongs to exactly one Fund. A Fund carries its name, a description, and an optional **regulation** document (PDF) that applicants can download in the context of any Process under that Fund. Funds are managed by administrators and follow an Active/Archived lifecycle; archiving a Fund freezes all activity beneath it.

The system is **not yet in production**, so there are no data-migration concerns — seed/demo data will create a Fund and attach existing seed Processes to it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administer Funds (Priority: P1)

An administrator creates and maintains Funds: each Fund has a name, a description, and optionally a regulation PDF. The admin can edit Fund details and upload, replace, or remove the regulation document.

**Why this priority**: Funds are the new foundational entity. Nothing else (Process association, regulation download, reporting) is possible until Funds can be created and maintained. This story alone delivers a usable Fund catalog.

**Independent Test**: Log in as an admin, create a Fund with a name and description, upload a regulation PDF, edit the name, replace the PDF, then remove it — verifying each change persists and is reflected in the Fund list and detail views.

**Acceptance Scenarios**:

1. **Given** an admin on the Fund list, **When** they create a Fund with a unique name and a description (no PDF), **Then** the Fund is saved as Active and appears in the list.
2. **Given** an existing Fund, **When** the admin edits its name to another unique value and saves, **Then** the new name is persisted and shown.
3. **Given** an existing Fund, **When** the admin uploads a valid PDF as the regulation, **Then** the document is stored and a download affordance appears on the Fund.
4. **Given** a Fund with a regulation PDF, **When** the admin uploads a different PDF, **Then** the new document replaces the prior one and only the latest is served.
5. **Given** a Fund with a regulation PDF, **When** the admin removes it, **Then** the Fund retains its name and description but no longer offers a regulation download.
6. **Given** an admin creating a Fund, **When** they submit a non-PDF or an over-size file as the regulation, **Then** the upload is rejected with an es-CR message and no document is stored.
7. **Given** an admin creating a Fund, **When** they submit a name that duplicates an existing Fund (case-insensitive) or leave name/description blank, **Then** the save is blocked with an es-CR validation message.

---

### User Story 2 - Associate every Process with a Fund (Priority: P1)

When an administrator creates or edits a Process, they must select the Fund it belongs to. A Process cannot exist without a Fund. Only Active Funds are selectable.

**Why this priority**: This enforces the core invariant of the new hierarchy ("a Process must belong to one Fund"). Without it the Fund catalog is decorative. Together with US1 it forms the MVP.

**Independent Test**: As an admin, attempt to create a Process without selecting a Fund (blocked), then create one with a Fund selected (succeeds), then reassign it to a different Active Fund via the edit screen.

**Acceptance Scenarios**:

1. **Given** the Process create form, **When** the admin attempts to save without selecting a Fund, **Then** the save is blocked with a required-field error.
2. **Given** the Process create form, **When** the admin selects an Active Fund and saves, **Then** the Process is created and shows its owning Fund.
3. **Given** an existing Process, **When** the admin changes its Fund to a different Active Fund and saves, **Then** the Process is reassigned to the new Fund.
4. **Given** the Process Fund selector, **When** the admin views the list of choices, **Then** only Active Funds appear (Archived Funds are not selectable).

---

### User Story 3 - Applicant downloads the governing regulation (Priority: P2)

An applicant viewing a Process can download the regulation PDF of the Fund that Process belongs to, so they understand the rules that govern their request.

**Why this priority**: Delivers direct applicant value and is the reason the regulation document exists, but depends on US1 (a Fund with a regulation) and US2 (Process↔Fund link) being in place first.

**Independent Test**: As an applicant, open a Process whose Active Fund has a regulation PDF and download it; open another Process whose Fund has no regulation and confirm no download link is shown.

**Acceptance Scenarios**:

1. **Given** an Active Fund with a regulation PDF, **When** an applicant opens a Process under that Fund, **Then** a download link for the regulation is shown and the document downloads successfully.
2. **Given** an Active Fund with no regulation PDF, **When** an applicant opens a Process under that Fund, **Then** no regulation download link is shown.

---

### User Story 4 - Archive a Fund to freeze its activity (Priority: P2)

An administrator archives a Fund that is no longer in use. Archiving freezes all activity beneath the Fund (no new Process attachment; its Processes and their submissions, edits, and reviewer actions become read-only) and hides the Fund and its Processes from non-admin users. Admins reach archived Funds via a status filter and can reactivate them.

**Why this priority**: Important lifecycle control, but the catalog and Process association (US1/US2) deliver value before archiving is needed.

**Independent Test**: As an admin, archive an Active Fund that has Processes; confirm those Processes are no longer visible/actionable to applicants and reviewers, that the Fund disappears from the Process create selector, that an admin status filter surfaces it, and that reactivation restores it.

**Acceptance Scenarios**:

1. **Given** an Active Fund with Processes, **When** the admin archives it, **Then** its Processes are frozen (read-only) and hidden from non-admin users.
2. **Given** a just-archived Fund, **When** an admin opens the Process create Fund selector, **Then** that Fund no longer appears as a choice.
3. **Given** an Archived Fund, **When** an admin filters the Fund list by Archived, **Then** the Fund is listed and can be reactivated.
4. **Given** an Archived Fund, **When** the admin reactivates it, **Then** it becomes Active, its Processes become actionable again, and it reappears in the Process Fund selector.

---

### User Story 5 - Filter Processes and reports by Fund (Priority: P3)

An administrator filters the Process list and existing admin reports/exports by Fund, and views a Fund's detail page listing all Processes that belong to it.

**Why this priority**: A reporting/visibility convenience that builds on the association established in US2; valuable but not required for the core workflow.

**Independent Test**: As an admin with multiple Funds and Processes, filter the Process list by one Fund (only its Processes show), apply the Fund filter to an existing report/export, and open a Fund detail page to see its Processes.

**Acceptance Scenarios**:

1. **Given** multiple Funds each owning Processes, **When** the admin filters the Process list by a Fund, **Then** only Processes belonging to that Fund are shown, and the owning Fund is visible as a column.
2. **Given** an existing admin report/export, **When** the admin applies the Fund filter, **Then** the results are limited to the selected Fund and process-scoped rows show the Fund.
3. **Given** a Fund with Processes, **When** the admin opens the Fund detail page, **Then** all Processes belonging to that Fund are listed.

---

### Edge Cases

- **Fund without a regulation PDF**: allowed; applicants simply see no download link.
- **Invalid regulation upload**: a non-PDF or over-cap file is rejected with an es-CR validation message and nothing is stored.
- **Archiving a Fund mid-application**: in-flight Processes/applications under it freeze immediately; only admins can still view them.
- **Replacing the regulation PDF**: the new document is served going forward and the prior reference is superseded (no version history kept).
- **Duplicate Fund name**: rejected case-insensitively against existing Funds.
- **Seed/legacy data**: no production data exists; seed data creates a Fund and attaches seed Processes to it so the required association holds from first run.
- **Unaffected invariants**: existing rules (a Process must have at least one Group; a member must belong to a Group) are unchanged and out of scope.

## Requirements *(mandatory)*

### Functional Requirements

**Fund management (admin)**

- **FR-001**: Admins MUST be able to create a Fund with a Name (required), a Description (required), and an optional regulation PDF. New Funds default to Active.
- **FR-002**: Admins MUST be able to edit a Fund's Name and Description.
- **FR-003**: Admins MUST be able to upload, replace, or remove a Fund's regulation document. A Fund has at most one regulation document; it MUST be a PDF and within the configured size cap for the regulation category.
- **FR-004**: Admins MUST be able to archive an Active Fund and reactivate an Archived Fund.
- **FR-005**: Archiving a Fund MUST freeze all activity beneath it — no new Process may be attached, and its Processes (and their submissions, edits, and reviewer actions) MUST become read-only. Archived Funds and their Processes MUST be hidden from non-admin users; admins MUST be able to reach them via a status filter.
- **FR-006**: Funds MUST NOT be hard-deletable; archiving is the retirement path.

**Process ↔ Fund association**

- **FR-007**: Every Process MUST belong to exactly one Fund. The Process create/edit experience MUST present a required Fund selector that lists only Active Funds.
- **FR-008**: The system MUST reject creating or saving a Process that has no Fund or whose selected Fund is not Active.
- **FR-009**: Admins MUST be able to reassign a Process to a different Active Fund.

**Regulation availability**

- **FR-010**: Applicants MUST be able to download a Fund's regulation document in the context of a Process belonging to that Fund, when the Fund is Active and a regulation document exists.

**Reporting / queries**

- **FR-011**: The admin Process list MUST display each Process's owning Fund and MUST support filtering by Fund.
- **FR-012**: Existing admin reports/exports MUST support filtering by Fund, and process-scoped rows MUST identify the owning Fund.
- **FR-013**: A Fund detail view MUST list all Processes belonging to that Fund.

**Validation & audit**

- **FR-014**: Fund Name MUST be required, non-empty (trimmed), and unique among Funds case-insensitively; Description MUST be required and non-empty. Violations MUST be reported with es-CR messages.
- **FR-015**: Administrative changes to Funds (create, edit, archive, reactivate, regulation upload/replace/remove) MUST be recorded in the existing admin audit trail.
- **FR-016**: All new and modified user-facing copy MUST be in es-CR.

### Key Entities

- **Fund (Fondo)**: The new top-level container above Process. Attributes: Name (unique, required), Description (required), Status (Active | Archived), and an optional reference to a single regulation document. A Fund owns one or more Processes. A Fund never directly contains Groups or members — those relationships hang off its Processes.
- **Process** (existing): Gains a required association to exactly one Fund. All other Process relationships (Groups, Plantilla) are unchanged.
- **Regulation document**: A single PDF governing a Fund, stored via the platform's existing object-storage mechanism and served to applicants through a time-limited link. Replaceable and removable; not versioned.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can create a fully specified Fund (name, description, regulation PDF) in a single form submission without leaving the Fund-management area.
- **SC-002**: 100% of Processes in the system belong to exactly one Fund — it is not possible to create or save a Process without an Active Fund.
- **SC-003**: An applicant viewing a Process whose Active Fund has a regulation can locate and download that regulation without admin assistance.
- **SC-004**: Archiving a Fund removes it and its Processes from every non-admin view, and an administrator can still locate it within the Fund list using the status filter.
- **SC-005**: An administrator can answer "which Processes belong to this Fund?" from the Fund detail view and constrain the Process list / existing reports to a single Fund.
- **SC-006**: Invalid Fund inputs (duplicate name, blank name/description, non-PDF or over-size regulation) are rejected with a clear es-CR message and leave no partial data.

## Assumptions

- Fund management is **admin-only**; no new role or per-Fund permission scoping is introduced (deferred).
- The system is **not in production**; no data migration is required. Seed/demo data creates a Fund and attaches existing seed Processes so the required Process→Fund association holds from first run.
- The regulation document reuses the platform's existing object-storage and time-limited-link serving; a new storage category governs its PDF-only constraint and size cap.
- Fund administration reuses the existing admin audit trail, in-app toast/dialog conventions, and es-CR localization approach already established in the platform.
- "Freeze all activity" on archive means existing read paths remain for admins while all create/edit/submit/review actions under the Fund are disabled; the exact set of affected actions follows the Process's existing state model.
- No new third-party/managed dependencies are introduced.

## Out of Scope

- Fund → Groups and Fund → Participants rollup reports (drill-down beyond Processes).
- Multiple regulation documents per Fund and regulation versioning/history.
- Per-Fund permission scoping or non-admin Fund management.
- Surfacing the regulation across additional applicant surfaces (landing page, emails).
- Hard deletion of Funds.
