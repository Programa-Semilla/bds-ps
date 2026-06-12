# Quickstart: Admin-only user provisioning + unique applicant User Code

**Feature**: 032-admin-user-code

## Run the app

```bash
dotnet run --project src/FundingPlatform.AppHost
```

Schema (the new `UserCode` column + filtered index) auto-deploys via the dacpac at AppHost startup outside ephemeral mode.

## Manual smoke (es-CR)

1. **Registration is gone**: while signed out, browse to `/Account/Register` → expect **404**. Confirm the landing page hero button and `/Account/Login` show **no** "create account / Crea una aquí" link.
2. **Assign a code**: sign in as admin (`admin@programa-semilla.test` / `Sentinel123!` in ephemeral). `/Admin/Users/Create`, role = **Solicitante** → the "Código de usuario" field appears. Submit blank → blocked ("…obligatorio para el rol Solicitante."). Submit a 50-char unique value → created.
3. **Uniqueness**: create/edit a second Solicitante with the same code → blocked ("…ya está en uso.").
4. **Non-applicant**: switch role to Revisor → the code field disappears; create succeeds with no code.
5. **Profile**: sign in as that applicant → `/Account/Profile` shows "Código de usuario" read-only with the `administrado` badge.
6. **Search**: as admin, `/Admin/Users` search by the code, by the cédula, by email, by name → applicant appears. Repeat on the reviewer queue (sign in as reviewer) and on `/Admin/Reports/Applicants` (+ CSV export contains the code column).

## Filtered E2E (delivery gate — Constitution III)

Run only the classes that exercise this change (full suite ~30 min is not the default gate):

```bash
# examples — final class names set during /speckit-tasks + implementation
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~RegistrationRemoved"
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~AdminUserCode"
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~UserCodeSearch"
```

Green on: registration 404 + no links; admin create/edit required+unique+role-toggle; widened search on each surface; profile read-only display.

## Unit / Integration

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # Applicant.UserCode guard (≤50, whitespace→null)
dotnet test tests/FundingPlatform.Tests.Integration   # service uniqueness pre-check + each widened search predicate (real DB)
```

The DB-index race + the schema 404 are E2E-only (the in-memory provider does not enforce the filtered unique index; native 404 needs the real pipeline) — mirrors spec 030's `UX_Processes_Name` handling.
