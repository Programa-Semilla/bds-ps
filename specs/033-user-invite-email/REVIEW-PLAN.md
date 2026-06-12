# Review Guide: User invitation / set-password onboarding email

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-12

---

## What This Spec Does

Spec 032 made user creation admin-only but left the onboarding hand-off manual: the admin types a temporary password and relays it. This replaces that with a standard **invite flow** — the admin creates an account with no password, the user gets an es-CR email with a 72-hour single-use set-password link, sets their own password, and signs in. Admins can resend; the confirmation also shows a copyable link so onboarding survives mail filtering.

**In scope:** removing the create-form password; issuing + emailing a 72h single-use invite (reusing the password-reset token flow); the resend action (supersedes the prior link); the admin-visible copyable-link fallback; es-CR copy.

**Out of scope:** the user-initiated forgot-password flow (unchanged); retiring the existing admin temp-password "reset password" action; bulk invites / reminders.

## Bigger Picture

This is a thin reuse layer over machinery the project already has: spec 021 built the email sender + `ForgotPasswordEmail` direct-send pattern; the password-reset token (`PasswordResetToken` + issue/consume handlers + `/Account/ResetPassword`) already does token→set-password→clear-`MustChangePassword`. The plan adds **one constant** (72h), **one store method** (invalidate-unused), **one command parameter pair** (Ttl + supersede), an email factory+template, and the admin confirmation/resend UI — and reuses everything else. The headline consequence is **no schema change** ([D8](research.md#d8-schema-impact)).

---

## Code Review Guide is N/A yet (planning phase)

> This is the post-planning review. The code-review guide is appended after implementation.

## Spec/Plan Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [research D1](research.md#d1-reuse-the-password-reset-token-vs-a-new-invitation-token) and [D4/D5](research.md#d4-no-usable-password-technique). As you read:

- The invite **is** a password-reset token with a longer life and a supersede step. Does reusing the reset flow wholesale feel right, or does an "invitation" deserve its own concept/table for clarity even at the cost of duplication? ([D1](research.md#d1-reuse-the-password-reset-token-vs-a-new-invitation-token))
- Accounts are created with **no password** (`CreateAsync(user)`), and invited users are **not** flagged `MustChangePassword`. Any concern with a passwordless account existing between create and first link-use? ([D4](research.md#d4-no-usable-password-technique))

### Key decisions that need your eyes (12 min)

**Direct-send, not the spec-021 outbox** ([D5](research.md#d5-delivery-path-direct-send-not-the-spec-021-outbox), [tasks T016](tasks.md#phase-3-user-story-1--new-user-onboards-via-an-emailed-invitation-priority-p1--mvp))
- The invite — the *only* way in — is sent best-effort from the controller with no retry/outbox durability. The mitigation is the admin-visible link + resend, not delivery retry. Question: is best-effort + admin-visible-link acceptable for the sole onboarding channel, or should the invite ride a durable queue?

**Supersede scoping** ([D3](research.md#d3-supersede-on-resend-fr-007), [T004/T005](tasks.md#phase-2-foundational--token-ttlsupersede--invite-email-blocks-us1--us2))
- `InvalidatePriorUnused` is set only on the invite path; forgot-password keeps multi-token behavior. Question: should forgot-password *also* supersede prior tokens (arguably better security), or is leaving it untouched the right blast-radius?

**Admin-visible copyable link** ([FR-008](spec.md#functional-requirements), [T015](tasks.md#phase-3-user-story-1--new-user-onboards-via-an-emailed-invitation-priority-p1--mvp))
- The admin sees a link that can set the user's password. They created the account, so they're trusted — but is showing it on-screen (vs. email-only) the right default? The alternative is relying on the non-prod allowlist config.

**No-password-fallback model** ([spec US3](spec.md#user-story-3---onboarding-works-even-when-email-cant-be-delivered-priority-p2))
- There's no temp-password safety net anymore. If both the email *and* the copyable link are lost, recovery is admin resend. Acceptable?

### Areas where I'm less certain (5 min)

- [T019](tasks.md#phase-3-user-story-1--new-user-onboards-via-an-emailed-invitation-priority-p1--mvp): the two first-login change-password tests assert the *replaced* temp-password model — I plan to rewrite them to the invite flow, but "rewrite vs. remove" depends on whether the change-password page retains any non-invite purpose (it still serves the admin temp-password reset action, which stays). Worth a reviewer eye on which behavior to keep asserting.
- [D2](research.md#d2-ttl-parameterization-72h-for-invites-60min-for-forgot-password--two-gates) — **resolved during this review**: the consume path validates Identity's DataProtector crypto token *in addition* to our row, and that token's lifetime is the global `DataProtectionTokenProviderOptions.TokenLifespan`, currently **60 min** (`Program.cs:79`). Left as-is the invite would die at 60 min, not 72h. Fix added as task T005b (bump the global to 72h); safe because the per-row TTL stays the stricter gate (forgot-password unchanged) and password-reset is the only DataProtector-token consumer. Reviewer question: comfortable raising the global token lifespan, or would you prefer a dedicated named invite provider?

### Deviations and risks (5 min)

- **E2E onboarding rewrite** ([T017/T019](tasks.md#phase-3-user-story-1--new-user-onboards-via-an-emailed-invitation-priority-p1--mvp)): removing the create-form password breaks admin-create-then-login tests (echoes spec 032's D-2). Mitigated by a confirmation-link→set-password→login helper + `SeedUser` keeping password bootstrap. Question: is landing that helper early (before the broad rewrite) the right sequencing?
- **Risk — Identity provider token vs. row TTL** (above): if they disagree, 72h isn't really 72h. Flagged for planning verification.
- No deviations from [plan.md](plan.md) structure; the reuse-shaped design matches the spec's stated dependencies.
