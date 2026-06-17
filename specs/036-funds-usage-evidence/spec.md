# Feature Specification: Funds-Usage Evidence Stage

**Feature Branch**: `036-funds-usage-evidence`
**Created**: 2026-06-16
**Status**: Draft
**Input**: User description: "Once an application is approved and the funds are given to the person, reviewers enter one more stage of the application life-cycle: upload evidence of funds usage. Reviewers can upload any file type (images, PDFs) that evidences correct execution of the funds, access this stage like the other stages, delete an existing evidence, and each evidence carries an optional note (max 250 chars). For now only reviewers and up can access this."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Collect funds-usage evidence on an executed application (Priority: P1)

Once an application's funding agreement has been executed (funds disbursed), an in-scope reviewer opens a new **"Evidencia de uso de fondos"** stage on that application — reached the same way as the application's other stages. There the reviewer uploads one or more files (photos, PDFs, Office documents) that evidence the correct execution of the funds. Each uploaded file appears in a list showing its file name, who uploaded it, and when, with a link to download/open it. The reviewer can keep adding files over time as the funds are spent.

**Why this priority**: This is the core of the feature and the minimum viable product. Without the ability to upload and see evidence, nothing else matters. It directly satisfies the business need: a post-disbursement place to gather proof that money was used correctly.

**Independent Test**: On an application that has reached the executed-agreement state, an in-scope reviewer uploads a PDF and an image, sees both listed with file name / uploader / timestamp / download link, and downloads one back successfully. Fully testable on its own and delivers immediate value (evidence is captured and retrievable).

**Acceptance Scenarios**:

1. **Given** an application whose agreement has been executed and an in-scope reviewer, **When** the reviewer opens the evidence stage and uploads a PDF, **Then** the file is stored and appears in the evidence list with its file name, uploader, upload time, and a download link.
2. **Given** the same reviewer on the same application, **When** they upload a second file (an image), **Then** both items appear in the list and neither replaces the other.
3. **Given** an evidence item in the list, **When** the reviewer activates its download link, **Then** the original file is served back unchanged.
4. **Given** an application that has **not** yet reached the executed-agreement state, **When** an in-scope reviewer looks at it, **Then** the evidence stage is not available (no upload surface is shown or reachable).

---

### User Story 2 - Annotate each evidence item with a note (Priority: P2)

When uploading (or afterward), the reviewer can attach a short note to an evidence item explaining what it shows (for example, "Factura de compra de equipo, marzo 2026"). The note is optional and limited to 250 characters, and can be edited later without re-uploading the file.

**Why this priority**: Notes make the evidence intelligible to other reviewers and auditors, but the evidence is still useful without them. High value, not blocking.

**Independent Test**: A reviewer adds a 250-character note to an existing evidence item, sees it displayed on that item, edits the note text, and sees the update — all without re-uploading the file.

**Acceptance Scenarios**:

1. **Given** an evidence item, **When** the reviewer saves a note of up to 250 characters, **Then** the note is stored and shown alongside the item.
2. **Given** an evidence item with an existing note, **When** the reviewer changes the note text and saves, **Then** the displayed note reflects the new text.
3. **Given** an evidence item, **When** the reviewer saves it with no note, **Then** the item is accepted and shown without a note.
4. **Given** a note longer than 250 characters, **When** the reviewer tries to save it, **Then** the save is rejected with a clear message in es-CR and the note is not stored.

---

### User Story 3 - Remove an evidence item (Priority: P2)

A reviewer can delete an evidence item that is no longer needed or was uploaded in error. Deletion asks for confirmation, then removes both the listed item and its stored file. Any in-scope reviewer or admin can delete any item on the application, not only the person who uploaded it.

**Why this priority**: Keeping the evidence set clean and correct matters for compliance, but it is secondary to capturing evidence in the first place.

**Independent Test**: A reviewer deletes one of several evidence items via a confirmation prompt, sees it disappear from the list, and confirms the file is no longer downloadable, while the other items remain.

**Acceptance Scenarios**:

1. **Given** an evidence item uploaded by another reviewer, **When** an in-scope reviewer confirms deletion, **Then** the item is removed from the list and its file is no longer retrievable.
2. **Given** a delete action, **When** the reviewer is shown the confirmation prompt and cancels, **Then** nothing is removed.
3. **Given** two reviewers viewing the same item, **When** both delete it, **Then** the first deletion succeeds and the second resolves harmlessly (the item is already gone) without error to the user.

---

### User Story 4 - Scoped, reviewer-only access (Priority: P3)

The evidence stage is visible and usable only to reviewers and admins. A reviewer can reach the evidence of an application only when that application belongs to a group they are assigned to; admins can reach any application's evidence. Applicants and out-of-scope reviewers cannot see the stage or reach its files; attempts are refused the same way other reviewer-only surfaces refuse access (no disclosure of whether the evidence or application exists).

**Why this priority**: The access boundary is essential for confidentiality, but it is expressed as a constraint over Stories 1–3 rather than a standalone user-visible feature; it is validated last.

**Independent Test**: An in-group reviewer can open the stage; a reviewer not assigned to the application's group, and an applicant, both fail to reach the stage or any evidence file, receiving the standard not-found/forbidden response.

**Acceptance Scenarios**:

1. **Given** a reviewer assigned to the application's group, **When** they open the evidence stage, **Then** they can view, upload, annotate, and delete evidence.
2. **Given** a reviewer **not** assigned to the application's group, **When** they attempt to reach the stage or a download link, **Then** access is refused with no disclosure of existence.
3. **Given** an applicant (the account that owns the application), **When** they attempt to reach the evidence stage or any evidence file, **Then** access is refused.
4. **Given** an admin, **When** they open any application's evidence stage, **Then** they can view and manage its evidence regardless of group.

---

### Edge Cases

- **Application not yet executed**: The evidence stage is unavailable until the application reaches the executed-agreement state; before then there is no upload surface and no stored evidence.
- **Disallowed file type**: A file outside the accepted set (images, PDF, Office documents) is rejected with a clear es-CR message and no evidence item is created.
- **Oversized file**: A file larger than the per-file cap (20 MiB) is rejected with a clear es-CR message before any item is created.
- **Empty note**: Allowed — the note is optional.
- **Concurrent deletion**: Two reviewers deleting the same item — the first succeeds, the second resolves to "already gone" without surfacing an error.
- **Storage failure mid-upload**: If the file cannot be stored, no evidence item is recorded (no orphaned list entry pointing at a missing file).
- **Many files on one application**: The evidence list accommodates many items without a fixed maximum count.
- **Empty state**: When an executed application has no evidence yet, the stage shows a clear es-CR empty-state message rather than a blank area.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST present a "Evidencia de uso de fondos" stage on an application, reachable in the same manner as the application's other stages, **only when** the application has reached the executed-agreement state (funds disbursed).
- **FR-002**: The system MUST restrict the evidence stage to reviewers and admins. A reviewer MUST be able to reach an application's evidence only when the application belongs to a group the reviewer is assigned to; an admin MUST be able to reach any application's evidence. Applicants and out-of-scope reviewers MUST be refused with no disclosure of existence.
- **FR-003**: A reviewer MUST be able to upload one or more evidence files to an eligible application, each upload producing a distinct evidence item that does not replace existing items.
- **FR-004**: The system MUST accept evidence files of the following types — images (JPEG, PNG, WebP, HEIC), PDF, and common Office documents (Word, Excel) — and MUST reject any other type with a clear es-CR message, creating no item.
- **FR-005**: The system MUST reject any evidence file larger than 20 MiB with a clear es-CR message, creating no item.
- **FR-006**: Each evidence item MUST support an optional note of at most 250 characters, settable at upload and editable afterward without re-uploading the file. A note exceeding 250 characters MUST be rejected with a clear es-CR message.
- **FR-007**: Any in-scope reviewer or admin MUST be able to delete any evidence item on an application (not only its uploader); deletion MUST be confirmed before it proceeds and MUST remove both the listed item and its stored file.
- **FR-008**: For each evidence item, the system MUST display the file name, the note (if any), who uploaded it, when it was uploaded, and a means to download/open the original file.
- **FR-009**: An in-scope reviewer or admin MUST be able to download/open an evidence file; the same group-scoping and reviewer-only access as FR-002 MUST apply to downloads.
- **FR-010**: The system MUST record an audit entry for each evidence upload, note edit, and deletion, capturing at minimum the acting user, the timestamp, the application, and the evidence file name.
- **FR-011**: All evidence-stage copy MUST be in es-CR, including labels, validation messages, the confirmation prompt, and an empty-state message shown when an eligible application has no evidence.
- **FR-012**: Uploading, annotating, downloading, and deleting evidence MUST NOT change the application's lifecycle state; the application remains in the executed-agreement state while evidence accrues (evidence is an open, ongoing collection, not a gated transition).

### Key Entities *(include if feature involves data)*

- **Funds-Usage Evidence Item**: A single piece of evidence attached to one application. Represents an uploaded file plus its metadata: the original file name, the stored file reference, file size, content type, an optional note (≤250 characters), the uploading user, and the upload timestamp. Belongs to exactly one application; an application may have zero or many. Has no independent lifecycle beyond existing until deleted.
- **Application** (existing): The aggregate that owns evidence items. Only relevant once it has reached the executed-agreement state. Its group membership determines reviewer access to the evidence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On an application in the executed-agreement state, an in-scope reviewer can upload a PDF and an image and see both listed with file name, uploader, and timestamp in a single session.
- **SC-002**: A reviewer can attach a note of up to 250 characters to an evidence item and later change it, with the displayed note reflecting each change, without re-uploading the file.
- **SC-003**: A reviewer can delete any evidence item after a confirmation step, after which the item and its file are no longer present or downloadable, while other items remain.
- **SC-004**: 100% of attempts to reach the evidence stage or its files by applicants and by reviewers not assigned to the application's group are refused with no disclosure of existence.
- **SC-005**: The evidence stage is not reachable for any application that has not reached the executed-agreement state.
- **SC-006**: Every upload, note edit, and deletion produces a corresponding audit entry identifying the actor, time, application, and file.
- **SC-007**: 100% of uploads of disallowed file types or files over 20 MiB are rejected with an es-CR message and create no evidence item.

## Assumptions

- "Funds given to the person" corresponds to the application having reached the **executed-agreement** state (the signed funding agreement has been approved/executed). This is the single trigger for the evidence stage's availability.
- The evidence stage is an **open collection** with no completion/closing action and introduces **no new lifecycle state**; reviewers add and remove evidence at will while the application stays executed.
- Notes are **optional**. An item with no note is valid.
- "Reviewers and up" means the existing **Reviewer** and **Admin** roles; admin access is unrestricted by group, reviewer access is group-scoped, consistent with all other reviewer surfaces.
- Applicants do **not** see evidence in this iteration; applicant-facing visibility is explicitly deferred.
- The per-file size cap is **20 MiB**, matching the existing signed-agreement upload cap; there is no maximum count of evidence items per application.
- Existing platform capabilities are reused: object storage for files, the reviewer group-scoping model, the in-app confirmation/toast system, and the audit-event system. No new third-party dependencies are introduced.

## Out of Scope

- Applicant visibility of evidence (deferred to a future iteration).
- Any evidence approval/review/decision workflow — this iteration is **collection only**; no accept/reject/score of evidence.
- Required-evidence gating, completeness checks, or any action that "closes" or completes the application based on evidence.
- Evidence versioning or revision history (delete + re-upload is the only way to replace a file).
- Virus/malware scanning beyond file-type and size validation.
