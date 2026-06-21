# Phase 0 Research: Regulatory Freshness Gating + Hacienda API Sync

All three spec Open Questions plus the scheduling, notification-scoping, and test-seam unknowns are resolved below. Decisions are grounded in live API sampling (2026-06-21) and a codebase map of the slice-A/slice-C seams.

---

## D1 — Hacienda `fe/ae` contract & status mapping (resolves OQ1)

**Decision.** Map the live `GET https://api.hacienda.go.cr/fe/ae?identificacion={id}` response to the existing `HaciendaStatus` enum as follows. Only `situacion` drives the mapping; `nombre`/`regimen`/`actividades` are ignored.

| API outcome | `situacion.estado` | `situacion.moroso` | `situacion.omiso` | → `HaciendaStatus` |
|---|---|---|---|---|
| 200 | `Inscrito` | `NO` | `NO` | `AlDia` (2) |
| 200 | `Inscrito` | `SI` | * | `EstadoMoroso` (3) |
| 200 | `Inscrito` | `NO` | `SI` | `CobroAdministrativo` (4) |
| 200 | `Desinscrito` | `NO` | * | `DesinscritoAlDia` (5) |
| 200 | `Desinscrito` | `SI` | * | `DesinscritoMoroso` (7) |
| 200 | `No inscrito` | * | * | `SinInscripcion` (1) |
| 404 | — (`{code:404,status:"Information no available…"}`) | — | — | `SinInformacion` (6) |
| transport error / 5xx / timeout / unparseable body / malformed-or-missing local id | — | — | — | **failure** — no value change |

**Rationale.** Live sampling on 2026-06-21:
- `2100042005` → 200 `{situacion:{estado:"Inscrito",moroso:"NO",omiso:"NO"}}` → al día.
- `117640123` → 200 `{estado:"Desinscrito",moroso:"SI"}` (+ a `mensaje`) → desinscrito moroso.
- `900930330` → 200 `{estado:"No inscrito",moroso:"NO"}` → **sin inscripción** (a definite 200 answer).
- `3101000038`, `1551240123`, `310123456`, `999999999` → **HTTP 404** `{code:404,status:"Information no available on this system…"}`.

The critical finding (a correction to the spec's first-pass guess): **404 is not "not registered"** — it is "information not available", which is exactly what the existing `SinInformacion` (6) enum value means. A real "not registered" answer arrives as a **200 with `estado:"No inscrito"`** → `SinInscripcion` (1). Treating 404 as `SinInformacion` (a check that occurred, refreshing the timestamp) rather than as a hard `SinInscripcion` avoids punitively marking a provider unregistered on an inconclusive lookup.

**Alternatives considered.**
- *404 → `SinInscripcion`* (spec's first guess): rejected — conflates "no info" with the distinct 200 "No inscrito" answer; would mislabel providers.
- *404 → failure (no change)*: rejected — the API answered authoritatively ("no info on this system"); recording `SinInformacion` + refreshing the timestamp is more truthful and keeps the field fresh. (If 404-flapping on real providers is observed post-ship, revisit toward treating 404 as a soft outcome — noted as a watch item, not a v1 concern.)
- `DesinscritoDeOficio` (8) and `CobroAdministrativo` (4): `fe/ae` exposes no field distinguishing "de oficio"; `omiso=SI` is the closest signal for an administrative-collection condition. `DesinscritoDeOficio` is therefore **never auto-set** by the sync (auditors can still set it manually via slice A). `CobroAdministrativo` for `Inscrito`+`omiso=SI` is a best-effort mapping; flagged for a quick stakeholder confirmation but does not block implementation.

**Edge specifics.** The mapper is pure and total over `HaciendaLookupResult`: it returns a non-null `HaciendaStatus` for every `Found`/`NotRegistered` result and is never called for `Failed`. Parsing is defensive (case-insensitive `estado`/`moroso`/`omiso`; an unrecognized `estado` value → `Failed` so it surfaces as a failure rather than a silent mismapping).

---

## D2 — Referenced-provider scope for the gate (resolves OQ2)

**Decision.** "Providers the application relies on" = the **distinct `Supplier`s referenced by the application's approved items via `Item.SelectedSupplierId`** — exactly the suppliers that will appear in the funding agreement.

**Rationale.** `Item.SelectedSupplierId` (set by `Item.Approve(supplierId, comment)`, spec 039) is the single authoritative "chosen supplier per line". The funding agreement is generated from these selections. Checking freshness of precisely these suppliers means the block protects the agreement's actual counterparties and nothing more.

**Alternatives considered.**
- *All suppliers with any attached quotation*: rejected — would block on rejected/unselected quotes irrelevant to the agreement, producing false blocks.
- *All suppliers anywhere on the application regardless of approval*: rejected — same over-blocking; also some items may not be approved at the audit stage gate (the gate runs when the agreement is about to be produced, by which point items are approved).

**Implementation note.** The freshness service loads the application with `Items → SelectedSupplier`, takes `Items.Where(i => i.SelectedSupplierId != null).Select(i => i.SelectedSupplier).Distinct()`, and computes `StaleRequiredFields(window, now)` per supplier.

---

## D3 — Stale-value notification: daily digest, direct-send, audit-pipeline-scoped (resolves OQ3)

**Decision.** A **daily digest** emailed **directly via `IEmailSender`** (not the per-application notification outbox), modeled on `StageExpiryReminderService`. Scope: for each application currently in the audit pipeline (`PendingAudit` or `ReturnedFromAudit`) whose selected suppliers have ≥1 stale required field, group by the application's `Group`; resolve the `Auditor`-role users in that group (slice-C group→auditor resolution); send each auditor **one aggregated email** listing the stale providers/fields for the applications in their group(s).

**Rationale.**
- The per-application transactional outbox is keyed by `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` — a per-application idempotency model. A daily, multi-application, per-auditor digest does not fit it. `StageExpiryReminderService` already establishes the correct pattern for recurring reminders: a `BackgroundService` that composes and sends directly with backoff, using its own once-per-period cadence for dedup. Reusing that pattern avoids forcing a digest into the wrong abstraction and means **no new `NotificationEvent` enum value**.
- Scoping by audit-pipeline applications (rather than "every stale supplier in the catalog") keeps the digest **actionable** — auditors only hear about providers blocking work they can actually act on — and naturally reuses the existing group→auditor recipient resolution, satisfying "scoped to those providers, consistent with other auditor notifications."

**Dedup / cadence.** The digest worker runs once per configured day (next-run-time scheduling, D4), so it fires once daily by construction; transient send failures retry with in-cycle exponential backoff (the `SendWithBackoffAsync` pattern). No persistent per-day mask is needed because the schedule guarantees a single daily pass.

**Alternatives considered.**
- *Per-application outbox event (`NotificationEvent.RegulatoryFreshnessStale`)*: rejected — recurring daily reminders don't map to the one-shot per-application idempotency key; would need a synthetic per-day anchor and bespoke recipient fan-out.
- *Digest of all stale suppliers catalog-wide*: rejected — noisy and not group-scopeable (suppliers have no group); not actionable for a specific auditor.
- *Notify once when a value first crosses the threshold*: viable but needs persistent "already notified" state per (supplier, field); deferred — the daily actionable digest is simpler and self-limiting.

**Brand.** The digest email renders through the spec-041 `_EmailLayout` brand shell with an es-CR body + `.text` twin, via a new `RegulatoryDigestEmailFactory` mirroring `StageReminderEmailFactory`.

---

## D4 — Daily scheduling at a wall-clock morning time (resolves §16.5)

**Decision.** Both daily workers (`HaciendaSyncService`, `RegulatoryFreshnessDigestService`) are `BackgroundService`s that loop: compute the `TimeSpan` until the next occurrence of the configured local time-of-day (`RunAtLocalTime`, default `06:00` in America/Costa_Rica), `Task.Delay` to it, run one cycle, repeat. Each exposes a **public `RunOnceAsync(CancellationToken)`** seam (mirroring `StageExpiryReminderService.ExecuteOneCycleAsync`) so integration + E2E tests run a deterministic pass without waiting on the clock. A cycle exception is caught/logged and never crashes the host (FR-024).

**Rationale.** The existing background services use `PeriodicTimer`/`Task.Delay` *intervals* relative to startup; none runs at a wall-clock time. §16.5 requires a daily **morning** run, so an interval-from-startup is insufficient (startup time is arbitrary). Computing the delay to the next configured local time is the minimal addition that satisfies the requirement while preserving the established structure (startup-resilient loop + public single-cycle test seam). The timezone for "morning" is the app operating zone (Costa Rica); staleness math itself remains UTC-instant-based (FR-003) and is unaffected by the run time.

**Alternatives considered.**
- *Pure 24h `PeriodicTimer`*: rejected — would run at startup-time + N×24h, not a defined morning.
- *A cron library (e.g. Quartz/NCronJob)*: rejected — new managed dependency for a one-line next-run calculation; violates reuse-first (Constitution VI).
- *External scheduler (cron/Azure)*: rejected — the VM/Aspire deployment runs everything in-process; an in-app worker is consistent with `EmailDispatchWorker`/`StageExpiryReminderService` and testable via the `RunOnceAsync` seam.

---

## D5 — `IHaciendaApiClient` test seam (live API never called in tests)

**Decision.** Define `IHaciendaApiClient` in Application; register the implementation by a config gate `Regulatory:HaciendaSync:Provider`: `Live` → `LiveHaciendaApiClient` (typed `HttpClient` via `IHttpClientFactory`), anything else (default for Aspire dev + E2E ephemeral) → `FakeHaciendaApiClient`. This mirrors the shipped `AiComparison:Provider` Anthropic/Stub gate (`StubAiClient`). `FakeHaciendaApiClient` returns canned/configurable results keyed by identification, exposes static counters (`LookupCallCount`) + a `Reset()` for test isolation, and lets a test stage specific outcomes (al día / moroso / no-inscrito / 404 / failure).

**Rationale.** Constitution III + the spec's hard rule "the live API is never called in tests." The `StubAiClient` pattern is the proven in-repo mechanism: a config-selected fake registered in Infrastructure DI, defaulted to the fake in dev/test, with static counters tests assert on. AppHost sets the provider to `Fake` for ephemeral E2E (the `AspireFixture` boots with `--EphemeralStorage=true`); real envs set `Live`.

**Alternatives considered.**
- *WireMock/HTTP-intercept in tests*: rejected — heavier, new dependency, and the codebase already standardizes on config-gated fakes.
- *Inject the fake only in integration, real client in E2E*: rejected — E2E must stay offline and deterministic; the config gate covers both.

**No new managed dependency.** `LiveHaciendaApiClient` uses `System.Net.Http.HttpClient` + `System.Text.Json`, both in-framework. `services.AddHttpClient<LiveHaciendaApiClient>()` is part of `Microsoft.Extensions.Http` already transitively present. (Confirm at implementation; if absent it is a first-party MS package, but reuse check expects it present.)

---

## D6 — Audit + sync-failure persistence (reuses slice A, minimal additions)

**Decision.**
- Successful sync writes reuse slice-A audit verbs: a changed value → `AdminAuditEvent` `supplier.regulatory_changed` with `source=Api`, `kind=Changed`, old/new; an unchanged value → `supplier.regulatory_reviewed` with `source=Api`, `kind=ReviewedNoChange`. (Matches the spec's "reuses slice A's `AdminAuditEvent` shape and `RegulatoryReviewSource.Api`.")
- A failed sync writes **one new verb** `supplier.hacienda_sync_failed` (payload: `{supplierId, identificacion, reason}`), routed via the existing `supplier.` prefix in `AdminAuditEventWriter`.
- Per-provider sync **outcome** lives on `Supplier` (new columns `HaciendaSyncAttemptAt`, `HaciendaSyncOutcome`, `HaciendaSyncError`) so the supplier detail screen and admin-list filter read it directly without scanning the audit log (mirrors how slice A reads freshness from the entity, not the audit trail).

**Rationale.** Maximizes reuse of the slice-A model; only the failure case is genuinely new. Storing outcome on the entity keeps the "verificación fallida" filter a simple indexed predicate (FR-020) and avoids audit-log scans on a hot admin list.

**Concurrency.** The sync saves each supplier individually under the existing `Supplier.RowVersion`; a `DbUpdateConcurrencyException` (a concurrent auditor edit) is caught, logged, and that supplier is skipped this cycle (FR-025) — never overwriting the auditor's change.

---

## D7 — Where the hard gate is enforced

**Decision.** Enforce the freshness block server-side at the auditor advance actions, calling `IRegulatoryFreshnessService.GetStaleFindingsForApplicationAsync` and refusing (es-CR message enumerating provider+field+last-reviewed) when findings exist — exactly mirroring the existing `IsAuditChecklistCompleteAsync` gate shape:
- `FundingAgreementController.Generate` (auditor path) — cannot produce the PDF while stale.
- `AuditWorkflowService.ReleaseForSignatureAsync` and the `ConfirmAgreementPdf` path — cannot confirm/release while stale (defense in depth so a crafted POST cannot bypass, FR-009).

The same service backs the **non-blocking** warning partial on `Review.cshtml` (reviewer send-to-audit) and `Views/Audit/*` (auditor), which renders findings but does not block (FR-010).

**Rationale.** These are the precise transitions that move an application toward signature (slice C). Gating at both the generate and the release/confirm points guarantees no path advances a stale application, while the early warning gives reviewers/auditors a heads-up before they hit the wall. Reusing the checklist-gate shape keeps the code idiomatic.

---

## Cross-cutting confirmations

- **No new managed dependency** (built-in HTTP + JSON). Reuse-first satisfied.
- **No new `ApplicationState`, no new `NotificationEvent`** — the gate uses existing states; the digest sends directly.
- **Schema change is dacpac-only**: three nullable columns on `dbo.Suppliers` + EF config; `HaciendaSyncOutcome` mapped `HasConversion<byte?>()` (the slice-C/040 lesson: TINYINT enums need explicit conversion or real-SQL materialization throws Byte→Int32).
- **E2E determinism**: Development-only trigger endpoints run the sync/digest once on demand; `FakeHaciendaApiClient` supplies outcomes; smtp4dev captures the digest (seed auditor emails are in the `@programa-semilla.test` allowlist).
