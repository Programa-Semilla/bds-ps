# Quickstart: Batch user creation

## What this feature adds

An admin-only page at **`/Admin/Users/Batch`** to upload a CSV and provision up to 200 Solicitante accounts at once. Each created user receives the spec-033 set-password invitation. A succeeded/errored report is shown after processing.

## Try it locally

1. Run the stack:
   ```bash
   dotnet run --project src/FundingPlatform.AppHost
   ```
2. Sign in as the admin and go to **Admin → Usuarios → Crear por lote** (`/Admin/Users/Batch`).
3. Download the template (`Descargar plantilla`), fill rows, and upload.
4. Review the succeeded/errored report.

## Sample CSV

```csv
Grupo,Proceso,Fondo,Nombre,Apellido 1,Apellido 2,Email,Teléfono,Cédula,Código de usuario
Norte,Migración inicial,Fondo General,Ana,Rojas,Mora,ana.rojas@example.cr,506 8888 1111,1-1234-5678,COD-001
Sur,Migración inicial,Fondo General,Luis,Mora,,luis.mora@example.cr,7777-2222 / 8888-3333,2-3456-7890,COD-002
```

> The ephemeral E2E seed creates Fund **"Fondo General"** → Process **"Migración inicial"** → Groups **Norte/Sur/Centro**, so those names form valid chains in tests.

## Build & test

```bash
# Build
dotnet build FundingPlatform.slnx

# Unit (CSV parser + phone normalizer)
dotnet test tests/FundingPlatform.Tests.Unit --filter "FullyQualifiedName~Batch"

# Integration (service, real-DB-shaped)
dotnet test tests/FundingPlatform.Tests.Integration --filter "FullyQualifiedName~BatchUserCreation"

# E2E (filtered to this feature — the delivery gate)
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~BatchUserCreate"
```

## Delivery gate (per CLAUDE.md)

Filtered E2E for the new `BatchUserCreateTests` (US1 all-valid + invitations captured via `MailCapture`; US2 mixed file → report; US3 chain mismatch skipped) must be **personally executed and green**. The full ~30-min E2E suite is not the default gate unless the change proves cross-cutting.

## Key files

- Parsing/normalization (pure): `src/FundingPlatform.Application/Admin/Users/Batch/`
- Orchestration: `UserAdministrationService.CreateUsersBatchAsync` (Infrastructure)
- Controller + views: `AdminUsersController` (`Batch`, `Batch` POST, `BatchTemplate`), `Views/Admin/Users/Batch.cshtml`, `BatchResult.cshtml`
- E2E: `tests/.../PageObjects/Admin/AdminBatchUsersPage.cs`, `tests/.../Tests/Admin/BatchUserCreateTests.cs`
