# Deep Review Findings

**Date:** 2026-05-01
**Branch:** 014-azure-blob-storage
**Rounds:** 1
**Gate Outcome:** PASS-WITH-FINDINGS
**Invocation:** quality-gate (speckit-spex-ship pipeline, partial implementation)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 4 | 4 | 0 |
| Minor | 9 | 0 | 9 |
| **Total** | **13** | **4** | **9** |

**Agents completed:** 5/5 (inline — sub-agent dispatch unavailable in this environment, so the main agent ran the five perspectives sequentially against the changed files)
**External tools:** CodeRabbit and Copilot disabled by orchestrator (`coderabbit=false, copilot=false`)

## Findings

### FINDING-1
- **Severity:** Important
- **Confidence:** 90
- **File:** `src/FundingPlatform.Infrastructure/Storage/LocalFilesystemObjectStorage.cs:171-176` (pre-fix)
- **Category:** security
- **Source:** security
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
`ResolveAbsolutePath` checked `rooted.StartsWith(rootedRoot, StringComparison.Ordinal)` without a trailing directory separator. A configured root of `/data` would match `/data2/...`, allowing a crafted `ObjectKey` whose normalised path landed in a sibling directory to escape the configured storage root.

**Why this matters:**
`LocalFilesystemObjectStorage` is the offline / test provider but the path-traversal guard is the only thing standing between user-controlled key components and arbitrary filesystem locations. Although `ObjectKey.Build` already strips `..` and `/` from individual segments, a defence-in-depth boundary check is the right pattern for security-relevant code and the spec's FR-edge "Path collisions / Local-mode parity" call-out implies it.

**How it was resolved:**
Changed the check to compare against `rootedRoot + DirectorySeparatorChar` (or exact equality with the root itself). Now `/data` does not match `/data2/...`.

### FINDING-2
- **Severity:** Important
- **Confidence:** 85
- **File:** `src/FundingPlatform.Infrastructure/Storage/EnsureContainersHostedService.cs:52-58` (pre-fix)
- **Category:** correctness
- **Source:** correctness
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
The post-bootstrap public-access check refused startup when `BlobPublicAccess != PublicAccessType.None`. In practice Azure / Azurite may surface `PublicAccessType.None` for a freshly-created private container, but other SDK versions or emulator builds can return a value not equal to `None` while still being safe (e.g. an undefined sentinel). The previous code would throw and refuse to start the platform on a perfectly safe Azurite container.

**Why this matters:**
SC-008 requires the Aspire dashboard to show the storage resource healthy within 30 s of AppHost start. A spurious `InvalidOperationException` from this guard would break local dev and CI on otherwise-correct configurations.

**How it was resolved:**
Reframed the guard to deny exactly the unsafe values (`PublicAccessType.Blob`, `PublicAccessType.BlobContainer`) rather than allow only `None`. Comment now documents the rule explicitly.

### FINDING-3
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Infrastructure/Storage/Legacy/FileStorageServiceFacade.cs:26-46` (pre-fix)
- **Category:** correctness
- **Source:** correctness, architecture
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
The facade silently routed every legacy `IFileStorageService.SaveFileAsync` call to `FileCategory.GeneratedArtifact`. Until the controllers retrofit (T028 / T052), that means signed PDFs and supplier-catalog uploads are persisted under the wrong container without any operator-visible signal. `DeleteFileAsync` likewise no-op'd on legacy filesystem paths with no log.

**Why this matters:**
Spec FR-013 mandates per-category containers; the silent default violates the operator's mental model of where each file lives, even though the facade is transitional. If T028 ships late, every signed PDF written via the legacy controller during the gap lands in `generated-artifacts` and operations cannot tell.

**How it was resolved:**
Added `ILogger<FileStorageServiceFacade>` injection and a `LogWarning` on every facade write (with file name + chosen category) plus a warning on the no-op delete path. The behavior is unchanged but the miscategorisation is now visible.

### FINDING-4
- **Severity:** Important
- **Confidence:** 75
- **File:** `tests/FundingPlatform.Tests.Integration/Storage/AzuriteFixture.cs:24` (pre-fix)
- **Category:** test-quality
- **Source:** test-quality, production-readiness
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
`var port = 10000 + Random.Shared.Next(0, 5000);` — port collisions are non-trivially likely when xUnit runs several Azurite-backed test classes concurrently. A collision causes `docker run` to fail with a misleading "port already allocated" error rather than a clean test infrastructure failure.

**Why this matters:**
Test-fixture flakiness erodes the delivery bar (CLAUDE.md: a feature is not delivered until full E2E suite has been personally executed and is green). Random-port races introduce noise that the team will spend time debugging.

**How it was resolved:**
Added `AllocateEphemeralPort` helper that binds a `TcpListener` to port 0 (OS-allocated ephemeral port), captures the assigned port, then releases the listener immediately before invoking `docker run`. Window for collision is now microseconds-narrow.

### FINDING-5
- **Severity:** Minor
- **Confidence:** 75
- **File:** `src/FundingPlatform.Infrastructure/Storage/StorageProductionGuardHealthCheck.cs:20`
- **Category:** correctness
- **Source:** correctness
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
`_warningEmitted` was a non-volatile `bool` field on a singleton health check; concurrent probes from multiple health-check sources could race and emit the warning twice (or not at all if a tear-down happened mid-write).

**Why this matters:**
The race only ever causes log noise (extra warning) but is the kind of small concurrency hazard that bites later. Microsoft's HealthCheckService runs probes on the thread pool and may execute them concurrently for different endpoints.

**How it was resolved:**
Switched to `int _warningEmitted` and `Interlocked.CompareExchange(ref _warningEmitted, 1, 0)`. At most one warning, regardless of probe concurrency.

### FINDING-6
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs:259-266`
- **Category:** correctness
- **Source:** correctness
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`IsRetryExhausted(RequestFailedException ex) => ex.Status == 0 || ex.Status >= 500;` conflates legitimate retry exhaustion with non-retryable backend errors (e.g. 501 Not Implemented). The distinction in `ObjectStorageOperationReason` enum is `RetryExhausted` vs `Backend`; this heuristic always picks `RetryExhausted`.

**Why this matters:**
The spec's logging contract (FR-025) tags `errorCode` as `RetryExhausted` on retry exhaustion. A misreport hides whether the SDK's retry policy actually fired, complicating runbook analysis.

**How it would be fixed:**
Distinguish: only treat the exception as `RetryExhausted` when the SDK's retry pipeline reports it (e.g. inspect `ex.Data["RetryCount"]` if surfaced, or wrap calls in a custom `Azure.Core.Pipeline.HttpPipeline` that sets a sentinel). Conservative alternative: rename the enum value to `NonRetryable` and document that the platform doesn't try to introspect SDK retry state.

**Why deferred:**
The decision is partly a domain choice (does the spec actually distinguish?) and editing the public enum or adding pipeline plumbing is larger than a fix-loop scope. Recommend addressing during T020 polish.

### FINDING-7
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs:244-257`
- **Category:** architecture
- **Source:** architecture
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`ResolveProvider` infers `Azurite` vs `AzureBlob` by sniffing the endpoint `Uri.Host`. The `host.EndsWith(".local")` rule is too greedy — a private-DNS Azure deployment that happens to use `.local` (e.g. `mystorage.contoso.local`) would be reported as `Azurite` in diagnostics.

**Why this matters:**
Provider name is a top-level FR-025 log field operators rely on for routing. A misreport hides the real backend in production logs.

**How it would be fixed:**
Drop the `.local` heuristic; rely on configured `Storage:Provider` value, or check for the well-known Azurite account name `devstoreaccount1` in the URL path. Trade-off: configured value can lie about the runtime endpoint, which was the original concern that motivated host sniffing.

**Why deferred:**
The current heuristic is correct for Aspire-managed Azurite containers (which use `127.0.0.1`/`localhost` paths). The `.local` case is a hypothetical that has not been observed. Recommend addressing if Azure deployment introduces a `.local` DNS entry.

### FINDING-8
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Infrastructure/DependencyInjection.cs:32, 51`
- **Category:** architecture
- **Source:** architecture
- **Round found:** 1
- **Resolution:** pending (deferred to T053)

**What is wrong:**
`IFileStorageService` is registered twice — first as `LocalFileStorageService` (line 32), then overwritten as `FileStorageServiceFacade` (line 51). The last registration wins, so the legacy service is dead but allocated.

**Why this matters:**
A future contributor reading the file sees conflicting intent. The duplicate registration is documented in the comment (line 47-50) but is still confusing.

**Why deferred:**
T053 explicitly deletes the legacy service and the facade together. Cleaning the duplicate registration in this PR would create a merge conflict with T053; the comment's reference to the cleanup task is sufficient.

### FINDING-9
- **Severity:** Minor
- **Confidence:** 65
- **File:** `src/FundingPlatform.Application/Abstractions/Storage/ObjectKey.cs:56-81`
- **Category:** correctness
- **Source:** correctness
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`ObjectKey.Parse` accepts `"signed-funding-agreements//entity/suffix.pdf"` (empty owner segment between consecutive slashes) and produces a key with `OwnerSegment = ""`. `Build` would have rejected this input. Round-trip via `Parse(Build(...).Value)` is safe; round-trip the other direction (Parse arbitrary key, ToString) would emit an invalid key.

**Why this matters:**
`Parse` is documented in `data-model.md` as diagnostic-only, so the invariant is technically met. But a defensive parser would refuse the empty-segment input.

**How it would be fixed:**
After splitting, validate `ownerSegment` is non-empty and re-run the same `NormalizeOwner` rules `Build` applies.

**Why deferred:**
No caller of `Parse` today would create such a key (all keys originate from `Build`). The hardening is paranoia; recommend addressing if `Parse` ever feeds a write path.

### FINDING-10
- **Severity:** Minor
- **Confidence:** 65
- **File:** `src/FundingPlatform.Infrastructure/Storage/AzureBlobObjectStorage.cs:74-86`
- **Category:** production-readiness
- **Source:** production-readiness
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
After every successful upload the code calls `GetPropertiesAsync` to read `ContentLength` and `LastModified`. The SDK already returns these in the upload response (`response.Value.LastModified`, the upload result also exposes `ContentLength`). The extra HEAD-equivalent doubles per-upload latency.

**Why this matters:**
Hot path for every blob write; minor latency/cost overhead. Not a correctness issue.

**How it would be fixed:**
Use `response.Value.ContentLength` (or the input `contentLength` when supplied) to fill the `StoredObject.SizeBytes` field. Avoid the `GetPropertiesAsync` call.

### FINDING-11
- **Severity:** Minor
- **Confidence:** 60
- **File:** `tests/FundingPlatform.Tests.Integration/Storage/AzuriteObjectStorageTests.cs:22-29`
- **Category:** test-quality
- **Source:** test-quality
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
Calls `Assert.Ignore("Docker not available — Azurite-backed tests skipped.")` when Docker is missing. That's the right developer-laptop ergonomic, but a misconfigured CI would also "pass" the suite by silently skipping every Azurite test.

**Why this matters:**
The spec's FR-008 emphasises that test-fixture fallback must log a warning and never silently switch providers. The same scepticism arguably applies to Azurite skip-on-missing-Docker.

**How it would be fixed:**
Allow the skip path only when an explicit env var (`Storage_TestFixture_AllowSkip=true`) is set; otherwise fail. Or invert: make the test fail loudly on CI but `Assert.Ignore` only when run from the IDE.

### FINDING-12
- **Severity:** Minor
- **Confidence:** 60
- **File:** `src/FundingPlatform.Infrastructure/Storage/LocalFilesystemObjectStorage.cs:24-30`
- **Category:** architecture
- **Source:** architecture
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`LocalFilesystemObjectStorage`'s constructor calls `Directory.CreateDirectory(_rootPath)`. Side effects in the DI graph constructor make startup failures hard to attribute (a permission error here surfaces as a generic `Aggregate-of-DI-resolution` failure).

**How it would be fixed:**
Move the `CreateDirectory` to a hosted-service (similar to `EnsureContainersHostedService`) or make it idempotent + wrapped in a single `IHostedService.StartAsync`. Better startup diagnostics.

### FINDING-13
- **Severity:** Minor
- **Confidence:** 60
- **File:** `src/FundingPlatform.Database/Tables/dbo.SignedUploads.sql, dbo.FundingAgreements.sql, dbo.Documents.sql`
- **Category:** architecture
- **Source:** architecture
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
Plan called for `BlobKey nvarchar(512)`. Implementation uses `nvarchar(1024)` (matches `ObjectKey.MaxLengthBytes`). Both columns (`BlobKey` AND `LegacyPath`) are 1024.

**Why this matters:**
Silent deviation from plan.md. More-permissive width is defensible but unannounced. `LegacyPath` at 1024 vs the original sources at 500/1000/1024 also leaks a small amount of disk per row.

**How it would be fixed:**
Either align with plan.md (512 for BlobKey), or update plan.md to match the implementation. `tasks.md` T015 has an "Adapted" note about table names but does not mention the width change.

## Resolution summary

The four Important findings were auto-fixed in this round. Build remains green
and the 25 storage unit tests pass. Nine Minor findings are documented above
with rationale for deferral; none of them block the gate. The auto-fixes
covered: a defence-in-depth path-traversal boundary, an over-strict
public-access check that would refuse safe Azurite startup, silent
miscategorisation in the transitional facade (now warns), and a randomised
test port that risked CI flakiness.

The implementation status (28 / 59 tasks) means many spec-level checks are
**deferred** rather than failing — the deferred work (controller retrofit,
US3 hermetic fixture, US4 migration tool body, US5 oversize guard, Phase 8
polish including SC-003 verification) is properly tracked in
[tasks.md](tasks.md) and is the orchestrator's responsibility to surface to
the user before the stamp gate.
