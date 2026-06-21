# Feature Specification: ALIA Transactional Email Brand UI-Lift

**Feature Branch**: `041-email-brand-lift`
**Created**: 2026-06-19
**Status**: Draft
**Input**: User description: "Redesign every ALIA transactional email into a branded, institutional design system for Programa Semilla (centered container, logo header, teal palette, CTA buttons, status/info cards, partner-logo footer strip), adopt the polished 'ALIA' voseo copy from the reference file, and add three new emails."

## Overview

Today every system email shares a deliberately minimal, **text-only** layout (a text wordmark, a near-black button, a gray footer) — a choice made in spec 021 (FR-023/NFR-001) to maximize deliverability. The institution now wants its transactional mail to look and read like Programa Semilla: a branded shell with the official logo, the brand teal palette, clear calls to action, structured detail cards, and a partner-logo footer strip — mirroring the brand work already done on the web UI (spec 037) and the PDF documents (spec 016). At the same time the platform is being given a product name, **ALIA**, which the new copy adopts throughout while keeping *Programa Semilla* as the institutional brand and sign-off.

This feature is a **visual + copy lift of the email subsystem**, plus three new emails the institution wants but does not yet send. It intentionally reverses spec 021's no-inline-image rule; that reversal is a recorded decision, mitigated by image-blocked degradation requirements (NFR-4).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Branded design system applied to every email (Priority: P1)

A recipient (applicant, reviewer, or auditor) receives any system email — a submission receipt, a decision notice, an appeal update, a signing-ceremony step, a stage reminder. Instead of the plain text-only layout, the email arrives in the Programa Semilla brand: a centered container, the official logo in the header, brand-teal headings and call-to-action button, a structured "Detalle" card where the message lists data, and a footer carrying the partner-logo strip, support contacts, and the automatic-message note. The platform is referred to as **ALIA** and the voice is consistent Costa Rican voseo. Every dynamic value (name, application number, status, links) appears exactly where it did before.

**Why this priority**: This is the core of the request and the largest surface. Delivering it — even without the three new emails — fully satisfies the brand-lift goal and is independently shippable.

**Independent Test**: Trigger each existing notification (or seed it) and capture the message in the mail sandbox; verify it renders the branded shell, preserves every variable, uses ALIA naming + Programa Semilla sign-off, shows the partner footer, and reads correctly with images both shown and blocked.

**Acceptance Scenarios**:

1. **Given** an applicant submits an application, **When** the submission-received email is sent, **Then** it renders the branded shell (logo header, teal CTA, partner footer), refers to the platform as ALIA, signs off as "Equipo Programa Semilla", and preserves the recipient name, application reference, and dashboard link.
2. **Given** a reviewer-facing notification that lists structured data (e.g. new application pending review), **When** it is sent, **Then** the data appears in a "Detalle" status/info card with no overflow, and every listed field is preserved.
3. **Given** any email whose body implies an action but carries no link variable, **When** it is sent, **Then** no CTA button and no fallback link are rendered, and no URL is invented.
4. **Given** a recipient whose client blocks images, **When** they open any email, **Then** the full message is readable, brand images show their Spanish alt text, and no critical content is trapped in an image.
5. **Given** any redesigned HTML email, **When** its plain-text twin is produced, **Then** the plain-text twin conveys the same meaning and the same variables in plain text.

---

### User Story 2 - "Solicitud en revisión" applicant notice (Priority: P2)

When an application moves into the review stage (distinct from the moment of submission), the applicant receives a new email letting them know their request has entered review and what to expect, with a link to follow its status on ALIA.

**Why this priority**: A genuinely new, recipient-valued notification, but secondary to lifting the emails that already exist.

**Independent Test**: Move a submitted application into the review stage and confirm the applicant receives exactly one branded "Tu solicitud está en revisión" email, deduplicated per the existing idempotency rules, respecting the non-production allowlist.

**Acceptance Scenarios**:

1. **Given** an application that transitions to the review stage, **When** the transition is recorded, **Then** the applicant receives one "Solicitud en revisión" email in the branded shell with their name and a status link.
2. **Given** the same transition is processed more than once, **When** the notification is evaluated, **Then** the applicant receives the email only once (idempotent).
3. **Given** a non-production environment, **When** the recipient is not on the allowlist, **Then** the email is dropped and recorded as blocked, consistent with every other event.

---

### User Story 3 - "Tu contraseña fue actualizada" confirmation (Priority: P2)

After a user successfully changes or resets their password, they receive a confirmation email telling them the change succeeded and what to do if they did not make it (contact support).

**Why this priority**: A security-hygiene email recipients expect; independent of the application workflow and of the outbox.

**Independent Test**: Complete a password change/reset and confirm a single branded confirmation email is delivered to that user with the security guidance and support contact.

**Acceptance Scenarios**:

1. **Given** a user completes a password change, **When** the change succeeds, **Then** the user receives a branded "Tu contraseña fue actualizada" email confirming the change and advising them to contact support if they did not make it.
2. **Given** a password reset via the reset link, **When** the new password is set, **Then** the same confirmation email is sent.

---

### User Story 4 - "Nueva empresa para revisión" notification (Priority: P3)

A designated audience is notified when a new company is registered for review, with the company's details in a "Detalle" card and a link to follow up on ALIA.

**Why this priority**: Lowest priority because the precise business trigger and recipient are not yet settled (see Open Questions OQ-1). The branded template can be built now; the live trigger is gated on that product decision.

**Independent Test**: Once the trigger/recipient are confirmed, fire the trigger and verify the designated audience receives one branded notification with the company detail card.

**Acceptance Scenarios**:

1. **Given** the confirmed trigger occurs, **When** the notification is sent, **Then** the designated recipient(s) receive one branded "Nueva empresa para revisión" email with the company name, identification, and date in a "Detalle" card.
2. **Given** the trigger/recipient are not yet confirmed, **When** the feature ships, **Then** the email template exists and is render-tested but no live notification is emitted.

---

### Edge Cases

- **No link available** — the email implies an action but no link variable is supplied: render neither the CTA button nor the fallback link; never fabricate a URL.
- **Long values** — a long applicant or company name in a "Detalle" card wraps cleanly without breaking the layout or overflowing the container.
- **Outlook rendering** — the button and logos render acceptably in the Windows/Word rendering engine (bulletproof button technique; no reliance on modern CSS layout).
- **Images blocked** — every email remains fully legible; alt text stands in for each image; no content lives only inside an image.
- **Forced dark mode** — logos and text remain visible (no white logo vanishing on a light container, no invisible text) where a client forces a dark theme.
- **Allowlist drop (non-production)** — for the new under-review outbox event, a recipient not on the allowlist is dropped and recorded exactly like existing events.

## Requirements *(mandatory)*

### Functional Requirements — design system

- **FR-001**: The shared email layout MUST be rebuilt into a centered, fixed-max-width (≈600px) branded shell composed of: a header carrying the official Programa Semilla logo, a body region, a sign-off block, a partner-logo footer strip, and a support footer.
- **FR-002**: All brand images MUST be served from the application at absolute URLs (derived from the configured public base URL) so they resolve when the email is opened outside an authenticated session. Every image MUST carry descriptive Spanish alternative text.
- **FR-003**: Emails MUST use the Programa Semilla brand palette — primary teal `#008a9e`, secondary teal `#42afa8`, orange `#f9a61c`, yellow `#ffc729`, on light neutral backgrounds. The call-to-action button MUST use brand teal (replacing the previous near-black button).
- **FR-004**: The design system MUST provide reusable building blocks — a hero/title block, a status/info ("Detalle") card, a key-value detail list, and a CTA button — that individual emails compose rather than redefining chrome per email.
- **FR-005**: A CTA button MUST render only when a valid link variable is present; in that case a plain-text fallback link MUST also be shown. When no link variable exists, neither the button nor the fallback link is rendered, and no URL is invented.
- **FR-006**: Every email footer MUST include the five-partner logo strip (Banca para el Desarrollo, CROCUS, nexo, De la Mano con su PYME, Programa Semilla), Programa Semilla branding, a support line with both the support email and the phone +506 4600-1234, and the "mensaje automático — no respondás" note.

### Functional Requirements — copy & naming

- **FR-007**: Email body copy MUST refer to the platform as **ALIA**; the institutional brand, logo, and sign-off MUST remain **Programa Semilla** ("Equipo Programa Semilla"). Costa Rican voseo MUST be used throughout.
- **FR-008**: Where the reference copy file covers an email, that copy is canonical. All other emails MUST be light-polished into the same voice while preserving meaning, every dynamic variable, all warnings, status information, and automatic-message notes. Existing subject lines MUST be preserved unless the reference supplies a new one.
- **FR-009**: Every redesigned HTML email MUST keep its plain-text twin in sync — same meaning and same variables, in plain text (no design system).

### Functional Requirements — coverage

- **FR-010**: The redesign MUST cover every existing HTML email: the application-lifecycle and post-resolution set (submission, return, resubmission, approval, rejection, withdrawal, response, appeals, agreement generation/execution, signed-upload lifecycle), the identity emails (invitation, forgot-password), the stage emails (24h/72h reminders, expiry), and the supplier/auditor email.

### Functional Requirements — three new emails

- **FR-011**: When an application transitions into the review stage (distinct from the submission receipt), the applicant MUST receive a new "Solicitud en revisión" email. It is an outbox-driven event that MUST deduplicate per the existing idempotency rules and respect the non-production allowlist.
- **FR-012**: After a user successfully changes or resets their password, that user MUST receive a "Tu contraseña fue actualizada" confirmation email advising them to contact support if they did not make the change. This is a direct-send identity email (not an outbox event).
- **FR-013** — ~~WITHDRAWN 2026-06-20~~: Originally required a "Nueva empresa para revisión" branded notifier email. Resolved (OQ-1) as a **duplication** of spec 038's already-shipped supplier→auditor notification (`IProviderCreatedNotifier`); the stub was removed and no live trigger added. See the Evolution Log.
- **FR-014**: The new **outbox** event (FR-011) MUST integrate with the existing outbox mechanics — a closed-set event identity, the established idempotency key, recipient resolution consistent with sibling events, the per-event call-to-action route, the non-production allowlist — and MUST ship an HTML email and a plain-text twin. The FR-013 notifier email MUST likewise ship an HTML email and a plain-text twin and respect the non-production allowlist, but is not subject to the application-keyed outbox idempotency mechanics.

### Non-Functional Requirements

- **NFR-001**: Emails MUST render acceptably across major clients including Outlook (Windows/Word engine), Gmail, Apple Mail, and mobile clients, using a table-based, inline-styled approach within the ≈600px width; no external stylesheets and no reliance on modern CSS layout (flexbox/grid).
- **NFR-002**: Layout MUST be single-column and fluid down to small screens, with a tap-friendly CTA on mobile.
- **NFR-003**: Emails MUST meet accessibility expectations — semantic heading structure, WCAG AA text contrast for brand-teal-on-white, descriptive Spanish alt text on all images, and link text meaningful out of context.
- **NFR-004**: Each email MUST remain fully readable with images disabled; no critical content may be conveyed only through an image.
- **NFR-005**: Emails MUST render sensibly where a client forces dark mode, with no disappearing logos or text.
- **NFR-006**: The feature MUST introduce no new managed dependencies and no build step; assets are served locally, consistent with project conventions.
- **NFR-007**: All copy MUST be es-CR.

### Key Entities *(include if feature involves data)*

- **Email design system**: the shared branded shell plus reusable blocks (hero/title, status/info card, detail list, CTA button) that every email composes. Attributes: brand palette, logo header, partner footer strip, support footer.
- **Brand asset**: a hosted image (header logo, partner-strip logo set) referenced by absolute URL with Spanish alt text.
- **Notification event (new)**: a closed-set email trigger identity for the new "Solicitud en revisión" outbox email, participating in idempotency, recipient resolution, CTA routing, and the allowlist.
- **Notifier email (new)**: the "Nueva empresa para revisión" email, delivered through a non-application-keyed notifier seam (mirroring the provider-registered notifier), not the application outbox; live trigger/recipient deferred (OQ-1).
- **Direct-send identity email (new)**: the password-changed confirmation, sent outside the outbox to the affected user.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of existing HTML emails plus the three new emails render in the Programa Semilla brand system (logo header, teal palette, partner footer) when captured in the mail sandbox.
- **SC-002**: Zero dynamic variables are lost relative to the current emails — every value present today appears in the redesigned email and its plain-text twin.
- **SC-003**: No email contains an invented or hard-coded URL; CTA buttons appear only when a real link variable is supplied.
- **SC-004**: Every redesigned email is legible with images blocked, with alt text standing in for each image (verified by inspection).
- **SC-005**: The brand palette and partner footer strip are visually consistent across all emails (no off-brand colors, no missing footer).
- **SC-006**: Filtered mail-capture end-to-end tests are green for every changed event and for each new email.
- **SC-007**: Each redesigned HTML email has a present, in-sync plain-text twin.
- **SC-008**: The new "Solicitud en revisión" and "password-changed" emails are each delivered exactly once per triggering action in the sandbox; the "Nueva empresa para revisión" template render-tests successfully even while its live trigger is deferred.

## Assumptions

- The platform is being branded as **ALIA** while *Programa Semilla* remains the institution; logo and sign-off stay Programa Semilla (confirmed with stakeholder).
- The institution wants the partner-logo footer strip on **every** email, matching the reference layout (confirmed).
- "Light polish" of copy is acceptable for emails the reference file does not cover, provided meaning, variables, and warnings are preserved (confirmed).
- A publicly reachable base URL is configured for composing absolute image and link URLs (the same configuration the emails already use for deep links).
- Plain-text twins remain plain text; the brand lift is HTML-only.
- The mail provider, outbox/worker, idempotency, allowlist, and CTA-route mechanics are reused as-is; this feature changes templates, copy, and adds event identities — not the delivery pipeline.
- The `From:` sender display configuration is unchanged by this feature.

## Dependencies

- The existing transactional-outbox and dispatch pipeline, idempotency keying, and non-production allowlist (spec 021).
- The per-event call-to-action routing and actor-exclusion seams (spec 028).
- The configured public base URL used to compose absolute links (and now absolute image URLs).
- The Programa Semilla brand assets and palette (spec 037 / `seeds/emails/`: header logo, partner-strip image, color palette).
- No external services beyond the existing mail provider.

## Out of Scope

- Changing the `From:` sender display name/address configuration.
- Altering the outbox/worker/allowlist delivery mechanics themselves.
- The PDF document templates and any non-email web UI.
- Backfilling or re-sending previously delivered emails.

## Open Questions

- **OQ-1 — RESOLVED 2026-06-20 (FR-013 withdrawn)**: The intended scenario ("applicant adds a new supplier → notify auditors") is already covered by spec 038's `IProviderCreatedNotifier`. The 041 "Nueva empresa para revisión" notifier was a duplication and has been removed; group-scoping of auditors was declined. See the Evolution Log.
- **OQ-2**: Is entering "review" (FR-011) a distinct lifecycle transition from submission in the current state model, or do submit→review happen atomically (which would make the new email redundant with the submission receipt)? To be confirmed against the state model during planning.
- **OQ-3**: From which public path are brand images served (a dedicated email-assets path vs. the existing static-library path)? To be resolved during planning.

## Evolution Log

- **2026-06-19 — FR-013/FR-014 refined (planning-time, pre-code).** Original wording modeled "Nueva empresa para revisión" as a new *outbox* event sharing the outbox idempotency key with FR-011. Planning research found the outbox is **application-keyed** (its payload and `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` dedup key both require an `ApplicationId`), but a company registration has no application. FR-013 is therefore refined to a **notifier-pattern** email (mirroring the spec-038 provider-registered notifier), and FR-014 now scopes the outbox-integration requirements to the under-review event (FR-011) only. The under-review event remains a true outbox event; FR-013's deferral (OQ-1) is unchanged. **Observable behavior is unchanged** — this is an internal delivery-mechanism correction. Recorded in `research.md` (Decision 5) and `contracts/notification-events.md`; `plan.md`/`tasks.md` already reflect the notifier model. OQ-2 and OQ-3 were also resolved during planning (see `research.md`).

- **2026-06-19 — Implementation notes (carried OQ resolutions + deviations into code).**
  - **OQ-2 confirmed in code**: `Application.StartReview()` is a distinct `Submitted → UnderReview` transition (not atomic with submit), so `ApplicationUnderReviewApplicant` is non-redundant. The transition now writes a `VersionHistory("StartReview")` row to anchor the outbox dedup key; `ReviewService.GetApplicationForReviewAsync` gained a `reviewerUserId` parameter (`ActorUserId` + history author).
  - **OQ-3 resolved**: reused `wwwroot/lib/brand/` (no new static path). T001 verified `partners-footer.png` already matches the seed's 5-partner set, so no `email-partners-footer.png` asset was added.
  - **Single-renderer unification** (Decision 1): the direct-send factories + notifiers now render Razor through `IEmailViewRenderer`/`_EmailLayout` instead of plain-text `{{token}}` substitution. The direct-send `EmailMessage` (Application.Abstractions) gained an optional `TextBody`; `SmtpEmailSender` ships a `multipart/alternative` when present. The three template factories moved Singleton→Scoped (they now depend on the scoped renderer).
  - **Text-twin encoding fix**: `EmailViewRenderer` HTML-decodes the layout-less (`.text.cshtml`) render so plain-text bodies show literal `+506 4600-1234` / accented es-CR copy instead of HTML entities (Razor HTML-encodes `@`-expressions). Scoped to the text path; HTML bodies are unchanged.
  - **US3 invite-first-set** (research D4): the password-changed confirmation also fires on the spec-033 invite first-set (both flow through `ConsumePasswordResetTokenHandler`); accepted as a correct, security-valuable message.
  - **Test reconciliation**: tests encoding the reversed spec-021 no-image / text-only-wordmark rule were updated to the brand shell; factory unit/integration tests moved to the model-building contract. Incidental: unblocked a pre-existing spec-037 E2E compile break (`ManualScreenshotsCaptureTests.CompanyNameInput`).
  - **OQ-1 still open**: FR-013 ships as a render-only notifier stub with no live trigger/call site (guarded by a source-scan test).

- **2026-06-20 — OQ-1 resolved → FR-013 withdrawn (duplication).** Product clarified the intended trigger as "an applicant adds a new supplier → notify auditors." That scenario is **already shipped by spec 038** (`IProviderCreatedNotifier.NotifyAuditorsAsync`, fired from `SupplierCatalogService.CreateDraftWithBranchAsync`, with the branded `ProviderCreatedAuditor` email). The 041 "Nueva empresa para revisión" notifier was therefore a **duplication** of existing behavior, and the proposed group-scoping of auditors was declined (auditors remain a platform-wide, un-scoped compliance role — no change). **FR-013 is withdrawn**: the render-only stub (`ICompanyForReviewNotifier`/`CompanyForReviewNotifier`, `CompanyForReviewAuditor.cshtml` + `.text` twin, `CompanyForReviewNotifierTests`, DI registration, and the design-system twin-list entry) was deleted. No live trigger is added. The branded email design system (US1–US3) is unaffected; build green, `EmailDesignSystemTests` 9/9.
