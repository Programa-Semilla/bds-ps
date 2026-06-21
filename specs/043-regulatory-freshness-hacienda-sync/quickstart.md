# Quickstart: Regulatory Freshness Gating + Hacienda API Sync

How to build, run, and verify slice D locally and in tests.

## Build

```bash
dotnet build FundingPlatform.slnx
```

Schema change (3 new `dbo.Suppliers` columns) deploys automatically when AppHost runs outside ephemeral mode.

## Run locally (Aspire)

```bash
dotnet run --project src/FundingPlatform.AppHost
```

- Dev defaults to `Regulatory:HaciendaSync:Provider=Fake` so no live API call happens; flip to `Live` in `appsettings`/azd-env to hit `https://api.hacienda.go.cr/fe/ae`.
- The daily workers schedule to `Regulatory:HaciendaSync:RunAtLocalTime` (default `06:00` CR). To exercise them immediately in Development, hit the dev trigger endpoints (below).

## Verify the two capabilities

### 1. Staleness block (US1)
1. As an auditor, take an application to the audit stage (`PendingAudit`) with an approved item whose selected supplier has a CCSS `LastReviewedAt` older than 30 days (or null).
2. Attempt to generate/confirm the agreement or release for signature → **blocked**, with an es-CR message naming the provider, the stale field (CCSS/Caja), and the last-reviewed date.
3. As an auditor, use slice-A **"Reviewed — No Change"** on that field → retry the advance → **succeeds**.

### 2. Daily Hacienda sync (US2/US3)
- Development trigger: `GET /Dev/RunHaciendaSync` runs one cycle immediately and returns `{checked, changed, unchanged, failed}`.
- With `Provider=Fake`, stage outcomes via `FakeHaciendaApiClient` (al día / moroso / no-inscrito / 404 / failure).
- Verify on a supplier detail screen: Hacienda status updated, freshness shows "actualizado … por sistema", and (on failure) the last-sync outcome shows "verificación fallida" + reason. Admin supplier list → filter "verificación fallida".
- Verify audit trail: `supplier.regulatory_changed`/`supplier.regulatory_reviewed` (source `Api`) on success; `supplier.hacienda_sync_failed` on failure.

### 3. Early warning + digest (US4)
- Reviewer send-to-audit screen + auditor screen show a non-blocking warning naming stale providers/fields.
- Development trigger: `GET /Dev/RunFreshnessDigest` → digest email(s) to group-scoped auditors; capture in smtp4dev (seed auditor emails are allowlisted under `@programa-semilla.test`).

## Tests

```bash
# Unit — HaciendaStatusMapper table; Supplier sync/freshness domain methods
dotnet test tests/FundingPlatform.Tests.Unit

# Integration (real DB) — RegulatoryFreshnessService queries; HaciendaSyncService.RunOnceAsync
#   with FakeHaciendaApiClient (changed/unchanged/404/failure/concurrency); digest service
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (filtered) — the delivery gate for this feature
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~RegulatoryFreshness|FullyQualifiedName~HaciendaSync"
```

- E2E uses `AspireFixture` (ephemeral SQL, `Provider=Fake`) and the `/Dev/Run*` endpoints for deterministic sync/digest passes. The live Hacienda API is never called in any test.
- Regression: re-run the slice-C funding-agreement / audit-workflow E2E classes since the gate is inserted at the auditor advance actions.

## Verify the live contract (manual, optional)

```bash
curl "https://api.hacienda.go.cr/fe/ae?identificacion=2100042005"   # 200 al día sample
curl "https://api.hacienda.go.cr/fe/ae?identificacion=900930330"    # 200 estado:"No inscrito"
curl -i "https://api.hacienda.go.cr/fe/ae?identificacion=999999999" # HTTP 404 information-not-available
```

## Delivery gate

Per CLAUDE.md: a feature is delivered when the **filtered E2E tests for this change have been personally executed and are green**. Run the filtered E2E above plus the slice-C audit/funding-agreement regression before marking done.
