# Feature Specification: User invitation / set-password onboarding email

**Feature Branch**: `033-user-invite-email`
**Created**: 2026-06-12
**Status**: Draft
**Input**: User description: "When a user is created via /Admin/Users/Create, send them a welcome email with a set-password link instead of the admin relaying a temporary password. Invite link replaces the temp password; all roles; 72-hour link; admins can resend."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - New user onboards via an emailed invitation (Priority: P1)

An administrator creates an account without choosing a password for the person. The new user receives an email inviting them to set their own password through a secure, time-limited link. They click it, choose a password, and sign in — the administrator never has to generate or relay a temporary password.

**Why this priority**: This is the core value: it removes the manual, insecure temp-password hand-off that spec 032's admin-only creation left in place. Without it, the feature delivers nothing.

**Independent Test**: As an administrator, create a user without entering a password; confirm an invitation email is sent to that address with a set-password link; follow the link, set a password, and sign in successfully.

**Acceptance Scenarios**:

1. **Given** the admin create form, **When** the administrator submits a valid new user **without** entering any password, **Then** the account is created with no usable password and an es-CR invitation email containing a set-password link is sent to the new user's email.
2. **Given** a freshly created user who has not yet set a password, **When** they open the invitation link within 72 hours and choose a valid password, **Then** their password is set and they can sign in.
3. **Given** a user created this way, **When** they attempt to sign in **before** using the invitation link, **Then** they cannot sign in (there is no usable password yet).
4. **Given** any of the four roles (Solicitante, Revisor, Administrador, Administrador de proveedores), **When** the account is created, **Then** the same invitation flow applies.
5. **Given** a user who completes the set-password step, **When** they sign in, **Then** they are **not** forced through a separate change-password step (they already chose their own password).

---

### User Story 2 - Administrator resends an invitation (Priority: P2)

An invitation can expire (72 hours) or never arrive. An administrator can resend the invitation for a user from the admin area, which issues a fresh link and invalidates the previous one.

**Why this priority**: Onboarding can stall (expired or undelivered link); without a resend, the only recovery would be deleting and recreating the account. It depends on Story 1's invitation existing.

**Independent Test**: Create a user, let/treat the first link as stale, resend the invitation, and confirm a new email/link is produced and the previous link no longer works.

**Acceptance Scenarios**:

1. **Given** an administrator on the admin user list or edit screen for a not-yet-onboarded user, **When** they resend the invitation, **Then** a fresh 72-hour link is issued, a new invitation email is sent, and any previously issued unused link for that user stops working.
2. **Given** a user who resends were performed for, **When** they open the **newest** link within 72 hours, **Then** they can set their password.

---

### User Story 3 - Onboarding works even when email can't be delivered (Priority: P2)

In some environments the invitation email is filtered or undeliverable (for example, a non-Production recipient allowlist drops external addresses). The administrator can still complete onboarding using a copyable invitation link shown on the post-creation confirmation.

**Why this priority**: Because the invitation link is now the *only* way in (no temp-password fallback), a dropped email would otherwise block onboarding entirely. This makes the flow robust in non-Production and during mail outages.

**Independent Test**: In an environment where the invitation email is filtered, create a user and confirm the confirmation screen shows a copyable invitation link that, when followed, lets the user set their password.

**Acceptance Scenarios**:

1. **Given** an administrator who has just created a user (or resent an invitation), **When** the confirmation is shown, **Then** it states the invitation was sent to the user's email **and** displays a copyable invitation link.
2. **Given** an environment where the email to that recipient is filtered/undeliverable, **When** the administrator copies and delivers the link out-of-band, **Then** the user can still set their password and sign in.

---

### Edge Cases

- **Expired or already-used link**: opening an invitation link after 72 hours, after it was already used, or with a tampered/invalid token shows an es-CR message ("Enlace inválido o expirado. Solicite uno nuevo a su administrador.") and changes no password. Recovery is an administrator resend.
- **Resend supersedes**: issuing a new invitation invalidates the prior unused invitation token for that user, so only the most recent link is valid.
- **Already-onboarded user**: a user who has already set their password has a dead invitation link; password recovery for them is the existing user-initiated forgot-password flow, not the invitation.
- **Re-onboarding via the same single-use link**: the link cannot be reused after the password is set.
- **Development test bootstrap**: the Development-only seam that creates users with a password directly is unaffected and bypasses the invitation path (so the test suite can still log in as seeded users).

## Requirements *(mandatory)*

### Functional Requirements

**A. Invitation at creation**

- **FR-001**: The administrator create flow MUST NOT collect or require an initial password; the account MUST be created with no usable password (the user cannot sign in until they set one through the invitation).
- **FR-002**: On successful creation, the system MUST issue a **single-use** invitation token that expires **72 hours** after issuance and MUST send an es-CR invitation email to the new user's email address containing a set-password link bound to that token.
- **FR-003**: The invitation flow MUST apply to **all** administrator-created roles (Solicitante, Revisor, Administrador, Administrador de proveedores).
- **FR-004**: Following the invitation link MUST present the set-password page; submitting a valid password MUST set the account's password and allow the user to sign in.
- **FR-005**: Users onboarded through the invitation MUST NOT be forced through a separate "must change password" step on first sign-in (they have already chosen their own password).

**B. Resend**

- **FR-006**: An administrator MUST be able to resend the invitation for a user from the admin user list and/or edit screen.
- **FR-007**: Resending MUST issue a fresh 72-hour single-use link, **invalidate any prior unused invitation token** for that user, and send a new invitation email.

**C. Delivery resilience (admin-visible link)**

- **FR-008**: After creating a user — and after a resend — the administrator MUST see a confirmation indicating the invitation was sent to the user's email, **and** a copyable invitation link, so onboarding is possible even where the email is filtered or undeliverable.

**D. Link lifecycle & validation**

- **FR-009**: The invitation link MUST be single-use: once the password has been set, the token MUST be consumed and the link MUST stop working.
- **FR-010**: An expired (>72h), already-used, or otherwise invalid invitation link MUST show an es-CR rejection message and MUST NOT change any password.

**E. Email content & localization**

- **FR-011**: The invitation email MUST be in es-CR and MUST include: a greeting, a brief line about the platform / that an account was created for them, the set-password call-to-action, the link's expiry, and the Programa Semilla sender identity. It MUST reuse the existing email sender identity/configuration and the established transactional-email branding/layout.
- **FR-012**: All new or changed UI copy and validation messages MUST be in es-CR.

**F. Conventions**

- **FR-013**: No new content-delivery-network dependencies may be introduced; only vendored assets may be used.

### Key Entities *(include if feature involves data)*

- **Invitation token**: a single-use, time-limited credential bound to one user account, issued when the account is created (or when an administrator resends), valid for 72 hours, consumed when the user sets their password. Conceptually the same kind of artifact as the existing password-reset token, but with a longer lifetime and an onboarding purpose. Issuing a new one for a user invalidates that user's prior unused one.
- **Account (platform user)**: created by an administrator (spec 032). After this feature, a newly created account begins with **no usable password** and is activated only when the invited user sets one. Roles are unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can create a user **without typing a password**; the new user receives an email containing a set-password link; following it within 72 hours lets them set a password and sign in.
- **SC-002**: The invitation link works **exactly once** and only within 72 hours; an expired or already-used link is rejected with an es-CR message and changes no password.
- **SC-003**: An administrator can resend a fresh invitation; the previously issued link stops working and the newest link succeeds.
- **SC-004**: All four roles onboard through the same invitation flow.
- **SC-005**: When the invitation email is filtered or undeliverable, the administrator can still complete onboarding using the copyable invitation link on the confirmation.
- **SC-006**: The corresponding/filtered end-to-end tests for the touched areas (admin create without a password, invitation link → set password → sign in, resend supersedes prior link, expired/used link rejection, admin-visible link fallback) pass green.

## Assumptions

- The invitation reuses the existing password-reset/set-password mechanism and the existing `/Account/ResetPassword` set-password page, with the lifetime parameterized to 72 hours for invitations (the user-initiated reset stays at its current shorter lifetime).
- The administrator is trusted (they created the account), so exposing the invitation link to them on the confirmation screen is acceptable; this is the deliberate delivery-resilience fallback.
- The existing non-Production recipient allowlist still applies to the invitation email; the admin-visible link is the onboarding path when a recipient is not allowlisted.
- "No usable password" means the account exists but cannot authenticate until the invited user sets a password; the exact mechanism is deferred to planning.
- Resend is available while a user has not completed onboarding; for already-onboarded users, password recovery is the existing forgot-password flow (resend may still be offered but is not required to be hidden — deferred to planning).

## Out of Scope

- The existing user-initiated forgot-password flow (unchanged).
- Retiring or converting the existing administrator temp-password "reset password" action to a link-based flow; it remains as-is even though it now overlaps with resend-invite (a future coherence pass).
- Bulk/CSV invitations and scheduled invite reminders.
- Any change to which users may be created or by whom (spec 032 governs admin-only creation, unchanged).

## Dependencies

- The existing password-reset/set-password token infrastructure (issue + consume), extended to support a 72-hour invitation lifetime.
- The existing `/Account/ResetPassword` set-password page.
- The existing email sender + sender configuration (sender identity, base URL for absolute links) and the established transactional-email branding/layout.
- The administrator user create/list/edit screens (spec 032).
