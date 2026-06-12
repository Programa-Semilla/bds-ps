# Tasks: User invitation / set-password onboarding email

**Input**: Design documents from `specs/033-user-invite-email/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/contracts.md, quickstart.md

**Tests**: Included — Constitution III makes E2E non-negotiable and SC-006 requires filtered E2E; integration covers the token TTL + supersede on a real DB; unit covers the new constant.

**Organization**: By user story. US2 and US3 build on US1's invite issuance + confirmation; all three depend on the Phase 2 foundation (token TTL/supersede + email factory/template).

## Format: `[ID] [P?] [Story] Description`
- **[P]**: parallelizable (different file, no incomplete-task dependency)
- File paths are repo-relative.

---

## Phase 1: Setup

- [ ] T001 [P] Confirm **no schema change** is needed (reuse `dbo.PasswordResetTokens`; the 72h invite is just a longer `ExpiresAt`, supersede is a `DELETE`); note the ephemeral E2E sentinel admin (`admin@programa-semilla.test` / `Sentinel123!`) for the new tests.
- [ ] T002 [P] Inventory the E2E tests that create a user via the admin UI **and then log in as them** (they pass `initialPassword`) and the two obsolete first-login tests (`AdminUserLifecycleTests.NewlyCreatedUser_OnFirstLogin_RedirectsToChangePassword`, `Admin_ChangePassword_ClearsMustChangeFlag`) — record the hit list for the T017/T019 rewrite.

---

## Phase 2: Foundational — token TTL/supersede + invite email (BLOCKS US1 + US2)

- [ ] T003 Add `public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(72);` to `src/FundingPlatform.Domain/Entities/PasswordResetToken.cs` (leave `DefaultLifetime` = 60 min unchanged).
- [ ] T004 Add `Task InvalidateUnusedAsync(string userId, CancellationToken ct)` to `src/FundingPlatform.Application/Abstractions/IPasswordResetTokenStore.cs` and implement it in `src/FundingPlatform.Infrastructure/Identity/PasswordResetTokenStore.cs` (delete rows `WHERE UserId == userId && ConsumedAt == null`).
- [ ] T005 Add `Ttl` (`TimeSpan?`, default `null`) and `InvalidatePriorUnused` (`bool`, default `false`) to `src/FundingPlatform.Application/Identity/IssuePasswordResetTokenCommand.cs`; in `src/FundingPlatform.Infrastructure/Identity/IssuePasswordResetTokenHandler.cs`, when `InvalidatePriorUnused` call `InvalidateUnusedAsync(user.Id, ct)` before issuing, and pass `command.Ttl ?? PasswordResetToken.DefaultLifetime` to `IssueAsync`. The existing `AccountController.ForgotPassword` call must keep default (60 min, no supersede).
- [ ] T005b **(plan-review fix — the second expiry gate)** In `src/FundingPlatform.Web/Program.cs` bump `DataProtectionTokenProviderOptions.TokenLifespan` from `TimeSpan.FromMinutes(60)` to `TimeSpan.FromHours(72)`. Required because the consume path validates Identity's DataProtector crypto token (via `ResetPasswordAsync`) in addition to our row, and that token's lifetime is this global option — left at 60 min it caps every invite at 60 min. Safe: password-reset is the only DataProtector-token consumer (`RequireConfirmedAccount = false`, no email-confirmation flow), and the per-row TTL stays the stricter gate so forgot-password remains effectively 60 min (research D2).
- [ ] T006 [P] Create `src/FundingPlatform.Infrastructure/Email/InvitationEmailFactory.cs` — builds an es-CR `EmailMessage` from `{ toAddress, firstName, inviteLink, expiresAt }`, mirroring `ForgotPasswordEmailFactory` (CR local-time expiry, branding).
- [ ] T007 [P] Create `src/FundingPlatform.Web/Views/Emails/Identity/InvitationEmail.cshtml` — es-CR invite template with `{{InviteLink}}`, `{{FirstName}}`, `{{ExpiresAt}}` placeholders (same plain-text + branding pattern as `ForgotPasswordEmail.cshtml`).
- [ ] T008 [P] Add es-CR strings to `src/FundingPlatform.Web/Resources/AdminUsersResources.cs`: invite email subject, "Invitación enviada a {0}", confirmation/help copy, copy-link label, "Reenviar invitación".
- [ ] T009 [P] Unit test in `tests/FundingPlatform.Tests.Unit/` asserting `PasswordResetToken.InvitationLifetime == 72h` and that `PasswordResetToken.Issue(..., InvitationLifetime)` sets `ExpiresAt = IssuedAt + 72h`.
- [ ] T010 Integration tests in `tests/FundingPlatform.Tests.Integration/` (real-DB-via-InMemory): issuing with `Ttl = 72h` persists `ExpiresAt ≈ +72h`; the default issue (forgot-password) still persists `ExpiresAt ≈ +60min`; `InvalidateUnusedAsync` deletes the user's un-consumed rows (and leaves consumed rows); after invalidate, the prior token's `ConsumeAsync` returns false. (The Identity-crypto-token 72h gate from T005b is verified end-to-end in the E2E set-password flow — `ResetPasswordAsync` succeeding on a >60-min-old invite — since `DataProtectionTokenProviderOptions` is a Web-host concern.)

**Checkpoint**: token TTL/supersede + invite email building blocks exist; US1/US2 can begin.

---

## Phase 3: User Story 1 — New user onboards via an emailed invitation (Priority: P1) 🎯 MVP

**Goal**: Admin creates a user with no password; the user gets a 72h set-password link, sets a password, and signs in.
**Independent Test**: create a user (no password field) → confirmation shows a copyable link → follow it, set a password → sign in (no forced change-password).
**Depends on**: Phase 2.

- [ ] T011 [US1] Remove `InitialPassword` from `src/FundingPlatform.Application/Admin/Users/DTOs/CreateUserRequest.cs`.
- [ ] T012 [US1] In `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` `CreateUserAsync`: create the user via `_userManager.CreateAsync(user)` (no-password overload), set `MustChangePassword = false`, and drop the `request.InitialPassword` usage.
- [ ] T013 [P] [US1] Remove `InitialPassword` (and its `[Required]`/`[StringLength]`/`[DataType]`) from `src/FundingPlatform.Web/ViewModels/Admin/AdminUserCreateViewModel.cs`.
- [ ] T014 [P] [US1] Remove the "Contraseña inicial" field block from `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml`.
- [ ] T015 [US1] Add `src/FundingPlatform.Web/ViewModels/Admin/AdminUserInvitationSentViewModel.cs` (`{ string Email, string InviteLink }`) and `src/FundingPlatform.Web/Views/Admin/Users/InvitationSent.cshtml` — es-CR confirmation with `data-testid="invitation-sent"`, the recipient email, a copyable link `data-testid="invitation-link"` + copy button, and a "Volver a usuarios" link.
- [ ] T016 [US1] In `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` `Create` POST: after `_service.CreateUserAsync` succeeds, issue an invite token via `IIssuePasswordResetTokenHandler` (`IssuePasswordResetTokenCommand(email, Ttl: PasswordResetToken.InvitationLifetime, InvalidatePriorUnused: true)`); compose the absolute link with `Url.Action(nameof(AccountController.ResetPassword), "Account", new { userId, token }, Request.Scheme, Request.Host.Value)`; send via `InvitationEmailFactory` + `IEmailSender`; render `InvitationSent` with the email + link. Inject the handler, `IEmailSender`, and `InvitationEmailFactory` into the controller. Map the create form without `InitialPassword`.
- [ ] T017 [US1] In `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminUserCreatePage.cs`: stop filling `InitialPassword` (keep the param, ignored). Add an E2E helper (e.g. on `AuthenticatedTestBase` or a new page object) that reads the invite link from the `InvitationSent` page (`[data-testid="invitation-link"]`) and completes set-password (`/Account/ResetPassword`) → login with the chosen password.
- [ ] T018 [US1] E2E `UserInvitationTests` in `tests/FundingPlatform.Tests.E2E/`: create a Solicitante (assert no password field) → confirmation shows `invitation-sent` + a copyable link → follow the link, set a password → sign in, land authenticated with **no** forced change-password. Repeat for at least one staff role (e.g. Reviewer) to cover FR-003.
- [ ] T019 [US1] Rewrite the two obsolete first-login tests in `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminUserLifecycleTests.cs` (`NewlyCreatedUser_OnFirstLogin_RedirectsToChangePassword`, `Admin_ChangePassword_ClearsMustChangeFlag`) to the invite flow (create → invite link → set password → sign-in), or remove them if fully superseded; keep the other lifecycle tests green via the T017 helper / `SeedUser`.

**Checkpoint**: US1 independently shippable.

---

## Phase 4: User Story 2 — Administrator resends an invitation (Priority: P2)

**Goal**: An admin can resend a fresh invite; the prior link stops working.
**Independent Test**: create → resend → the new link works; the prior link is rejected.
**Depends on**: Phase 2 + US1's issue/confirmation path (T015/T016).

- [ ] T020 [US2] Add `[HttpPost("{id}/ResendInvitation")]` to `AdminUsersController`: resolve the user by id, issue a fresh invite token (`InvalidatePriorUnused: true`, 72h) by email, compose the link, send via `InvitationEmailFactory` + `IEmailSender`, and render the `InvitationSent` confirmation. Antiforgery + Admin auth.
- [ ] T021 [P] [US2] Add a "Reenviar invitación" row action to `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml` (`data-testid="row-action-resend-invite"`, es-CR), alongside — not replacing — the existing "Restablecer" temp-password action.
- [ ] T022 [US2] E2E in `UserInvitationTests` (or a sibling class): create a user, resend the invitation, confirm the new confirmation/link completes onboarding, and that the **first** (superseded) link now shows the es-CR "Enlace inválido o expirado…" rejection. Add `RowResendInviteLink(email)` to `AdminUsersListPage`.

**Checkpoint**: US2 shippable.

---

## Phase 5: User Story 3 — Onboarding works when email can't be delivered (Priority: P2)

**Goal**: The admin-visible copyable link lets onboarding complete even when the email is filtered/undeliverable.
**Independent Test**: create/resend → the confirmation's copyable link (used directly, not via email) completes set-password → sign-in.
**Depends on**: US1 (the confirmation view already carries the link).

- [ ] T023 [US3] E2E assertion (extend `UserInvitationTests`): the create **and** resend confirmations render the copyable `invitation-link`, and following that link (independent of any email capture) completes onboarding — the explicit FR-008 fallback. Note in the test that in non-prod the email recipient may be allowlist-dropped, so the link is the onboarding path.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T024 Run the filtered E2E (`UserInvitationTests` + the rewritten `AdminUserLifecycleTests`) green and capture counts; run Unit + Integration green.
- [ ] T025 Update `CLAUDE.md` Recent Changes with the `033-user-invite-email` summary + counts; flip the SPECKIT marker to "implemented".
- [ ] T026 Sweep: grep for residual `InitialPassword` references in the create path (none should remain); confirm `AccountController.ForgotPassword` still issues a non-superseding token whose **row** TTL is 60 min (now the binding gate, since the global Identity token is 72h) — i.e. a forgot-password link is still rejected after 60 min; confirm the admin temp-password "reset password" action is untouched.

---

## Dependencies & Order

- **Phase 1** → no deps.
- **Phase 2 (T003–T010)** → blocks **US1** and **US2**.
- **US1 (Phase 3)** → needs Phase 2. T011/T012 (service+DTO) and T013/T014 (VM+view) before T016 (controller); T015 (confirmation view) before T016; T017 helper before T018/T019.
- **US2 (Phase 4)** → needs Phase 2 + US1's T015/T016 (confirmation + issue path).
- **US3 (Phase 5)** → needs US1's confirmation view; mostly an added assertion.
- **Phase 6** → after the stories it verifies.

## Parallel Execution Examples

- **Phase 2**: T006, T007, T008, T009 are `[P]` (distinct files) after T003/T005.
- **US1**: T013, T014 `[P]` (VM + view) run alongside T011/T012 (DTO + service); T015 `[P]` with them.

## Implementation Strategy

- **MVP = US1** (invite-on-create + set-password + sign-in). Ship/verify first.
- Then **US2** (resend) and **US3** (the explicit fallback assertion). Each story checkpoints with its own filtered E2E (Constitution III, SC-006). Commit at each checkpoint (CLAUDE.md speckit-checkpoint discipline).
- Reuse-first: no schema change, no new entity/table/deps — the bulk of the risk is the E2E onboarding-flow rewrite (T017/T019), so land the T017 helper early.
