# Data Model: 037-applicant-companies

Phase 1 output. Defines the new `Company` aggregate, the `Application` change, and the schema. Schema is authored in the dacpac (`FundingPlatform.Database`); EF config mirrors it (Schema-First, Constitution IV).

---

## New aggregate: `Company` (Empresa)

Admin-managed company owned by exactly one applicant. One business attribute (name) plus lifecycle + audit.

| Field | Type | Notes |
|---|---|---|
| `Id` | int, identity, PK | |
| `ApplicantId` | int, required, FK → `Applicants.Id` (NO ACTION) | Owning applicant; one applicant → many companies. |
| `Name` | nvarchar(200), required | Trimmed, ≤200, non-empty. Width matches the snapshot column. |
| `ArchivedAt` | datetimeoffset(0), null | `null` ⇔ active. Soft-archive; reversible. |
| `CreatedAt` | datetime2, required, default `GETUTCDATE()` | |
| `UpdatedAt` | datetime2, required | Bumped on rename/archive/unarchive. |
| `RowVersion` | rowversion | Optimistic concurrency. |

**Domain behavior** (`Company.cs`):
- `Company(int applicantId, string name)` — sets `ApplicantId`, validates `name` (trim/required/≤200), `ArchivedAt = null`, stamps timestamps.
- `Rename(string newName)` — trim/required/≤200; no-op if equal after trim (caller suppresses audit); bumps `UpdatedAt`.
- `Archive()` — sets `ArchivedAt = UtcNow` (idempotent if already archived). **Floor check is in the service**, not here.
- `Unarchive()` — clears `ArchivedAt`. (Name-collision check on unarchive is in the service.)
- `bool IsActive => ArchivedAt is null`.

**Validation messages**: surfaced via es-CR at the controller/service boundary; entity throws `ArgumentException` with a stable reason discriminator (same `Data[ValidationReasonKey]` technique as `Application.SetCompanyName`).

**Indexes / constraints** (`dbo.Companies.sql`):
- `PK_Companies (Id)`.
- `FK_Companies_Applicants (ApplicantId)` → `Applicants(Id)` ON DELETE NO ACTION.
- `IX_Companies_ApplicantId (ApplicantId)`.
- `UX_Companies_ApplicantId_Name (ApplicantId, Name) WHERE ArchivedAt IS NULL` — filtered unique backstop for active per-applicant name uniqueness (D3).

```sql
CREATE TABLE [dbo].[Companies]
(
    [Id]          INT             IDENTITY(1,1) NOT NULL,
    [ApplicantId] INT             NOT NULL,
    [Name]        NVARCHAR(200)   NOT NULL,
    [ArchivedAt]  DATETIMEOFFSET(0) NULL,
    [CreatedAt]   DATETIME2       NOT NULL CONSTRAINT [DF_Companies_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]   DATETIME2       NOT NULL,
    [RowVersion]  ROWVERSION      NOT NULL,

    CONSTRAINT [PK_Companies] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Companies_Applicants]
        FOREIGN KEY ([ApplicantId]) REFERENCES [dbo].[Applicants]([Id]) ON DELETE NO ACTION
);
GO
CREATE NONCLUSTERED INDEX [IX_Companies_ApplicantId]
    ON [dbo].[Companies]([ApplicantId]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Companies_ApplicantId_Name]
    ON [dbo].[Companies]([ApplicantId],[Name]) WHERE [ArchivedAt] IS NULL;
GO
```

**EF config** (`CompanyConfiguration.cs`, mirrors `FundConfiguration`): `ToTable("Companies")`, key, `Name` required/maxlength 200, `ArchivedAt` optional, `CreatedAt` default sql `GETUTCDATE()`, `RowVersion` `IsRowVersion()`, the two indexes. Register via existing `ApplyConfigurationsFromAssembly`. Add `public DbSet<Company> Companies => Set<Company>();` to `AppDbContext`.

---

## Changed aggregate: `Application`

| Field | Change |
|---|---|
| `CompanyId` | **NEW** — `int?`, nullable FK → `Companies.Id` (NO ACTION). The live reference. |
| `CompanyName` | **Re-purposed** — now the **frozen name snapshot** copied from the selected company (was applicant free text). Still NVARCHAR(200) NOT NULL. |

**Domain behavior**:
- Constructor: `Application(int applicantId, int groupId, int companyId, string companyNameSnapshot)` (replaces `(applicantId, groupId, companyName)`). Sets `CompanyId`, snapshots the name via the existing trim/≤200 path.
- `SetCompany(int companyId, string nameSnapshot)` — `EnsureNotFrozen()`; sets `CompanyId` + re-copies snapshot; bumps `UpdatedAt`. Used by draft re-select (autosave). Replaces the applicant-facing `SetCompanyName`.
- Submit gate (FR-020): the submit path verifies the linked company is still active; otherwise throws an es-CR-mapped error requiring re-selection.

**Schema** (`dbo.Applications.sql`, inline — nullable, no backfill per D9):
```sql
[CompanyId]  INT  NULL,                         -- after [CompanyName]
...
CONSTRAINT [FK_Applications_Companies]
    FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies]([Id]) ON DELETE NO ACTION,
...
CREATE NONCLUSTERED INDEX [IX_Applications_CompanyId] ON [dbo].[Applications]([CompanyId]);
```

**EF config** (`ApplicationConfiguration.cs`): add `builder.Property(a => a.CompanyId);`, `builder.HasOne<Company>().WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.NoAction);`, and `builder.HasIndex(a => a.CompanyId).HasDatabaseName("IX_Applications_CompanyId");`. `CompanyName` mapping unchanged.

---

## Relationships

```
Applicant 1 ──── * Company          (Company.ApplicantId, NO ACTION)
Applicant 1 ──── * Application       (existing)
Company   0..1 ─ * Application       (Application.CompanyId nullable, NO ACTION)
Application *1 ── snapshot          (Application.CompanyName frozen copy)
```

An application's `CompanyId` and its owning `Applicant`'s companies must agree: the selected company's `ApplicantId` must equal the application's `ApplicantId` (enforced in the service/autosave/controller, FR-018).

---

## Audit (`AdminAuditEvent`)

New action constants + target type (D10):

| Action | Payload JSON |
|---|---|
| `company.create` | `{ "companyId", "applicantId", "name" }` |
| `company.rename` | `{ "companyId", "oldName", "newName" }` (no audit on equal-name no-op) |
| `company.archive` | `{ "companyId", "name" }` |
| `company.unarchive` | `{ "companyId", "name" }` |

`TargetTypeCompany = "company"`; `AdminAuditEventWriter.DeriveTarget` gains `if (eventKind.StartsWith("company.", …)) return (TargetTypeCompany, "0");`.

---

## DTOs (Application layer)

- **`CompanyDto`** (NEW): `(int Id, string Name, bool IsArchived)` — admin read surfaces.
- **`CreateUserRequest`**: + `IReadOnlyList<string> CompanyNames` (at-creation companies; ≥1 for Solicitante).
- **`UserDetailDto`**: + `IReadOnlyList<CompanyDto> Companies` (Edit-page management card source).
- **`BatchUserImportRow`**: + `string? NombreEmpresa`.
- **`CreateApplicationCommand`**: `CompanyName` → `CompanyId`.

---

## Seed data (demo / E2E)

`IdentityConfiguration.SeedUsersAsync` gains a step seeding companies for `applicant@programa-semilla.test` (e.g. two active companies: `"Acme Consulting S.A."`, `"TechCorp Ltda."`) so existing demo + E2E create flows have selectable companies. Idempotent (`!Companies.Any(c => c.ApplicantId == applicant.Id)`). Seeding **two** companies exercises the multi-company (explicit-choice) path by default; single-company tests SQL-seed a throwaway applicant or archive one.

---

## State & lifecycle

No new application state. `Company` lifecycle is binary: **Active** (`ArchivedAt IS NULL`) ⇄ **Archived**. Transitions: `Archive()` (blocked when last active — service), `Unarchive()` (blocked on active-name collision — service). The application's company link is mutable while `Draft`, frozen at `Submitted` (existing `EnsureNotFrozen`).
