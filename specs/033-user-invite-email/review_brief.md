# Review Brief: User invitation / set-password onboarding email

**Spec:** specs/033-user-invite-email/spec.md
**Generated:** 2026-06-12

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Builds on spec 032 (admin-only user creation). Today an admin types a temporary password on the create form and relays it to the new user out-of-band. This feature replaces that: the admin creates the account **without** a password, and the new user receives an es-CR email with a **single-use, 72-hour set-password link**. They click it, choose their own password, and sign in — no credential relay. Admins can resend an invitation, and the post-creation confirmation also shows a copyable link so onboarding survives mail filtering/outages.

## Scope Boundaries

- **In scope:** removing the create-form password field; issuing + emailing a 72h single-use invite link to all admin-created roles; the set-password landing (reuses `/Account/ResetPassword`); resend (supersedes prior link); an admin-visible copyable link fallback; es-CR copy.
- **Out of scope:** the user-initiated forgot-password flow (unchanged); retiring the existing admin temp-password "reset password" action (kept, though now overlapping); bulk/CSV invites and scheduled reminders.
- **Why:** keep it a focused, additive reuse of the existing token + email seams.

## Critical Decisions

### Invite link *replaces* the temp password (not additive)
- **Choice:** the admin no longer sets any password; the invite link is the only way in.
- **Trade-off:** cleanest UX and kills the manual relay, but there's **no temp-password fallback** if the email is missed — mitigated by the admin-visible copyable link (FR-008).
- **Feedback:** is removing the temp-password fallback entirely acceptable, given the copyable-link mitigation?

### Admin-visible copyable invite link
- **Choice:** show the link to the admin on the create/resend confirmation.
- **Trade-off:** robust against dropped/filtered email, but the admin can see/use the link (they can set the user's password). The admin created the account, so they're already trusted.
- **Feedback:** acceptable, or should it be email-only (and instead rely on adding real domains to the non-prod allowlist)?

### 72-hour, single-use, supersede-on-resend
- **Choice:** longer than the 60-min forgot-password token; resend invalidates the prior unused link.
- **Feedback:** is 72h the right window?

## Areas of Potential Disagreement

### Coexistence with the existing temp-password reset action
- **Decision:** leave the admin "reset password (type a temp password)" action as-is.
- **Why this might be controversial:** after this feature, that action is inconsistent with the new invite model and overlaps with resend-invite.
- **Alternative view:** convert it to a link-based flow now for coherence.
- **Seeking input on:** defer to a follow-up, or fold the conversion into this spec?

### Reuse vs. new token
- **Decision (deferred to planning):** likely reuse the existing password-reset token (parameterized to 72h) vs. a separate invitation token.
- **Seeking input on:** any preference, given the "resend supersedes prior unused" requirement?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| The credential | Invitation token | Single-use, 72h, onboarding purpose; sibling of the existing password-reset token |
| Landing page | `/Account/ResetPassword` (reused) | Set-password page the invite link targets |
| Admin action | Resend invitation | On the user list/edit |

## Open Questions

- [ ] Reuse the existing reset token (72h-parameterized) or a dedicated invitation token?
- [ ] Delivery path: direct-send (like `ForgotPasswordEmail`) vs. the spec-021 outbox?
- [ ] Should resend be hidden once a user has onboarded, or always available?

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Invite email dropped (non-prod allowlist) → user can't onboard (no temp-password fallback) | High | FR-008 admin-visible copyable link; Development `SeedUser` seam bypasses email for tests |
| E2E ripple: removing the create-form password breaks admin-create-then-login tests | Med | Tests traverse the invite flow; `SeedUser` keeps password-based bootstrap |
| Admin-visible link exposes the set-password capability to the admin | Low | Admin already created the account; deliberate, documented trade-off |

---
*Share with reviewers before implementation.*
