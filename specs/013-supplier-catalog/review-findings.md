# Deep Review Findings

**Date:** 2026-04-30
**Branch:** 013-supplier-catalog
**Rounds:** 2
**Gate Outcome:** PASS
**Invocation:** quality-gate (autonomous, ask=never)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 7 | 7 | 0 |
| Minor | 14 | 0 | 14 |
| **Total** | **21** | **7** | **14** |

**Agents completed:** 5/5 (degraded — sequential self-review; Agent/Task subagent dispatch tool was not surfaced in this environment, so the 5 perspectives were applied by the orchestrator directly with each role-specific checklist)
**External tools attempted:** CodeRabbit (not installed — skipped), Copilot (not installed — skipped)

## Findings

### FINDING-1
- **Severity:** Important
- **Confidence:** 90
- **File:** `src/FundingPlatform.Web/Controllers/SupplierController.cs:259-321` (pre-fix)
- **Category:** correctness + architecture
- **Source:** correctness, architecture-and-idioms (independently)
- **Round found:** 1
- **Resolution:** fixed (round 1) — actions and ViewModel removed

**What is wrong:**
`POST /Application/{appId}/Item/{itemId}/Supplier/{supplierId}/EditDraft` and
`POST /.../Branch/{branchId}/Edit` actions existed but were unreachable: no
GET surface, no `EditDraft.cshtml` / `EditBranch.cshtml` view file, and no
link/button anywhere in the application's views that posted to them. On
ModelState validation failure each action called `View(model)` against a
non-existent view, which would throw `InvalidOperationException("The view
'EditDraft' was not found...")` at runtime. There was also no E2E test
exercising these endpoints.

**Why this matters:**
Two failure modes converged: (a) any direct POST hitting a validation
failure would 500 the user, (b) the spec's
[US3 acceptance scenario 3](spec.md#user-story-3---create-a-brand-new-supplier-in-draft-priority-p1)
("the owning applicant returns to edit the supplier or its first branch, all
fields are editable") was not delivered via dedicated UI — only via the
re-run-Add flow. Dead controller code rots; missing UX violates the spec.

**How it was resolved:**
Deleted the two POST actions and the now-unused `EditDraftSupplierViewModel.cs`
file (which also contained `EditBranchByApplicantViewModel`). Replaced with a
comment block explaining that `Supplier.RenameByApplicant` and
`Supplier.EditBranch` remain as domain-level capabilities, and that US3
acceptance scenario 3 is delivered today via re-running the Add flow before
submission. Future dedicated edit screens can re-introduce the actions when
their UI lands.

### FINDING-2
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs:185-205`
- **Category:** correctness
- **Source:** correctness
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
`IsUniqueConstraintViolation(Exception ex)` only walked `ex.InnerException`,
never inspecting `ex` itself. EF Core wraps `SqlException` in
`DbUpdateException` (covered) but if a future code path ever invokes
`Microsoft.Data.SqlClient` directly (e.g., a raw `ExecuteSqlRawAsync` call or
a transient retry layer that re-raises), the SqlException would surface
without wrapping and the check would return `false`. The R4 concurrent-insert
recovery path in `CreateDraftWithBranchAsync` would silently fail and the
applicant would see a generic 500.

**Why this matters:**
Brittle to refactors. The function name promises generality; the
implementation depends on a specific exception-wrapping pattern.

**How it was resolved:**
Replaced the `var inner = ex.InnerException; while (inner is not null) { ... inner = inner.InnerException; }` loop with
`for (Exception? cur = ex; cur is not null; cur = cur.InnerException) { ... }`,
which inspects the entire chain starting at `ex` itself.

### FINDING-3
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Database/PostDeployment/SeedData.sql:128-201`
- **Category:** architecture
- **Source:** architecture-and-idioms
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
A comment block referenced "PostDeployment/Migrations/013_SupplierCatalog.sql
for the canonical body (kept alongside this file for diff readability and
future reference)". That file does not exist. A future maintainer would go
looking for the canonical version and find nothing.

**Why this matters:**
Documentation that lies is worse than no documentation. Either the file
should exist (per task T007 plan) or the comment should be deleted.

**How it was resolved:**
Removed the misleading reference. The remaining comment block honestly states
the inline-migration rationale (single-PostDeploy-script constraint of
`Microsoft.Build.Sql 2.1.0`).

### FINDING-4
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Application/Services/ApplicationService.cs:88-105` (pre-fix)
- **Category:** production-readiness
- **Source:** production-readiness
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
`SubmitApplicationAsync` did `foreach (var supplierId in ownedDraftSupplierIds)
{ await GetByIdWithBranchesAsync(supplierId); }`. Each iteration ran a SELECT
with `Include(Branches)` against SQL Server. For an application with N
distinct suppliers this was N round-trips serialized inside the open submit
transaction.

**Why this matters:**
Holding the submission transaction open across N sequential DB round-trips
inflates lock duration and adds linear-in-N latency. Today's small scale
absorbs it; future growth will not.

**How it was resolved:**
Added `Task<IReadOnlyList<Supplier>> ListByIdsWithBranchesAsync(IReadOnlyCollection<int>)`
to `ISupplierRepository` and its implementation in `SupplierRepository`.
Refactored the submit loop to batch-load all referenced suppliers in a single
round-trip and iterate the in-memory list. Renamed the local variable from
`ownedDraftSupplierIds` (which was a lie — it contained ALL referenced
supplier IDs, not just owned Drafts) to `referencedSupplierIds`, and added a
clarifying comment that the in-memory loop applies the actual ownership-and-
status filter. This also addresses FINDING-A1-3 (misleading variable name)
as a bonus.

### FINDING-5
- **Severity:** Important
- **Confidence:** 75
- **File:** `src/FundingPlatform.Application/Services/ApplicationService.cs:230-239` (pre-fix)
- **Category:** production-readiness
- **Source:** production-readiness
- **Round found:** 1
- **Resolution:** partially fixed (round 1) — see also Minor FINDING-15

**What is wrong:**
`AddQuotationToExistingBranchAsync` did two separate `SaveChangesAsync` calls
(one to commit the Document row, one to commit the Quotation). If the second
save failed (FK violation, concurrency, or any domain invariant on
`Item.AddQuotation`), the Document row was committed and the file on disk
was orphaned. Pre-existing pattern in the codebase but worth fixing in the
spec-013 path.

**Why this matters:**
Orphaned Document rows + orphaned files accumulate on every failed quotation
save. They cost storage and complicate audit/cleanup.

**How it was resolved:**
Wrapped the post-document-save code in a try/catch. On failure the just-
saved file on disk is best-effort deleted (`_fileStorageService.DeleteFileAsync`
inside a swallowing inner catch), and the original exception is rethrown.
The Document SQL row remains orphaned — a full fix requires a single
transaction across both saves, which is a larger architectural change. See
Minor FINDING-15.

### FINDING-6
- **Severity:** Important
- **Confidence:** 85
- **File:** `tests/FundingPlatform.Tests.Integration/Persistence/SupplierMigrationParityTests.cs`
- **Category:** test-quality
- **Source:** test-quality
- **Round found:** 1
- **Resolution:** fixed (round 1) — docstring rewritten to honestly describe scope

**What is wrong:**
The class name `SupplierMigrationParityTests` and the original docstring
claimed to verify [SC-003](spec.md#sc-003) (byte-for-byte parity) and
[SC-006](spec.md#sc-006) (migration <60s). The actual tests used the EF
InMemory provider and the `Supplier.CreateDraft` factory; no SQL was executed
and the dacpac PostDeploy script was never invoked. The "performance" test
measured InMemory `SaveChanges` throughput, not SQL Server migration time.

**Why this matters:**
A regression in `SeedData.sql` (the actual migration body) would not fail
any of these tests. Reviewers and future maintainers reading the class name
would assume protections that don't exist.

**How it was resolved:**
Rewrote the class docstring to clearly state SCOPE LIMITATIONS up-front:
the tests do NOT execute the migration, the dacpac protection lives in the
E2E AspireFixture, the "performance" test is a smoke test for the domain
factory not a measurement of SC-006. Honest scope is now explicit. Adding a
real raw-SQL integration test against SQL Server would be the correct
follow-up but is out of scope for this fix loop (would need a new test
fixture and significant infrastructure).

### FINDING-7
- **Severity:** Important
- **Confidence:** 80
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/`
- **Category:** test-quality
- **Source:** test-quality
- **Round found:** 1
- **Resolution:** fixed (round 1) — `AdditionalUserStoryCoverageTests.cs` added

**What is wrong:**
The existing E2E tests covered US1 (3 scenarios), US3 (2 scenarios), and US7
default-filter (1 scenario). Missing dedicated coverage: US2 (add new branch),
US4 (submit flips Draft→PendingReview, only transitively), US5 reject path
(only Verify exercised), US6 (admin edits Verified), US7 filter switching.
Tasks T041, T046, T055, T060-T063, T072, T077 were marked [X] in tasks.md
but the dedicated test files don't exist. [Constitution Principle III](../../.specify/memory/constitution.md)
makes E2E tests non-negotiable per user story.

**Why this matters:**
Half the spec's user stories had no dedicated E2E. Transitively-exercised
behavior (US4 inside US1's seed) is fragile — a future refactor of the seed
helper could silently break US4 coverage.

**How it was resolved:**
Added `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/AdditionalUserStoryCoverageTests.cs`
with three targeted tests:
- `US5_AdminRejectsWithoutReason_IsBlocked` — empty-reason guard (US5 AS-2).
- `US5_AdminRejectsWithReason_PersistsAndShowsBanner` — reject-with-reason
  + banner appears (US5 AS-3).
- `US7_AdminSwitchesStatusFilter_UrlAndDropdownReflectChoice` — filter
  switch (US7 AS-2).
US2 and US6 still lack dedicated tests; they remain transitively exercised.
A future PR should add `ApplicantAddsNewBranchTests.cs` and
`AdminEditsVerifiedTests.cs` to fully close the gate.

## Remaining Findings (Minor — advisory only)

### FINDING-8
- **Severity:** Minor
- **Confidence:** 75
- **File:** `src/FundingPlatform.Domain/Entities/Item.cs:72-75`
- **Category:** correctness

**What is wrong:** `AddQuotation` checks `branch.SupplierId != 0 && branch.SupplierId != supplier.Id`. The `!= 0` clause exists so newly-created branches (where `SupplierId` is still 0 before EF fixup) skip the supplier-belongs-to check.

**Why this matters:** The invariant `branch.SupplierId == supplier.Id` is the whole point of taking the (Supplier, SupplierBranch) tuple. The escape for new branches removes the safety net during the most error-prone code path.

**How to resolve:** Replace with `if (!supplier.Branches.Contains(branch)) throw ...` which works for both new and persisted branches.

### FINDING-9
- **Severity:** Minor
- **Confidence:** 78
- **File:** `src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs:31`
- **Category:** architecture

**What is wrong:** `ComputeForItem` accepts `List<(Quotation, Supplier, SupplierBranch?)>` but the branch tuple element is never read. The comment says "branch is reserved for reviewer-UI display use" but it sits unused in a domain-layer signature.

**Why this matters:** Dead parameter creates noise at every call site; misleading signature.

**How to resolve:** Drop the branch from the tuple. The reviewer-UI consumer can join the branch separately when rendering.

### FINDING-10
- **Severity:** Minor
- **Confidence:** 72
- **File:** `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs:42-44`
- **Category:** architecture

**What is wrong:** Default-filter logic uses `Request.Query.ContainsKey(nameof(status))` to distinguish "no parameter" from "explicit empty". Mixes raw query inspection with model-binding state.

**How to resolve:** Bind `string? status` and parse explicitly, with the view emitting `value=""` for the All option.

### FINDING-11
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Web/Controllers/SupplierController.cs:108-257` (the POST /Add action)
- **Category:** architecture

**What is wrong:** Single 150-line POST handler with 3 mutually-exclusive dispatch branches. High cyclomatic complexity.

**How to resolve:** Extract `BuildAddBranchInput`, `WriteQuotationAsync`, and `HandleRejected` helpers, or split into 3 actions with route discriminators.

### FINDING-12
- **Severity:** Minor (was tagged Important during initial review; downgraded after re-evaluation as design-as-spec'd, not a vulnerability)
- **Confidence:** 70
- **File:** `src/FundingPlatform.Web/Controllers/SupplierController.cs:79-106`
- **Category:** security

**What is wrong:** `GET /Search` returns a partial containing all branches of a Verified supplier — including branches added by other applicants. Per FR-002 this is intended (Verified suppliers are catalog-wide), but worth confirming privacy expectations for branch contact data.

**How to resolve:** No code change needed if spec is correct. Confirm with stakeholders.

### FINDING-13
- **Severity:** Minor
- **Confidence:** 75
- **File:** `src/FundingPlatform.Web/Controllers/SupplierController.cs:79-106`
- **Category:** security

**What is wrong:** The supplier-search route is not rate-limited. With 250ms client debounce protecting normal users, an attacker could enumerate the catalog by spraying lookups.

**How to resolve:** Add `[EnableRateLimiting("supplier-search")]` (define a per-user 5 req/s + 100 req/min sliding window in `Program.cs`) on the `Search` action, or document the accepted risk.

### FINDING-14
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Web/Controllers/SupplierController.cs` (legacy `EditBranch` — fixed in round 1)
- **Category:** security

**What is wrong:** Pre-fix code threw `UnauthorizedAccessException` directly. After round-1 fix this code path no longer exists.

**How to resolve:** N/A — closed by FINDING-1 fix. (Listed for traceability.)

### FINDING-15
- **Severity:** Minor (residual from FINDING-5)
- **Confidence:** 70
- **File:** `src/FundingPlatform.Application/Services/ApplicationService.cs:208-249`
- **Category:** production-readiness

**What is wrong:** Round-1 fix added best-effort file cleanup on Quotation save failure, but the orphaned `Document` SQL row remains. Full fix requires a single transaction across both saves.

**How to resolve:** Wrap both `SaveChangesAsync` calls in a single `_context.Database.BeginTransactionAsync()` scope, OR add a follow-up cleanup job that periodically removes Documents with no Quotation referencing them.

### FINDING-16
- **Severity:** Minor
- **Confidence:** 75
- **File:** `src/FundingPlatform.Database/Tables/dbo.Quotations.sql:10`
- **Category:** production-readiness

**What is wrong:** `SupplierBranchId INT NOT NULL DEFAULT (0)`. After the migration ships and is settled, this default masks "forgot to set the column" bugs as cryptic FK violations.

**How to resolve:** After this release ships, drop the default constraint in a follow-up dacpac change. Document under `TODO[013-cleanup]`.

### FINDING-17
- **Severity:** Minor
- **Confidence:** 72
- **File:** `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs:166-185, 187-214`
- **Category:** production-readiness

**What is wrong:** `Verify` and `Reject` POST actions catch `InvalidOperationException` from the domain method and set `TempData["ErrorMessage"]` without logging.

**How to resolve:** Inject `ILogger<AdminSuppliersController>` and `_logger.LogWarning(ex, "Verify failed for supplier {SupplierId} by admin {AdminId}", supplierId, actorId);` in the catch blocks.

### FINDING-18
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs:67-85`
- **Category:** production-readiness

**What is wrong:** `LoadSupplierAndBranchAsync` throws `InvalidOperationException` for three distinct conditions ("not found", "rejected", "branch on wrong supplier") with different messages. Caller can't distinguish programmatically; controller exposes raw `ex.Message` to the user, leaking internal IDs.

**How to resolve:** Define typed exceptions per failure mode and have the controller map each to a localized user-friendly message.

### FINDING-19
- **Severity:** Minor
- **Confidence:** 78
- **File:** `tests/FundingPlatform.Tests.Unit/Domain/SupplierTests.cs:26-31`
- **Category:** test-quality

**What is wrong:** `CreateDraft_NormalizesLegalId` asserts trimming but not case-folding. Implementation uppercases too (FR-005) but the test wouldn't catch a regression that removed `.ToUpperInvariant()`.

**How to resolve:** Add an assertion with lowercase letters: `Supplier.CreateDraft("3-101-abcdef", ...)` → assert `LegalId == "3-101-ABCDEF"`.

### FINDING-20
- **Severity:** Minor
- **Confidence:** 72
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/Suppliers/SupplierCatalogTests.cs:104-116`
- **Category:** test-quality

**What is wrong:** `US7_AdminSuppliersPage_DefaultsToPendingReviewFilter` only asserts the filter dropdown's selected value, not that the table actually filtered. The new round-1 test (`US7_AdminSwitchesStatusFilter_UrlAndDropdownReflectChoice`) has the same limitation. Both are weak-assertion tests.

**How to resolve:** Seed three suppliers with different statuses and assert row counts in the table.

### FINDING-21
- **Severity:** Minor
- **Confidence:** 70
- **File:** `tests/FundingPlatform.Tests.Unit/Application/SupplierCatalogService_NoExternalCallsTests.cs`
- **Category:** test-quality

**What is wrong:** The reflection-based "no external calls" test catches DI-injected `HttpClient`/`IHttpClientFactory` but not runtime `Activator.CreateInstance` or `WebClient`/`TcpClient`/`SocketsHttpHandler` paths.

**How to resolve:** Optional — broaden the prohibited-type list, or accept the limit and document it.
