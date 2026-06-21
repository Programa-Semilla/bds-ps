# Code Review Guide: Regulatory Freshness Gating + Hacienda API Sync

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-21

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on
high-level questions that need human judgment. Spec compliance is 25/25
(detailed matrix in the console report); this guide covers the *judgment calls*.

**Changed files:** ~30 source/test/schema files across Domain, Application,
Infrastructure, Web, Database, and the three test projects (the rest of the 73-file
diff is the spec docs). New: 2 background services, 1 HTTP client + 1 fake + 1
pure mapper, 1 freshness service, 1 digest factory + 2 email views, 1 ViewComponent
+ partial, 1 dev controller, 1 enum, 3 `Supplier` columns. Edited: `Supplier`,
`AuditWorkflowService`, `FundingAgreementController`, `AdminSuppliersController` +
supplier views, DI, AppHost, appsettings, `ReviewFreshness`/error-translator.

### Understanding the changes (8 min)

- Start with [`Supplier.cs`](../../src/FundingPlatform.Domain/Entities/Supplier.cs) — the freshness predicate (`IsRegulatoryStale`/`StaleRequiredFields`) and the sync mutators (`ApplyHaciendaSyncResult`/`RecordHaciendaSyncFailure`) are the domain core both capabilities build on.
- Then [`HaciendaSyncService.cs`](../../src/FundingPlatform.Infrastructure/BackgroundServices/HaciendaSyncService.cs) — the daily worker + `RunOnceAsync` seam; per-provider scope, FK-safe sentinel actor, RowVersion skip, failure isolation.
- Then [`RegulatoryFreshnessService.cs`](../../src/FundingPlatform.Infrastructure/Services/RegulatoryFreshnessService.cs) — the one query that backs both the hard gate (US1) and the warning (US4).
- Question: the shared es-CR copy lives in Application ([`RegulatoryFreshnessCopy`](../../src/FundingPlatform.Application/Regulatory/RegulatoryFreshnessCopy.cs)) so Infrastructure + Web both consume it (spec-034 precedent). Is that the right layering, or should the gate message be built only in Web with the service returning pure data?

### Key decisions that need your eyes (12 min)

**System-sentinel as the automated actor** (`HaciendaSyncService.cs` RunOnceAsync, relates to [FR-014](spec.md#functional-requirements))
The sync attributes the audit `ActorUserId` and `Supplier.HaciendaLastReviewedBy` to the real system-sentinel user id (resolved via `IgnoreQueryFilters()`), because both carry FKs to `AspNetUsers` — the literal `"system"` string failed on real SQL (E2E-caught; InMemory hid it).
- Question: is leaning on the spec-009 sentinel acceptable, or should slice D seed its own dedicated `hacienda-sync` service principal? The run aborts (logged) if no sentinel exists — is silent-abort the right failure mode, or should it throw/alert?

**Offline-default provider** (`DependencyInjection.AddRegulatoryFreshness`, [FR-012](spec.md#functional-requirements))
The DI gate defaults to the **Fake** client and appsettings no longer pins `Provider` — so dev/E2E never hit the live API even when Aspire's `WithEnvironment` forwarding doesn't override appsettings (it didn't in the fixture). Mirrors `AiComparison`'s Stub default.
- Question: is "fail offline unless prod explicitly opts into Live" the right default? It means a prod env that forgets `Regulatory:HaciendaSync:Provider=Live` silently runs the Fake (no real sync). Acceptable, or should prod fail-fast when the live API isn't configured?

**Live client uses a manual long-lived `HttpClient`** (`DependencyInjection`, [`LiveHaciendaApiClient.cs`](../../src/FundingPlatform.Infrastructure/Hacienda/LiveHaciendaApiClient.cs))
`Microsoft.Extensions.Http` isn't referenced in Infrastructure, so the live client is registered with a hand-built singleton `HttpClient` (BaseAddress + 30s timeout) instead of `IHttpClientFactory` — honoring "no new managed dependency".
- Question: acceptable for a once-daily worker, or worth adding the first-party `Microsoft.Extensions.Http` package for proper handler rotation?

**Digest is direct-send, not the outbox** (`RegulatoryFreshnessDigestService.cs`, [FR-022](spec.md#functional-requirements) vs [research D3](research.md#d3--stale-value-notification-daily-digest-direct-send-audit-pipeline-scoped-resolves-oq3))
FR-022's literal text says "email outbox" but the plan (research D3) resolved it to a direct-send daily digest (the `StageExpiryReminderService` pattern) — a recurring multi-app per-auditor digest doesn't fit the per-application outbox idempotency key. Scoped to audit-pipeline apps (`PendingAudit`/`ReturnedFromAudit`) → group → auditors.
- Question: agree the outbox is the wrong abstraction here? And is the audit-pipeline scoping (vs every catalog-wide stale provider) the right actionable set? **FR-022's spec wording is a spec-evolution candidate** to match the plan.

**404 → `SinInformacion`, not `SinInscripcion`** ([`HaciendaStatusMapper.cs`](../../src/FundingPlatform.Infrastructure/Hacienda/HaciendaStatusMapper.cs), [research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1))
[FR-015](spec.md#functional-requirements) says "not registered → no inscripción", but live sampling showed 404 = "information not available" (→ `SinInformacion`) is distinct from a 200 `estado:"No inscrito"` (→ `SinInscripcion`). The mapper treats them differently.
- Question: confirm 404→`SinInformacion` is right (refresh timestamp, don't punitively mark unregistered). The `Inscrito+omiso=SI → CobroAdministrativo` row (T043) is the one mapping not live-confirmed — ship best-effort or block on confirmation?

### Areas where I'm less certain (5 min)

- [`RegulatoryFreshnessDigestService.cs`](../../src/FundingPlatform.Infrastructure/BackgroundServices/RegulatoryFreshnessDigestService.cs) reuses `HaciendaSyncOptions.RunAtLocalTime` for the digest schedule — both workers fire at the same wall-clock time with no ordering guarantee, so a given day's digest may reflect the *previous* day's sync. Acceptable for a daily cadence?
- Integration tests use EF InMemory (project precedent), so the two real-SQL defects (FK actor, RowVersion) were only caught by E2E. The **`RowVersion` concurrency-skip (FR-025)** path is asserted nowhere except by construction — there's no test that forces a concurrent edit mid-sync.
- [`ReviewFreshness.Describe`](../../src/FundingPlatform.Web/Helpers/ReviewFreshness.cs) ([FR-020/T024](spec.md#functional-requirements)) now renders Api/System sources as "por el sistema (Hacienda)" — I changed slice-A copy + its test. Is the wording right for the supplier detail?

### Deviations and risks (5 min)

All deviations are logged in [tasks.md → Deviations](tasks.md). The two that touch *spec text* (not just impl detail):

- [FR-015](spec.md#functional-requirements)/[FR-016](spec.md#functional-requirements): refined by [research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1) (404 vs "No inscrito"). Plan-reconciled. Question: update the FR text to match, or leave the plan as the authority?
- [FR-022](spec.md#functional-requirements): "outbox" → direct-send per [research D3](research.md#d3--stale-value-notification-daily-digest-direct-send-audit-pipeline-scoped-resolves-oq3). Plan-reconciled. Spec-evolution candidate.
- Risk: prod must set `Provider=Live` + (if Live) the daily sync writes to `dbo.Suppliers` for the whole catalog under the sentinel actor. No measured perf budget at catalog scale beyond `BatchSize` throttling ([FR-017](spec.md#functional-requirements)). Question: is the current catalog size safely within one daily pass?
