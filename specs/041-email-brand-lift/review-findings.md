# Deep Review Findings

**Date:** 2026-06-19
**Branch:** 041-email-brand-lift
**Rounds:** 1 (fix) + 1 (re-review)
**Gate Outcome:** PASS
**Invocation:** superpowers (after_implement quality gate)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 1 | 1 | 0 |
| Important | 3 | 3 | 0 |
| Minor | 13 | 6 | 7 (accepted) |
| **Total** | **17** | **10** | **7** |

**Agents completed:** 5/5 internal (correctness, architecture, security, production-readiness, test-quality) + 1 round-2 re-review. **External tools:** CodeRabbit + Copilot not installed (skipped).
**Security agent:** no issues found (untrusted-data-into-HTML flow is fully Razor-auto-encoded; no `@Html.Raw`; the text-decode is text/plain-only).

**Dominant theme:** spec 041 converted the three direct-send `BuildAsync` methods from cannot-throw plain-text token substitution into Razor renders that **can throw**, but their call sites still only guarded `SendAsync`. Three agents independently converged on this. All fixed.

## Findings

### FINDING-1
- **Severity:** Critical · **Confidence:** 88 · **Category:** production-readiness (also security)
- **File:** `src/FundingPlatform.Web/Controllers/AccountController.cs:209` (ForgotPassword POST)
- **Source:** production-readiness-agent
- **Resolution:** fixed (round 1)

**What is wrong:** `_forgotPasswordEmailFactory.BuildAsync(...)` (a Razor render that can throw `EmailRenderException`) was called *outside* the try that swallows errors for FR-028 neutrality. The try wrapped only `SendAsync`.

**Why this matters:** This branch runs only when the email belongs to a real account. A render failure there 500s for **valid** accounts while unknown emails return the neutral 200 — reopening the exact account-enumeration side-channel FR-028 exists to prevent.

**How it was resolved:** Moved `BuildAsync` inside the existing try, so a render OR transport failure is caught + logged and the neutral response is still returned.

### FINDING-2
- **Severity:** Important · **Confidence:** 85 · **Category:** production-readiness
- **File:** `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs:109` (`IssueAndSendInvitationAsync`)
- **Source:** production-readiness-agent
- **Resolution:** fixed (round 1)

**What is wrong:** Invitation `BuildAsync` was outside the best-effort try (which wrapped only `SendAsync`). A render failure 500s **after** the account was already created (the admin loses the FR-008 copyable-link fallback) and, in the batch-create path, aborts the whole loop.

**Why this matters:** A secondary best-effort concern (the email) breaks an already-committed primary operation and a multi-row batch.

**How it was resolved:** Moved `BuildAsync` inside the try (now using `sendCts.Token`); the catch still `return inviteLink;`, so the admin gets the fallback link and the batch loop continues.

### FINDING-3
- **Severity:** Important · **Confidence:** 80 · **Category:** production-readiness
- **File:** `src/FundingPlatform.Infrastructure/BackgroundServices/StageExpiryReminderService.cs:144`
- **Source:** production-readiness-agent
- **Resolution:** fixed (round 1)

**What is wrong:** The per-candidate reminder `BuildAsync` had no per-iteration try/catch (only the whole cycle was guarded). One application whose render throws ends the cycle early, starving every remaining candidate's reminder until the next tick.

**Why this matters:** The service is designed for resilient per-app delivery; the new render step sat outside that boundary, turning one bad render into batch-wide reminder suppression.

**How it was resolved:** Wrapped the per-iteration `BuildAsync` in try/catch that logs the `publicCode` and `continue`s (the reminder bit stays unset → retried next cycle).

### FINDING-4
- **Severity:** Important · **Confidence:** 75 · **Category:** correctness
- **File:** `src/FundingPlatform.Application/Services/ReviewService.cs:87` (`GetApplicationForReviewAsync`)
- **Source:** correctness-agent
- **Resolution:** fixed (round 1)

**What is wrong:** The new `Submitted→UnderReview` transition (mutation + two `SaveChangesAsync` + outbox enqueue) on the GET `Review` path had no `DbUpdateConcurrencyException` handling, unlike every sibling write method in the file. Two reviewers opening the same Submitted application concurrently → the loser's optimistic-concurrency save throws an uncaught 500.

**Why this matters:** A plain page open under a (rare) concurrent-open race surfaces as an unhandled 500 instead of rendering the now-`UnderReview` application.

**How it was resolved:** Wrapped the transition block in try/catch on `DbUpdateConcurrencyException`; on catch, re-read the application and fall through to render (the winner already transitioned + enqueued exactly one email). Round-2 re-review confirmed no NRE on the catch path.

### FINDING-5..10 (Minor — fixed)
- **architecture:** deleted dead `EmailTemplateText.cs` (orphaned after the token→Razor switch); removed dead `EmailRenderModel.SenderName/SenderEmail` fields + their config reads (the From: display lives in config + the sender impls); clarified the misleading "single-edit palette" claim in `EmailBrand` (palette hex is inlined in the chrome for client reliability); `_PartnerFooter` mailto now uses `@EmailBrand.SupportEmail` instead of a drift-prone literal; removed two unused `@using` directives in `_ViewImports`.
- **test-quality:** added a unit test asserting the direct-send emails' `.text.cshtml` twins exist on disk (the outbox twin-parity test only covered `NotificationEvent` variants — SC-007 gap for the direct-send family).

## Remaining Findings (Minor — accepted, no action)

- **Outbox template `<p>`-style + greeting duplication** (architecture, Low): the ~22 outbox bodies inline the same paragraph style; the direct-send path already factored this into `_DirectBody`. Accepted: per-email *copy* legitimately differs and the chrome is guarded by tests; a shared `_OutboxBody` is a reasonable future follow-up, not a blocker.
- **`CompanyForReviewNotifier` render-then-discard** (architecture/YAGNI, Low): deliberate per FR-013/OQ-1 (render-only proof of the deferred stub); covered by the render test + a source-scan "no live trigger" guard.
- **`ProviderCreatedNotifier` renders per-auditor** (production-readiness, Low): acceptable for small auditor pools; documented.
- **Password-changed E2E asserts `4600-1234` not the full `+506 ...`** (test-quality, Medium): the HTML body HTML-encodes `+`→`&#x2B;` (observed), so the digit assertion is the robust choice; the direct-send `text/plain` part is unreliably surfaced by the capture client, so the twin's *existence* is now covered by the new unit test instead.
- **UnderReview re-open uses a fixed `Task.Delay`** (test-quality, Low): the deterministic SC-008 "exactly once" proof is the integration dedup test; the E2E negative check is a secondary smoke check.
- **CompanyForReview source-scan guard matches by filename** (test-quality, Low): adequate — any real future trigger would inject the interface and trip the whitelist.
- **`Variants_use_es_cr_copy_no_english_markers`** (test-quality, Low): narrow smoke check; the real es-CR guarantee is the E2E body assertions.
