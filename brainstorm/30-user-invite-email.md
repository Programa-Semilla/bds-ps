# Brainstorm: User invitation / set-password onboarding email

**Date:** 2026-06-12
**Status:** spec-created
**Spec:** specs/033-user-invite-email/

## Problem Framing

Spec 032 made user creation admin-only and removed public registration — but it left the onboarding hand-off entirely manual: the admin types a temporary password on the create form, relays it out-of-band, and the user changes it at first login. The admin "reset password" action is likewise temp-password-based. No admin-side flow emails a set-password link (the tokenized-link flow exists only for user-initiated forgot-password). The user asked whether admin-created users get a welcome email — they don't — and wanted to close that gap with a proper invitation.

Recon confirmed all the building blocks already exist: the password-reset token (`IIssuePasswordResetTokenHandler` → 60-min token → `/Account/ResetPassword` → `IConsumePasswordResetTokenHandler` sets password + clears `MustChangePassword`), the direct-send tokenized-email pattern (`ForgotPasswordEmailFactory` + `IEmailSender`, sent synchronously from the controller — it bypasses the spec-021 outbox), and the spec-021 email sender/branding. The spec-021 outbox is application-shaped (requires ApplicationId + a recipient resolver), so it's a poor fit for a single-recipient tokenized invite.

## Approaches Considered

### A: Invite link replaces the temp password (chosen)
- Admin sets no password; the invite link is the only way in. Pros: kills the manual relay; modern "invite" UX. Cons: no temp-password fallback if email is missed (mitigated by an admin-visible copyable link); removes the create-form password field (E2E ripple).

### B: Welcome email + keep the temp password
- Additive: admin still sets a temp password; the email adds a set-password link. Pros: lower risk, fallback preserved. Cons: doesn't fully kill the relay; two onboarding paths.

### C: Notify-only (no link)
- Just "your account was created." Cons: doesn't solve the relay problem. Rejected.

### Delivery: direct-send vs. outbox
- Lean direct-send (like `ForgotPasswordEmail`) — the invite is one known recipient with a token and no application context; forcing it through the application-shaped outbox needs resolver + payload changes. Deferred to planning.

## Decision

Ship as spec **033-user-invite-email** (Approach A):
- Admin create collects **no password**; on creation, issue a **single-use, 72-hour** set-password token and email an es-CR invite link (→ `/Account/ResetPassword`). **All roles**. Invite-created users are not `MustChangePassword`-flagged (they choose their own password).
- **Resend** invitation from the admin list/edit; resend issues a fresh 72h link and **invalidates the prior unused** one.
- **Admin-visible copyable link** on the create/resend confirmation as the delivery-resilience fallback (non-prod allowlist / mail outages).
- Single-use; expired/used/invalid → es-CR rejection.

Spec review: **SOUND** (REVIEW-SPEC.md), no critical/important issues. Constitution-aligned.

## Open Threads

- Reuse the existing password-reset token (parameterized to 72h) vs. a dedicated invitation token — pin in plan; "resend supersedes prior unused" (FR-007) is the key constraint either way.
- Delivery path: direct-send (`ForgotPasswordEmail` pattern) vs. spec-021 outbox — pin in plan; leaning direct-send.
- "No usable password" technique (create without a password vs. random unusable hash) — pin in plan.
- Whether to retire/convert the existing admin temp-password "reset password" action for coherence — deliberately out of scope; future pass.
- E2E ripple: admin-create-then-login tests must traverse the invite flow; the Development `SeedUser` seam keeps password-based bootstrap — pin the test strategy in plan.
- Whether resend is hidden once a user has onboarded, or always available — defer to plan.
