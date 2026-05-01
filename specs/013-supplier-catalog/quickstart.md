# Quickstart: Centralized Supplier Catalog (013)

**Date:** 2026-04-30
**Plan:** [plan.md](./plan.md)

This is a manual walkthrough of the feature for local development and for a reviewer to validate the implementation against the spec. It assumes the implementation is complete and the dacpac has been deployed (see step 1).

## Prerequisites

- .NET 10 SDK installed.
- Docker Desktop running (Aspire spins up SQL Server in a container).
- Repository checked out at `main` with the `013-supplier-catalog` branch merged or checked out.
- Tabler.io static assets, Fraunces / Inter / JetBrains Mono fonts already vendored from prior specs.

## 1. Apply the schema and seed

```bash
# from repo root
dotnet build src/FundingPlatform.Database/FundingPlatform.Database.sqlproj -c Debug
dotnet run --project src/FundingPlatform.AppHost
```

Aspire boots SQL Server; the dacpac auto-deploys on first run. The post-deployment script `Migrations/013_SupplierCatalog.sql` runs idempotently. On a fresh DB it is a no-op (no legacy columns exist); on a database carrying spec-012 schema, it backfills the new columns and creates default branches.

Watch the Aspire log for either `Migration 013: completed` (intentional log line you add per FR-063 visibility) or any `THROW 5001x` errors — those abort the deploy.

## 2. Seed test data

The default seed (`PostDeployment/SeedData.sql`) already creates two applicants, one reviewer, one admin, and a handful of categories / impacts. To exercise this feature, add three suppliers:

- One **Verified** supplier with two branches (existing-supplier happy path).
- One **PendingReview** supplier owned by applicant `seed-applicant-1` (used to exercise the creator-only visibility rule).
- One **Rejected** supplier (used to exercise the lookup-rejected partial).

Drop the following INSERTs into a local one-off script (do NOT commit; the seed file is shared):

```sql
DECLARE @AdminId NVARCHAR(450) = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers WHERE [IsSystemSentinel] = 1);

INSERT INTO dbo.Suppliers (LegalId, Name, HasElectronicInvoice, IsCompliantCCSS, IsCompliantHacienda, IsCompliantSICOP, VerificationStatus, VerifiedByUserId, VerifiedAt, UpdatedAt)
VALUES (N'3-101-111111', N'Distribuidora Demo SA', 1, 1, 1, 1, 2, @AdminId, SYSUTCDATETIME(), SYSUTCDATETIME());

DECLARE @VerifiedId INT = SCOPE_IDENTITY();
INSERT INTO dbo.SupplierBranches (SupplierId, BranchName, ContactName, Email, Phone, AddressLine, Province, IsDefault, UpdatedAt)
VALUES (@VerifiedId, N'Sede principal', N'María Solís', N'maria@demo.cr', N'2222-1111', N'San José centro', N'San José', 1, SYSUTCDATETIME()),
       (@VerifiedId, N'Sucursal Heredia', N'Carlos Vega', N'carlos@demo.cr', N'2233-2222', N'Heredia centro',  N'Heredia',  0, SYSUTCDATETIME());

-- ... similar for PendingReview owned by an applicant, and Rejected.
```

## 3. User Story 1 — Reuse a Verified Supplier

1. Sign in as `seed-applicant-1`.
2. Create a new application (or reuse a draft).
3. Add an item.
4. Click **Agregar cotización**.
5. In the legal-ID field, type `3-101-111111`. After 250 ms, the lookup-result region updates and renders Distribuidora Demo SA with both branches as radio options. Compliance flags are read-only badges.
6. Pick **Sede principal**.
7. Fill price, currency, validity, attach a PDF.
8. Click **Guardar**.

Expected: redirected to `/Application/{appId}/Details`, the new quotation row references `Sede principal`, and the existing `SupplierScore` reviewer view (sign in as the reviewer to verify) shows score `5` with no "Pending verification" badge.

## 4. User Story 2 — Add a New Branch

1. As `seed-applicant-1`, on a new draft application, add an item and start a quotation.
2. Look up `3-101-111111`. Both branches appear.
3. Click **Agregar nueva sucursal**. The form expands to reveal `AddBranchInputViewModel` fields.
4. Fill: branch name "Sucursal Cartago", contact "Ana Mora", email, phone, address, province "Cartago".
5. Save the quotation.

Expected: a new branch row exists in `dbo.SupplierBranches` under supplier id `@VerifiedId` with `IsDefault = 0` and `CreatedByApplicantId = seed-applicant-1.Id`. The new quotation references that branch.

## 5. User Story 3 — Create a Brand-New Draft Supplier

1. As `seed-applicant-2` (different from US1/US2), on a new draft application, start a quotation.
2. Look up `3-101-999999`. The lookup returns "no encontrado", and the form exposes the new-supplier panel: `Name`, plus a default first-branch panel.
3. Fill name "Proveedor Nuevo SA" and the branch contact info.
4. Save the quotation.

Expected: one new `Suppliers` row with `VerificationStatus = Draft`, `CreatedByApplicantId = seed-applicant-2.Id`, all four admin-only flags `false`. Exactly one branch with `IsDefault = 1`.

Verify cross-applicant invisibility: sign in as `seed-applicant-1` and look up `3-101-999999`. Result: `_LookupEmpty.cshtml` partial with the offer to create a new supplier.

## 6. User Story 4 — Submit Locks Drafts and Routes to Admin

1. As `seed-applicant-2`, finish the application from US3 (add required impact, item details, etc.) and click **Enviar postulación**.

Expected:

- `dbo.Suppliers` row for `3-101-999999` now has `VerificationStatus = PendingReview`.
- Trying to navigate to `/Application/{appId}/Item/{itemId}/Supplier/{supplierId}/EditDraft` returns 403.
- Sign in as the admin user. Open `/Admin/Suppliers`. The default filter `PendingReview` lists the new supplier.

## 7. User Story 5 — Admin Verifies

1. As admin, open the `PendingReview` supplier from US4.
2. Toggle the four admin-only flags (CCSS, Hacienda, SICOP, e-invoice) on.
3. Click **Verificar**.

Expected: `Suppliers.VerificationStatus = Verified`, `VerifiedByUserId = currentAdminId`, `VerifiedAt = SYSUTCDATETIME()` at the moment of click. As `seed-applicant-1`, look up `3-101-999999` — now visible to everyone.

Test the rejection path: from a different `PendingReview` supplier (seed a fourth one if needed), click **Rechazar** without typing a reason. Expected: ModelState error shown inline. Type a reason and click. Expected: status `Rejected`, `RejectionReason` stored.

## 8. User Story 6 — Admin Edits Verified

1. As admin, open the supplier from US5. Edit the email of one of its branches.
2. Save.

Expected: as the applicant, on the next render of the relevant Application Details view, the corrected email appears.

## 9. User Story 7 — Admin Filters

1. As admin, on `/Admin/Suppliers`, switch the filter to `Verified`. Confirm only Verified suppliers list.
2. Search by partial legal ID `999999`. Confirm matching results.
3. Toggle the "Tiene cumplimientos incompletos" filter. Confirm only suppliers with at least one false admin-only flag list.

## 10. Recommendation parity (SC-003)

Run the integration test suite — specifically `MigrationTests.cs` — and confirm:

```bash
dotnet test tests/FundingPlatform.Tests.Integration --filter MigrationTests
```

Expected: all migration assertions pass; the byte-for-byte score parity check succeeds against the seeded application data.

## 11. Full E2E

Per project testing convention (CLAUDE.md):

```bash
dotnet test tests/FundingPlatform.Tests.E2E
```

All tests must pass before declaring the feature delivered.
