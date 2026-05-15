# Review Guide: Email Notifications System

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-12

**Overall verdict:** READY — minor advisory findings only. The plan and tasks are coherent, the coverage matrix is clean across FR-001..FR-032, NFR-001..NFR-008, and SC-001..SC-010, every user story has at least one E2E task, T078 honors OQ-011 with `[Explicit]`, and T086 carries the "personally-executed green E2E" wording the repo memory mandates.

---

## What This Spec Does

This spec introduces the FundingPlatform's first email-notification subsystem. Workflow transitions (`Submit`, `SendBack`, `Submit`-as-resubmit, `Finalize`-approved, `Finalize`-rejected) currently produce no out-of-band signal — applicants poll the UI, reviewers don't know new work has arrived, and admins who once acted on an application lose visibility once they navigate away. This feature closes that loop with a transactional-outbox + background-worker architecture: outbox rows are written in the same EF transaction as the workflow state change; a hosted `BackgroundService` polls and dispatches via a pluggable `IEmailSender` (smtp4dev sidecar in Local, Mailgun HTTP API outside Local, `NoOpEmailSender` fallback). A `RecipientAllowlistFilter` decorator fail-closes outside Production.

**In scope:** six event variants (`APPLICATION_SUBMITTED_REVIEWER`, `APPLICATION_SUBMITTED_APPLICANT`, `RETURNED_TO_APPLICANT`, `RESUBMITTED_BY_APPLICANT`, `APPLICATION_APPROVED`, `APPLICATION_REJECTED`); Razor templates with HTML + plain-text fallback; idempotency via unique index; backoff retry `(1s, 5s, 30s)` over three attempts; dead-letter for permanent failures; smtp4dev Aspire sidecar; `MailCaptureClient` test surface that replaces the spec-019 `Assert.Ignore` placeholder ([spec FR-032](spec.md#functional-requirements)).

**Out of scope:** in-app notifications / SignalR / bell icon; stage-granular and signing-stage events (`STAGE_APPROVED`, `REVIEWER_ASSIGNED`, `AGREEMENT_GENERATED`, `SIGNED_PDF_UPLOADED`, `COMMENT_ADDED`); user-facing preferences UI; digests; multilingual templates (es-CR only); Mailgun bounce-webhook ingestion; SMS / push; nightly retention cleanup.

## Bigger Picture

This is the deferred-counterpart spec to [spec 019 (Programa Semilla brand)](../019-programa-semilla-brand/spec.md), which shipped `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` with `Assert.Ignore` and an explicit handoff to a later spec. Spec 021 is that handoff. It also reads (without modifying) the workflow surfaces from [spec 002 (review-approval-workflow)](../002-review-approval-workflow/spec.md), [spec 004 (applicant-response-appeal)](../004-applicant-response-appeal/spec.md), and [spec 016 (user-groups)](../016-user-groups/spec.md) — the recipient resolver crosses `Application.VersionHistory`, the stage-group assignment, and `UserGroupMembership`.

Architecturally it is the first hosted `BackgroundService` in the platform. Subsequent work (in-app notifications, signing events, Slack/Teams) is expected to layer onto the same outbox + worker spine. The provider abstraction (`IEmailSender` + `RecipientAllowlistFilter`) and `MailCaptureClient` fixture are reusable across those future specs.

---

## Coverage Matrix (FR / NFR / SC)

### Functional Requirements

| Req | Title | Implementing Tasks |
|---|---|---|
| FR-001 | Outbox row in same DB transaction | T015, T022, T032, T042 |
| FR-002 | Outbox row schema | T002, T017, T019 |
| FR-003 | Background worker polling | T026, T037 |
| FR-004 | RowVersion optimistic claim | T017, T037, T068 |
| FR-005 | Done / DeadLetter / retry state machine | T037, T066, T068 |
| FR-006 | `INotificationRecipientResolver` shape | T012, T014, T030 |
| FR-007 | Submit splits into 2 outbox rows | T009, T032, T042 |
| FR-008 | Return-to-applicant resolution | T047 |
| FR-009 | Resubmit resolution | T052 |
| FR-010 | Approved resolution | T059 |
| FR-011 | Rejected resolution | T064 |
| FR-012 | Dedup + bucket priority | T030, T079 |
| FR-013 | Participating-admin via existing reads | T031, T078 |
| FR-014 | `IEmailSender` + 3 impls (MailKit v3) | T001, T010, T038, T039, T067 |
| FR-015 | Provider selection + NoOp fallback | T026, T038 |
| FR-016 | Production fail-fast on missing keys | T026 |
| FR-017 | Allowlist decorator drops + records | T072, T074, T075 |
| FR-018 | Empty allowlist → fail-closed | T072, T076, T077 |
| FR-019 | Production bypasses allowlist | T073, T075 |
| FR-020 | Delivery unique index + pre-send check | T003, T020, T037, T053 |
| FR-021 | Transient retry `(1s,5s,30s)` × 3 | T066, T068 |
| FR-022 | Permanent failure → immediate DeadLetter | T066, T068, T070 |
| FR-023 | Razor `_EmailLayout.cshtml` + footer | T023, T024, T025 |
| FR-024 | Eight body variant partials | T033–T036, T045–T046, T050–T051, T057–T058, T062–T063 — **see Finding #1** |
| FR-025 | es-CR Spanish subjects | T016, T033, T035, T045, T050, T057, T062 |
| FR-026 | CTA deep links to existing routes | T033, T035, T045, T050, T057, T062 |
| FR-027 | Brand-grep gate green | T041, T081, T082 |
| FR-028 | NotificationDelivery row schema | T003, T018, T020 |
| FR-029 | Null email → `Skipped` | T018, T080 |
| FR-030 | smtp4dev Aspire resource | T005 |
| FR-031 | `MailCaptureClient` in `AspireFixture` | T028, T029 |
| FR-032 | Replace `EmailTemplateSenderTests.Assert.Ignore` | T081 |

### Non-Functional Requirements

| NFR | Title | Implementing Tasks |
|---|---|---|
| NFR-001 | No inline `<img>` | T024, T041, T081 |
| NFR-002 | P95 < 30 s; P99 < 2 min | T083 (P95); T071 (P99 via outage test ≤ 2 min) |
| NFR-003 | Email PII ≤ in-app PII | T062 (rejection no-leak) — **see Finding #2** |
| NFR-004 | Worker exception ≠ host crash | T037, T066, T068 |
| NFR-005 | Dacpac only, no EF migrations | T002, T003, T019, T020 |
| NFR-006 | es-CR Spanish only | T016, T041 |
| NFR-007 | Sidecar auto-start; failure → NoOp WARN | T005, T026 |
| NFR-008 | New config keys logged in CLAUDE.md | T006, T007, T085 |

### Success Criteria

| SC | Title | Verifying Tasks |
|---|---|---|
| SC-001 | E2E per event variant | T043, T048, T055, T060, T065 |
| SC-002 | Predicate matches table | T078, T079 |
| SC-003 | Idempotency holds | T053 |
| SC-004 | Allowlist blocks 100% in non-prod | T076, T077 |
| SC-005 | Placeholder removed | T081 |
| SC-006 | Brand-grep gate stays green | T041, T082 |
| SC-007 | Provider-outage resilience | T071 |
| SC-008 | Zero new EF migrations | T002, T003, T019, T020 (Constitution §IV) |
| SC-009 | P95 < 30 s observed | T083 |
| SC-010 | Qualitative usability validation | Not automated — manual UAT (acceptable per spec text) |

No orphan requirements. Every FR / NFR / SC maps to ≥1 task.

## Story-Task Mapping

| Story | Priority | Tasks | E2E Test? |
|---|---|---|---|
| US1 (Submit closed loop) | P1 | T030–T043 (14 tasks, incl. T037 worker, T038 NoOp, T039 SMTP) | T043 |
| US2 (SendBack) | P1 | T044–T048 | T048 |
| US3 (Resubmit + idempotency) | P1 | T049–T055 | T055 (plus T053/T054 integration) |
| US4 (Approved) | P1 | T056–T060 | T060 |
| US5 (Rejected) | P1 | T061–T065 | T065 |
| US6 (Outage resilience) | P2 | T066–T071 | T071 |
| US7 (Allowlist fail-closed) | P2 | T072–T077 | T077 |
| US8 (Participating-admin matrix) | P3 | T078–T080 | Integration-only (acceptable — predicate is internal; resolver E2E coverage already in US1) |

All 8 user stories have at least one mandatory test task. P1 + P2 stories each have a dedicated `[USn]` E2E. US8 is integration-only because the predicate is queried below the controller boundary and the resolver's full output is already exercised end-to-end in US1.

## Areas where I'm less certain

- **FR-024 wording vs. plan template list ([spec FR-024](spec.md#functional-requirements) vs. [plan.md Project Structure](plan.md#project-structure)).** Spec calls for *"eight body variant partials"* including dedicated admin-flavored copies for `RETURNED_TO_APPLICANT`, `APPLICATION_APPROVED`, `APPLICATION_REJECTED`. Plan lists 6 `.cshtml` body files (12 if you count the `.text.cshtml` plain-text twin per variant). My read: the implementation routes participating-admins through the *applicant variant* template (consistent with FR-008/-010/-011 wording: "applicant-variant... AND every participating admin (applicant-variant)") rather than rendering a distinct admin partial. If that interpretation is wrong, the plan needs three additional Razor partials. **Finding #1 (advisory)** below.
- **NFR-003 enforcement scope ([spec NFR-003](spec.md#non-functional-requirements)).** Only T062 explicitly calls out the "no reviewer-internal commentary verbatim" rule (rejection body). Approved bodies, send-back bodies, and resubmit bodies don't carry a matching assertion. PII surface there is narrower (no decision rationale), but a `RazorEmailRendererTests` row covering every body variant would harden the gate. **Finding #2 (advisory)**.
- **T032 worker hook location ([tasks.md T032](tasks.md#phase-3-user-story-1--applicant-submits-and-the-workflow-speaks-back-priority-p1--mvp)).** The task points to `src/FundingPlatform.Application/.../ApplicationService.cs` with an ellipsis path because the exact folder is "expand if needed" in the plan tree. This is the only file path in the entire task list that isn't fully resolved. A reviewer with codebase context should sanity-check that the file already exists and that the path resolves cleanly.

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md Recipient Rules](spec.md#recipient-rules) and [spec.md Event Catalog v1](spec.md#event-catalog-v1). The cleanest mental model is: each row in the Event Catalog produces exactly one outbox row per recipient bucket, with bucket priority `applicant > reviewer > admin` resolving collisions.

- Is the 6-enum split (with `APPLICATION_SUBMITTED` decomposed into `_REVIEWER` and `_APPLICANT`) cleaner than a single event with a variant column? The clarifying session ([Clarifications 2026-05-12](spec.md#clarifications)) settled this, but a reviewer might still want to push back on enum sprawl.
- Is the "participating admin = explicit prior action via `VersionHistory`" definition tight enough for v1? See [research.md R-006](research.md) and [spec OQ-011](spec.md#open-questions) — the v1 predicate has a known false-negative for demoted admins, deliberately deferred.

### Key decisions that need your eyes (12 min)

**Outbox in same EF transaction, not domain events** ([plan.md Summary](plan.md#summary), [tasks.md T015/T022](tasks.md#phase-2-foundational-blocking-prerequisites))

The plan rejects a domain-event dispatcher. Outbox enqueue happens from the Application Service between `AddVersionHistory(...)` and `SaveChangesAsync()`. This keeps the `Application` aggregate storage-agnostic but pushes responsibility for "remember to enqueue" onto each service method.
- Question: is a missed enqueue (developer forgets to call `_outboxWriter.EnqueueAsync` in a new state-transition path) caught by anything other than a missing E2E? No outbox-vs-state-transition reconciliation job exists in v1.

**Bucket priority `applicant > reviewer > admin`** ([spec FR-012](spec.md#functional-requirements), [tasks.md T079](tasks.md#phase-10-user-story-8--participating-admin-predicate-is-correct-across-role-changes-priority-p3))

When a user qualifies via multiple buckets (rare but possible), the applicant variant wins. Rationale: the applicant's relationship to the application is the strongest signal.
- Question: in the dedup test ([T079](tasks.md#phase-10-user-story-8--participating-admin-predicate-is-correct-across-role-changes-priority-p3)), is the chosen variant on collision the *most informative* one? An applicant who is also a participating admin would lose access to admin-flavored copy. The spec accepts this. Is it the right trade-off?

**Retry schedule `(1s, 5s, 30s)` × 3, then DeadLetter** ([spec FR-021](spec.md#functional-requirements), [tasks.md T066](tasks.md#phase-8-user-story-6--provider-outage-does-not-lose-notifications-priority-p2))

Three attempts, fixed schedule, no jitter.
- Question: for a Mailgun 429 (rate-limited) burst, three attempts within 36 seconds may all hit the same backpressure. Is the schedule's terminal step (`30s`) long enough for typical Mailgun cool-off?

**Allowlist fail-closes when empty** ([spec FR-018](spec.md#functional-requirements), [tasks.md T076](tasks.md#phase-9-user-story-7--non-prod-allowlist-guard-blocks-real-users-fail-closed-priority-p2))

An unset / empty `Notifications:NonProdAllowlist` in Staging produces zero deliveries.
- Question: this is the right default (no surprise spam), but is there a developer-facing log line at boot that says "Allowlist is empty; no emails will leave"? The plan implies WARN via the per-drop log, not a single boot-time signal. Consider whether a startup log line would prevent silent zero-delivery confusion.

**OQ-011 v1 predicate ships with a known gap** ([spec OQ-011](spec.md#open-questions), [tasks.md T078](tasks.md#phase-10-user-story-8--participating-admin-predicate-is-correct-across-role-changes-priority-p3))

T078 marks the demoted-admin case as `[Test, Explicit("OQ-011 — deferred to a future spec")]`. Skip rather than fail.
- Question: is a `[Explicit]` test the right discipline, or should this be a `[Test]` with a `Assert.Inconclusive` so it shows in test runs? Either is defensible. The plan chose `[Explicit]`; the limitation is documented in the task description and in spec OQ-011.

### Risks and open questions (5 min)

- If [spec FR-024](spec.md#functional-requirements) genuinely requires three additional admin-flavored partials, the plan's template count is short. See Finding #1.
- If a developer adds a *seventh* event variant later (e.g., `STAGE_APPROVED` per the deferred §Out of Scope list), they must also update [tasks.md T016 NotificationTemplateBindings](tasks.md#phase-2-foundational-blocking-prerequisites) and add a partial. Is the bindings table a strong enough enforcement point, or should it be exhaustive-checked at startup?
- The plan assumes single-replica worker ([spec OQ-009](spec.md#open-questions)). FR-004 + FR-020 are sound under multi-replica, but no test exercises the contended-claim path against two worker instances. EC-008 documents the future need.

## Constitution Check (per principle)

| Principle | Verdict | Evidence |
|---|---|---|
| §I Clean Architecture | PASS | Resolver + sender interfaces in Application; EF mapping + workers + providers in Infrastructure; Razor templates in Web. No reverse references. ([plan.md Project Structure](plan.md#project-structure)) |
| §II Rich Domain Model | PASS | `Application.Submit/SendBack/Finalize` unchanged. Outbox enqueue is invoked from the Application Service, not the aggregate. |
| §III E2E Mandatory | PASS | Every P1/P2 user story has a dedicated `[USn]` E2E task (T043, T048, T055, T060, T065, T071, T077). T086 mandates personally-executed green E2E suite. |
| §IV Schema-First | PASS | T002 + T003 add `.sql` files; T019/T020 EF mapping only; CI grep gate over `**/Migrations/**` remains green (SC-008). |
| §V SDD | PASS | Spec → Plan → Tasks → Implementation; 5 clarifications resolved in session 2026-05-12; OQ-011 explicitly deferred and tracked in T078. |
| §VI Simplicity | PASS | No domain-event dispatcher; no i18n key system; no in-app channel; no bounce-webhook ingestion. Each rejection logged in `implementation-notes.md` and spec §Out of Scope. |

## Red Flags Found

| # | Severity | Finding | Remediation |
|---|---|---|---|
| 1 | advisory | [FR-024](spec.md#functional-requirements) reads "eight body variant partials" with explicit admin-flavored copies; plan lists 6 HTML partials. | Either (a) add 3 admin-flavored partials (`ReturnedToApplicantAdmin.cshtml`, `ApplicationApprovedAdmin.cshtml`, `ApplicationRejectedAdmin.cshtml`) + plain-text twins and corresponding tasks, OR (b) reconcile FR-024 wording with the plan's intent that participating admins receive the applicant-variant body verbatim. The latter is consistent with FR-008/-010/-011's "applicant-variant" wording for the admin bucket. |
| 2 | advisory | NFR-003 (no reviewer-internal-commentary verbatim) is asserted only in T062 (rejection body). | Extend `RazorEmailRendererTests` (T041, T084) to assert each body variant's PII surface against an allow-list. |
| 3 | advisory | T032 file path uses `src/FundingPlatform.Application/.../ApplicationService.cs` — the ellipsis is unresolved. | Confirm the existing file location (likely `src/FundingPlatform.Application/ApplicationServices/ApplicationService.cs` per plan.md) and inline the full path. |
| 4 | advisory | Single-replica worker assumption ([OQ-009](spec.md#open-questions)) has no test of the two-replica contended-claim path. | Acceptable for v1. Note for future spec; covered by EC-008. |

No Critical or Important findings. Pipeline is safe to advance to `/speckit-implement`.

## OQ-011 / T086 spot-checks

- **OQ-011 handling**: [tasks.md T078](tasks.md#phase-10-user-story-8--participating-admin-predicate-is-correct-across-role-changes-priority-p3) explicitly marks the `CurrentReviewerWithVersionHistory_isExcluded` subcase with `[Test, Explicit("OQ-011 — deferred to a future spec")]` and references the limitation in XML doc. Does NOT silently fail. PASS.
- **T086 wording**: "Run the **full** E2E suite locally: `dotnet test tests/FundingPlatform.Tests.E2E`. Confirm 100% green. Per memory feedback `delivery_requires_e2e_green`, NOTHING ships until this passes." Wording matches the repo memory rule. PASS.

## Sign-off Recommendation

**READY for `/speckit-implement`.** No blocking findings. All four advisory findings can be addressed during implementation or in a follow-up pass without re-running `/speckit-tasks`. The plan is constitutionally clean, the coverage matrix has zero orphans, and the critical-path tasks (T032 outbox hook, T037 worker, T066 retry, T072 allowlist, T078 OQ-011, T081 placeholder removal, T086 personally-executed E2E) are all present and worded with sufficient precision.

---
*Full context in linked [spec](spec.md), [plan](plan.md), [tasks](tasks.md), [research](research.md), [data-model](data-model.md), [contracts](contracts/), and [quickstart](quickstart.md).*
