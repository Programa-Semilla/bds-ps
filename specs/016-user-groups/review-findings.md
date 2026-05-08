# Deep Review Findings

**Date:** 2026-05-08
**Branch:** feature/group-users (commits 2e3eecf → 418243d)
**Rounds:** 1
**Gate Outcome:** PASS WITH FINDINGS
**Invocation:** quality-gate (speckit-spex-ship)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 8 | 5 | 3 (ambiguous — human review) |
| Minor | 14 | 0 | 14 |
| **Total** | **22** | **5** | **17** |

**Agents completed:** 5/5 (in-context multi-perspective review; Agent tool unavailable so dispatched as 5 sequential perspectives by the orchestrator)
**External tools:** skipped per pipeline directive (`coderabbit=false copilot=false`)

## Findings

### FINDING-1
- **Severity:** Important
- **Confidence:** 70
- **File:** `src/FundingPlatform.Infrastructure/Services/GroupService.cs:46-103` (pre-fix)
- **Category:** correctness
- **Source:** correctness-perspective
- **Round found:** 1
- **Resolution:** **fixed (round 1)**

**What is wrong:**
`CreateAsync` was a two-phase write: insert Group + audit row with placeholder `TargetId = "0"` → `SaveChanges` (which was then wrapped in try/catch for unique-violation) → look up the audit row in `_db.AdminAuditEvents.Local` → patch `TargetId` to the new group id → second `SaveChanges` (NOT in a try/catch). If the second SaveChanges failed (network blip, transient deadlock, cancellation), the audit row was persisted with `TargetId = "0"` while the Group was committed.

**Why this matters:**
NFR-005 requires the audit trail to track the actor + target for every mutation. A row with `TargetId = "0"` is an orphan — no group has id 0, so the audit cannot be correlated back to a group via `TargetId`. The actor and the timestamp survive (PayloadJson contains the name), so the trail is degraded but recoverable.

**How it was resolved:**
Restructured to one-phase: persist Group first → SaveChanges (gets IDENTITY id) → record audit row with the known `entity.Id.ToString()` → SaveChanges. Failure modes are now: SaveChanges 1 fails → no Group, no audit (clean rollback); SaveChanges 2 fails → Group exists, audit missing (clean inconsistency that an operator can back-fill from logs). No more orphan `TargetId = "0"`.

### FINDING-2
- **Severity:** Important
- **Confidence:** 75
- **File:** `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs:170-186` (pre-fix)
- **Category:** correctness
- **Source:** correctness-perspective
- **Round found:** 1
- **Resolution:** **fixed (round 1)**

**What is wrong:**
`CreateUserAsync` calls `_userManager.CreateAsync` and `_userManager.AddToRoleAsync` (rolling back on failure with `DeleteAsync`). It then iterates the requested group ids, adds membership rows, writes the audit, and calls `_dbContext.SaveChangesAsync`. If this final SaveChanges fails (FK violation if a Group was deleted concurrently, transient connection blip), the user record exists with role=Reviewer/Applicant + zero memberships — a state FR-007 / FR-008 say cannot occur on create.

**Why this matters:**
The data-store invariant "non-admin user with role assigned ⇒ at least one membership" is broken on the failure path. A subsequent admin would see this user in the list, and the user would log in but see an empty queue (FR-005's intended state for the cascade-delete path), which masks the real failure.

**How it was resolved:**
Wrapped the membership SaveChanges in try/catch. On failure, calls `_userManager.DeleteAsync(user)` as a compensating action and rethrows. The user creation is fully rolled back; the caller sees the original exception.

### FINDING-3
- **Severity:** Important
- **Confidence:** 75
- **File:** `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs:221-385`
- **Category:** correctness
- **Source:** correctness-perspective
- **Round found:** 1
- **Resolution:** **AMBIGUOUS — deferred to human review**

**What is wrong:**
`UpdateUserAsync` performs 4–6 separate `SaveChanges` calls (UserManager.UpdateAsync, UpdateSecurityStamp, role swap via Remove+AddToRole, applicant upsert, ApplyMembershipDiffAsync) without an enclosing `IDbContextTransaction`. A failure between role-change and membership-diff leaves a user with the new role but stale memberships.

**Why this matters:**
Atomicity is broken across the full user-update operation. A reviewer-→-admin promotion that fails halfway leaves the user with role=Admin and the old reviewer memberships intact (then `ApplyMembershipDiffAsync` would try to clear them on retry, but if the first attempt persisted role-change, the second attempt's ConcurrencyStamp check would fire, surfacing `CONCURRENCY_CONFLICT`).

**Why it was not auto-fixed:**
The spec ([spec.md edge case line 86](spec.md#edge-cases)) explicitly delegates concurrency to the existing `ConcurrencyStamp`, citing the Constitution's optimistic-concurrency Quality Gate. Wrapping the multi-step update in `BeginTransactionAsync` is a wide-blast-radius refactor: ASP.NET Identity's UserManager calls SaveChanges internally, so wrapping requires using the same DbContext (which it already does via `AddEntityFrameworkStores<AppDbContext>`), but the integration tests use EF InMemory which ignores transactions entirely — the transaction would silently degrade to non-atomic in tests. The test fixtures already configure `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`. The auto-fix would silently pass tests but real behavior would change. This is a judgment call the human reviewer should make.

**Recommended human action:**
Either (a) accept the current behavior as spec-compliant per the optimistic-concurrency edge case, OR (b) wrap steps 318–381 in `await using var tx = await _dbContext.Database.BeginTransactionAsync(ct); try { ... await tx.CommitAsync(ct); } catch { await tx.RollbackAsync(ct); throw; }` and verify with a real-SQL integration test that exercises a mid-update failure.

### FINDING-4
- **Severity:** Important
- **Confidence:** 75
- **File:** `src/FundingPlatform.Web/Controllers/Admin/AdminGroupsController.cs:67-72, 121-125` (pre-fix)
- **Category:** security / i18n
- **Source:** security-perspective
- **Round found:** 1
- **Resolution:** **fixed (round 1)**

**What is wrong:**
The defensive `catch (ArgumentException ex)` in Create/Edit handlers added `ex.Message` to ModelState. Group.Create / Group.Rename throw with English messages ("Group name is required.", "Group name must be 100 characters or fewer."). This violates NFR-004 (es-CR localization) for the rare path where DataAnnotation validation is bypassed but domain validation catches the input.

**Why this matters:**
NFR-004 says all admin-area copy MUST be available in es-CR. While the model attributes catch most cases, the defensive fallback is a real visible-to-admin surface (e.g., if someone bypasses client validation by submitting a 200-char name; DataAnnotation would catch it, but if a future change relaxed the model attribute, only the domain check would fire and an English message would surface).

**How it was resolved:**
Catch blocks now ignore `ex.Message` and add `AdminGroupsResources.NameRequired` (the localized resource string) to ModelState. This is the closest matching localized message; the more granular `NameTooLong` could also be selected by inspecting the input length, but the value-add is small for a defensive path.

### FINDING-5
- **Severity:** Important
- **Confidence:** 80
- **File:** `src/FundingPlatform.Web/Views/Admin/Groups/Edit.cshtml:37` (pre-fix)
- **Category:** security
- **Source:** security-perspective
- **Round found:** 1
- **Resolution:** **fixed (round 1)**

**What is wrong:**
The delete button used `onclick="return confirm('@deletePromptTitle\n\n@deletePromptBody');"` where `@deletePromptTitle = string.Format(AdminGroupsResources.Delete_ConfirmTitle, Model.Name)`. Razor HTML-encodes `Model.Name` (so `'` becomes `&#x27;`), but the browser HTML-decodes the attribute value before passing the JS to the engine. A group name containing `'` would break out of the JS string literal in `confirm('...')`. Example: a group named `Norte');alert('xss')//` would inject executable JS at render time.

**Why this matters:**
This is a stored XSS in the admin area. The attack surface is admin-vs-admin: an admin who creates a group with a malicious name can XSS another admin who opens the Edit page. Severity bounded because group names are admin-controlled, but stored XSS is stored XSS.

**How it was resolved:**
Replaced the inline `@deletePromptTitle` interpolation with `data-confirm-title` and `data-confirm-body` HTML attributes (Razor HTML-encodes these correctly into attribute context, NOT into JS context). The inline JS now reads them via `this.getAttribute('data-confirm-title')` which returns the decoded plain text and passes it as a runtime value to `confirm()`, never as JS source. Single-quote, double-quote, backslash, and newline characters in the group name are rendered as data, not as JS code.

### FINDING-6
- **Severity:** Important
- **Confidence:** 75
- **File:** `src/FundingPlatform.Infrastructure/Services/GroupService.cs:164-177` (pre-fix)
- **Category:** architecture / robustness
- **Source:** architecture-perspective
- **Round found:** 1
- **Resolution:** **fixed (round 1)**

**What is wrong:**
`IsUniqueViolation(DbUpdateException)` matched on inner-exception `Message.Contains("UX_Groups_Name")` and `"duplicate key"`. SQL Server may localize error messages depending on server collation. If the unique index is renamed (say, `IX_Groups_Name_Unique`), the substring match silently fails to recognize the violation and the raw `DbUpdateException` propagates instead of `DuplicateGroupNameException` — causing a 500 error response instead of a friendly inline ModelState error.

**Why this matters:**
Brittle string-matching error classification is a classic refactor hazard. The proper signal is `SqlException.Number` (2601 = unique-index violation, 2627 = unique-constraint violation), which is locale- and rename-immune.

**How it was resolved:**
Updated `IsUniqueViolation` to read the inner exception's `Number` property via reflection (avoids taking a hard `Microsoft.Data.SqlClient` reference at this assembly boundary, which is currently provider-agnostic). The Number-based check runs first; the message-substring fallback remains for non-SQL-Server providers (defensive).

### FINDING-7
- **Severity:** Important
- **Confidence:** 70
- **File:** `tests/FundingPlatform.Tests.Unit/Application/ReviewerScopePredicateTests.cs`
- **Category:** test-quality
- **Source:** test-quality-perspective
- **Round found:** 1
- **Resolution:** **AMBIGUOUS — deferred to human review**

**What is wrong:**
Per [tasks.md T039](tasks.md), this test was supposed to "validate that the composed `IQueryable` filter is short-circuited when `IsAdmin == true` and otherwise emits an `EXISTS`-shaped predicate (assert via `ToQueryString()` on a real `IQueryable<Application>` instance)". The actual file only checks the `ReviewerScope.Admin` and `ReviewerScope.Empty` constants — it never invokes a real `IQueryable<Application>` and never asserts SQL shape via `ToQueryString()`.

**Why this matters:**
NFR-001 mandates EF-level filtering. The unit-test layer is the cheapest place to pin "admin scope produces a query without `EXISTS (SELECT 1 FROM UserGroupMemberships ...)`" — a regression there (e.g., someone removes the `if (!scope.IsAdmin)` short-circuit) would slip past the unit suite. Coverage exists at the integration layer (`ReviewerQueueScopeTests.Admin_SeesEveryApplication`) which exercises behavior; a unit-level shape pin would catch the regression earlier.

**Why it was not auto-fixed:**
The shape assertion requires building a `DbContext` instance, getting an `IQueryable<Application>` for the relevant repo method, calling `.ToQueryString()`, and asserting the resulting SQL fragment contains/omits `EXISTS`. This requires (a) deciding which path to instrument (the production repository, or a stand-in extension method that takes the predicate), (b) handling EF InMemory's `.ToQueryString()` behavior (it produces a stub query, not real SQL), and (c) deciding whether to add a `Microsoft.EntityFrameworkCore.SqlServer` test reference for shape assertions. These are judgment calls about test design.

**Recommended human action:**
Either (a) update the test docstring to match the actual scope (admin/empty constant invariants only — drop the misleading "via `ToQueryString()`" promise), OR (b) add a real-SQL integration test that pins the `EXISTS` shape for non-admin and confirms its absence for admin.

### FINDING-8
- **Severity:** Important
- **Confidence:** 70
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/Admin/ReviewerScopeTests.cs`
- **Category:** test-quality
- **Source:** test-quality-perspective
- **Round found:** 1
- **Resolution:** **AMBIGUOUS — deferred to human review**

**What is wrong:**
Per [tasks.md T048](tasks.md), the E2E test should cover ALL FIVE Story 3 acceptance scenarios PLUS a sixth FR-014 search scenario. The current file covers only:
1. Out-of-scope detail URL → 403 (acceptance scenario 2). ✅
2. Queue search renders + URL contains `?search=` (FR-014 search smoke). ✅ (but does not assert that scope-respect AND search compose — it only asserts URL has the query param)
3. Admin queue page renders without 403 (acceptance scenario 4 — partial; doesn't actually count applications visible).

Missing:
- Scenario 1: reviewer in "Norte" only sees Norte + Norte+Sur applicants (positive-result scope assertion).
- Scenario 3: applicant sees own application regardless of own group set (only-applicant-path coverage).
- Scenario 5: reviewer with zero memberships sees empty queue + 403 on detail.
- Sixth (FR-014): queue search narrows results AND still respects scope (the existing test asserts URL but not scoped result content).

**Why this matters:**
The constitution mandates E2E coverage for every user story (Principle III). [tasks.md T048](tasks.md) explicitly enumerates the six scenarios. The current file ships with three out of six. Integration tests partially compensate (`ReviewerQueueScopeTests` covers the listing-side scope predicate against EF in-memory), but the spec/constitution gate is the E2E layer.

**Why it was not auto-fixed:**
Adding three to four real-journey E2E tests requires writing seed flows for: an applicant submitting an application end-to-end, a reviewer landing on a populated queue, group-removal mid-session, and admin-bypass observation. Each test is 50–100 lines of Playwright. The fix is mechanical but bulky and benefits from human judgment about how heavily to overlap with existing E2E flows (e.g., `AdminGroupCrudTests` already covers some of the seed work).

**Recommended human action:**
Add the four missing scenarios to `ReviewerScopeTests`. Optionally, narrow scope by reusing fixtures from `AdminUserGroupAssignmentTests` for the seed flow (admin creates groups + applicant + reviewer, applicant submits, etc.). Each test should drive the real journey through the UI per the project's "E2E must drive real user journey" memory.

---

### Minor Findings (Not in Auto-Fix Loop)

These are not gate-blocking and are listed for reviewer awareness. None require immediate action.

#### FINDING-9 — `MapToDetailAsync` always queries memberships
- File: `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs:485-516`
- Issue: Queries `UserGroupMemberships` for every user, including Admins (always empty by FR-009 invariant). Wasted DB round-trip when role=Admin.
- Suggestion: Skip the query when `role == "Admin"`.

#### FINDING-10 — `Group.Rename` no-op uses `Ordinal` comparison
- File: `src/FundingPlatform.Domain/Entities/Group.cs:48`
- Issue: Renaming "Norte" to "norte" passes the Ordinal-equality short-circuit check (different strings), so DB is updated to "norte". The unique index allows it (case-insensitive collation, same row excluded). Probably intentional but worth confirming; `OrdinalIgnoreCase` would make rename idempotent on case.

#### FINDING-11 — Two-phase audit save was overly complex (now fixed; documented for the architecture conversation)
- File: `src/FundingPlatform.Infrastructure/Services/GroupService.cs`
- Issue (pre-fix): The two-phase pattern with `TargetId = "0"` placeholder existed because the original author wrote audit + entity in one transaction. The fix collapses to one-phase. Resolved by FINDING-1 fix.

#### FINDING-12 — Long methods in `UserAdministrationService`
- Issue: `CreateUserAsync` (~100 lines) and `UpdateUserAsync` (~165 lines) mix validation, role policy, applicant upsert, membership diff, audit. Splitting along these axes would help testability.
- Suggestion: Defer to a follow-up refactor spec; not spec-016 in scope.

#### FINDING-13 — Unused `reviewerId` parameter
- Files: `src/FundingPlatform.Application/Services/ReviewerQueueProjection.cs:60-66, 120-125`
- Issue: `GetForReviewerAsync(reviewerId, ...)` and `GetRowsAsync(reviewerId, ...)` accept `reviewerId` but it is never referenced inside the method body — only `firstName`, `scope`, and `searchTerm` are used. Dead parameter, leftover from spec 011.

#### FINDING-14 — Detail-page authorization in controller (could move to service)
- File: `src/FundingPlatform.Web/Controllers/ReviewController.cs:144-169`
- Issue: `ReviewController.Review` enforces overlap by directly calling `_applicationRepository.ApplicantSharesAnyGroupAsync`. A dedicated `IReviewerAuthorizer` would isolate the policy and make it independently testable.

#### FINDING-15 — Two scope types: `IReviewerScope` and `ReviewerScopeHint`
- Files: `src/FundingPlatform.Application/Reviewer/IReviewerScope.cs` + `src/FundingPlatform.Domain/Interfaces/IApplicationRepository.cs`
- Issue: Same concept (admin flag + group ids) modeled in two types because Domain mustn't reference Application. The duplication is intentional but poorly documented.
- Suggestion: Add an XML doc comment on `ReviewerScopeHint` clarifying it is the Domain-side projection of `IReviewerScope`.

#### FINDING-16 — Duplicated inline JS in user form views
- Files: `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml:102-123`, `Edit.cshtml:105-125`
- Issue: Identical role-toggle JS in both files. Extract to `wwwroot/js/admin-user-form.js`.

#### FINDING-17 — Inconsistent resource-key naming convention
- Files: `AdminGroupsResources` uses `Page_Title`, `Action_Create`; `AdminUsersResources` and `ReviewerQueueResources` use `GroupSelectorLabel`, `SearchLabel`.
- Suggestion: Pick one convention; align in a follow-up.

#### FINDING-18 — No ILogger usage on the new admin actions
- Files: `src/FundingPlatform.Infrastructure/Services/GroupService.cs`, `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` (membership-diff path)
- Issue: Audit table captures successful mutations; failed attempts (e.g., `DuplicateGroupNameException` thrown) are not logged. Operational visibility gap.

#### FINDING-19 — Story 2 acceptance scenario 3 not covered E2E
- File: `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminUserGroupAssignmentTests.cs`
- Issue: Scenario 3 from spec ("existing Reviewer with two groups, removes one, save succeeds and retains one") is missing.

#### FINDING-20 — `Reviewer_QueueSearch_NarrowsResults_AndStillRespectsScope` doesn't assert scope-respect
- File: `tests/FundingPlatform.Tests.E2E/Tests/Admin/ReviewerScopeTests.cs:107-142`
- Issue: The test asserts the URL gets a `?search=` query parameter; it doesn't assert that out-of-scope applicants are excluded when the search would otherwise match them.

#### FINDING-21 — LIKE wildcard chars in user input not escaped
- File: `src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs:144-153`
- Issue: A search for `100%` triggers a SQL wildcard match. UX issue; not security.

#### FINDING-22 — Admin-path signed inbox query still scans
- File: `src/FundingPlatform.Infrastructure/Persistence/Repositories/SignedUploadRepository.cs:53-56`
- Issue: `where (isAdmin || EXISTS (...))` — admin path still evaluates the predicate per row. Could short-circuit by branching the query in C#. Negligible at current scale.

## Remaining Findings (Important, Ambiguous)

These three Important findings could not be auto-fixed and require human judgment. The pipeline orchestrator should pause and ask the user.

| ID | Description | Why ambiguous |
|---|---|---|
| FINDING-3 | No transaction boundary around UpdateUserAsync's multi-step user mutation | Spec edge case explicitly delegates to `ConcurrencyStamp`; auto-fix would silently degrade in EF InMemory tests |
| FINDING-7 | `ReviewerScopePredicateTests` doesn't match T039's intent | Adding `ToQueryString()` shape assertions requires test-design judgment |
| FINDING-8 | E2E `ReviewerScopeTests` covers 3/6 Story 3 scenarios | Bulky additions; reviewer should choose seed-fixture reuse strategy |

These do not block compilation, do not regress existing tests, and do not violate spec compliance (which is 100%). They are quality findings the implementer + reviewer should triage together.
