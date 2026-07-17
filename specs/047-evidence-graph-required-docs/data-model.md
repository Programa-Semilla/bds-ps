# Data Model: Evidence Graph & Required-Document Rules (spec 047)

**Additive, dacpac-only.** 5 new tables + additive columns on `dbo.Items`. No table is dropped or reshaped; `DisbursementEvidence` is untouched (D1). Money is `decimal(18,2)`; every enum column is `TINYINT` + EF `HasConversion<byte>()` (the byte→int32 gotcha) unless noted.

---

## New enums (Domain/Enums)

| Enum | Members | Column | Notes |
|---|---|---|---|
| `EvidenceType : byte` | `BankReceipt=0`, `Invoice=1`, `SignedAcceptance=2`, `CreditNote=3`, `RefundReceipt=4`, `Other=5` | `dbo.Evidence.Type TINYINT` | The graph's six types (D1). Distinct from P1's `EvidenceKind` (which stays 2-valued on `DisbursementEvidence`). |
| `ItemClosureState : byte` | `Open=0`, `Closed=1` | `dbo.Items.ClosureState TINYINT` | Stored closure trigger (D3). |

---

## New entity: `Evidence` (aggregate root, Application-scoped)

The graph node. Carries current denormalized values; the version chain is the audit history.

| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `ApplicationId` | int, FK → Applications **NO ACTION** | scope; mirrors `Disbursement`/`FundsUsageEvidence` |
| `DisbursementId` | int? NULL, FK → Disbursements **NO ACTION** | optional payment anchor (receipt/invoice); null for acceptance/credit-note/refund/other |
| `Type` | `EvidenceType` (TINYINT) | |
| `Amount` | decimal(18,2) | `CK_Evidence_Amount_Positive CHECK (Amount > 0)` |
| `Currency` | char(3), default `CRC` | CRC-only this slice |
| `DocumentReferenceNumber` | nvarchar(100) | |
| `DocumentDate` | date (`DateOnly`) | |
| `SupplierId` | int? NULL, FK → Suppliers **NO ACTION** | optional |
| `BlobKey` / `OriginalFileName` / `FileSize` / `ContentType` | current-file pointer (nvarchar 1024/500/bigint/nvarchar 100) | denormalized from the current version |
| `FileHash` | char(64) | SHA-256 of the current file (FR-170) |
| `UploadedByUserId` | nvarchar(450), FK → AspNetUsers **NO ACTION** | |
| `UploadedAtUtc` | datetime2, default `SYSUTCDATETIME()` | |
| `RowVersion` | rowversion | optimistic concurrency (allocation/replace/close races) |

- **Orphan guard (FR-007):** must link to ≥1 budget-line (`EvidenceLineAllocation`) or a `DisbursementId`. Enforced in the service at save (a domain-level invariant can't see the allocation rows), refusing `EvidenceReasons.Orphaned`.
- `Attach(...)` factory + `ReplaceCurrent(...)` (appends a version, updates denormalized current) + collection of `EvidenceVersion`. All setters private.
- FK to Applications is **NO ACTION** (soft-delete filter model, matches `Disbursement`).

## New entity: `EvidenceVersion` (owned child, immutable, append-only) — D4

| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `EvidenceId` | int, FK → Evidence **CASCADE** | owned |
| `VersionNumber` | int | seed 1, increments |
| `IsCurrent` | bit | exactly one true per evidence |
| `BlobKey`/`OriginalFileName`/`FileSize`/`ContentType` | file snapshot | |
| `Amount`/`Currency`/`DocumentReferenceNumber`/`DocumentDate` | reconciliation-critical field snapshot | |
| `FileHash` | char(64) | SHA-256 |
| `Reason` | nvarchar(500) | **required** for versions after the first (FR-021) |
| `CreatedByUserId` | nvarchar(450), FK → AspNetUsers **NO ACTION** | |
| `CreatedAtUtc` | datetime2, default `SYSUTCDATETIME()` | |

- **One-current** enforced by filtered unique `UX_EvidenceVersions_OneCurrent ON (EvidenceId) WHERE [IsCurrent] = 1` (copies `UX_SignedUploads_OnePending_PerAgreement`).
- Rows never mutate except `IsCurrent 1→0` when superseded (transition method, no field rewrite).
- `IX_EvidenceVersions_EvidenceId` covering.

## New entity: `EvidenceLineAllocation` (M:N `Evidence ↔ Item`) — D2

| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `EvidenceId` | int, FK → Evidence **CASCADE** | |
| `ItemId` | int, FK → Items **NO ACTION** (EF `ClientCascade`) | two-cascade-path avoidance |
| `Amount` | decimal(18,2) | `CK_EvidenceLineAllocations_Amount_Positive CHECK (Amount > 0)` |
| `RowVersion` | rowversion | |

- `UX_EvidenceLineAlloc_Evidence_Item UNIQUE (EvidenceId, ItemId)`; `IX_EvidenceLineAlloc_ItemId` covering (per-line sums).
- No mutators; `static For(evidenceId, itemId, amount)` factory (amount > 0). Replace-all persistence (copy `ReplaceSplitAsync`).

## New entity: `DocumentRuleSet` + `DocumentRuleItem` (admin matrix) — D5

**`DocumentRuleSet`** (aggregate root)

| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `CategoryId` | int? NULL, FK → Categories **NO ACTION** | null = global default |
| `RowVersion` | rowversion | |

- `UNIQUE (CategoryId)` — one set per category; one global-default row (`CategoryId IS NULL`). (SQL Server treats a single NULL as unique-eligible; only one NULL row is intended and the service enforces it.)
- Owns `List<DocumentRuleItem> _items`.

**`DocumentRuleItem`** (owned child)

| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `DocumentRuleSetId` | int, FK → DocumentRuleSets **CASCADE** | |
| `EvidenceType` | `EvidenceType` (TINYINT `HasConversion<byte>()`) | |
| `IsRequired` | bit | |

- `UNIQUE (DocumentRuleSetId, EvidenceType)`. Edit = full-replace of items (no snapshot table needed — D5).
- **Seed:** post-deploy `NN_SeedDocumentRules.sql` inserts the global-default set (Bank Receipt + Invoice + Signed Acceptance = Required).

---

## Extended entity: `Item` (budget-line) — additive columns — D3

| New field | Type | Notes |
|---|---|---|
| `ClosureState` | `ItemClosureState` TINYINT NOT NULL DEFAULT(0) | `.HasConversion<byte>().IsRequired().HasDefaultValue(Open)`; nullable-safe inline add, no backfill |
| `ClosedByUserId` | nvarchar(450) NULL, FK → AspNetUsers **NO ACTION** | stamp (mirrors `Disbursement.ValidatedByUserId`) |
| `ClosedAtUtc` | datetime2 NULL | stamp |
| `ClosureReason` | nvarchar(500) NULL | optional note at close |
| `ReopenReason` | nvarchar(500) NULL | **required** at reopen (FR-017) |

New `internal` mutators (mirror `Commit()`/`Uncommit()`): `Close(userId, reason?)`, `Reopen(userId, reason)` — idempotent, stamp/clear actor+timestamp. Gate ("required docs present + payments validated + equality chain + fully allocated") is enforced by the **service** (the entity can't see attributions/evidence). `MissingRequiredDocuments(requiredTypes, presentTypes)` pure helper mirroring `MissingRequiredCategoryFields()`.

---

## Derived (never stored) — Application/Infrastructure

- `BudgetLineStatus` gains `Closed`; `DeriveStatus` gets a leading `if (closed) return Closed;` (D3).
- Composed line DTO (`BudgetLineBalance`) gains `bool EvidenceIncomplete` (≥1 required doc missing) + the missing-type list, and `ClosureState`.
- New DTOs: `EvidenceSummary`, `EvidenceDetail`, `EvidenceVersionRow`, `LineCompleteness` (required vs present per type), `DocumentRuleSetRow`/`Detail`.

---

## FK cascade topology summary (dacpac publish safety)

| Table | Parent cascade path | Other FKs |
|---|---|---|
| `Evidence` | Applications **NO ACTION** | Disbursements NO ACTION, Suppliers NO ACTION, AspNetUsers NO ACTION |
| `EvidenceVersion` | Evidence **CASCADE** | AspNetUsers NO ACTION |
| `EvidenceLineAllocation` | Evidence **CASCADE** | Items **NO ACTION** (EF `ClientCascade`) — two-path avoidance |
| `DocumentRuleSet` | — | Categories NO ACTION |
| `DocumentRuleItem` | DocumentRuleSets **CASCADE** | — |

The only two-cascade-path risk is `EvidenceLineAllocation` (Evidence→Application and Item→Application both reach Applications) — resolved exactly as `DisbursementLineAllocation`/`ItemImpacts`: Evidence path CASCADE, Item path NO ACTION.
