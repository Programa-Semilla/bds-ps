# Data Model: Fund Process Reception Windows (044)

## New aggregate: `ProcessEvent`

A configurable calendar item belonging to a Process. The reception-window type controls submission availability; other types are reserved (schema-only, US5).

### Entity — `FundingPlatform.Domain/Entities/ProcessEvent.cs`

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Identity PK |
| `ProcessId` | `int` | FK → `Processes` (NO ACTION) |
| `EventType` | `ProcessEventType` (enum, byte-backed) | `ReceptionWindow=0` only behavioral type this slice |
| `Name` | `string` (≤120) | required, trimmed |
| `Description` | `string?` (≤500) | optional |
| `StartUtc` | `DateTimeOffset` | absolute UTC instant |
| `EndUtc` | `DateTimeOffset` | absolute UTC instant; invariant `EndUtc > StartUtc` |
| `ControlsSubmissionAvailability` | `bool` | `true` for reception windows |
| `ApplicantFacingMessage` | `string?` (≤500) | optional copy surfaced in the notice |
| `IsActive` | `bool` | inactive windows ignored by gating + display |
| `DisplayOrder` | `int` | admin ordering |
| `CreatedAt` | `DateTimeOffset` | `SYSUTCDATETIME()` default |
| `CreatedByUserId` | `string?` | audit |
| `UpdatedAt` | `DateTimeOffset?` | audit |
| `UpdatedByUserId` | `string?` | audit |
| `RowVersion` | `byte[]` | optimistic concurrency |

**Domain behavior** (Rich Domain Model):
- `ProcessEvent.CreateReceptionWindow(processId, name, startUtc, endUtc, applicantMessage, description, displayOrder, createdBy)` — factory; throws `ArgumentException` if `endUtc <= startUtc` or name blank/too long; sets `EventType=ReceptionWindow`, `ControlsSubmissionAvailability=true`, `IsActive=true`.
- `Update(name, startUtc, endUtc, applicantMessage, description, displayOrder, updatedBy)` — re-validates `endUtc > startUtc`.
- `Activate(updatedBy)` / `Deactivate(updatedBy)` — toggles `IsActive` (no-op if unchanged).
- `ComputeState(DateTimeOffset nowUtc) → ReceptionWindowState` (`Upcoming` / `OpenNow` / `Closed`) — pure, for the admin badge.

### Enum — `FundingPlatform.Domain/Enums/ProcessEventType.cs`
```
ReceptionWindow = 0   // behavioral this slice
Informational   = 1   // reserved (US5, schema-only)
Deadline        = 2   // reserved
Milestone       = 3   // reserved
```

## Pure evaluation value objects — `FundingPlatform.Domain`

### `ReceptionWindowSnapshot` (record)
`(int Id, string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? ApplicantFacingMessage)` — minimal projection of an **active** reception window passed to the evaluator (decouples evaluation from EF).

### `ReceptionAvailability` (result)
```
enum SubmissionAvailabilityStatus { Unrestricted, Open, BeforeFirstWindow, BetweenWindows, AllWindowsClosed }

record ReceptionAvailability(
    SubmissionAvailabilityStatus Status,
    ReceptionWindowSnapshot? ActiveWindow,    // set when Open: drives close countdown
    ReceptionWindowSnapshot? NextWindow,      // set when BeforeFirstWindow/BetweenWindows: drives open date
    ReceptionWindowSnapshot? LastClosedWindow // set when AllWindowsClosed: drives last-closed date
)
{
    bool CanSubmit => Status is Unrestricted or Open;
    bool CanCreateDraft => Status != AllWindowsClosed;   // FR-014
}
```

### `ReceptionWindowEvaluation.Evaluate(IReadOnlyList<ReceptionWindowSnapshot> windows, DateTimeOffset nowUtc) → ReceptionAvailability`
Pure static. Logic:
- `windows` empty → `Unrestricted`.
- any window with `Start ≤ now < End` → `Open` (ActiveWindow = that window; if several overlap, the one with the latest `End`).
- else `NextWindow` = earliest window with `Start > now`. If present: `BeforeFirstWindow` when no window has `End ≤ now`, otherwise `BetweenWindows`.
- else (all windows have `End ≤ now`) → `AllWindowsClosed` (LastClosedWindow = the one with the latest `End`).

## Modified entity: `Process`

- **Removed**: `SolicitudWindowDays` property (`Process.cs:32`), its `OverrideStageWindow` switch arm (`:130`) and `OverrideForStage` arm (`:150`). `OverrideStageWindow`/`OverrideForStage` keep `Revision`/`Facturacion` arms only.
- **Added navigation**: `ICollection<ProcessEvent> Events` (one Process → many events).
- No global start/end dates added (spec decision).

## Schema (dacpac)

### `src/FundingPlatform.Database/Tables/dbo.ProcessEvents.sql`
```sql
CREATE TABLE [dbo].[ProcessEvents]
(
    [Id]                             INT              IDENTITY(1,1) NOT NULL,
    [ProcessId]                      INT              NOT NULL,
    [EventType]                      TINYINT          NOT NULL CONSTRAINT [DF_ProcessEvents_EventType] DEFAULT (0),
    [Name]                           NVARCHAR(120)    NOT NULL,
    [Description]                    NVARCHAR(500)    NULL,
    [StartUtc]                       DATETIMEOFFSET(0) NOT NULL,
    [EndUtc]                         DATETIMEOFFSET(0) NOT NULL,
    [ControlsSubmissionAvailability] BIT              NOT NULL CONSTRAINT [DF_ProcessEvents_Controls] DEFAULT (0),
    [ApplicantFacingMessage]         NVARCHAR(500)    NULL,
    [IsActive]                       BIT              NOT NULL CONSTRAINT [DF_ProcessEvents_IsActive] DEFAULT (1),
    [DisplayOrder]                   INT              NOT NULL CONSTRAINT [DF_ProcessEvents_DisplayOrder] DEFAULT (0),
    [CreatedAt]                      DATETIMEOFFSET(0) NOT NULL CONSTRAINT [DF_ProcessEvents_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [CreatedByUserId]                NVARCHAR(450)    NULL,
    [UpdatedAt]                      DATETIMEOFFSET(0) NULL,
    [UpdatedByUserId]                NVARCHAR(450)    NULL,
    [RowVersion]                     ROWVERSION       NOT NULL,
    CONSTRAINT [PK_ProcessEvents] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ProcessEvents_Processes]
        FOREIGN KEY ([ProcessId]) REFERENCES [dbo].[Processes]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_ProcessEvents_EndAfterStart] CHECK ([EndUtc] > [StartUtc])
);
GO
CREATE NONCLUSTERED INDEX [IX_ProcessEvents_ProcessId]
    ON [dbo].[ProcessEvents] ([ProcessId]) INCLUDE ([IsActive], [EventType]);
```
> `CK_EndAfterStart` is a defense-in-depth backstop; the user-facing es-CR rejection (FR-003) is enforced earlier in the domain factory/service.

### `dbo.Processes.sql` — remove
```diff
-    [SolicitudWindowDays]    INT NULL,
```

### `src/FundingPlatform.Database/PostDeployment/07_DropSolicitudWindowDays.sql` (new, idempotent)
```sql
IF COL_LENGTH('dbo.Processes', 'SolicitudWindowDays') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[Processes] DROP COLUMN [SolicitudWindowDays];
END
GO
DELETE FROM [dbo].[SystemConfigurations] WHERE [Key] = N'Stage.Solicitud.WindowDays';
GO
```
> `SolicitudWindowDays` has no default constraint to drop first (it is nullable, no `DF_`).

## EF configuration — `Infrastructure/Persistence/Configurations/ProcessEventConfiguration.cs`
Mirror `FundConfiguration`:
- `ToTable("ProcessEvents")`, key `Id` `ValueGeneratedOnAdd`.
- `EventType` `.HasConversion<byte>().IsRequired()` **(mandatory — InMemory-vs-SQL TINYINT gotcha)**.
- `Name` max 120 required; `Description`/`ApplicantFacingMessage` max 500.
- `StartUtc`/`EndUtc` required; `CreatedAt` `HasDefaultValueSql("SYSUTCDATETIME()")`; `RowVersion` `.IsRowVersion()`.
- `HasOne(Process).WithMany(p => p.Events).HasForeignKey(ProcessId).OnDelete(NoAction)`.
- `HasIndex(ProcessId)`.
Remove `builder.Property(p => p.SolicitudWindowDays)` from `ProcessConfiguration.cs`.

## Test-data touchpoints
- E2E `/Account/SeedUser` / process seeding gains a seam to create reception windows relative to `UtcNow` (open / upcoming / all-closed scenarios). No clock freeze.
- Unit tests for `ReceptionWindowEvaluation.Evaluate` cover: empty, open, boundary `now==Start` (open) / `now==End` (closed), before-first, between, all-closed, overlap (latest-End wins).
