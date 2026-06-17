# Quickstart: 037-applicant-companies

How to build, run, and verify the controlled-company-selection feature.

## Build

```bash
dotnet build FundingPlatform.slnx
```

Schema changes live in `FundingPlatform.Database` (dacpac): the new `dbo.Companies` table and the nullable `Applications.CompanyId` column/FK. The AppHost auto-deploys the dacpac at startup (run mode).

## Run (dev)

```bash
dotnet run --project src/FundingPlatform.AppHost
```

Demo applicant `applicant@programa-semilla.test` / `Demo123!` is seeded with **two** active companies (`Acme Consulting S.A.`, `TechCorp Ltda.`) so the multi-company selection path is exercisable out of the box. Admin `demo-admin@programa-semilla.test` / `Demo123!` manages companies under `/Admin/Users/{id}/Edit`.

## Manual verification (maps to success criteria)

1. **Multi-company select (FR-013 / SC-003)** — as the demo applicant, start a new application (`/Application/Create`): the company `<select>` shows both companies, no default, placeholder `— Seleccione una empresa —`; creation is blocked until one is chosen.
2. **Single-company auto-select (FR-012 / SC-002)** — archive one of the demo applicant's companies via admin Edit, then start a new application: the remaining company is auto-selected (read-only), creation proceeds without a choice.
3. **Zero-company block (FR-014)** — for a fresh applicant with no companies, `/Application/Create` blocks with an es-CR message directing to an admin.
4. **History preservation (FR-016 / SC-005)** — create an application under a company, then rename that company in admin Edit; the existing application still shows the old name (Details/Review), a new application shows the new name.
5. **Draft re-select (FR-015)** — on a `Draft` application's editor, change the selected company; the displayed snapshot updates. Submit, then confirm the company is frozen.
6. **Admin management (FR-005–008 / SC-006)** — on `/Admin/Users/{id}/Edit` (Solicitante): add, rename, archive, unarchive companies; archiving the last active company is blocked.
7. **Ownership guard (FR-018/019 / SC-004)** — POST `/Application/Create` with a `CompanyId` belonging to another applicant (or archived) is rejected server-side; no application is persisted.
8. **Batch (FR-009 / SC-007)** — download `/Admin/Users/Batch/Template`; confirm the trailing `Nombre de la empresa` column; import a file and confirm each created applicant has exactly that company.

## Tests

```bash
# Unit — Company entity invariants, CSV column (Count=11), batch reasons
dotnet test tests/FundingPlatform.Tests.Unit

# Integration (real DB) — CompanyAdministrationService (add/rename/archive/unarchive + floor + uniqueness),
# create-user-with-companies, create-application-with-company, batch-with-company-column
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (filtered — the delivery bar). Run the new/affected classes:
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~ApplicantCompanySelection|FullyQualifiedName~AdminCompanyManagement|FullyQualifiedName~CompanyHistoryPreservation|FullyQualifiedName~BatchUserCompany"
```

**Delivery bar**: the four filtered E2E classes above must be personally executed and green. The full ~30-min suite runs only if a cross-cutting ripple (the `Application` constructor change / autosave field-key swap) is suspected to affect other create/submit tests — run it on explicit request or if filtered runs reveal breakage in shared base helpers.

## Key ripple to watch

The `Application` constructor signature change (`companyName` → `companyId, companyNameSnapshot`) and the autosave field-key swap (`CompanyName` → `CompanyId`) touch shared test infrastructure (unit `new Application(...)` call sites, any E2E that fills the old free-text company input). Update those call sites as a foundational task; if filtered E2E for application-create/submit suites (outside this feature) go red, that's the cause.
