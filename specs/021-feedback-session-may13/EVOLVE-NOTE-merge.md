# EVOLVE NOTE: main merge into 021-feedback-session-may13

**Date**: 2026-05-14
**Merge commit**: `1dd1e36`
**Test reconcile commit**: `a7315f1`

> Records architectural decisions taken at merge time when main brought in
> spec 020 (AI quote comparison) + spec 021-email-notifications + spec 022
> (combined-release plan/tasks artifacts).

---

## Decision 1: Two `IEmailSender` interfaces coexist

**Conflict**: Both branches independently introduced an `IEmailSender`
interface in different namespaces.

| Branch | Namespace | Implementation | Used for |
|---|---|---|---|
| main (021-email-notifications) | `FundingPlatform.Application.Notifications.IEmailSender` | `MailgunHttpEmailSender` (prod), `MailtrapSmtpEmailSender` (dev), `NoOpEmailSender` (fail-closed fallback), wrapped by `RecipientAllowlistFilter` decorator | Workflow-event notifications driven by `NotificationOutbox` + `EmailDispatchWorker`. Idempotent, transactional, six v1 events tied to `VersionHistory` rows. |
| worktree (021-feedback-session-may13) | `FundingPlatform.Application.Abstractions.IEmailSender` | `SmtpEmailSender` (`System.Net.Mail.SmtpClient`), `LoggingEmailSender` (no-host fallback) | Time-scoped stage-expiry reminders (`StageExpiryReminderService` hosted) + identity-flow password reset (`PasswordResetTokenStore`-driven). Best-effort one-off sends; no outbox; no per-recipient delivery audit. |

**Resolution**: **Keep both. Document boundary.**

**Why**:
- Different lifecycles — notifications fire on workflow state-change; reminders fire on a wall-clock schedule; password reset fires on identity-flow request.
- Different failure semantics — notification failures are recoverable via outbox retry/dead-letter; reminder failures are best-effort (next-day cycle); password reset failures surface to the user immediately.
- Different transaction boundaries — notifications participate in the same DB transaction as the workflow change (FR-001 of 021-email-notifications); reminders + password resets are autonomous send paths.
- Different package posture — main's path uses `MailKit 3.6.0` (MIT v3); worktree's path uses `System.Net.Mail` only (NFR-005 of 021-feedback-session-may13 forbids MailKit). Coexisting is acceptable because the constraint applies to the worktree's surfaces, not project-wide.

**Future seam**: a follow-up spec could unify by extending `NotificationOutbox` to support time-triggered (`SCHEDULED_REMINDER_*`) + identity-triggered (`IDENTITY_PASSWORD_RESET`) event kinds. Out of scope for this merge.

---

## Decision 2: NotificationOutboxWriter exempted from soft-delete query gate

**Conflict**: Worktree's structural test `DashboardQueriesHonorSoftDeleteTests` (R-10 of spec 021-feedback-session-may13) requires every `_context.Applications` read to route through `IApplicationQueryFilter.ExcludeDeleted` or be exempted with a written rationale.

`NotificationOutboxWriter.GetApplicantStageGroupIdsAsync` (added by main) reads `_context.Applications` to resolve the applicant's group memberships for reviewer-bucket fan-out. The read does not filter soft-deleted applications.

**Resolution**: **Exempt the file with a documented rationale.**

**Why**:
- The outbox writer is invoked from inside `ApplicationService.SubmitAsync` / `ReviewService.SendBackAsync` / etc. — i.e., on the workflow path that committed the application's state change. By the time the outbox row is enqueued, the workflow event reflects historical truth.
- A soft-delete that races the worker is tolerable: the outbox row is idempotent (per main's `SequentialResubmitTests`), the recipient bucket already saw the workflow change, and dropping the email after-the-fact would create a worse user experience (user submits → admin deletes seconds later → reviewer never knows what happened).
- This is a single-row by-Id read on a known parent aggregate, not a list / dashboard surface.

**Cross-spec anchor**: spec 021-email-notifications FR-001 (transactional outbox) + spec 021-feedback-session-may13 R-10 (soft-delete query gate). Exemption noted in the test.

---

## Decision 3: Spec dir numbering collision tolerated

**Conflict**: Both branches use directory `specs/021-*`:
- `specs/021-email-notifications/` (from main)
- `specs/021-feedback-session-may13/` (from worktree)

Plus `specs/022-combined-release/` (main).

**Resolution**: **Keep both as-is. Per-user instruction, no rename.**

**Side effects**:
- Brainstorm overview index (`brainstorm/00-overview.md`) renumbered worktree's session #18 → #20 (main brought in #18 ai-quote-comparison + #19 email-notifications). Brainstorm doc renamed from `18-feedback-session-may13.md` → `20-feedback-session-may13.md` to match.
- `CLAUDE.md` "Active Technologies" + "Recent Changes" lists both 021 specs side-by-side with full slug suffixes for disambiguation.
- Future `/speckit-*` tooling that assumes one dir per number prefix may need updating. None tripped during this merge cycle.

---

## Decision 4: `MailKit 3.6.0` accepted as managed dep

**Conflict**: Worktree's spec 021-feedback-session-may13 NFR-005 forbids MailKit ("System.Net.Mail.SmtpClient is the only built-in SMTP client; no MailKit / new managed dep"). Main's spec 021-email-notifications adopts `MailKit 3.6.0` (MIT v3) per its own clarification (OQ-005 → "v3 MIT — no commercial-license review required, satisfies CLAUDE.md managed-NuGet rule").

**Resolution**: **Both true, no contradiction in practice.**

**Why**: NFR-005 of the worktree spec governs the surfaces *that spec* introduces (`StageExpiryReminderService`, `SmtpEmailSender` for password reset). The MailKit dep is brought in by a *different* spec (021-email-notifications) for a *different* code path (`MailtrapSmtpEmailSender` + `MailgunHttpEmailSender` in the notifications outbox worker). Worktree's NFR-005 is a per-spec constraint, not a project-wide ban.

**CLAUDE.md** updated post-merge to list both packages in the Stack section so the boundary is visible.

---

## Decision 5: Optional `IPublicCodeGenerator` dep on `ApplicationService`

**Conflict**: `ApplicationService.CreateApplicationAsync` calls `_publicCodeGenerator?.GenerateAsync()` only if the dep is non-null. Worktree's DI registers a real implementation; integration test seeders that build `ApplicationService` manually were skipping this dep — Application then saved without `PublicCode`, EF rejected.

**Resolution**: **Keep the optional dep**. Tests that exercise `CreateApplicationAsync` MUST wire a stub `IPublicCodeGenerator` (now done in `CompanyNameRequiredTests`). Ctor remains backward-compatible for tests that exercise other surfaces.

**Why**: making the dep required would have broken every existing integration test fixture that builds the service for unrelated assertions (suppliers, agreements, etc.). Optionality preserves test ergonomics; production DI always supplies the real generator (FR-008 randomness/uniqueness).

---

## Test reconciliation summary

| Failure cluster | Count | Fix |
|---|---|---|
| `PublicCode` required at EF save | 84 | Added `Helpers/TestPublicCodes.cs` + 28 construction-site patches. |
| `NotificationOutboxWriter` flagged by R-10 query-hygiene | 1 | Exemption table entry. |
| `Group.ProcessId` FK violated on SQLite | 1 | Test seeds in-memory `Process` first. |
| `Processes.RowVersion NOT NULL` on SQLite EnsureCreated | 1 | SqliteAppDbContext demotes ROWVERSION to plain `byte[]` default. |
| `CompanyNameRequiredTests` PublicCode missing | 2 | Stub `IPublicCodeGenerator` wired in setup. |

**Final**: 376 unit + 262 integration tests green.

---

## Open follow-ups

- Run full E2E suite (delivery bar per CLAUDE.md). Pre-merge stamp at `4d7e040` is invalidated; re-stamp required.
- Spec 021-email-notifications + 021-feedback-session-may13 architectural unification under a future spec.
- `MailKit` vulnerability advisories (NU1902/NU1903) carried over from main — consider upgrade in follow-up.
