# Deep Review Findings

**Date:** 2026-06-19
**Branch:** 041-evidence-inbox
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** manual (after_implement hook)
**Scope:** this session's work (`a6e8a7c..HEAD`) — spec 041 + the carried spec-040 audit-surface refactor. The full spec-040 surface was excluded (already deep-reviewed at its own delivery).

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 3 | 3 | 0 |
| Minor | 7 | 4 | 3 (accepted/deferred) |
| **Total** | **10** | **7** | **3** |

**Agents completed:** 5/5 (Correctness, Architecture, Security, Production-Readiness, Test Quality)
**External tools:** CodeRabbit + Copilot not installed — skipped.
**Agents failed:** none. Correctness and Security returned zero findings (verified twice each).

## Findings

### FINDING-1 (Important → fixed)
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/EvidenceInboxTests.cs` (US2 crafted Upload)
- **Category:** test-quality · **Source:** test-quality agent · **Round:** 1 · **Resolution:** fixed (round 1)

**What is wrong:** The crafted Upload POST sent only the antiforgery token and **no file**. In `FundsUsageEvidenceController.Upload` the closed-process gate runs before the `file is null` check, but both reject without creating evidence — so the "row count stays 1" assertion would pass identically even if the FR-007 closed-gate were deleted. The empty-file guard alone rejected it. Classic passes-for-the-wrong-reason.

**Why it matters:** US2 is the security heart of spec 041 (FR-007 / SC-003 — crafted writes rejected while the process is closed). A test that can't distinguish the closed-gate from an unrelated guard provides false assurance.

**How it was resolved:** The crafted Upload now attaches a **real `%PDF` file part** (and the EditNote a real note), so absent the gate the mutation *would* succeed and change state — making the no-change assertion a genuine proof of the gate.

### FINDING-2 (Important → fixed)
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/EvidenceInboxTests.cs` (US2 crafted mutations)
- **Category:** test-quality · **Resolution:** fixed (round 1)

**What is wrong:** SC-003 requires upload, **edit-note**, and delete all be rejected, but the crafted-POST block fired only Upload + Delete. No crafted `…/Evidence/{id}/Note` POST existed anywhere in the suite, so the EditNote closed-gate was entirely E2E-unverified — it could be deleted and every test would stay green.

**Why it matters:** A whole mutation path (EditNote) of the FR-007 gate had zero end-to-end coverage.

**How it was resolved:** Added a crafted `EditNote` POST with a real note, plus an assertion that the row's read-only note text does **not** contain the injected value after reload — proving the edit was rejected.

### FINDING-3 (Important → fixed)
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/EvidenceInboxTests.cs` (`CraftedPostAsync` + US2 session handling)
- **Category:** test-quality · **Resolution:** fixed (round 1)

**What is wrong:** `CraftedPostAsync` swallowed all exceptions (`catch { }`) and asserted nothing about the request outcome, so a silently-failed request (network/antiforgery) would leave the count unchanged and pass for the wrong reason. Compounding this, the test captured the antiforgery token, then logged the reviewer **out and back in** before firing the POST — which rotates the antiforgery cookie, so the captured token would have failed validation (400) and never reached the gate.

**Why it matters:** Two independent ways for the crafted-POST assertion to pass without ever exercising the closed-gate.

**How it was resolved:** (a) The process is now closed from a **separate admin browser context** so the reviewer session — and its antiforgery token — stays valid when the crafted POST fires. (b) `CraftedPostAsync` was rewritten to issue an authenticated `HttpClient` POST carrying the live context cookies (the in-page `fetch` tripped on the dev self-signed cert + http→https redirect), and now **returns the final status + URL**. The test asserts each crafted write returns **200 and lands back on `/Applications/{id}/Evidence`** (the closed-gate's redirect target) — proving it was authenticated, passed antiforgery, and reached the gate — *and* that nothing changed.

### FINDING-4 (Minor → fixed)
- **File:** `tests/FundingPlatform.Tests.Integration/EvidenceInbox/EvidenceInboxQueryTests.cs`
- **Category:** test-quality · **Resolution:** fixed (round 1)

**What is wrong:** The inclusion tests asserted the app id/number/applicant but never asserted `FundName`/`ProcessName` were populated (FR-003 requires fund/process identification per row). A regression nulling those projections would pass.

**How it was resolved:** Added `FundName == "Fondo 041"` and `ProcessName` starts-with `"Proceso "` assertions to the in-scope inclusion test.

### FINDING-5 (Minor → fixed)
- **File:** `tests/FundingPlatform.Tests.Unit/Application/ReviewerQueueReturnedFromAuditTests.cs`
- **Category:** test-quality · **Resolution:** fixed (round 1)

**What is wrong:** The test proved the projection *surfaces* a ReturnedFromAudit app when the mocked repo yields one, but never asserted the projection actually *queries* that state (the exact call the shipped bug omitted). Coverage rested implicitly on NSubstitute's default-return.

**How it was resolved:** The builder now returns the repo substitute, and the test asserts `repo.Received(1).GetByStateForReviewerAsync(ReturnedFromAudit, …)` — making the contract explicit.

### FINDING-6 (Minor → fixed)
- **File:** `src/FundingPlatform.Infrastructure/Persistence/EvidenceInboxProjection.cs`
- **Category:** architecture/naming · **Source:** architecture agent · **Resolution:** fixed (round 1, comment only)

**What is wrong:** The `orderby` comment claimed "most-recently-executed first," but the value is `Application.UpdatedAt`, which is re-stamped on every mutation — not literally the execution timestamp.

**How it was resolved:** Reworded the comment to explain it orders by last update, and *why* that equals execution time for an `AgreementExecuted` app (nothing mutates `Application.UpdatedAt` afterwards; evidence ops touch `FundsUsageEvidence`). The DTO field name `ExecutedAtUtc` and the "Ejecutado" column were kept — accurate in practice for terminal executed apps.

### FINDING-7 (Minor → accepted, not fixed)
- **File:** `src/FundingPlatform.Web/Controllers/FundsUsageEvidenceController.cs` (`Index`)
- **Category:** production-readiness · **Source:** prod-readiness agent · **Resolution:** accepted (documented)

**What is wrong:** `Index` issues a duplicate `Applications`-by-`Id` point-lookup — `IsAccessibleAsync` reads `State`, then `IsProcessClosedAsync` reads `Group.Process.Status` on the same row. Could be one projection.

**Why accepted:** Both are PK/indexed point-lookups (negligible cost). Folding them would require reshaping the shared `IsAccessibleAsync` helper, which is deliberately ordered first to preserve the FR-008 no-disclosure 404. The micro-optimization isn't worth risking that security-sensitive ordering. Left as-is intentionally.

### FINDING-8 (Minor → accepted, not fixed)
- **File:** `src/FundingPlatform.Infrastructure/Persistence/EvidenceInboxProjection.cs` (`Take(MaxRows)`)
- **Category:** production-readiness · **Resolution:** accepted (documented)

**What is wrong:** The 200-row cap truncates silently with no log/metric, so a reviewer group exceeding 200 executed-active apps would silently lose overflow rows (vs SC-002's "100% appear").

**Why accepted:** The spec explicitly defers pagination ("simple capped list", Out of Scope) and the sibling `ReviewerDashboardProjection` carries no logger. 200 executed-active apps in a single group is implausible near-term. Injecting `ILogger` here would diverge from the reference projection for a non-issue. Noted for the future pagination iteration.

### FINDING-9 (Minor → deferred)
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/EvidenceInboxTests.cs`
- **Category:** test-quality · **Resolution:** deferred (documented in tasks.md Deviations)

**What is wrong:** No reopen test (US2 scenario 4 / FR-004 "evaluated live, not snapshotted"). A snapshot-on-close implementation would pass all current tests.

**Why deferred:** `ProcessAdminPage` exposes no reopen action (no reopen UI surface exists in 041's scope). The live-evaluation guarantee is proven by the integration matrix (`ClosedProcess_IsExcluded` + the controller's per-request `IsProcessClosedAsync`, which reads `Status` live with no snapshot field anywhere). Adding the E2E would require building a reopen affordance — out of scope for 041.

### FINDING-10 (Minor → deferred)
- **File:** `tests/FundingPlatform.Tests.Integration/EvidenceInbox/EvidenceInboxQueryTests.cs`
- **Category:** test-quality · **Resolution:** deferred (documented in tasks.md Deviations)

**What is wrong:** The matrix runs on EF InMemory; the soft-deleted + archived-fund exclusions have no real-SQL backstop (E2E doesn't seed those cases).

**Why deferred:** Mirrors the shipping spec-036 `FundsUsageEvidenceServiceTests` precedent (InMemory in the Integration project). The green `EvidenceInbox` E2E exercises the real-SQL path for the primary scope/state/process-status filters; the two exclusion filters reuse the identical, already-shipping `IApplicationQueryFilter.ExcludeDeleted`/`ExcludeArchivedFund` extension methods used across every other reviewer read. Low marginal risk.

## Remaining Findings

None blocking. Three Minor findings (FINDING-7, -8, -10) and one deferred (FINDING-9) are accepted as documented; see each entry above and `tasks.md` Deviations. Correctness and Security agents found zero issues.
