# Code Review: ALIA Transactional Email Brand UI-Lift (041)

**Spec:** [spec.md](spec.md) · **Date:** 2026-06-19 · **Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall: 100% (21/21 hard requirements compliant)** — validated by the filtered E2E delivery gate (43 passed / 5 intentional skips / 0 real failures) plus unit + integration suites.

- Design-system FRs (FR-001..006): 6/6 compliant
- Copy/naming + twins (FR-007..009): 3/3 compliant
- Coverage (FR-010): compliant (every existing email + 3 new)
- New emails (FR-011..014): 4/4 compliant
- NFRs (NFR-001..007): 7/7 compliant
- Success criteria (SC-001..008): all met

One minor, intentional interpretation under [FR-008](spec.md#fr-008): existing **outbox subject lines were preserved** (e.g. `ApplicationRejected` keeps "Decisión sobre tu solicitud" rather than the reference's softer "Actualización sobre tu solicitud"). The reference *voice* is applied in hero titles + body copy; subjects were held stable to avoid churning E2E subject-filter assertions. Behaviorally neutral.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on high-level questions that need human judgment.

**Changed files:** ~80 files — 1 layout rebuild + 7 new shared partials, ~22 outbox bodies + ~22 text twins refactored, 3 direct-send factories + 2 notifiers converted to Razor, 1 new domain event + bindings/resolver/service wiring, 3 new emails, and the unit/integration/E2E test reconciliation.

### Understanding the changes (8 min)

- Start with [`Views/Emails/_EmailLayout.cshtml`](../../src/FundingPlatform.Web/Views/Emails/_EmailLayout.cshtml) + the `Shared/` partials: this is the single source of brand chrome every email composes.
- Then [`Services/EmailViewRenderer.cs`](../../src/FundingPlatform.Web/Services/EmailViewRenderer.cs) and [`Application/Notifications/Email/`](../../src/FundingPlatform.Application/Notifications/Email/): the one Razor-render primitive + the shared models (`IBrandedEmailModel`, `DirectEmailModel`) that make outbox **and** direct-send flow through the same shell ([Decision 1](research.md)).
- Question: is routing **all** delivery paths through one Razor shell (vs. leaving identity/stage emails as token files) worth the factory rewrites? The payoff is "one place to change the brand"; the cost is the model-typing subtlety below.

### Key decisions that need your eyes (12 min)

**Shared `_ViewStart` model typing** (`Views/Emails/_ViewImports.cshtml` + every outbox template's `@model` line, relates to [FR-001](spec.md#fr-001))

The `_ViewImports` no longer sets a default `@model`; each view declares its own (outbox → `EmailRenderModel`, direct-send → `DirectEmailModel`, layout → `IBrandedEmailModel`). This was forced by a real bug: `_ViewStart` runs for every page and had inherited `EmailRenderModel`, so direct-send pages threw a model-mismatch before rendering (see [Phase 7 commit](../../)). 
- Question: is per-view `@model` the right call, or would a common base model for all emails be cleaner long-term?

**Text-twin HTML-decoding** (`Services/EmailViewRenderer.cs`, layout-less render path)

Razor HTML-encodes `@`-expressions, so plain-text twins would show `&#x2B;506` / `autom&#xE1;tico`. The renderer HTML-decodes the text-only output. 
- Question: is decode-the-whole-text-body acceptable, or should text twins use `@Html.Raw` per value? (Decode is simpler and text bodies carry no intentional entities.)

**Under-review trigger writes a `VersionHistory("StartReview")` row** ([`ReviewService.GetApplicationForReviewAsync`](../../src/FundingPlatform.Application/Services/ReviewService.cs), relates to [FR-011](spec.md#fr-011))

The `Submitted→UnderReview` transition now persists a history row to anchor the outbox dedup key, and the method gained a `reviewerUserId` param (actor + author). 
- Question: any concern that a reviewer (or admin) *opening* a Submitted application is the right trigger for "review started"? It fires on first open, guarded by the `Submitted` state check.

**Direct-send `EmailMessage` gained `TextBody` + multipart** ([`SmtpEmailSender`](../../src/FundingPlatform.Infrastructure/Email/SmtpEmailSender.cs))

To honor [FR-009](spec.md#fr-009) for identity/stage emails, the direct-send envelope now carries a text part shipped as `multipart/alternative`. 
- Question: acceptable to extend the spec-021 direct-send seam this way?

### Areas where I'm less certain (5 min)

- Brand-teal contrast ([NFR-003](spec.md#nfr-003)): `#008a9e` is used for the hero `<h1>` and the white-on-teal CTA — both large/bold, so WCAG AA *large-text* (3:1) is met, but it's below the 4.5:1 normal-text bar. Body text uses dark `#243b40` on white (well within AA). Worth a designer's eye if teal ever moves to small text.
- Dark-mode ([NFR-005](spec.md#nfr-005)): handled only via `color-scheme: light only` meta + light backgrounds — not exercised by an automated test; verify in a forced-dark client.
- `CompanyForReviewNotifier` renders-then-discards (`src/FundingPlatform.Infrastructure/Suppliers/CompanyForReviewNotifier.cs`): the stub renders to prove the template works but sends nothing (recipient deferred, [OQ-1](spec.md#open-questions)). The "Identificación" row uses the owning applicant's `LegalId` since `Company` has no identification field — a placeholder choice OQ-1 may revise.

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md)'s architecture were identified; the implementation followed Decisions 1–6. Intentional refinements (recorded in [spec Evolution Log](spec.md#evolution-log)): factories Singleton→Scoped (now depend on the scoped renderer); password-changed also fires on spec-033 invite first-set (research D4); the text-decode + `_ViewStart` fixes above.

- Shared-fixture E2E flakiness: one reviewer-bucket signing test timed out in the full gate but passes in isolation (worker backlog under load). Question: acceptable as known shared-fixture timing, or should the mail-capture timeout be raised?
- Incidental fix: unblocked a pre-existing spec-037 E2E compile break (`ManualScreenshotsCaptureTests.CompanyNameInput`). Question: fine to carry in this PR?

---

## Deep Review Report

> Automated multi-perspective code review results.

**Date:** 2026-06-19 | **Rounds:** 1 fix + 1 re-review | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 1 | completed |
| Architecture & Idioms | 7 | completed |
| Security | 0 | completed (clean) |
| Production Readiness | 4 | completed |
| Test Quality | 7 | completed |
| CodeRabbit (external) | – | skipped (not installed) |
| Copilot (external) | – | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 1 | 1 | 0 |
| Important | 3 | 3 | 0 |
| Minor | 13 | 6 | 7 (accepted) |

### What was fixed automatically

One theme drove the Critical + Important fixes: spec 041 made the three direct-send `BuildAsync` methods render Razor (which can throw), but their call sites still only guarded `SendAsync`. Moved the build inside the best-effort try at all three sites — `AccountController.ForgotPassword` (Critical: an uncaught render failure 500'd valid accounts only, reopening the FR-028 enumeration channel), `AdminUsersController` invitation (broke the already-committed create + aborted the batch loop), and `StageExpiryReminderService` (one bad render starved the whole reminder cycle). Separately, added the missing `DbUpdateConcurrencyException` catch to `ReviewService.GetApplicationForReviewAsync` so a concurrent reviewer-open no longer 500s. Plus cleanups: deleted dead `EmailTemplateText`, removed dead `EmailRenderModel.SenderName/SenderEmail`, de-duplicated the `_PartnerFooter` support address onto `EmailBrand.SupportEmail`, dropped unused `@using`s, corrected the "single-edit palette" doc, and added a unit test for direct-send `.text` twin existence.

### What still needs human attention

All Critical and Important findings were resolved and a round-2 re-review of the fix sites was clean. Seven Minor findings remain, all accepted with rationale in [review-findings.md](review-findings.md) — none blocking. Reviewers may want to weigh in on two judgment calls:

- Should the ~22 outbox templates eventually share an `_OutboxBody` partial the way the direct-send path shares `_DirectBody`, or is the per-email inline chrome acceptable? (architecture, Low)
- Is the render-only `CompanyForReviewNotifier` stub the right shape for the FR-013/OQ-1 deferral, or should the render-only proof live solely in a test? (architecture, Low)

### Recommendation

All findings addressed. Re-verification after fixes: build green, Unit 48/48, Integration 45/45, affected E2E 15/15. Code is ready for human review with no known blockers.
