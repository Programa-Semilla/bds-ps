# Feature Specification: Post-Resolution Email Notifications

**Feature Branch**: `028-post-resolution-notifications`
**Created**: 2026-05-27
**Status**: Draft
**Input**: User description: Close the email-notification gap that opens once an application reaches `Resolved`. Today the FundingPlatform sends transactional email for the submission→review→decision arc (spec 021), but goes completely silent afterward: when the applicant accepts or rejects the reviewer's resolution, when either party works an appeal thread, and across the entire funding-agreement (convenio) signing ceremony, **no email is ever sent**. The bug that surfaced this: a reviewer was never notified when an applicant accepted the reviewer's response, even though "every interaction must be notified." This spec is an increment to the shipped spec 021 (`021-email-notifications`): it adds **twelve** new `NotificationEvent` values covering every remaining applicant↔reviewer interaction in the post-`Resolved` flow, wired through the **existing** spec-021 transactional-outbox + `EmailDispatchWorker` + provider-abstraction + allowlist pipeline with **no schema change** (no new tables, no dacpac change, no EF migration). The design principle is "notify the counterparty of each action" (no self-confirmation emails — matching the existing `RETURNED_TO_APPLICANT` / `APPLICATION_APPROVED` / `APPLICATION_REJECTED` events which have no self-confirm; only the original `Submit` carries an applicant confirmation). One Razor partial pair (HTML + plain-text) per event = 24 new `.cshtml` files under `Views/Emails/`, mirroring spec-021 FR-024's one-partial-per-event convention; all es-CR Spanish; all must pass the spec-019 brand-grep gate.

## Clarifications

### Session 2026-05-27

- Q: Appeal threads are back-and-forth messages — notify on every individual message or only on appeal open/resolve? → A: Notify on **every** message; each `PostAppealMessage` emails the opposite party (chat-like cadence is accepted). Distinct `VersionHistoryId` per message means idempotency never collapses successive messages.
- Q: Which signed-upload actions notify the reviewer — upload only, upload+replace, or all three including withdraw? → A: **All three** (upload + replace + withdraw). A withdraw notifies the reviewer that there is nothing to review now.
- Q: Add applicant self-confirmation emails alongside the counterparty alerts? → A: **No self-confirmations.** Only the counterparty of each action is notified, consistent with the existing spec-021 events.
- Q: One Razor partial per event, or shared parametric partials? → A: **One partial pair per event** (24 files), consistent with spec-021 FR-024 and the brand-grep gate.
- Q: Fold into spec 021 or create a new spec dir? → A: **New spec 028** (own directory), cross-referencing spec 021 as a dependency; shipped spec 021 stays frozen.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Applicant response reaches the reviewer (Priority: P1)

An application has been resolved by the reviewer. The applicant opens their response screen and submits their per-item accept/reject decisions on the reviewer's resolution. Within seconds, every reviewer of the application's group (spec-016 group-overlap) plus every participating admin receives an email titled `El solicitante respondió la resolución — Solicitud #{Id}` with a deep link to `/Review/{id}` (the reviewer's next step is to generate the convenio). The applicant receives no email for this transition — it is the reviewer's turn.

**Why this priority**: This is the **reported bug**. "Every interaction must be notified," yet the most consequential post-decision handoff — the applicant formally accepting/rejecting the resolution, which unblocks the reviewer to start the funding agreement — produced no signal at all. Reviewers had to poll the UI to discover that an application was ready for convenio generation. P1 because it closes the reported defect and re-opens the single most important post-`Resolved` handoff.

**Independent Test**: Run an E2E that drives the real UI: applicant signs in, opens their resolved application's response screen, submits accept/reject decisions, and asserts via the smtp4dev sidecar that (a) each reviewer in the application's group received exactly one `RESPONSE_SUBMITTED_REVIEWER` email with a `/Review/{id}` deep link, and (b) the applicant received zero emails for this transition.

**Acceptance Scenarios**:

1. **Given** a `Resolved` application with a reviewer group assigned and an applicant who has not yet responded, **When** the applicant submits their accept/reject decisions (`Resolved → ResponseFinalized`), **Then** the sidecar captures one `RESPONSE_SUBMITTED_REVIEWER` email per reviewer in the group, each with the spec-019 sender display and a `/Review/{id}` CTA, and zero applicant-bucket emails.
2. **Given** the same response submission, **When** a participating admin (explicit prior action on the application) is seeded, **Then** that admin also receives the reviewer-variant email; a non-participating admin receives nothing.
3. **Given** the worker is forced to process the response outbox row twice, **When** the second pass runs, **Then** no second `NotificationDelivery` row is written and no second provider call occurs (idempotency on `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`).

---

### User Story 2 - The appeal lifecycle is fully voiced (Priority: P1)

After a resolution that rejected at least one item, the applicant may open an appeal, exchange messages with the reviewer, and the reviewer resolves it. Every step now emails the counterparty:

- The applicant **opens an appeal** → reviewers + participating admins receive `Nueva apelación abierta — Solicitud #{Id}` (CTA `/ApplicantResponse/Appeal/{id}`).
- The applicant **posts an appeal message** → reviewers + admins receive `Nuevo mensaje en la apelación — Solicitud #{Id}` (CTA `/ApplicantResponse/Appeal/{id}`).
- The reviewer **posts an appeal message** → applicant + admins receive `Nuevo mensaje del revisor en tu apelación — Solicitud #{Id}` (CTA `/ApplicantResponse/Index/{id}`).
- The reviewer **resolves the appeal** (Uphold / GrantReopenToDraft / GrantReopenToReview) → applicant + admins receive `Resolución de tu apelación — Solicitud #{Id}`, with body copy that differs by outcome (CTA `/ApplicantResponse/Index/{id}`).
- When the resolution is **GrantReopenToReview** (the application returns to `UnderReview`), reviewers + admins **additionally** receive `Apelación concedida: solicitud reabierta para revisión — Solicitud #{Id}` (CTA `/Review/{id}`).

**Why this priority**: An appeal is the highest-stakes, most adversarial moment in the lifecycle, and it is conducted as a turn-based conversation. Silence here means a party can wait days unaware that the other has replied or that a decision has landed. P1 because the appeal flow is precisely where "every interaction must be notified" matters most.

**Independent Test**: Run an E2E that drives the real UI through open-appeal → applicant message → reviewer message → resolve, asserting the sidecar captures, in order: one `APPEAL_OPENED_REVIEWER` (to reviewers), one `APPEAL_MESSAGE_REVIEWER` (to reviewers), one `APPEAL_MESSAGE_APPLICANT` (to the applicant), and one `APPEAL_RESOLVED_APPLICANT` (to the applicant). Each message notifies only the opposite party.

**Acceptance Scenarios**:

1. **Given** a `ResponseFinalized` application with at least one rejected item, **When** the applicant opens an appeal (`ResponseFinalized → AppealOpen`), **Then** reviewers + participating admins receive `APPEAL_OPENED_REVIEWER` and the applicant receives nothing.
2. **Given** an open appeal, **When** the applicant posts a message, **Then** reviewers + admins receive `APPEAL_MESSAGE_REVIEWER`; **When** the reviewer posts a message, **Then** the applicant + admins receive `APPEAL_MESSAGE_APPLICANT`. The party who authored the message receives nothing for their own message.
3. **Given** an open appeal with three messages posted by the applicant in succession (no intervening reviewer message), **When** the worker dispatches, **Then** three distinct `APPEAL_MESSAGE_REVIEWER` emails are sent — one per message — because each message anchors on a distinct `VersionHistoryId` (no dedup collapse).
4. **Given** an open appeal, **When** the reviewer resolves it as **Uphold**, **Then** the applicant receives `APPEAL_RESOLVED_APPLICANT` with upheld-outcome body copy and reviewers receive nothing.
5. **Given** an open appeal, **When** the reviewer resolves it as **GrantReopenToReview** (`AppealOpen → UnderReview`), **Then** the applicant receives `APPEAL_RESOLVED_APPLICANT` (reopened-to-review body copy) **and** reviewers + admins receive `APPEAL_REOPENED_REVIEWER` — two outbox rows written in the same transaction with distinct `EventType` and the same `VersionHistoryId`, so both send and neither is deduped against the other.
6. **Given** an open appeal resolved as **GrantReopenToDraft** (`AppealOpen → Draft`), **Then** the applicant receives `APPEAL_RESOLVED_APPLICANT` with reopened-to-draft body copy and no reviewer-bucket email fires.

---

### User Story 3 - The convenio signing ceremony is fully voiced (Priority: P1)

After the applicant accepts the resolution, the reviewer generates the funding agreement (convenio) PDF, the applicant downloads/signs/uploads it, and the reviewer approves or rejects the signed upload. Every step now emails the counterparty:

- The reviewer **generates (or regenerates) the convenio** → applicant + admins receive `Tu convenio está listo para firmar — Solicitud #{Id}` (CTA to the applicant funding-agreement surface).
- The applicant **uploads the signed convenio** → reviewers (signing-inbox group-overlap) + admins receive `Convenio firmado recibido para revisión — Solicitud #{Id}` (CTA `/Review/SigningInbox`).
- The applicant **replaces** the pending signed upload → reviewers + admins receive `Convenio firmado reemplazado — Solicitud #{Id}` (CTA `/Review/SigningInbox`).
- The applicant **withdraws** the pending signed upload → reviewers + admins receive `Convenio firmado retirado — Solicitud #{Id}` (CTA `/Review/SigningInbox`).
- The reviewer **approves** the signed upload (`ResponseFinalized → AgreementExecuted`) → applicant + admins receive `Tu convenio fue ejecutado — Solicitud #{Id}` (CTA to the funding-agreement details surface).
- The reviewer **rejects** the signed upload requesting changes → applicant + admins receive `Tu convenio firmado requiere cambios — Solicitud #{Id}` (CTA to the applicant funding-agreement upload surface to re-upload).

**Why this priority**: The signing ceremony is the last mile that turns an approved application into an executed agreement, and it is a multi-step handoff between two parties. Without notifications the applicant doesn't know the convenio is ready to sign, the reviewer doesn't know a signed PDF arrived in their inbox, and neither learns the final execute/reject outcome out-of-band. P1 because an un-notified signing flow is where approved funding silently stalls.

**Independent Test**: Run an E2E that drives the real UI: reviewer generates the convenio (assert applicant gets `AGREEMENT_GENERATED_APPLICANT`), applicant uploads the signed PDF (assert reviewers get `SIGNED_UPLOAD_SUBMITTED_REVIEWER` with a `/Review/SigningInbox` CTA), reviewer approves (assert applicant gets `AGREEMENT_EXECUTED_APPLICANT`). A second variant exercises reject (assert `SIGNED_UPLOAD_REJECTED_APPLICANT`).

**Acceptance Scenarios**:

1. **Given** a `ResponseFinalized` application with the applicant's response accepted, **When** the reviewer generates the convenio, **Then** the applicant + participating admins receive `AGREEMENT_GENERATED_APPLICANT`, and a `VersionHistory` row with `Action="AgreementGenerated"` is written in the same transaction.
2. **Given** a generated convenio, **When** the reviewer **regenerates** it, **Then** the applicant receives a second `AGREEMENT_GENERATED_APPLICANT` email (distinct `VersionHistoryId`; not deduped against the first generation).
3. **Given** a generated convenio, **When** the applicant uploads a signed PDF, **Then** reviewers (signing-inbox group-overlap) + admins receive `SIGNED_UPLOAD_SUBMITTED_REVIEWER` with a `/Review/SigningInbox` CTA; **When** the applicant replaces it, **Then** reviewers + admins receive `SIGNED_UPLOAD_REPLACED_REVIEWER`; **When** the applicant withdraws it, **Then** reviewers + admins receive `SIGNED_UPLOAD_WITHDRAWN_REVIEWER`.
4. **Given** a pending signed upload, **When** the reviewer approves it (`→ AgreementExecuted`), **Then** the applicant + admins receive `AGREEMENT_EXECUTED_APPLICANT` and no reviewer-bucket email fires.
5. **Given** a pending signed upload, **When** the reviewer rejects it with a comment, **Then** the applicant + admins receive `SIGNED_UPLOAD_REJECTED_APPLICANT` whose body conveys that changes are required without embedding internal reviewer commentary verbatim beyond what the applicant already sees in-app, and the CTA points to the re-upload surface.

---

### Edge Cases

- **EC-001 — GrantReopenToReview dual-fire.** Resolving an appeal as `GrantReopenToReview` writes two outbox rows in one transaction (`APPEAL_RESOLVED_APPLICANT` + `APPEAL_REOPENED_REVIEWER`). They share `(ApplicationId, VersionHistoryId)` but differ on `EventType`, so the idempotency key differs and both send. The applicant and the reviewers each receive exactly one (different) email.
- **EC-002 — Successive appeal messages.** Multiple messages posted by the same party without an intervening reply each anchor on a distinct `VersionHistoryId` (one `PostAppealMessage` audit row per message), so each produces its own email; idempotency does not collapse them. (Mirrors spec-021 EC-001 for back-to-back resubmissions.)
- **EC-003 — Convenio regeneration.** Each generate/regenerate produces a distinct `AgreementGenerated` `VersionHistory` row → a distinct `AGREEMENT_GENERATED_APPLICANT` email. The applicant is re-notified that the convenio changed.
- **EC-004 — Withdraw notifies an empty inbox.** A `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` email links to `/Review/SigningInbox`, which will no longer list the withdrawn upload. The body states the upload was withdrawn so the reviewer is not confused by an absent item.
- **EC-005 — Appeal-resolution outcome drives body copy.** `APPEAL_RESOLVED_APPLICANT` renders one of three body variants (upheld / reopened-to-draft / reopened-to-review) selected from the resolution recorded on the appeal. The subject is identical across outcomes; only the body differs.
- **EC-006 — Reviewer rejection comment.** `SIGNED_UPLOAD_REJECTED_APPLICANT` may surface a short context cue that changes were requested, but MUST NOT leak internal reviewer commentary beyond what the applicant already accesses in-app (NFR-PII).
- **EC-007 — Participating-admin predicate across role changes.** This spec inherits the spec-021 OQ-011 known limitation: the participating-admin predicate filters `VersionHistory.UserId` by current `Admin` role; a former-admin-now-reviewer who acted on the application will not match. Fixing OQ-011 is **out of scope** here.
- **EC-008 — Applicant email changed between event-fire and worker-pickup.** The resolver runs at worker-pickup time and uses the recipient's *current* email, inherited unchanged from spec-021 EC-003. `PayloadJson` does not snapshot the recipient email.
- **EC-009 — Application hard-deleted before pickup.** Cascade delete removes the outbox rows; no email is sent (inherited from spec-021 EC-005).
- **EC-010 — Non-prod allowlist.** Outside Production, every new event flows through the same `RecipientAllowlistFilter`; non-allowlisted recipients are dropped and recorded as `BlockedByAllowlist` (inherited fail-closed behavior from spec-021 FR-017/FR-018).
- **EC-011 — Actor is also a participating admin.** When the actor who triggered the event also qualifies for the admin bucket (e.g., a reviewer who posts an appeal message and has prior explicit action on the application), the actor MUST be excluded so they never receive a copy of their own action (FR-013a).
- **EC-012 — Multiple reviewers in the group.** Reviewer-bucket events (`RESPONSE_SUBMITTED_REVIEWER`, `APPEAL_*_REVIEWER`, `SIGNED_UPLOAD_*_REVIEWER`) fan out to every reviewer whose groups overlap the application's groups; each receives exactly one email, deduped by `(UserId, Event)`.

## Requirements *(mandatory)*

### Functional Requirements

#### New events + triggers

- **FR-001**: The `NotificationEvent` enum MUST gain twelve new values, stored the same way as existing values (no schema change): `RESPONSE_SUBMITTED_REVIEWER`, `APPEAL_OPENED_REVIEWER`, `APPEAL_MESSAGE_REVIEWER`, `APPEAL_MESSAGE_APPLICANT`, `APPEAL_RESOLVED_APPLICANT`, `APPEAL_REOPENED_REVIEWER`, `AGREEMENT_GENERATED_APPLICANT`, `SIGNED_UPLOAD_SUBMITTED_REVIEWER`, `SIGNED_UPLOAD_REPLACED_REVIEWER`, `SIGNED_UPLOAD_WITHDRAWN_REVIEWER`, `AGREEMENT_EXECUTED_APPLICANT`, `SIGNED_UPLOAD_REJECTED_APPLICANT`.
- **FR-002**: On `Application.SubmitResponse` (applicant submits accept/reject decisions; `Resolved → ResponseFinalized`) the system MUST enqueue one `RESPONSE_SUBMITTED_REVIEWER` outbox row in the same transaction as the workflow state change and its `VersionHistory` row.
- **FR-003**: On `Application.OpenAppeal` (`ResponseFinalized → AppealOpen`) the system MUST enqueue one `APPEAL_OPENED_REVIEWER` outbox row.
- **FR-004**: On `Appeal.PostMessage` the system MUST enqueue `APPEAL_MESSAGE_REVIEWER` when the author is the applicant, and `APPEAL_MESSAGE_APPLICANT` when the author is a reviewer. Exactly one event fires per posted message, addressed to the opposite party.
- **FR-005**: On `Application.ResolveAppeal*` the system MUST enqueue one `APPEAL_RESOLVED_APPLICANT` outbox row for all three resolutions (Uphold / GrantReopenToDraft / GrantReopenToReview). The body variant MUST be selected from the recorded resolution.
- **FR-006**: When and only when the resolution is `GrantReopenToReview` (`AppealOpen → UnderReview`), the system MUST **additionally** enqueue one `APPEAL_REOPENED_REVIEWER` outbox row in the same transaction. The two rows share `(ApplicationId, VersionHistoryId)` and differ on `EventType`.
- **FR-007**: On funding-agreement generation **and** regeneration (`FundingAgreementService.PersistGenerationAsync` → `Application.GenerateFundingAgreement` / `RegenerateFundingAgreement`) the system MUST enqueue one `AGREEMENT_GENERATED_APPLICANT` outbox row. Each (re)generation fires the event again with a distinct `VersionHistoryId`.
- **FR-008**: On `Application.SubmitSignedUpload`, `ReplaceSignedUpload`, and `WithdrawSignedUpload` the system MUST enqueue `SIGNED_UPLOAD_SUBMITTED_REVIEWER`, `SIGNED_UPLOAD_REPLACED_REVIEWER`, and `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` respectively.
- **FR-009**: On `Application.ApproveSignedUpload` (`ResponseFinalized → AgreementExecuted`) the system MUST enqueue `AGREEMENT_EXECUTED_APPLICANT`; on `Application.RejectSignedUpload` the system MUST enqueue `SIGNED_UPLOAD_REJECTED_APPLICANT`.

#### Idempotency anchor (the one non-notification code change)

- **FR-010**: Convenio generation MUST append a `VersionHistory` row with `Action="AgreementGenerated"` in the same transaction as the generation (via the existing `Application.AddVersionHistory` domain method, honoring Rich Domain Model — Constitution §II — rather than a raw service-layer mutation), so the `AGREEMENT_GENERATED_APPLICANT` outbox row anchors on a real `VersionHistoryId` exactly like every other event. This is the only behavior change to a non-notification path; its side effect is that convenio generation becomes audited (it currently is not).
- **FR-011**: All twelve events MUST use the existing idempotency unique index `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` unchanged. The eleven non-generation triggers already append a `VersionHistory` row in their transaction; FR-010 supplies the twelfth.

#### Recipient resolution

- **FR-012**: `INotificationRecipientResolver` MUST return recipients per the §Recipient Rules table. Reviewer-bucket events resolve to reviewers of the application's group via the spec-016 group-overlap path (the same path the review queue and the signing inbox use); applicant-bucket events resolve to the applicant; participating-admins are added on every event per the inherited spec-021 predicate.
- **FR-013**: Recipient de-duplication and the bucket-priority `applicant > reviewer > admin` rule MUST apply unchanged from spec-021 FR-012. One email per `(UserId, Event)`.
- **FR-013a**: The actor who triggered the event MUST be excluded from the resolved recipient set (defense-in-depth), consistent with spec-021's exclusion of the submitting applicant. This matters where the actor also qualifies via another bucket — e.g., a reviewer who authors an appeal message and is also a participating admin MUST NOT receive a copy of their own `APPEAL_MESSAGE_APPLICANT`.
- **FR-014**: Admin recipients MUST reuse the bucket-priority-winning variant body for each event (no separate admin-flavored partials), per spec-021 FR-024.

#### Templates + content

- **FR-015**: The system MUST ship one Razor body partial pair (HTML + plain-text fallback) per new event = 24 new `.cshtml` files under `Views/Emails/`, rendered under the existing shared `_EmailLayout.cshtml` (text-only wordmark, no inline `<img>`, static `mailto:soporte@programa-semilla.cr` footer).
- **FR-016**: All new template strings MUST be es-CR Spanish (no English fallback, no i18n key system). Subjects MUST match the §Event Catalog table.
- **FR-017**: `APPEAL_RESOLVED_APPLICANT` MUST render three body variants keyed off the appeal resolution (upheld / reopened-to-draft / reopened-to-review) within the single event's partial pair.

#### CTA deep links

- **FR-018**: Every new event's CTA MUST link to `Notifications:BaseUrl` + an **existing** MVC route; no new MVC routes are introduced. The allowed CTA target set is extended (beyond spec-021 FR-026's two targets) to: `/Review/{id}`, `/Review/SigningInbox`, `/ApplicantResponse/Index/{id}`, `/ApplicantResponse/Appeal/{id}`, and the applicant funding-agreement surface under `/Applications/{id}/FundingAgreement/`. CTA targets per event MUST match the §Event Catalog table.
- **FR-019**: Access control for every CTA target MUST stay enforced by the existing `[Authorize]` attributes on the target controllers. This spec adds no authorization logic.

#### Reuse (unchanged from spec 021)

- **FR-020**: All new events MUST flow through the existing outbox writer, `EmailDispatchWorker` (poll/claim/retry/backoff/dead-letter), `IEmailSender` provider abstraction (smtp4dev / Mailgun / NoOp), and `RecipientAllowlistFilter`. No change to that pipeline is permitted by this spec.
- **FR-021**: The feature MUST introduce **zero** new database tables, **zero** dacpac changes, and **zero** EF migrations. The `NotificationEvent` enum extension is the only data-shape change and is stored identically to existing values.

### Non-Functional Requirements

- **NFR-001**: All new email bodies MUST be es-CR Spanish (Constitution §VI + spec 012).
- **NFR-002**: No inline `<img>` in any new email body (spec 019 NFR-005). The spec-019 brand-grep gate MUST stay green on all 24 new templates (no "Capital Semilla", no "Forge", no English-only strings).
- **NFR-003 (PII)**: New email bodies MUST NOT carry PII beyond what the recipient already accesses in-app. In particular, `SIGNED_UPLOAD_REJECTED_APPLICANT` MUST NOT embed internal reviewer commentary verbatim; reviewer-bucket emails carry applicant name + `Solicitud #{Id}` + CTA only.
- **NFR-004**: P95 time-to-send under 30 s and P99 under 2 min, reused from spec-021 NFR-002. The added events MUST NOT regress these targets.
- **NFR-005**: A worker exception while dispatching a new event MUST NOT crash the Web host (inherited spec-021 NFR-004).

### Event Catalog (new)

| # | Event (enum) | Trigger (domain method) | Subject (es-CR) | CTA target (existing route) |
|---|---|---|---|---|
| 1 | `RESPONSE_SUBMITTED_REVIEWER` | `Application.SubmitResponse` | `El solicitante respondió la resolución — Solicitud #{Id}` | `/Review/{id}` |
| 2 | `APPEAL_OPENED_REVIEWER` | `Application.OpenAppeal` | `Nueva apelación abierta — Solicitud #{Id}` | `/ApplicantResponse/Appeal/{id}` |
| 3 | `APPEAL_MESSAGE_REVIEWER` | `Appeal.PostMessage` (applicant authors) | `Nuevo mensaje en la apelación — Solicitud #{Id}` | `/ApplicantResponse/Appeal/{id}` |
| 4 | `APPEAL_MESSAGE_APPLICANT` | `Appeal.PostMessage` (reviewer authors) | `Nuevo mensaje del revisor en tu apelación — Solicitud #{Id}` | `/ApplicantResponse/Index/{id}` |
| 5 | `APPEAL_RESOLVED_APPLICANT` | `Application.ResolveAppeal*` (all 3) | `Resolución de tu apelación — Solicitud #{Id}` | `/ApplicantResponse/Index/{id}` |
| 6 | `APPEAL_REOPENED_REVIEWER` | `Application.ResolveAppealAsGrantReopenToReview` | `Apelación concedida: solicitud reabierta para revisión — Solicitud #{Id}` | `/Review/{id}` |
| 7 | `AGREEMENT_GENERATED_APPLICANT` | `PersistGenerationAsync` (generate + regenerate) | `Tu convenio está listo para firmar — Solicitud #{Id}` | `/Applications/{id}/FundingAgreement/` |
| 8 | `SIGNED_UPLOAD_SUBMITTED_REVIEWER` | `Application.SubmitSignedUpload` | `Convenio firmado recibido para revisión — Solicitud #{Id}` | `/Review/SigningInbox` |
| 9 | `SIGNED_UPLOAD_REPLACED_REVIEWER` | `Application.ReplaceSignedUpload` | `Convenio firmado reemplazado — Solicitud #{Id}` | `/Review/SigningInbox` |
| 10 | `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` | `Application.WithdrawSignedUpload` | `Convenio firmado retirado — Solicitud #{Id}` | `/Review/SigningInbox` |
| 11 | `AGREEMENT_EXECUTED_APPLICANT` | `Application.ApproveSignedUpload` | `Tu convenio fue ejecutado — Solicitud #{Id}` | `/Applications/{id}/FundingAgreement/` |
| 12 | `SIGNED_UPLOAD_REJECTED_APPLICANT` | `Application.RejectSignedUpload` | `Tu convenio firmado requiere cambios — Solicitud #{Id}` | `/Applications/{id}/FundingAgreement/` |

### Recipient Rules (new)

| Event (enum) | Reviewers of group | Applicant | Participating admins |
|---|---|---|---|
| `RESPONSE_SUBMITTED_REVIEWER` | yes | no | yes |
| `APPEAL_OPENED_REVIEWER` | yes | no | yes |
| `APPEAL_MESSAGE_REVIEWER` | yes | no | yes |
| `APPEAL_MESSAGE_APPLICANT` | no | yes | yes |
| `APPEAL_RESOLVED_APPLICANT` | no | yes | yes |
| `APPEAL_REOPENED_REVIEWER` | yes | no | yes |
| `AGREEMENT_GENERATED_APPLICANT` | no | yes | yes |
| `SIGNED_UPLOAD_SUBMITTED_REVIEWER` | yes | no | yes |
| `SIGNED_UPLOAD_REPLACED_REVIEWER` | yes | no | yes |
| `SIGNED_UPLOAD_WITHDRAWN_REVIEWER` | yes | no | yes |
| `AGREEMENT_EXECUTED_APPLICANT` | no | yes | yes |
| `SIGNED_UPLOAD_REJECTED_APPLICANT` | no | yes | yes |

Bucket priority on collision: `applicant > reviewer > admin`. One email per `(UserId, Event)`.

### Key Entities

- **NotificationEvent (enum)** — Extended by the twelve values in FR-001. Stored identically to existing values; no schema change.
- **NotificationOutbox / NotificationDelivery** — Reused unchanged from spec 021. Each new event writes outbox rows transactionally and delivery rows per recipient, carrying the existing idempotency unique index.
- **VersionHistory** — Existing audit entity. FR-010 adds one new `Action="AgreementGenerated"` row type on convenio generation; all other triggers already write a `VersionHistory` row. No schema change (the table and columns already exist).
- **NotificationRecipient** — Resolver output value object, reused unchanged: `(UserId, Email, DisplayName, Bucket, TemplateVariantKey)`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All twelve new events fire on their workflow trigger across the full Aspire stack and are captured by the smtp4dev sidecar; verified by at least one E2E per user story (US1, US2, US3), each driving the **real UI journey** (no deep-link shortcuts to MVC routes the UI never exposes).
- **SC-002**: The recipient predicate matches the §Recipient Rules table exactly. Verified by an integration test seeding one applicant, reviewers in the application's group, one participating admin, and one non-participating admin, asserting the expected per-bucket counts on every event.
- **SC-003**: Idempotency holds for the new events — forcing the worker to process the same outbox row twice produces no second delivery and no second provider call — including (a) successive appeal messages each sending exactly once, and (b) the `GrantReopenToReview` dual-fire sending exactly two distinct emails. Verified by integration test.
- **SC-004**: The non-prod allowlist blocks 100% of non-allowlisted recipients for the new events when `HostEnvironment != "Production"`. Verified by integration test with an empty allowlist.
- **SC-005**: The spec-019 brand-grep gate stays green on all 24 new templates (zero hits for "Capital Semilla" / "Forge"; zero English-only strings).
- **SC-006**: Zero new EF migrations and zero dacpac changes are introduced; verified by the constitution check during planning plus the CI grep gate over `**/Migrations/**`.
- **SC-007**: The reported bug — reviewer not notified when the applicant accepts/rejects the reviewer's response — is closed and regression-covered by the US1 E2E.
- **SC-008**: P95 time-to-send stays below 30 s across a full E2E run including the new events (no regression of spec-021 NFR-002).

## Assumptions

- The eleven non-generation triggers (`SubmitResponse`, `OpenAppeal`, `PostAppealMessage`, `ResolveAppeal*`, `SubmitSignedUpload`, `ReplaceSignedUpload`, `WithdrawSignedUpload`, `ApproveSignedUpload`, `RejectSignedUpload`) each append exactly one `VersionHistory` row inside the same `SaveChangesAsync()` transaction, providing a stable `VersionHistoryId` for the idempotency anchor. (Verified 2026-05-27 against the Application/Infrastructure services.)
- `FundingAgreementService.PersistGenerationAsync` currently appends no `VersionHistory` row on success; FR-010 adds one. No other generation behavior changes.
- The signing inbox resolves reviewers via the spec-016 group-overlap predicate (applicant's groups ∩ reviewer's groups), which is the recipient set for the signing-stage reviewer events. (Verified 2026-05-27.)
- The appeal surface routes (`/ApplicantResponse/Index/{id}`, `/ApplicantResponse/Appeal/{id}`) and the funding-agreement applicant surface already exist and are guarded by the appropriate `[Authorize]` attributes (spec 027 / appeal flow). This spec links CTAs to them and adds no routes.
- The author of a `PostAppealMessage` is identifiable as applicant vs reviewer at enqueue time, so the correct directional event (FR-004) can be chosen.
- All spec-021 infrastructure (outbox, worker, providers, allowlist, `_EmailLayout`, `MailCaptureClient`, recipient resolver, dedup) is present and unchanged.

## Dependencies

- **Spec 021 (email-notifications)** — Parent. Reuses the transactional outbox, `EmailDispatchWorker`, `IEmailSender` providers (smtp4dev / Mailgun / NoOp), `RecipientAllowlistFilter`, `INotificationRecipientResolver`, `_EmailLayout.cshtml`, `MailCaptureClient`, and the `NotificationOutbox` / `NotificationDelivery` tables and idempotency index — all unchanged.
- **Spec 016 (user-groups)** — Read-only consumer of the group-overlap predicate for both review-stage and signing-inbox reviewer resolution.
- **Spec 019 (programa-semilla-brand)** — Sender display, signature block, brand-grep gate applied to the 24 new templates.
- **Spec 027 (review-funding-ux)** — Provides the funding-agreement / signing surfaces and the `ApplicantResponse` appeal surfaces this spec links its CTAs to.
- **Constitution §IV** — Dacpac is schema source of truth; no EF migrations. (FR-021, SC-006.)
- **Constitution §III** — E2E mandate; new E2E coverage drives the real UI journey through the smtp4dev sidecar.

## Out of Scope

- In-app notifications, SignalR push, bell icon, toast feed (multi-spec open thread, remains open).
- Stage-granular events beyond this set (`STAGE_APPROVED`, `MOVED_TO_NEXT_STAGE`, `REVIEWER_ASSIGNED`, `REVIEWER_UNASSIGNED`, `COMMENT_ADDED` on non-appeal surfaces).
- Applicant self-confirmation emails (explicitly declined; only the counterparty is notified per action).
- User-facing notification-preferences / opt-out UI.
- Digests / batching / rate-limiting of the chat-like appeal-message cadence.
- Fixing the spec-021 OQ-011 participating-admin role-change predicate limitation (inherited as-is).
- Any schema / dacpac change, new table, or EF migration.
- Retention / cleanup job for `NotificationOutbox` / `NotificationDelivery` rows (still deferred from spec 021).

## Open Questions

- **OQ-001 — Appeal-message email cadence at scale.** v1 sends one email per appeal message (per the clarified decision). If a high-volume appeal thread proves noisy in practice, a future spec MAY add debounce/digest. Deferred; not a v1 concern.
- **OQ-002 — Participating-admin role-change fidelity.** Inherited spec-021 OQ-011 limitation applies to all twelve new events. A future spec MAY add a role-at-action snapshot. Out of scope here.
