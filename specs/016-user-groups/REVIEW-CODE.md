# Code Review: Group-Scoped Reviewer Access (Spec 016)

**Spec:** [spec.md](spec.md)
**Plan:** [plan.md](plan.md)
**Tasks:** [tasks.md](tasks.md)
**Branch:** `feature/group-users` (commits 2e3eecf → 418243d)
**Date:** 2026-05-08
**Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall Score: 100% (21/21)**

- Functional Requirements: 16/16 (100%)
- Non-Functional Requirements: 5/5 (100%)

### Compliance Matrix — Functional

| Req | Verdict | Implementation |
|---|---|---|
| [FR-001](spec.md#requirements-mandatory) name unique CI/AI | PASS | `Group.cs:36-73`; `dbo.Groups.sql:4-16` (`COLLATE Latin1_General_CI_AI`); `GroupConfiguration.cs:18-22` |
| [FR-002](spec.md#requirements-mandatory) Admin-only CRUD → 403 | PASS | `AdminGroupsController.cs:17` `[Authorize(Roles = "Admin")]`; E2E [`AdminGroupCrudTests.NonAdmin_DirectAccessToGroupsIndex_Returns403`](../../tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminGroupCrudTests.cs) |
| FR-003 list with member count | PASS | `AdminGroupsController.Index`; `GroupService.ListAsync` projects `g.Memberships.Count()`; view renders Miembros column |
| FR-004 delete cascades memberships | PASS | `dbo.UserGroupMemberships.sql:11` `ON DELETE CASCADE`; `UserGroupMembershipConfiguration.cs:26`; T051 metadata test |
| FR-005 zero-group user not blocked | PASS | `GroupService.DeleteAsync` only removes Group; user rows survive (`GroupServiceTests.Delete_RemovesMembershipsButNotUsers`); `ReviewerScope.Empty` short-circuits queue + denies detail |
| FR-006 rename preserves memberships | PASS | `Group.Rename` keeps Id; `GroupServiceTests.Rename_PreservesMemberships_AndWritesAuditRow` |
| FR-007 create requires ≥1 group (non-admin) | PASS | `AdminUsersController.cs:92-96`; `UserAdministrationService.cs:125-130` |
| FR-008 edit requires ≥1 group (non-admin) | PASS | `AdminUsersController.cs:175-179`; `UserAdministrationService.cs:254-259` |
| FR-009 Admin discards memberships | PASS | `NormalizeGroupIdsForRole` strips ids when role=Admin; `ApplyMembershipDiffAsync` removes pre-existing rows; JS hides selector when role=Admin |
| FR-010 multi-select UI | PASS | `Views/Admin/Users/Create.cshtml`/`Edit.cshtml` `<select asp-for="GroupIds" multiple>` |
| FR-011 queue scoped at EF level | PASS | `ApplicationRepository.GetByStateForReviewerAsync` composes `EXISTS` predicate against `UserGroupMemberships`; admin short-circuits |
| FR-012 detail page 403 for out-of-scope | PASS | `ReviewController.Review` calls `ApplicantSharesAnyGroupAsync` then `Forbid()`; admin exempt |
| FR-013 signing inbox same predicate | PASS | `SignedUploadRepository.GetPendingInboxAsync` composes the same predicate; `GetSigningInboxQuery` carries `ReviewerGroupIds`+`IsAdministrator` |
| FR-014 search composes with scope | PASS | `searchTerm` flows queue→repo via `EF.Functions.Like` on first/last name + legal id; group-overlap predicate applied first |
| FR-015 admin bypass | PASS | `IsAdmin` short-circuits in queue, signing inbox, repo, and `ReviewController` |
| FR-016 applicant own access | PASS | `ReviewController` is `[Authorize(Roles = "Reviewer,Admin")]` only; applicant own-access lives on the existing `ApplicationController` path (acknowledged in spec lines 56-57, 158) |

### Compliance Matrix — Non-Functional

| Req | Verdict | Implementation |
|---|---|---|
| NFR-001 EF query-level filter | PASS | All four surfaces (queue, search, signing inbox, detail-auth) compose the predicate at `IQueryable` level. No in-memory `.Where(...)` post-filter on unscoped result sets. |
| NFR-002 server-side detail auth | PASS | `ReviewController.Review` enforces overlap before rendering view; URL tampering cannot bypass |
| NFR-003 no sign-out required | PASS | `IReviewerScopeProvider` is request-scoped, reads memberships fresh from DB each request; `ReviewerScopeNextRequestTests` proves it |
| NFR-004 es-CR copy | PASS | `AdminGroupsResources.cs`, `ReviewerQueueResources.cs`, `AdminUsersResources` group-related strings all es-CR; no English bleed-through verified |
| NFR-005 audit on every mutation | PASS | `GroupService` + `UserAdministrationService.ApplyMembershipDiffAsync` write one `AdminAuditEvent` per mutation with `ActorUserId + OccurredAt`; no-op edits skip the row per `contracts/admin-users-form.md` |

## Detailed Review — Manual-Review Hot-Spots

### 1. `GroupService.CreateAsync` — two-phase audit-row patch (`GroupService.cs:46-103`)

**Verdict: ACCEPTABLE WITH NOTED CAVEAT (Important — not a blocker).**

The current code:
1. Constructs `Group` entity (in-memory).
2. Pre-checks for duplicate name; throws `DuplicateGroupNameException` if found.
3. Adds Group to context.
4. Adds `AdminAuditEvent` with `TargetId = "0"` (the group's Id is not yet known).
5. `SaveChangesAsync()` — writes both rows in a single SQL transaction. Group gets its real Id.
6. Looks up the audit row in `_db.AdminAuditEvents.Local`, patches `TargetId` to the new group Id.
7. Second `SaveChangesAsync()` flushes the patched audit row.

**Failure analysis:**
- **Step 5 fails (e.g., unique-index race after pre-check):** transaction rolls back; both rows are undone. Caller sees `DuplicateGroupNameException`. **Safe.**
- **Step 7 fails (rare — only the audit row's TargetId update):** Group is persisted; audit row exists with `TargetId = "0"`. The trail is honest about the actor + action + timestamp + payload (`{"name":"Norte"}` is in `PayloadJson`), but `TargetId` is stale. **Audit drift but no business-data corruption.**
- **Process crash between step 5 and step 7:** same as step 7 failure.

**Concurrency:**
- No race against other admins creating *different* groups. Each call has its own DbContext (scoped DI) and tracking session.
- Race against another admin creating *the same* group: pre-check (line 56) reduces likelihood; unique index on `Name` is the authoritative gate. The catch on `DbUpdateException` (line 79-82) re-throws as `DuplicateGroupNameException`. No race window left.
- The `.Local` lookup on line 87-93 filters by `Action + TargetType + ActorUserId + TargetId == "0"` and takes the most recent. Inside a single scoped DbContext, only ONE matching audit row exists (the one this method just added), so the filter is deterministic. **Safe under concurrency.**

**Recommendation (nice-to-have, not blocker):** Drop the second `SaveChangesAsync` and patch the in-memory `TargetId` to a sentinel like `"pending"` so the failure mode is obvious to log scrapers. Or, alternatively, defer the audit-row insert until after the Group is known: add Group → SaveChanges → record audit with the now-known Id → SaveChanges. The current path emits the audit row in the SAME transaction as the Group, which has a defensible argument (atomic), so neither alternative is strictly better. Acceptable as written. The implementer's flag is fair but the design is intentional.

### 2. `UserAdministrationService.UpdateUserAsync` — membership diff not in explicit transaction (`UserAdministrationService.cs:221-385`)

**Verdict: ACCEPTABLE FOR THIS SPEC (Nit — recommend documenting).**

The flow:
1. Concurrency-stamp check (in-memory, against fetched `target.ConcurrencyStamp`).
2. Validate role + group ids.
3. Update `target` field-by-field; `_userManager.UpdateAsync(target)` — separate `SaveChanges` inside Identity.
4. (If role changed) `UpdateSecurityStampAsync` — another `SaveChanges`.
5. (If role changed) `RemoveFromRolesAsync` + `AddToRoleAsync` — more `SaveChanges`.
6. (If Applicant) `Applicants.Add/UpdateProfile` + `SaveChanges`.
7. `ApplyMembershipDiffAsync` — adds/removes membership rows + audit row + final `SaveChanges`.

There are 4–5 separate `SaveChanges` calls without an enclosing `IDbContextTransaction`. If a failure occurs at step 7 after step 3 succeeded, the user has been renamed/role-flipped but memberships haven't been updated. The `ConcurrencyStamp` was already bumped by Identity, so a retry from a stale form will surface `CONCURRENCY_CONFLICT` to the user and recovery path is correct.

**Why this is acceptable for spec 016:**
- The spec's edge-case coverage (line 86) explicitly relies on the existing `ConcurrencyStamp` for "two admins concurrently edit the same user", citing the Constitution's optimistic-concurrency Quality Gate. No additional concurrency control is required by the spec.
- The integration test `Update_ConcurrencyStamp_Mismatch_ReportsConflict` proves the recovery path.
- The pre-existing `UserAdministrationService` did not wrap its `_userManager` calls in a transaction either; spec 016 inherits that contract.

**Recommendation:** Wrapping steps 3–7 in `await using var tx = await _dbContext.Database.BeginTransactionAsync(ct)` would tighten atomicity. Marked as a follow-up improvement, NOT a spec-016 blocker. Note: this would not work under EF InMemory (used by integration tests) without `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`, which is already set.

### 3. `AdminAuditEvents.ActorUserId` FK is `ON DELETE NO ACTION` (`dbo.AdminAuditEvents.sql:14-15`)

**Verdict: SPEC-COMPLIANT.**

- The implementer worried that admin user deletion would be blocked by audit rows.
- Reviewing the spec: [data-model.md](data-model.md) line 87 explicitly says "no cascade — audit rows survive user deletion". [plan.md](plan.md) line 168 calls this "soft retention".
- The spec-level decision is honored. The current `AdminUsersController` does not even expose a Delete action — it only Disables/Enables — so the FK does not affect any current code path.
- If a future delete path is added, the design constraint is intentional: the audit trail is meant to outlive deleted actors. That is the spec contract.

**No action required.**

## Detailed Review — Other Observations

### Authorization on AdminGroupsController

- `[Authorize(Roles = "Admin")]` on the class — covers all 5 actions.
- `[ValidateAntiForgeryToken]` on every POST: Create, Edit, Delete (verified at lines 48, 95, 132).
- Non-admin returns 403 (or AccessDenied redirect) — exercised by `AdminGroupCrudTests.NonAdmin_DirectAccessToGroupsIndex_Returns403`.

### Authorization on AdminUsersController

- `[Authorize(Roles = "Admin")]` on the class.
- `[ValidateAntiForgeryToken]` on Create, Edit, Disable, Enable, ResetPassword POST endpoints.

### Reviewer-scope predicate — composition discipline

All four reviewer-facing surfaces compose at the EF query level:

1. **Reviewer queue** — `ApplicationRepository.GetByStateForReviewerAsync` (lines 115-164): admin short-circuit, scope-empty short-circuit (returns `(0, [])`), and the `EXISTS`-shaped predicate `_context.UserGroupMemberships.Any(m => m.UserId == a.Applicant.UserId && groupIds.Contains(m.GroupId))`.
2. **Signing inbox** — `SignedUploadRepository.GetPendingInboxAsync` (lines 25-88): same shape, applied via `LINQ where`.
3. **Search** — composes on top of the same `IQueryable` after the scope predicate (lines 144-153). FR-014 status filters compose at projection level.
4. **Detail-page authorization** — `ApplicationRepository.ApplicantSharesAnyGroupAsync` (lines 166-179): single-application variant of the listing predicate, called from `ReviewController.Review`.

NFR-001 + NFR-002 hold. No surface bypasses.

### Localized copy (NFR-004)

- `AdminGroupsResources.cs`: 100% es-CR. "Catálogo de grupos", "Nuevo grupo", "Editar", "Eliminar", "Miembros", "El nombre del grupo es obligatorio", "Ya existe un grupo con ese nombre", flash messages — all Spanish.
- `AdminUsersResources.cs` (group additions): "Grupos", "Selecciona uno o más grupos…", "Debes seleccionar al menos un grupo", "Otro administrador modificó este usuario al mismo tiempo".
- `ReviewerQueueResources.cs`: "Buscar solicitante", "Nombre o cédula", "Buscar", "Limpiar".
- No English bleed-through.

### Integration tests use EF InMemory (deviation from CLAUDE.md "real DB" rule)

Implementer's claim: real DB is honored at the E2E layer; tests/Integration uses InMemory for parity with the rest of the project.

**Validated.** The repo-wide pattern is InMemory (see grep of `UseInMemoryDatabase` — every integration test uses it, including pre-spec-016 tests `CurrencyConfigServiceTests`, `ExchangeRateServiceTests`, `LegacyQuotationRateAttachServiceTests`, plus 10+ persistence tests). Spec 016 follows the established convention. The dacpac shape is asserted via `INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS` checks (referenced in T051) and EF metadata assertions (`GroupDeletionCascadeTests.EfMetadata_GroupForeignKey_IsCascade`). The full SQL behavior is exercised by `tests/FundingPlatform.Tests.E2E/Tests/Admin/AdminGroupCrudTests.cs`, `AdminUserGroupAssignmentTests.cs`, `ReviewerScopeTests.cs`, `GroupDeletionCascadeTests.cs` against the Aspire-orchestrated SQL container (`AspireFixture` boots SQL via `AddSqlProject` and deploys the dacpac via `sqlpackage`).

CLAUDE.md says "Integration tests must hit a real DB, never mocks." The InMemory provider is not strictly a mock (it is EF Core's own in-memory provider, exercising the same EF pipeline minus SQL translation), but it is a deviation from the strict reading of the rule. The deviation is **pre-existing**, not introduced by spec 016. Best read: this should be flagged as a follow-up cleanup in a dedicated spec — not a spec-016 blocker. The spec-016 cascade-shape test (T051) exists precisely to compensate.

### Minor finding: unused parameter `reviewerId` in `IReviewerQueueProjection`

`ReviewerQueueProjection.GetForReviewerAsync(reviewerId, ...)` and `GetRowsAsync(reviewerId, ...)` accept `reviewerId` as the first parameter but only `firstName` is used downstream (for the hero greeting). Spec 011 added these signatures; spec 016 left them unchanged. Not a blocker; flagged for future cleanup.

## Conclusion

Spec compliance is **100%**. The three implementer-flagged hot-spots are all defensible:

1. Two-phase audit insert in `GroupService.CreateAsync` — narrow drift window in catastrophic failure; payload preserves the actor/name/action; acceptable as designed.
2. Membership-diff transaction boundary — relies on existing optimistic-concurrency contract per spec edge case; acceptable.
3. `AdminAuditEvents.ActorUserId` FK `NO ACTION` — explicitly spec'd as soft-retention.

External tools (CodeRabbit, Copilot) skipped per pipeline directive.

Proceeding to deep multi-perspective review (extension `spex-deep-review` enabled).

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes,
focusing on high-level questions that need human judgment.

**Changed files:** ~48 source files + ~15 test files. Three new C# domain entities, four new EF configurations, three new SQL tables, one new admin controller, two new view models, three new Razor views, one new resource file (`ReviewerQueueResources`), modified Identity service, modified application repo, modified signed-upload repo, modified review controller. ~3,500 lines of net additions.

### Understanding the changes (8 min)

The shortest path through the implementation is to read in this order:

1. Start with [`spec.md`](spec.md) Story 3 acceptance scenarios — these are the user-visible outcomes the rest of the work serves.
2. Then [`src/FundingPlatform.Application/Reviewer/IReviewerScope.cs`](../../src/FundingPlatform.Application/Reviewer/IReviewerScope.cs) — the value type the whole feature pivots around. `IsAdmin` short-circuits everywhere; `GroupIds` is the EF predicate input.
3. Then [`src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs:115-179`](../../src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs) — the `EXISTS`-shaped predicate composed on `IQueryable<Application>` for the queue + the single-application variant for detail-page auth. This is the core of [FR-011](spec.md#requirements-mandatory) / [FR-012](spec.md#requirements-mandatory) / [NFR-001](spec.md#requirements-mandatory) / [NFR-002](spec.md#requirements-mandatory).
4. Then [`src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs:221-385`](../../src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs) — admin user CRUD with the new GroupIds path, the role-change rules, and the audit-on-diff logic.

- Question: Does decomposing the scope into a request-scoped `IReviewerScope` (held by the controller, passed to projection + repo) feel right, or would you have preferred a cross-cutting EF `HasQueryFilter`? Plan [§ Phase 0 item 4](plan.md#phase-0--outline--research) explains why query filters were rejected (no per-request opt-out without `IgnoreQueryFilters`); does that argument hold up?
- Question: The signing inbox uses `IReadOnlyCollection<int> ReviewerGroupIds` and `bool IsAdministrator` directly on the query DTO ([`GetSigningInboxQuery`](../../src/FundingPlatform.Application/SignedUploads/Queries/GetSigningInboxQuery.cs)) instead of taking an `IReviewerScope`, while the queue projection takes `IReviewerScope`. Should those converge on one shape?

### Key decisions that need your eyes (12 min)

**Two-phase audit row in group creation** ([`GroupService.cs:46-103`](../../src/FundingPlatform.Infrastructure/Services/GroupService.cs), relates to [NFR-005](spec.md#requirements-mandatory))

The `Group` row's Id is IDENTITY, so the audit row's `TargetId` cannot be set until after `SaveChanges`. Current code adds the audit row with `TargetId = "0"`, calls `SaveChanges`, then patches the audit row's `TargetId` and calls `SaveChanges` a second time. If the second `SaveChanges` fails, the audit row exists with `TargetId = "0"` but the Group is committed.

- Question: Is the alternative — write Group first, `SaveChanges`, then write audit + `SaveChanges` — preferable? It reverses the failure mode (Group exists, audit missing, vs. Group exists, audit `TargetId="0"` with payload-name intact). Which failure mode is easier for an audit reviewer to detect and fix?

**`AdminAuditEvent.ActorUserId` FK is `ON DELETE NO ACTION`** ([`dbo.AdminAuditEvents.sql:14-15`](../../src/FundingPlatform.Database/Tables/dbo.AdminAuditEvents.sql), relates to [NFR-005](spec.md#requirements-mandatory))

- The spec ([data-model.md § AdminAuditEvent](data-model.md), [plan.md Phase 1](plan.md#phase-1--design--contracts)) explicitly chose "no cascade — audit rows survive user deletion."
- Today, no admin-user-delete code path exists (`AdminUsersController` only Disables/Enables).
- Question: If a future spec adds a real "delete admin" flow, should this FK migrate to `ON DELETE SET NULL` (preserving the audit row but losing the actor link) or remain `NO ACTION` (forcing the operator to also delete or reassign audit rows first)? Spec 016 doesn't have to answer this; flagging because it is the natural follow-up.

**Membership diff transaction boundary** ([`UserAdministrationService.cs:381`](../../src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs), relates to [edge case "Two admins concurrently edit the same user's groups"](spec.md#edge-cases))

`UpdateUserAsync` performs 4–5 `SaveChanges` calls (UserManager.UpdateAsync, security stamp, role swap, applicant upsert, membership diff) without an enclosing `IDbContextTransaction`. The spec's edge case (line 86) routes concurrency to the existing `ConcurrencyStamp`, not to a transaction.

- Question: Is the existing optimistic-concurrency contract sufficient given that the user's `ConcurrencyStamp` is bumped by Identity in the FIRST `SaveChanges`, so a retry from a stale form fails fast? Or do you want a wrapping `BeginTransactionAsync` for cleaner all-or-nothing semantics?

**EF query filter on `ApplicationUser` interacts with audit FK** ([`AppDbContext.cs:50`](../../src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs))

`ApplicationUser` has a global `HasQueryFilter(u => !u.IsSystemSentinel)`. The audit row references `ActorUserId` directly; there is no navigation, so no filter interaction. But the `Applicant.User` chain in `GetByStateForReviewerAsync` does cross the user table.

- Question: For a reviewer queue request, the predicate `m.UserId == a.Applicant.UserId` joins through `Applicant`, not `ApplicationUser`. Is the query filter ever silently dropping the sentinel-owned applicant from the queue? (Answer should be no — sentinel does not own applicants — but worth a sanity confirm.)

**Reviewer queue `searchTerm` uses `EF.Functions.Like` with `%term%`** ([`ApplicationRepository.cs:144-153`](../../src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs), relates to [FR-014](spec.md#requirements-mandatory))

- The EF InMemory provider does NOT support `EF.Functions.Like`, so [`ReviewerQueueScopeTests`](../../tests/FundingPlatform.Tests.Integration/Application/ReviewerQueueScopeTests.cs) does not exercise the search path. Real-SQL exercise lives in the E2E test [`ReviewerScopeTests.Reviewer_QueueSearch_NarrowsResults_AndStillRespectsScope`](../../tests/FundingPlatform.Tests.E2E/Tests/Admin/ReviewerScopeTests.cs).
- Question: Is one E2E test sufficient coverage for FR-014, or should an additional integration test (with `UseSqlServer` against the real container) be added in a follow-up?

### Areas where I'm less certain (5 min)

- [`GroupService.cs:87-99`](../../src/FundingPlatform.Infrastructure/Services/GroupService.cs): the audit-row patch finds the most recent matching local entry with `TargetId = "0"`. Inside one DbContext-scoped service this is deterministic, but the comment block (lines 67-73) is unusually long and apologetic. Worth tightening or restructuring; the algorithm is correct but the prose suggests the author was uneasy.
- [`ReviewerQueueProjection.cs:81-84`](../../src/FundingPlatform.Application/Services/ReviewerQueueProjection.cs): the queue fetches Submitted + UnderReview + Resolved separately, then concatenates and re-filters in memory for KPI counts. The group-overlap predicate is applied at the SQL level for each fetch, but the filter switch (lines 142-150) and KPIs (89-96) run in memory over the union. NFR-001 only mandates EF-level filter for *group overlap*; status filtering in memory is fine since the group-overlap result set is already bounded. Confirming this is the correct read.
- [`ReviewerScopeNextRequestTests.cs`](../../tests/FundingPlatform.Tests.Integration/Application/ReviewerScopeNextRequestTests.cs) simulates "next request" by re-creating the `ReviewerScopeProvider` against the same in-memory DB. The HTTP layer is not exercised — the E2E suite is the real test for NFR-003. Is the integration test pulling its weight, or is it redundant given the E2E coverage?
- The `IGroupService` interface signature uses `string actorUserId` everywhere instead of pulling the actor from a `ClaimsPrincipal`/`IHttpContextAccessor`. This couples controller knowledge into the service. Was the alternative considered? (`AdminAuditWriter` is also stateless w.r.t. the actor; the service is the only seam where the actor enters.)
- After the deep-review fix loop, [`GroupService.CreateAsync`](../../src/FundingPlatform.Infrastructure/Services/GroupService.cs) is now one-phase (Group SaveChanges → audit SaveChanges) instead of two-phase. Confirm the new failure modes (Group exists / audit missing) are an acceptable trade vs. the previous (Group exists / audit `TargetId="0"`).

### Deviations and risks (5 min)

- **Integration tests use EF InMemory.** Documented in [plan.md Constitution Check](plan.md#constitution-check) row III, but the broader project-level CLAUDE.md says "Integration tests must hit a real DB." This deviation is pre-existing across the repo (every integration test uses InMemory), spec-016 inherits the convention. T051's metadata + dacpac assertions partially compensate, and the E2E suite covers the real SQL path. Question: should a follow-up spec migrate integration tests to `Testcontainers.MsSql`, or leave the E2E layer as the single source of truth for SQL behavior?
- **Reviewer queue search input does not exercise the real SQL plan in integration tests.** EF InMemory does not support `EF.Functions.Like`, so only the E2E test exercises the FR-014 path against real SQL. Question: acceptable, or want a parallel `UseSqlServer` integration test?
- **`GroupService.IsUniqueViolation`** matches by error-message text (`"UX_Groups_Name"` or `"duplicate key"`). This is fragile if the SQL Server message format changes or the index name is renamed. Question: should we match on `SqlException.Number == 2601 || 2627` instead? (Would require unwrapping the inner exception — slightly more code, much more durable.) **(Addressed in fix round 1 — now reads SqlException.Number via reflection with the message-substring as fallback.)**
- **Sentinel admin's audit rows.** If the sentinel admin (`admin@FundingPlatform.com`) creates a group, the audit row's `ActorUserId` references the sentinel user. The `HasQueryFilter` on `ApplicationUser` hides the sentinel from default user queries, so audit-row enrichment that joins back to `ApplicationUser` will return null without `IgnoreQueryFilters`. No such enrichment exists today. Question: flag for the future audit-viewer spec.

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-05-08 | **Rounds:** 1/3 | **Gate:** PASS WITH FINDINGS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 5 | completed |
| Architecture & Idioms | 8 | completed |
| Security | 3 | completed |
| Production Readiness | 1 | completed |
| Test Quality | 4 | completed |
| CodeRabbit (external) | — | skipped (pipeline `coderabbit=false`; CLI not installed locally) |
| Copilot (external) | — | skipped (pipeline `copilot=false`; CLI not installed locally) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 8 | 5 | 3 (ambiguous — see below) |
| Minor | 14 | - | 14 |

### What was fixed automatically

Five Important findings were resolved in the round-1 fix loop:

- **XSS in admin Groups Edit view** — group `Name` was interpolated into a JS `confirm('...')` string literal where Razor's HTML encoding does not protect against attribute-decoded apostrophe break-outs. Fixed by moving the prompt copy to `data-confirm-*` attributes (which Razor encodes correctly) read by inline JS via `getAttribute()`. (FINDING-5)
- **English domain-exception message leak** — `AdminGroupsController` defensive `catch (ArgumentException)` exposed Group.Create's English messages, violating NFR-004. Replaced with localized resource string `AdminGroupsResources.NameRequired`. (FINDING-4)
- **Two-phase audit save in `GroupService.CreateAsync`** — collapsed to one-phase: persist Group → SaveChanges → record audit with known id → SaveChanges. Eliminates the `TargetId="0"` orphan-row failure mode. (FINDING-1)
- **Missing rollback on membership-insert failure in `CreateUserAsync`** — wrapped membership SaveChanges in try/catch with `_userManager.DeleteAsync(user)` compensating action so a non-admin user is never persisted with zero memberships. (FINDING-2)
- **Brittle string-match unique-violation detection** — `GroupService.IsUniqueViolation` now reads `SqlException.Number == 2601 || 2627` via reflection with the message-substring as fallback. (FINDING-6)

All 16 unit tests + 22 integration tests for spec 016 pass after the fixes.

### What still needs human attention

Three Important findings could not be auto-fixed because they require judgment calls. The orchestrator should pause and ask the user:

- The transaction boundary for [`UserAdministrationService.UpdateUserAsync`](../../src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs) was flagged. The spec ([edge case line 86](spec.md#edge-cases)) explicitly delegates concurrency to `ConcurrencyStamp`. Question: accept current behavior as spec-compliant, or wrap steps 318–381 in `BeginTransactionAsync` despite the risk of EF InMemory tests silently degrading? (FINDING-3, see [review-findings.md](review-findings.md))
- The unit test [`ReviewerScopePredicateTests`](../../tests/FundingPlatform.Tests.Unit/Application/ReviewerScopePredicateTests.cs) does not match its docstring (T039 promised `ToQueryString()` shape assertions). Question: relax the docstring, or add a real-SQL shape assertion in a parallel integration test? (FINDING-7)
- The E2E [`ReviewerScopeTests`](../../tests/FundingPlatform.Tests.E2E/Tests/Admin/ReviewerScopeTests.cs) covers 3 of 6 scenarios from [tasks.md T048](tasks.md). Question: add the 3 missing scenarios now (positive scope-respect on populated queue; applicant own-access; reviewer-with-zero-memberships), or accept integration coverage as compensating? (FINDING-8)

### Recommendation

8 Important findings found; 5 auto-fixed; 3 remain as ambiguous judgment calls. 14 Minor findings are listed in [review-findings.md](review-findings.md) for reviewer awareness — none block. The fix loop kept compliance at 100% and all 38 spec-016 unit/integration tests still pass.

Recommended action: pause the pipeline so the user can decide on the three ambiguous Important findings. None are correctness blockers (the spec is honored as written), but they touch areas where the spec author may want to tighten the contract.

---

## Round 2 — F-3 / F-7 / F-8 resolved (2026-05-08)

The three ambiguous Important findings flagged above were resolved in a follow-up pass:

- **F-3 (transaction boundary on `UpdateUserAsync`)** — `UpdateUserAsync` now wraps the membership-diff + applicant upsert + user-row updates in `Database.BeginTransactionAsync` when the provider is relational (`Database.IsRelational()`). On non-relational providers (EF InMemory, used by the rest of the integration suite) the wrapper is skipped so existing tests pass unchanged. New coverage: `tests/FundingPlatform.Tests.Integration/UserAdministrationTransactionTests.cs` exercises the rollback path against the SQLite provider (added as a test-only dependency: `Microsoft.EntityFrameworkCore.Sqlite` 10.0.6) by injecting a `SaveChangesInterceptor` that throws on the membership SaveChanges. Test asserts the user's first/last name updates were rolled back together with the membership change. The SQLite-flavoured `AppDbContext` strips SqlServer-specific column metadata (`UseCollation`, `SYSUTCDATETIME()` defaults) so the schema can be created in-process.
- **F-7 (real `ToQueryString` shape assertion)** — split the `T039` coverage into two files. `ReviewerScopePredicateTests` (unit) keeps the in-memory short-circuit invariants of `ReviewerScope`. New integration test `tests/FundingPlatform.Tests.Integration/Application/ReviewerScopeQueryShapeTests.cs` composes the same predicate against a real `AppDbContext` bound to the SqlServer provider against a sentinel connection string (never opened) and asserts via `IQueryable.ToQueryString()` that admin scope renders SQL with no `UserGroupMemberships` join, while non-admin scope with `GroupIds = [1, 2]` renders SQL with an EXISTS / JOIN against `UserGroupMemberships`. `tasks.md` T039 wording updated to reflect the split.
- **F-8 (3 missing E2E scenarios)** — added three new tests to `tests/FundingPlatform.Tests.E2E/Tests/Admin/ReviewerScopeTests.cs`: `Reviewer_NorteOnly_OutOfScopeSurDetail_Returns403` (a Norte-only reviewer cannot reach the Sur applicant's detail URL — drives the queue first to confirm the row is absent, then GETs the URL by id and expects 403), `Reviewer_NorteOnly_SigningInbox_DoesNotShowSurApplicant` (the same scope predicate applies on `/Review/SigningInbox`; uses a new `FundingAgreementSeeder.SeedPendingSignedUploadAsync` SQL helper to inject a Pending signed upload for the Sur applicant, then verifies it is not visible to the Norte-only reviewer), and `Reviewer_TwoGroups_QueueSearch_Narrows_AndStillRespectsScope` (a reviewer in two groups runs the FR-014 search; an out-of-scope decoy whose name matches the same fragment is filtered out by the group-overlap predicate that composes BEFORE the search). All three drive the real user journey through the UI (no deep-link shortcuts to MVC routes the UI never exposes, per project memory).

Compliance remains 100%. Unit tests: 189 passed. Integration tests: 182 passed (177 prior + 5 new — 2 from F-3, 3 from F-7). E2E project compiles; the new E2E tests will run with the rest of the suite at the orchestrator's stamp gate.
