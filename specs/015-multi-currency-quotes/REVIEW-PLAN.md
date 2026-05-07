# Review Guide: Suppliers Quotes Multi-Currency

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-06

---

## What This Spec Does

Lets administrators turn on a second currency (USD) alongside the platform's base currency (CRC) and publish CRC↔USD reference exchange rates. Applicants can then enter supplier quotes in either currency; non-CRC quotes get a CRC equivalent computed at save time and the applied rate is *snapshotted* onto the quote so historical figures never drift when a future rate is published. All UI surfaces, totals, and the legal agreement PDF reflect both the original and converted CRC values, with conversion notes where applicable.

**In scope:** CRC + USD only; admin currency enable/disable (CRC permanently on); admin rate CRUD (create-only — immutable once a quote uses it); applicant quote form with real-time server-computed preview and persisted snapshot; mixed-currency display across lists/details/dashboards/CSV/PDF; idempotent migration that auto-stamps CRC quotes and quarantines legacy non-CRC quotes for admin review.

**Out of scope:** more than two currencies; per-quote manual conversion override; re-pricing existing quotes against newer rates; rate approval/draft workflow; future-dated effective timestamps; stale-rate notifications; mid-quote currency switching (deletion + recreation required to switch — see [Q4 clarification](spec.md#clarifications)).

## Bigger Picture

This is the first feature on FundingPlatform that introduces dual-currency intake. The platform's locale and currency story has historically been hard-coded to es-CR / CRC (see [`FundingAgreement:CurrencyIsoCode = COP`](../../CLAUDE.md) — note that the constant is currently `COP` in CLAUDE.md but the spec works in `CRC` per [Assumptions](spec.md#assumptions); worth confirming during review whether the CLAUDE.md value is stale or whether spec 015 is renaming the base currency).

The feature builds on existing scaffolding from spec 002 (review/approval) for the reviewer surfaces, spec 010 (admin reports) for the CSV streaming output, spec 012 (localization), and spec 016 (Chromium-backed Syncfusion PDF). It does NOT introduce a managed Money library — see [research R1](research.md#r1-decimal-arithmetic-and-rounding-in-c) for the rationale.

Adjacent work that could collide: ongoing spec 014 (blob storage) and spec 017 (Azure SQL publish) — both touch Infrastructure but in different folders. No file-level overlap is expected.

---

## Spec Review Guide (30 minutes)

> Focuses your 30 minutes on the parts that need human judgment most. Each section points to specific locations and frames the review as questions.

### Understanding the approach (8 min)

Read [Clarifications](spec.md#clarifications) (Q1–Q4) and [Functional Requirements: Quote creation and conversion](spec.md#quote-creation-and-conversion). The core mental model is "snapshot at save time and never recompute". As you read, consider:

- The buy rate is stored as **CRC per 1 USD** (Costa Rican banking convention, [Q1](spec.md#clarifications)). USD→CRC conversion is `usd × buy_rate`. Is this the convention your future readers will expect, or does displaying the rate in the UI need an extra label?
- Quote currency is fixed at save ([FR-017a](spec.md#requirements)). Switching currency requires deletion + recreation. Does that match how applicants behave today, or will it generate support tickets?
- Editing the amount on an existing quote re-applies the **original** snapshotted rate, even when the currency was later disabled ([FR-017b](spec.md#requirements), [Q3](spec.md#clarifications)). Is this the desired user-perceived behavior, or would a "rate is stale, please review" prompt be safer?

### Key decisions that need your eyes (12 min)

**Snapshot embedding strategy** ([research R3](research.md#r3-snapshot-embedding-strategy), [data-model.md SupplierQuotes](data-model.md#supplierquotes--new-columns-existing-table))

Spec embeds the rate value, type, and effective timestamp directly on `SupplierQuotes` AND keeps an FK to the source `ExchangeRates` row. This duplicates ~30 bytes per quote line.
- Question for reviewer: is value-stability under DB restore/cross-environment moves a real concern here, or is the FK alone enough? The plan justifies belt-and-suspenders for [FR-026](spec.md#requirements) (PDFs must be value-stable across regenerations) — is that justification load-bearing?

**Immutable-once-used enforced only at the domain layer** ([research R2](research.md#r2-immutable-once-used-enforcement-on-exchangerate))

There is no DB-level CHECK preventing UPDATEs of a used rate row. The plan relies on `ExchangeRate.MarkUsed()` being the only mutator and `OnDelete(Restrict)` from the FK. Direct SQL or future repository code could still violate the invariant.
- Question for reviewer: is a CHECK constraint or trigger worth adding for defense-in-depth, or is the domain-only enforcement aligned with how the rest of the codebase handles invariants?

**Conversion preview is server-only** ([research R4](research.md#r4-conversion-preview-endpoint), [contracts/conversion-preview-api.md](contracts/conversion-preview-api.md))

Every keystroke (debounced 300 ms) hits `POST /SupplierQuotes/Convert`. No client-side rate cache.
- Question for reviewer: at the expected scale this is fine, but should there be a soft cache (e.g., the form preloads the latest rate into a hidden field at render time) to avoid 5–10 round-trips while users type?

**Migration uses `WHERE OriginalCurrencyCode IS NULL` as the idempotency guard** ([T016](tasks.md#phase-2-foundational-blocking-prerequisites), [research R6](research.md#r6-migration--post-deploy-strategy))

This guard works only because the new column is added in the same dacpac deploy. If the post-deploy script ever runs partially (e.g., a deploy fails midway), is re-running it safe?
- Question for reviewer: is there a stronger guard (e.g., a marker row in a `_DeploymentHistory` table) we should adopt for future complex post-deploy steps?

**Legacy queue UX is admin-only** ([User Story 6](spec.md#user-story-6---legacy-usd-quotes-are-flagged-and-quarantined-until-reviewed-priority-p3))

Legacy non-CRC quotes are excluded from cross-currency totals until an admin attaches a rate. End users see the original USD amount only.
- Question for reviewer: should there be an applicant-facing notice that "this quote is pending administrator review" so users don't think the system is broken?

### Areas where I'm less certain (5 min)

- The CLAUDE.md config table lists `FundingAgreement:CurrencyIsoCode = COP`, but this spec assumes the platform's base currency is **CRC**. I treated this as a CLAUDE.md staleness issue, but if the platform really runs on COP today, the migration in [T016](tasks.md#phase-2-foundational-blocking-prerequisites) and the seed in [data-model.md](data-model.md#currencies) need a name change. Worth a 30-second human check.
- [FR-021](spec.md#display-rules) says CSV exports must include both original-currency columns and the converted CRC column. [T416](tasks.md#phase-6-user-story-4--reviewers-approvers-and-dashboards-display-multi-currency-clearly-priority-p2) updates the spec-010 CSV endpoint generically — it does not specify whether new columns are appended (backward-compatible) or interleaved (cleaner). Existing CSV consumers may break either way.
- [FR-027](spec.md#final-agreement-pdf) requires the PDF refusal error to be displayed inline AND logged. [T512](tasks.md#phase-7-user-story-5--final-agreement-pdf-shows-crc-with-conversion-indicator-priority-p2) uses `TempData` for the inline message — this is fine for the same-request post-redirect-get, but will not survive a hard reload. Is that acceptable?
- The plan does not specify what happens to **active sessions** when an admin disables USD mid-edit. The form may have already rendered USD as a selectable option. Is "let the save fail with a validation error" the intended behavior, or should the form re-fetch the currency list on submit?

### Risks and open questions (5 min)

- If two admins create rates within the same `datetime2(0)` second ([data-model.md ExchangeRates](data-model.md#exchangerates) — note **second** precision, not millisecond), the unique index will reject the second attempt. Is `datetime2(3)` (millisecond precision) safer, or is the second-level coarseness intentional to make rate timestamps human-readable?
- [SC-005](spec.md#measurable-outcomes) ("admin can publish a new rate in under 2 minutes") has no automated verification approach in the plan. Is manual stopwatch acceptable, or should the [AdminExchangeRateE2E](tasks.md#phase-5-user-story-3--administrator-manages-enabled-currencies-and-exchange-rates-priority-p1) test record timing?
- The performance goal ([plan.md Technical Context](plan.md#technical-context)) of "conversion preview p95 < 200 ms" has no measurement method or perf-test task. Is the existing `scripts/` perf baseline tooling expected to cover this, or does it need a new task?
- Already-generated historical PDFs are NOT regenerated by migration ([FR-034](spec.md#migration--backward-compatibility)). If a user clicks "regenerate PDF" on an old request that contains a legacy-flagged quote, the PDF will refuse ([FR-027](spec.md#final-agreement-pdf)). Is that the intended UX, or is there an old-PDF-preserved-as-archive path missing?

---
*Full context in linked [spec](spec.md) and [plan](plan.md).*
