# Feature Specification: Fund (Fondo) Entity

**Feature Branch**: `029-fund-entity`
**Created**: 2026-06-09
**Status**: Draft (evolved during planning 2026-06-10)
**Input**: User description: "Introduce a Fund (Fondo) as the top-level container above Process, carrying name, description, and an optional regulation PDF; each Process must belong to exactly one Fund."

## Planning Evolution (2026-06-10)

During `/speckit-plan` research two architectural facts surfaced that the original brainstorm assumed away:

1. **An `Application` has no stored link to a `Process`.** Today an application is tied only to its `Applicant`; its Process/Plantilla/Fund is *derived* through the applicant's reviewer-scoping group memberships, which can span multiple Processes — so the mapping is ambiguous (Plantilla validation even resolves it with `FirstOrDefault`).
2. **`Process.Close()` blocks rather than freezes:** the existing lifecycle refuses to close a Process while active applications exist; it has no "freeze in-flight work" mechanism.

The product owner chose the stronger options on both, which **evolves** this spec beyond the original "Group/Plantilla wiring unchanged" stance:

- **Authoritative anchor** — an `Application` gains a required link to the **Group** it is filed under (captured at application creation), making its Process and Fund an exact derivation (`Application → Group → Process → Fund`). This also makes Plantilla resolution deterministic. See **FR-017..FR-019**.
- **Force-freeze** — archiving a Fund immediately makes its Processes and their in-flight applications read-only for non-admins (a new query/mutation guard), rather than blocking the archive. See **FR-005** (amended) and **FR-020..FR-021**.

These additions are reflected in the requirements, edge cases, and entities below.

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

1. **Given** an Active Fund with Processes and in-flight applications anchored under it, **When** the admin archives it, **Then** those applications become read-only and disappear from applicant lists, the reviewer queue, and the signing inbox.
2. **Given** an application anchored to a now-Archived Fund, **When** its applicant attempts to edit, add an item/quotation, submit, or withdraw it, **Then** the action is rejected with an es-CR message.
3. **Given** a just-archived Fund, **When** an admin opens the Process create Fund selector, **Then** that Fund no longer appears as a choice.
4. **Given** an Archived Fund, **When** an admin filters the Fund list by Archived, **Then** the Fund is listed and can be reactivated.
5. **Given** an Archived Fund with frozen applications, **When** the admin reactivates it, **Then** it becomes Active and its Processes and anchored applications return to their prior actionable, visible state.

---

### User Story 6 - Anchor each application to its Fund at creation (Priority: P1)

When an applicant creates an application, it is anchored to exactly one Group (and therefore one Process and Fund). If the applicant is eligible for a single Group it is chosen automatically; if eligible for several, the applicant chooses; if eligible for none, they cannot start an application. This authoritative anchor is what makes Fund-scoped reporting and the archive freeze exact.

**Why this priority**: P1 — it is the prerequisite that makes FR-012 (exact Fund on reports) and US4 (force-freeze) implementable, and it removes the existing nondeterministic Plantilla resolution. Without it, US4 and US5's report filter cannot be exact.

**Independent Test**: As an applicant eligible for one Group, create an application and confirm it is anchored to that Group's Process/Fund; as an applicant eligible for multiple Groups, confirm the create form requires a choice; confirm submission validation uses the anchored Process's Plantilla.

**Acceptance Scenarios**:

1. **Given** an applicant eligible for exactly one Group, **When** they create an application, **Then** it is anchored to that Group's Process and Fund with no extra prompt.
2. **Given** an applicant eligible for two or more Groups, **When** they create an application, **Then** they must select the Group/Process and the application is anchored to the selection.
3. **Given** an applicant eligible for no Group, **When** they attempt to create an application, **Then** they are blocked with a clear es-CR message.
4. **Given** an anchored application, **When** it is submitted, **Then** the minimum-quotations/required-field validation uses the Plantilla of the anchored Process (deterministic).

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
- **Archiving a Fund mid-application**: every application anchored to it freezes immediately (read-only) and disappears from non-admin lists/queues; only admins can still view them. Reactivating the Fund restores them.
- **Applicant eligible for multiple Groups**: at application creation the applicant must choose which Group/Process (hence Fund) they are applying under; the choice is fixed for that application.
- **Applicant eligible for no Group**: cannot start an application; shown a clear es-CR message.
- **Replacing the regulation PDF**: the new document is served going forward and the prior reference is superseded (no version history kept).
- **Duplicate Fund name**: rejected case-insensitively against existing Funds.
- **Seed/legacy data**: no production data exists; seed data creates a Fund, attaches seed Processes, and anchors seed applications to a seed Group so the required associations hold from first run.
- **Unaffected invariants**: existing rules (a Process must have at least one Group; a member must belong to a Group) still hold.

## Requirements *(mandatory)*

### Functional Requirements

**Fund management (admin)**

- **FR-001**: Admins MUST be able to create a Fund with a Name (required), a Description (required), and an optional regulation PDF. New Funds default to Active.
- **FR-002**: Admins MUST be able to edit a Fund's Name and Description.
- **FR-003**: Admins MUST be able to upload, replace, or remove a Fund's regulation document. A Fund has at most one regulation document; it MUST be a PDF and within the configured size cap for the regulation category.
- **FR-004**: Admins MUST be able to archive an Active Fund and reactivate an Archived Fund.
- **FR-005**: Archiving a Fund MUST **immediately freeze all activity beneath it** regardless of in-flight state — no new Process may be attached, and every application anchored (via its Group → Process) to that Fund MUST become read-only: applicant create/edit/add-item/add-quotation/submit/withdraw and reviewer decision/signing actions MUST be rejected. Archived Funds, their Processes, and their anchored applications MUST be hidden from non-admin read surfaces (applicant lists, reviewer queues, signing inbox, dashboards); admins MUST be able to reach them via a status filter. (See FR-020/FR-021 for the freeze mechanism.)
- **FR-006**: Funds MUST NOT be hard-deletable; archiving is the retirement path.

**Process ↔ Fund association**

- **FR-007**: Every Process MUST belong to exactly one Fund. The Process create/edit experience MUST present a required Fund selector that lists only Active Funds.
- **FR-008**: The system MUST reject creating or saving a Process that has no Fund or whose selected Fund is not Active.
- **FR-009**: Admins MUST be able to reassign a Process to a different Active Fund.

**Regulation availability**

- **FR-010**: Applicants MUST be able to download a Fund's regulation document in the context of a Process belonging to that Fund, when the Fund is Active and a regulation document exists.

**Reporting / queries**

- **FR-011**: The admin Process list MUST display each Process's owning Fund and MUST support filtering by Fund.
- **FR-012**: Existing admin reports/exports MUST support filtering by Fund, and application/process-scoped rows MUST identify the owning Fund. The owning Fund MUST be derived exactly via the authoritative `Application → Group → Process → Fund` link (FR-017), not approximated through group-membership overlap.
- **FR-013**: A Fund detail view MUST list all Processes belonging to that Fund.

**Validation & audit**

- **FR-014**: Fund Name MUST be required, non-empty (trimmed), and unique among Funds case-insensitively; Description MUST be required and non-empty. Violations MUST be reported with es-CR messages.
- **FR-015**: Administrative changes to Funds (create, edit, archive, reactivate, regulation upload/replace/remove) MUST be recorded in the existing admin audit trail.
- **FR-016**: All new and modified user-facing copy MUST be in es-CR.

**Application anchoring (added during planning — see Planning Evolution)**

- **FR-017**: Every `Application` MUST carry an authoritative reference to the **Group** it is filed under (and therefore, transitively, a single Process and Fund). This reference MUST be captured when the application is created.
- **FR-018**: When an applicant who is eligible for exactly one Group creates an application, the system MUST anchor it to that Group automatically. When the applicant is eligible for more than one Group, the create experience MUST require the applicant to choose the Group/Process they are applying under. An applicant eligible for no Group MUST NOT be able to start an application (with a clear es-CR message).
- **FR-019**: Plantilla-driven submission validation (minimum quotations, required fields) and the Fund regulation shown to the applicant MUST resolve through the application's authoritative anchor (FR-017), replacing the prior nondeterministic group-membership lookup.

**Archive freeze mechanism (added during planning — see Planning Evolution)**

- **FR-020**: A reusable application read filter MUST exclude applications anchored to an Archived Fund from every non-admin read surface (applicant lists/dashboard, reviewer queue, signing inbox, reviewer/admin dashboards), composed alongside the existing soft-delete exclusion. Admin read surfaces MUST be able to opt out of this exclusion to retain visibility.
- **FR-021**: Every applicant- and reviewer-facing mutation on an application anchored to an Archived Fund MUST be rejected (defense-in-depth: enforced both at the controller boundary and at the domain entity), returning an es-CR message. Admin actions are exempt.

### Key Entities

- **Fund (Fondo)**: The new top-level container above Process. Attributes: Name (unique, required), Description (required), Status (Active | Archived), and an optional reference to a single regulation document. A Fund owns one or more Processes. A Fund never directly contains Groups or members — those relationships hang off its Processes.
- **Process** (existing): Gains a required association to exactly one Fund.
- **Application** (existing): Gains a required authoritative association to the **Group** it is filed under (captured at creation), making its Process and Fund an exact derivation. This replaces the previous behavior where an application's Process/Plantilla was inferred nondeterministically from the applicant's group memberships.
- **Regulation document**: A single PDF governing a Fund, stored via the platform's existing object-storage mechanism and served to applicants through a time-limited link. Replaceable and removable; not versioned.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can create a fully specified Fund (name, description, regulation PDF) in a single form submission without leaving the Fund-management area.
- **SC-002**: 100% of Processes in the system belong to exactly one Fund — it is not possible to create or save a Process without an Active Fund.
- **SC-003**: An applicant viewing a Process whose Active Fund has a regulation can locate and download that regulation without admin assistance.
- **SC-004**: Archiving a Fund removes it and its Processes from every non-admin view, and an administrator can still locate it within the Fund list using the status filter.
- **SC-005**: An administrator can answer "which Processes belong to this Fund?" from the Fund detail view and constrain the Process list / existing reports to a single Fund.
- **SC-006**: Invalid Fund inputs (duplicate name, blank name/description, non-PDF or over-size regulation) are rejected with a clear es-CR message and leave no partial data.
- **SC-007**: Every application created after this feature ships has exactly one authoritative Group anchor, so its Process and Fund are derivable with a single deterministic query (no ambiguity).
- **SC-008**: When a Fund is archived, none of its anchored applications can be read or mutated by applicants or reviewers, and every such application returns to its prior behavior verbatim when the Fund is reactivated.

## Assumptions

- Fund management is **admin-only**; no new role or per-Fund permission scoping is introduced (deferred).
- The system is **not in production**; no data migration is required. Seed/demo data creates a Fund and attaches existing seed Processes so the required Process→Fund association holds from first run.
- The regulation document reuses the platform's existing object-storage and time-limited-link serving; a new storage category governs its PDF-only constraint and size cap.
- Fund administration reuses the existing admin audit trail, in-app toast/dialog conventions, and es-CR localization approach already established in the platform.
- "Freeze all activity" on archive means existing read paths remain for admins while all applicant/reviewer create/edit/submit/review actions on anchored applications are disabled, enforced by a reusable query filter plus controller/domain guards (FR-020/FR-021).
- The authoritative `Application → Group` anchor is captured at application creation. Pre-feature applications do not exist in production; seed data anchors any seed applications to a seed Group.
- The reviewer queue's existing group-overlap visibility predicate is retained as-is for *which reviewers* see an application; the new anchor is additive (it does not narrow reviewer visibility), except that the archive-freeze filter (FR-020) additionally hides archived-Fund applications from everyone non-admin.
- No new third-party/managed dependencies are introduced.

## Out of Scope

- Fund → Groups and Fund → Participants rollup reports (drill-down beyond Processes).
- Multiple regulation documents per Fund and regulation versioning/history.
- Per-Fund permission scoping or non-admin Fund management.
- Surfacing the regulation across additional applicant surfaces (landing page, emails).
- Hard deletion of Funds.
- Re-anchoring an existing application to a different Group/Process after creation (the anchor is fixed at creation; admin re-anchoring is a possible follow-up).
- Replacing or tightening the reviewer group-overlap visibility predicate to use the new anchor (kept as-is; potential follow-up).
