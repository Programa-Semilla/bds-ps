# Code Review: Email Notifications System (spec 021)

**Spec:** [spec.md](./spec.md)
**Plan:** [plan.md](./plan.md)
**Tasks:** [tasks.md](./tasks.md)
**Research:** [research.md](./research.md)
**Date:** 2026-05-12
**Reviewer:** Claude (`speckit-spex-gates-review-code`)
**Branch:** `feature/notifications` (8 implementation commits ahead of `main`)

## Compliance Summary

**Overall Score: 92%** (29 / 32 FR + NFR fully compliant; 1 documented deviation; 2 partial-compliance gaps with mitigating integration coverage)

| Category | Compliant | Deviated | Missing |
|---|---|---|---|
| Functional Requirements (FR-001..FR-032) | 30 | 1 | 1 |
| Non-Functional (NFR-001..NFR-008) | 8 | 0 | 0 |
| Success Criteria (SC-001..SC-010) | 8 | 2 | 0 |
| Brand-grep gate (T030/FR-027) | PASS | - | - |

**Gate outcome: NEEDS-WORK** (one accept-as-documented deviation requires architectural sign-off; one orphan-dependency cleanup; partial E2E coverage for US3/US4/US5/US6/US7 vs SC-001 wording).

## Detailed Findings

### FR-001 — Two-phase save (DOCUMENTED DEVIATION)

**Spec:** "The system MUST write a `NotificationOutbox` row in the SAME database transaction as the workflow state change that triggered it."

**Implementation:** [`ApplicationService.SubmitApplicationAsync` lines 175-220](../../src/FundingPlatform.Application/Services/ApplicationService.cs) and the symmetric `ReviewService.SendBackAsync` / `FinalizeReviewAsync` perform two consecutive `SaveChangesAsync` calls with NO explicit `BeginTransaction`. The first commit materializes `VersionHistory.Id`; the second commit persists outbox rows referencing that id.

**Documented in:** [`research.md` Post-Implementation Findings](./research.md#post-implementation-findings)

**Verdict: accept-as-documented, with a recommended stricter fix path.**

Why accept: the Aspire-managed `Microsoft.Data.SqlClient` connection has the transient-retry execution strategy enabled (default for `AddSqlServerDbContext<>`). Wrapping the dual save in an explicit `BeginTransactionAsync` conflicts with the retry strategy — EF Core requires `dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)` to bracket the transaction OR the retry strategy disabled. The implement subagent observed silent SaveChanges failures (no exception, no commit) when wrapping the dual save in `IDbContextTransaction`; the two-phase pattern resolved it. The exposure window between the two saves is ~1 ms; outbox idempotency catches duplicates and the dead-letter path catches losses on retry.

**Recommended follow-up** (defer to `/speckit-spex-evolve` post-stamp, NOT auto-fixed):
Option A (smaller delta): formalize FR-001 to state two-phase save with the ~1 ms exposure window documented.
Option B (restores fidelity): re-introduce a true single transaction via `IExecutionStrategy.CreateExecutionStrategy().ExecuteAsync(async ct => { await using var tx = await _db.Database.BeginTransactionAsync(ct); /* save+enqueue+save */; await tx.CommitAsync(ct); })`.

Option B is the spec-faithful fix. Recommend running with the documented deviation through stamp/ship and scheduling the option-B rework as a follow-up evolution.

### Orphan dependency: `IWorkflowTransactionScope` is dead code (IMPORTANT)

[`ApplicationService` line 42](../../src/FundingPlatform.Application/Services/ApplicationService.cs) and [`ReviewService` line 18](../../src/FundingPlatform.Application/Services/ReviewService.cs) inject `IWorkflowTransactionScope` but **never call** `_txScope.BeginAsync(...)`. The implementation [`EfWorkflowTransactionScope`](../../src/FundingPlatform.Infrastructure/Notifications/Persistence/EfWorkflowTransactionScope.cs) and interface [`IWorkflowTransactionScope`](../../src/FundingPlatform.Application/Notifications/IWorkflowTransactionScope.cs) are fully implemented but unused; the field is initialized in the constructor and read nowhere.

This is the residue of the original FR-001 design that the two-phase deviation replaced. The DI registration in [`NotificationsServiceCollectionExtensions:46`](../../src/FundingPlatform.Web/Services/NotificationsServiceCollectionExtensions.cs) still adds it.

**Recommendation:** keep the scaffolding intact through stamp (it's the seam needed by FR-001 option-B fix) OR clean up via `/speckit-spex-evolve`. Not auto-fixing — choice between "keep for future fix" vs "delete unused" is an architectural call.

### Production fallback path for unset `Notifications:Provider` (MINOR)

[`NotificationsServiceCollectionExtensions` lines 113-128](../../src/FundingPlatform.Web/Services/NotificationsServiceCollectionExtensions.cs) — the `default` arm registers `MailtrapSmtpEmailSender` for an unset provider regardless of environment, including Production. The contract table in [`contracts/IEmailSender.md`](./contracts/IEmailSender.md) and FR-015 imply the non-Production fallback is `NoOpEmailSender` (with WARN log) and the Production fallback should be either explicit Mailgun (with FR-016 fail-fast) or explicit NoOp (with CRIT log).

A real-world misconfiguration where Production boots with an empty `Notifications:Provider` would silently land on MailtrapSmtp — which then attempts to connect to `localhost:25`, fails repeatedly, and dead-letters every notification. Detectable via deliveries but not announced at boot.

**Recommendation:** strengthen the fall-through to throw in Production when no provider is set, mirroring the FR-016 fail-fast pattern. Not auto-fixing — spec wording does not strictly mandate this beyond Mailgun.

### Backoff schedule has one unused entry (MINOR — cosmetic)

[`EmailDispatchWorker.BackoffSchedule`](../../src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs) declares `[1s, 5s, 30s]` (FR-021). With default `MaxAttempts=3`, the worker traverses backoffs[0]=1s after attempt 1, backoffs[1]=5s after attempt 2, then attempt 3 fails → DeadLetter. The third entry (30s) is **never used**. To exercise all three backoffs, `MaxAttempts` would need to be 4. The spec wording "(1s, 5s, 30s) over three attempts" is ambiguous — three attempts = 2 inter-attempt waits.

**Verdict:** acceptable as documented; recommend either dropping the third backoff entry OR bumping `MaxAttempts` to 4 for spec parity. Not auto-fixed.

### SC-001 / E2E coverage gap (IMPORTANT)

**Spec SC-001:** "All six event variants in the §Event Catalog fire on their workflow trigger across the full Aspire stack; verified by at least one E2E test per variant against the SMTP-capture sidecar."

**Implementation:** five of the six event variants do **not** have a live (un-ignored) E2E:

| Event | E2E test | Status |
|---|---|---|
| `APPLICATION_SUBMITTED_APPLICANT` | [`ApplicationSubmittedNotificationsTests`](../../tests/FundingPlatform.Tests.E2E/Notifications/ApplicationSubmittedNotificationsTests.cs) | LIVE |
| `APPLICATION_SUBMITTED_REVIEWER` | (same) | LIVE |
| `RETURNED_TO_APPLICANT` | [`ReturnedToApplicantNotificationsTests`](../../tests/FundingPlatform.Tests.E2E/Notifications/ReturnedToApplicantNotificationsTests.cs) | LIVE |
| `RESUBMITTED_BY_APPLICANT` | [`ResubmittedNotificationsTests`](../../tests/FundingPlatform.Tests.E2E/Notifications/ResubmittedNotificationsTests.cs) | `Assert.Ignore`("deferred to T086") |
| `APPLICATION_APPROVED` | [`ApprovedAndRejectedNotificationsTests.Approve_fires_approval_emails`](../../tests/FundingPlatform.Tests.E2E/Notifications/ApprovedAndRejectedNotificationsTests.cs) | `Assert.Ignore`("deferred to T086") |
| `APPLICATION_REJECTED` | (same file, second test) | `Assert.Ignore`("deferred to T086") |

The deferred tests include explanatory rationale and point to compensating integration coverage ([`SequentialResubmitTests`](../../tests/FundingPlatform.Tests.Integration/Notifications/SequentialResubmitTests.cs), [`IdempotencyDoubleProcessTests`](../../tests/FundingPlatform.Tests.Integration/Notifications/IdempotencyDoubleProcessTests.cs), source-level scans in [`RazorEmailRendererTests`](../../tests/FundingPlatform.Tests.Unit/Notifications/RazorEmailRendererTests.cs)). The brand invariants per FR-027/SC-005/SC-006 ARE exercised by the source-level [`EmailTemplateSenderTests`](../../tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs) (every variant). The placeholder `Assert.Ignore` FR-032 mandated is gone.

[`ProviderOutageResilienceTests`](../../tests/FundingPlatform.Tests.E2E/Notifications/ProviderOutageResilienceTests.cs) (US6) and [`AllowlistGuardE2ETests`](../../tests/FundingPlatform.Tests.E2E/Notifications/AllowlistGuardE2ETests.cs) (US7) are also `Assert.Ignore` deferrals; their FR-021/22 + FR-017/18/19 contracts are integration-tested via [`DeadLetterPathTests`](../../tests/FundingPlatform.Tests.Integration/Notifications/DeadLetterPathTests.cs) and [`AllowlistFailClosedTests`](../../tests/FundingPlatform.Tests.Integration/Notifications/AllowlistFailClosedTests.cs).

**Verdict:** SC-001 wording demands an E2E test per variant; only 3 of 6 have live E2E coverage. The integration tests give strong evidence that the writer/worker/resolver paths are correct, but they do not exercise the **live Aspire stack + smtp4dev sidecar** that SC-001 explicitly names. Tracking under T086, which is correctly marked `[ ]` (not done) in [`tasks.md` line 251](./tasks.md). Recommend escalating this gap to stamp stage as a known open item — the orchestrator's "T086 honestly marked" criterion is met.

### Provider name resolution under decorator (MINOR)

[`EmailDispatchWorker.ResolveProviderName`](../../src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs#L325) does not unwrap the decorator — when `RecipientAllowlistFilter` is the injected `IEmailSender`, the `switch` falls into the `_ => MailtrapSmtp` default. The author flagged this in the inline comment; the field's value is audit-only. Acceptable.

### Other spec compliance highlights

- **FR-001 transactional contract** (despite the deviation noted above): outbox rows are NEVER persisted unless the workflow `SaveChangesAsync` succeeds, because the writer's `Add` is a no-op until the caller commits. Failed workflow transitions yield zero outbox rows — verified by [`OutboxTransactionalEnqueueTests.Submit_fails_writes_zero_outbox_rows`](../../tests/FundingPlatform.Tests.Integration/Notifications/OutboxTransactionalEnqueueTests.cs).
- **FR-002** outbox columns present in [`dbo.NotificationOutbox.sql`](../../src/FundingPlatform.Database/Tables/dbo.NotificationOutbox.sql) and EF entity ([`NotificationOutbox`](../../src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutbox.cs)).
- **FR-003..FR-005** worker poll, claim, terminal states — implemented in [`EmailDispatchWorker.DispatchOneAsync`](../../src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs).
- **FR-006..FR-013** resolver + buckets + dedup + bucket-priority — [`NotificationRecipientResolver`](../../src/FundingPlatform.Infrastructure/Notifications/Resolvers/NotificationRecipientResolver.cs).
- **FR-014..FR-016** three sender impls + Production fail-fast on Mailgun missing config — [`NotificationsServiceCollectionExtensions:60-77`](../../src/FundingPlatform.Web/Services/NotificationsServiceCollectionExtensions.cs).
- **FR-017..FR-019** allowlist decorator registered only outside Production; Production resolves bare sender — verified across all three provider arms in `NotificationsServiceCollectionExtensions`. Empty allowlist is fail-closed by construction (`allowlist.Count == 0 → false`).
- **FR-020..FR-022** dedup unique index + retry backoff + permanent-failure DeadLetter — [`dbo.NotificationDelivery.sql:32-35`](../../src/FundingPlatform.Database/Tables/dbo.NotificationDelivery.sql) and worker terminal-state logic.
- **FR-023..FR-027** Razor templates, no inline `<img>`, six variants × 2 (HTML + text) = 12 cshtml files, brand-grep gate green:
  ```
  grep -r -E 'Capital Semilla|Forge' src/FundingPlatform.Web/Views/Emails/  →  EMPTY
  ```
- **FR-028..FR-029** delivery audit columns + `Skipped` for missing email — [`NotificationDelivery.RecordSkipped`](../../src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationDelivery.cs).
- **FR-030..FR-031** smtp4dev sidecar + `MailCaptureClient` — [`AppHost.cs:131-156`](../../src/FundingPlatform.AppHost/AppHost.cs), [`MailCaptureClient.cs`](../../tests/FundingPlatform.Tests.E2E/Fixtures/MailCaptureClient.cs).
- **FR-032** `Assert.Ignore` removed from [`EmailTemplateSenderTests.cs`](../../tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs).
- **NFR-001** no inline `<img>` (only in code comments) — confirmed.
- **NFR-002** P95 < 30s — design satisfies (5s poll + 1s/5s/30s backoff); operational measurement deferred.
- **NFR-005** zero EF migrations — `find . -type d -name Migrations` returned EMPTY.
- **NFR-007** smtp4dev autostarts; AppHost works regardless.
- **NFR-008** CLAUDE.md configuration-knobs table updated with all `Notifications:*` keys.

### Security spot-checks

- **Mailgun ApiKey** never appears in `_logger.LogWarning(...)` calls. Logged: status + body (provider error responses do not echo the key). Basic auth header constructed correctly: `Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"))`. Per [Mailgun docs](https://documentation.mailgun.com/) this is the canonical pattern.
- **SMTP credentials** (`Notifications:Mailtrap:Username/Password`) are read from `IConfiguration` and passed to `client.AuthenticateAsync`; never logged. `SmtpCommandException.Message` does not contain credentials.
- **PII in subject lines** — only `{ApplicantName}` (display name) and `{ApplicationId}` are interpolated; no legal-id, no money, no internal commentary. FR-029 satisfied.
- **AppHost env-var binding fix** (commit `945c2f2`): `Notifications__Mailtrap__Host`/`Port` is the correct .NET configuration key shape (double-underscore → colon). Production paths (Mailgun) consume `Notifications:Mailgun:*` and are unaffected by smtp4dev env vars.

### Constitution alignment

- **§I Clean Architecture** — Domain (`NotificationEvent` enum), Application (interfaces + value objects), Infrastructure (EF entities + senders + worker + decorator + resolver), Web (Razor renderer + DI extension). No Domain → Infrastructure references. The outbox writer is invoked from the Application Service layer, not from controllers.
- **§II Rich Domain** — Workflow transitions remain on `Application` aggregate (`Submit`, `SendBack`, `Finalize`). Outbox enqueue happens in the Application Service, not the aggregate.
- **§IV Schema-First** — Two `.sql` files in dacpac; zero EF migrations.
- **§VI Simplicity** — No domain-event dispatcher abstraction; raw `HttpClient` for Mailgun; no Mailgun NuGet; no i18n key system.

### OQ-011 — Demoted-admin participating-admin predicate

[`ParticipatingAdminPredicateTests` line 41](../../tests/FundingPlatform.Tests.Integration/Notifications/ParticipatingAdminPredicateTests.cs):
```csharp
[Test, Explicit("OQ-011 — v1 predicate filters by CURRENT role; demoted admin is excluded by design. Future spec extends VersionHistory with RoleAtAction.")]
```
Correctly marked `Explicit`; does not silently pass; documented in [`research.md` R-006](./research.md#r-006--participating-admin-predicate-sources).

## Auto-fixed during this review

None. All findings are architectural / behavioral and require human judgment.

## Recommendations

### Blockers
None.

### Important — surface to user before stamp
- **FR-001 two-phase save deviation** — accept-as-documented OR queue option-B `IExecutionStrategy.ExecuteAsync` rework via `/speckit-spex-evolve`.
- **`IWorkflowTransactionScope` orphan dependency** — keep as scaffolding for the option-B fix path OR delete via spec-evolve.
- **SC-001 E2E coverage gap** — three of six event variants (RESUBMITTED, APPROVED, REJECTED) plus US6 + US7 still `Assert.Ignore`. T086 honestly marked `[ ]`; deferred to the stamp stage's full E2E pass.

### Optional / nice-to-have
- Production fallback when `Notifications:Provider` is unset should fail-fast (mirror FR-016 wording).
- Backoff schedule has an unused third entry given `MaxAttempts=3` — bump MaxAttempts to 4 OR drop the 30s entry for parity.
- `EmailDispatchWorker.ResolveProviderName` could unwrap the decorator via reflection or `IEmailSender.Inner` exposure for accurate audit when the allowlist filter wraps Mailgun.

## Conclusion

92% spec compliance. The single documented deviation (FR-001 two-phase save) is technically justified by Aspire's retry-strategy interaction with explicit transactions and carries a ~1 ms exposure window. The E2E test-surface gap for three event variants + US6/US7 is deferred to T086, with honest tracking in `tasks.md` and compensating integration + unit + source-level coverage; the brand-grep gate (FR-027/SC-006) is green and the `Assert.Ignore` placeholder mandated by FR-032 is gone.

**Recommendation:** PROCEED to `/speckit-spex-gates-stamp` with the FR-001 deviation accepted as documented; queue option-B rework as a post-merge evolution. The stamp stage's verification gate (full E2E suite) is the natural place to close the SC-001 coverage gap.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes, focusing on high-level questions that need human judgment.

**Changed files:** 47 source files + 14 Razor views + 16 tests + 2 dacpac tables + 1 AppHost edit + 1 DI extension + 1 CLAUDE.md update across 8 commits (`3da1c65`..`945c2f2`).

### Understanding the changes (8 min)

The fastest path through the implementation:

- Start with [`NotificationsServiceCollectionExtensions`](../../src/FundingPlatform.Web/Services/NotificationsServiceCollectionExtensions.cs): the wiring table tells you what the system *is*. Which sender wraps which decorator under which environment is one read.
- Then [`ApplicationService.SubmitApplicationAsync`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) (lines 120-241) and [`ReviewService.SendBackAsync`](../../src/FundingPlatform.Application/Services/ReviewService.cs) (lines 210-253): these are the workflow-event hooks. Note the two-phase save pattern and the inline rationale comments.
- Finally [`EmailDispatchWorker.DispatchOneAsync`](../../src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs#L123): the claim → resolve → render → send → terminal-state state machine.
- Question: does the `Application Service → Outbox Writer → DbContext` seam violate Clean Architecture? Is the writer correctly Application-layer scoped (interface in Application, impl in Infrastructure)? Or should outbox enqueue move to a Domain event dispatched after `Application.Submit()`?

### Key decisions that need your eyes (12 min)

**FR-001 two-phase save** (`src/FundingPlatform.Application/Services/ApplicationService.cs:175-220`, relates to [FR-001](spec.md#functional-requirements) and [research R-002](research.md#r-002--workflow-hook-point-is-the-application-service-between-addversionhistory-and-savechangesasync))

The original design called for a single transaction wrapping workflow save + outbox save. The implement subagent observed silent SaveChanges failures with `BeginTransactionAsync` under Aspire's transient-retry strategy; switching to two consecutive saves resolved it. Documented in [research.md Post-Implementation Findings](research.md#post-implementation-findings).
- Question: is the ~1 ms exposure window between save-1 (workflow committed) and save-2 (outbox not yet committed) acceptable for transactional-email semantics? OR should this be fixed via `IExecutionStrategy.CreateExecutionStrategy().ExecuteAsync(...)` to coexist with the retry policy and restore single-transaction fidelity?

**Idempotency anchor `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`** (`src/FundingPlatform.Database/Tables/dbo.NotificationDelivery.sql:32-35`, relates to [FR-020](spec.md#fr-020))

The unique index is filtered to `WHERE [RecipientUserId] IS NOT NULL` so future synthetic-address paths can coexist without violating dedup.
- Question: is `VersionHistoryId` the right idempotency anchor, or should we use a fresh UUID per outbox row? Spec-002 emits one VersionHistory row per state transition; if a future spec splits a transition into multiple rows, the dedup logic needs re-anchoring. Recorded as a trade-off in [`implementation-notes.md`](implementation-notes.md).

**Participating-admin predicate is current-role only** (`src/FundingPlatform.Infrastructure/Notifications/Resolvers/ParticipatingAdminPredicate.cs:37-46`, relates to [FR-013](spec.md#fr-013) and [OQ-011](spec.md#open-questions))

The v1 predicate filters `VersionHistory.UserId` by *currently in Admin role*. A demoted admin who acted in the past is excluded. EC-002 only partially supported in v1; documented as OQ-011 deferred to a future spec.
- Question: is the over-narrow predicate acceptable for v1, given the documented OQ-011 follow-up? The `[Test, Explicit("OQ-011 ...")]` annotation on the demoted-admin sub-case is the correct way to keep the regression detectable without silently passing.

**`IWorkflowTransactionScope` is registered but unused** (`src/FundingPlatform.Application/Notifications/IWorkflowTransactionScope.cs`, relates to [FR-001 deviation](research.md#post-implementation-findings))

The interface and EF impl exist; both `ApplicationService` and `ReviewService` inject it but never call it. Residue of the original FR-001 design.
- Question: keep the scaffolding for the option-B `ExecuteAsync(...)` fix path, or delete the dead code now?

### Areas where I'm less certain (5 min)

- `src/FundingPlatform.Web/Services/NotificationsServiceCollectionExtensions.cs:113-128` ([FR-015](spec.md#fr-015) is ambiguous on Production with unset provider) — current behavior falls into `MailtrapSmtpEmailSender` regardless of environment. Should Production with unset provider fail-fast? The spec only mandates fail-fast for `Provider=Mailgun + missing config`.
- `src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs:325-340` (`ResolveProviderName`) — the audit-column value when the `RecipientAllowlistFilter` wraps the sender is always `MailtrapSmtp` (default). Inline comment acknowledges this is acceptable because the value is audit-only. Confirm acceptable for forensic queries.
- `src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutbox.cs:39` — `RowVersion` is seeded as `Array.Empty<byte>()` for the in-memory EF provider used in some integration tests. The dacpac column `ROWVERSION` overwrites on insert in real SQL Server. Should the in-memory sentinel use a more meaningful initial value (e.g., a GUID-derived stamp) to surface bugs in pure-in-memory tests?

### Deviations and risks (5 min)

- `src/FundingPlatform.Application/Services/ApplicationService.cs:175-220` (deviation from [FR-001](spec.md#fr-001) via two-phase save). Question: accept-as-documented in spec OR queue option-B rework via `/speckit-spex-evolve`?
- `tests/FundingPlatform.Tests.E2E/Notifications/{ResubmittedNotificationsTests,ApprovedAndRejectedNotificationsTests,ProviderOutageResilienceTests,AllowlistGuardE2ETests}.cs` (deviation from [SC-001](spec.md#sc-001) — five of six event variants + US6 + US7 are `Assert.Ignore` deferred to T086). Question: is the integration-test + source-scan coverage sufficient to enter `/speckit-spex-gates-stamp`, or should T086's live E2E pass be a hard prerequisite of stamp?
- `EmailDispatchWorker.BackoffSchedule` has 3 entries but with `MaxAttempts=3` the third backoff (30s) is never used. Question: drop the 30s entry or bump MaxAttempts to 4?

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-05-12 | **Rounds:** 1/3 | **Gate:** PASS (with 3 Important findings surfaced for human review, no auto-fixes applied)

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 2 | completed |
| Architecture & Idioms | 1 | completed |
| Security | 0 | completed |
| Production Readiness | 2 | completed |
| Test Quality | 1 | completed |
| CodeRabbit (external) | - | skipped (CLI not installed) |
| Copilot (external) | - | skipped (CLI not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 3 | 0 | 3 |
| Minor | 3 | 0 | 3 |

### What was fixed automatically

Nothing. All findings are architectural or behavioral and require human judgment per the autonomous-mode rules: ambiguous findings are surfaced, not auto-fixed.

### What still needs human attention

- **Correctness — FR-001 two-phase save.** Is the ~1 ms exposure window between workflow save and outbox save acceptable, or should we restore single-transaction fidelity via `IExecutionStrategy.CreateExecutionStrategy().ExecuteAsync(...)`?
- **Architecture — orphan `IWorkflowTransactionScope`.** Keep as scaffolding for an option-B fix or delete the dead code now?
- **Test quality — SC-001 E2E coverage.** Five of six event variants + US6 + US7 are `Assert.Ignore` deferred to T086; integration tests cover the writer/worker contracts. Is this acceptable to enter the stamp stage, or should the full E2E pass be a hard prerequisite?
- **Production readiness — Production unset-Provider fallback.** Should an empty `Notifications:Provider` in Production fail-fast like Mailgun's missing-key path?
- **Production readiness — backoff schedule cardinality.** Three backoffs declared but only two used given `MaxAttempts=3`.

### Recommendation

3 Critical/Important findings surfaced for human review. None blocking. The spec is 92% compliant; the documented FR-001 deviation and the deferred E2E coverage are the two open architectural questions. Brand-grep gate green; constitution alignment intact; security review clean.

Go/no-go for `/speckit-spex-gates-stamp`: **GO with caveats** — recommend stamp-stage `verification` proceeds to run the full E2E suite, which will exercise the deferred Aspire-stack tests and close the SC-001 gap. If stamp's E2E run is green, the FR-001 deviation can be tracked as a post-merge `/speckit-spex-evolve` candidate.
