# Phase 0 Research: ALIA Transactional Email Brand UI-Lift

**Feature**: 041-email-brand-lift
**Date**: 2026-06-19

This document resolves the spec's open questions and the technical unknowns, grounded in the existing email subsystem (specs 021 / 028 / 033 / 037 / 038).

## Email subsystem map (baseline facts)

Two distinct delivery paths exist and both are in scope:

1. **Outbox path** (spec 021/028) — the application-lifecycle emails (~20 templates).
   - `NotificationOutbox` rows → `EmailDispatchWorker` → `IEmailTemplateRenderer` (`RazorEmailRenderer`) → `IEmailSender` (Mailgun/Mailtrap/NoOp) with the non-prod allowlist decorator.
   - `RazorEmailRenderer` (`src/FundingPlatform.Web/Services/RazorEmailRenderer.cs`) renders `~/Views/Emails/{HtmlViewName}.cshtml` (through `_ViewStart`→`_EmailLayout`) **and** `~/Views/Emails/{TextViewName}.text.cshtml` (`Layout=null`). Model is `EmailRenderModel(EventType, Recipient, Payload, Subject, CtaUrl, SenderName, SenderEmail)`.
   - Per-event metadata lives in `NotificationTemplateBindings` (`src/FundingPlatform.Application/Notifications/Templates/`): `(SubjectTemplate, HtmlViewName, TextViewName, TemplateVariantKey, CtaRouteTemplate)`. A unit test (`Every_enum_value_has_a_binding`) enforces totality.
   - Recipient buckets resolved in `NotificationRecipientResolver` (`IncludesApplicantBucket`/`IncludesReviewerBucket`/`IncludesAdminBucket` switches) + actor exclusion via `NotificationPayload.ActorUserId`.
   - Idempotency: unique filtered index `UX_NotificationDelivery_DedupKey` on `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`. **The payload is application-keyed** — `NotificationPayload.ApplicationId` is required and is part of the dedup key.

2. **Direct-send path** (spec 021/033) — identity + stage emails.
   - Factories in `src/FundingPlatform.Infrastructure/Email/`: `InvitationEmailFactory`, `ForgotPasswordEmailFactory`, `StageReminderEmailFactory`. They read a `.cshtml` token template **as plain text** (not Razor-rendered), substitute `{{Token}}`s, and return `EmailMessage(ToAddress, Subject, HtmlBody)` sent best-effort via the *direct-send* `IEmailSender` (`src/FundingPlatform.Application/Abstractions/IEmailSender.cs`).
   - Templates: `Views/Emails/Identity/{Invitation,ForgotPassword}Email.cshtml`, `Views/Emails/Stages/{T24,T72}ReminderEmail.cshtml` + `ExpiredEmail.cshtml`.

3. **Provider notifier path** (spec 038) — `IProviderCreatedNotifier`/`ProviderCreatedNotifier` (Application interface + Infrastructure impl) sends the auditor "nuevo proveedor registrado" email. This is a **non-application-keyed** notifier — the correct template for any "a new entity was registered" email.

Brand assets already exist in `wwwroot/lib/brand/` (spec 037): `programa-semilla-horizontal.png`, `programa-semilla-vertical.png`, `programa-semilla-icon.png`, `partners-footer.png`, `pdf/footer-partners-strip.png`. `wwwroot` is served anonymously via `MapStaticAssets()`. `Notifications:BaseUrl` is already read by `RazorEmailRenderer` to compose absolute CTA links (`Combine(baseUrl, path)`).

---

## Decision 1 — Single design-system shell shared by BOTH render paths

**Decision**: Build the brand design system as Razor partials that are the single source of truth for brand chrome, and route **all** in-scope emails (outbox + identity + stage) through the shared `_EmailLayout`. Concretely:
- Rebuild `_EmailLayout.cshtml` as the 600px table-based, inline-CSS branded shell (logo header, body region, sign-off, partner-strip footer, support footer).
- Extract reusable partials: `_BrandHeader`, `_PartnerFooter` (replaces/extends `_SupportFooter`), `_CtaButton`, `_StatusCard` (the "Detalle" card), `_DetailList`, `_Hero`.
- Generalize the existing Razor-view-to-string capability in `RazorEmailRenderer` into a reusable `IEmailViewRenderer.RenderViewAsync(viewPath, model, disableLayout)` so the **direct-send factories render real Razor views through `_EmailLayout`** instead of plain-text token substitution.

**Rationale**: The feature's core value is *consistency* — "one place to change the brand." If identity/stage emails kept their own embedded brand HTML, the chrome would be duplicated and drift. A shared partial set keeps the brand canonical (mirrors how spec 037 token-drove the web UI and how the PDF lift shares header/footer images).

**Alternatives considered**:
- *Token-substitution identity templates with embedded branded HTML* — least refactor, but duplicates the header/partner-footer markup across ≥6 templates → guaranteed drift. Rejected.
- *Two separate shells (outbox vs identity)* — also duplicates brand chrome. Rejected.

**Consequence**: The direct-send factories change from "read text + substitute tokens" to "render a Razor view with a small per-email model." Each identity/stage email gets a tiny view model (recipient name, the link, expiry, brand image URLs). Subjects stay owned by the factories.

---

## Decision 2 — Brand image URLs composed from `Notifications:BaseUrl`

**Decision**: Reference brand images by **absolute https URL** built from `Notifications:BaseUrl` + `/lib/brand/...`, reusing the existing `Combine()` helper. Add `LogoUrl` and `PartnerStripUrl` (and any others) to `EmailRenderModel` (outbox) and to the identity/stage view models. Templates render `<img src="@Model.LogoUrl" alt="Programa Semilla" ...>` with Spanish alt text; every image is decorative-with-alt so the email is fully legible when blocked (NFR-004).

**Rationale**: `wwwroot` is anonymous-served; `BaseUrl` is already the configured public origin used for CTA deep links. No new static path, no asset import needed. (OQ-3 resolved: reuse `wwwroot/lib/brand/`.)

**Asset check (implementation-time)**: confirm `wwwroot/lib/brand/partners-footer.png` shows the intended 5-partner set (Banca para el Desarrollo / CROCUS / nexo / De la Mano con su PYME / Programa Semilla) matching the seed `seeds/emails/Fooder-general.png`. If it differs, add the seed strip as `wwwroot/lib/brand/email-partners-footer.png` and reference that. Header logo: use `programa-semilla-horizontal.png`.

**Alternatives**: CID-embedded / base64 images (bloat, client quirks) — rejected per spec. Importing `seeds/emails/*.png` into a new `/email-assets/` path — unnecessary since official assets already exist. Rejected.

---

## Decision 3 — FR-011 "Solicitud en revisión" is a real, non-redundant outbox event

**Decision**: Add a new outbox event `ApplicationUnderReviewApplicant` (storage `APPLICATION_UNDER_REVIEW_APPLICANT`), applicant-bucket, fired at the `Submitted → UnderReview` transition. CTA route `/Application/Details/{id}`.

**Finding (OQ-2 resolved)**: `Application.Submit()` → `Submitted`; a **distinct** `Application.StartReview()` → `UnderReview` is called lazily in `ReviewService.GetApplicationForReviewAsync` when a reviewer first opens the application. It is NOT redundant with the submission receipt — it tells the applicant a reviewer has begun review.

**Implementation note**: `StartReview()` does not currently write a `VersionHistory` row. The outbox idempotency key needs a `VersionHistoryId`, so the transition must add a `VersionHistory(reviewerUserId, "StartReview", …)` row (the `"StartReview"` action code already exists in `StageMappingProvider`/`ActivityActionCopy`) and enqueue with that row's id. The enqueue + VH-add + save happen in `ReviewService` only when the state actually changes (guarded by the `Submitted` check that already gates `StartReview`), so a reviewer re-opening the page does not re-enqueue; the dedup key is a backstop. No schema change (the `VersionHistory` table exists).

**Recipient**: applicant only (admins optional — default to applicant + admins per the spec-028 convention; confirm in plan). Actor = the reviewer (excluded automatically; the reviewer is not the applicant anyway).

---

## Decision 4 — FR-012 "Tu contraseña fue actualizada" is a direct-send identity email

**Decision**: Add a `PasswordChangedEmailFactory` (mirrors `ForgotPasswordEmailFactory`) + `Views/Emails/Identity/PasswordChangedEmail.cshtml`, rendered through the shared `_EmailLayout` (Decision 1), sent best-effort via the direct-send `IEmailSender`. Fire it at every password-reset/change success point.

**Finding**: Success points are (a) `ConsumePasswordResetTokenHandler` after `ResetPasswordAsync` succeeds (covers forgot-password **and** the spec-033 invite first-set, since both flow through `/Account/ResetPassword`), (b) `/Account/ChangePassword` after `ChangePasswordAsync`, (c) `/Profile/ChangePassword`. `user.Email`, `user.FirstName`, `user.LastName` are available at each.

**Decision on invite-first-set**: send the confirmation on invite-set too. "Tu contraseña fue actualizada / si no reconocés esta acción, contactá a soporte" is a correct, security-valuable message even on first set, and avoids threading a "was-invite" flag through `ConsumePasswordResetTokenHandler` (which doesn't distinguish today). Simplicity (Constitution VI) over a marginal copy nicety. Noted as a reversible choice.

**Rationale**: Mirrors the proven `InvitationEmailFactory`/`ForgotPasswordEmailFactory` pattern; no outbox row (no `ApplicationId` exists for a password event); no schema change.

---

## Decision 5 — FR-013 "Nueva empresa para revisión" follows the provider-notifier pattern, trigger deferred

**Decision**: Build the branded `Views/Emails/...` template + a notifier seam mirroring `IProviderCreatedNotifier`/`ProviderCreatedNotifier`. Do **not** wire a live trigger; render-test only. The trigger + recipient remain deferred (OQ-1).

**Rationale (OQ-1)**: A "new company/entity registered for review" is **not application-keyed**, so it cannot use the application outbox (whose dedup key and payload require `ApplicationId`). The spec-038 `ProviderCreatedNotifier` is the established non-application notifier and the correct pattern to mirror once product confirms the trigger (a newly registered applicant `Company` from spec 037? an auditor-review queue entry?) and recipient (reviewer pool vs auditor). Until then the template + seam exist and are render-tested; no enqueue site is added.

**Spec consequence**: FR-013/FR-014's wording ("new *outbox* event") is refined here — FR-013 is a *notifier*-pattern email, not an outbox event. This is an implementation-detail refinement consistent with the spec's intent (a branded "nueva empresa para revisión" email with deferred trigger); it does not change any FR's observable behavior. Flagged for the spec's evolution log; no spec rewrite required pre-implementation.

---

## Decision 6 — Copy & naming applied uniformly

**Decision**: Apply the reference-file copy (`seeds/emails/Respuestas correo ALIA.txt`) verbatim where it maps; light-polish the rest into the same voseo voice. Platform = "ALIA" in body copy; sign-off block in `_EmailLayout` becomes "Equipo Programa Semilla"; `_PartnerFooter` carries the support email + phone `+506 4600-1234` + "mensaje automático — no respondás" note. The `From:` sender display (`Notifications:Sender:*`) is unchanged (Out of Scope).

**Mapping** (reference → template):
| Reference | Template |
|---|---|
| 1 Bienvenida a ALIA | `Identity/InvitationEmail` (copy refresh) |
| 2 Contraseña actualizada | **new** `Identity/PasswordChangedEmail` |
| 3 Recibimos tu solicitud | `ApplicationSubmittedApplicant` |
| 4 Tu solicitud está en revisión | **new** `ApplicationUnderReviewApplicant` |
| 5 Solicitud aprobada | `ApplicationApproved` |
| 6 Solicitud rechazada | `ApplicationRejected` |
| 7 Estatus actualizado (reviewer) | reviewer-bucket events (voice alignment) |
| 8 Nuevo proveedor registrado | `Suppliers/ProviderCreatedAuditor` |
| 9 Nueva empresa para revisión | **new** stub (Decision 5) |
| 10 Nueva aplicación pendiente | `ApplicationSubmittedReviewer` |

Emails the reference does not cover (returns, resubmission, withdrawal, appeals ×5, agreement generated/executed, signed-upload lifecycle ×4) keep their meaning + variables and are light-polished into the ALIA voice.

---

## Constitution / schema impact

- **No dacpac change.** New outbox event = string-stored (spec-028 pattern); password-changed + FR-013 stub are not outbox events; `VersionHistory` table already exists. (Constitution IV satisfied with zero schema work.)
- **No new managed dependencies, no build step** (NFR-006).
- **Rich Domain Model**: FR-011 reuses the existing `Application.StartReview()` domain transition; the `VersionHistory` add stays in the service layer consistent with the other transitions in `ReviewService`.
- **E2E (non-negotiable)**: mail-capture E2E for the new emails + a representative sweep of redesigned events.

## Open items carried to the plan

- Confirm the existing `partners-footer.png` matches the intended 5-partner set (else add the seed strip). (Decision 2)
- Confirm FR-011 recipient set: applicant-only vs applicant + admins. (Decision 3)
- OQ-1 (FR-013 trigger/recipient) remains a product decision; template + seam ship without a live trigger. (Decision 5)
