# Phase 0 Research: User invitation / set-password onboarding email

**Feature**: 033-user-invite-email
**Date**: 2026-06-12

All open threads from the spec/brainstorm are resolved below. No `NEEDS CLARIFICATION` remain.

---

## D1. Reuse the password-reset token vs. a new invitation token

**Decision**: **Reuse** the existing password-reset/set-password flow end-to-end — `PasswordResetToken` + `IPasswordResetTokenStore` + `IIssuePasswordResetTokenHandler` + `IConsumePasswordResetTokenHandler` + the `/Account/ResetPassword` page. Add **one parameter** (TTL) and **one store op** (invalidate-prior-unused).

**Rationale**:
- The consume path already does exactly what an invite needs: validates the token, sets the password, sets `MustChangePassword=false`, and refreshes the security stamp (`ConsumePasswordResetTokenHandler`). It already works for a user with **no current password** (`ResetPasswordAsync`/`GeneratePasswordResetTokenAsync` are stamp-based, not password-based).
- The `/Account/ResetPassword` page + single-use marker (`ConsumeAsync`) already implement FR-004/FR-009/FR-010 (set-password landing, single-use, expired/used rejection with the exact es-CR copy "Enlace inválido o expirado…").
- A separate invitation token/table/entity would duplicate all of this for no gain. **No new entity, no new table, no dacpac change.**

**Alternatives considered**:
- *Dedicated invitation token + table*: rejected — pure duplication of `PasswordResetToken`; the only differences (72h lifetime, supersede-on-resend) are a parameter and a delete.

---

## D2. TTL parameterization (72h for invites, 60min for forgot-password)

**Decision**: Add an optional `TimeSpan? Ttl` to `IssuePasswordResetTokenCommand` (default `null` → `PasswordResetToken.DefaultLifetime` = 60 min). The invite path passes **72h**; the existing forgot-password path passes nothing (unchanged).

**Rationale**: The TTL is currently hard-coded at `IssuePasswordResetTokenHandler` (`IssueAsync(..., PasswordResetToken.DefaultLifetime, ct)`). The store's `IssueAsync(userId, rawToken, ttl, ct)` already accepts a `TimeSpan ttl` — so only the handler/command need a pass-through. The 72h value is added as a named constant (e.g., `PasswordResetToken.InvitationLifetime = TimeSpan.FromHours(72)`).

---

## D3. "Supersede on resend" (FR-007)

**Decision**: Add `InvalidateUnusedAsync(userId, ct)` to `IPasswordResetTokenStore` (deletes the user's un-consumed token rows). The invite issuance (create **and** resend) calls it **before** issuing; forgot-password does **not** (its behavior is unchanged). Gate it behind a `bool InvalidatePriorUnused = false` flag on `IssuePasswordResetTokenCommand`.

**Rationale**: The store currently allows multiple live tokens per user (unique index is on `TokenHash`, not per-user). FR-007 requires only the newest invite to be valid. Invalidating before issue gives that; scoping the flag to the invite path avoids changing forgot-password semantics. Edge: resending an invite also voids any in-flight forgot-password token for that user — negligible and arguably desirable.

---

## D4. "No usable password" technique

**Decision**: Create invited users via `_userManager.CreateAsync(user)` (the **no-password** overload). The account has no password hash and cannot authenticate until the invite is consumed. Set `MustChangePassword = false` for invited users (FR-005 — they choose their own password through the link; the consume path keeps it false).

**Rationale**: Standard Identity behavior; `CreateAsync(user)` leaves `PasswordHash` null (the column is already nullable). Removes the need for a throwaway random password. `MustChangePassword=true` (the current default) would be meaningless (no password to change) and contradicts FR-005, so invited users get `false`.

---

## D5. Delivery path: direct-send (not the spec-021 outbox)

**Decision**: Send the invitation email **directly** via `IEmailSender.SendAsync` from the controller, mirroring the existing `ForgotPasswordEmail` path — a new `InvitationEmailFactory` + `Views/Emails/Identity/InvitationEmail.cshtml` (es-CR, same branding). **Not** the spec-021 notification outbox.

**Rationale**: The outbox is application-shaped (`NotificationOutbox` requires `ApplicationId` + a recipient resolver over the application aggregate). An invite is a single known recipient with a token and no application context — forcing it through the outbox needs resolver + payload changes. The forgot-password email already bypasses the outbox with exactly this direct-send-from-controller pattern; the invite is structurally identical. Delivery resilience is covered by FR-008 (admin-visible link) + resend, not outbox retry.

**Composition site**: the absolute link is built with `Url.Action(nameof(ResetPassword), "Account", { userId, token }, scheme, host)` — which requires the request context, so the **controller** (`AdminUsersController`) issues the token + composes the link + sends the email (the `UserAdministrationService` only creates the user). This matches `AccountController.ForgotPassword`.

---

## D6. Admin confirmation with the copyable link (FR-008)

**Decision**: On create-success and resend-success, render an **"Invitación enviada"** confirmation view showing the recipient email **and** the copyable invite link (with a copy button), plus a "Volver a usuarios" action. The link is shown **once** (the raw token is never stored — only its hash), so navigating away requires a resend.

**Rationale**: The raw token only exists at issue time. A confirmation rendered directly from the POST (no redirect) carries it without persistence. This is the delivery-resilience fallback for allowlist-filtered / undeliverable environments. Acceptable that the admin sees the link (they created the account).

---

## D7. Resend surface + admin-create form change

**Decision**:
- **Create form**: remove the `InitialPassword` field from `AdminUserCreateViewModel`, `Create.cshtml`, and `CreateUserRequest`; `UserAdministrationService.CreateUserAsync` calls `CreateAsync(user)` (no password) and sets `MustChangePassword=false`.
- **Resend**: add `POST /Admin/Users/{id}/ResendInvitation` + a "Reenviar invitación" action on the admin users list (and/or edit), alongside — not replacing — the existing temp-password "Restablecer" action (which stays, out of scope per spec). Resend is always available (works regardless of onboarding state).

**E2E ripple** (the main consequence):
- `AdminUserCreatePage.FillAsync` no longer fills `InitialPassword` (field gone); the param is kept but ignored for call-site compatibility (mirrors the spec-032 UserCode auto-fill approach).
- Tests that create a user via the admin UI **and then log in as them** must traverse the invite flow (grab the link from the confirmation → set password → login). The two spec-021/032 tests asserting the **old** temp-password + first-login change-password behavior (`AdminUserLifecycleTests.NewlyCreatedUser_OnFirstLogin_RedirectsToChangePassword`, `Admin_ChangePassword_ClearsMustChangeFlag`) are now **obsolete** (admin-create no longer sets a temp password / `MustChangePassword`) and are rewritten to the invite flow or removed.
- The Development-only `SeedUser` seam keeps password-based creation, so the ~broad bootstrap (`RegisterUserAsync`) is unaffected.
- The existing admin temp-password **reset** tests (`AdminResetPasswordTests`) are unaffected (that action is unchanged).

---

## D8. Schema impact

**Decision**: **No dacpac change.** The invite reuses `dbo.PasswordResetTokens` (the 72h lifetime is just a different `ExpiresAt`; supersede is a `DELETE`). Removing the create-form password doesn't touch schema (`AspNetUsers.PasswordHash` is already nullable). No new managed dependencies.

---

## Summary of decisions

| # | Topic | Decision |
|---|-------|----------|
| D1 | Token | Reuse the password-reset token/flow + `/Account/ResetPassword` |
| D2 | TTL | Optional `Ttl` on the issue command; invite = 72h, forgot-password = 60min |
| D3 | Supersede | `InvalidateUnusedAsync` on the store + an `InvalidatePriorUnused` flag (invite path only) |
| D4 | No password | `CreateAsync(user)` (no-password overload); invited users `MustChangePassword=false` |
| D5 | Delivery | Direct-send from the controller (`ForgotPasswordEmail` pattern) + `InvitationEmailFactory` + template; not the outbox |
| D6 | Confirmation | Render an "Invitación enviada" view with the copyable link (shown once) |
| D7 | Form + resend | Remove create-form password; add `ResendInvitation` action; rewrite obsolete first-login tests |
| D8 | Schema | None — reuse `dbo.PasswordResetTokens` |
