# Review Brief: Regulatory Freshness Gating + Hacienda API Sync

**Spec:** specs/042-regulatory-freshness-hacienda-sync/spec.md
**Generated:** 2026-06-21

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Feedback-3 slice D closes the compliance loop that slice A (spec 038) opened. Slice A added per-provider regulatory freshness metadata, an audit trail, and a manual "Reviewed — No Change" re-authorize action, but only *tracked and displayed* freshness. This slice adds the two deferred pieces: a **staleness block** that stops an application from advancing through the audit stage while any provider it relies on has a regulatory value not reviewed within a configurable window (default 30 days), and a **daily automated Hacienda sync** that keeps each provider's tax status current and audited via the public Hacienda API. It also surfaces sync failures and notifies auditors of stale values.

## Scope Boundaries

- **In scope:** staleness gate at auditor advance actions (all three fields block); early non-blocking warning on reviewer/auditor screens; daily Hacienda sync job with audit + freshness refresh; per-provider sync-failure visibility; daily stale-value auditor notification.
- **Out of scope:** other slices (E/F/G/H); any change to slice A's regulatory model/enums; real-time per-request Hacienda lookups; an API for CCSS/SICOP (manual-only); retroactive blocking of already-released agreements.
- **Why these boundaries:** D is defined by the decomposition as "enforcement + automation only"; the model it builds on already shipped in A, and the audit stage it gates already shipped in C.

## Critical Decisions

### All three regulatory fields block equally (§28.7)
- **Choice:** Hacienda, CCSS/Caja, and SICOP/CCOP all block application advancement when stale — Hacienda included.
- **Trade-off:** Hacienda is normally kept fresh by the daily job, so it would rarely block; but if the API is down long enough for Hacienda to go stale, advancement halts (fail-safe).
- **Feedback:** Is fail-safe-on-outage the right posture, or should Hacienda be exempt from blocking because it is machine-maintained?

### Freshness window = configurable days, default 30 (§28.6)
- **Choice:** `Regulatory:FreshnessWindowDays`, default 30, comparing UTC instants.
- **Trade-off:** "30 days" is simpler/testable but not identical to a calendar month.
- **Feedback:** Is a fixed 30-day default acceptable vs. a strict calendar month?

### Real Hacienda client now (not a stub)
- **Choice:** Build the real HTTP client against `https://api.hacienda.go.cr/fe/ae` (no auth) this slice, behind an `IHaciendaApiClient` seam so tests inject a fake.
- **Trade-off:** Real integration risk now vs. deferring; mitigated because the contract was captured live and tests never hit the live API.
- **Feedback:** Confirm the live-API-never-in-tests boundary is acceptable.

## Areas of Potential Disagreement

### Referenced-provider scope for the gate
- **Decision:** the gate checks providers whose quotations are *selected for the application's line items*.
- **Why this might be controversial:** "selected" could mean recommended-and-approved per item, or any attached quotation.
- **Alternative view:** check all attached quotations (broader, blocks more often).
- **Seeking input on:** the exact selection semantics (Open Question 2).

### Notification cadence
- **Decision:** a daily digest of all currently-stale providers.
- **Why this might be controversial:** daily digests can become noise if values stay stale.
- **Alternative view:** notify once when a value first crosses the threshold.
- **Seeking input on:** Open Question 3.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Freshness window config key | `Regulatory:FreshnessWindowDays` (default 30) | Configurable staleness threshold |
| Integration seam | `IHaciendaApiClient` | Replaceable so tests use a fake |
| Sync source marker | `RegulatoryReviewSource.Api` | Reused from slice A (already reserved) |
| Failure surface label | "verificación fallida" | Admin supplier list filter/badge (es-CR) |

## Open Questions

- [ ] Mapping of less-common Hacienda `estado` (Desinscrito variants) and `omiso = SI` cases to the existing status enum (Open Question 1).
- [ ] Exact "selected quotation" semantics for the referenced-provider set (Open Question 2).
- [ ] Daily digest vs. once-on-threshold notification cadence (Open Question 3).

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Hacienda API shape differs from captured sample for some taxpayers | Med | `IHaciendaApiClient` seam isolates parsing; defensive mapping + failures recorded, never crash |
| Daily job overwrites concurrent auditor edits | Med | Optimistic concurrency (RowVersion); skip/retry on conflict (FR-025) |
| Long API outage stale-blocks many applications | Med | Failure surface explains why; auditors can re-authorize manually to unblock |
| Large provider catalog exhausts resources during sync | Low | Batched/throttled processing (FR-017) |

---
*Share with reviewers before implementation.*
