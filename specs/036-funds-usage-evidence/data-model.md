# Phase 1 Data Model: Funds-Usage Evidence Stage

## Entity: `FundsUsageEvidence` (Domain aggregate root)

One row per uploaded evidence file on an executed application.

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` (identity) | PK |
| `ApplicationId` | `int` | FK → `dbo.Applications(Id)`. The owning application. |
| `UploadedByUserId` | `string` (≤450) | FK → `dbo.AspNetUsers(Id)`. The reviewer/admin who uploaded. |
| `OriginalFileName` | `string` (≤500) | As supplied by the browser; sanitized for display. |
| `BlobKey` | `string` (≤1024) | Canonical `ObjectKey` string in the `funds-usage-evidence` container. |
| `FileSize` | `long` | Bytes (> 0). |
| `ContentType` | `string` (≤100) | Resolved/validated content type. |
| `Note` | `string?` (≤250) | Optional. `null`/empty allowed. |
| `UploadedAt` | `DateTime` (UTC) | Set on creation. |
| `RowVersion` | `byte[]` (rowversion) | Optimistic concurrency (concurrent-delete edge). |

### Domain behavior (Rich Domain Model — Constitution II)

- `static FundsUsageEvidence CreateForExecutedApplication(Application application, string uploadedByUserId, string originalFileName, string blobKey, long fileSize, string contentType, string? note)`
  - Throws `InvalidOperationException` if `application.State != ApplicationState.AgreementExecuted` (FR-001).
  - Validates: non-empty uploader, non-empty file name, non-empty blob key, `fileSize > 0`, `note` length ≤ 250 (trim; empty → `null`).
  - Sets `ApplicationId = application.Id`, `UploadedAt = DateTime.UtcNow`.
- `void EditNote(string? note)` — trims, empty → `null`, rejects > 250 chars with `InvalidOperationException` (FR-006).
- Construction is via the factory only (private parameterless ctor for EF; no public setter exposure).

### Relationships

- `FundsUsageEvidence` → `Application` (many-to-one). The application is **not** burdened with a navigation
  collection (research D2); evidence is queried flat by `ApplicationId`.
- `FundsUsageEvidence` → `ApplicationUser` via `UploadedByUserId` (display name resolved at read time).

### Lifecycle

No status enum, no transitions. The row exists from upload until deletion. The owning application stays in
`AgreementExecuted` throughout (FR-012).

## Table DDL: `dbo.FundsUsageEvidence.sql` (Database project)

```sql
CREATE TABLE [dbo].[FundsUsageEvidence]
(
    [Id]                INT            IDENTITY(1,1) NOT NULL,
    [ApplicationId]     INT            NOT NULL,
    [UploadedByUserId]  NVARCHAR(450)  NOT NULL,
    [OriginalFileName]  NVARCHAR(500)  NOT NULL,
    [BlobKey]           NVARCHAR(1024) NOT NULL,
    [FileSize]          BIGINT         NOT NULL,
    [ContentType]       NVARCHAR(100)  NOT NULL,
    [Note]              NVARCHAR(250)  NULL,
    [UploadedAt]        DATETIME2(3)   NOT NULL CONSTRAINT [DF_FundsUsageEvidence_UploadedAt] DEFAULT (GETUTCDATE()),
    [RowVersion]        ROWVERSION     NOT NULL,

    CONSTRAINT [PK_FundsUsageEvidence] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_FundsUsageEvidence_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_FundsUsageEvidence_AspNetUsers]
        FOREIGN KEY ([UploadedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_FundsUsageEvidence_FileSize_Positive] CHECK ([FileSize] > 0)
);
GO

CREATE NONCLUSTERED INDEX [IX_FundsUsageEvidence_ApplicationId]
    ON [dbo].[FundsUsageEvidence] ([ApplicationId]);
GO
```

**FK cascade rationale**: `ApplicationId` is `ON DELETE NO ACTION` (not CASCADE). Applications are
**soft-deleted** (`Application.SoftDelete`), never hard-deleted, so there is no orphan risk, and NO ACTION
avoids any multiple-cascade-path publish failure (the spec-029/035 lesson). `UploadedByUserId` is NO ACTION,
matching `SignedUploads`. This is a **greenfield additive** table — no DEFAULT-placeholder/backfill dance is
needed (unlike the spec-029 `Funds`/anchor columns), since it is a brand-new table with no existing rows.

## EF configuration: `FundsUsageEvidenceConfiguration.cs` (Infrastructure)

- Map to `dbo.FundsUsageEvidence`; `Id` identity PK.
- `OriginalFileName` (500), `BlobKey` (1024), `ContentType` (100), `Note` (250, nullable).
- `RowVersion` as `IsRowVersion()`.
- `UploadedAt` mapped; backing fields for private setters (no public mutation).
- Relationship: `HasOne<Application>().WithMany().HasForeignKey(e => e.ApplicationId)` (no nav on Application).
- Register `DbSet<FundsUsageEvidence>` on `AppDbContext` + apply configuration.

## Audit keys (added to `AdminAuditEvent`)

```csharp
public const string FundsEvidenceUploaded   = "funds_evidence.uploaded";
public const string FundsEvidenceNoteEdited = "funds_evidence.note_edited";
public const string FundsEvidenceDeleted    = "funds_evidence.deleted";
public const string TargetTypeFundsEvidence = "funds_evidence";
```

Payload JSON shape: `{ "applicationId": <int>, "evidenceId": <int|null>, "fileName": "<string>" }`.
`AdminAuditEventWriter` target routing for the `funds_evidence.` prefix must be added (research D6).

## Storage category (added to `StorageCategoriesOptions`)

```csharp
public StorageCategoryOptions FundsUsageEvidence { get; set; } = new()
{
    MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes20Mib,   // 20 MiB (FR-005)
    ServingMode  = ServingMode.BackendStream,                 // download via backend stream (FR-009)
};
// + For(FileCategory.FundsUsageEvidence) => FundsUsageEvidence;
```

`FileCategory.FundsUsageEvidence` → container `funds-usage-evidence` (added to `ContainerName()` +
`AllContainerNames`). Defaults are compile-time, so no `appsettings`/AppHost change is strictly required;
an env can still override `Storage:Categories:FundsUsageEvidence:MaxSizeBytes`.

## ObjectKey shape

`ObjectKey.Build(FileCategory.FundsUsageEvidence, ownerSegment: "application/{applicationId}", entityId: "{applicationId}", deterministicSuffix: "{guid-or-timestamp}", extension)` →
`funds-usage-evidence/application/{id}/{id}/{suffix}.{ext}`. Suffix is per-file (multiple files per application),
so it must be unique per upload (e.g. a new GUID) — evidence items do not overwrite each other (FR-003).
