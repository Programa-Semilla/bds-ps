# Phase 1 Data Model: User invitation / set-password onboarding email

**Feature**: 033-user-invite-email
**Date**: 2026-06-12

**No new entities, no new tables, no dacpac change.** The feature reuses the existing
`PasswordResetToken` aggregate and the `dbo.PasswordResetTokens` table. The changes are
behavioral (a longer lifetime, a supersede operation, and a password-less account creation).

---

## Reused entity: `PasswordResetToken` (unchanged structure)

`src/FundingPlatform.Domain/Entities/PasswordResetToken.cs` — used as-is. One additive constant:

| Member | Change |
|--------|--------|
| `DefaultLifetime` (`60 min`) | unchanged (forgot-password) |
| `InvitationLifetime` (`72 h`) | **new** `static readonly TimeSpan` — the invite TTL |

Table `dbo.PasswordResetTokens` (UserId, TokenHash, IssuedAt, ExpiresAt, ConsumedAt; unique
`UX_PasswordResetTokens_TokenHash`; index `IX_PasswordResetTokens_UserId_IssuedAt`) — **unchanged**.
An invite is just a row with `ExpiresAt = IssuedAt + 72h`.

---

## Account (`ApplicationUser`) — behavioral change only

| Aspect | Before | After (invited users) |
|--------|--------|------------------------|
| Password at creation | admin-typed temp password (`CreateAsync(user, pwd)`) | **none** (`CreateAsync(user)`, `PasswordHash` null) |
| `MustChangePassword` | `true` | **`false`** (FR-005 — user picks their own via the link) |

No column change (`AspNetUsers.PasswordHash` is already nullable).

---

## Store change: `IPasswordResetTokenStore`

`src/FundingPlatform.Application/Abstractions/IPasswordResetTokenStore.cs` (+ impl `PasswordResetTokenStore`)

| Member | Change |
|--------|--------|
| `IssueAsync(userId, rawToken, ttl, ct)` | unchanged |
| `ConsumeAsync(userId, rawToken, ct)` | unchanged |
| `InvalidateUnusedAsync(userId, ct)` | **new** — deletes the user's un-consumed token rows (DELETE WHERE `UserId=@id AND ConsumedAt IS NULL`). Backs FR-007 (resend supersedes). |

---

## Command change: issue handler

`src/FundingPlatform.Application/Identity/IssuePasswordResetTokenCommand.cs` + `IssuePasswordResetTokenHandler`

| Field | Change |
|-------|--------|
| `Email` | unchanged |
| `Ttl` (`TimeSpan?`, default `null`) | **new** — `null` → `DefaultLifetime`; invite passes `InvitationLifetime` (72h) |
| `InvalidatePriorUnused` (`bool`, default `false`) | **new** — when `true`, handler calls `InvalidateUnusedAsync(userId)` before issuing (invite path) |

`IssuePasswordResetTokenResult` (UserFound, UserId, Email, FirstName, RawToken) — unchanged; the controller already composes the link from `RawToken` + `UserId`.

The existing `AccountController.ForgotPassword` call passes neither new field (defaults preserve 60-min, no-supersede behavior).

---

## DTO / view-model changes (Web + Application)

| Type | Change |
|------|--------|
| `CreateUserRequest` | **remove** `InitialPassword` |
| `AdminUserCreateViewModel` | **remove** `InitialPassword` (+ its `[Required]`/`[StringLength]`) |
| `AdminUserInvitationSentViewModel` (new) | `{ Email, InviteLink }` — backs the FR-008 confirmation view |

---

## Email artifact (new, Infrastructure + Web)

| Artifact | Purpose |
|----------|---------|
| `InvitationEmailFactory` (Infrastructure/Email) | builds the es-CR `EmailMessage` (subject + body) from `{ toAddress, firstName, inviteLink, expiresAt }` — mirrors `ForgotPasswordEmailFactory` |
| `Views/Emails/Identity/InvitationEmail.cshtml` | es-CR template with `{{InviteLink}}`, `{{FirstName}}`, `{{ExpiresAt}}` placeholders (same plain-text + branding pattern as `ForgotPasswordEmail.cshtml`) |

---

## Validation rules (where enforced)

| Rule | Enforced at |
|------|-------------|
| Create no longer requires a password | `AdminUserCreateViewModel` (field removed) + controller |
| Invite token 72h, single-use | `IssuePasswordResetTokenHandler` (TTL) + `PasswordResetToken.Consume` (existing) |
| Resend invalidates prior unused | `PasswordResetTokenStore.InvalidateUnusedAsync` via the `InvalidatePriorUnused` flag |
| Password policy on set | existing `ConsumePasswordResetTokenHandler` → Identity `ResetPasswordAsync` validators (unchanged) |
| Expired/used/invalid link rejection | existing `ConsumeAsync` + `/Account/ResetPassword` (unchanged es-CR copy) |
