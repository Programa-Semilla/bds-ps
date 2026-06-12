# Code Review: User invitation / set-password onboarding email

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md)
**Date:** 2026-06-12
**Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall Score: 100%** (13/13 functional requirements compliant)

- Invitation at creation (FR-001…FR-005): 5/5
- Resend (FR-006, FR-007): 2/2
- Delivery resilience (FR-008): 1/1
- Link lifecycle & validation (FR-009, FR-010): 2/2
- Email content & localization (FR-011, FR-012): 2/2
- Conventions (FR-013): 1/1

Tests: Unit 532/0, Integration 355/0, filtered E2E green (`UserInvitationTests` — incl. all-four-roles + invalid-link — plus 9 rewired classes). SC-001…SC-006 covered. (Counts after the deep-review fix round below.)

No deviations from [plan.md](plan.md). One intentional structural note: the email subject string is owned by `InvitationEmailFactory` (Infrastructure cannot reference `Web.Resources`), mirroring `ForgotPasswordEmailFactory`.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on high-level questions that need human judgment.

**Changed files:** 23 (8 source: 1 domain, 2 application, 4 infrastructure, 1 web-DI; 6 web UI: controller, 2 view-models, 3 views/templates, 1 resources, Program.cs; 9 test files across Unit/Integration/E2E + 3 new E2E artifacts).

### Understanding the changes (8 min)

- Start with [`AdminUsersController.cs`](../../src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs) — `IssueAndSendInvitationAsync` is the heart of the feature (issue token → compose link → send → return link), shared by the `Create` POST and the new `ResendInvitation` POST. This is where the create flow stopped redirecting to the list and started rendering the confirmation.
- Then [`IssuePasswordResetTokenHandler.cs`](../../src/FundingPlatform.Infrastructure/Identity/IssuePasswordResetTokenHandler.cs) + [`PasswordResetTokenStore.cs`](../../src/FundingPlatform.Infrastructure/Identity/PasswordResetTokenStore.cs): how the reused password-reset seam was parameterized (TTL) and extended (supersede) without disturbing forgot-password.
- Question: the whole feature is layered onto the spec-021 password-reset token rather than a new invitation entity. Does that reuse read clearly, or does overloading "password reset" for "invitation" obscure intent at the call sites?

### Key decisions that need your eyes (12 min)

**Direct-send from the controller, not the outbox** ([`AdminUsersController.cs` IssueAndSendInvitationAsync](../../src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs), [FR-002](spec.md#functional-requirements), research [D5](research.md#d5-delivery-path-direct-send-not-the-spec-021-outbox))

The sole onboarding email is sent best-effort; transport failures are caught and logged, and onboarding falls back to the admin-visible copyable link (FR-008). There is no retry/durability.
- Question: is best-effort + admin-visible-link acceptable for the *only* way a user gets in, or should the invite ride a durable queue?

**Global DataProtection `TokenLifespan` bumped 60min → 72h** ([`Program.cs:78`](../../src/FundingPlatform.Web/Program.cs), [research D2](research.md#d2-ttl-parameterization-72h-for-invites-60min-for-forgot-password--two-gates))

The consume path is a dual gate (per-row `ExpiresAt` AND Identity's crypto-token lifespan); the stricter binds. Raising the global keeps forgot-password at 60min via its row TTL while letting invites live the full 72h.
- Question: comfortable raising the global Identity token lifespan (safe today because password-reset is the only DataProtector-token consumer — `RequireConfirmedAccount=false`, no email-confirmation flow), or would a dedicated named invite provider be worth the extra plumbing to keep the global at 60min?

**Confirmation renders the raw link once; admin sees it** ([`InvitationSent.cshtml`](../../src/FundingPlatform.Web/Views/Admin/Users/InvitationSent.cshtml), [FR-008](spec.md#functional-requirements))

Rendered directly from the POST (no redirect) so the raw token survives without persistence (only its hash is stored). The admin — who created the account — can copy a link that sets the user's password.
- Question: is on-screen exposure of the set-password link the right default, given the admin is already trusted?

**Supersede scoped to the invite path only** ([`IssuePasswordResetTokenHandler.cs`](../../src/FundingPlatform.Infrastructure/Identity/IssuePasswordResetTokenHandler.cs), [FR-007](spec.md#functional-requirements))

`InvalidatePriorUnused` is set only when issuing an invite; forgot-password keeps its multi-token behavior. A resend therefore also voids any in-flight forgot-password token for that user (research [D3](research.md#d3-supersede-on-resend-fr-007) calls this negligible/desirable).
- Question: acceptable blast radius, or should forgot-password be left strictly untouched by a resend?

### Areas where I'm less certain (5 min)

- [`PasswordResetTokenStore.InvalidateUnusedAsync`](../../src/FundingPlatform.Infrastructure/Identity/PasswordResetTokenStore.cs): uses a load-then-`RemoveRange` tracked-entity delete (not `ExecuteDeleteAsync`) to stay translatable under the SQLite integration provider, mirroring `ConsumeAsync`'s rationale. Under SQL Server this is a SELECT+DELETE rather than a single statement — fine at this volume, but worth a glance if token tables ever grow.
- E2E onboarding helper `OnboardAndLoginAsync` ([`AuthenticatedTestBase.cs`](../../tests/FundingPlatform.Tests.E2E/Fixtures/AuthenticatedTestBase.cs)) reuses the dev-only `LatestPasswordResetLink` seam to set a password for admin-created users in the many create-then-login tests. It decouples onboarding from scraping the create-confirmation link, but means those tests don't exercise the *create-issued* invite link end-to-end — that path is covered explicitly only by `UserInvitationTests`. Is that division of coverage acceptable?

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md) were identified; the reuse-shaped design matches the plan's touch-point list.

- E2E blast radius was broader than the plan's two named obsolete tests (echoing spec 032's D-2): post-create now renders the confirmation instead of redirecting, and admin-created users have no password, so ~10 create-flow classes needed rewiring (post-create assertions → `InvitationSentPage`; temp-password+first-login logins → the invite/onboard helpers). All are green. Question: is the `OnboardAndLoginAsync`-via-dev-seam approach the right long-term shape for these, or should they capture the real create-confirmation link?
- Risk — the two 72h gates must stay in agreement. If a future change lowers `DataProtectionTokenProviderOptions.TokenLifespan` below the invite row TTL, invites would silently die early. The E2E `UserInvitationTests` set-password path is the guard; an integration test cannot cover it (the option is a Web-host concern).

---

## Deep Review Report

> Automated multi-perspective code review results (spex-deep-review). Summarizes
> what was checked, found, and fixed; full detail in [review-findings.md](review-findings.md).

**Date:** 2026-06-12 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 3 | completed |
| Architecture & Idioms | 5 | completed |
| Security | 3 | completed |
| Production Readiness | 5 | completed |
| Test Quality | 6 | completed |
| CodeRabbit (external) | – | skipped (CLI not installed) |
| Copilot (external) | – | skipped (CLI not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 6 | 6 | 0 |
| Minor | 14 | 6 | 8 |

### What was fixed automatically

Security/correctness hardening of the new controller surface: added the sentinel-immutability guard to `ResendInvitation`, moved invite-link composition onto the trusted `Notifications:BaseUrl` in deployed environments (host-header-poisoning defense, dev/test still use the dynamic request host), and bounded the best-effort SMTP send with a 10s linked-CTS timeout so a stalled relay can't pin the request thread. HTML-encoded the free-text name in the email body, cached the not-found template fallback, removed a dead resource constant, and corrected a stale comment. Closed the biggest coverage gaps: a new unit test for the invitation email (subject/link/expiry/encoding), an E2E invalid-link es-CR rejection test, and Admin + SupplierAdmin onboarding tests (FR-003 now covers all four roles).

### What still needs human attention

All Critical/Important findings were resolved. 8 Minor findings remain, documented in [review-findings.md](review-findings.md). The ones worth a reviewer's eye:

- The global `DataProtectionTokenProviderOptions.TokenLifespan` is now 72h ([Program.cs](../../src/FundingPlatform.Web/Program.cs), a deliberate [research D2](research.md) call). Is the dual-gate rationale acceptable, or is a dedicated named invite token provider worth the plumbing to keep the global at 60min?
- The set-password link burns on a weak first password (pre-existing `ConsumePasswordResetTokenHandler` ordering). Is "recovery via admin resend" acceptable, or should the password be validated before the token is consumed?
- Defense-in-depth: should a per-environment `AllowedHosts` / `UseHostFiltering` policy be added at the infra layer to back the link-composition fix?

### Recommendation

All findings addressed or consciously accepted. Code is ready for human review with no known blockers; the remaining Minor items are non-blocking and tracked in [review-findings.md](review-findings.md).
