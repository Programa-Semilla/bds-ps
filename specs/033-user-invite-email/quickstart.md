# Quickstart: User invitation / set-password onboarding email

**Feature**: 033-user-invite-email

## Run the app

```bash
dotnet run --project src/FundingPlatform.AppHost
```

No schema change (reuses `dbo.PasswordResetTokens`). The smtp4dev sidecar captures the invite email in dev.

## Manual smoke (es-CR)

1. Sign in as admin (`admin@programa-semilla.test` / `Sentinel123!` in ephemeral).
2. `/Admin/Users/Create` — note there is **no password field**. Create a Solicitante (valid UserCode per spec 032).
3. Confirmation shows "Invitación enviada a {email}" + a **copyable invite link**. Copy it.
4. Open the link (incognito): set a password on `/Account/ResetPassword` → redirected to Login.
5. Sign in with the new password → lands authenticated, **no** forced change-password step.
6. Back as admin, on `/Admin/Users` use **"Reenviar invitación"** → new confirmation + link; the **old** link now shows "Enlace inválido o expirado…".
7. Open the invite email in smtp4dev (`http://localhost:<smtp4dev http port>`) → es-CR invitation with the set-password CTA + 72h expiry.

## Filtered E2E (delivery gate — Constitution III / SC-006)

Run only the touched classes (final names set during `/speckit-tasks`):

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~UserInvitation"
# + the rewritten admin-lifecycle onboarding tests
```

Green on: admin create without a password + confirmation link; invite link → set password → sign in; resend supersedes the prior link; expired/used link rejection; all roles.

## Unit / Integration

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # InvitationLifetime constant; (token invariants already covered)
dotnet test tests/FundingPlatform.Tests.Integration   # issue-with-72h-TTL; InvalidateUnusedAsync supersede; consume-after-invalidate (real-DB-via-InMemory)
```

## Notes

- The raw invite link is shown **once** (only the token hash is stored); if the admin loses it, resend.
- The Development-only `SeedUser` seam still creates users **with** a password — the E2E bootstrap (`RegisterUserAsync`) is unaffected by removing the create-form password.
