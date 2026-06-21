# Feature Specification: Funds-Usage Evidence Inbox

**Feature Branch**: `041-evidence-inbox`  
**Created**: 2026-06-19  
**Status**: Draft  
**Input**: User description: "After executing an agreement and receiving the signature, how does a reviewer come back later to add evidence of the execution of the agreement (bills, photos, etc.)? The option appeared once, then navigating away there was no way back to the application for evidence purposes."

## Summary

Spec 036 added a post-disbursement **"Evidencia de uso de fondos"** stage where in-scope reviewers/admins upload evidence (bills, photos, documents) on an `AgreementExecuted` application "over time as the funds are spent." But that stage is only reachable through a conditional link on the funding-agreement panel, and once an agreement is executed the application falls off **every** reviewer list (the review queue carries Submitted/UnderReview/ReturnedFromAudit/Resolved; the signing inbox carries only pending uploads). The result: a reviewer who navigates away cannot find the application again to add evidence.

This feature adds a **persistent, group-scoped sidebar inbox** of executed applications so reviewers always have a way back, and **bounds the editable evidence window to the application's Process lifetime**: while the application's Process is `Active` the evidence stage behaves exactly as today; once the Process is `Closed` the application drops out of the inbox and its evidence page becomes **read-only** (view + download only).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reviewer returns to an executed application to add evidence (Priority: P1)

A reviewer finished the signing ceremony for an application weeks ago. Funds have been disbursed and the supplier has now delivered bills and photos. The reviewer logs in, clicks **"Evidencia de uso de fondos"** in the sidebar, sees the list of their executed applications, opens the relevant one, and uploads the new evidence.

**Why this priority**: This is the reported gap and the core value — without a persistent entry point the evidence stage is effectively unreachable after execution, making spec 036's "add evidence over time" promise unfulfillable.

**Independent Test**: With at least one `AgreementExecuted` application in an `Active` process belonging to the reviewer's group, the reviewer opens the sidebar entry, sees the application listed, clicks through to its evidence page, and uploads a file successfully. Delivers immediate value on its own.

**Acceptance Scenarios**:

1. **Given** an in-scope reviewer and an `AgreementExecuted` application in an `Active` process that belongs to one of the reviewer's groups, **When** the reviewer opens the sidebar **"Evidencia de uso de fondos"** entry, **Then** the application appears in the inbox with its application number, applicant name, and fund/process identification, and a link to its evidence page.
2. **Given** the inbox is open, **When** the reviewer clicks an application row, **Then** they land on that application's evidence page and can upload, edit notes on, delete, and download evidence (full spec-036 behavior).
3. **Given** an admin, **When** they open the sidebar entry, **Then** they see every `AgreementExecuted` application in an `Active` process regardless of group.
4. **Given** a reviewer with no group memberships, **When** they open the inbox, **Then** it is empty.
5. **Given** there are no qualifying applications for the user, **When** they open the inbox, **Then** they see a friendly empty-state message in es-CR (not an error).

---

### User Story 2 - Closing the process freezes and de-lists evidence (Priority: P2)

When a funding Process is closed, the administrative cycle for its applications is over. Their executed applications should no longer clutter the reviewer's active inbox, and no further evidence should be added — but the evidence already captured must remain viewable and downloadable for the record.

**Why this priority**: Bounds the working set so the inbox stays relevant, and enforces the business rule that evidence collection ends when the process ends, while preserving the historical record. Builds on US1 but is independently demonstrable.

**Independent Test**: Take an `AgreementExecuted` application whose Process is `Closed`; confirm it is absent from the inbox, confirm its evidence page opens read-only for an in-scope reviewer (existing files listed and downloadable), and confirm upload/edit/delete are unavailable and rejected.

**Acceptance Scenarios**:

1. **Given** an `AgreementExecuted` application whose Process is `Closed`, **When** an in-scope reviewer or admin opens the inbox, **Then** the application does **not** appear in the list.
2. **Given** the same application reached by direct link, **When** an in-scope reviewer or admin opens its evidence page, **Then** the page loads (no 404), lists existing evidence, allows download, shows a clear es-CR read-only notice, and does **not** offer upload, edit-note, or delete controls.
3. **Given** a `Closed` process, **When** a crafted upload, edit-note, or delete request is submitted directly to the server, **Then** it is rejected and no evidence is created, modified, or removed.
4. **Given** a `Closed` process that an admin later reopens to `Active`, **When** the reviewer opens the inbox and the evidence page again, **Then** the application reappears in the inbox and the page returns to full read-write behavior.

---

### User Story 3 - Access control is preserved on the new surfaces (Priority: P2)

The new inbox and the read-only mode must not weaken spec 036's access rules: applicants and out-of-group reviewers must be refused with no disclosure of whether the application or its evidence exists.

**Why this priority**: A navigation feature must not become a disclosure or privilege-escalation vector. Independently testable via refusal checks.

**Independent Test**: An out-of-group reviewer and the owning applicant each attempt to reach the inbox-linked application's evidence page and a direct download/mutation route, for both `Active` and `Closed` processes, and are refused with the same no-disclosure response spec 036 already gives.

**Acceptance Scenarios**:

1. **Given** an applicant (including the application's owner), **When** they attempt to reach the sidebar entry or any evidence page/file, **Then** access is refused (the sidebar entry is not offered to applicants; direct access is refused with no disclosure).
2. **Given** a reviewer not assigned to the application's group, **When** they attempt to reach the application's evidence page or a download/mutation route (in either `Active` or `Closed` process state), **Then** access is refused with no disclosure of existence.
3. **Given** the inbox, **When** it is rendered for any user, **Then** it lists only applications the user is authorized to see under the existing group-overlap rule.

---

### Edge Cases

- **Process closed then reopened**: the application reappears in the inbox and the evidence page returns to read-write (status is evaluated live, not snapshotted).
- **Application executed while its process is already closed**: it never appears in the inbox; its evidence page is read-only from the start.
- **Empty inbox**: rendered as a friendly es-CR empty state, never an error.
- **Process closes while a reviewer is mid-upload**: the server-side read-only enforcement is authoritative and rejects the in-flight mutation even if the page was loaded while the process was still active.
- **Application with no evidence yet, process active**: appears in the inbox; its evidence page shows the existing spec-036 empty state with upload available.
- **Application with evidence, process closed**: appears nowhere in the inbox; evidence remains viewable/downloadable via read-only page.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST present a sidebar entry labeled **"Evidencia de uso de fondos"** visible only to users in the Reviewer or Admin role; it MUST NOT be offered to applicants.
- **FR-002**: The sidebar entry MUST open an inbox that lists applications which are in the executed-agreement state **and** whose governing Process is `Active`. The list MUST be group-scoped identically to the existing reviewer worklist: a reviewer sees only applications whose applicant shares at least one group with them; an admin sees all; a reviewer with no group memberships sees an empty list.
- **FR-003**: Each inbox row MUST identify the application sufficiently to choose it — at minimum the application number, the applicant's name, and the fund/process it belongs to — and MUST link to that application's existing evidence page.
- **FR-004**: When an application's governing Process is `Closed`, the application MUST NOT appear in the inbox. Process status MUST be evaluated at request time so that reopening a Process restores the application to the inbox.
- **FR-005**: While the governing Process is `Active`, the evidence page MUST retain its full existing behavior: upload, edit-note, delete, and download (no change to spec 036).
- **FR-006**: When the governing Process is `Closed`, the evidence page MUST operate in read-only mode for in-scope reviewers/admins: it MUST remain reachable (no 404), MUST list existing evidence, MUST allow download, MUST display a clear es-CR read-only notice, and MUST NOT present upload, edit-note, or delete controls.
- **FR-007**: When the governing Process is `Closed`, the system MUST reject any upload, edit-note, or delete request server-side — including requests crafted to bypass the hidden UI controls — without creating, modifying, or deleting any evidence.
- **FR-008**: The system MUST preserve spec 036's access control unchanged on both the inbox and the evidence page (including direct download and mutation routes): applicants and reviewers outside the application's group MUST be refused with no disclosure of whether the application or its evidence exists, in both `Active` and `Closed` process states.
- **FR-009**: All new or changed user-facing copy (sidebar label, inbox page title, empty-state message, read-only notice, and any blocked-action message) MUST be in es-CR.
- **FR-010**: Closing a Process MUST NOT delete, archive, or otherwise alter stored evidence; the data MUST remain intact and retrievable via the read-only page.

### Non-Functional Requirements

- **NFR-001**: Group-overlap scoping for the inbox MUST be enforced at the data-query level (consistent with the existing reviewer-scope mechanism), not only by hiding rows in the UI.
- **NFR-002**: The feature MUST introduce no new application lifecycle state, no database schema change, and no new third-party/managed dependency. It builds entirely on existing constructs (the executed-agreement state, the existing Process Active/Closed status, the existing reviewer-scope rule, and the existing evidence stage).

### Key Entities

- **Application (executed-agreement state)**: The unit listed in the inbox and owning the evidence. Relevant only once it has reached the executed-agreement state. Its group determines reviewer visibility; its governing Process's status determines inbox membership and read-only mode.
- **Process (Active/Closed)**: The governing cycle reached via the application's group. Its status is the single switch between "listed + read-write" (`Active`) and "de-listed + read-only" (`Closed`).
- **Funds-usage evidence item (existing, spec 036)**: A stored file with uploader, timestamp, and optional note. Created/edited/deleted only while the Process is `Active`; always viewable/downloadable by in-scope users.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From any page in the application, an in-scope reviewer can reach an executed application's evidence page in at most two clicks via the sidebar entry.
- **SC-002**: 100% of executed applications whose governing Process is `Active` and that belong to the user's scope appear in the inbox; 0% of applications whose governing Process is `Closed` appear.
- **SC-003**: With a `Closed` process, 100% of attempted upload, edit-note, and delete operations (including crafted direct requests) are rejected with no change to stored evidence, while view and download succeed for in-scope users.
- **SC-004**: 100% of attempts by applicants and out-of-group reviewers to reach the inbox-linked evidence page, its files, or its mutation routes are refused with no disclosure of existence, unchanged from spec 036, in both `Active` and `Closed` process states.
- **SC-005**: No regression to the existing evidence stage while the Process is `Active`: all spec-036 acceptance behaviors (upload, list, note add/edit, delete, download, scoped refusals) continue to pass.

## Assumptions

- "The process closes" maps to the application's governing **Process** status transitioning to `Closed` (the existing `Active`/`Closed` Process status). The Process is reached via `Application → Group → Process`.
- "The results should be gone" when the process closes means the application is removed from the inbox **navigation** and the evidence page becomes read-only; it does **not** mean evidence data is deleted (data is preserved per FR-010).
- Admins are subject to the same `Active`/`Closed` read-only rule on the evidence page (closing a process freezes evidence for everyone); admins retain their broader visibility only for *which applications* appear (group bypass), not for bypassing the read-only freeze. This mirrors "read-only for pages if open" applying uniformly.
- The inbox uses a simple capped list consistent with the existing reviewer worklist (no dedicated search/pagination in this iteration).
- The evidence page continues to live at its current per-application route; this feature adds an entry point and a state-dependent mode, not a new evidence location.

## Dependencies

- **Spec 036 (funds-usage evidence)**: the evidence stage, its storage, and its access rules that this feature surfaces and extends.
- **Spec 016 (user groups / reviewer scope)**: the group-overlap rule reused for the inbox.
- **Process Active/Closed status**: the existing lifecycle switch driving inbox membership and read-only mode.
- **Existing sidebar navigation**: where the new role-gated entry is added.

## Out of Scope

- Deleting or archiving stored evidence when a process closes (data is preserved, only frozen).
- Any applicant-facing access to the evidence stage (remains reviewer/admin only).
- Notifications or emails about the evidence stage or process-close.
- Search, filtering, or pagination on the inbox beyond a simple capped list.
- Changes to how or when a Process transitions between `Active` and `Closed`.
