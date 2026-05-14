# Phase 1 — Data Model

**Feature**: Feedback Session May-13 (021) | **Date**: 2026-05-14

Schema source of truth = `FundingPlatform.Database` (dacpac). All shapes below ship as `.sql` files; EF Core mappings in `Infrastructure/Persistence/Configurations` track them with `Code First` model annotations.

---

## New entities

### `Process`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `INT IDENTITY(1,1)` | PK | |
| `Name` | `NVARCHAR(120)` | NOT NULL | Free text (e.g. *Crocus 2025*). |
| `Status` | `TINYINT` | NOT NULL DEFAULT 0 | Enum `ProcessStatus { Active = 0, Closed = 1 }`. |
| `SolicitudWindowDays` | `INT NULL` | | Per-Process override of platform default. |
| `RevisionWindowDays` | `INT NULL` | | Per-Process override. |
| `FacturacionWindowDays` | `INT NULL` | | Per-Process override. |
| `CreatedAt` | `DATETIME2(0)` | NOT NULL DEFAULT SYSUTCDATETIME() | |
| `ClosedAt` | `DATETIME2(0) NULL` | | Set when `Status` transitions to Closed. |
| `RowVersion` | `ROWVERSION` | | Optimistic concurrency. |

- **Indexes**: `UX_Processes_Name` on `Name` (catalog uniqueness within active set is enforced at the application layer; reuse across closed cycles allowed).
- **Behavior** (`Process.cs`):
  - `Close()` — guards: no Active Applications; sets `Status = Closed`, `ClosedAt = now`; raises `ProcessClosed` audit event.
  - `OverrideStageWindow(StageKind, int? days)` — null = revert to platform default; raises `StageWindowOverridden`.
  - `Reopen()` — disallowed in 021 (out-of-scope).

### `Plantilla`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `INT IDENTITY` | PK | |
| `Name` | `NVARCHAR(120)` | NOT NULL | e.g. *PlantillaMVP-v1*. |
| `MinimumQuotationsPerItem` | `INT` | NOT NULL DEFAULT 3 | |
| `RequiredFieldFlags` | `BIGINT` | NOT NULL DEFAULT 0 | Bitfield mapping to `RequiredFieldKind` enum (FirstName, Phone, Address, …). |
| `IsArchived` | `BIT` | NOT NULL DEFAULT 0 | Soft-delete for base Plantillas. |
| `CreatedAt` | `DATETIME2(0)` | NOT NULL DEFAULT SYSUTCDATETIME() | |
| `RowVersion` | `ROWVERSION` | | |

- **Joins**: `PlantillaImpactTemplates` (many-to-many to `ImpactTemplates`).
- **Behavior**:
  - `AssignTo(Process)` — validates ≥ 1 ImpactTemplate attached, no existing `ProcessPlantilla`; creates `ProcessPlantilla` snapshot (`Plantilla.ToSnapshot()`); raises `PlantillaAssignedToProcess`.
  - `Edit(...)` — never propagates into already-assigned `ProcessPlantilla` rows (FR-004).
  - `Detach(force = false)` — blocked when used by Applications; force-detach raises `PlantillaForceDetached` audit + requires admin reason.

### `ProcessPlantilla`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `INT IDENTITY` | PK | |
| `ProcessId` | `INT` | FK → `Processes.Id`, UNIQUE | One-to-one with Process (OQ-1 resolution). |
| `SourcePlantillaId` | `INT` | FK → `Plantillas.Id` | Historical pointer; payload below is the source of truth at read time. |
| `MinimumQuotationsPerItem` | `INT` | NOT NULL | Snapshot copy. |
| `RequiredFieldFlags` | `BIGINT` | NOT NULL | Snapshot copy. |
| `ImpactTemplateIdsCsv` | `NVARCHAR(2000)` | NOT NULL | Comma-separated `ImpactTemplate.Id`s — snapshot list. Stored as CSV (not FK) so deleting a base ImpactTemplate does not corrupt the snapshot. |
| `AssignedAt` | `DATETIME2(0)` | NOT NULL DEFAULT SYSUTCDATETIME() | |

- **Behavior**: Immutable except for force-detach (sets a soft-delete flag if introduced later).

### `Province`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `INT IDENTITY` | PK | |
| `Code` | `CHAR(2)` | NOT NULL UNIQUE | CR INE codes 01..07. |
| `Name` | `NVARCHAR(40)` | NOT NULL | *San José*, *Alajuela*, … |

- 7 rows seeded via PostDeployment script.

### `Canton`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `INT IDENTITY` | PK | |
| `ProvinceId` | `INT` | FK → `Provinces.Id` | |
| `Code` | `CHAR(5)` | NOT NULL UNIQUE | `0101` + canton index (e.g. *San José/Acosta* = `0101_05`). |
| `Name` | `NVARCHAR(60)` | NOT NULL | |

- ~82 rows seeded; idempotent MERGE in PostDeployment.

### `PasswordResetToken`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `BIGINT IDENTITY` | PK | |
| `UserId` | `NVARCHAR(450)` | FK → `AspNetUsers.Id`, INDEX | |
| `TokenHash` | `VARBINARY(64)` | NOT NULL | SHA-256 of the dispatched token; raw token never persisted. |
| `IssuedAt` | `DATETIME2(0)` | NOT NULL DEFAULT SYSUTCDATETIME() | |
| `ExpiresAt` | `DATETIME2(0)` | NOT NULL | `IssuedAt + 60 minutes`. |
| `ConsumedAt` | `DATETIME2(0) NULL` | | Single-use enforcement. |

- **Behavior** (domain): `Consume()` — guards: not consumed, not expired; sets `ConsumedAt`.

---

## Modified entities

### `Group`

- New column: `ProcessId INT NOT NULL FK → Processes.Id`.
- Migration: seeded *"Migración inicial"* Process row created in PostDeployment; existing `Groups` rows updated to that ProcessId.

### `Application`

- New columns:
  - `PublicCode CHAR(9) NOT NULL UNIQUE` (8 alphanumerics + `-`; regex `^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$`).
  - `ImpactTemplateId INT NULL` FK → `ImpactTemplates.Id` (nullable until applicant picks it on first save; non-nullable for state `>= Submitted` via domain guard).
  - `RemindersSentMask TINYINT NOT NULL DEFAULT 0` (bits: 0x1 = T-72h sent, 0x2 = T-24h sent, 0x4 = expiry sent).
  - `StageEnteredAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()` (reset whenever the stage transitions).
- Soft-delete column (`DeletedAt`) already exists in current schema — confirm before plan execution; if missing, add `DeletedAt DATETIME2(0) NULL`.
- Existing FK to `Groups.Id` preserved.

### `Item`

- Drop column `ImpactId` (FR-005 + NFR-001 — no production data).
- All read paths that previously joined Item → Impact route through Application instead.

### `ImpactParameterValue`

- Re-parent FK: was `ImpactId → Impacts.Id`; becomes `ApplicationId → Applications.Id`.
- Drop legacy `dbo.Impacts` table after FK re-target completes (no other references).

### `SupplierBranch`

- New columns:
  - `ContactPersonName NVARCHAR(120) NULL`.
  - `ProvinceId INT NULL` FK → `Provinces.Id`.
  - `CantonId INT NULL` FK → `Cantons.Id`.
- Domain guard: when both Province and Cantón are non-null, Cantón.ProvinceId must equal ProvinceId.

### `ApplicationUser` (`AspNetUsers`)

- New column: `CodigoPersonal NVARCHAR(40) NULL` (admin-set, applicant read-only on profile).

### `SystemConfiguration`

- New rows seeded:
  - `Stage.Solicitud.WindowDays = 14`
  - `Stage.Revision.WindowDays = 10`
  - `Stage.Facturacion.WindowDays = 30`
  - `Public.Landing.Reglamento.StorageKey = (null)` (set when admin uploads).
  - `Public.Landing.Ejemplo.StorageKey = (null)`.

### `AdminAuditEvent`

- New event kinds (string discriminators):
  - `ProcessCreated`, `ProcessClosed`, `ProcessStageWindowOverridden`
  - `PlantillaAssignedToProcess`, `PlantillaForceDetached`
  - `SupplierAdminDeniedAccess`
- No schema change beyond seeding the discriminator strings (the existing column is `NVARCHAR(60)`).

---

## Value objects

### `PublicCode` (Domain.ValueObjects)

- Wraps a `string` validated against `^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$`.
- Factory `PublicCode.Generate(IPublicCodeGenerator)` produces a fresh code with retry on DB-side UNIQUE collision (R-1 in research.md).

### `Impact` (Domain.ValueObjects)

- Lightweight projection: `(ImpactTemplate, IReadOnlyList<ImpactParameterValue>)`.
- `Application.SetImpact(impact)` validates `ImpactTemplate.Id ∈ ProcessPlantilla.ImpactTemplateIds`.

---

## Relationships

```
Process 1───* Group 1───* Applicant 1───* Application *───1 ImpactTemplate
   │           │                           │              ↑ (snapshot list on ProcessPlantilla)
   │           │                           *
   │           │                       ApplicationImpactParameterValue
   │           │                           │
   │           │                       (was ImpactParameterValue, re-parented)
   │           │
   │           1
   *           │
ProcessPlantilla  
       1───────╯
       Plantilla *───* ImpactTemplate

SupplierBranch *───1 Province 1───* Canton
SupplierBranch *───1 Canton ───→ Province (validated equal to SupplierBranch.ProvinceId)

ApplicationUser 1───* PasswordResetToken

AdminAuditEvent (existing, extended with new event kinds)
```

---

## Validation rules

| Rule | Where enforced | FR / SC link |
|------|----------------|--------------|
| `PublicCode` regex on persist | Domain (`PublicCode` value object) + DB CHECK constraint | FR-008 |
| `Process.Close()` blocks if active Applications exist | Domain | OQ-2 |
| `Application.Submit()` requires ≥ 1 Item, Impact set, all RequiredFieldFlags filled, stage window not closed | Domain + autosave projection | FR-006, FR-017 |
| `Plantilla.AssignTo()` requires ≥ 1 ImpactTemplate attached | Domain | FR-003, Edge Cases |
| `ProcessPlantilla` is one-per-Process | DB UNIQUE on `ProcessId` | OQ-1 |
| `SupplierBranch.Cantón.ProvinceId = SupplierBranch.ProvinceId` | Domain + persistence guard | FR-014 |
| `PasswordResetToken.Consume()` rejects expired / consumed tokens | Domain | FR-028, SC-009 |
| Dashboard queries exclude soft-deleted Applications | `IApplicationQueryFilter.ExcludeDeleted` (single helper) | FR-021, SC-011 |
| `Supplier.IsCompliant` read live, no per-Application copy | Application read paths | FR-010 |
| `SupplierAdmin` role denied on non-supplier admin routes (403 + audit) | `SupplierAdminDeniedAttribute` filter | FR-007, SC-006 |

---

## State transitions

### `Application` state

- `Borrador → Submitted` — guard chain: `RequiredFieldsComplete`, `HasImpact`, `HasAtLeastOneItem`, `EachItemHasMinimumQuotations`, `StageWindowOpen`.
- `Submitted → InReview / Approved / Rejected` — existing transitions; stage-window guard applies.
- `Any → SoftDeleted (DeletedAt set)` — admin-only; resets dashboard surfaces (FR-021).
- Stage entry timestamp `StageEnteredAt` updated on every transition that crosses a stage boundary.

### `Process` state

- `Active → Closed` — guard: no `Status ∈ {Borrador, Submitted, InReview, Signing}` Applications attached via Groups. FundingAgreements freeze (OQ-2).

### `PasswordResetToken` state

- Created → Consumed (single use, set on successful password reset)
- Created → Expired (TTL passed; lazy-evaluated at read).
