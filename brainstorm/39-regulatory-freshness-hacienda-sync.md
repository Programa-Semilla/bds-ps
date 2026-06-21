# Brainstorm: Regulatory Freshness Gating + Hacienda API Sync (feedback-3 slice D)

**Date:** 2026-06-21
**Status:** spec-created
**Spec:** specs/042-regulatory-freshness-hacienda-sync/

## Problem Framing

Feedback-3 was sliced (`seeds/feedback-3/00-decomposition.md`) into A–H. Slices A (provider compliance + Auditor role, spec 038), B (supplier recommendation, spec 039), and C (auditor workflow stage, spec 040) are shipped. Slice D was the natural next phase: it completes the auditor/compliance arc by adding the two enforcement/automation pieces A deliberately deferred — the regulatory **staleness block** and the **daily Hacienda API sync**.

Verified via an Explore pass that slice A genuinely shipped everything D consumes: per-field `{Hacienda,Ccss,Sicop}LastReviewedAt/By/Source`, the `AdminAuditEvent` audit trail (with kind/source/old/new), the `RegulatoryReviewSource` enum with `Api`/`System` reserved for D, and the "Reviewed — No Change" re-authorize action. The `EmailDispatchWorker` `BackgroundService` and `AnthropicAiClient` `IOptions`+HttpClient patterns give D its job/HTTP-client templates. So D is genuinely just enforcement + automation; the A/D boundary held.

## Approaches Considered

### Hacienda integration: real client vs. stub seam
- **A (stub behind seam):** ship `IHaciendaApiClient` + a fake fallback, full job/gating/audit built and E2E-tested against the fake, real client dropped in later.
  - Pros: ships complete without external dependency risk.
  - Cons: real integration deferred.
- **B (real client now):** the client provided the real endpoint — `https://api.hacienda.go.cr/fe/ae?identificacion={id}`, no auth.
  - Pros: real integration delivered this slice.
  - Cons: external risk; mitigated by the `IHaciendaApiClient` seam (fake in tests, live API never called in tests).

### Which fields block (§28.7)
- All three equally (chosen) vs. CCSS+SICOP only with Hacienda exempt (machine-maintained).

### Freshness window (§28.6)
- Configurable days, default 30 (chosen) vs. strict calendar month vs. hard-coded.

## Decision

Real client (B) — captured the live contract (response `{ nombre, tipoIdentificacion, regimen, situacion{moroso,omiso,estado,administracionTributaria}, actividades }`; HTTP 404 for unregistered ids), behind an `IHaciendaApiClient` seam so the live API is never hit in tests. Decisions resolved in the session:

- **§28.6** → freshness window configurable, default **30 days**, UTC-instant comparison.
- **§28.7** → **all three** fields (Hacienda/CCSS/SICOP) block equally; Hacienda blocks if it goes stale through sync failure (fail-safe).
- **Block placement** → hard block at the auditor's audit-stage advance actions; early non-blocking warning on the reviewer send-to-audit and auditor screens.
- **§25.3** → include a daily auditor stale-value digest via the existing outbox + allowlist.
- **§16.4** → per-provider last-sync outcome on the supplier detail screen + "verificación fallida" filter/badge on the admin list + `AdminAuditEvent` per failure.

Spec written as **042-regulatory-freshness-hacienda-sync**, reviewed SOUND by `speckit-spex-gates-review-spec` (no critical/important issues).

## Open Threads

- Hacienda→`HaciendaStatus` mapping for less-common cases (Desinscrito variants, `omiso = SI` not moroso) — confirm against real Hacienda value vocabulary at plan (Open Question 1).
- Exact "selected quotation" semantics for the referenced-provider set the gate checks (Open Question 2).
- Notification cadence: daily digest (proposed) vs. once-on-threshold-crossing (Open Question 3).
- Remaining feedback-3 slices after D: **E** (fund process windows + applicant timing), **F** (per-user funding limit), **G** (applicant timeline + % progress), **H** (UX grab-bag).
