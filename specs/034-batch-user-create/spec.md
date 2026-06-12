# Feature Specification: Batch user creation (bulk applicant provisioning via CSV)

**Feature Branch**: `feature/batch-user-create`
**Created**: 2026-06-12
**Status**: Draft
**Input**: User description: "Batch user creation — bulk applicant provisioning via CSV upload. An administrator uploads the org's existing intake spreadsheet (as CSV) and provisions up to 200 Solicitante accounts in one pass, each receiving the standard set-password invitation (spec 033). Rows are validated independently and the admin gets a clear succeeded/errored report. Builds on spec 032 (admin-only create + unique applicant UserCode) and spec 033 (emailed set-password invitation — no passwords are set or relayed by the admin)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin bulk-creates applicants from a CSV (Priority: P1)

An administrator uploads a CSV that matches the intake template. Each valid row becomes a **Solicitante** account, with its linked applicant record carrying the personal identification (cédula física) and the unique User Code, joined to the Group named in that row. Each newly created user receives the standard es-CR set-password invitation email (spec 033). The administrator never chooses, sets, or relays any password.

**Why this priority**: This is the core value — turning a manual, one-at-a-time provisioning chore into a single upload for an entire funding cohort. Without it the feature delivers nothing. It is independently valuable: even with no other refinement, an all-valid file provisions a whole cohort at once.

**Independent Test**: As an administrator, upload a small CSV in which every row is valid; confirm one Solicitante account exists per row, each with its User Code and membership in the named Group, and that one set-password invitation email was sent per created user.

**Acceptance Scenarios**:

1. **Given** an administrator on the batch-creation page, **When** they upload a CSV of N (≤200) all-valid rows, **Then** exactly N Solicitante accounts are created, each linked to an applicant record with its cédula and User Code and a membership in the row's Group, and N set-password invitation emails are sent.
2. **Given** a created batch user who has not yet onboarded, **When** they open their invitation link within 72 hours and set a password, **Then** they can sign in — the administrator never handled a password (consistent with spec 033).
3. **Given** an administrator who is not signed in or lacks the administrator role, **When** they attempt to reach the batch-creation page, **Then** access is denied exactly as for the rest of the admin user-management area.

---

### User Story 2 - Per-row validation with a succeeded/errored report (Priority: P1)

Invalid rows never block valid ones. The system validates each data row independently; valid rows are created and invalid rows are skipped. After processing, the administrator sees a report that partitions the rows into **succeeded** and **errored**, with each errored row identified and given a plain es-CR reason. Processing is never all-or-nothing across the file.

**Why this priority**: Real intake spreadsheets contain mistakes (blank cells, duplicate codes, malformed cédulas). Without per-row resilience plus a clear report, a single bad cell would either block the whole cohort or silently produce wrong data. It depends on US1 producing creations to report on, and is independently testable.

**Independent Test**: Upload a CSV that mixes valid rows with a blank-email row, a duplicate-User-Code row, and a structurally-invalid-cédula row; confirm the valid rows are created and each bad row appears in the report's errored list with a specific reason, and that the succeeded + errored counts equal the number of data rows.

**Acceptance Scenarios**:

1. **Given** a CSV containing both valid and invalid rows, **When** it is processed, **Then** every valid row is created and every invalid row is skipped; no valid row is rolled back because another row failed.
2. **Given** processing has finished, **When** the report is shown, **Then** it lists each errored row with its row number, a key identifying field (e.g., email or User Code), and an es-CR reason, and the succeeded count plus the errored count equals the data-row count.
3. **Given** a row whose creation fails partway (e.g., a uniqueness collision detected at persistence), **When** the report is shown, **Then** that row appears as errored and nothing partial is left behind for it, while already-created rows remain created.

---

### User Story 3 - Group → Proceso → Fondo chain integrity (Priority: P2)

Each row names a **Grupo**, a **Proceso**, and a **Fondo**. These must name a coherent chain: the Group must sit under that Process, which must sit under that Fund (the spec-029 Fund → Process → Group hierarchy). Only the **Group** is persisted (as the applicant's membership); **Proceso** and **Fondo** are validation guards that confirm the administrator placed the person in the intended part of the hierarchy. A row whose chain does not reconcile is skipped and reported.

**Why this priority**: It protects against silently filing an applicant under a Group that belongs to a different Process or Fund than the operator intended. It depends on US1/US2 (creation + reporting) and is independently testable per row.

**Independent Test**: Upload a row whose Grupo exists but belongs to a different Proceso/Fondo than the row names; confirm it is skipped with a chain-mismatch reason while coherent rows in the same file succeed.

**Acceptance Scenarios**:

1. **Given** a row whose Grupo, Proceso, and Fondo names form a valid chain, **When** it is processed, **Then** the applicant is created and made a member of that Group.
2. **Given** a row whose Grupo name is valid but does not belong to the named Proceso, or whose Proceso does not belong to the named Fondo, **When** it is processed, **Then** the row is skipped with an es-CR chain-mismatch reason and no account is created.
3. **Given** a row in which any of Grupo, Proceso, or Fondo names a value that does not exist or is ambiguous (matches more than one), **When** it is processed, **Then** the row is skipped with an es-CR reason.

---

### Edge Cases

- **Optional cells empty**: `Apellido 2` and `Teléfono` may be blank — the row is still valid (last name is just `Apellido 1`; phone is stored empty).
- **Phone normalization**: a `Teléfono` value with a leading `506` country prefix has the prefix stripped; if the cell contains more than one number, only the first is kept.
- **In-file duplicates**: when the same Email, Cédula, or User Code appears in more than one row of the file, the **first** occurrence is created and every later duplicate is errored.
- **Invalid cédula**: a `Cédula` that is not structurally a valid cédula física is an errored row.
- **Group exists but wrong chain**: a valid Group name placed under the wrong Proceso/Fondo is an errored row (not a silent reassignment).
- **Partial-then-fail**: when some rows have already been created and a later row fails, the already-created rows remain — the file is not transactional as a whole.
- **Invitation email not delivered**: a failure to *send* the invitation email (e.g., recipient filtered by a non-Production allowlist) does **not** fail the row; the account is still created and is recoverable through the existing per-user invitation resend (spec 033).
- **File-level rejection**: a file that is not CSV / not parseable, whose header columns are missing or do not match the template, that has zero data rows, or that exceeds 200 data rows is rejected as a whole with a single es-CR message and creates nothing.

## Requirements *(mandatory)*

### Functional Requirements

**A. Upload & file-level validation**

- **FR-001**: The system MUST provide an administrator-only batch-creation page within the admin user-management area that accepts a single CSV upload and processes it within the request (synchronously). The 200-row cap (FR-003) keeps synchronous processing acceptable; no background job is required.
- **FR-002**: The system MUST provide a downloadable CSV template containing exactly the expected header columns in the expected order.
- **FR-003**: The system MUST reject the entire upload — processing no rows and creating nothing — with a single es-CR message when any of the following holds: the file is not a CSV or cannot be parsed; the header columns are missing or do not match the template; the file contains zero data rows; or the file contains more than **200** data rows.

**B. Row mapping & normalization** *(role is fixed to Solicitante for every row; there is no role column)*

- **FR-004**: For each data row the system MUST map columns as follows: `Nombre` → first name; `Apellido 1` + `Apellido 2` → a single last name (with `Apellido 2` optional and joined to `Apellido 1` by a single space); `Email` → account email; `Cédula` → personal identification value; `Código de usuario` → User Code.
- **FR-005**: `Teléfono` MUST be treated as optional and normalized before storage: a leading `506` country-code prefix MUST be stripped, and when the cell contains more than one number, only the first MUST be kept.
- **FR-006**: Every row's identification type MUST be **cédula física**; the `Cédula` value MUST be a valid cédula física per the existing identification rules (spec 026), otherwise the row MUST be skipped with an es-CR reason.

**C. Row-level validation** *(skip-and-report; never all-or-nothing)*

- **FR-007**: The system MUST skip a row with an es-CR reason when a required cell is missing (`Nombre`, `Apellido 1`, `Email`, `Cédula`, `Código de usuario`, `Grupo`, `Proceso`, `Fondo`) or when `Email`, `Cédula`, or `Código de usuario` fails its format/length rule (User Code ≤ 50 characters; email and cédula per spec 026).
- **FR-008**: The system MUST skip a row with an es-CR reason when its `Email`, `Cédula`, or `Código de usuario` already exists in the system, **or** is duplicated by an earlier row in the same file. Among in-file duplicates the first occurrence is created and later occurrences are errored.
- **FR-009**: The system MUST match `Grupo`, `Proceso`, and `Fondo` **by name** and MUST skip a row with an es-CR reason when any name is unknown or ambiguous, or when the named Group does not sit under the named Process which sits under the named Fund (the spec-029 Fund → Process → Group chain).

**D. Creation & invitation**

- **FR-010**: For each valid row the system MUST create a Solicitante account and its linked applicant record (personal identification, identification type, User Code) and MUST make the account a member of the resolved Group, reusing the existing single-create rules and uniqueness guards (spec 016 required-membership path, spec 032 uniqueness).
- **FR-011**: For each created user the system MUST issue the spec-033 single-use, 72-hour set-password invitation and send the es-CR invitation email; the batch MUST NOT collect or set any password. A failure to **send** the email MUST NOT fail the row; recovery is the existing per-user invitation resend.

**E. Report**

- **FR-012**: After processing, the system MUST present an es-CR report that partitions the rows into **succeeded** and **errored**, identifying each errored row by its row number and a key field with a specific es-CR reason. The report does not offer a file download and does not display invitation links (v1).

**F. Conventions**

- **FR-013**: All new or changed UI copy, the CSV template header labels, and all validation/report messages MUST be in es-CR.
- **FR-014**: No new managed (NuGet) dependencies may be introduced; CSV parsing MUST use in-house or already-vendored code.

### Key Entities *(include if feature involves data)*

- **Applicant**: the person applying for funding, one-to-one with a platform account. Carries personal identification (`LegalId` + identification type, here cédula física), first name, last name, email, phone, and the unique **User Code** (spec 032). The applicant itself has **no** Group or Fund attribute.
- **Account (platform user)**: the sign-in identity with a role. For the batch the role is fixed to **Solicitante**. The account is onboarded via the spec-033 invitation and has no usable password until the user sets one.
- **Group / Process / Fund**: the spec-029 hierarchy Fund → Process → Group. The batch resolves `Grupo` to an existing Group and persists it as the applicant's membership (the spec-016 required-membership path); `Proceso` and `Fondo` are validation-only guards that confirm the chain.
- **Batch upload (transient)**: the uploaded CSV and its in-memory rows. Not persisted as an entity; it yields the per-row outcomes shown in the report.
- **Row outcome (transient)**: per data row, either *succeeded* (account created) or *errored* (skipped) with a row number, a key identifying field, and an es-CR reason.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Uploading an all-valid CSV of N ≤ 200 rows creates exactly N invited Solicitante accounts, each with its User Code and membership in the named Group, and exactly N set-password invitations are sent.
- **SC-002**: Uploading a CSV that mixes valid and invalid rows creates every valid row and skips every invalid row; in the report the succeeded count plus the errored count equals the number of data rows, and every errored row carries a specific reason.
- **SC-003**: A row whose Grupo → Proceso → Fondo chain does not reconcile is never created (0% leakage of chain-mismatched rows into created accounts).
- **SC-004**: 100% of rows whose Email, Cédula, or User Code duplicates an existing record or an earlier row in the same file are rejected and create nothing.
- **SC-005**: A file that is non-CSV, header-mismatched, empty of data rows, or larger than 200 data rows creates nothing and shows exactly one es-CR rejection message.
- **SC-006**: An administrator can provision a 200-row cohort in a single upload action (one file, one submit) rather than 200 separate create operations.

## Assumptions

- **Reused single-create semantics**: batch row creation reuses the existing admin single-create behavior and guards (spec 032 admin-only create + User Code uniqueness; spec 016 required Group membership for applicants), so a batch-created applicant is indistinguishable from one created through the single form.
- **Onboarding via invitation only**: per spec 033, no password is collected in the batch; each created user onboards through the emailed set-password link. A dropped email is recovered with the existing per-user resend (no batch-level resend in v1).
- **Identification type fixed**: all rows are individuals, so identification type is cédula física for the whole batch; there is no identification-type column.
- **CSV, not native spreadsheet**: the operator exports the intake spreadsheet to CSV before uploading; native `.xlsx` parsing is out of scope (avoids a new dependency).
- **Name-based hierarchy resolution**: Grupo/Proceso/Fondo are resolved by the human-readable names present in the spreadsheet; ambiguous names (more than one match) are treated as errors rather than guessed.
- **In-file duplicate rule**: when a value is duplicated within the file, the first occurrence wins and later occurrences are errored (chosen for determinism).
- **Synchronous processing**: the 200-row cap makes in-request processing acceptable; no background worker, progress streaming, or scheduling is introduced.
- **es-CR + vendored assets**: consistent with the rest of the platform (default culture es-CR; no CDN; no new managed dependencies).

## Dependencies

- **Spec 032** — admin-only user creation and the unique applicant User Code.
- **Spec 033** — emailed single-use set-password invitation onboarding.
- **Spec 016** — Groups and the required Group membership for applicant accounts.
- **Spec 029** — the Fund → Process → Group hierarchy used for chain validation.
- **Spec 026** — Costa Rican identification (cédula física) and phone validation/normalization rules.
- **Spec 021** — the email outbox used to send invitation emails.

## Out of Scope

- Native `.xlsx` (or other spreadsheet) upload — CSV only.
- A downloadable results report, or invitation links shown in the report.
- Batch **edit/update** or batch **delete/disable** of users (creation only).
- Non-applicant roles in the batch (Revisor, Administrador, Administrador de proveedores).
- Background/asynchronous processing, progress streaming, and scheduling.
- Persisting `Proceso`/`Fondo` (or any new attribute) on the applicant — they are validation-only.
