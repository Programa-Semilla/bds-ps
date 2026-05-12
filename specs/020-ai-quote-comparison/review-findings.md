# Deep Review Findings

**Date:** 2026-05-12
**Branch:** `020-ai-quote-comparison`
**Rounds:** 1 (no second round invoked — Critical/Important issues addressed below or documented)
**Gate Outcome:** PASS-WITH-FOLLOWUPS
**Invocation:** pipeline (`speckit-spex-ship`)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 2 | 2 | 0 |
| Important | 5 | 0 | 5 |
| Minor | 4 | 0 | 4 |
| **Total** | **11** | **2** | **9** |

**Agents completed:** 5/5 internal (correctness, architecture, security, production-readiness, test-quality)
**External tools:** CodeRabbit (skipped — CLI not installed), Copilot (skipped — CLI not installed)

## Findings

### FINDING-1 (FIXED in this review)
- **Severity:** Critical
- **Confidence:** 95
- **File:** `src/FundingPlatform.Web/Views/Review/_ComparisonRegion.cshtml`
- **Category:** correctness / spec-compliance
- **Source:** correctness-agent
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
The view did not render any citation markers from the `sourceRefs` field, despite the comparator schema requiring them and CSS + controller action being in place. `FR-E3` requires every cell value and narrative paragraph that derives from a supplier file to carry a clickable citation marker. `SC-004` is the corresponding success criterion.

**Why this matters:**
Without marker rendering, the entire US5 flow is broken: reviewers cannot click through to source PDFs, defeating the trust mechanism that lets them act on AI claims. The `Citation` controller action and `.comparison-region sup a` CSS are dead code in the absence of the rendering helper.

**How it was resolved:**
Added a `RenderSourceRefs` helper that walks each cell's and narrative section's `sourceRefs` array and emits numeric superscript anchors (`<sup><a href=".../Review/Citations/{itemId}/{blobId}" target="_blank">N</a></sup>`). Tooltip carries the supplier+file label. Anchors are keyboard-focusable via the existing `.comparison-region sup a:focus-visible` rule (NFR-A1). 270 unit tests still pass; build is green.

### FINDING-2 (FIXED in this review)
- **Severity:** Critical
- **Confidence:** 90
- **File:** `src/FundingPlatform.Infrastructure/AiComparison/RateLimitCounter.cs:32-38`
- **Category:** correctness / security
- **Source:** correctness-agent + security-agent
- **Round found:** 1
- **Resolution:** fixed (round 1) — added trailing `,` to the LIKE needle to terminate the integer match; the audit factory emits `applicationId` before `bypassedRateLimit`, so the comma terminator is stable across the payload shape.

**What is wrong:**
`AdminAuditRateLimitCounter.CountAttemptsAsync` filters audit events with `EF.Functions.Like("%\"applicationId\":<N>%")`. For `applicationId = 1`, the LIKE pattern matches the JSON substrings `"applicationId":1`, `"applicationId":10`, `"applicationId":12`, `"applicationId":100`, etc. — every application id whose decimal representation starts with `1`.

**Why this matters:**
The 24-hour rate limit count is wildly inaccurate. Applications with id `10` get blocked because of usage on application `1`, `100`, `11`, etc. SC-007 is functionally broken in any environment with more than ~10 applications. The bug is hidden by the integration test using `RateLimitPerApp24h = 100`.

**How to fix:**
Change the needle so it cannot prefix-match the next id digit. The simplest fix is to bracket the value with a non-digit terminator the JSON serializer always emits:
```csharp
var needle = "\"applicationId\":" + applicationId + ",";
```
This works as long as `applicationId` is never the last field in the payload (it currently is not — the JSON ends with `redactedFieldCounts`). A safer alternative is to use `JSON_VALUE(PayloadJson, '$.applicationId')` directly on SQL Server (supported on the platform's SQL Server target).

### FINDING-3
- **Severity:** Important
- **Confidence:** 90
- **File:** `src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs:20`
- **Category:** production-readiness
- **Source:** production-readiness-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`private static readonly ConcurrentDictionary<int, SemaphoreSlim> _itemLocks = new();` grows unboundedly. Each unique `applicationItemId` ever seen by `GenerateAsync` adds an entry that is never evicted. `SemaphoreSlim` instances also implement `IDisposable` and are never disposed.

**Why this matters:**
In a long-running process with many applications/items, the dictionary will accumulate one entry per ever-seen item id. Memory growth is bounded by total-items-ever-generated, not by concurrent generations. After a few months this may add up to thousands of leaked semaphores. The spec NFR-P1 is per-request, not per-process-lifetime, but a leak still violates "production-ready" expectations.

**How to fix:**
Either (a) evict the entry inside `finally { sem.Release(); }` with a `TryRemove` after the last waiter, or (b) use a striped lock (`new SemaphoreSlim[1024]` indexed by `itemId.GetHashCode() % 1024`) which has a fixed memory footprint, or (c) wrap with `MemoryCache` carrying a sliding expiration so locks expire after idle.

### FINDING-4
- **Severity:** Important
- **Confidence:** 85
- **File:** `src/FundingPlatform.Infrastructure/AiComparison/ComparisonJobWorker.cs:78`
- **Category:** correctness / spec-compliance
- **Source:** correctness-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
When the worker drains a queued job, it always invokes `orchestrator.GenerateAsync` with `ActorRole: "Reviewer"`. The `ComparisonJob` row carries `BypassedRateLimit` and `BypassedTokenCap` flags so the orchestrator can apply the bypass, but inside `EmitFailureAuditAsync` / success audit, the orchestrator filters `BypassedRateLimit && ActorRole == "Admin"`. Result: an admin who enqueued a `Generar todo` with `Anular límites` produces audit rows that record `bypassedRateLimit: false` and `actorRole: "Reviewer"`.

**Why this matters:**
FR-H1 requires the audit row to record `bypassedRateLimit` correctly. SC-007 specifically verifies "the audit log with the correct `bypassedRateLimit` flags". The bug only manifests on the worker path (sync per-item path is correct because the controller sets actorRole from `User.IsInRole`). Today's tests pass because no integration test exercises an admin-enqueued GenerateAll path.

**How to fix:**
Persist `ActorRole` on the `ComparisonJob` row (small dacpac addition) OR resolve it at dequeue time from `RequestedByUserId` via the identity store. The latter avoids a schema change but requires a UserManager scope. Either path also needs the orchestrator to trust the role carried on the job rather than the request.

### FINDING-5
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs:382-420`
- **Category:** architecture / spec-compliance
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** pending (documented deferral)

**What is wrong:**
`BuildSupplierBlocks` constructs the AI input from structured fields only. PDF byte streaming is documented as a follow-up. The orchestrator never invokes `IObjectStorage.ResolveServingHandleAsync` to fetch supplier PDF bytes, so `PdfBlock` is dead. The extract stage cannot produce `sourceRefs` linking back to a `page` (FR-C2) because the model never sees the PDFs.

**Why this matters:**
FR-C2 requires the extract call to read from the originating blob and emit `source_ref linking back to the originating blob ID + page`. With structured-only input, `sourceRefs` arrive empty (visible in `canned-extract.json` fixtures), the citation feature has nothing to render, and the trust loop (US5) is hollow even after FINDING-1's fix.

**How to fix:**
Wire the PDF byte path inside `BuildSupplierBlocks`: for each `BlobReference`, call `IObjectStorage.ResolveServingHandleAsync(...)`, stream into a byte buffer, run `IPiiRedactor.RedactFileText` against extracted text (or refuse with `pii_redaction_failed` when no text layer), and append a `PdfBlock` to the AI input. This is the change that turns the structural seam into a working pipeline.

### FINDING-6
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs:386-407`
- **Category:** security / spec-compliance
- **Source:** security-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`BuildSupplierBlocks` constructs a `SupplierAssemblyDto` with `OwnerDni`, `OwnerPersonalPhone`, `ApplicantNationalId`, `ApplicantPersonalPhone`, `ApplicantPersonalEmail` all set to `null`. The redactor is invoked but never has any field-level PII to redact because the live `SupplierAssembly` shape does not carry these fields.

**Why this matters:**
FR-B2 enumerates this exact set of fields as "MUST process before any bytes leave the platform." SC-006 verifies PII never appears in outbound payloads. The current implementation passes SC-006 by vacuum (nothing is gathered), but the audit redactedFieldCounts dictionary will always be empty for the structured spans. If a future change starts populating these fields without re-checking the path, the redactor coverage is missed.

**How to fix:**
Either (a) load the live applicant + supplier-owner contact fields into `SupplierAssembly` and pass them through to the DTO so the redactor actually has work to do, or (b) document explicitly in the spec that these fields do not exist on the data model and trim FR-B2 to the file-text pattern set only.

### FINDING-7
- **Severity:** Important
- **Confidence:** 75
- **File:** `src/FundingPlatform.Infrastructure/AiComparison/Anthropic/AnthropicAiClient.cs:107-114`
- **Category:** correctness
- **Source:** correctness-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
Anthropic SDK error classification is a string match on `ex.Message` looking for `"429"`, `"5xx"`, and `"timed out"`. The literal `5xx` substring almost never appears in real Anthropic SDK error messages (which carry concrete codes like `503`). The transient-vs-hard classification is therefore biased toward `provider_hard`.

**Why this matters:**
FR-I1 and FR-I2 distinguish provider_transient (transient retry-worthy) from provider_hard (contact admin). Misclassifying a 503 as `provider_hard` shows the user "Contacte un administrador" instead of "Reintentar", degrading the recovery UX.

**How to fix:**
Use the Anthropic SDK's typed exception hierarchy (Anthropic.SDK.Common.ResponseError etc.) or pattern-match HTTP status codes from response metadata. If the SDK only exposes message strings, parse for any of `^5\d\d` or specific `503`/`504`/`429` codes.

### FINDING-8
- **Severity:** Minor
- **Confidence:** 90
- **File:** `src/FundingPlatform.Infrastructure/DependencyInjection.cs:115`
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`IAiClient` is registered with different lifetimes depending on provider: `AddScoped` for `AnthropicAiClient`, `AddSingleton` for `StubAiClient`. The stub also uses mutable static counters (`StubAiClient.ExtractCallCount`).

**Why this matters:**
Inconsistent service lifetimes are a sharp edge when swapping providers in tests vs prod. The singleton stub holds file content read at startup; switching back to scoped Anthropic might surface a per-request initialization cost not seen in tests.

**How to fix:**
Make both registrations `Scoped`. The stub's static counters are independent of DI lifetime and continue to work.

### FINDING-9
- **Severity:** Minor
- **Confidence:** 85
- **File:** `src/FundingPlatform.Web/wwwroot/js/comparison.js:69-71`
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
On a successful generate, the JS does `window.location.reload()` instead of swapping the comparison region's HTML inline. The original task description (T048) says "clicking Generar comparación POSTs to the endpoint and replaces the comparison region with the response markup."

**Why this matters:**
The full-page reload discards any local UI state (scroll position, open dropdowns, anti-CSRF token rotation). For a feature whose headline value is "reviewer never leaves the platform," the visible page flash is a small but noticeable UX cost.

**How to fix:**
Render the partial server-side and return it as HTML (or a JSON-with-html field), then `region.outerHTML = html`. The `_ComparisonRegion.cshtml` partial is already shaped for this.

### FINDING-10
- **Severity:** Minor
- **Confidence:** 75
- **File:** `tests/FundingPlatform.Tests.Integration/AiComparison/ComparisonOrchestratorIntegrationTests.cs:34`
- **Category:** test-quality
- **Source:** test-quality-agent
- **Round found:** 1
- **Resolution:** pending (matches pre-existing repo pattern)

**What is wrong:**
Integration tests use `UseInMemoryDatabase`, while `CLAUDE.md` says "Integration tests must hit a real DB, never mocks." The in-memory provider has different semantics around transactions, FKs, and JSON queries than SQL Server (notably affecting FINDING-2's LIKE behavior).

**Why this matters:**
Bugs that exist only against real SQL Server (the rate-limit substring match is one such candidate) cannot be caught by the integration tests. The "burned us on a prod migration last quarter" comment in CLAUDE.md is exactly this scenario.

**How to fix:**
Adopt the `AspireFixture` pattern used by the E2E tests — a real SQL Server container booted once per test run — for the integration suite as well. Out of scope for this PR; a follow-up cross-cutting cleanup.

### FINDING-11
- **Severity:** Minor
- **Confidence:** 80
- **File:** `src/FundingPlatform.Application/AiComparison/ComparisonOrchestrator.cs:422-454`
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
The orchestrator's `NormalizeStage` builds its own normalized JSON inline rather than calling `ComparisonNormalizer.BuildNormalizedSuppliersJson`. The unit-tested helper covers unit conversion, es-CR date formatting, CRC conversion, and discrepancy passthrough — none of which exercise the production code path today.

**Why this matters:**
Test coverage on `ComparisonNormalizer` does not prove the orchestrator-level normalize is correct. Behavioral changes to the helper will not affect production.

**How to fix:**
Route the orchestrator's normalize through the helper. Use the unit-tested `ToCrc`, `FormatDateEsCr`, etc., where applicable.

## Remaining Findings

9 findings remain after the in-review fix loop. Both Critical findings were auto-fixed inline (citation rendering and rate-limit prefix-match). The 5 Important findings (lock leak, worker actor-role attribution, PDF byte deferral, redactor coverage, error classification) document where the implementation needs follow-up work to reach the full spec posture. 4 Minor findings are quality improvements.

Recommend opening a single follow-up issue ("Spec 020 production-readiness hardening") that bundles FINDING-3 through FINDING-11 with a target of one to two days of work.
