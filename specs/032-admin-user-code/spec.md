# Feature Specification: Admin-only user provisioning + unique applicant User Code

**Feature Branch**: `032-admin-user-code`
**Created**: 2026-06-11
**Status**: Draft
**Input**: User description: "Remove free public sign-up via /Account/Register; the only way to add a user is through /Admin/Users/Create. When the user is 'solicitante', the system must ask for a unique, admin-assigned free-text 'User Code' (max 50 chars). The code must be filterable around the system — the search-by-name/email field must also search by personal identification and by User Code, and every other screen with that filtering must be updated to support the wider range of values."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Only administrators can create accounts (Priority: P1)

The platform no longer offers public self-registration. Account creation happens exclusively through the administrator's user-management area. A member of the public who reaches the old registration URL — by bookmark, stale link, or guess — cannot create an account and is not shown a sign-up form.

**Why this priority**: This is the core security/governance change. Until self-registration is closed, the rest of the feature (admin-assigned codes) can be bypassed by anyone creating their own account. It must land first and is independently valuable on its own.

**Independent Test**: Visit the public registration URL while signed out and confirm no account can be created (a 404 is returned); confirm no "create an account / register" affordance is reachable from the landing page or the login page; confirm an administrator can still create a user through the admin area.

**Acceptance Scenarios**:

1. **Given** a signed-out visitor, **When** they navigate to the public registration URL, **Then** the system returns a 404 Not Found and presents no registration form.
2. **Given** a signed-out visitor on the public landing page, **When** they look for a way to sign up, **Then** no registration link or call-to-action exists; the primary call-to-action leads to the sign-in page.
3. **Given** a signed-out visitor on the sign-in page, **When** they look for a "create an account" link, **Then** none is present.
4. **Given** an administrator, **When** they use the admin user-creation flow, **Then** they can still create an account of any role exactly as before, including the companion applicant record for applicants.

---

### User Story 2 - Administrator assigns a unique User Code to each applicant (Priority: P1)

When an administrator creates or edits a user whose role is "Solicitante" (Applicant), the form asks for a **User Code** — a free-text identifier the administrator assigns. The code is required for applicants, capped at 50 characters, and must be unique across all applicants. The field is not shown or required for non-applicant roles. The applicant can later see their own code (read-only) on their profile.

**Why this priority**: The User Code is the new identifier the organization wants to track and search applicants by. It is the data foundation that Story 3 (search) depends on. It is independently valuable because, even before search is widened, having a governed unique code on each applicant is useful.

**Independent Test**: As an administrator, create a Solicitante: leaving the User Code blank is rejected; entering a code already used by another applicant is rejected; entering a unique code ≤50 chars succeeds. Switch the role selector to a non-applicant role and confirm the User Code field is not shown/required. Sign in as that applicant and confirm the code appears read-only on the profile.

**Acceptance Scenarios**:

1. **Given** the admin create form with role = Solicitante, **When** the administrator submits with an empty User Code, **Then** the save is blocked with an es-CR validation message and no user is created.
2. **Given** the admin create form with role = Solicitante, **When** the administrator enters a User Code already assigned to another applicant, **Then** the save is blocked with an es-CR "code already in use" validation message.
3. **Given** the admin create form with role = Solicitante, **When** the administrator enters a unique, non-blank code of 50 characters or fewer, **Then** the applicant is created with that code.
4. **Given** the admin create or edit form, **When** the administrator selects a non-applicant role (Revisor, Administrador, Administrador de proveedores), **Then** the User Code field is hidden and is neither requested nor validated.
5. **Given** an existing applicant with a User Code, **When** that applicant views their own profile, **Then** the User Code is displayed read-only and cannot be edited by the applicant.
6. **Given** an administrator editing an existing applicant, **When** they change the User Code to a new unique value of ≤50 chars, **Then** the change is saved; **When** they change it to a value used by another applicant, **Then** the save is blocked.

---

### User Story 3 - Search applicants by User Code and identification everywhere (Priority: P2)

Every screen that already lets a user search people by name (and sometimes email) is widened so the same single search box also matches the applicant's **personal identification** (LegalId) and their **User Code**. A reviewer or administrator can paste a User Code, a cédula, an email, or a name fragment into the existing search box and find the matching applicant(s) — on the admin users list, the reviewer queue, and the admin reports (including the applicants CSV export).

**Why this priority**: This makes the new code operationally useful across day-to-day workflows. It depends on Story 2 producing codes, so it follows. It is independently testable per surface.

**Independent Test**: On each widened surface, seed an applicant with a known name, email, identification, and User Code, then search by each of those four values in turn and confirm the applicant appears; confirm a non-matching term excludes them.

**Acceptance Scenarios**:

1. **Given** the admin users list, **When** an administrator searches by a full or partial User Code, **Then** matching applicants are returned; searching by identification, email, or name also returns matches (the search box now spans all four).
2. **Given** the reviewer queue (and its incremental row-refresh view), **When** a reviewer searches by User Code or email, **Then** matching applications are returned, in addition to the existing name and identification matching.
3. **Given** each admin report that exposes a people search (Applications, Applicants, Aging Applications) and the applicants CSV export, **When** the operator searches by User Code, **Then** matching rows are returned, in addition to the existing full-name, identification, and email matching.
4. **Given** any widened surface, **When** the search box is left empty, **Then** results are unchanged from today's behavior (the full, paged list).
5. **Given** any widened surface, **When** the operator searches with mixed case or accents, **Then** matching is case-insensitive and accent-insensitive, consistent with that surface's existing matching behavior.

---

### Edge Cases

- **Legacy applicants without a code**: Applicants created before this feature (seed accounts, previously self-registered users) have no User Code. They remain valid, are not force-assigned a code, and simply do not appear in User-Code searches until an administrator assigns one. Uniqueness must permit any number of applicants having no code.
- **Role changed away from Solicitante on edit**: The User Code requirement is no longer enforced; any previously assigned code value is retained (not cleared, not re-validated for presence).
- **Uniqueness scope vs. absent codes**: Uniqueness is enforced only among assigned (non-empty) codes; absence of a code never collides with another absence.
- **Whitespace-only code**: A code consisting only of whitespace is treated as blank and rejected for applicants.
- **Removed registration endpoint reached by POST**: Submitting the old registration form (e.g., a replayed POST) also yields 404 and creates nothing.
- **Search term that matches across fields**: A term that happens to match one applicant by name and another by code returns both; results are de-duplicated per applicant where a surface lists applicants once.

## Requirements *(mandatory)*

### Functional Requirements

**A. Close public self-registration**

- **FR-001**: The system MUST NOT expose any anonymous, public account self-registration flow.
- **FR-002**: Requests (GET or POST) to the former public registration URL MUST return 404 Not Found and MUST NOT create any account or applicant record.
- **FR-003**: The system MUST remove every user-facing link or call-to-action that leads to self-registration, specifically from the public landing page and the sign-in page; the landing page's primary call-to-action MUST instead lead to the sign-in page.
- **FR-004**: The administrator user-creation flow MUST remain the sole means of creating accounts and MUST continue to create the companion applicant record when the role is Solicitante.
- **FR-005**: Sign-in, forgot-password, password-reset, and forced-password-change flows MUST be unaffected.

**B. Unique applicant User Code**

- **FR-006**: The system MUST provide a User Code attribute on the applicant, distinct and independent from the existing personal code field (`CodigoPersonal`), which MUST be left unchanged.
- **FR-007**: The User Code MUST be free text of at most 50 characters and MUST be optional at the storage level (absent for applicants without one).
- **FR-008**: When the role is Solicitante, the administrator create and edit forms MUST request the User Code and MUST reject a blank (or whitespace-only) value with an es-CR validation message, blocking the save.
- **FR-009**: The User Code MUST be unique across applicants that have one; an attempt to assign a code already used by another applicant MUST be rejected with an es-CR validation message, blocking the save. Multiple applicants without a code MUST NOT be considered a conflict.
- **FR-010**: For non-applicant roles (Revisor, Administrador, Administrador de proveedores), the User Code field MUST NOT be shown, requested, or validated; it MUST show/hide in response to the role selector consistently with the existing identification field's behavior.
- **FR-011**: The applicant MUST be able to view their own User Code, read-only, on their profile page, and MUST NOT be able to edit it.

**C. Widen search to include identification and User Code**

- **FR-012**: On the administrator users list, the existing single search box MUST additionally match the applicant's personal identification and User Code (in addition to name and email).
- **FR-013**: On the reviewer queue and its incremental row-refresh view, the existing single search box MUST additionally match the applicant's User Code and email (in addition to name and identification).
- **FR-014**: On the administrator reports that expose a people search (Applications, Applicants, Aging Applications) and on the applicants CSV export, the existing search MUST additionally match the applicant's User Code (in addition to full name, identification, and email).
- **FR-015**: All widened matching MUST be case-insensitive and accent-insensitive and MUST support partial matches, consistent with each surface's existing matching behavior; an empty search term MUST preserve today's behavior.
- **FR-016**: Where it adds operational value and fits the layout, the User Code value SHOULD be surfaced as a column/field on the administrator users list and in the applicants report and CSV export; added columns MUST be minimal and labelled in es-CR.

**D. Localization & conventions**

- **FR-017**: All new or changed UI copy, labels, and validation messages MUST be in es-CR.
- **FR-018**: No new content delivery network dependencies may be introduced; only vendored assets may be used.

### Key Entities *(include if feature involves data)*

- **Applicant**: The person applying for funding, already linked one-to-one to a platform account and already carrying personal identification (type + LegalId). This feature adds the **User Code** attribute to the applicant: an optional, ≤50-character, free-text identifier, unique among applicants that have one, assigned and edited only by administrators, required when the account's role is Solicitante.
- **Account (platform user)**: The sign-in identity with a role. Roles relevant here: Solicitante (Applicant), Revisor (Reviewer), Administrador (Admin), Administrador de proveedores (SupplierAdmin). Only Solicitante accounts carry a User Code (via the linked Applicant). The pre-existing `CodigoPersonal` attribute on the account is unrelated and untouched.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of attempts to self-register (reach the old registration URL while signed out) result in no account being created and a 404 response; zero registration links remain anywhere in the signed-out UI.
- **SC-002**: An administrator cannot save a Solicitante without a non-blank, ≤50-character, unique User Code; 100% of blank or duplicate submissions are rejected with an es-CR message and create/modify no data.
- **SC-003**: On each of the three widened surface groups (admin users list, reviewer queue + row refresh, admin reports + applicants CSV), searching a known applicant by any one of name, email, identification, or User Code returns that applicant; a clearly non-matching term excludes them.
- **SC-004**: Non-applicant user creation/edit is unchanged: the User Code field never appears for Revisor, Administrador, or Administrador de proveedores, and those flows succeed exactly as before.
- **SC-005**: An applicant can see their own User Code read-only on their profile and has no control to change it.
- **SC-006**: The corresponding/filtered end-to-end tests for the touched areas (removed-registration 404, admin user create/edit with User Code, and the widened search on each surface) pass green.

## Assumptions

- The User Code lives on the **applicant** record (alongside the existing identification), because it is applicant-scoped; non-applicant accounts do not have one. This was chosen over placing it on the account, since identification — the field it sits beside in the UI and in search — already lives on the applicant.
- Suggested es-CR label: **"Código de usuario"**. The administrator assigns the value with no system-imposed format beyond the 50-character cap and uniqueness; it is free text.
- **Uniqueness** is enforced among assigned codes only; many applicants may have no code. Comparison for uniqueness is exact (the stored value), independent of the case/accent-insensitive *search* matching.
- Existing applicants are **not** backfilled with codes, and no bulk-import path is provided; administrators assign codes going forward.
- Supplier search is explicitly **out of scope** — suppliers are a separate entity with their own code/identification and are unaffected.
- The reviewer queue and admin reports already search the applicant's identification (LegalId); this feature adds the User Code (and, where missing, email) to those same boxes rather than introducing new search controls.
- "Personal identification" refers to the applicant's existing identification value (LegalId), captured with its identification type; this feature does not change how identification is captured or validated.
- The administrator create/edit flow already captures identification, groups, and fund anchors; this feature adds only the User Code to that flow and does not alter those existing behaviors.

## Out of Scope

- Any change to the existing `CodigoPersonal` account attribute.
- Any change to supplier search or the supplier entity.
- Backfilling User Codes for existing applicants, or any bulk-import of codes.
- Any invite-by-email or self-service onboarding replacement for the removed registration.
- Format/pattern validation of the User Code beyond length and uniqueness.

## Dependencies

- Existing account/role wiring and the administrator user-management area (create/edit/list).
- Existing applicant identification capture (identification type + LegalId).
- Existing group membership and fund-anchor selection on the administrator create form (unchanged).
- Existing search controls on the administrator users list, reviewer queue (and row-refresh), and admin reports + applicants CSV export.
