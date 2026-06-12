# Phase 1 Contracts: User invitation / set-password onboarding email

**Feature**: 033-user-invite-email

Server-rendered ASP.NET MVC; the "contracts" are HTTP route behaviors + the invitation
email contract. Each is stated so an E2E test can assert it.

---

## C1. Admin create — no password, invitation sent

`POST /Admin/Users/Create` (auth: Admin; `[SupplierAdminDenied]`).

**Request change**: the form no longer contains/binds `InitialPassword`.

| Scenario | Result |
|----------|--------|
| Valid new user submitted (any role) | Account created with **no password** + `MustChangePassword=false`; a 72h single-use invite token is issued; an es-CR invitation email is sent to the user; the response renders the **"Invitación enviada"** confirmation showing the email + a copyable invite link |
| Validation error (e.g., duplicate email, missing required field, spec-032 UserCode rules) | Re-renders the create form with es-CR errors; no account, no invite |

**Assertions**: after a valid create, the confirmation shows `data-testid="invitation-sent"` with the recipient email and a copyable link (`data-testid="invitation-link"`); the created account cannot sign in until the link is used.

---

## C2. Set password via the invite link (reuses existing route)

`GET /Account/ResetPassword?userId=&token=` → set-password page; `POST /Account/ResetPassword` → consumes the token, sets the password, allows sign-in. **Unchanged route/behavior** (the invite link targets it).

| Scenario | Result |
|----------|--------|
| Valid, unexpired, unused invite token + valid password | password set; redirect to Login with success; user can now sign in; **not** forced through change-password |
| Expired (>72h) / already-used / invalid token | es-CR "Enlace inválido o expirado. Solicite uno nuevo a su administrador."; no password change |

---

## C3. Resend invitation

`POST /Admin/Users/{id}/ResendInvitation` (auth: Admin; antiforgery).

| Scenario | Result |
|----------|--------|
| Resend for a user | the user's prior **un-consumed** invite tokens are invalidated; a fresh 72h single-use token is issued; a new invitation email is sent; renders the same **"Invitación enviada"** confirmation with the new copyable link |
| The previously issued (now superseded) link | no longer works (→ C2 expired/used rejection) |

**UI**: a "Reenviar invitación" action on the admin users list (`data-testid="row-action-resend-invite"`) and/or edit, alongside the existing "Restablecer" (temp-password reset) action, which is unchanged.

---

## C4. Invitation email contract

Sent via `IEmailSender` directly (not the outbox), composed by `InvitationEmailFactory`.

- **To**: the new user's email. **From**: the existing configured sender (Programa Semilla).
- **Subject** (es-CR), e.g. "Le han creado una cuenta — establezca su contraseña".
- **Body** (es-CR): greeting with the user's name, one line that an account was created for them on the platform, a **set-password call-to-action** linking to the absolute `/Account/ResetPassword` URL (built from `Notifications:BaseUrl`/request host + the token), and the **expiry** (72h, rendered in CR local time). Same branding/layout as the other transactional emails.
- **Non-prod**: subject to the existing recipient allowlist; a non-allowlisted recipient is dropped (`BlockedByAllowlist`) — onboarding then relies on the C1/C3 admin-visible link.

---

## C5. Confirmation / fallback contract (FR-008)

After C1 (create) and C3 (resend), the rendered confirmation MUST contain:
- text confirming the invitation was sent to `{email}`,
- the full invite link, copyable (a copy button), shown **once** (not retrievable later — only the token hash is stored),
- a link back to the users list.
