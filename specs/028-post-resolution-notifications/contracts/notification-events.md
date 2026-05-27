# Contract: Post-Resolution Notification Events

**Date**: 2026-05-27

The "interface" this feature exposes is its **event contract**: for each of the 12 events, the exact trigger, recipients, subject, CTA, and template binding. This table is the source of truth that the implementation, the `NotificationTemplateBindings` dictionary, the resolver bucket predicates, and the E2E assertions must all agree with.

## Event contract table

| # | Event (enum) | Storage string | Trigger (Application-layer call site) | Recipients | Subject (es-CR) | CtaRouteTemplate | HtmlViewName |
|---|---|---|---|---|---|---|---|
| 1 | `ResponseSubmittedReviewer` | `RESPONSE_SUBMITTED_REVIEWER` | `ApplicantResponseService.SubmitResponseAsync` | reviewers(group) + admins | `El solicitante respondió la resolución — Solicitud #{ApplicationId}` | `/Review/{id}` | `ResponseSubmittedReviewer` |
| 2 | `AppealOpenedReviewer` | `APPEAL_OPENED_REVIEWER` | `ApplicantResponseService.OpenAppealAsync` | reviewers(group) + admins | `Nueva apelación abierta — Solicitud #{ApplicationId}` | `/ApplicantResponse/Appeal/{id}` | `AppealOpenedReviewer` |
| 3 | `AppealMessageReviewer` | `APPEAL_MESSAGE_REVIEWER` | `ApplicantResponseService.PostMessageAsync` (author = applicant) | reviewers(group) + admins | `Nuevo mensaje en la apelación — Solicitud #{ApplicationId}` | `/ApplicantResponse/Appeal/{id}` | `AppealMessageReviewer` |
| 4 | `AppealMessageApplicant` | `APPEAL_MESSAGE_APPLICANT` | `ApplicantResponseService.PostMessageAsync` (author = reviewer) | applicant + admins | `Nuevo mensaje del revisor en tu apelación — Solicitud #{ApplicationId}` | `/ApplicantResponse/Index/{id}` | `AppealMessageApplicant` |
| 5 | `AppealResolvedApplicant` | `APPEAL_RESOLVED_APPLICANT` | `ApplicantResponseService.ResolveAppealAsync` (all 3 outcomes) | applicant + admins | `Resolución de tu apelación — Solicitud #{ApplicationId}` | `/ApplicantResponse/Index/{id}` | `AppealResolvedApplicant` |
| 6 | `AppealReopenedReviewer` | `APPEAL_REOPENED_REVIEWER` | `ApplicantResponseService.ResolveAppealAsync` (GrantReopenToReview only) | reviewers(group) + admins | `Apelación concedida: solicitud reabierta para revisión — Solicitud #{ApplicationId}` | `/Review/{id}` | `AppealReopenedReviewer` |
| 7 | `AgreementGeneratedApplicant` | `AGREEMENT_GENERATED_APPLICANT` | `FundingAgreementService.PersistGenerationAsync` (generate + regenerate) | applicant + admins | `Tu convenio está listo para firmar — Solicitud #{ApplicationId}` | `/Applications/{id}/FundingAgreement/` | `AgreementGeneratedApplicant` |
| 8 | `SignedUploadSubmittedReviewer` | `SIGNED_UPLOAD_SUBMITTED_REVIEWER` | `SignedUploadService.UploadAsync` | reviewers(group) + admins | `Convenio firmado recibido para revisión — Solicitud #{ApplicationId}` | `/Review/SigningInbox` | `SignedUploadSubmittedReviewer` |
| 9 | `SignedUploadReplacedReviewer` | `SIGNED_UPLOAD_REPLACED_REVIEWER` | `SignedUploadService.ReplaceAsync` | reviewers(group) + admins | `Convenio firmado reemplazado — Solicitud #{ApplicationId}` | `/Review/SigningInbox` | `SignedUploadReplacedReviewer` |
| 10 | `SignedUploadWithdrawnReviewer` | `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` | `SignedUploadService.WithdrawAsync` | reviewers(group) + admins | `Convenio firmado retirado — Solicitud #{ApplicationId}` | `/Review/SigningInbox` | `SignedUploadWithdrawnReviewer` |
| 11 | `AgreementExecutedApplicant` | `AGREEMENT_EXECUTED_APPLICANT` | `SignedUploadService.ApproveAsync` | applicant + admins | `Tu convenio fue ejecutado — Solicitud #{ApplicationId}` | `/Applications/{id}/FundingAgreement/` | `AgreementExecutedApplicant` |
| 12 | `SignedUploadRejectedApplicant` | `SIGNED_UPLOAD_REJECTED_APPLICANT` | `SignedUploadService.RejectAsync` | applicant + admins | `Tu convenio firmado requiere cambios — Solicitud #{ApplicationId}` | `/Applications/{id}/FundingAgreement/` | `SignedUploadRejectedApplicant` |

## Recipient-bucket predicate contract

Resolver switch arms to add (`NotificationRecipientResolver`):

- **`IncludesReviewerBucket` → true**: events 1, 2, 3, 6, 8, 9, 10.
- **`IncludesApplicantBucket` → true**: events 4, 5, 7, 11, 12.
- **`IncludesAdminBucket` → true (default)**: all 12 (no event sets it false; only spec-021 `ApplicationSubmittedApplicant` is false).

Bucket priority on collision unchanged: `applicant > reviewer > admin`. One email per `(UserId, Event)`. **Actor exclusion**: the `payload.ActorUserId` is removed from the final recipient set for every event (FR-013a).

## Per-event payload contract

| Event | `ActorUserId` | `StageGroupIds` | `OutcomeCode` |
|---|---|---|---|
| 1, 2, 3, 8, 9, 10 | the applicant (the actor) | applicant's groups (reviewer resolution) | null |
| 4, 6 | the reviewer (the actor) | applicant's groups (reviewer resolution for event 6) | null |
| 5 | the reviewer (the actor) | — | `AppealUpheld` \| `AppealReopenedToDraft` \| `AppealReopenedToReview` |
| 7 | the reviewer (the actor) | — | null |
| 11 | the reviewer (the actor) | — | null |
| 12 | the reviewer (the actor) | — | optional non-PII reason code (body renders a cue only) |

## Dual-fire contract (event 5 + 6)

`ResolveAppealAsync` with resolution `GrantReopenToReview` enqueues **both** event 5 (`AppealResolvedApplicant`, applicant) and event 6 (`AppealReopenedReviewer`, reviewers) in the same phase-2 save. Same `(ApplicationId, VersionHistoryId)`, distinct `EventType` → the idempotency unique index admits both. For `Uphold` and `GrantReopenToDraft`, only event 5 fires.

## Template-render contract

Every partial renders under `_EmailLayout.cshtml` against `EmailRenderModel(EventType, Recipient, Payload, Subject, CtaUrl, SenderName, SenderEmail)`:
- HTML body: `Views/Emails/{HtmlViewName}.cshtml`; text fallback: `Views/Emails/{HtmlViewName}.text.cshtml`.
- es-CR copy only; no inline `<img>`; brand-grep clean (no "Capital Semilla" / "Forge" / English-only strings).
- `AppealResolvedApplicant` switches body copy on `Model.Payload.OutcomeCode` (3 variants).
- `SignedUploadRejectedApplicant` body conveys "changes required" + CTA; never embeds verbatim reviewer commentary (NFR-003).
- CTA href = `Notifications:BaseUrl` + `CtaRouteTemplate` with `{id}` → `ApplicationId` (routes with no `{id}`, e.g. `/Review/SigningInbox`, are used verbatim).

## Idempotency contract

All 12 events use `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` unchanged. A second worker pass over the same outbox row produces no second `NotificationDelivery` row and no second provider call (SC-003). Successive appeal messages and the dual-fire are distinguished by `VersionHistoryId` and `EventType` respectively.
