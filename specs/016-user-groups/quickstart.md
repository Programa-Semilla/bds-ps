# Quickstart: Group-Scoped Reviewer Access

Walks through the feature end-to-end on a local Aspire dev run.

## Prerequisites

- The standard FundingPlatform dev requirements (.NET 10 SDK, Docker for
  the Aspire-managed SQL container).
- A clean checkout of `feature/group-users` after spec 016 ships.

## Steps

1. Start the platform:

   ```bash
   dotnet run --project src/FundingPlatform.AppHost
   ```

   Wait until the Aspire dashboard reports the `Web` resource healthy. The
   dacpac auto-deploys on startup; the post-deploy script seeds the three
   demo groups: `Norte`, `Sur`, `Centro`.

2. Sign in as the sentinel admin:

   - Email: `admin@FundingPlatform.com`
   - Password: the value of `Admin:DefaultPassword` (or `Sentinel123!` if
     `EphemeralStorage=true`).

3. Verify the catalog is in place:

   - Navigate to `/Admin/Groups`.
   - Confirm `Norte`, `Sur`, `Centro` are listed with member count `0`.

4. Create a fourth group to confirm CRUD works:

   - Click "Crear grupo" and submit `Pruebas`.
   - Confirm it appears in the list with member count `0`.
   - Submit `Pruebas` again — the form rejects with the
     `NameAlreadyInUse` validation message.

5. Assign a reviewer to a group:

   - Navigate to `/Admin/Users`.
   - Open the seeded reviewer (`reviewer@FundingPlatform.com`) for editing.
   - Select `Norte` in the group multi-select. Save.
   - Confirm the redirect lands on `/Admin/Users` with the reviewer's row
     visible.

6. Sign in as the reviewer:

   - Sign out as admin. Sign in with `reviewer@FundingPlatform.com`.
   - Navigate to the reviewer queue (`/Review`).
   - Confirm the queue lists only applications whose applicant is in
     `Norte`.
   - Open an application detail page that the queue shows — page renders.
   - Manually craft a URL to an out-of-scope application id. The server
     returns 403 (FR-012, NFR-002).

7. Confirm the signing inbox and search obey the same scope:

   - Open `/Review/SigningInbox`. Confirm only `Norte` applicants show.
   - Use the applicant search; confirm only `Norte` applicants are
     returned.

8. Confirm cascade deletion:

   - Sign back in as admin. Navigate to `/Admin/Groups`.
   - Delete `Norte`. Confirm the reviewer (and any other users formerly in
     `Norte`) now show with empty membership lists in `/Admin/Users` — but
     none have been deleted.
   - Sign back in as the reviewer. The queue is empty; sign-in still works.

9. Confirm Admin bypass:

   - Sign in as admin. Confirm `/Review`, `/Review/SigningInbox`, the
     applicant search, and an arbitrary application detail page all show
     every applicant and every application.

## What was demonstrated

- Stories 1, 2, 3, and 4 from the spec, end-to-end against a real database.
- NFR-001 (query-level filtering), NFR-002 (server-side detail auth),
  NFR-003 (no sign-out required when memberships change),
  NFR-004 (es-CR copy in the new screens), NFR-005 (audit rows in
  `dbo.AdminAuditEvents` for each admin action).
