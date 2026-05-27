# STAMP — Post-Resolution Email Notifications (spec 028)

**Date**: 2026-05-27
**Branch**: `028-post-resolution-notifications`
**Verdict**: ✅ **PASS** — additive increment to shipped spec 021; full test pyramid green; no schema change.

## Test execution (personally run)

| Layer | Command | Result |
|---|---|---|
| Build | `dotnet build FundingPlatform.slnx` | 0 errors |
| Unit (notifications) | `dotnet test tests/FundingPlatform.Tests.Unit --filter ~Notifications` | 27/27 ✅ |
| Integration (notifications) | `dotnet test tests/FundingPlatform.Tests.Integration --filter ~Notifications` | 34/34 ✅ |
| **Full E2E (delivery gate)** | `dotnet test tests/FundingPlatform.Tests.E2E` | **291 passed, 0 failed, 5 skipped (pre-existing), 27m57s** ✅ |

The 5 skips are inherited spec-021 deferrals (`ApprovedAndRejectedNotificationsTests` Assert.Ignore, the under-Aspire empty-allowlist skip, smtp-degraded inconclusive) — none introduced by this spec.

## Functional-requirement coverage

| FR | Where | Evidence |
|---|---|---|
| FR-001 (12 enum values, string storage) | `NotificationEvent.cs` | `NotificationTemplateBindingsTests.Storage_string_round_trip` (all 19) |
| FR-002 (RESPONSE_SUBMITTED_REVIEWER) | `ApplicantResponseService.SubmitResponseAsync` | `ResponseNotificationsTests` (E2E), `ResponseSubmittedNotificationsTests` (int) |
| FR-003 (APPEAL_OPENED_REVIEWER) | `OpenAppealAsync` | `AppealNotificationsE2ETests` |
| FR-004 (directional message) | `PostMessageAsync` (author==applicant) | `AppealNotificationsTests.Message_direction_follows_the_author` + E2E |
| FR-005 (APPEAL_RESOLVED_APPLICANT, all 3 + body variant) | `ResolveAppealAsync` + `AppealResolvedApplicant.cshtml` `OutcomeCode` switch | E2E + unit binding |
| FR-006 (dual-fire on GrantReopenToReview) | `EnqueueAppealResolvedAsync` (single phase-2 save) | `AppealNotificationsTests.GrantReopenToReview_dual_fire_yields_two_distinct_emails` + E2E |
| FR-007 (AGREEMENT_GENERATED on gen+regen, distinct anchor) | `FundingAgreementService.PersistGenerationAsync` | `SigningNotificationsTests.Regenerate_refires…` + E2E |
| FR-008 (signed upload submit/replace/withdraw → reviewer) | `SignedUploadService.{Upload,Replace,Withdraw}Async` | E2E (submit) + int reviewer-set; replace/withdraw share the identical enqueue helper |
| FR-009 (executed / rejected → applicant) | `SignedUploadService.{Approve,Reject}Async` | `SigningNotificationsE2ETests` (both) |
| FR-010 (AgreementGenerated VH row via domain method) | `PersistGenerationAsync` → `Application.AddVersionHistory` | green FA integration (76) + US3 E2E |
| FR-011 (idempotency index unchanged) | reused | `ResponseSubmittedNotificationsTests` double-pass |
| FR-012 (resolver per recipient rules) | `NotificationRecipientResolver` bucket arms | recipient-matrix integration |
| FR-013 (dedup + bucket priority) | unchanged spec-021 | `Spec021Regression…` |
| FR-013a (actor exclusion) | resolver post-dedup filter | `…Actor_user_is_dropped` + US2 E2E (resolving reviewer excluded from reopen) |
| FR-014 (admin reuses winning variant body) | resolver `TemplateVariantKey` reuse (unchanged) | — |
| FR-015 (24 partials under `_EmailLayout`) | `Views/Emails/*` | `RazorEmailRendererTests.Every_variant_has_html_and_text_files` |
| FR-016 (es-CR only) | partials | brand-grep + English-marker unit tests |
| FR-017 (3 body variants in one partial) | `AppealResolvedApplicant.{cshtml,text}` | unit binding + E2E |
| FR-018 (existing routes, extended CTA set) | `Binding.CtaRouteTemplate` + `RazorEmailRenderer.ComposeCtaUrl` | `RazorEmailCtaUrlTests` + route existence verified |
| FR-019 (existing `[Authorize]`) | no auth added | — |
| FR-020 (existing pipeline) | outbox→worker→sender→allowlist reused | worker-driven integration + full E2E |
| FR-021 (zero schema/dacpac/migration) | — | no `Migrations/`, no `Database/` diff vs main |

## Success-criteria coverage

| SC | Evidence |
|---|---|
| SC-001 (12 events fire + captured) | 3 E2E classes (US1/US2/US3) via smtp4dev |
| SC-002 (recipient matrix exact) | `PostResolutionNotificationsHarness` matrix tests (reviewer + participating-admin in; applicant + non-participating-admin out) |
| SC-003 (idempotency incl. dual-fire + successive) | integration double-pass + dual-fire (==2) + 3-message (no collapse) |
| SC-004 (allowlist fail-closed) | `PostResolutionAllowlistFailClosedTests` (empty allowlist → BlockedByAllowlist, zero sends) |
| SC-005 (brand-grep on 24 templates) | `RazorEmailRendererTests` (no "Capital Semilla"/"Forge", no inline `<img>`, no English markers) |
| SC-006 (zero migrations / dacpac) | verified — see FR-021 |
| SC-007 (reported bug closed) | `ResponseNotificationsTests` — reviewer notified on applicant response; applicant gets nothing |
| SC-008 (P95 < 30 s, no regression) | transport + worker cadence (5 s poll) unchanged; full E2E green incl. all new events. No separate instrumentation — pipeline is byte-for-byte the shipped spec-021 path (FR-020). |

## Deviations / notes

1. **CTA route trailing slash** — the contract table wrote the applicant funding-agreement CTA as `/Applications/{id}/FundingAgreement/`; implemented as `/Applications/{id}/FundingAgreement` (no trailing slash) to match the controller's actual `[HttpGet("")]` route exactly. E2E confirms the deep link resolves. Cosmetic; route target unchanged.
2. **T036 (P95 instrumentation)** — not a dedicated automated assertion. Rationale: spec 028 changes nothing in the transport (FR-020); the `EmailDispatchWorker` poll/claim/retry path and 5 s cadence are the shipped spec-021 code, so NFR-002's P95 cannot regress by construction. The full E2E run (which waits on captures within 60 s budgets and completed in 28 m across 296 tests) exhibits no time-to-send regression.
3. **Replace/Withdraw reviewer events** — exercised at unit (binding/enum/bucket), integration (reviewer-set + actor exclusion), and service-wiring level; the E2E journey drives submit→approve/reject explicitly. Replace/Withdraw route through the same `EnqueueSigningReviewerAsync` helper as submit, so the behavior is identical by construction.

## Constitution re-check

PASS — Clean Architecture (enum in Domain, writer/bindings/payload in Application, resolver/EF in Infrastructure, partials/CTA in Web); Rich Domain Model (`AgreementGenerated` via `AddVersionHistory`); E2E non-negotiable (one real-UI Playwright journey per US, full suite green); Schema-First (no dacpac/EF change); Spec-Driven; Simplicity (entire spec-021 pipeline reused).
