# Data Model: Spec 038

## Enums (Domain) — stored as `TINYINT` via `HasConversion<byte?>()`

Numeric codes follow the **source order in spec §13**, starting at 1. `null` = "sin revisar". Verbatim
Spanish labels live in `RegulatoryStatusLabels` (Application/Web), never in the DB.

### `HaciendaStatus : byte`
| Code | Verbatim label | Scoring (slice B, not here) |
|---|---|---|
| 1 | `sin inscripción` | 1 |
| 2 | `al día` | **2** |
| 3 | `estado moroso` | 1 |
| 4 | `cobro administrativo` | 1 |
| 5 | `desinscrito al día` | 1 |
| 6 | `sin información` | 1 |
| 7 | `desinscrito moroso` | 1 |
| 8 | `desinscrito de oficio` | 1 |

### `CcssStatus : byte`
| Code | Verbatim label |
|---|---|
| 1 | `sin inscripción` |
| 2 | `al día` |
| 3 | `estado moroso` |
| 4 | `cobro administrativo` |
| 5 | `estado inactivo / al día` |
| 6 | `estado inactivo / moroso` |
| 7 | `sin información` |
| 8 | `cobro judicial` |

### `SicopStatus : byte`
| Code | Verbatim label |
|---|---|
| 1 | `inhabilitación` |
| 2 | `sin sanciones` |
| 3 | `sin suscripción` |
| 4 | `con sanciones` |
| 5 | `suspensión` |

### `RegulatoryReviewSource : byte`
`Manual = 1` (slice A) · `Api = 2` · `System = 3` (reserved for slice D).

### `RegulatoryField : byte` (for the re-review action + audit payload)
`Hacienda = 1` · `Ccss = 2` · `Sicop = 3`.

## Aggregate: `Supplier` (modified)

**Removed properties/columns:** `HasElectronicInvoice`, `IsCompliantCCSS`, `IsCompliantHacienda`,
`IsCompliantSICOP` (all 4 BIT).

**Added properties:**

| Property | Type | DB column | Notes |
|---|---|---|---|
| `HaciendaStatus` | `HaciendaStatus?` | `HaciendaStatus TINYINT NULL` | null = unreviewed |
| `HaciendaLastReviewedAt` | `DateTime?` | `DATETIME2 NULL` | |
| `HaciendaLastReviewedBy` | `string?` | `NVARCHAR(450) NULL` FK→AspNetUsers NO ACTION | reviewer user id |
| `HaciendaLastReviewedSource` | `RegulatoryReviewSource?` | `TINYINT NULL` | Manual in slice A |
| `CcssStatus` | `CcssStatus?` | `CcssStatus TINYINT NULL` | |
| `CcssLastReviewedAt` | `DateTime?` | `DATETIME2 NULL` | |
| `CcssLastReviewedBy` | `string?` | `NVARCHAR(450) NULL` FK→AspNetUsers NO ACTION | |
| `CcssLastReviewedSource` | `RegulatoryReviewSource?` | `TINYINT NULL` | |
| `SicopStatus` | `SicopStatus?` | `SicopStatus TINYINT NULL` | |
| `SicopLastReviewedAt` | `DateTime?` | `DATETIME2 NULL` | |
| `SicopLastReviewedBy` | `string?` | `NVARCHAR(450) NULL` FK→AspNetUsers NO ACTION | |
| `SicopLastReviewedSource` | `RegulatoryReviewSource?` | `TINYINT NULL` | |
| `IsPmeOrPyme` | `bool` | `BIT NOT NULL DEFAULT(0)` | |
| `HasWarning` | `bool` | `BIT NOT NULL DEFAULT(0)` | |
| `WarningNote` | `string?` | `NVARCHAR(1000) NULL` | trimmed; ≤1000 |
| `RowVersion` | `byte[]` | `ROWVERSION` | optimistic concurrency (D15) |

> The three `…LastReviewedBy` FKs to `AspNetUsers` use `ON DELETE NO ACTION` (consistent with
> `VerifiedByUserId`). Adding three FK columns is acceptable; if dacpac FK count is a concern they MAY be
> plain `NVARCHAR(450)` without an FK (the display tolerates a missing user) — decide at implementation, NO
> ACTION preferred.

**New / changed behavior methods (Rich Domain):**

- `ApplyRegulatoryEdit(HaciendaStatus? hacienda, CcssStatus? ccss, SicopStatus? sicop, bool isPmeOrPyme, bool hasWarning, string? warningNote, string actorUserId, DateTime nowUtc) : IReadOnlyList<RegulatoryChange>`
  - For each of the 3 status fields whose value **differs** from current: set value, set that field's
    `LastReviewedAt=nowUtc`, `LastReviewedBy=actorUserId`, `LastReviewedSource=Manual`; add a
    `RegulatoryChange(field, oldValue, newValue, kind=Changed)`.
  - If `isPmeOrPyme` differs → add `RegulatoryChange(Pme, old, new, kind=Changed)`.
  - If `hasWarning`/`warningNote` differs → normalize (`hasWarning=false` clears note; trim; guard ≤1000) and
    add `RegulatoryChange(Warning, …, kind=Changed)`.
  - `UpdatedAt=nowUtc`. Returns the change list (may be empty → service writes no audit, no-op save).
- `ConfirmRegulatoryReviewed(RegulatoryField field, string actorUserId, DateTime nowUtc) : RegulatoryChange`
  - Guard: throws `InvalidOperationException` if that field's status is `null` (D9).
  - Sets that field's `LastReviewedAt/By/Source(Manual)`; `UpdatedAt=nowUtc`; returns
    `RegulatoryChange(field, value, value, kind=ReviewedNoChange)`.
- `EditByAdmin(...)` is **narrowed** to name-only (the 4 bool params removed); compliance/PME/warning move to
  `ApplyRegulatoryEdit`. (Name edit keeps its current controller path.)

**`RegulatoryChange`** (Domain value object, returned for auditing): `Field` (RegulatoryField | `Pme` |
`Warning` discriminator), `OldValue` (string?), `NewValue` (string?), `Kind` (`Changed` | `ReviewedNoChange`),
`Source` (RegulatoryReviewSource).

**Unchanged:** `Id`, `LegalId`, `IdentificationType`, `Name`, `VerificationStatus` + verify/reject lifecycle,
`CreatedByApplicantId`, `VerifiedBy/At`, `RejectionReason`, `CreatedAt`, branches, `CreateDraft` factory.

## `AdminAuditEvent` (extended — no schema change)

New action constants + target type (Domain):
- `SupplierRegulatoryChanged = "supplier.regulatory_changed"`
- `SupplierRegulatoryReviewed = "supplier.regulatory_reviewed"`
- `SupplierPmeChanged = "supplier.pme_changed"`
- `SupplierWarningChanged = "supplier.warning_changed"`
- `TargetTypeSupplier = "supplier"`

`AdminAuditEventWriter.DeriveTarget`: route `supplier.` prefix → `(TargetTypeSupplier, <supplierId>)` (real id,
not "0"). Payload JSON per change: `{ supplierId, field, oldValue, newValue, source, kind }`.
`AdminAuditEventCopyProvider`: add es-CR phrases for the four actions so the dashboard activity feed renders
them.

## dacpac changes

- `dbo.Suppliers.sql`: drop the 4 BIT columns; add the 16 new columns above; add `RowVersion ROWVERSION`; add
  3 FKs `FK_Suppliers_{Hacienda,Ccss,Sicop}ReviewedBy_AspNetUsers` (NO ACTION) if FK form chosen.
- `PostDeployment/03_SeedSupplierAdminRole.sql`: replace body with idempotent rename-or-create of `Auditor`
  (see D1).
- No other table changes. `AdminAuditEvents`, `VersionHistory` untouched.

## Migration / concurrency notes

- Column drop relies on `DropObjectsNotInSource=true` (dev auto-deploy; Azure publish uses `--no-drop` per
  CLAUDE.md — for the prod path the drop must be handled deliberately, but dev/E2E are greenfield).
- `ROWVERSION` is auto-populated by SQL Server; no backfill needed.
- Greenfield: no data migration for the dropped booleans.
