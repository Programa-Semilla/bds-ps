# Phase 1 Contracts: Regulatory Freshness Gating + Hacienda API Sync

Interface/seam contracts introduced by slice D. Application-layer abstractions; Infrastructure implementations. Signatures are illustrative (final names settled in implementation) but the contracts are binding.

---

## `IHaciendaApiClient` (Application — `Abstractions/Hacienda/`)

The replaceable seam over the Hacienda `fe/ae` endpoint. The live API is never called in tests (a fake is config-selected).

```csharp
public interface IHaciendaApiClient
{
    // Looks up one taxpayer by identification number.
    // Never throws for HTTP/transport errors — maps them to HaciendaLookupResult.Failed.
    Task<HaciendaLookupResult> LookupAsync(string identificacion, CancellationToken ct);
}
```

**Result contract** (`HaciendaLookupResult`):
- `Found(string Nombre, HaciendaSituacion Situacion)` — HTTP 200, body parsed.
- `NotRegistered` — HTTP 404 (`{code:404,…}`).
- `Failed(string Reason)` — transport error / non-200-non-404 / timeout / unparseable / unrecognized `estado`.

`HaciendaSituacion(string Estado, bool Moroso, bool Omiso)` — from `situacion.{estado,moroso,omiso}`.

**Implementations:**
- `LiveHaciendaApiClient` (Infrastructure) — typed `HttpClient` (`IHttpClientFactory`), `GET {BaseUrl}/fe/ae?identificacion={id}`, configured timeout; all exceptions/timeouts caught → `Failed`.
- `FakeHaciendaApiClient` (Infrastructure) — canned results keyed by identification; static `LookupCallCount` + `Reset()`; supports staging any outcome (al día / moroso / no-inscrito / 404 / failure). Mirrors `StubAiClient`.

**Registration (config-gated, mirrors `AiComparison:Provider`):**
```csharp
var provider = config["Regulatory:HaciendaSync:Provider"] ?? "Live";
if (provider.Equals("Live", StringComparison.OrdinalIgnoreCase)) {
    services.AddHttpClient<IHaciendaApiClient, LiveHaciendaApiClient>(/* BaseAddress, Timeout */);
} else {
    services.AddSingleton<IHaciendaApiClient, FakeHaciendaApiClient>();
}
```

---

## `HaciendaStatusMapper` (Infrastructure — `Hacienda/`)

Pure, total mapping from a lookup result to the enum (research D1).

```csharp
public static class HaciendaStatusMapper
{
    // Returns null ONLY for Failed results (caller records a failure instead).
    public static HaciendaStatus? Map(HaciendaLookupResult result);
}
```

Mapping table is the binding contract — see research.md D1. Unit-tested exhaustively (one case per row + an unrecognized-`estado` → null/Failed case).

---

## `IRegulatoryFreshnessService` (Application — `Regulatory/`)

Backs both the hard gate and the non-blocking warning.

```csharp
public interface IRegulatoryFreshnessService
{
    // Distinct selected suppliers of the application's approved items,
    // flattened to one finding per stale required field. Empty ⇒ fresh.
    Task<IReadOnlyList<StaleRegulatoryFinding>> GetStaleFindingsForApplicationAsync(
        int applicationId, CancellationToken ct);
}
```

- Uses `RegulatoryFreshnessOptions.FreshnessWindowDays` and a clock (UTC).
- Implementation `RegulatoryFreshnessService` (Infrastructure) loads `Application → Items → SelectedSupplier`, dedups suppliers, calls `Supplier.StaleRequiredFields(window, nowUtc)`.
- **Gate consumers:** `FundingAgreementController.Generate` (auditor), `AuditWorkflowService.ReleaseForSignatureAsync` + `ConfirmAgreementPdf` — non-empty ⇒ refuse with es-CR message enumerating provider+field+last-reviewed (FR-007/FR-009).
- **Warning consumers:** `Review.cshtml` (send-to-audit), `Views/Audit/*` — render findings, do not block (FR-010).

---

## Options (Application — `Regulatory/`)

```csharp
public sealed class RegulatoryFreshnessOptions { public int FreshnessWindowDays { get; set; } = 30; }   // FR-002

public sealed class HaciendaSyncOptions
{
    public const string SectionName = "Regulatory:HaciendaSync";
    public string Provider { get; set; } = "Live";          // Live | Fake
    public bool Enabled { get; set; } = true;
    public string RunAtLocalTime { get; set; } = "06:00";   // America/Costa_Rica; §16.5
    public int BatchSize { get; set; } = 100;               // FR-017 throttle
    public int PerCallDelayMs { get; set; } = 0;
    public string BaseUrl { get; set; } = "https://api.hacienda.go.cr";
}
```

---

## `HaciendaSyncService` (Infrastructure — `BackgroundServices/`, `BackgroundService`)

Daily sync (research D4 scheduling). Public single-cycle seam for tests.

```csharp
public sealed class HaciendaSyncService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct);   // loop: delay→RunOnceAsync→repeat (next RunAtLocalTime)
    public Task<HaciendaSyncSummary> RunOnceAsync(CancellationToken ct);   // test seam
}
```

`RunOnceAsync` per supplier: validate local id (empty/malformed → `RecordHaciendaSyncFailure`, no call) → `LookupAsync` → `Map` → `ApplyHaciendaSyncResult` (success) or `RecordHaciendaSyncFailure` (Failed) → write audit (D6 verbs) → `SaveChangesAsync` under `RowVersion` (concurrency conflict → skip+log, FR-025). Batched per `BatchSize`; one provider's exception never aborts the run (FR-024). Returns a summary `{checked, changed, unchanged, failed}` for logging/tests.

---

## `RegulatoryFreshnessDigestService` (Infrastructure — `BackgroundServices/`, `BackgroundService`)

Daily stale-value digest, direct-send (research D3). Public single-cycle seam.

```csharp
public sealed class RegulatoryFreshnessDigestService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct);   // loop: delay→RunOnceAsync→repeat
    public Task<int> RunOnceAsync(CancellationToken ct);          // test seam; returns emails sent
}
```

`RunOnceAsync`: gather audit-pipeline apps (`State ∈ {PendingAudit, ReturnedFromAudit}`) whose selected suppliers have stale required fields → group by `Group` → resolve `Auditor`-role users per group → compose one aggregated email per auditor via `RegulatoryDigestEmailFactory` (041 brand shell, es-CR + `.text` twin) → send via `IEmailSender` with in-cycle backoff (allowlist applies). No outbox, no new `NotificationEvent`.

---

## Development-only trigger endpoints (Web)

For deterministic E2E (mirrors the `GET /Account/SeedUser` dev seam; 404 outside `Development`):

- `GET /Dev/RunHaciendaSync` → `HaciendaSyncService.RunOnceAsync`, returns the summary.
- `GET /Dev/RunFreshnessDigest` → `RegulatoryFreshnessDigestService.RunOnceAsync`, returns count.

(Exact route/host controller pinned at implementation; must be Development-gated.)

---

## Audit constant (Domain)

`AdminAuditEvent.SupplierHaciendaSyncFailed = "supplier.hacienda_sync_failed"` (new). Success reuses existing `SupplierRegulatoryChanged` / `SupplierRegulatoryReviewed`.
