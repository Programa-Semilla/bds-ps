# Contract: Identity / Direct-Send Emails

**Feature**: 041-email-brand-lift

Direct-send emails (no outbox) rendered through the **shared** `_EmailLayout` so they match the outbox emails (research Decision 1). A reusable `IEmailViewRenderer.RenderViewAsync(viewPath, model, disableLayout)` (generalized out of `RazorEmailRenderer`) renders these Razor views to HTML + text. Each factory owns its subject and builds an `IdentityEmailModel`.

## New: Password-changed confirmation (FR-010/FR-012/FR-014)

| Field | Value |
|---|---|
| Factory | `PasswordChangedEmailFactory` (mirrors `ForgotPasswordEmailFactory`) |
| View | `Views/Emails/Identity/PasswordChangedEmail.cshtml` + `.text.cshtml` |
| Subject | `Tu contraseña fue actualizada` |
| Body (reference #2) | Confirms the change succeeded; advises contacting support (`+506 4600-1234` / support email) if the recipient did not make it. Voseo. **No CTA button** (no link variable ⇒ FR-005: omit button + fallback). |
| Recipient | the affected user (`user.Email`, `user.FirstName`) |
| Delivery | best-effort via direct-send `IEmailSender`; failure logged, never blocks the password operation |

### Send sites (all password set/change success points)
1. `ConsumePasswordResetTokenHandler` after `ResetPasswordAsync` succeeds (covers forgot-password **and** spec-033 invite first-set).
2. `AccountController.ChangePassword` after `ChangePasswordAsync` succeeds.
3. `AccountController.ProfileChangePassword` after `ChangePasswordAsync` succeeds.

Invite-first-set also sends the confirmation (accepted; research Decision 4).

## Rebranded existing direct-send emails (FR-010)

| Email | View | Notes |
|---|---|---|
| Invitation | `Identity/InvitationEmail.cshtml` | reference #1 "Bienvenida a ALIA" copy refresh; keeps `{{InviteLink}}`/`{{ExpiresAt}}` semantics → CTA button + fallback link. |
| Forgot password | `Identity/ForgotPasswordEmail.cshtml` | rebrand only; reset link → CTA + fallback. |
| Stage reminders | `Stages/T24ReminderEmail.cshtml`, `T72ReminderEmail.cshtml`, `ExpiredEmail.cshtml` | rebrand; preserve `{{PublicCode}}`/`{{StageName}}`/`{{ClosesAtLocal}}`/`{{ApplicantName}}` data; CTA only if a link variable exists. |
| Provider created (auditor) | `Suppliers/ProviderCreatedAuditor.cshtml` | rebrand; "Detalle" card. |

## Invariants
- Every redesigned email has a synced `.text.cshtml` twin (FR-009).
- All dynamic tokens/variables preserved 1:1 (FR-008; SC-002 zero-variable-loss).
- No invented URLs (FR-005; SC-003).
- Rendering identity/stage emails through `_EmailLayout` must not regress their best-effort send semantics (a render exception must be caught/logged, never throw into the password/invite flow).
