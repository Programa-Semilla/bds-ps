# Tasks: Post-Resolution Email Notifications

**Input**: Design documents from `specs/028-post-resolution-notifications/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notification-events.md, quickstart.md

**Tests**: INCLUDED — Constitution §III mandates Playwright E2E per user story; SC-001..008 require E2E + integration coverage.

**Organization**: Tasks grouped by user story (US1/US2/US3, all P1) for independent implementation and testing. This is an additive increment to shipped spec 021 — no project init, no schema change.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no incomplete-task dependency)
- **[Story]**: US1 / US2 / US3 (Setup / Foundational / Polish carry no story label)

## Path Conventions

Clean-Architecture .NET solution (`FundingPlatform.slnx`): `src/FundingPlatform.{Domain,Application,Infrastructure,Web}`, `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

⚠️ **Shared-file note**: three files are edited by all three stories — `NotificationEvent.cs` (enum + storage switches), `NotificationTemplateBindings.cs` (bindings), `NotificationRecipientResolver.cs` (bucket arms). Tasks touching them within a story are sequential; if stories run in parallel, coordinate merges on these three files. Razor partials and test files are per-event → freely `[P]`.

---

## Phase 1: Setup

**Purpose**: Confirm baseline before additive changes.

- [X] T001 Confirm green baseline: `dotnet build FundingPlatform.slnx` passes on branch `028-post-resolution-notifications`; confirm `dbo.NotificationOutbox.EventType` is `VARCHAR(64)` (no dacpac/EF-migration work is in scope for this feature).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting extensions every one of the 12 events depends on. **No user-story work may begin until this phase is complete.**

- [X] T002 Add nullable `ActorUserId` field to `NotificationPayload` (ctor + JSON serialization; tolerate absence on legacy rows) in `src/FundingPlatform.Application/Notifications/NotificationPayload.cs`
- [X] T003 Add `CtaRouteTemplate` member to the `Binding` record and backfill the existing 7 bindings with their current bucket routes (`/Application/Details/{id}` or `/Review/{id}`) to preserve behavior, in `src/FundingPlatform.Application/Notifications/Templates/NotificationTemplateBindings.cs`
- [X] T004 Make CTA composition event-aware in `RazorEmailRenderer`: build CTA from `Notifications:BaseUrl` + `Binding.CtaRouteTemplate` with `{id}` → `ApplicationId` (routes without `{id}`, e.g. `/Review/SigningInbox`, used verbatim), replacing the bucket-only branch, in `src/FundingPlatform.Infrastructure/Notifications/RazorEmailRenderer.cs`
- [X] T005 Add actor-exclusion filter in `NotificationRecipientResolver.ResolveAsync`: after bucket resolution + dedup, drop any recipient whose `UserId == payload.ActorUserId` (null actor = no-op), in `src/FundingPlatform.Infrastructure/Notifications/Resolvers/NotificationRecipientResolver.cs`
- [X] T006 Inject `INotificationOutboxWriter` into the constructors of `ApplicantResponseService`, `SignedUploadService`, and `FundingAgreementService` (DI registration already present in `NotificationsServiceCollectionExtensions`); assign private fields — `src/FundingPlatform.Application/Services/{ApplicantResponseService,SignedUploadService,FundingAgreementService}.cs`
- [X] T007 [P] Regression test: the existing 7 spec-021 events still resolve identical recipient buckets and produce their original CTA URLs after the T003–T005 refactor, in `tests/FundingPlatform.Tests.Integration/Notifications/`

**Checkpoint**: Foundation ready — US1, US2, US3 can now proceed (coordinating on the three shared files).

---

## Phase 3: User Story 1 — Applicant response reaches the reviewer (Priority: P1) 🎯 MVP

**Goal**: When the applicant submits accept/reject decisions on the resolution, reviewers (group) + participating admins are emailed; applicant gets nothing. Fixes the reported bug.

**Independent Test**: Applicant submits a response via the real UI → smtp4dev shows each group reviewer received `El solicitante respondió la resolución — Solicitud #{id}` (CTA `/Review/{id}`); applicant received zero.

- [X] T008 [US1] Add `ResponseSubmittedReviewer` (storage `RESPONSE_SUBMITTED_REVIEWER`) to the enum + `ToStorageString`/`FromStorageString` in `src/FundingPlatform.Domain/Notifications/NotificationEvent.cs`
- [X] T009 [US1] Add the `ResponseSubmittedReviewer` `Binding` (subject `El solicitante respondió la resolución — Solicitud #{ApplicationId}`, views `ResponseSubmittedReviewer`(.text), `CtaRouteTemplate=/Review/{id}`, variant key) in `NotificationTemplateBindings.cs`
- [X] T010 [US1] Add `ResponseSubmittedReviewer` to `IncludesReviewerBucket` in `NotificationRecipientResolver.cs`
- [X] T011 [P] [US1] Create `ResponseSubmittedReviewer.cshtml` + `ResponseSubmittedReviewer.text.cshtml` (es-CR, no inline `<img>`, `_EmailLayout`) in `src/FundingPlatform.Web/Views/Emails/`
- [X] T012 [US1] Enqueue `ResponseSubmittedReviewer` in `ApplicantResponseService.SubmitResponseAsync` using the two-phase pattern (save VH → build payload with `ActorUserId=applicant`, `StageGroupIds` via `GetApplicantStageGroupIdsAsync` → `EnqueueAsync(…, vhRow.Id, …)` → save) in `src/FundingPlatform.Application/Services/ApplicantResponseService.cs`
- [X] T013 [P] [US1] Integration: recipient matrix (reviewers-in-group + participating admin receive; applicant + non-participating admin do not) and double-pass idempotency for this event, in `tests/FundingPlatform.Tests.Integration/Notifications/`
- [X] T014 [US1] E2E `ResponseNotificationsTests` driving the real UI (applicant opens response screen, submits decisions); assert reviewer capture (subject + `/Review/{id}`), zero applicant email, sender display, no `<img>`, no "Capital Semilla"/"Forge", in `tests/FundingPlatform.Tests.E2E/Notifications/`

**Checkpoint**: US1 independently demoable — the reported bug is closed (SC-007).

---

## Phase 4: User Story 2 — Appeal lifecycle fully voiced (Priority: P1)

**Goal**: Open-appeal, bidirectional appeal messages, and resolve (incl. GrantReopenToReview dual-fire) all notify the counterparty.

**Independent Test**: open → applicant message → reviewer message → resolve via the real UI; smtp4dev captures `APPEAL_OPENED_REVIEWER`, `APPEAL_MESSAGE_REVIEWER`, `APPEAL_MESSAGE_APPLICANT`, `APPEAL_RESOLVED_APPLICANT` to the correct opposite party; a GrantReopenToReview resolution also produces `APPEAL_REOPENED_REVIEWER`.

- [X] T015 [US2] Add the 5 appeal events (`AppealOpenedReviewer`, `AppealMessageReviewer`, `AppealMessageApplicant`, `AppealResolvedApplicant`, `AppealReopenedReviewer`) to the enum + both storage switches in `NotificationEvent.cs`
- [X] T016 [US2] Add the 5 `Binding` entries (subjects + view names + `CtaRouteTemplate` per contract: `/ApplicantResponse/Appeal/{id}`, `/ApplicantResponse/Index/{id}`, `/Review/{id}`) in `NotificationTemplateBindings.cs`
- [X] T017 [US2] Add bucket arms — `AppealOpenedReviewer`/`AppealMessageReviewer`/`AppealReopenedReviewer` → `IncludesReviewerBucket`; `AppealMessageApplicant`/`AppealResolvedApplicant` → `IncludesApplicantBucket` — in `NotificationRecipientResolver.cs`
- [X] T018 [P] [US2] Create 10 partials (5 events × HTML+text); `AppealResolvedApplicant`(.text) switch body copy on `Model.Payload.OutcomeCode` (`AppealUpheld` / `AppealReopenedToDraft` / `AppealReopenedToReview`), in `src/FundingPlatform.Web/Views/Emails/`
- [X] T019 [US2] Enqueue `AppealOpenedReviewer` (two-phase, `ActorUserId=applicant`) in `ApplicantResponseService.OpenAppealAsync`
- [X] T020 [US2] Enqueue directional appeal-message event in `ApplicantResponseService.PostMessageAsync`: author `== Application.Applicant.UserId` → `AppealMessageReviewer`, else → `AppealMessageApplicant`; `ActorUserId=author` (two-phase)
- [X] T021 [US2] Enqueue `AppealResolvedApplicant` (all 3 outcomes, set `OutcomeCode`) in `ApplicantResponseService.ResolveAppealAsync`; **additionally** enqueue `AppealReopenedReviewer` when the resolution is `GrantReopenToReview` (same phase-2 save, same `VersionHistoryId`); `ActorUserId=reviewer`
- [X] T022 [P] [US2] Integration: GrantReopenToReview yields exactly 2 distinct emails; 3 successive applicant messages yield 3 emails (no dedup collapse); message direction correctness; idempotency double-pass — in `tests/FundingPlatform.Tests.Integration/Notifications/`
- [X] T023 [US2] E2E `AppealNotificationsTests` driving the real UI (open → applicant msg → reviewer reply → resolve, plus a GrantReopenToReview variant); assert directional captures + dual-fire, in `tests/FundingPlatform.Tests.E2E/Notifications/`

**Checkpoint**: US2 independently demoable — full appeal thread is voiced.

---

## Phase 5: User Story 3 — Convenio signing ceremony fully voiced (Priority: P1)

**Goal**: Convenio generate/regenerate, signed-upload submit/replace/withdraw, and approve/reject all notify the counterparty.

**Independent Test**: reviewer generates convenio (applicant gets `AGREEMENT_GENERATED_APPLICANT` + an `AgreementGenerated` history row appears), applicant uploads (reviewers get `SIGNED_UPLOAD_SUBMITTED_REVIEWER` → `/Review/SigningInbox`), reviewer approves (applicant gets `AGREEMENT_EXECUTED_APPLICANT`); reject variant → `SIGNED_UPLOAD_REJECTED_APPLICANT`.

- [X] T024 [US3] Add the 6 signing events (`AgreementGeneratedApplicant`, `SignedUploadSubmittedReviewer`, `SignedUploadReplacedReviewer`, `SignedUploadWithdrawnReviewer`, `AgreementExecutedApplicant`, `SignedUploadRejectedApplicant`) to the enum + both storage switches in `NotificationEvent.cs`
- [X] T025 [US3] Add the 6 `Binding` entries (subjects + view names + `CtaRouteTemplate`: `/Applications/{id}/FundingAgreement/` for applicant events, `/Review/SigningInbox` for reviewer events) in `NotificationTemplateBindings.cs`
- [X] T026 [US3] Add bucket arms — `SignedUploadSubmitted/Replaced/WithdrawnReviewer` → `IncludesReviewerBucket`; `AgreementGenerated/AgreementExecuted/SignedUploadRejectedApplicant` → `IncludesApplicantBucket` — in `NotificationRecipientResolver.cs`
- [X] T027 [P] [US3] Create 12 partials (6 events × HTML+text); `SignedUploadRejectedApplicant` body conveys "changes required" + CTA only, no verbatim reviewer commentary (NFR-003), in `src/FundingPlatform.Web/Views/Emails/`
- [X] T028 [US3] In `FundingAgreementService.PersistGenerationAsync`: append `Application.AddVersionHistory(actor, "AgreementGenerated", details)` (domain method, §II), then enqueue `AgreementGeneratedApplicant` (two-phase, `ActorUserId=reviewer`) — fires on both generate and regenerate — in `src/FundingPlatform.Application/Services/FundingAgreementService.cs`
- [X] T029 [US3] Enqueue reviewer signing events in `SignedUploadService`: `SignedUploadSubmittedReviewer` in `UploadAsync`, `SignedUploadReplacedReviewer` in `ReplaceAsync`, `SignedUploadWithdrawnReviewer` in `WithdrawAsync` (two-phase, `ActorUserId=applicant`)
- [X] T030 [US3] Enqueue applicant signing events in `SignedUploadService`: `AgreementExecutedApplicant` in `ApproveAsync`, `SignedUploadRejectedApplicant` in `RejectAsync` (two-phase, `ActorUserId=reviewer`; rejection reason → non-PII `OutcomeCode` cue if used)
- [X] T031 [P] [US3] Integration: regenerate re-fires `AGREEMENT_GENERATED_APPLICANT` with a distinct `VersionHistoryId`; signing reviewer set equals the group-overlap inbox set; idempotency double-pass — in `tests/FundingPlatform.Tests.Integration/Notifications/`
- [X] T032 [US3] E2E `SigningNotificationsTests` driving the real UI (generate → upload → approve, plus a reject variant); assert captures + CTAs (`/Review/SigningInbox`, FA surface), in `tests/FundingPlatform.Tests.E2E/Notifications/`

**Checkpoint**: US3 independently demoable — signing ceremony is voiced end-to-end.

---

## Phase 6: Polish & Cross-Cutting

**Purpose**: Gates that span all 12 events; final delivery validation.

- [X] T033 [P] Brand-grep gate: confirm all 24 new `Views/Emails/*.cshtml` partials are es-CR, contain no "Capital Semilla"/"Forge"/English-only strings and no inline `<img>` (SC-005) — extend the existing CI grep scope if needed
- [X] T034 [P] Integration: non-prod allowlist fail-closed across a sample of the new events (empty allowlist → zero deliveries, all recorded `BlockedByAllowlist`) (SC-004), in `tests/FundingPlatform.Tests.Integration/Notifications/`
- [X] T035 [P] Verify zero EF migrations and no dacpac change: grep `**/Migrations/**`, dacpac diff clean (SC-006)
- [X] T036 [P] P95 time-to-send check across the E2E run (`NotificationDelivery.SentAt − NotificationOutbox.CreatedAt` < 30 s; no regression of NFR-002 / SC-008)
- [X] T037 Run the FULL E2E suite (`dotnet test tests/FundingPlatform.Tests.E2E`) and confirm green — delivery gate per CLAUDE.md (structural readiness / partial runs do not count)
- [X] T038 STAMP readiness: run `speckit-spex-gates-stamp` (tests + spec-compliance + drift); record FR/SC coverage in `STAMP.md`

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational, blocking)** → **Phases 3/4/5 (user stories)** → **Phase 6 (Polish)**.
- **Foundational T002–T006 block all stories** (payload field, CTA, actor exclusion, DI). T007 is the refactor regression guard.
- **Within each story**: enum → binding → bucket arm → enqueue are sequential (shared files); partials `[P]`; integration `[P]` after impl; E2E last.
- **Stories are independent** after Phase 2 (different service methods), but US1/US2/US3 all edit the 3 shared files (`NotificationEvent.cs`, `NotificationTemplateBindings.cs`, `NotificationRecipientResolver.cs`) — serialize those edits or merge-coordinate if parallelizing.
- **Phase 6** requires all three stories complete (T037 full E2E; T033 needs all 24 partials).

## Parallel Opportunities

- **T011, T018, T027** (partial creation) are `[P]` within their stories — each event's two files are independent.
- **T013, T022, T031** (per-story integration) and **T033–T036** (polish gates) are `[P]`.
- The three user-story phases can run on parallel branches/agents after Phase 2, given the shared-file coordination caveat above.

## Implementation Strategy

- **MVP = US1** (Phase 1 + 2 + 3): closes the reported bug (reviewer notified on applicant response). Independently shippable.
- **Incremental delivery**: add US2 (appeals), then US3 (signing). Each checkpoint is independently testable and demoable.
- **Delivery bar**: T037 (full E2E green, personally executed) + T038 (STAMP) gate completion.

## Task Count

- **Total**: 38 tasks
- Setup: 1 (T001) · Foundational: 6 (T002–T007) · US1: 7 (T008–T014) · US2: 9 (T015–T023) · US3: 9 (T024–T032) · Polish: 6 (T033–T038)
- Per-event work (enum + binding + bucket + 2 partials + enqueue) × 12 events, threaded through the 5 reused integration seams.
