# Increment Plan: US9 — Applicant-initiated delete / withdrawal

**Feature:** 021-feedback-session-may13 (increment on an already-shipped spec)
**Spec scope:** User Story 9, FR-035–FR-041, SC-017/SC-018, OQ-11
**Created:** 2026-05-21
**Status:** Draft

> This plan covers ONLY the US9 increment. The US1–US8 artifacts (`plan.md`, `research.md`,
> `data-model.md`, `contracts/`, `tasks.md`) are shipped and MUST NOT be regenerated or altered.

## Summary

Give applicants a self-service exit for Applications they own: **delete** a `Draft`, **withdraw** a
`Submitted`/`UnderReview`. Both reuse the existing soft-delete (`Application.SoftDelete()` →
`DeletedAt`), so the row leaves every dashboard surface via the established `ExcludeDeleted` filter
(FR-021) with **zero schema change**. Withdrawing an `UnderReview` Application enqueues a new
`APPLICATION_WITHDRAWN_BY_APPLICANT` event to the stage-group reviewer pool — the exact recipient set
`APPLICATION_SUBMITTED_REVIEWER` already resolves. Plain `Submitted` withdrawals and empty pools are
silent.

## Technical Context

- **Language/stack:** C# 13 / .NET 10, ASP.NET MVC, EF Core 10 (unchanged).
- **No new managed dependencies** (NFR-005). No NuGet additions.
- **No schema change** (Constitution IV honored trivially): reuses `Application.DeletedAt`
  (`Application.cs:80`), `Application.SoftDelete()` (`Application.cs:154-159`), and
  `ApplicationQueryFilter.ExcludeDeleted`. The new notification event is a CLR enum value persisted
  into the existing `NotificationOutbox.EventType` **string** column — no dacpac edit.
- **Reuses spec-021 outbox end-to-end:** `INotificationOutboxWriter.EnqueueAsync(...)`
  (`ApplicationService.cs:256-261`), payload shape `NotificationPayload(ApplicationId,
  ApplicantUserId, ApplicantDisplayName, StageGroupIds, OutcomeCode)`
  (`ApplicationService.cs:233-246`), stage-group lookup
  `NotificationOutboxWriter.GetApplicantStageGroupIdsAsync` (`NotificationOutboxWriter.cs:51-62`).
- **Recipient resolution unchanged:** `NotificationRecipientResolver` reviewer bucket
  (`NotificationRecipientResolver.cs:76-121`) gated by `IncludesReviewerBucket`
  (`NotificationRecipientResolver.cs:167-172`).
- **NEEDS CLARIFICATION:** none blocking. OQ-11 (notify on plain `Submitted` too?) is resolved for v1
  = `UnderReview`-only; left as a stakeholder-confirm open question, not an implementation unknown.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. Clean Architecture | PASS | State guard + removal decision in Domain; orchestration in `ApplicationService`; controller stays thin. |
| II. Rich Domain Model | PASS | New `Application` method owns the state machine + the "notify?" decision; controller never inspects `State` to decide behavior. |
| III. E2E (NON-NEGOTIABLE) | PASS | Real-journey Playwright tests: delete a draft (no mail), withdraw an `UnderReview` app (mail captured), gated-state + cross-user rejection. |
| IV. Schema-First DB | PASS | Zero schema delta. Enum value is code; stored via existing string column. |
| V. Spec-Driven Development | PASS | Derives from US9 / FR-035–041 / SC-017-018. |
| VI. Simplicity | PASS | One domain method, one service method, one controller endpoint, one event + template pair, two view affordances. No new entity, no new state. |

No violations; no Complexity Tracking entries required.

## Design

### Domain (`FundingPlatform.Domain/Entities/Application.cs`)

Add one method that owns the lifecycle rule (Constitution II):

```
public ApplicantRemovalOutcome RemoveByApplicant()
```

- `Draft` → `SoftDelete()`; return `ApplicantRemovalOutcome.DraftDeleted` (NotifyReviewers = false).
- `Submitted` → capture state; `SoftDelete()`; return `Withdrawn` with NotifyReviewers = **false**.
- `UnderReview` → capture state; `SoftDelete()`; return `Withdrawn` with NotifyReviewers = **true**.
- `Resolved` / `AppealOpen` / `ResponseFinalized` / `AgreementExecuted` → throw
  `InvalidOperationException` (FR-037 server-side guard).
- Idempotent on already-deleted (FR / "repeat withdraw" edge case): if `IsDeleted`, return a no-op
  outcome (NotifyReviewers = false) so a double-submit never re-enqueues mail.

`ApplicantRemovalOutcome` = small value type: `{ enum Kind (DraftDeleted | Withdrawn | NoOp);
bool NotifyReviewers; ApplicationState PriorState }`.

### Application service (`FundingPlatform.Application/Services/ApplicationService.cs`)

Add `RemoveByApplicantAsync(int applicationId, string applicantUserId, CancellationToken ct)`:

1. Load Application incl. `Applicant` (use the by-id read path; do NOT pre-filter deleted so a stale
   double-submit resolves to the idempotent no-op).
2. **Ownership (FR-041):** if `application.Applicant?.UserId != applicantUserId` → return a
   `NotFound`-style result (no info leak; do not mutate).
3. `var outcome = application.RemoveByApplicant();` (throws → surfaced as bad-state result).
4. If `outcome.NotifyReviewers`:
   - `stageGroupIds = await _outboxWriter.GetApplicantStageGroupIdsAsync(application.Id, ct)`
   - build `NotificationPayload` exactly as the submit path does (ApplicantUserId,
     ApplicantDisplayName, StageGroupIds, OutcomeCode: null)
   - create a `VersionHistory` row (action `"Withdrawn"`, note "Retirada por el solicitante") →
     gives a fresh `VersionHistoryId` so the idempotency key
     `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` is **distinct** from the
     submission's reviewer mail (FR-040).
   - `await _outboxWriter.EnqueueAsync(NotificationEvent.WithdrawnByApplicant, application.Id,
     vhRow.Id, payload, ct)`
5. `UpdateAsync` + `SaveChangesAsync`. Return the outcome for controller messaging.

> Empty stage-group pool ⇒ the resolver yields zero reviewer recipients ⇒ no delivery rows ⇒ natural
> no-op (no special-casing needed).

### Notification wiring (no schema change)

1. `FundingPlatform.Domain/Notifications/NotificationEvent.cs`:
   add `WithdrawnByApplicant = 7` and `ToStorageString` case
   `=> "APPLICATION_WITHDRAWN_BY_APPLICANT"`.
2. `NotificationRecipientResolver.IncludesReviewerBucket` (`:167-172`): add
   `NotificationEvent.WithdrawnByApplicant => true`.
   **VERIFY (impl task):** the sibling applicant-bucket inclusion does NOT include this event — the
   applicant must not receive a "you withdrew" email. If an `IncludesApplicantBucket` exists, ensure
   it returns false for `WithdrawnByApplicant`.
3. `NotificationTemplateBindings.cs` (`:36-90`): add a `Binding` for `WithdrawnByApplicant`:
   - SubjectTemplate: `"Solicitud retirada por el solicitante: {ApplicantName}"`
   - HtmlViewName: `ApplicationWithdrawnByApplicant`, TextViewName:
     `ApplicationWithdrawnByApplicant.text`, TemplateVariantKey:
     `reviewer-application-withdrawn`.
4. New es-CR templates `Views/Emails/ApplicationWithdrawnByApplicant.cshtml` +
   `.text.cshtml`, modeled on `ApplicationSubmittedReviewer*`.
   **Copy caveat:** the Application is soft-deleted, so the CTA MUST NOT deep-link to
   `/Review/{id}` (would 403/404). Link to the reviewer queue (`/Review`) and state the application
   was withdrawn and removed from the worklist. Brand-grep rule (NFR-003 / spec 019): no
   `financiamiento`, text-only wordmark.

### Web — controller (`FundingPlatform.Web/Controllers/ApplicationController.cs`)

One endpoint (Constitution VI), `[Authorize(Roles="Applicant")]` (matches the controller),
`[HttpPost]`, `[ValidateAntiForgeryToken]`:

```
[HttpPost] public async Task<IActionResult> Remove(int id)
```

- Resolve current applicant user id; call `_applicationService.RemoveByApplicantAsync(id, userId, ct)`.
- Map result: `NotFound` → `NotFound()`; bad-state (`InvalidOperationException`) → redirect to
  `Index` with a danger TempData (`"La solicitud ya no puede retirarse."`); success → redirect to
  `Index` with success TempData (`"Borrador eliminado."` for DraftDeleted,
  `"Solicitud retirada."` for Withdrawn/NoOp).
- One endpoint serves both delete + withdraw; the domain decides by state. The UI chooses the label
  and confirm copy.

### Web — views (state- and ownership-gated affordance, FR-038/FR-039)

- `Views/Application/Index.cshtml` (`:86-97` row actions): for rows the applicant owns whose state ∈
  `{Draft, Submitted, UnderReview}`, render a destructive button — label *"Eliminar borrador"*
  (Draft) or *"Retirar solicitud"* (Submitted/UnderReview) — that opens a Tabler confirm modal
  (reuse spec-008 `_ConfirmDialog` pattern) wrapping a POST form to `Remove`. Hidden for all other
  states (FR-037).
- `Views/Application/Details.cshtml` (`:19-24` header actions): same affordance.
- Withdraw confirm copy warns: *"Si tu solicitud ya está en revisión, se notificará a las personas
  revisoras."* (FR-039).
- Add `data-testid`s for E2E: `application-row-remove`, `application-details-remove`,
  `remove-confirm`.

## Test Plan (E2E NON-NEGOTIABLE — Constitution III, NFR-004)

Mirror `tests/.../Notifications/ApplicationSubmittedNotificationsTests.cs` and reuse
`AuthenticatedTestBase` (`RegisterUserAsync`, `LoginAsync`, `SubmitDraftViaReviewAsync`,
`CompleteImpactStepAsync`) + `MailCaptureClient` (`DrainAsync`, `WaitForAsync`,
`ListAsync(recipientFilter)`).

New file `tests/FundingPlatform.Tests.E2E/.../ApplicantRemovalTests.cs`:

1. **Delete draft (SC-017a):** applicant creates a draft → clicks *Eliminar borrador* → confirms →
   draft absent from `/Application`; `MailCapture.ListAsync()` shows **zero** new messages.
2. **Withdraw under-review fires reviewer mail (SC-017b):** applicant + reviewer registered in a
   shared group; applicant submits; **reviewer opens the application** (`/Review/{id}` →
   `Application.StartReview()` → `UnderReview`); applicant withdraws → app absent from applicant
   dashboard AND from the reviewer queue; `MailCapture.WaitForAsync(minCount:1, filter: subject
   StartsWith "Solicitud retirada")` captures one message addressed to the reviewer; body links to
   `/Review` (not `/Review/{id}`).
3. **Withdraw plain Submitted is silent (SC-017c/SC-018):** submit, do NOT open as reviewer,
   withdraw → app leaves both surfaces; zero reviewer mail captured.
4. **Gated states (SC-018):** for an Application in `Resolved` (drive via the review→resolve path),
   assert no remove affordance in rendered HTML AND a direct `POST /Application/Remove/{id}` returns
   non-success without deleting (state unchanged).
5. **Cross-user ownership (FR-041):** applicant B `POST`s `Remove` for applicant A's app → rejected;
   A's app still present.
6. **Empty pool no-op (SC-018):** withdraw an `UnderReview` app whose stage group has no
   Reviewer-role member → soft-deleted, zero mail. (May be folded into an integration test if E2E
   group setup is heavy.)

Unit tests: `Application.RemoveByApplicant()` truth table across all 7 states + already-deleted
idempotency. Integration test: `ApplicationService.RemoveByApplicantAsync` enqueues exactly one
outbox row for `UnderReview`, none for `Submitted`/`Draft`, with a `VersionHistoryId` distinct from
the submission row.

## Ordered Tasks

1. Domain: `ApplicantRemovalOutcome` + `Application.RemoveByApplicant()` (+ unit tests, TDD).
2. Notifications: enum value + `ToStorageString` + `IncludesReviewerBucket` (+ verify applicant
   bucket exclusion) + `NotificationTemplateBindings` row.
3. Templates: `ApplicationWithdrawnByApplicant.cshtml` + `.text.cshtml` (es-CR, queue-level CTA,
   brand-grep clean).
4. Application service: `RemoveByApplicantAsync` (ownership, domain call, conditional enqueue +
   `VersionHistory`) (+ integration test against real DB).
5. Controller: `[HttpPost] Remove` + result mapping + anti-forgery.
6. Views: `Index.cshtml` + `Details.cshtml` gated affordance + confirm modal + `data-testid`s.
7. E2E: `ApplicantRemovalTests` (scenarios 1–6).
8. Full E2E suite green (delivery bar) → STAMP.

## Open Questions (carried)

- **OQ-11:** confirm with stakeholders that `UnderReview`-only notification is right (vs. also plain
  `Submitted`, vs. never). v1 ships `UnderReview`-only.
- Whether withdrawal should leave an applicant-visible "Retirada" trace vs. silently vanishing
  (current: vanishes like any soft-delete). Parked.
