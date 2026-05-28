# Phase 1 Data Model: Post-Resolution Email Notifications

**Date**: 2026-05-27

> **No database schema change.** No new tables, no column changes, no `.sql` edits, no EF migration. This document describes the in-code data-shape additions only (enum members + one value-object field). The `dbo.NotificationOutbox` / `dbo.NotificationDelivery` tables and the idempotency unique index are reused exactly as shipped by spec 021.

## 1. `NotificationEvent` enum (extended)

`src/FundingPlatform.Domain/Notifications/NotificationEvent.cs` — append 12 members (next ordinals `8`–`19`) and matching `ToStorageString` / `FromStorageString` arms. Stored as `VARCHAR(64)` (no schema impact).

| Member (ordinal) | Storage string |
|---|---|
| `ResponseSubmittedReviewer` (8) | `RESPONSE_SUBMITTED_REVIEWER` |
| `AppealOpenedReviewer` (9) | `APPEAL_OPENED_REVIEWER` |
| `AppealMessageReviewer` (10) | `APPEAL_MESSAGE_REVIEWER` |
| `AppealMessageApplicant` (11) | `APPEAL_MESSAGE_APPLICANT` |
| `AppealResolvedApplicant` (12) | `APPEAL_RESOLVED_APPLICANT` |
| `AppealReopenedReviewer` (13) | `APPEAL_REOPENED_REVIEWER` |
| `AgreementGeneratedApplicant` (14) | `AGREEMENT_GENERATED_APPLICANT` |
| `SignedUploadSubmittedReviewer` (15) | `SIGNED_UPLOAD_SUBMITTED_REVIEWER` |
| `SignedUploadReplacedReviewer` (16) | `SIGNED_UPLOAD_REPLACED_REVIEWER` |
| `SignedUploadWithdrawnReviewer` (17) | `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` |
| `AgreementExecutedApplicant` (18) | `AGREEMENT_EXECUTED_APPLICANT` |
| `SignedUploadRejectedApplicant` (19) | `SIGNED_UPLOAD_REJECTED_APPLICANT` |

Ordinals are not persisted (string storage), so the exact integer values are cosmetic; append sequentially after `WithdrawnByApplicant = 7`.

## 2. `NotificationPayload` (value object — one new field)

`src/FundingPlatform.Application/Notifications/NotificationPayload.cs`. Serialized to `NotificationOutbox.PayloadJson` (nvarchar/max — no column change; JSON shape is additive and tolerated by the existing deserializer for old rows where the field is absent).

Existing fields (reused): `ApplicationId`, `ApplicantUserId`, `ApplicantDisplayName`, `StageGroupIds`, `OutcomeCode`.

**New field:**
- `ActorUserId` (string, nullable) — the user who triggered the event. Used by the resolver for actor exclusion (R-003 / FR-013a). Null on legacy rows; the resolver treats null as "no actor to exclude" (current behavior).

**Reused with new values:**
- `OutcomeCode` — for `AppealResolvedApplicant` carries the appeal resolution: `"AppealUpheld"` | `"AppealReopenedToDraft"` | `"AppealReopenedToReview"` (R-004). For `SignedUploadRejectedApplicant` MAY carry a short non-PII reason code; the body renders a cue, never verbatim reviewer commentary (NFR-003).

## 3. `Binding` record (template binding — one new field)

`src/FundingPlatform.Application/Notifications/Templates/NotificationTemplateBindings.cs`. Add a `CtaRouteTemplate` member to the `Binding` record (R-001) and 12 dictionary entries (one per new event). Existing 7 entries get `CtaRouteTemplate` set to their current bucket route to preserve behavior.

This is a code-only data shape; not persisted.

## 4. `VersionHistory` (existing entity — new Action value only)

`src/FundingPlatform.Domain/Entities/VersionHistory.cs`. No schema change (`Action VARCHAR(100)` already accommodates it). New `Action` string value `"AgreementGenerated"` written by `FundingAgreementService.PersistGenerationAsync` via `Application.AddVersionHistory` (FR-010 / R-007). All other 11 triggers already write their own `Action` values.

## 5. Reused entities (no change)

- **NotificationOutbox** — row per event; columns, `RowVersion` claim, status lifecycle unchanged.
- **NotificationDelivery** — row per (outbox, recipient); the unique index `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` is the idempotency guarantee for all 12 new events with no change.
- **NotificationRecipient** — resolver output `(UserId, Email, DisplayName, Bucket, TemplateVariantKey)`; unchanged.

## State / cardinality notes

- **One outbox row per triggering transaction**, except `ResolveAppealAsGrantReopenToReview` which writes **two** (dual-fire, FR-006).
- **Appeal messages**: one outbox row per `PostAppealMessage`; successive messages anchor on distinct `VersionHistoryId` → no idempotency collapse (EC-002).
- **Convenio (re)generation**: one outbox row + one `AgreementGenerated` `VersionHistory` row per generation; regenerate repeats with a new id (EC-003).
