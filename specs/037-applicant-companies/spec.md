# Feature Specification: Applicant Companies — controlled company selection on submission

**Feature Branch**: `037-applicant-companies`
**Created**: 2026-06-17
**Status**: Draft
**Input**: User description: "Replace the free-text company-name field on funding-application creation with a controlled dropdown of admin-assigned companies. Each Applicant has one or more admin-managed companies (Name only). Single company auto-selects; multiple require an explicit choice. Submissions store a company reference plus a frozen name snapshot, preserving historical names after later edits. Backend prevents selecting another applicant's company or bypassing the dropdown. Applies only to the Applicant (Solicitante) role."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Applicant selects a company when starting a submission (Priority: P1)

A Solicitante starts a new funding application. Instead of typing a company name, they pick from a dropdown limited to the companies an administrator has assigned to their account. If they have exactly one company it is chosen for them; if they have several they must choose one. They cannot type a free-text name and cannot submit without a company.

**Why this priority**: This is the core behavior change driving the feature — it eliminates inconsistent free-text company data and is the visible outcome for the primary (applicant) role. Without it, the feature delivers no value.

**Independent Test**: Sign in as an applicant with one company (auto-select path) and as an applicant with multiple companies (explicit-choice path); confirm a submission can only be created with a valid owned company selected and that free text is impossible.

**Acceptance Scenarios**:

1. **Given** an applicant with exactly one active company, **When** they open the new-application form, **Then** the company dropdown is visible, pre-selected to that single company, offers no other value, and validation passes without further action.
2. **Given** an applicant with two or more active companies, **When** they open the new-application form, **Then** the dropdown lists all their active companies with no default selection and a "Seleccione una empresa…" placeholder, and the application cannot be created until one is chosen.
3. **Given** an applicant with multiple companies who has not chosen one, **When** they attempt to create the application, **Then** creation is blocked and an es-CR validation message requests a company selection.
4. **Given** an applicant viewing the new-application form, **When** they inspect the company field, **Then** there is no free-text input for a company name anywhere in the flow.

---

### User Story 2 - Administrator manages an applicant's companies (Priority: P1)

An administrator assigns at least one company when creating a Solicitante account, and afterward can add more companies, correct/edit a company's name, and archive companies that should no longer be selectable — without ever destroying the historical record. Applicants have no ability to manage their own companies.

**Why this priority**: Companies cannot be selected (US1) unless administrators can create and maintain them. The "at least one company at creation" rule and name-correction ability are explicit business requirements. This is co-critical with US1 for a usable MVP.

**Independent Test**: As an administrator, create a Solicitante and confirm at least one company is required; then add a second company, rename one, archive one (and confirm the last active one cannot be archived), and confirm an applicant account cannot perform any of these actions.

**Acceptance Scenarios**:

1. **Given** the admin single-user create form with role = Solicitante, **When** the admin submits without specifying any company, **Then** creation is blocked with an es-CR message requiring at least one company.
2. **Given** an existing applicant with one company, **When** the admin adds a second company with a distinct name, **Then** both companies are active and available for that applicant's future submissions.
3. **Given** an applicant company with a misspelled name, **When** the admin edits the name, **Then** the corrected name is shown on the applicant's submission dropdown and on future applications, while previously-created applications keep the name they were created with (see US3).
4. **Given** an applicant with two active companies, **When** the admin archives one, **Then** that company no longer appears in the applicant's new-submission dropdown but remains retrievable/unarchivable by the admin.
5. **Given** an applicant with exactly one active company, **When** the admin attempts to archive it, **Then** the action is blocked with an es-CR message because every applicant must retain at least one active company.
6. **Given** any company-management action (create, rename, archive, unarchive), **When** it succeeds, **Then** an administrative audit record is written.
7. **Given** a signed-in applicant, **When** they look for any control to create, edit, archive, or delete a company, **Then** none exists and any direct attempt is refused by the server.

---

### User Story 3 - Historical company names are preserved (Priority: P2)

When a company's name is later corrected or changed, applications that were already created keep displaying the company name as it was at the time they were created. New applications reflect the current name.

**Why this priority**: Data integrity for the historical record is an explicit requirement, but it is observable only after US1/US2 exist. It is essential for correctness but not required to demonstrate the basic selection flow.

**Independent Test**: Create an application under a company, have an admin rename that company, then confirm the existing application still shows the original name while a newly created application shows the new name.

**Acceptance Scenarios**:

1. **Given** an application created while a company was named "Acme SA", **When** an admin later renames the company to "ACME S.A.", **Then** the existing application still displays "Acme SA".
2. **Given** the same renamed company, **When** the applicant creates a new application under it, **Then** the new application displays "ACME S.A.".
3. **Given** a `Draft` application whose selected company is changed by the applicant to a different company, **When** the change is saved, **Then** the displayed company name updates to match the newly-selected company.

---

### User Story 4 - Bulk applicant import assigns the first company (Priority: P2)

An administrator bulk-creates Solicitante accounts from a CSV. Each imported applicant receives their first company from a required company column, so bulk-provisioned applicants can immediately start submissions.

**Why this priority**: Bulk provisioning is an established admin workflow (spec 034). Without a company column, every batch-created applicant would be unable to submit until manually fixed. Valuable and required, but secondary to the single-create and selection paths.

**Independent Test**: Download the CSV template, confirm it includes the new trailing company column, import a small file, and confirm each created applicant has exactly the company named in their row and can immediately select it on a submission.

**Acceptance Scenarios**:

1. **Given** the downloadable CSV template, **When** the admin downloads it, **Then** it includes a trailing "Nombre de la empresa" column after the existing columns.
2. **Given** an import file with a non-empty company name in every data row, **When** the admin imports it, **Then** each successfully created applicant has one active company with that name.
3. **Given** an import row whose company-name cell is empty, **When** the admin imports the file, **Then** that row is rejected with an es-CR reason and no account is created for it, consistent with the existing per-row reporting.

---

### Edge Cases

- **Applicant with zero active companies** (e.g., a pre-existing applicant created before this feature, or — hypothetically — one whose companies were all archived): the applicant cannot start a submission; the form shows a clear es-CR message directing them to contact an administrator. (The "last active company" floor normally prevents reaching zero through archival.)
- **Company archived while an application sits in `Draft`**: the application's stored name snapshot is preserved; because the archived company no longer appears in the dropdown, the applicant must re-select an active company before the application can be submitted.
- **Duplicate company name**: attempting to create/rename a company to a name that duplicates one of the same applicant's active companies (case- and accent-insensitive) is rejected with an es-CR message. The same duplicate rule applies within a single batch import for one applicant.
- **Forged or manipulated submission request**: a request carrying a company reference that belongs to another applicant, does not exist, or is archived is rejected by the server regardless of what the browser sent, and does not reveal information about other applicants' companies.
- **Pre-existing applications** created before this feature retain their existing free-text name and have no company reference; they continue to display their stored name unchanged.

## Requirements *(mandatory)*

### Functional Requirements

**Company management (administrator-only)**

- **FR-001**: The system MUST model an applicant **Company** with a single attribute — a name (required, trimmed, ≤200 characters) — owned by exactly one applicant, with an applicant able to own one or more companies, and with an archived/active lifecycle state.
- **FR-002**: Companies MUST be managed exclusively by administrators. Applicants MUST NOT be able to create, edit, archive, or delete companies; the system MUST refuse any such applicant-initiated request at the server boundary.
- **FR-003**: A company name MUST be unique among a given applicant's **active** companies (case- and accent-insensitive); duplicates MUST be rejected with an es-CR message.
- **FR-004**: When an administrator creates a Solicitante account, the system MUST require at least one company to be specified, and MUST block creation otherwise with an es-CR message.
- **FR-005**: Administrators MUST be able to add one or more additional companies to an existing applicant after account creation.
- **FR-006**: Administrators MUST be able to edit/correct an existing company's name (subject to FR-003).
- **FR-007**: Administrators MUST be able to archive a company (a reversible/soft action) and to unarchive it. Archived companies MUST be excluded from applicants' new-submission dropdowns while remaining intact and retrievable.
- **FR-008**: The system MUST prevent archiving an applicant's last remaining active company, so every applicant always retains at least one active company.
- **FR-009**: The bulk applicant-import CSV MUST gain a required company-name column titled "Nombre de la empresa", appended after the existing columns (resulting header order: Grupo, Proceso, Fondo, Nombre, Apellido 1, Apellido 2, Email, Teléfono, Cédula, Código de usuario, Nombre de la empresa). For each successfully created applicant the system MUST create their first company from this cell, applying the same required-cell, trim, and per-applicant duplicate rules; the downloadable template MUST reflect the new column.
- **FR-010**: The system MUST record an administrative audit event for each company create, rename, archive, and unarchive action.

**Submission creation (applicant)**

- **FR-011**: The company field on the new-application flow MUST be mandatory and selectable only from the applicant's active companies; free-text company entry MUST NOT be accepted anywhere in the flow.
- **FR-012**: When an applicant has exactly one active company, the dropdown MUST remain visible, MUST be pre-selected to that company, MUST offer no other value, and validation MUST pass automatically.
- **FR-013**: When an applicant has two or more active companies, the dropdown MUST list all of them with no default selection and a "Seleccione una empresa…" placeholder, and the system MUST block application creation until one is chosen.
- **FR-014**: When an applicant has no active companies, the system MUST prevent starting a submission and MUST display a clear es-CR message directing them to contact an administrator.
- **FR-015**: The selected company MUST be changeable by the applicant while the application is in `Draft` and MUST become immutable once the application is submitted.
- **FR-016**: The system MUST store, on each application, a name snapshot copied from the selected company at creation and re-copied whenever the selection changes while in `Draft`; the snapshot MUST be frozen at submission. Later edits to a company's name MUST NOT alter the snapshot on already-created applications.

**Data integrity & security**

- **FR-017**: Every application MUST store both a reference to the selected company and the company-name snapshot (FR-016).
- **FR-018**: The system MUST validate, on the server, that a selected company belongs to the applicant creating/editing the application and is active; otherwise the request MUST be rejected without disclosing other applicants' companies.
- **FR-019**: The system MUST reject forged or manipulated requests that attempt to bypass the dropdown (e.g., submitting an arbitrary company reference or another applicant's company), independent of any client-side restriction.
- **FR-020**: At submission, the selected company MUST be active; if the previously-selected company was archived while the application was in `Draft`, the applicant MUST be required to re-select an active company before submitting.

### Key Entities *(include if feature involves data)*

- **Company**: An applicant's company as managed by administrators. Attributes: name (required, ≤200 chars), owning applicant (required), active/archived lifecycle state, creation/update timestamps. An applicant owns one or more companies; companies are never shared across applicants.
- **Application (submission)** *(existing, extended)*: Gains a reference to the selected Company and retains a company-name snapshot frozen per FR-016. The reference is optional for records created before this feature; new records created through this flow always carry a valid, owned company reference.
- **Applicant** *(existing)*: Now the owner of one or more Companies. No change to its other attributes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of new applications created by applicants carry a company selected from that applicant's own active companies; 0% are created via free-text company entry.
- **SC-002**: An applicant with exactly one active company can create an application without performing any company-selection step.
- **SC-003**: An applicant with multiple active companies cannot create an application until a company is chosen (creation attempts without a selection are blocked 100% of the time).
- **SC-004**: Every server-side request carrying a company reference not owned by, or not active for, the requesting applicant is rejected, and no such request results in a persisted application.
- **SC-005**: After an administrator renames a company, 100% of previously-created applications still display the prior name and all newly-created applications display the new name.
- **SC-006**: Administrators can add, rename, archive, and unarchive companies, and every attempt to archive an applicant's last active company is blocked.
- **SC-007**: Every applicant created through bulk import with a non-empty company cell ends up with exactly one active company matching that cell and can immediately select it on a submission.
- **SC-008**: Applicants have no available means to create, edit, archive, or delete companies, and direct attempts are refused.

## Assumptions

- **Greenfield rollout**: No backfill or migration of existing applicants or applications is performed. Pre-existing applications keep their stored free-text name with no company reference; pre-existing applicants start with zero companies until an administrator adds them (and therefore cannot create new submissions until then — this is accepted).
- **Scope is the Applicant (Solicitante) role only**: The change affects applicant submission creation and admin management of applicant companies. Reviewer/Admin/SupplierAdmin roles and their application surfaces are unaffected beyond continuing to display the stored company-name snapshot.
- **Existing seams are reused**: The submission flow, admin user create/edit/batch flows, searchable-dropdown enhancement, administrative audit log, and es-CR localization conventions already exist and are reused rather than rebuilt. No new third-party/managed dependencies are required.
- **"At time of submission" preservation is satisfied by a per-application name snapshot** rather than company versioning; while a company is shared across many of an applicant's applications, each application independently retains the name captured when it was created/last changed in `Draft`.
- **The company is a foundational attribute of an application** (like its group/fund anchor): it is chosen when the application starts, may be changed while `Draft`, and is frozen on submission.

## Out of Scope

- Company attributes beyond a name (e.g., address, tax/legal identifier, contact details).
- Applicant self-service management of their own companies.
- Backfill/migration of existing applicants or existing applications to the new company model.
- Changes to reviewer/admin application surfaces beyond continuing to display the stored company-name snapshot.
- Linking Company to the supplier catalog or to the applicant's legal identification.

## Open Questions

- Exact administrative UI placement for the per-applicant company list (inline on the user edit/detail page vs. a dedicated company sub-surface) — deferred to planning as a HOW decision; does not affect requirements.
