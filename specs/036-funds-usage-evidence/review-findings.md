# Deep Review Findings

**Date:** 2026-06-17
**Branch:** 036-funds-usage-evidence
**Rounds:** 1 (with a prod-DB revert sub-iteration)
**Gate Outcome:** PASS (advisory — manual invocation)
**Invocation:** manual (`/speckit-spex-deep-review-review`)

## Summary

| Severity | Found | Fixed | Remaining (accepted) |
|----------|-------|-------|-----------|
| Critical | 2 | 2 | 0 |
| Important | 6 | 4 | 2 |
| Minor | 8 | 3 | 5 |
| **Total** | **16** | **9** | **7** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality). External tools: CodeRabbit + Copilot **skipped** (CLIs not installed).

All Critical + the high-value Important findings were fixed and re-verified green (Unit 28, Integration 8 + per-category oversize, E2E 4/4). Two Important and several Minor findings are consciously accepted with documented rationale (below).

## Findings

### FINDING-1 — Oversize **file** rejection had no coverage
- **Severity:** Critical · **Confidence:** 96 · **Category:** test-quality
- **Source:** test-quality agent
- **File:** tests/FundingPlatform.Tests.Integration/Storage/PerCategoryOversizeTests.cs
- **Resolution:** **fixed (round 1).**

**What was wrong:** FR-005/SC-007 (reject files > 20 MiB) had zero coverage for this feature — the E2E `…AndOversizeRejected` exercises an oversize *note*, and `PerCategoryOversizeTests` omitted `FileCategory.FundsUsageEvidence`.

**How it was resolved:** Added `FileCategory.FundsUsageEvidence` to `PerCategoryOversizeTests.AllCategories()`, so the 413-reject and at-cap-pass paths are now asserted for the new category (the `UploadSizeGuard(FileCategory.FundsUsageEvidence)` attribute on the controller is the production guard).

### FINDING-2 — Disallowed-file-type rejection only unit-tested at the policy level
- **Severity:** Critical · **Confidence:** 90 · **Category:** test-quality
- **Source:** test-quality agent
- **File:** tests/FundingPlatform.Tests.E2E/Tests/FundsUsageEvidenceTests.cs (US1)
- **Resolution:** **fixed (round 1).**

**What was wrong:** FR-004 rejection was proven only for the pure `EvidenceFileTypePolicy` function; the controller wiring (buffer → sniff → `Error_FileType` → redirect → no row) was never exercised end-to-end.

**How it was resolved:** US1 now uploads a `.txt` file and asserts an es-CR error toast appears and the row count is unchanged (no item created).

### FINDING-3 — Concurrent delete throws an unhandled `DbUpdateConcurrencyException`
- **Severity:** Important · **Confidence:** 88 · **Category:** correctness
- **Source:** correctness agent (relates to architecture agent's "RowVersion" note)
- **File:** src/FundingPlatform.Infrastructure/Services/FundsUsageEvidenceService.cs (DeleteAsync)
- **Resolution:** **fixed (round 1).**

**What was wrong:** The entity carries a `RowVersion` token, so two concurrent deletes of the same item make the second `SaveChanges` throw `DbUpdateConcurrencyException` (not `KeyNotFoundException`). Neither layer caught it → a 500, violating US3-AS3/SC-003 ("the second resolves harmlessly"). This also resolves the architecture agent's "RowVersion is dead" observation — it is *implicitly* active and is exactly what surfaces here; the right move is to keep it and handle the exception, not remove it.

**How it was resolved:** `DeleteAsync` now catches `DbUpdateConcurrencyException` around the final `SaveChanges` and resolves harmlessly (logs Information, returns). The sequential double-delete case (already covered) returns `KeyNotFoundException`, which the controller maps to a harmless redirect.

### FINDING-4 — No logging on swallowed/best-effort failure paths
- **Severity:** Important · **Confidence:** 82 · **Category:** production-readiness
- **Source:** production-readiness agent (also: security agent context)
- **File:** src/FundingPlatform.Infrastructure/Services/FundsUsageEvidenceService.cs
- **Resolution:** **fixed (round 1).**

**What was wrong:** No `ILogger` was injected; the best-effort blob delete, the orphan-on-upload-failure path, and the missing-blob / unparseable-key download branches swallowed silently, so leaked blobs and missing-blob downloads would accumulate invisibly (the spec's "storage failure" / "orphaned blob" edge cases).

**How it was resolved:** Injected `ILogger<FundsUsageEvidenceService>`; added Warning logs to `DeleteBlobBestEffortAsync`, the unparseable-key and missing-blob download branches, and an Information log on the concurrent-delete path. DI auto-supplies the logger; the integration test helper passes `NullLogger`.

### FINDING-5 — Storage-failure-mid-upload rollback was untested
- **Severity:** Important · **Confidence:** 85 · **Category:** test-quality
- **Source:** test-quality agent
- **File:** tests/FundingPlatform.Tests.Integration/FundsUsageEvidence/FundsUsageEvidenceServiceTests.cs
- **Resolution:** **fixed (round 1).**

**What was wrong:** The compensating `catch { DeleteBlobBestEffortAsync(); throw; }` (no orphaned row, blob cleaned up — a spec edge case) had no test; the only failed-upload test threw *before* the blob was written.

**How it was resolved:** Added `Upload_WhenRowCreationFails_RollsBackRow_AndCleansUpBlob`, which forces the post-upload domain factory to throw (note > 250 reaches the service directly), then asserts no row persists **and** `InMemoryObjectStorage.StoredCount == 0` (blob cleaned up). Added a `StoredCount` accessor to the test double.

### FINDING-6 — US4 verified no-disclosure only for the stage, not the download
- **Severity:** Important · **Confidence:** 82 · **Category:** test-quality
- **Source:** test-quality agent
- **File:** tests/FundingPlatform.Tests.E2E/Tests/FundsUsageEvidenceTests.cs (US4)
- **Resolution:** **fixed (round 1).**

**What was wrong:** SC-004 covers "the evidence stage **or its files**", but US4 only hit the Index route. The download refusal path was unverified for the access boundary.

**How it was resolved:** US4 now has the admin seed one evidence item, captures its download URL, and asserts the **applicant** is refused (403/AccessDenied) and the **out-of-group reviewer** gets 404 on the download route — alongside the existing stage-route checks.

### FINDING-7 — Upload row + audit are not atomic (two `SaveChanges`)
- **Severity:** Important · **Confidence:** 84 · **Category:** production-readiness / correctness
- **Source:** production-readiness, correctness, and architecture agents (merged)
- **File:** src/FundingPlatform.Infrastructure/Services/FundsUsageEvidenceService.cs (UploadAsync)
- **Resolution:** **acknowledged — not fixed (accepted).**

**What is wrong:** `UploadAsync` commits the evidence row, then writes the audit row in a second `SaveChanges`. If the second fails, a committed row+blob exists with no `funds_evidence.uploaded` audit row (FR-010/SC-006 edge).

**Why it is accepted:** This exactly mirrors the shipping `FundService.CreateAsync` pattern (save to get the Id, then audit + save), which is the codebase's established discipline. A fix was attempted (wrapping both saves in `BeginTransactionAsync`) but **broke every upload against the real DB**: `AddSqlServerDbContext` enables the SQL Server **retrying execution strategy**, which forbids a raw user-initiated transaction (`InvalidOperationException`). The codebase's own transaction sites (`UserAdministrationService`) use `CreateExecutionStrategy().ExecuteAsync(...)` with an `IsRelational()` guard — but for an upload that has *already written a blob*, execution-strategy re-execution on a transient retry would re-add the already-tracked entity. The transaction was reverted to the FundService-consistent two-save form. The failure window is a narrow transient-DB-error case on the audit commit only; the row (the source of truth) is never orphaned from its blob.

### FINDING-8 — Cross-scope `EvidenceBelongsAsync` guard has no dedicated test
- **Severity:** Important · **Confidence:** 80 · **Category:** test-quality
- **Source:** test-quality agent
- **File:** src/FundingPlatform.Web/Controllers/FundsUsageEvidenceController.cs (EvidenceBelongsAsync)
- **Resolution:** **acknowledged — not fixed (accepted, code-inspection + adjacent coverage).**

**What is wrong:** The guard rejecting an `evidenceId` that belongs to a different application than the route `applicationId` has no test posting a mismatched pair.

**Why it is accepted:** Exercising it well needs two executed applications each holding evidence — a heavy E2E setup. The same controller refusal path (`NotFound()`) is now exercised by FINDING-6's download no-disclosure test, and the guard itself is a small, read-only `AnyAsync(e => e.Id == evidenceId && e.ApplicationId == applicationId)`. Logged here as a known coverage gap for a future hardening pass rather than auto-fixed.

### FINDING-9 — Upload catch mislabeled a state-race as "note too long"
- **Severity:** Minor · **Confidence:** 71 · **Category:** architecture / production-readiness
- **Source:** architecture + production-readiness agents (merged)
- **File:** src/FundingPlatform.Web/Controllers/FundsUsageEvidenceController.cs (Upload)
- **Resolution:** **fixed (round 1).**

**What was wrong:** The `Upload` catch mapped every `InvalidOperationException` to `Error_NoteTooLong`, but the domain factory also throws for a non-executed state race — a user would be wrongly told their note is too long.

**How it was resolved:** The catch now checks the trimmed note length against `FundsUsageEvidence.MaxNoteLength` and shows `Error_NoteTooLong` only when the note really is too long; otherwise a new generic `Error_UploadFailed` es-CR message.

### FINDING-10 — Hard cast to `BackendStreamHandle`
- **Severity:** Minor · **Confidence:** 72 · **Category:** production-readiness
- **Source:** production-readiness agent
- **File:** src/FundingPlatform.Infrastructure/Services/FundsUsageEvidenceService.cs (OpenForDownloadAsync)
- **Resolution:** **fixed (round 1).**

**What was wrong:** `var handle = (BackendStreamHandle)resolved;` would throw `InvalidCastException` (500) if a serving-mode misconfiguration ever returned a different handle.

**How it was resolved:** Replaced with `if (resolved is not BackendStreamHandle handle) { log; return null; }` — degrades to a clean not-found.

### FINDING-11 — Served `Content-Type` is the client-declared value
- **Severity:** Minor · **Confidence:** 72 · **Category:** security
- **Source:** security agent
- **Resolution:** **acknowledged — not fixed (accepted, defense-in-depth).**

**Why it is accepted:** Not exploitable today — the file-type allow-list keeps the family to pdf/image/office (no active types), and `File(..., fileDownloadName)` forces `Content-Disposition: attachment`, so nothing renders inline. Deriving the MIME from the validated extension and/or adding `X-Content-Type-Options: nosniff` is a worthwhile future hardening if the allow-list ever widens or the disposition changes.

### FINDING-12 — Magic-byte sniff trusts a single `ReadAsync` return
- **Severity:** Minor · **Confidence:** 70 · **Category:** security
- **Source:** security agent
- **Resolution:** **acknowledged — not fixed (accepted).**

**Why it is accepted:** Correct today (the source is a `MemoryStream`, which returns all buffered bytes in one read). It is a latent footgun only if the buffering source is ever changed to a non-`MemoryStream`; `ReadExactlyAsync` would be more robust. Recorded for a future refactor.

### FINDING-13 — File-type family check lives in the controller, not the domain
- **Severity:** Minor · **Confidence:** 70 · **Category:** architecture
- **Source:** architecture agent
- **Resolution:** **acknowledged — not fixed (accepted).**

**Why it is accepted:** Magic-byte sniffing needs the buffered stream at the boundary, so the policy legitimately lives in the Application layer / controller. The controller is the only caller today. Documented as an intentional split.

### FINDING-14 — Concurrent-delete harmless resolution tested only at the service layer
- **Severity:** Minor · **Confidence:** 72 · **Category:** test-quality
- **Source:** test-quality agent
- **Resolution:** **partially addressed (code-fixed) — deterministic test omitted.**

**Why:** The code path is now handled (FINDING-3). A *deterministic* concurrency test is inherently racy (two parallel service calls non-deterministically hit either the `KeyNotFoundException` or the `DbUpdateConcurrencyException` branch), so a flaky test was deliberately not added. Both branches resolve harmlessly.

### FINDING-15 — List ordering + display-name fallback under-asserted
- **Severity:** Minor · **Confidence:** 73 · **Category:** test-quality
- **Source:** test-quality agent
- **Resolution:** **acknowledged — not fixed (accepted).**

**Why it is accepted:** Newest-first ordering and the `email ?? ""` display-name fallback are low-risk branches; the multi-item "neither replaces the other" guarantee is covered (E2E asserts 2 rows; US3 asserts independent deletion). Recorded as a minor coverage gap.

### FINDING-16 — `RowVersion` appears unused
- **Severity:** Minor · **Confidence:** 80 · **Category:** architecture
- **Source:** architecture agent
- **Resolution:** **superseded by FINDING-3 (keep RowVersion).**

**Why:** The architecture agent read `RowVersion` as dead infrastructure, but it is implicitly enforced by EF on `SaveChanges` and is exactly what makes the concurrent-delete path detectable (FINDING-3). The correct resolution is to **keep** it and handle `DbUpdateConcurrencyException` (done), not remove it. The plan specified optimistic concurrency for this entity.

## Remaining Findings (accepted, not blocking)

- **FINDING-7** (Important) — upload row/audit two-save non-atomicity: accepted as the codebase-wide `FundService` pattern; a transaction is incompatible with the retrying execution strategy here.
- **FINDING-8** (Important) — cross-scope guard has no dedicated test: accepted; same refusal path is covered by the download no-disclosure test.
- **FINDING-11, 12, 13, 15** (Minor) — defense-in-depth / coverage notes recorded for a future hardening pass.

These are recorded for human judgement; none block merge.
