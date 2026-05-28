# Phase 0 Research: Post-Resolution Email Notifications

**Date**: 2026-05-27
All decisions verified against the spec-021 implementation in the current codebase (2026-05-27).

---

## R-001 — Event-aware CTA resolution (the one cross-cutting gap)

**Decision**: Extend the per-event `Binding` record in `NotificationTemplateBindings` with a new `CtaRouteTemplate` field (e.g. `"/Review/{id}"`, `"/Review/SigningInbox"`, `"/ApplicantResponse/Appeal/{id}"`). `RazorEmailRenderer` composes the CTA as `Notifications:BaseUrl` + the event's `CtaRouteTemplate` with `{id}` substituted, instead of the current bucket-only branch.

**Rationale**: Today CTA URL composition in `RazorEmailRenderer` is bucket-based and hard-wired to exactly two routes (`Applicant → /Application/Details/{id}`, `Reviewer/Admin → /Review/{id}`). Spec 028 needs five distinct targets (`/Review/{id}`, `/Review/SigningInbox`, `/ApplicantResponse/Index/{id}`, `/ApplicantResponse/Appeal/{id}`, `/Applications/{id}/FundingAgreement/`). Putting the route template next to the subject template (which already lives per-event in `Bindings`) keeps event→presentation data in one place and is the smallest change that generalizes cleanly. The existing 7 events keep their current behavior by setting `CtaRouteTemplate` to their current bucket route.

**Alternatives considered**:
- *Keep bucket-based, add more buckets* — rejected: buckets describe recipients, not destinations; two events to the same bucket (`/Review/{id}` vs `/Review/SigningInbox`) need different routes.
- *Per-recipient route override in the resolver* — rejected: CTA is a function of the event, not the recipient; belongs with the template binding.

**No new MVC routes** are introduced (FR-018); all targets already exist (spec 027 / appeal flow). Access control stays on the existing `[Authorize]` attributes (FR-019).

---

## R-002 — Appeal-message direction (which of the two message events fires)

**Decision**: In `ApplicantResponseService.PostMessageAsync`, choose the event by comparing the message author to the application's applicant: author `== Application.Applicant.UserId` → `APPEAL_MESSAGE_REVIEWER` (notify reviewers + admins); otherwise (a reviewer authored) → `APPEAL_MESSAGE_APPLICANT` (notify applicant + admins). Exactly one event per posted message.

**Rationale**: The author user-id is already available in the method (it appends `Action="PostAppealMessage"` with that user-id). The applicant user-id is on the eager-loaded aggregate. No role lookup needed for the common case; the comparison is unambiguous because only the applicant or a reviewer can post.

**Alternatives considered**: role-based check via `UserManager` — rejected as an unnecessary DB round-trip; identity comparison suffices.

---

## R-003 — Actor exclusion (FR-013a / EC-011)

**Decision**: Add an `ActorUserId` field to `NotificationPayload`, set at enqueue time to the user who triggered the event. In `NotificationRecipientResolver.ResolveAsync`, filter the final resolved recipient list to drop any recipient whose `UserId == payload.ActorUserId` (after bucket resolution, before dedup output).

**Rationale**: The resolver already excludes the submitting applicant from the reviewer query (`m.UserId != applicantUserId`). Generalizing to "exclude the actor" closes the case where a reviewer authors an appeal message (or resolves an appeal) and also qualifies as a participating admin — they must not receive a copy of their own action. Carrying the actor in the payload keeps the resolver's query inputs self-contained (consistent with how `ApplicantUserId`/`StageGroupIds` are already passed).

**Alternatives considered**:
- *Exclude only in the admin bucket* — rejected: incomplete; an actor could appear via another bucket.
- *Snapshot nothing, resolve actor from VersionHistory* — rejected: the actor is known at enqueue time; passing it is cheaper and explicit.

**Backward compatibility**: existing 7 events set `ActorUserId` to the same value they already use (applicant on submit/return; reviewer on approve/reject). Where the actor equals an intended recipient under current behavior (none today, since current events never send to the actor), behavior is unchanged.

---

## R-004 — Appeal-resolution body variant (one event, three bodies)

**Decision**: `APPEAL_RESOLVED_APPLICANT` carries the resolution outcome in the existing `NotificationPayload.OutcomeCode` field (values `"AppealUpheld"`, `"AppealReopenedToDraft"`, `"AppealReopenedToReview"`). The single HTML/text partial pair switches on `Model.Payload.OutcomeCode` to render the correct copy. Subject is identical across outcomes (FR-017 / EC-005).

**Rationale**: `OutcomeCode` already exists on the payload (used by `APPLICATION_APPROVED`/`REJECTED`). Reusing it avoids a new field and keeps one binding/one partial pair per event (per the one-partial-per-event convention).

**Alternatives considered**: three separate events — rejected: same recipient, same subject, same CTA; only body copy differs. One event with a switch is simpler and matches the spec's intent.

---

## R-005 — Two-phase save and the VersionHistoryId anchor

**Decision**: Each touched service method follows the canonical spec-021 two-phase pattern already used in `ReviewService.SendBackAsync` / `ApplicationService.SubmitApplicationAsync`: (1) mutate the aggregate + `AddVersionHistory(...)` and `SaveChangesAsync()` so the `VersionHistory` row gets its identity; (2) build the `NotificationPayload`, call `EnqueueAsync(event, applicationId, versionHistoryId, payload, ct)`, and `SaveChangesAsync()` again to commit the outbox row(s). Implementation mirrors the exact ordering of the reference methods.

**Rationale**: The idempotency anchor `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` requires a real `VersionHistoryId`. The reference services already establish the pattern; mirroring them guarantees the new call sites land in the same transaction shape and pass the existing idempotency tests.

**Note on the dual-fire (FR-006)**: `ResolveAppealAsGrantReopenToReview` enqueues **two** outbox rows (`APPEAL_RESOLVED_APPLICANT` + `APPEAL_REOPENED_REVIEWER`) in phase 2, sharing `(ApplicationId, VersionHistoryId)`, differing on `EventType` — the unique index admits both.

---

## R-006 — Signing-stage reviewer recipients

**Decision**: Reuse the resolver's existing group-overlap reviewer query unchanged. The reviewer-bucket signing events (`SIGNED_UPLOAD_*_REVIEWER`) resolve to reviewers whose group membership overlaps the applicant's groups (`payload.StageGroupIds` from `GetApplicantStageGroupIdsAsync`), which is exactly the predicate the signing inbox (`SignedUploadRepository.GetPendingInboxAsync`) uses (spec 016 group-overlap).

**Rationale**: The signing inbox and the notification reviewer query both scope by applicant-group ∩ reviewer-group; they agree by construction. No new query is needed — only adding the events to `IncludesReviewerBucket`.

**Alternatives considered**: a dedicated signing-reviewer query — rejected as redundant; the group-overlap set is identical.

---

## R-007 — `AgreementGenerated` audit row placement

**Decision**: In `FundingAgreementService.PersistGenerationAsync`, after the domain generates/regenerates the agreement, call `Application.AddVersionHistory(actorUserId, "AgreementGenerated", details)` (domain method, §II) and `SaveChangesAsync()` (phase 1), then enqueue `AGREEMENT_GENERATED_APPLICANT` against that row's id and `SaveChangesAsync()` (phase 2).

**Rationale**: This is the only trigger lacking a `VersionHistory` row today. Adding one makes the idempotency anchor uniform across all 12 events and, as a side benefit, makes convenio generation auditable (FR-010). Regeneration produces a fresh row → a fresh `VersionHistoryId` → a fresh email (no dedup collapse; EC-003).

---

## R-008 — Storage type confirms zero migration

**Decision**: No schema change. `dbo.NotificationOutbox.EventType` is `VARCHAR(64)` (dacpac + `NotificationOutboxConfiguration`); the enum persists via `ToStorageString()`. Adding 12 values appends mappings only.

**Rationale**: String storage means new enum values are non-breaking and operator-readable in raw SQL. Confirmed against `src/FundingPlatform.Database/Tables/dbo.NotificationOutbox.sql` and `NotificationOutboxConfiguration.cs`. Satisfies Constitution §IV and SC-006.

---

## R-009 — Template binding + partial naming

**Decision**: For each event add one `Bindings` entry (`SubjectTemplate`, `HtmlViewName`, `TextViewName`, `TemplateVariantKey`, new `CtaRouteTemplate`) and two partials under `Views/Emails/`: `{HtmlViewName}.cshtml` + `{HtmlViewName}.text.cshtml`, each rendered under `_EmailLayout.cshtml` against `EmailRenderModel`. Subjects use `{ApplicantName}` / `{ApplicationId}` tokens interpolated by `RenderSubject` (78-char RFC-5322 truncation already handled).

**Rationale**: Mirrors the shipped 7-event convention exactly; keeps the brand-grep gate (source-`.cshtml` scan) applicable to the 24 new files unchanged.

**View-name proposal** (kebab/Pascal mirrors existing `ApplicationSubmittedReviewer` style):

| Event | HtmlViewName |
|---|---|
| `RESPONSE_SUBMITTED_REVIEWER` | `ResponseSubmittedReviewer` |
| `APPEAL_OPENED_REVIEWER` | `AppealOpenedReviewer` |
| `APPEAL_MESSAGE_REVIEWER` | `AppealMessageReviewer` |
| `APPEAL_MESSAGE_APPLICANT` | `AppealMessageApplicant` |
| `APPEAL_RESOLVED_APPLICANT` | `AppealResolvedApplicant` |
| `APPEAL_REOPENED_REVIEWER` | `AppealReopenedReviewer` |
| `AGREEMENT_GENERATED_APPLICANT` | `AgreementGeneratedApplicant` |
| `SIGNED_UPLOAD_SUBMITTED_REVIEWER` | `SignedUploadSubmittedReviewer` |
| `SIGNED_UPLOAD_REPLACED_REVIEWER` | `SignedUploadReplacedReviewer` |
| `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` | `SignedUploadWithdrawnReviewer` |
| `AGREEMENT_EXECUTED_APPLICANT` | `AgreementExecutedApplicant` |
| `SIGNED_UPLOAD_REJECTED_APPLICANT` | `SignedUploadRejectedApplicant` |

---

## Resolved unknowns summary

All Technical-Context unknowns resolved; no remaining NEEDS CLARIFICATION. The two planning-pin items from REVIEW-SPEC (exact FA sub-route per applicant CTA; message-snippet vs cue) are resolved in `contracts/notification-events.md` (R-001 route map; cue-only bodies per NFR-003).
