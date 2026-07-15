# Phase 1 Data Model: Financial Disbursement Core

**Spec:** spec.md · **Research:** research.md · **Date:** 2026-07-15

All money is `decimal` mapped `decimal(18,2)` (`CurrencyCode` `char(3)` where a currency is stored). All enums are `TINYINT` + `HasConversion<byte>()`. All tables carry `RowVersion ROWVERSION` + `.IsRowVersion()`. FKs to `Applications`/`AspNetUsers` are `ON DELETE NO ACTION`. EF configs live in `Infrastructure/Persistence/Configurations/` and auto-register via `ApplyConfigurationsFromAssembly`.

## Enums (Domain/Enums)

```
DisbursementState : byte { Recorded = 0, Inconsistent = 1, Validated = 2, Cancelled = 3 }
EvidenceKind      : byte { BankReceipt = 0, Invoice = 1 }
LedgerEntryType   : byte { Allocation = 0, Disbursement = 1 }
DiscrepancySeverity : byte { Blocking = 0 }   // Warning reserved for P4; single value in P1
ReconciliationComparison : byte { DisbursementVsBankReceipt = 0, DisbursementVsInvoice = 1, TotalVsAllocation = 2 }
```

## Aggregate 1 — `Disbursement` (mutable operational record)

**Domain entity** `Domain/Entities/Disbursement.cs` (`sealed`), standalone, keyed by `ApplicationId`.

| Field | Type | Notes |
|---|---|---|
| Id | int | identity |
| ApplicationId | int | FK → Applications, NO ACTION |
| PaymentDate | DateOnly/DateTime | date money left the bank |
| Amount | decimal(18,2) | CK > 0 |
| BankTransactionReference | nvarchar(100) | required |
| BankAccountReference | nvarchar(100)? | optional free-text (spec Open-Q default) |
| State | DisbursementState (TINYINT) | derived, recomputed on mutation |
| CreatedByUserId | nvarchar(450) | FK → AspNetUsers, NO ACTION |
| CreatedAtUtc | datetimeoffset(0) | default SYSUTCDATETIME() |
| ValidatedByUserId | nvarchar(450)? | set at Validar |
| ValidatedAtUtc | datetimeoffset(0)? | set at Validar |
| CancelledByUserId | nvarchar(450)? | set at Cancel |
| CancelledAtUtc | datetimeoffset(0)? | set at Cancel |
| RowVersion | rowversion | concurrency |

**Behavior (rich domain — Constitution II):**
- `static Disbursement Record(Application app, string operatorUserId, DateOnly paymentDate, decimal amount, string bankTxnRef, string? bankAcctRef)` — gate: `app.State == AgreementExecuted` (else throw); `amount > 0`; sets State=Recorded. Mirrors `FundsUsageEvidence.CreateForExecutedApplication`.
- `void EditDetails(...)` / `void ReplaceEvidenceMarker(...)` — guarded `State ∈ {Recorded, Inconsistent}` (locked once Validated).
- `void ApplyReconciliation(IReadOnlyList<ReconciliationDiscrepancy> discrepancies)` — sets State = discrepancies.Any() ? Inconsistent : Recorded (no-op if Validated/Cancelled — those are terminal).
- `bool IsValidatable(bool bothEvidencePresent, bool zeroDiscrepancies)` => `State != Validated && State != Cancelled && bothEvidencePresent && zeroDiscrepancies`.
- `void Validate(string operatorUserId)` — guarded on `IsValidatable`; sets State=Validated + Validated{By,At}. (Ledger entry posted by the service in the same SaveChanges.)
- `void Cancel(string operatorUserId)` — guarded `State ∈ {Recorded, Inconsistent}`; sets State=Cancelled + Cancelled{By,At}.

**State machine:** `Recorded ⇄ Inconsistent` (reconciliation flips) → `Validated` (via Validate, terminal) ; `{Recorded,Inconsistent}` → `Cancelled` (terminal). No transition out of Validated/Cancelled.

**Table** `Database/Tables/dbo.Disbursements.sql`: PK clustered Id; FK Applications NO ACTION; FK AspNetUsers (CreatedByUserId) NO ACTION; `CK_Disbursements_Amount_Positive CHECK ([Amount] > 0)`; index `IX_Disbursements_ApplicationId`; index `IX_Disbursements_ApplicationId_State` (inbox/projection).

## Aggregate 2 — `DisbursementEvidence` (typed, 1:1 each kind)

**Domain entity** `Domain/Entities/DisbursementEvidence.cs` (`sealed`), keyed by `DisbursementId`.

| Field | Type | Notes |
|---|---|---|
| Id | int | identity |
| DisbursementId | int | FK → Disbursements, NO ACTION |
| Kind | EvidenceKind (TINYINT) | BankReceipt / Invoice |
| Amount | decimal(18,2) | CK > 0 — the reconciled figure |
| Currency | char(3) | CurrencyCode; must be CRC in P1 |
| DocumentReferenceNumber | nvarchar(100) | required |
| DocumentDate | date | required |
| OriginalFileName | nvarchar(500) | |
| BlobKey | nvarchar(1024) | object-storage key |
| FileSize | bigint | CK > 0 |
| ContentType | nvarchar(100) | |
| UploadedByUserId | nvarchar(450) | FK → AspNetUsers, NO ACTION |
| UploadedAtUtc | datetimeoffset(0) | default SYSUTCDATETIME() |
| RowVersion | rowversion | |

**Behavior:** `static DisbursementEvidence Attach(Disbursement d, EvidenceKind kind, decimal amount, CurrencyCode currency, string reference, DateOnly docDate, file metadata, string uploaderUserId)` — gate `d.State ∈ {Recorded, Inconsistent}`, `amount > 0`, `currency == CRC`. `void Replace(...)` same gate (overwrite file+fields; no version chain — P3).

**Table** `Database/Tables/dbo.DisbursementEvidence.sql`: PK Id; FK Disbursements NO ACTION; FK AspNetUsers NO ACTION; `CK_DisbursementEvidence_Amount_Positive`; `CK_DisbursementEvidence_FileSize_Positive`; **`UX_DisbursementEvidence_Disbursement_Kind UNIQUE (DisbursementId, Kind)`** (enforces exactly one bank receipt + one invoice — FR-006/1:1); index `IX_DisbursementEvidence_DisbursementId`.

## Aggregate 3 — `DisbursementLedgerEntry` (append-only)

**Domain entity** `Domain/Entities/DisbursementLedgerEntry.cs` (`sealed`), keyed by `ApplicationId`. **Never updated or deleted** (append-only by service discipline — no mutating methods).

| Field | Type | Notes |
|---|---|---|
| Id | int | identity |
| ApplicationId | int | FK → Applications, NO ACTION |
| EntryType | LedgerEntryType (TINYINT) | Allocation / Disbursement |
| Amount | decimal(18,2) | Allocation = ceiling; Disbursement = debited amount |
| DisbursementId | int? | null for Allocation; set for Disbursement entries, FK → Disbursements NO ACTION |
| PostedByUserId | nvarchar(450) | FK → AspNetUsers NO ACTION |
| PostedAtUtc | datetimeoffset(0) | default SYSUTCDATETIME() |
| RowVersion | rowversion | inert (append-only) but kept for house consistency |

**Behavior:** `static DisbursementLedgerEntry Allocation(int applicationId, decimal amount, string userId)`; `static DisbursementLedgerEntry ForValidatedDisbursement(Disbursement d, string userId)`. No instance mutators.

**Table** `Database/Tables/dbo.DisbursementLedgerEntries.sql`: PK Id; FK Applications NO ACTION; FK Disbursements NO ACTION (nullable); FK AspNetUsers NO ACTION; index `IX_DisbursementLedgerEntries_ApplicationId_EntryType`; **`UX_DisbursementLedger_Allocation UNIQUE (ApplicationId) WHERE [EntryType] = 0`** (exactly one Allocation entry per application — filtered unique); **`UX_DisbursementLedger_Disbursement UNIQUE (DisbursementId) WHERE [EntryType] = 1`** (one ledger entry per validated disbursement — no double-post / idempotency).

## Value objects (Domain — not persisted in P1)

- `ReconciliationDiscrepancy` (record): `ReconciliationComparison Comparison`, `decimal Expected`, `decimal Actual`, `decimal Difference`, `string SourceDocument`, `DiscrepancySeverity Severity`. Produced by the pure evaluator; rendered on read; not stored.
- `ParticipantBalance` (record): `decimal Allocated, Paid, Validated, PendingValidation, Available`. Projection result; not stored.

## Pure domain service — reconciliation

`Domain/Services/DisbursementReconciliation.cs` (static, pure, deterministic — NFR-020):

```
IReadOnlyList<ReconciliationDiscrepancy> Evaluate(
    decimal disbursementAmount,
    decimal? bankReceiptAmount,        // null if not yet attached
    decimal? invoiceAmount,            // null if not yet attached
    decimal sumOfNonCancelledIncludingThis,
    decimal allocation)
```

Rules: zero tolerance; a difference (`|a − b| >= 0.01`, i.e. ≥ 1 colón) yields a discrepancy. Comparison (1) runs only if `bankReceiptAmount != null`; (2) only if `invoiceAmount != null`; (3) always, flagging when `sum > allocation` (`Difference = sum − allocation`, source = "conjunto de desembolsos"). All severities `Blocking`.

## Balance projection

`ParticipantBalance` computed by `IParticipantBalanceProjection` (Application) / EF impl (Infrastructure):
- `Allocated` = ledger Allocation entry amount **if present**, else Σ `Quotation.ConvertedCrcAmount` of the executed application's selected line-item quotations.
- `Validated` = Σ ledger Disbursement-entry amounts for the application.
- `PendingValidation` = Σ `Disbursements.Amount` where `State ∈ {Recorded, Inconsistent}`.
- `Paid` = `Validated + PendingValidation`.
- `Available` = `Allocated − Paid` (may be negative — over-disbursement signal, FR-020; never clamped).

Double-count safety: a validated disbursement is counted **only** via its ledger entry (its `Disbursements` row is `State=Validated`, excluded from the Pending sum).

## Relationships

```
Application (1) ──< Disbursement (0..N)          [FK ApplicationId, NO ACTION]
Disbursement (1) ──< DisbursementEvidence (0..2) [unique per Kind → exactly 1 BankReceipt + 1 Invoice when complete]
Application (1) ──< DisbursementLedgerEntry (0..N) [1 Allocation + 1 per validated Disbursement]
Disbursement (1) ──1 DisbursementLedgerEntry?     [only when Validated; filtered-unique on DisbursementId]
```

No navigation collections added to `Application` (queried flat by `ApplicationId`, per 036).

## EF configuration notes (Infrastructure/Persistence/Configurations)

- `DisbursementConfiguration`, `DisbursementEvidenceConfiguration`, `DisbursementLedgerEntryConfiguration` — one each; auto-registered.
- `Amount` fields → `HasColumnType("decimal(18,2)")`. `Currency` → `char(3)` (`.HasMaxLength(3).IsFixedLength()` or `CurrencyCode` converter per `ExchangeRateConfiguration`).
- `State` / `Kind` / `EntryType` → **`HasConversion<byte>()`** (TINYINT). ⚠️ Must be validated by E2E against real SQL — InMemory hides the `Byte→Int32` throw (035/040 lesson).
- `RowVersion` → `.IsRowVersion()`.
- FKs → `.OnDelete(DeleteBehavior.Restrict)`; no `WithMany` navigation on `Application`.
- New `DbSet`s on `AppDbContext`: `Disbursements`, `DisbursementEvidence`, `DisbursementLedgerEntries`.

## Dacpac deliverables

- New tables: `dbo.Disbursements.sql`, `dbo.DisbursementEvidence.sql`, `dbo.DisbursementLedgerEntries.sql`.
- New post-deploy `10_SeedFinancialOperatorRole.sql` (idempotent `AspNetRoles` insert for `"Financial Operator"`), `:r`-included at the tail of `PostDeployment/SeedData.sql` and dual-listed (`<Build Remove>` + `<None Include>`) in `FundingPlatform.Database.sqlproj`.
- No column changes to existing tables. Purely additive (no migration-safe DEFAULT dance needed).
