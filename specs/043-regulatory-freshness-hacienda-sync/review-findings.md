# Deep Review Findings

**Date:** 2026-06-21
**Branch:** 043-regulatory-freshness-hacienda-sync
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** manual (review-code quality gate, deep-review extension)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 6 | 6 | 0 |
| Minor | 11 | 4 | 7 |
| **Total** | **17** | **10** | **7** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality). External tools (CodeRabbit/Copilot) disabled via `--no-external`.
**Agents failed:** none. Security agent found zero issues (verified twice).

## Findings

### FINDING-1 (Important → fixed)
- **Severity:** Important · **Confidence:** 90 · **Category:** production-readiness
- **File:** src/FundingPlatform.Application/Regulatory/HaciendaSyncOptions.cs:15
- **Source:** production-readiness (P-2)

**What is wrong:** `HaciendaSyncOptions.Provider` defaulted to `"Live"`, contradicting the Fake-by-default safety posture. The DI gate reads raw config `?? "Fake"`, so the live API is off today — but any future code reading `options.Provider` would silently flip to hitting `api.hacienda.go.cr` in dev/test (a Constitution-III trap).

**Why this matters:** Two sources of truth with opposite defaults; the safe one is the one being read today. A refactor toward the bound-options path would call the live API in tests.

**How it was resolved:** Default changed to `"Fake"` so the options object agrees with the DI/AppHost wiring; both now default offline.

### FINDING-2 (Important → fixed)
- **Severity:** Important · **Confidence:** 82 · **Category:** production-readiness
- **File:** src/FundingPlatform.Infrastructure/Hacienda/LiveHaciendaApiClient.cs:11; src/FundingPlatform.Infrastructure/DependencyInjection.cs:294
- **Source:** production-readiness (P-4)

**What is wrong:** The class doc-comment falsely claimed the client was a "Typed HttpClient configured via AddHttpClient" — it is a manually-constructed singleton `new HttpClient`. The singleton also had no `PooledConnectionLifetime`, so it could pin a stale DNS resolution indefinitely.

**Why this matters:** Misleading docs mislead the next maintainer; an unbounded-lifetime singleton HttpClient is the textbook DNS-staleness caveat (low risk at once-daily cadence, but real).

**How it was resolved:** Corrected the doc comment to "manually-constructed long-lived HttpClient (not AddHttpClient)" and wrapped the client in a `SocketsHttpHandler { PooledConnectionLifetime = 15 min }` for periodic DNS refresh.

### FINDING-3 (Important → fixed)
- **Severity:** Important · **Confidence:** 88 · **Category:** production-readiness
- **File:** src/FundingPlatform.Infrastructure/BackgroundServices/RegulatoryFreshnessDigestService.cs:152
- **Source:** production-readiness (P-6)

**What is wrong:** The digest send loop relied on `SendAsync` *returning* an outcome; a thrown exception (bad template/recipient) propagated out of the per-auditor `foreach`, aborting digests to every subsequent auditor for that cycle. `StageExpiryReminderService` wraps each send in try/catch.

**Why this matters:** One malformed recipient silently suppresses every other auditor's stale-value notice for the day — a partial silent failure of US4/SC-005.

**How it was resolved:** Wrapped the per-auditor build+send in try/catch (rethrow on cancellation, log+continue otherwise), mirroring `StageExpiryReminderService`.

### FINDING-4 (Important → fixed)
- **Severity:** Important · **Confidence:** 88 · **Category:** test-quality
- **File:** tests/FundingPlatform.Tests.Integration/BackgroundServices/HaciendaSyncTests.cs:21; tests/FundingPlatform.Tests.Integration/Suppliers/RegulatoryFreshnessQueryTests.cs:22
- **Source:** test-quality (T-1)

**What is wrong:** Both integration files claimed the RowVersion concurrency-skip (FR-025) was "covered by E2E", but no E2E forces a concurrent edit mid-sync — the path is verified by construction only.

**Why this matters:** A false test-coverage claim hides a real gap; FR-025 could break and every test would still pass.

**How it was resolved:** Corrected both comments to state honestly that FR-025 is not deterministically tested (EF InMemory cannot enforce row-version concurrency; no E2E races it). The gap is now documented, not masked. (A real-SQL concurrency test remains a recommended follow-up — see Remaining.)

### FINDING-5 (Important → fixed)
- **Severity:** Important · **Confidence:** 85 · **Category:** test-quality
- **File:** tests/FundingPlatform.Tests.Unit/Application/RegulatoryFreshnessCopyTests.cs (new); was a gap at RegulatoryFreshnessBlockTests / RegulatoryFreshnessWarningDigestTests
- **Source:** test-quality (T-2, T-3)

**What is wrong:** No test asserted the block message enumerates *every* stale provider+field (FR-007 "all MUST be listed") or that the warning *names* the provider/field (FR-010) — the E2E tests used a single supplier and generic substrings.

**Why this matters:** A bug surfacing only the first finding (or a warning that renders but names nothing) would pass every prior test.

**How it was resolved:** Added `RegulatoryFreshnessCopyTests` (unit) asserting `BuildBlockMessage`/`BuildWarningMessage` enumerate multiple providers + all field labels + the dated/"sin revisar" forms. Also strengthened the warning E2E (reviewer + auditor screens) to assert the provider name appears, and the digest E2E to assert the body names the provider (T-6 below).

### FINDING-6 (Important → fixed)
- **Severity:** Important · **Confidence:** 80 · **Category:** test-quality
- **File:** tests/FundingPlatform.Tests.E2E/Tests/RegulatoryFreshnessWarningDigestTests.cs:60,75,87
- **Source:** test-quality (T-3, T-4)

**What is wrong:** The warning E2E asserted only element-visibility / a generic "sin revisar" label; the digest E2E matched only subject + recipient. Neither verified the provider was actually *named* (FR-010 / SC-005).

**How it was resolved:** Added `ToContainTextAsync("Supplier ")` on both the reviewer and auditor warning panels, and `HtmlBody+TextBody Does.Contain("Supplier ")` on the captured digest.

### FINDING-7 (Minor → fixed)
- **Severity:** Minor · **Confidence:** 70 · **Category:** correctness
- **File:** src/FundingPlatform.Infrastructure/BackgroundServices/DailyRunSchedule.cs:48
- **Source:** correctness (C-1), architecture (—)

**What is wrong:** The Windows timezone fallback used `"Central Standard Time"`, which observes US DST — drifting the daily run time by an hour for half the year on a Windows host, contradicting the "CR has no DST" intent.

**How it was resolved:** Replaced with `"Central America Standard Time"` (the Windows id that is UTC-6 with no DST) + an explanatory comment. (Moot on the Linux target where `America/Costa_Rica` resolves, but now correct on Windows.)

### FINDING-8 (Minor → fixed)
- **Severity:** Minor · **Confidence:** 80 · **Category:** architecture
- **File:** src/FundingPlatform.Web/Controllers/FundingAgreementController.cs:170
- **Source:** architecture (A-1)

**What is wrong:** The regulatory-stale *business* block reused `LogUnauthorized(...)`, polluting the authorization-rejection log taxonomy with a domain-precondition refusal.

**How it was resolved:** Replaced with `_logger.LogInformation("... generation blocked: N stale regulatory finding(s)")`, matching how the sibling checklist-incomplete refusal is (not) logged.

### FINDING-9 (Minor → fixed)
- **Severity:** Minor · **Confidence:** 70 · **Category:** architecture
- **File:** src/FundingPlatform.Infrastructure/Hacienda/HaciendaStatusMapper.cs:32
- **Source:** architecture (A-3)

**What is wrong:** `HaciendaStatus.DesinscritoDeOficio` (enum 8) is never produced by the mapper, with no comment explaining whether that is deliberate.

**How it was resolved:** Added a comment that `fe/ae`'s `estado` vocabulary cannot distinguish "de oficio", so that status is unreachable via sync (manual-only) — a documented decision rather than a silent gap.

### FINDING-10 (Minor → fixed)
- **Severity:** Minor · **Confidence:** 85 · **Category:** test-quality
- **File:** tests/FundingPlatform.Tests.Integration/BackgroundServices/HaciendaSyncTests.cs (new BatchSize test)
- **Source:** test-quality (T-5)

**What is wrong:** `BatchSize`/throttling (FR-017) was never exercised with more suppliers than one batch, so a "load-all" impl was indistinguishable from a batched one.

**How it was resolved:** Added `BatchSize_ProcessesAllSuppliersAcrossMultipleBatches` (BatchSize=2, 5 suppliers → 3 batches, asserts all 5 checked + changed).

## Remaining Findings (Minor — documented, non-blocking)

These are accepted/known-limitation Minors that do not block the gate. They are recorded for the human reviewer.

- **C-2 / A-2 (correctness/architecture):** a non-cédula (passport) supplier yields `<9` digits and is recorded "verificación fallida" every cycle. By-design per FR-018 (invalid id = failure), but an operator may want a distinct "no aplica" outcome. Also the digit-length rule lives in the worker while the live client only rejects empty — divergent thresholds. Recommend a shared id-validity helper + a "not-applicable" outcome if non-cédula providers are expected.
- **P-3 (production-readiness):** the in-process daily scheduler skips a day if the host is down at the scheduled time (no catch-up/durable last-run). Consistent with the deliberate in-process design ([research D4](research.md#d4--daily-scheduling-at-a-wall-clock-morning-time-resolves-165)) over cron; self-recovers next day; staleness surfaced by the gate+digest. Follow-up decision: persist last-run + catch-up, or accept.
- **P-1 / P-5 (production-readiness):** the sync loads all supplier ids, and the digest loads all audit-pipeline apps, into memory. Bounded in practice (small catalog / human-bottlenecked audit pipeline). Page/project to DTO if either grows large.
- **A-4 (architecture):** `ReviewFreshness` renders the (unused) `System` source as "por el sistema (sistema)" — redundant, but the `System` source is never produced by this slice.
- **A-5 (architecture):** the Web `RegulatoryFreshnessResources.Warning_Heading` re-export is unused (the warning partial reads `RegulatoryFreshnessCopy.WarningHeading` directly).
- **A-6 (architecture):** `HaciendaSyncOptions.BaseUrl` is bound but unconsumed (the DI live-client path re-reads the raw config key + its own literal default).
- **A-7 (architecture):** the digest reuses `HaciendaSyncOptions.RunAtLocalTime` for its schedule, coupling its run time to the sync's config. Consider a dedicated digest run-time knob.
- **T-6 (test-quality):** the E2E `FakeHaciendaApiClient` static state is process-wide with no reset seam; isolation holds today only because tests key by unique cédulas.
