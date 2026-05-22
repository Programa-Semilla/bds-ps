# Merge Tasks: main → 021-feedback-session-may13

**Created**: 2026-05-14
**Merge base**: `a2e1a85` (last common commit)
**Worktree HEAD**: `4d7e040` (stamp pass)
**Incoming HEAD**: `9b6a024` (origin/main — combined release of specs 020 + 021-email-notifications + 022 + 019 snapshots)

> This file lives at `specs/021-feedback-session-may13/MERGE-TASKS.md` until Phase 0 renames the dir to `023-feedback-session-may13/`.

---

## Conceptual divergence

### Incoming from main (1 squashed commit `9b6a024`)

- **Spec 020 — AI Quote Comparison**
  - New domain entities: `ComparisonArtifact`, `ComparisonJob`, `FreshnessResult`
  - New schema tables: `ComparisonArtifacts`, `ComparisonJobs`
  - Application abstractions: `IAiClient`, `IComparisonOrchestrator`, `IComparisonArtifactRepository`, `IPiiRedactor`, `InputDescriptor`
  - Application services: `ComparisonOrchestrator`, `ComparisonNormalizer`, `InputHasher`, `PromptCatalog`, `RateLimitGuard`, `SchemaValidator`, `SupplierAssembler`, `TokenCapGuard`, `GenerateComparisonCommandHandler`, `AdminAuditEventComparisonFactory`
  - Infrastructure: `AnthropicAiClient`, `StubAiClient`, `ComparisonJobWorker` (hosted), `ComparisonJobReaper` (hosted), `RateLimitCounter`, `PiiRedactor`
  - Touches `ReviewService.cs` (adds AI comparison action surface)

- **Spec 021-email-notifications — Email Notifications System** *(numbering collision with this branch)*
  - New domain: `NotificationEvent` value object/entity
  - New schema tables: `NotificationOutbox`, `NotificationDelivery`
  - Application abstractions: `IEmailSender` *(in `Application.Notifications` namespace)*, `IEmailTemplateRenderer`, `INotificationOutboxWriter`, `INotificationRecipientResolver`, `IWorkflowTransactionScope`, `NotificationPayload`, `NotificationRecipient`, `RecipientBucket`, `NotificationTemplateBindings`
  - Infrastructure: `MailgunHttpEmailSender`, `RazorEmailRenderer`, `RecipientAllowlistFilter` (decorator), `EmailDispatchWorker` (hosted), outbox writer/reader, recipient resolver
  - AppHost: smtp4dev sidecar wired into Aspire orchestration
  - Touches `ApplicationService.Submit` (transactional outbox dispatch + resubmit detection + two-phase save)
  - Touches `ReviewService.SendBack/Approve/Reject` (outbox dispatch on every workflow transition)
  - Test harness: `NotificationsTestHarness`, allowlist/dedup/idempotency/dead-letter integration tests
  - Replaces placeholder `EmailTemplateSenderTests.Assert.Ignore` from spec 019

- **Spec 022-combined-release** — `plan.md` + `tasks.md` only (PR composition artifact, no `spec.md`)

- **Spec 019** — 4 new PNG snapshots only

### On worktree (25 commits — spec 021-feedback-session-may13)

- US1: Process + Plantilla admin
- US2: Applicant flow (Impact-first draft, autosave, /review, submit gating, PublicCode)
- US3: SupplierAdmin role
- US4: Stage expiry + reminders (`StageExpiryReminderService` hosted + `SmtpEmailSender` direct via `System.Net.Mail.SmtpClient`, NFR-005 forbids MailKit)
- US5: Profile + forgot-password (`PasswordResetToken` flow)
- US6: Admin dashboard KPIs + reviewer pending-quotation tile + supplier search wiring
- US7: Acompañamiento copy + public landing scaffold + forbidden-strings sweep
- US8: Soft-delete predicate fix
- 7 new tables, 7 alters, 3 PostDeployment seeds, 6 new entities + value objects

### Two structural problems

1. **Spec-number collision** — both sides claim `021/`. Worktree must rename to `023/` (022 taken by combined-release).
2. **Two parallel `IEmailSender` interfaces** — different namespaces; can coexist but conceptually overlap.

---

## File collisions (12)

| File | Type | Strategy |
|---|---|---|
| `CLAUDE.md` | additive | Merge both Active Technologies entries + Recent Changes |
| `.specify/feature.json` | metadata | Keep worktree (active feature) |
| `brainstorm/00-overview.md` | additive | Merge index entries + open threads |
| `src/FundingPlatform.Web/appsettings.json` | additive | Keep both: main's `Notifications` block + log levels, worktree's `Storage.Categories.PublicLandingFile` |
| `src/FundingPlatform.Web/Program.cs` | additive | Keep both registrations |
| `src/FundingPlatform.Infrastructure/DependencyInjection.cs` | additive | Keep both registrations |
| `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj` | additive | Keep both file/package refs |
| `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` | additive | Keep both DbSets + entity configs |
| `src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs` | conceptual-additive | Worktree adds soft-delete predicate; main adds outbox-helper queries — keep both |
| `src/FundingPlatform.Web/Controllers/ReviewController.cs` | conceptual-additive | Worktree adds /review confirmation; main adds AI-comparison action — keep both |
| `src/FundingPlatform.Application/Services/ApplicationService.cs` | **hot spot** | See Phase 1b |
| `src/FundingPlatform.Application/Services/ReviewService.cs` | **hot spot** | See Phase 1c |

---

## Ordered checklist

### Phase 0 — Pre-merge spec rename

- [ ] **0.1** `git mv specs/021-feedback-session-may13 specs/023-feedback-session-may13`
- [ ] **0.2** Search-and-replace `021-feedback-session-may13` → `023-feedback-session-may13` across:
  - `specs/023-feedback-session-may13/spec.md`
  - `specs/023-feedback-session-may13/plan.md`
  - `specs/023-feedback-session-may13/tasks.md`
  - `specs/023-feedback-session-may13/REVIEW-CODE.md`
  - `specs/023-feedback-session-may13/MERGE-TASKS.md` (this file)
  - `CLAUDE.md` (Active Technologies + Recent Changes mentions)
- [ ] **0.3** Commit: `chore(023): rename spec dir 021→023 to clear main's email-notifications collision`
- [ ] **0.4** *(optional)* `git branch -m 021-feedback-session-may13 023-feedback-session-may13`

### Phase 1 — Merge

- [ ] **1.0** `git fetch origin main && git merge origin/main` — expect 12 conflicts
- [ ] **1a** Resolve additive collisions (10 files) — concatenate sections, no behavior change
- [ ] **1b** Resolve `ApplicationService.cs` (HOT SPOT)
  - Layer order in merged `SubmitAsync`:
    1. Worktree gating (required-field + min-quotations + Impact-first invariants)
    2. `application.Submit(minQuotations)` (existing)
    3. Worktree's `PublicCode` generation (if absent)
    4. `application.AddVersionHistory(vhRow)` (existing)
    5. Main's `isResubmit = await _outboxWriter.HasPriorSendBackAsync(...)` — must read **before** AddVersionHistory or capture its semantic equivalent
    6. `_applicationRepository.UpdateAsync(application)` + `SaveChangesAsync()`
    7. Main's outbox enqueue (two-phase save — workflow first, outbox second)
  - Constructor: append main's `INotificationOutboxWriter outboxWriter` + `IWorkflowTransactionScope txScope` to worktree's existing dep set
  - Keep main's comment block on why two-phase save (no explicit BeginTransaction conflicts with Aspire SqlClient retry policy)
- [ ] **1c** Resolve `ReviewService.cs` (HOT SPOT)
  - Worktree changes: line-code req+unique tweak — keep at command-entry validation
  - Main changes: outbox dispatch on `SendBack`/`Approve`/`Reject` — runs after state change + SaveChanges
  - Both behaviors stack; no order conflict
- [ ] **1.99** `git diff --check` — no conflict markers left

### Phase 2 — Build + tests

- [ ] **2.0** `dotnet build FundingPlatform.slnx` — clean
- [ ] **2a** `dotnet test tests/FundingPlatform.Tests.Unit` — green
  - Watch for: ctor-signature breakage on `ApplicationService` test doubles (now requires `IOutboxWriter` + `ITxScope` mocks/stubs)
- [ ] **2b** `dotnet test tests/FundingPlatform.Tests.Integration` — green
  - Watch for: dacpac deploy failures from merged schema (notification + AI comparison + worktree's 7 new tables must coexist)
  - Real DB only, no mocks (CLAUDE.md)

### Phase 3 — Document architectural decisions

- [ ] **3.0** Write `specs/023-feedback-session-may13/EVOLVE-NOTE-merge.md`:
  - **Decision**: Two `IEmailSender` interfaces coexist post-merge.
    - `Application.Notifications.IEmailSender` (from spec 021-email) — outbox-driven, transactional, workflow events tied to `VersionHistory`.
    - `Application.Abstractions.IEmailSender` (from spec 023) — direct SMTP via `System.Net.Mail`, time-scoped (stage-expiry reminders) + identity-scoped (password reset).
  - **Why**: different lifecycles, different failure semantics. Reminders must not block on outbox dispatch latency; password reset is identity-flow not workflow-flow.
  - **Future**: file follow-up spec to evaluate unification (would require outbox to support time-triggered + identity-triggered events).

### Phase 4 — Delivery bar

- [ ] **4.0** `dotnet test tests/FundingPlatform.Tests.E2E` — full suite, personally executed, all green
  - Worktree US1–US8 tests
  - Main's notification harness (`NotificationsTestHarness`-driven Idempotency/Allowlist/DeadLetter/SequentialResubmit/etc.)
  - Main's AI comparison tests (Anthropic stub mode)
  - Critical end-to-end: applicant Submit fires worktree's PublicCode generation AND main's outbox notification dispatch in same transaction; outbox row inserted; worker drains; smtp4dev sidecar captures email

### Phase 5 — Re-stamp

- [ ] **5.0** Run `speckit-spex-gates-stamp` on merged branch
  - Pre-merge stamp (`4d7e040`) is invalidated by main's incoming subsystems
  - Re-stamp must reflect post-merge code-to-spec compliance for spec 023

### Phase 6 — Push (confirm with user)

- [ ] **6.0** Confirm with user before push (shared state)
- [ ] **6.1** `git push origin 023-feedback-session-may13` (or `021-` if branch not renamed)

---

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| ctor-signature break across `ApplicationService` test doubles | High | Medium | Phase 2a — fix once, propagates |
| dacpac deploy fails when both schemas applied together | Medium | High | Phase 2b — investigate per-table; no FK or sproc collisions expected |
| Outbox dispatch breaks worktree's submit-gating expectations (E2E) | Medium | High | Phase 1b layering order; Phase 4 critical-path E2E |
| `PublicCode` not yet generated when outbox enqueue reads it for template binding | Medium | Medium | Generate `PublicCode` BEFORE outbox enqueue (Phase 1b step 3) |
| smtp4dev sidecar conflicts with worktree's direct `SmtpEmailSender` (port 25) | Low | Low | Different containers; sidecar binds explicit port; reminders use config-driven host |
| Branch rename breaks downstream PR/CI tooling | Low | Low | Phase 0b is optional; can defer |
