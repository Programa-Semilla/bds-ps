# REVIEW-CODE — Post-Resolution Email Notifications (spec 028)

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Compliance:** 100% (FR-001..021/013a, SC-001..008 — see [STAMP.md](STAMP.md))

---

## Code Review Guide (30 minutes)

This guide walks a reviewer through the spec-028 implementation, focused on the
decisions that need human judgment rather than the compliance matrix (that lives in
[STAMP.md](STAMP.md)). This is an additive increment to shipped spec 021: 12 new
events threaded through the existing outbox → worker → sender → allowlist pipeline,
with two cross-cutting extensions. **No schema change.**

**Changed files:** ~30 — 1 Domain (enum), 4 Application (payload, bindings, 2 services
+ ApplicantResponseService), 2 Infrastructure (resolver, outbox writer untouched but
exercised), 1 Web (renderer), 24 Razor partials, plus unit/integration/E2E tests.

### Understanding the changes (8 min)

- Start with [contracts/notification-events.md](contracts/notification-events.md): the
  12-event table (trigger → recipients → subject → CTA) is the source of truth every
  other file agrees with.
- Then `src/FundingPlatform.Application/Services/ApplicantResponseService.cs`: the US1+US2
  enqueue call sites and the three two-phase helpers (`EnqueueReviewerEventAsync`,
  `EnqueueApplicantEventAsync`, `EnqueueAppealResolvedAsync`). This is where the canonical
  spec-021 pattern (mutate + save → enqueue → save) is mirrored.
- Question: the enqueue helpers are duplicated across `ApplicantResponseService`,
  `SignedUploadService`, and `FundingAgreementService` (each has its own small
  payload-builder). Is that acceptable per-service locality, or worth a shared
  `Application`-layer enqueue helper?

### Key decisions that need your eyes (12 min)

**Event-driven CTA replaces the bucket-derived branch** (`src/FundingPlatform.Web/Services/RazorEmailRenderer.cs:144`, [R-001](research.md#r-001--event-aware-cta-resolution-the-one-cross-cutting-gap) / [FR-018](spec.md#functional-requirements))

`ComposeCtaUrl` now reads `Binding.CtaRouteTemplate` instead of switching on the recipient
bucket. The CTA is a function of the **event**, not the recipient. The shipped 7 events
were backfilled with their original routes.
- Question: see "Deviations and risks" below — this changes the *admin* CTA for the
  spec-021 `ReturnedToApplicant` event. Is that acceptable?

**Dual-fire as one phase-2 save** (`src/FundingPlatform.Application/Services/ApplicantResponseService.cs` → `EnqueueAppealResolvedAsync`, [FR-006](spec.md#functional-requirements))

GrantReopenToReview enqueues both `APPEAL_RESOLVED_APPLICANT` and `APPEAL_REOPENED_REVIEWER`
against the same `VersionHistoryId`, distinct `EventType`, committed in a single
`SaveChangesAsync` so the pair is atomic.
- Question: is one shared `VersionHistoryId` with `EventType` as the only differentiator
  a robust enough idempotency anchor here? (The unique index admits both; integration test
  asserts exactly two distinct emails.)

**Actor exclusion in the resolver** (`src/FundingPlatform.Infrastructure/Notifications/Resolvers/NotificationRecipientResolver.cs:150`, [FR-013a](spec.md#functional-requirements))

After dedup, the resolver drops `payload.ActorUserId`. Null actor (every legacy spec-021
row) is a no-op, so the shipped events are unchanged.
- Question: the actor is passed in the payload rather than re-derived from VersionHistory.
  Comfortable with that being the single source of "who acted"?

**Signing enqueue placed after the blob-cleanup try/catch** (`src/FundingPlatform.Application/Services/SignedUploadService.cs`, `UploadAsync`/`ReplaceAsync`)

For upload/replace, the phase-2 enqueue runs *after* the try/catch that deletes the blob on
failure — so a notification-save failure never deletes an already-committed signed PDF.
Withdraw/Approve/Reject (no blob) keep the enqueue inside the concurrency-guarded save.
- Question: is the asymmetry (enqueue inside vs. after the try) clear enough, or should a
  comment/refactor make the blob-safety rationale louder?

### Areas where I'm less certain (5 min)

- `src/FundingPlatform.Web/Views/Emails/SignedUploadRejectedApplicant.cshtml` ([NFR-003](spec.md#non-functional-requirements)):
  "no verbatim reviewer commentary" is a copy judgment. I render a generic "requiere cambios"
  cue + CTA and pass `OutcomeCode: null` so the comment never enters the payload. Reviewer's
  eye on the Spanish copy is the only check — there's no automated "no PII" assertion beyond
  the E2E asserting the comment string is absent from the body.
- `AppealResolvedApplicant.text.cshtml`: the plain-text `@switch` uses `@:` literal lines for
  the three outcome bodies. Worth confirming the rendered text wraps acceptably in a real
  mail client (the HTML variant is the primary surface).
- Replace/Withdraw reviewer events are covered at unit + integration level but the E2E journey
  exercises submit→approve/reject only. They route through the same `EnqueueSigningReviewerAsync`
  helper as submit, so they're identical by construction — but no end-to-end capture proves it.

### Deviations and risks (5 min)

- **Admin CTA for applicant-bucket events** (`RazorEmailRenderer.ComposeCtaUrl`, [R-001](research.md#r-001--event-aware-cta-resolution-the-one-cross-cutting-gap)):
  the event-driven model gives *one* route per event for *all* recipients. Two consequences a
  reviewer should sign off on:
  (1) For new applicant-bucket events (e.g. `APPEAL_RESOLVED_APPLICANT` → `/ApplicantResponse/Index/{id}`,
  which is `[Authorize(Roles="Applicant")]`), a participating-admin recipient gets a CTA they
  cannot open (403). The spec deliberately chose one CTA per event ([R-001](research.md#r-001--event-aware-cta-resolution-the-one-cross-cutting-gap)); admins are secondary observers.
  (2) The shipped spec-021 `ReturnedToApplicant` admin CTA **changed** from `/Review/{id}`
  (admin-accessible) to `/Application/Details/{id}` (applicant route) as a side effect of
  collapsing to one route per event. Email subjects/bodies are unchanged and the full E2E
  suite is green (captures assert URL strings, not admin navigation), so this is latent.
  Question: acceptable, or should the CTA model carry an optional admin-route override?
- `Binding.CtaRouteTemplate` for the funding-agreement events is `/Applications/{id}/FundingAgreement`
  (no trailing slash) vs. the contract table's `/Applications/{id}/FundingAgreement/` — matched
  to the controller's actual `[HttpGet("")]`. E2E confirms the link resolves. Cosmetic.
- [T036](tasks.md) (P95 < 30 s) is not separately instrumented: the transport is the unchanged
  spec-021 pipeline ([FR-020](spec.md#functional-requirements)), so NFR-002 cannot regress by
  construction; the full E2E run exhibited no time-to-send issue. Question: is the
  by-construction argument sufficient, or do you want a dedicated assertion?
