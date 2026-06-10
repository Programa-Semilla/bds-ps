# Data Model: Fund (Fondo) Entity

**Feature**: 029-fund-entity | **Date**: 2026-06-10

Schema is owned by the dacpac (`src/FundingPlatform.Database`, Constitution IV). EF Core configs map to it; no EF migrations.

---

## New entity: `Fund` (Domain)

| Field | Type | Notes |
|---|---|---|
| `Id` | int (identity) | PK |
| `Name` | string ≤120 | required, unique (case-insensitive) |
| `Description` | string ≤2000 | required, non-empty |
| `Status` | `FundStatus` (Active=0, Archived=1) | default Active |
| `RegulationBlobKey` | string? ≤1024 | spec-014 ObjectKey; null = no regulation |
| `RegulationFileName` | string? ≤260 | original filename |
| `RegulationContentType` | string? ≤100 | `application/pdf` |
| `RegulationSizeBytes` | long? | bytes |
| `RegulationUploadedAtUtc` | DateTime? | set on upload/replace |
| `RegulationUploadedByUserId` | string? ≤450 | actor |
| `CreatedAt` | DateTimeOffset(0) | default `SYSUTCDATETIME()` |
| `RowVersion` | rowversion | optimistic concurrency |
| `Processes` | nav → `IReadOnlyCollection<Process>` | one-to-many |

**Behavior methods** (Rich Domain Model):
- `static Fund Create(string name, string description)` → validates, Status=Active.
- `Rename(string)`, `EditDescription(string)` — validate, guarded against Archived (no edits while archived except lifecycle).
- `Archive()` / `Reactivate()` — toggle Status with idempotency guards.
- `SetRegulation(blobKey, fileName, contentType, size, uploadedBy, now)` — sets/replaces the regulation reference (caller already stored the blob).
- `RemoveRegulation()` — clears regulation columns (caller deletes the blob).
- `bool HasRegulation => RegulationBlobKey is not null`.

**Invariants**: Name required + unique (DB `UX_Funds_Name`); Description required; regulation columns are all-or-nothing (set together / cleared together).

### dacpac: `dbo.Funds.sql`
```sql
CREATE TABLE [dbo].[Funds]
(
    [Id]                        INT            IDENTITY(1,1) NOT NULL,
    [Name]                      NVARCHAR(120)  NOT NULL,
    [Description]               NVARCHAR(2000) NOT NULL,
    [Status]                    TINYINT        NOT NULL CONSTRAINT [DF_Funds_Status] DEFAULT (0),
    [RegulationBlobKey]         NVARCHAR(1024) NULL,
    [RegulationFileName]        NVARCHAR(260)  NULL,
    [RegulationContentType]     NVARCHAR(100)  NULL,
    [RegulationSizeBytes]       BIGINT         NULL,
    [RegulationUploadedAtUtc]   DATETIME2(3)   NULL,
    [RegulationUploadedByUserId] NVARCHAR(450) NULL,
    [CreatedAt]                 DATETIMEOFFSET(0) NOT NULL CONSTRAINT [DF_Funds_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]                ROWVERSION     NOT NULL,
    CONSTRAINT [PK_Funds] PRIMARY KEY CLUSTERED ([Id])
);
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Funds_Name] ON [dbo].[Funds] ([Name]);
GO
```
Case-insensitive uniqueness is provided by the DB's default collation (SQL Server default collations are case-insensitive); the service additionally trims and pre-checks for a friendly es-CR message.

---

## Modified: `Process`

- Add `FundId int` (required) + `Fund` nav.
- `Process.Create` factory signature gains `fundId`; add `SetFund(int fundId)` for admin reassignment (FR-009), guarded against Closed.

### dacpac delta: `dbo.Processes.sql`
```sql
[FundId] INT NOT NULL,                       -- new column
...
CONSTRAINT [FK_Processes_Funds] FOREIGN KEY ([FundId]) REFERENCES [dbo].[Funds]([Id]) ON DELETE NO ACTION
GO
CREATE NONCLUSTERED INDEX [IX_Processes_FundId] ON [dbo].[Processes]([FundId]);
```
EF (`ProcessConfiguration`): `builder.HasOne(p => p.Fund).WithMany(f => f.Processes).HasForeignKey(p => p.FundId).OnDelete(DeleteBehavior.NoAction);`

---

## Modified: `Application` — authoritative anchor (FR-017)

- Add `GroupId int` (required) + `Group` nav.
- Derivations: `Process` = `Group.Process`; `Fund` = `Group.Process.Fund`.
- Add `bool IsFrozen` (service-fed from `Group.Process.Fund.Status == Archived`); mutating domain methods throw `FundArchivedException` when frozen (FR-021).

### dacpac delta: `dbo.Applications.sql`
```sql
[GroupId] INT NOT NULL,                       -- new column
...
CONSTRAINT [FK_Applications_Groups] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[Groups]([Id]) ON DELETE NO ACTION
GO
CREATE NONCLUSTERED INDEX [IX_Applications_GroupId] ON [dbo].[Applications]([GroupId]);
```
EF (`ApplicationConfiguration`): `builder.HasOne(a => a.Group).WithMany().HasForeignKey(a => a.GroupId).OnDelete(DeleteBehavior.NoAction);`

---

## Relationships (after)

```
Fund (1) ──< Process (N) ──< Group (N) ──< Application (N)        [anchor: Application.GroupId]
                 │
                 └── ProcessPlantilla (1:1)   ← Plantilla validation resolved via Application.Group.Process.Plantilla
Fund (0..1) ── RegulationBlob (spec-014 object storage, columns on Fund)
```

## State transitions

**Fund**: `Active ⇄ Archived` (Archive / Reactivate). Archived ⇒ (a) excluded from Process-create Fund selector and application-create Group selector; (b) anchored applications excluded from non-admin reads (FR-020) and frozen against mutation (FR-021).

**Application (freeze overlay)**: orthogonal to the existing Draft→Submitted→… state machine. `IsFrozen` is a read-through of the anchored Fund's status; it gates mutations and non-admin visibility without changing the persisted `State`.

## Derived queries enabled

- Processes of a Fund: `Processes.Where(p => p.FundId == id)`.
- Applications of a Fund: `Applications.Where(a => a.Group.Process.FundId == id)`.
- Fund of an application (reports/CSV): `a.Group.Process.Fund.Name`.
- Non-admin visibility: `ExcludeDeleted(q).Pipe(ExcludeArchivedFund)`.

## Audit (`AdminAuditEvent`) — new constants

`fund.create`, `fund.edit`, `fund.archive`, `fund.reactivate`, `fund.regulation.set`, `fund.regulation.remove` with target type `fund`.

## Seed (dacpac post-deploy, idempotent MERGE)

1. Upsert `Fondo General` (Active) → capture `@FundId`.
2. `UPDATE dbo.Processes SET FundId=@FundId WHERE FundId IS NULL` (and seed new Processes with it).
3. Ensure seed applications carry a valid `GroupId` (seed-only; no production data).
