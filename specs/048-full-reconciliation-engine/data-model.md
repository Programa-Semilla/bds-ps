# Data Model: Full Reconciliation Engine (spec 048)

Two additive greenfield tables + three new enums + two extended enums. No changes to existing tables. No new managed dependencies. All money `DECIMAL(18,2)`; zero-colón tolerance constant `0.01`.

---

## Enums (`Domain/Enums/`, all `: byte`, stored TINYINT via EF `HasConversion<byte>()`)

### `DiscrepancyState : byte` (NEW)
| Value | Ordinal | Meaning |
|-------|---------|---------|
| `Open` | 0 | detected, unassigned |
| `Assigned` | 1 | assigned to a responsible operator |
| `UnderCorrection` | 2 | operator is actively correcting |
| `Resolved` | 3 | cleared — numbers match (auto) or a warning's condition cleared |
| `Waived` | 4 | a **Warning** deliberately accepted (reason required); never valid for Blocking |

Transitions (enforced on the aggregate): `Open→Assigned→UnderCorrection` (operator, any order among Assigned/UnderCorrection); any non-terminal `→Resolved` (auto, materializer); any Warning non-terminal `→Waived` (operator, reason); `Resolved→Open` and `Waived→Open` (auto reopen, materializer, on recurrence / waived-amount change).

### `DiscrepancyScopeType : byte` (NEW)
| Value | Ordinal | `ScopeEntityId` holds |
|-------|---------|----------------------|
| `Document` | 0 | `EvidenceId` |
| `Payment` | 1 | `DisbursementId` |
| `BudgetLine` | 2 | `ItemId` |
| `Participant` | 3 | `ApplicationId` |
| `Tranche` | 4 | `TrancheId` |

### `DiscrepancySeverity : byte` (EXTENDED — currently only `Blocking=0`)
Add `Warning = 1` (the reserved P1 seam). `Blocking` still prevents validate/close; `Warning` never blocks.

### `ReconciliationComparison : byte` (EXTENDED — currently 0–4)
Add the three warning comparisons:
| Value | Ordinal | Severity | Rule |
|-------|---------|----------|------|
| `EvidenceDateAnomaly` | 5 | Warning | evidence dated after its payment, or before agreement execution |
| `PossibleDuplicatePayment` | 6 | Warning | same supplier + amount + date across non-cancelled disbursements |
| `GraphInvoiceAllocationDrift` | 7 | Warning | validated line payment vs independently-allocated graph invoice (047 FINDING-13) |

Severity is derived from the comparison by a fixed map in the materializer (0–4 → Blocking; 5–7 → Warning). Ordinals 0–4 keep their spec-045/046 meaning.

---

## Aggregate: `Discrepancy` (`Domain/Entities/Discrepancy.cs`, `dbo.Discrepancies`)

Application-scoped aggregate (flat, no navigation on `Application` — R2 pattern). Copies the `Evidence` shape: sealed class, private setters, private EF ctor, static factory, owned append-only child collection, guarded transition methods, `RowVersion`.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` IDENTITY | PK |
| `ApplicationId` | `int` | FK → `Applications` (NO ACTION) |
| `ScopeType` | `DiscrepancyScopeType` (TINYINT) | polymorphic scope |
| `ScopeEntityId` | `int` | id within scope (no FK — engine-managed) |
| `Comparison` | `ReconciliationComparison` (TINYINT) | which rule |
| `Severity` | `DiscrepancySeverity` (TINYINT) | fixed from comparison |
| `State` | `DiscrepancyState` (TINYINT) | lifecycle, default `Open` |
| `Expected` | `DECIMAL(18,2)` | reference amount |
| `Actual` | `DECIMAL(18,2)` | observed amount |
| `Difference` | `DECIMAL(18,2)` | signed `Actual − Expected` |
| `ToleranceApplied` | `DECIMAL(18,2)` | default `0` (FR-005 seam) |
| `SourceDocument` | `NVARCHAR(200)` | es-CR source label (e.g. "factura") |
| `AssigneeUserId` | `NVARCHAR(450)` NULL | FK → `AspNetUsers` (NO ACTION) |
| `FirstDetectedAt` | `DATETIMEOFFSET(0)` | set on insert |
| `LastEvaluatedAt` | `DATETIMEOFFSET(0)` | updated every materialization touch |
| `ResolvedAt` | `DATETIMEOFFSET(0)` NULL | set on →Resolved, cleared on reopen |
| `WaivedReason` | `NVARCHAR(500)` NULL | required when `State=Waived` |
| `RowVersion` | `ROWVERSION` | optimistic concurrency (FR-018, OQ-4) |
| `Events` | `IReadOnlyList<DiscrepancyEvent>` | owned; private `_events` backing field |

**Indexes / constraints**
- `PK_Discrepancies` clustered on `Id`.
- **`UX_Discrepancies_Identity` UNIQUE** on `(ApplicationId, ScopeType, ScopeEntityId, Comparison)` — the FR-003 stable identity (exactly one row per identity, ever).
- `IX_Discrepancies_App_State` on `(ApplicationId, State)` INCLUDE `(Severity)` — dashboard/gate reads.
- `IX_Discrepancies_Assignee` on `(AssigneeUserId)` WHERE `AssigneeUserId IS NOT NULL` — filter by responsible user.
- `FK_Discrepancies_Applications` NO ACTION; `FK_Discrepancies_AspNetUsers_Assignee` NO ACTION.
- `CK_Discrepancies_Waive_Blocking`: `NOT (Severity = 0 AND State = 4)` — a Blocking discrepancy can never be Waived (DB backstop of the domain guard).
- `CK_Discrepancies_WaivedReason`: `State <> 4 OR WaivedReason IS NOT NULL`.

**Aggregate behavior (methods)**
- `static Discrepancy Detect(applicationId, scopeType, scopeEntityId, comparison, severity, expected, actual, tolerance, sourceDocument, nowUtc)` — factory; state `Open`; appends an `Opened` event (system actor).
- `void Refresh(expected, actual, difference, nowUtc)` — materializer updates amounts on an existing row; keeps state/assignee; updates `LastEvaluatedAt`; **if `Waived` and amount changed → reopen** (append `Reopened`, state `Open`).
- `void AutoResolve(systemUserId, nowUtc)` — non-terminal → `Resolved`; appends `Resolved` event (system actor); sets `ResolvedAt`.
- `void AutoReopen(systemUserId, nowUtc)` — `Resolved/Waived` → `Open` on recurrence; appends `Reopened`.
- `void Assign(assigneeUserId, actorUserId, nowUtc)` — → `Assigned`; appends `Assigned` event.
- `void MarkUnderCorrection(actorUserId, note?, nowUtc)` — → `UnderCorrection`.
- `void Waive(reason, actorUserId, nowUtc)` — **throws if `Severity == Blocking`** or reason blank; → `Waived`; stores `WaivedReason`; appends `Waived` event.

---

## Child: `DiscrepancyEvent` (`Domain/Entities/DiscrepancyEvent.cs`, `dbo.DiscrepancyEvents`)

Immutable append-only history row (copy `DisbursementLedgerEntry`: static factories, no mutators). Created only through the `Discrepancy` root (`internal` ctor).

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` IDENTITY | PK |
| `DiscrepancyId` | `int` | FK → `Discrepancies` (**CASCADE**) |
| `OccurredAt` | `DATETIMEOFFSET(0)` | |
| `ActorUserId` | `NVARCHAR(450)` | FK → `AspNetUsers` (NO ACTION); **system-sentinel id** for auto transitions (spec-043 lesson — never the literal `"system"`) |
| `FromState` | `DiscrepancyState` (TINYINT) | |
| `ToState` | `DiscrepancyState` (TINYINT) | |
| `Kind` | `NVARCHAR(30)` | `Opened`/`Assigned`/`UnderCorrection`/`Resolved`/`Waived`/`Reopened` (timeline label) |
| `Reason` | `NVARCHAR(500)` NULL | required for `Waived` |
| `Note` | `NVARCHAR(500)` NULL | optional explanation |

- `PK_DiscrepancyEvents` clustered on `Id`; `IX_DiscrepancyEvents_Discrepancy` on `(DiscrepancyId, OccurredAt)`.
- `FK_DiscrepancyEvents_Discrepancies` **CASCADE** (single-ownership child); `FK_DiscrepancyEvents_AspNetUsers` NO ACTION.
- No `RowVersion` (append-only).

---

## Wiring
- `AppDbContext`: `DbSet<Discrepancy> Discrepancies => Set<Discrepancy>();` + `DbSet<DiscrepancyEvent> DiscrepancyEvents => Set<DiscrepancyEvent>();`.
- Two `IEntityTypeConfiguration<>` files (auto-registered): `.HasConversion<byte>()` on every enum; `.IsRowVersion()`; `PropertyAccessMode.Field` on `Discrepancy.Events`; the unique + filtered indexes; FK `OnDelete` per above.
- dacpac: `Database/Tables/dbo.Discrepancies.sql` + `dbo.DiscrepancyEvents.sql` (auto-globbed). No post-deploy backfill (greenfield). No seed (no new role/config row).

## Regression-materialization test (mandatory, the established gotcha)
Add `DiscrepancyEnumMaterializationTests` (mirror `DisbursementEnumMaterializationTests`) proving the four TINYINT enums round-trip on **real SQL** (EF-InMemory hides the `Byte→Int32` failure).
