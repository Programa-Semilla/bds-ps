---
description: "Task list for ALIA Transactional Email Brand UI-Lift (041)"
---

# Tasks: ALIA Transactional Email Brand UI-Lift

**Input**: Design documents from `specs/041-email-brand-lift/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — Constitution III makes E2E non-negotiable and the CLAUDE.md delivery bar gates on filtered mail-capture E2E. Integration tests hit a real DB (no mocks).

**Organization**: By user story (US1 P1 brand lift → US2 P2 under-review → US3 P2 password-changed → US4 P3 company-for-review stub).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- File paths are repo-relative.

## Conventions for this feature

- Outbox HTML bodies + their `.text.cshtml` twins live in `src/FundingPlatform.Web/Views/Emails/`.
- New shared partials live in `src/FundingPlatform.Web/Views/Emails/Shared/`.
- Direct-send factories live in `src/FundingPlatform.Infrastructure/Email/`.
- Every refactored HTML email MUST keep its `.text.cshtml` twin in sync (FR-009).
- No DB schema change, no new managed dependency, no build step.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Brand assets + a single copy/brand constants source.

- [X] T001 Verify brand email assets: confirm `src/FundingPlatform.Web/wwwroot/lib/brand/programa-semilla-horizontal.png` (header) and `partners-footer.png` (5-partner strip) match the intended set in `seeds/emails/` (`Fooder-general.png` = Banca para el Desarrollo / CROCUS / nexo / De la Mano con su PYME / Programa Semilla). If `partners-footer.png` differs, add `src/FundingPlatform.Web/wwwroot/lib/brand/email-partners-footer.png` from the seed and use that path downstream.
- [X] T002 [P] Add an es-CR brand/copy constants source (ALIA platform name usage, sign-off "Equipo Programa Semilla", support email + phone `+506 4600-1234`, automatic-message note) — extend the existing email resources/constants rather than scattering literals; document the chosen location in a code comment referencing FR-006/FR-007.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared design system every email (existing + new) renders through. **No user story can start until this is done.**

- [X] T003 Extract a reusable `IEmailViewRenderer.RenderViewAsync(viewPath, model, disableLayout)` from the private Razor-view-to-string logic in `src/FundingPlatform.Web/Services/RazorEmailRenderer.cs`; register in DI (`NotificationsServiceCollectionExtensions`). `RazorEmailRenderer` consumes it for outbox emails; direct-send factories will consume it in US1/US3.
- [X] T004 Add `LogoUrl` and `PartnerStripUrl` to `EmailRenderModel` and compose them from `Notifications:BaseUrl` (reuse `Combine`) in `RazorEmailRenderer.RenderAsync`, in `src/FundingPlatform.Web/Services/RazorEmailRenderer.cs`.
- [X] T005 Rebuild `src/FundingPlatform.Web/Views/Emails/_EmailLayout.cshtml` as the 600px centered, table-based, inline-CSS branded shell: `_BrandHeader` → `@RenderBody()` → sign-off block ("Equipo Programa Semilla") → `_PartnerFooter`. Brand palette inline (teal `#008a9e`/`#42afa8`, orange `#f9a61c`, yellow `#ffc729`); preheader from `Model.Subject`. Per `contracts/email-design-system.md`.
- [X] T006 [P] Create partial `src/FundingPlatform.Web/Views/Emails/Shared/_BrandHeader.cshtml` (hosted logo `<img>`, Spanish alt).
- [X] T007 [P] Create partial `Shared/_PartnerFooter.cshtml` (partner strip image + support email + `+506 4600-1234` + automatic-message note); retire/replace `_SupportFooter.cshtml`.
- [X] T008 [P] Create partial `Shared/_CtaButton.cshtml` — bulletproof (VML for Outlook) teal button; renders **only** when a URL is supplied and ALWAYS emits a plain-text fallback link (FR-005).
- [X] T009 [P] Create partial `Shared/_StatusCard.cshtml` (the "Detalle" card; wraps long values, no overflow).
- [X] T010 [P] Create partial `Shared/_DetailList.cshtml` (key/value rows used inside `_StatusCard`).
- [X] T011 [P] Create partial `Shared/_Hero.cshtml` (brand-teal semantic `<h1>` title block).
- [X] T012 [P] Unit test design-system invariants in `tests/FundingPlatform.Tests.Unit/Notifications/` — `_CtaButton` emits nothing when URL empty and emits button+fallback when set; assert no near-black `#1d1d1f` and a partner-footer marker are present after a representative render.

**Checkpoint**: Shared shell + partials render; brand image URLs resolve. User stories can begin.

---

## Phase 3: User Story 1 — Branded design system applied to every email (Priority: P1) 🎯 MVP

**Goal**: Every existing email renders in the Programa Semilla brand shell with ALIA voseo copy, partner footer, and preserved variables.

**Independent Test**: Trigger each existing notification and inspect in smtp4dev — branded shell, logo, partner strip, ALIA naming, "Equipo Programa Semilla" sign-off, every variable intact, legible with images blocked.

### Tests for User Story 1

- [X] T013 [P] [US1] E2E mail-capture sweep in `tests/FundingPlatform.Tests.E2E/Notifications/` asserting the brand shell (logo header marker, partner-footer marker, teal CTA, ALIA naming) on representative redesigned emails: submitted-applicant, approved, an appeal message, a stage reminder, the invitation.

### Implementation for User Story 1

- [X] T014 [P] [US1] Refactor application-lifecycle outbox bodies + `.text` twins to compose `_Hero`/`_StatusCard`/`_DetailList`/`_CtaButton` and apply ALIA reference copy (#3/#5/#6/#10): `ApplicationSubmittedApplicant`, `ApplicationSubmittedReviewer`, `ApplicationApproved`, `ApplicationRejected`, `ReturnedToApplicant`, `ResubmittedByApplicant`, `ApplicationWithdrawnByApplicant` in `src/FundingPlatform.Web/Views/Emails/`.
- [X] T015 [P] [US1] Refactor appeal bodies + `.text` twins (`AppealOpenedReviewer`, `AppealMessageReviewer`, `AppealMessageApplicant`, `AppealResolvedApplicant`, `AppealReopenedReviewer`) — light-polish into ALIA voseo, preserve `OutcomeCode` branching + variables.
- [X] T016 [P] [US1] Refactor agreement / signed-upload / response bodies + `.text` twins (`AgreementGeneratedApplicant`, `AgreementExecutedApplicant`, `SignedUploadSubmittedReviewer`, `SignedUploadReplacedReviewer`, `SignedUploadWithdrawnReviewer`, `SignedUploadRejectedApplicant`, `ResponseSubmittedReviewer`).
- [X] T017 [P] [US1] Rebrand identity emails through the shared shell: convert `ForgotPasswordEmailFactory` and `InvitationEmailFactory` (`src/FundingPlatform.Infrastructure/Email/`) from plain-text token substitution to `IEmailViewRenderer` + an `IdentityEmailModel`; rewrite `Views/Emails/Identity/InvitationEmail.cshtml` (ALIA "Bienvenida" copy #1) and `ForgotPasswordEmail.cshtml` to compose partials; add `.text.cshtml` twins.
- [X] T018 [P] [US1] Rebrand stage emails: update `StageReminderEmailFactory` to render `Views/Emails/Stages/{T24,T72}ReminderEmail.cshtml` + `ExpiredEmail.cshtml` through the shared shell (preserve `{{PublicCode}}`/`{{StageName}}`/`{{ClosesAtLocal}}`/`{{ApplicantName}}`); add `.text.cshtml` twins.
- [X] T019 [P] [US1] Rebrand `Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml` (+ `.text`) using `_StatusCard`/`_DetailList` ("Detalle" of the provider).
- [X] T020 [US1] Sweep every refactored email for ALIA naming in body copy + "Equipo Programa Semilla" sign-off consistency; ensure no English literals remain (NFR-007).
- [X] T021 [US1] Verify `.text.cshtml` twin parity (FR-009): each refactored HTML email has a present, in-sync plain-text twin conveying the same meaning + variables.

**Checkpoint**: All existing emails are branded and shippable as the MVP, independent of US2–US4.

---

## Phase 4: User Story 2 — "Solicitud en revisión" applicant notice (Priority: P2)

**Goal**: The applicant gets one branded email when their application enters review.

**Independent Test**: Submit as applicant → open as reviewer → exactly one "Tu solicitud está en revisión" to the applicant; reviewer re-opens → no duplicate.

### Tests for User Story 2

- [X] T022 [P] [US2] Integration test in `tests/FundingPlatform.Tests.Integration/Notifications/` — recipient matrix for `ApplicationUnderReviewApplicant` is **applicant only** (reviewer + admins excluded), and the worker is idempotent on a second pass (reviewer re-open does not duplicate).
- [X] T023 [P] [US2] E2E mail-capture in `tests/FundingPlatform.Tests.E2E/Notifications/` — submit → reviewer opens application → one applicant email captured; re-open → still one.

### Implementation for User Story 2

- [X] T024 [US2] Add `NotificationEvent.ApplicationUnderReviewApplicant` (+ `ToStorageString`/`FromStorageString` → `APPLICATION_UNDER_REVIEW_APPLICANT`) in `src/FundingPlatform.Domain/Notifications/NotificationEvent.cs`.
- [X] T025 [US2] Add the binding (subject `Tu solicitud está en revisión — Solicitud #{ApplicationId}`, views `ApplicationUnderReviewApplicant`(+`.text`), CTA `/Application/Details/{id}`) in `src/FundingPlatform.Application/Notifications/Templates/NotificationTemplateBindings.cs`.
- [X] T026 [US2] Add recipient-bucket switch cases in `src/FundingPlatform.Infrastructure/Notifications/Resolvers/NotificationRecipientResolver.cs`: applicant → true, reviewer → false, admin → false (applicant-only; avoids admin noise on routine reviewer page-opens).
- [X] T027 [US2] Enqueue at the transition in `src/FundingPlatform.Application/Services/ReviewService.cs` (`GetApplicationForReviewAsync`): when `StartReview()` actually transitions `Submitted→UnderReview`, add a `VersionHistory(reviewerUserId, "StartReview", …)` row, build `NotificationPayload(ActorUserId = reviewerUserId)`, call `EnqueueAsync(ApplicationUnderReviewApplicant, …, vhRow.Id, …)` before `SaveChangesAsync`. Guard so re-opens (already `UnderReview`) do not enqueue.
- [X] T028 [P] [US2] Create `Views/Emails/ApplicationUnderReviewApplicant.cshtml` + `.text.cshtml` (ALIA reference copy #4; compose partials; CTA to the application).

**Checkpoint**: New under-review email sends exactly once at the real transition.

---

## Phase 5: User Story 3 — "Tu contraseña fue actualizada" confirmation (Priority: P2)

**Goal**: A user gets a branded confirmation after any successful password set/change/reset.

**Independent Test**: Complete a password reset/change → exactly one branded confirmation email to that user; no CTA button (no link variable).

### Tests for User Story 3

- [X] T029 [P] [US3] E2E mail-capture in `tests/FundingPlatform.Tests.E2E/` — drive the reset flow to success and assert one "Tu contraseña fue actualizada" email (branded shell, no CTA button, support phone present).

### Implementation for User Story 3

- [X] T030 [P] [US3] Create `PasswordChangedEmailFactory` in `src/FundingPlatform.Infrastructure/Email/` (mirrors `ForgotPasswordEmailFactory`; subject "Tu contraseña fue actualizada"; renders via `IEmailViewRenderer`).
- [X] T031 [P] [US3] Create `Views/Emails/Identity/PasswordChangedEmail.cshtml` + `.text.cshtml` (reference copy #2; voseo; advise contacting support if not them; **no CTA** per FR-005; support phone `+506 4600-1234`).
- [X] T032 [US3] Send the confirmation at reset success in `src/FundingPlatform.Infrastructure/Identity/ConsumePasswordResetTokenHandler.cs` (covers forgot-password and spec-033 invite first-set), best-effort (catch+log, never block).
- [X] T033 [US3] Send the confirmation at change success in `src/FundingPlatform.Web/Controllers/AccountController.cs` (`ChangePassword` and `ProfileChangePassword`), best-effort.

**Checkpoint**: Password-changed confirmation fires at all set/change/reset success points.

---

## Phase 6: User Story 4 — "Nueva empresa para revisión" stub (Priority: P3) — ❌ WITHDRAWN 2026-06-20

> **WITHDRAWN (OQ-1 resolved).** The intended scenario ("applicant adds a new supplier → notify auditors") is already shipped by spec 038's `IProviderCreatedNotifier`, so this US4 stub was a **duplication**. All US4 artifacts (T034–T036: `ICompanyForReviewNotifier`/`CompanyForReviewNotifier`, `CompanyForReviewAuditor.cshtml` + `.text` twin, `CompanyForReviewNotifierTests`, DI registration, design-system twin-list entry) were **deleted**. Group-scoping of auditors was declined. See `spec.md` Evolution Log (2026-06-20).

**Goal** (original, withdrawn): A branded template + notifier seam exist; no live trigger until OQ-1 is resolved.

**Independent Test** (original, withdrawn): Render-test the template with sample company data; confirm no live notification is emitted anywhere.

### Tests for User Story 4

- [X] T034 [P] [US4] Render test in `tests/FundingPlatform.Tests.Integration/Notifications/` (or Unit) — `CompanyForReviewAuditor` renders in the brand shell with a populated "Detalle" card (company name / identificación / fecha); assert no enqueue/notifier call site is wired (grep-style guard or absence-of-trigger assertion).

### Implementation for User Story 4

- [X] T035 [P] [US4] Add `ICompanyForReviewNotifier` (Application, `src/FundingPlatform.Application/Suppliers/Notifications/`) + a stub impl (Infrastructure) mirroring `IProviderCreatedNotifier`/`ProviderCreatedNotifier`; **no call site** (trigger/recipient deferred to OQ-1). Document the deferral in a code comment referencing OQ-1.
- [X] T036 [P] [US4] Create `Views/Emails/Suppliers/CompanyForReviewAuditor.cshtml` + `.text.cshtml` (reference copy #9; branded; "Detalle" card via `_StatusCard`/`_DetailList`).

**Checkpoint**: Stub template + seam ready; activating it later is a one-call-site change once OQ-1 lands.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T037 [P] Brand/copy guard test (source-level scan of `Views/Emails/**`): no English literals; no near-black `#1d1d1f` button; partner-footer + support phone present on every HTML email; no external `<link>`/`<style>`.
- [X] T038 [P] Accessibility/compatibility checklist over all templates: Spanish `alt` on every `<img>`, content ≤600px, no flexbox/grid, WCAG AA teal-text contrast, no critical content image-only (NFR-001..005).
- [X] T039 Run `quickstart.md` manual verification in smtp4dev with images both shown and blocked across the redesigned + new emails.
- [X] T040 Run filtered E2E (`--filter FullyQualifiedName~Notifications`) plus any affected identity/stage classes; confirm green (delivery gate).
- [X] T041 Record the FR-013 notifier refinement (outbox is application-keyed → notifier pattern) in the spec's evolution note, and reconfirm OQ-1/OQ-2/OQ-3 resolutions in `research.md` are reflected.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: depends on Setup; **blocks all user stories** (shell + partials + `IEmailViewRenderer` + brand image URLs).
- **US1 (P3)**: depends on Foundational. The MVP.
- **US2 (P4)**, **US3 (P5)**, **US4 (P6)**: each depends on Foundational; their new templates compose the shared partials. Independent of US1's existing-template refactor and of each other.
- **Polish (P7)**: after the desired stories are complete.

### Within Each User Story

- New `NotificationEvent` (T024) → binding (T025) → resolver (T026) → enqueue (T027); template (T028) parallel to wiring.
- Integration/E2E tests assert behavior; per Constitution III the filtered E2E must be green before a story is "done".

### Parallel Opportunities

- Foundational partials T006–T011 are all `[P]` (different files).
- US1 template-family refactors T014–T019 are `[P]` (different files); T013 test `[P]`.
- US2 template T028 `[P]` with the wiring tasks; US3 factory/template T030/T031 `[P]`.
- US2/US3/US4 can be worked in parallel by different developers once Foundational is done.

---

## Parallel Example: User Story 1

```bash
# After Foundational, launch the template-family refactors together:
Task: "T014 Refactor application-lifecycle bodies + text twins"
Task: "T015 Refactor appeal bodies + text twins"
Task: "T016 Refactor agreement/signed-upload/response bodies + text twins"
Task: "T017 Rebrand identity emails via shared shell"
Task: "T018 Rebrand stage emails via shared shell"
Task: "T019 Rebrand supplier ProviderCreatedAuditor"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational (shell + partials).
2. Phase 3 US1 — rebrand every existing email.
3. **STOP & VALIDATE** in smtp4dev + the T013 E2E sweep. This alone delivers the brand-lift goal and is shippable.

### Incremental Delivery

1. Foundational ready.
2. US1 (brand lift) → validate → demo (MVP).
3. US2 (under-review) → validate → demo.
4. US3 (password-changed) → validate → demo.
5. US4 (company-for-review stub) → render-test → demo; activate later when OQ-1 lands.

---

## Notes

- `[P]` = different files, no incomplete-task dependency.
- Keep each HTML email's `.text.cshtml` twin in sync (FR-009) — treat as part of the same task.
- Commit after each task or logical group (Constitution commit discipline).
- No schema change, no new managed dependency, no build step — if any task seems to require one, stop and reconcile with the plan.
- OQ-1 (FR-013 trigger/recipient) stays open; US4 intentionally ships without a live trigger.
