# Phase 1 Data Model: Regulatory Freshness Gating + Hacienda API Sync

Slice D adds **no new table** and **no new `ApplicationState`/`NotificationEvent`**. It extends the existing `Supplier` aggregate with per-provider Hacienda-sync outcome fields, adds one enum, reuses the slice-A regulatory model + audit trail, and introduces transient (non-persisted) DTOs for the API lookup and the freshness query.

---

## 1. `Supplier` (existing aggregate — extended)

`src/FundingPlatform.Domain/Entities/Supplier.cs` / `src/FundingPlatform.Database/Tables/dbo.Suppliers.sql`

### Existing fields consumed (slice A, unchanged)

Per regulatory field (`Hacienda`, `Ccss`, `Sicop`):
- `{Field}Status` — `HaciendaStatus?` / `CcssStatus?` / `SicopStatus?` (TINYINT, null = "sin revisar")
- `{Field}LastReviewedAt` — `DateTime?` (DATETIME2)
- `{Field}LastReviewedBy` — `string?` (NVARCHAR(450))
- `{Field}LastReviewedSource` — `RegulatoryReviewSource?` (TINYINT: Manual=1, **Api=2**, System=3)
- `RowVersion` — `byte[]` (ROWVERSION) — optimistic concurrency, reused by the sync write

### New columns

| Column | Type | Null | Meaning |
|---|---|---|---|
| `HaciendaSyncAttemptAt` | `DATETIME2` | NULL | UTC instant of the last daily Hacienda sync attempt (success or failure). |
| `HaciendaSyncOutcome` | `TINYINT` | NULL | `HaciendaSyncOutcome?` — `Success=1` / `Failure=2`. NULL = never attempted. |
| `HaciendaSyncError` | `NVARCHAR(500)` | NULL | Failure reason (es-CR) when the last attempt failed; cleared on success. |

- All nullable → migration-safe add, no backfill (greenfield, mirrors slice-A column additions).
- EF mapping in `SupplierConfiguration`: `HaciendaSyncOutcome` mapped `HasConversion<byte?>()` (TINYINT-enum lesson from spec 040 — InMemory hides it; real SQL throws Byte→Int32 without it). `HaciendaSyncError` `HasMaxLength(500)`.

### New / changed behavior (Rich Domain Model)

- `RegulatoryChange ApplyHaciendaSyncResult(HaciendaStatus mapped, DateTime nowUtc)`
  - If `mapped != HaciendaStatus`: set `HaciendaStatus = mapped`; return a `RegulatoryChange { Field=Hacienda, Kind=Changed, Old, New, Source=Api }`.
  - Else: return `RegulatoryChange { Field=Hacienda, Kind=ReviewedNoChange, Source=Api }`.
  - Always: `HaciendaLastReviewedAt=nowUtc`, `HaciendaLastReviewedBy="system"`, `HaciendaLastReviewedSource=Api`; `HaciendaSyncAttemptAt=nowUtc`, `HaciendaSyncOutcome=Success`, `HaciendaSyncError=null`.
- `void RecordHaciendaSyncFailure(DateTime nowUtc, string reason)`
  - `HaciendaSyncAttemptAt=nowUtc`, `HaciendaSyncOutcome=Failure`, `HaciendaSyncError=reason` (truncated ≤500).
  - **Touches no status or last-reviewed field** (FR-018 — never corrupt regulatory data on failure).
- `bool IsRegulatoryStale(int windowDays, DateTime nowUtc)` — true if any required field is stale.
- `IReadOnlyList<RegulatoryField> StaleRequiredFields(int windowDays, DateTime nowUtc)` — the specific stale fields among {Hacienda, Ccss, Sicop}.
  - A field is **stale** when `{Field}LastReviewedAt` is `null` OR `< nowUtc.AddDays(-windowDays)` (FR-001). All three are required (FR-005).

---

## 2. `HaciendaSyncOutcome` (NEW enum)

`src/FundingPlatform.Domain/Enums/HaciendaSyncOutcome.cs`

```csharp
public enum HaciendaSyncOutcome : byte
{
    Success = 1,
    Failure = 2,
}
```

es-CR labels (e.g. "Verificación exitosa" / "Verificación fallida") live in the Web resources, never in the DB (mirrors `RegulatoryStatusLabels`).

---

## 3. `HaciendaStatus` (existing enum — mapping target, unchanged)

`SinInscripcion=1, AlDia=2, EstadoMoroso=3, CobroAdministrativo=4, DesinscritoAlDia=5, SinInformacion=6, DesinscritoMoroso=7, DesinscritoDeOficio=8`. The sync mapper (research D1) targets these; `DesinscritoDeOficio` is never auto-set (`fe/ae` cannot distinguish it).

---

## 4. Transient DTOs (not persisted)

### `HaciendaLookupResult` (Application — `Abstractions/Hacienda/`)

Result of one `IHaciendaApiClient.LookupAsync`. A discriminated result:

- `Found(string Nombre, HaciendaSituacion Situacion)` — HTTP 200 with a parseable body.
- `NotRegistered` — HTTP 404 (`{code:404,…}`) → mapper yields `SinInformacion`.
- `Failed(string Reason)` — transport error / 5xx / timeout / unparseable body / unrecognized `estado` / malformed-or-missing local id.

`HaciendaSituacion(string Estado, bool Moroso, bool Omiso)` — parsed from `situacion.{estado,moroso,omiso}` (`"SI"`/`"NO"` → bool, case-insensitive).

### `StaleRegulatoryFinding` (Application — `Regulatory/`)

One stale (provider, field) pair for the gate/warning:

```csharp
public sealed record StaleRegulatoryFinding(
    int SupplierId,
    string SupplierName,
    RegulatoryField Field,        // Hacienda | Ccss | Sicop
    DateTime? LastReviewedAt);     // null = never reviewed
```

`GetStaleFindingsForApplicationAsync(appId)` returns the flattened set across the application's distinct selected suppliers (research D2). Empty list ⇒ no block / no warning.

---

## 5. Audit (reuses `AdminAuditEvent`, +1 verb)

`src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`

| Situation | Action constant | Payload |
|---|---|---|
| Sync changed a value | `supplier.regulatory_changed` (existing) | `{supplierId, field:"Hacienda", oldValue, newValue, source:"Api", kind:"Changed"}` |
| Sync confirmed unchanged | `supplier.regulatory_reviewed` (existing) | `{supplierId, field:"Hacienda", source:"Api", kind:"ReviewedNoChange"}` |
| Sync failed | `supplier.hacienda_sync_failed` (**NEW**) | `{supplierId, identificacion, reason}` |

`supplier.` prefix already routes to `TargetTypeSupplier` in `AdminAuditEventWriter`; the new constant needs no router change.

---

## 6. Configuration (options, defaults)

| Key | Default | Bound to |
|---|---|---|
| `Regulatory:FreshnessWindowDays` | `30` | `RegulatoryFreshnessOptions.FreshnessWindowDays` |
| `Regulatory:HaciendaSync:Provider` | `Live` (real env) / `Fake` (Aspire dev + E2E) | selects `LiveHaciendaApiClient` vs `FakeHaciendaApiClient` |
| `Regulatory:HaciendaSync:Enabled` | `true` | gate the daily worker |
| `Regulatory:HaciendaSync:RunAtLocalTime` | `06:00` | next-run scheduling (America/Costa_Rica) |
| `Regulatory:HaciendaSync:BatchSize` | `100` | per-cycle provider batch / throttle |
| `Regulatory:HaciendaSync:PerCallDelayMs` | `0` | optional inter-call throttle |
| `Regulatory:HaciendaSync:BaseUrl` | `https://api.hacienda.go.cr` | live client base address |

All have sensible defaults; absence must not crash (Constitution VI). Sentinel/dev values set by AppHost for ephemeral E2E (`Provider=Fake`).

---

## 7. Relationships touched (read-only by the gate)

```
Application ──< Item ──(SelectedSupplierId)──> Supplier ──(Hacienda/Ccss/Sicop freshness)
        │
        └─ Group ─> Process            (digest scoping: audit-pipeline app → Group → Auditor users)
```

The freshness gate reads `Application.Items.Where(SelectedSupplierId != null).SelectedSupplier`; the digest reads audit-pipeline applications (`State ∈ {PendingAudit, ReturnedFromAudit}`) → `Group` → `Auditor`-role members. No write to `Application`/`Item`; the only writes are to `Supplier` (sync) and `AdminAuditEvent`.
