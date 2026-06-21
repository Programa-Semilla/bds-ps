# Contract: Notification Events (additions)

**Feature**: 041-email-brand-lift

## New outbox event

| Field | Value |
|---|---|
| Enum member | `NotificationEvent.ApplicationUnderReviewApplicant` |
| Storage string | `APPLICATION_UNDER_REVIEW_APPLICANT` (stable; never rename) |
| Subject | `Tu solicitud está en revisión — Solicitud #{ApplicationId}` |
| HTML view | `ApplicationUnderReviewApplicant.cshtml` |
| Text view | `ApplicationUnderReviewApplicant.text.cshtml` |
| CTA route | `/Application/Details/{id}` |
| Recipients | Applicant (+admins by default; confirm applicant-only in tasks) |
| Actor excluded | reviewer who opened the application (via `ActorUserId`) |
| Trigger | `Submitted → UnderReview` transition (`Application.StartReview()` in `ReviewService.GetApplicationForReviewAsync`) — enqueued only on actual state change |
| Idempotency | `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`; `VersionHistoryId` = a new `VersionHistory(reviewerUserId, "StartReview", …)` row |

### Required touchpoints (per the spec-028 worked pattern)
1. `NotificationEvent.cs` — enum + `ToStorageString` + `FromStorageString`.
2. `NotificationTemplateBindings.cs` — binding row (totality unit test enforces this).
3. `NotificationRecipientResolver.cs` — applicant/reviewer/admin bucket switches.
4. `ReviewService.cs` — on `StartReview()` actually transitioning: add `VersionHistory("StartReview")`, build `NotificationPayload(ActorUserId = reviewerUserId)`, `EnqueueAsync(...)` before `SaveChangesAsync`.
5. `Views/Emails/ApplicationUnderReviewApplicant.cshtml` + `.text.cshtml` (compose design-system partials; ALIA copy from reference #4).
6. Tests: integration recipient-matrix + idempotency; E2E mail-capture.

### Acceptance (from spec US2)
- Transition fires exactly one applicant email; reviewer re-opening the page does not duplicate (state-change guard + dedup index).
- Non-prod allowlist drops non-listed recipients and records the drop, like every event.

## FR-013 — "Nueva empresa para revisión" (notifier seam, NOT an outbox event)

Rationale: no `ApplicationId` ⇒ cannot use the application-keyed outbox. Mirror `IProviderCreatedNotifier`/`ProviderCreatedNotifier` (spec 038).

| Field | Value |
|---|---|
| Seam | `ICompanyForReviewNotifier` (Application) + impl (Infrastructure) — mirrors provider notifier |
| Template | `Views/Emails/Suppliers/CompanyForReviewAuditor.cshtml` + `.text.cshtml` (branded; "Detalle" card with company name / identificación / fecha) |
| Live trigger | **DEFERRED (OQ-1)** — no enqueue/call site wired; render-tested only |
| Recipient | **DEFERRED (OQ-1)** — reviewer pool vs auditor, to be confirmed |

### Acceptance (from spec US4)
- Template renders in the brand shell with a populated detail card (render test).
- No live notification is emitted until OQ-1 is resolved.
