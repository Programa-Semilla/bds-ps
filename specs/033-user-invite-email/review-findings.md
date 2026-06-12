# Deep Review Findings

**Date:** 2026-06-12
**Branch:** 033-user-invite-email
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** superpowers (after_implement quality gate)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 6 | 6 | 0 |
| Minor | 14 | 6 | 8 |
| **Total** | **20** | **12** | **8** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality). External tools (CodeRabbit, Copilot): not installed — skipped.

After fixes: Unit 532/0, filtered E2E (UserInvitation + AdminUserLifecycle) 14/0; full touched-class E2E previously 46/0.

---

## Findings

### FINDING-1 — Resend mints a set-password link for the sentinel
- **Severity:** Important · **Confidence:** 85
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs (ResendInvitation)
- **Category:** correctness / security · **Source:** correctness-agent, security-agent
- **Resolution:** fixed (round 1)

**What was wrong:** `ResendInvitation` resolved the user by id and issued a fresh 72h set-password link with no sentinel guard, while every other mutating action (Disable/Edit/ResetPassword) throws/handles `SentinelUserModificationException`. An admin could mint a live set-password link for the protected system sentinel, bypassing `SentinelAwareUserStore`.

**How resolved:** Added `if (user.IsSystemSentinel)` → es-CR `AdminErrorMessages.SentinelImmutable` + redirect to the list, before issuing.

### FINDING-2 — Set-password link built from the request host (host-header poisoning)
- **Severity:** Important · **Confidence:** 80
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs (ComposeResetLink)
- **Category:** security · **Source:** security-agent
- **Resolution:** fixed (round 1)

**What was wrong:** The absolute invite link was composed from `Request.Host`. With `AllowedHosts: "*"` and no host filtering, the emailed/admin-visible single-use account-takeover link for a brand-new account (no other credential) could be pointed at an attacker domain via a forged host header. The codebase already keeps a trusted base (`Notifications:BaseUrl`).

**How resolved:** New `ComposeResetLink` builds the link from `Notifications:BaseUrl` in deployed (non-Development) environments and falls back to the request scheme/host only in Development/test (where the Aspire host port is dynamic). This keeps the trusted base in production while not breaking dev/E2E. (Defense-in-depth: a per-environment `AllowedHosts`/`UseHostFiltering` policy is still recommended at the infra layer — see "Remaining".)

### FINDING-3 — Invite email sent synchronously with no bounded timeout
- **Severity:** Important · **Confidence:** 80
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs (IssueAndSendInvitationAsync)
- **Category:** production-readiness · **Source:** production-readiness-agent
- **Resolution:** fixed (round 1)

**What was wrong:** The best-effort send runs on the admin's request thread; `SmtpClient` does not reliably honor the ambient `CancellationToken` during TCP connect, so a relay outage could pin the request for ~100s+ before the catch even ran.

**How resolved:** Wrapped the send in a linked `CancellationTokenSource` with `CancelAfter(10s)`; a timeout is caught like any other transport failure and the admin-visible link remains the fallback. Request latency is now bounded regardless of relay behavior.

### FINDING-4 — Invitation email had zero test coverage
- **Severity:** Important · **Confidence:** 90
- **File:** tests/FundingPlatform.Tests.Unit/Email/InvitationEmailFactoryTests.cs (new)
- **Category:** test-quality · **Source:** test-quality-agent
- **Resolution:** fixed (round 1)

**What was wrong:** `InvitationEmailFactory` + the template — the headline artifact of the feature — had no assertions; a broken subject, dropped link, English copy, or send failure would pass every test.

**How resolved:** Added a unit test (deterministic temp-template ContentRoot) asserting the es-CR subject, the embedded set-password link, the CR-local 72h expiry copy, and HTML-encoding of the free-text name (also closes FINDING-12).

### FINDING-5 — No test for invalid/expired-link UI rejection
- **Severity:** Important · **Confidence:** 80
- **File:** tests/FundingPlatform.Tests.E2E/Tests/Admin/UserInvitationTests.cs (InvalidLink_ShowsEsCrRejection_AndSetsNoPassword)
- **Category:** test-quality · **Source:** test-quality-agent
- **Resolution:** fixed (round 1)

**What was wrong:** FR-010's invalid-link path (`ViewData["InvalidLink"]` → `reset-password-invalid`, es-CR copy) had no E2E assertion; only the superseded-link path was covered, and it asserted only summary visibility, not the rejection text.

**How resolved:** Added an E2E test navigating to `/Account/ResetPassword?userId=bogus&token=bogus`, asserting the es-CR rejection message is visible, the text contains "Enlace inválido o expirado", and the set-password form is NOT presented.

### FINDING-6 — FR-003 "all four roles" only partially covered
- **Severity:** Important · **Confidence:** 78
- **File:** tests/FundingPlatform.Tests.E2E/Tests/Admin/UserInvitationTests.cs (CreateStaffRole_… [Admin], [SupplierAdmin])
- **Category:** test-quality · **Source:** test-quality-agent
- **Resolution:** fixed (round 1)

**What was wrong:** Only Applicant + Reviewer were driven through invite onboarding; Admin and SupplierAdmin (explicitly in scope) were not.

**How resolved:** Added a `[TestCase("Admin")]` / `[TestCase("SupplierAdmin")]` parameterized test (no password field → confirmation → link → set password → sign-in without forced change-password). All four roles now covered.

---

## Remaining Findings (Minor — accepted / documented, not blocking)

- **FINDING-7 (was Important → reclassified Minor): email expiry display drift.** The email expiry is recomputed as `DateTimeOffset.UtcNow.Add(72h)` while the row stamps `IStageExpiryClock.UtcNow.Add(ttl)`. Both agents noted this is the **pre-existing forgot-password idiom** (`AccountController.ForgotPassword`) and resolves sub-second apart in production. Reclassified Minor; not fixed to avoid threading `ExpiresAt` through the shared `IssuePasswordResetTokenResult` (would touch forgot-password). Worth a future shared-seam cleanup.
- **C-3: weak-password on first set burns the single invite link.** `ConsumePasswordResetTokenHandler` (pre-existing spec-021 code, not in this changeset) consumes the marker before `ResetPasswordAsync`, so a too-short first password spends the link. Documented: recovery is the admin resend the feature already provides (FR-006/FR-007); changing the order alters shared replay-safety design. Candidate for a follow-up (validate password before consume).
- **Global `DataProtectionTokenProviderOptions.TokenLifespan` = 72h footgun.** A deliberate plan decision (research D2 — the dual-gate keeps forgot-password at 60min). Latent risk: a future DataProtector-token consumer (email confirm / change-email / 2FA) would silently inherit 72h. Mitigation options (named invite provider, or a startup guard test) deferred — see [research D2](research.md).
- **Non-transactional invalidate+issue.** `InvalidatePriorUnused` runs `InvalidateUnusedAsync` (its own SaveChanges) then `IssueAsync` (another). A crash between them could leave the user with no live token; resend recovers. Small window; documented.
- **`InvitationEmailFactory` duplicates `ForgotPasswordEmailFactory`** (template-load/cache/path-probe) and carries the same non-atomic double-checked read. Third copy of the shape; optional consolidation into a shared loader.
- **Send-failure observability:** userId added to the WARN log (this round); an aggregate metric for invite-delivery failures is still absent (the only delivery attempt, no outbox) — consider a counter.
- **Handler→store supersede wiring** (`InvalidatePriorUnused` flag → `InvalidateUnusedAsync`) is covered end-to-end by the E2E resend test but has no dedicated integration test; **resend single-live-token invariant** is proven by "prior link rejected + new link works" but not by an explicit one-live-token assertion. Both Minor; acceptable coverage.

See [REVIEW-CODE.md](REVIEW-CODE.md) for the human-facing review guide and [spec.md](spec.md) / [plan.md](plan.md) for requirements and design.
