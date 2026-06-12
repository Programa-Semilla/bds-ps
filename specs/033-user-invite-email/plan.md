# Implementation Plan: User invitation / set-password onboarding email

**Branch**: `033-user-invite-email` | **Date**: 2026-06-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/033-user-invite-email/spec.md`

## Summary

Replace the admin-typed temporary password (spec 032) with an emailed, 72-hour, single-use **set-password invitation**. The admin create form drops its password field; the account is created with **no password** (`MustChangePassword=false`); the controller issues a 72h reset/invite token (reusing the existing password-reset flow with a parameterized TTL), composes the absolute `/Account/ResetPassword` link, sends an es-CR invitation email directly (the `ForgotPasswordEmail` pattern, not the outbox), and renders an "Invitación enviada" confirmation showing a **copyable link** (delivery-resilience fallback). An admin can **resend**, which invalidates the prior unused token and issues a fresh one. The user clicks the link, sets their own password (existing consume path clears `MustChangePassword` + refreshes the security stamp), and signs in. **No schema change, no new dependencies.**

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10, ASP.NET Identity
**Primary Dependencies**: existing password-reset token flow, `IEmailSender` + sender config, Razor email templates, spec-032 admin user screens. **No new NuGet/CDN deps.**
**Storage**: reuses `dbo.PasswordResetTokens` (no schema change); EF Core for data access.
**Testing**: Playwright E2E (AspireFixture), Unit, Integration (real DB).
**Target Platform**: Linux container (Aspire-orchestrated).
**Project Type**: Server-rendered web app (Clean Architecture).
**Performance Goals**: N/A (one extra token-issue + email send per create/resend).
**Constraints**: es-CR copy; vendored assets only; reuse over new infra; invite email is direct-send (single recipient + token, no application context).
**Scale/Scope**: ~1 store method, ~1 command param + handler tweak, 1 email factory + template, create-form password removal, 1 confirmation view, 1 resend action; plus the E2E onboarding-test rewrite.

## Constitution Check

*GATE: must pass before Phase 0 and again after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| **I. Clean Architecture** | Token issue/consume = Application handlers + Infrastructure store; email factory + template = Infrastructure/Web; confirmation/resend = Web. Dependencies point inward. | PASS |
| **II. Rich Domain Model** | Token invariants (single-use/expiry) stay on the reused `PasswordResetToken` entity; supersede is a store delete (data op), consistent with the existing store. | PASS |
| **III. E2E (NON-NEGOTIABLE)** | Each story gets Playwright coverage: create-without-password + confirmation link; invite → set password → sign-in; resend supersedes; expired/used rejection. SC-006. | PASS |
| **IV. Schema-First** | **No dacpac change** — reuse `dbo.PasswordResetTokens`; no `EnsureCreated`/EF migration. | PASS |
| **V. Spec-Driven** | spec → plan → tasks → implement; stories independently testable/deliverable. | PASS |
| **VI. Simplicity / YAGNI** | Heavy reuse (token flow, set-password page, email pattern); no new entity/table/deps; bulk/reminders + retiring the temp-password reset action explicitly out of scope. | PASS |

**Initial Constitution Check: PASS** (no violations → Complexity Tracking empty).
**Post-Design Re-check: PASS** (design adds no new projects/dependencies/abstractions beyond one store method + one command parameter).

## Project Structure

### Documentation (this feature)

```text
specs/033-user-invite-email/
├── spec.md            # /speckit-specify
├── plan.md            # this file
├── research.md        # Phase 0 — D1..D8
├── data-model.md      # Phase 1 — reused token, behavioral changes, DTO/email artifacts
├── contracts/contracts.md   # Phase 1 — create/set-password/resend/email/confirmation contracts
├── quickstart.md      # Phase 1 — run + smoke + test gates
├── review_brief.md / REVIEW-SPEC.md / checklists/requirements.md
└── tasks.md           # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (touch points)

```text
src/FundingPlatform.Domain/
└── Entities/PasswordResetToken.cs            # + InvitationLifetime (72h) constant

src/FundingPlatform.Application/
├── Abstractions/IPasswordResetTokenStore.cs  # + InvalidateUnusedAsync(userId, ct)
├── Identity/IssuePasswordResetTokenCommand.cs # + Ttl (TimeSpan?), InvalidatePriorUnused (bool)
└── Admin/Users/DTOs/CreateUserRequest.cs      # − InitialPassword

src/FundingPlatform.Infrastructure/
├── Identity/PasswordResetTokenStore.cs        # implement InvalidateUnusedAsync (DELETE unused)
├── Identity/IssuePasswordResetTokenHandler.cs # honor Ttl + InvalidatePriorUnused
├── Identity/UserAdministrationService.cs      # CreateUserAsync → CreateAsync(user) (no pwd) + MustChangePassword=false
└── Email/InvitationEmailFactory.cs            # NEW — es-CR EmailMessage (mirror ForgotPasswordEmailFactory)

src/FundingPlatform.Web/
├── Program.cs                                 # bump DataProtectionTokenProviderOptions.TokenLifespan 60min → 72h (the Identity crypto-token gate; per-row TTL keeps forgot-password at 60min — see research D2)
├── Controllers/Admin/AdminUsersController.cs  # Create POST: issue 72h invite token + compose link + send + render confirmation; new ResendInvitation POST
├── ViewModels/Admin/AdminUserCreateViewModel.cs   # − InitialPassword
├── ViewModels/Admin/AdminUserInvitationSentViewModel.cs  # NEW — { Email, InviteLink }
├── Views/Admin/Users/Create.cshtml            # − password field
├── Views/Admin/Users/InvitationSent.cshtml    # NEW — confirmation w/ copyable link (data-testid)
├── Views/Admin/Users/Index.cshtml             # + "Reenviar invitación" row action
├── Views/Emails/Identity/InvitationEmail.cshtml  # NEW — es-CR invite template
└── Resources/AdminUsersResources.cs           # + es-CR invite strings (subject/labels/confirmation)

tests/
├── FundingPlatform.Tests.Unit/                # InvitationLifetime
├── FundingPlatform.Tests.Integration/         # issue-72h-TTL; InvalidateUnusedAsync supersede; consume after invalidate
└── FundingPlatform.Tests.E2E/                 # UserInvitation* ; rewrite obsolete AdminUserLifecycle first-login tests; AdminUserCreatePage (drop password fill)
```

**Structure Decision**: existing four-layer Clean Architecture (`FundingPlatform.slnx`). Additive within current files + reuse of the password-reset seam; the only deletions are the create-form password field and two obsolete first-login E2E tests.

## Complexity Tracking

> No Constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |

## Notes for `/speckit-tasks`

- Order by user story: **US1 invite-on-create** (token TTL param + store reuse → service no-password create → controller issue+send+confirmation → email factory+template → create-form removal), **US2 resend** (InvalidateUnused + ResendInvitation action), **US3 delivery-resilience** (confirmation copyable link — largely lands with US1's confirmation view; add the explicit fallback assertion).
- Foundational first: `InvitationLifetime` constant, `InvalidateUnusedAsync` + the `Ttl`/`InvalidatePriorUnused` command params, the `InvitationEmailFactory` + template — US1/US2 build on these.
- **E2E rewrite** (call out, like spec 032's D-2): `AdminUserCreatePage.FillAsync` drops the password fill (param kept, ignored); rewrite `AdminUserLifecycleTests.NewlyCreatedUser_OnFirstLogin_RedirectsToChangePassword` + `Admin_ChangePassword_ClearsMustChangeFlag` to the invite flow (or remove — they assert the replaced temp-password model). A new E2E helper that, given the confirmation page, extracts the invite link and completes set-password→login (so admin-create-then-login tests keep working). `SeedUser` keeps password-based bootstrap untouched.
- Integration: the supersede + 72h-TTL behaviors are real-DB-testable (the store + handler), unlike a pure UI assertion — cover them at the Integration layer; the single-use/expired rejection is already covered by existing reset-token tests.
- es-CR for all new copy (subject, confirmation, resend button, email body).
