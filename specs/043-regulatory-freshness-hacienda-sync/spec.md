# Feature Specification: Regulatory Freshness Gating + Hacienda API Sync

**Feature Branch**: `043-regulatory-freshness-hacienda-sync`
**Created**: 2026-06-21
**Status**: Draft
**Input**: feedback-3 slice D (master `seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md` §15.5, §16, §17, §24.7 reuse, §25.3; resolves §28.6 and §28.7). Builds on shipped slice A (`specs/038-auditor-provider-compliance/`).

## Overview

Slice A (spec 038) gave each provider per-field regulatory **freshness metadata** (`{Hacienda,Ccss,Sicop}LastReviewedAt` / `LastReviewedBy` / `LastReviewedSource`), an **audit trail** (`AdminAuditEvent` with old/new/kind/source), the `RegulatoryReviewSource` enum (with `Api`/`System` reserved), and a **"Reviewed — No Change"** re-authorize action — but it deliberately only **tracked and displayed** freshness. It did not *enforce* it and did not *keep it current automatically*.

This slice closes that loop with the two pieces A deferred:

1. **A staleness *block*** — an application cannot advance through the audit stage while any provider it relies on has a regulatory value that has not been reviewed within a configurable window (default one month).
2. **A daily automated Hacienda sync** — a scheduled job that consults the Costa Rican Hacienda tax-status API for every provider, updates the Hacienda status, refreshes its freshness metadata, and records audit history — keeping Hacienda current without manual auditor effort.

Plus the supporting surfaces: an early non-blocking warning so blocks don't surprise reviewers/auditors, visibility of sync failures, and a daily notification to auditors about stale values.

D **consumes** A's timestamp/audit/re-authorize seams unchanged; it adds no new regulatory model.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stale provider regulatory values block application advancement (Priority: P1)

When an auditor tries to advance an application through the audit stage (confirm/generate the funding agreement or release it for signature), the system checks the regulatory freshness of every provider the application relies on. If any required regulatory value (Hacienda, CCSS/Caja, or SICOP/CCOP) was last reviewed more than the configured window ago — or was never reviewed — the system blocks the advancement and tells the auditor exactly which provider and which field is stale, when it was last reviewed, and that an auditor must review/update or re-authorize it.

**Why this priority**: This is the core enforcement requirement (§15.5, §17). Without it, reviewers and auditors can approve funding while treating outdated compliance data as current — the exact risk the client raised. It is independently valuable even without the Hacienda automation: auditors keep CCSS/SICOP fresh manually today.

**Independent Test**: Seed an application that has reached the audit stage with a selected provider whose CCSS last-reviewed timestamp is older than the window. Attempt the auditor advance action; verify it is blocked with a message naming the provider, the field, and the last-reviewed date. Use the slice-A "Reviewed — No Change" action on that field, then retry the advance and verify it now succeeds.

**Acceptance Scenarios**:

1. **Given** an application in the audit stage whose selected provider has a CCSS value last reviewed more than the window ago, **When** the auditor attempts to confirm/generate the agreement or release for signature, **Then** the system blocks the action and shows a message naming the provider, the stale field (CCSS/Caja), and when it was last reviewed.
2. **Given** the same blocked application, **When** an auditor re-authorizes the stale field via "Reviewed — No Change" (refreshing its timestamp), **Then** the auditor can advance the application.
3. **Given** an application whose selected provider has never had a given required field reviewed (timestamp is empty), **When** the auditor attempts to advance, **Then** the system treats that field as stale and blocks with the same guidance.
4. **Given** an application relying on multiple providers with more than one stale field across them, **When** the auditor attempts to advance, **Then** the message enumerates every offending provider+field, not just the first.
5. **Given** an application whose providers all have all required fields reviewed within the window, **When** the auditor advances, **Then** no freshness block occurs.

---

### User Story 2 - Daily Hacienda sync keeps tax status current and audited (Priority: P1)

Every morning an automated job iterates all providers, looks up each provider's tax situation from the Hacienda API by its identification number, maps the result to the provider's Hacienda status, updates the value if it changed, and — in all successful cases — refreshes the Hacienda freshness metadata (timestamp, source = automated) and records audit history. Auditors no longer have to manually keep Hacienda fresh; the daily run both prevents Hacienda from going stale and preserves evidence that the validation occurred.

**Why this priority**: This is the automation half of the slice (§16). It is what makes "all three fields block equally" tolerable operationally — Hacienda is kept fresh by the machine while CCSS/SICOP remain manual. It is independently demonstrable: run the job, inspect the providers and the audit trail.

**Independent Test**: With a fake Hacienda lookup injected (the live API is never called in tests), seed providers with varied identifications, run the job once, and verify: changed statuses are updated and audited as automated changes; unchanged statuses still refresh the freshness timestamp and record that a check occurred; the Hacienda last-reviewed source is recorded as automated.

**Acceptance Scenarios**:

1. **Given** a provider whose current Hacienda status differs from the API result, **When** the daily job runs, **Then** the status is updated, the Hacienda freshness timestamp/source (automated) are refreshed, and an audit entry records the previous value, new value, and automated source.
2. **Given** a provider whose Hacienda status matches the API result, **When** the daily job runs, **Then** the value is left unchanged, the Hacienda freshness timestamp is still refreshed, and an audit entry records that the provider was checked and unchanged.
3. **Given** an identification the API reports as not registered, **When** the daily job runs, **Then** the provider's Hacienda status is set to the "no inscripción" status and the change is audited.
4. **Given** a successful daily run, **When** an auditor later views the provider during application review, **Then** the Hacienda freshness shows it was updated automatically (e.g. "al día — actualizado hoy por sistema").

---

### User Story 3 - Sync failures are visible, never silent (Priority: P2)

When the daily job cannot verify a provider — the API is unavailable, the identification is invalid or missing, or the response is unexpected — the system records the failure (without corrupting the provider's existing data) and surfaces it: a per-provider last-sync outcome on the supplier detail screen, a "verificación fallida" filter/badge on the admin supplier list, and an audit entry. Administrators and auditors can find exactly which providers failed automatic verification.

**Why this priority**: §16.4 requires that failures are never silently ignored, but this is operability around the core sync rather than the sync itself, so it ships just behind US2.

**Independent Test**: Inject a fake Hacienda lookup that throws/returns errors for specific providers. Run the job. Verify those providers' data is unchanged, their last-sync outcome shows the failure with a reason, the admin list can filter to failed providers, and an audit entry exists; verify successful providers are unaffected.

**Acceptance Scenarios**:

1. **Given** the API is unavailable for a provider during the run, **When** the job processes it, **Then** the provider's regulatory data is not changed, its last-sync outcome records the failure and reason, and the batch continues to other providers.
2. **Given** a provider with a missing or malformed identification, **When** the job processes it, **Then** the failure is recorded with a reason indicating the identification problem and no value is changed.
3. **Given** providers that failed verification, **When** an administrator opens the supplier list, **Then** they can filter or see a badge for "verificación fallida" and open each to see the failure reason and time.

---

### User Story 4 - Early warning + stale-value notification reduce friction (Priority: P3)

Reviewers and auditors see a non-blocking warning naming at-risk providers/fields **before** they hit the hard block — on the reviewer's send-to-audit screen and on the auditor screen — so staleness is visible early. Separately, a daily notification informs the relevant auditors of providers whose required regulatory values are stale, so they can act before an application is blocked mid-review.

**Why this priority**: Both are friction-reducers around the P1 block (§17.2 "visible before the final blocking point"; §25.3 "should consider notifying"). The block works without them, so they are lowest priority but close the operational loop.

**Independent Test**: Seed a provider with a stale field. Verify the reviewer send-to-audit screen and the auditor screen each show a non-blocking warning naming the provider+field. Run the notification step and verify a stale-value digest email is captured for the relevant auditor(s) via the existing mail-capture pipeline.

**Acceptance Scenarios**:

1. **Given** an application whose provider has a field within or past the staleness window, **When** the reviewer opens the send-to-audit screen, **Then** a non-blocking warning names the provider and field and indicates it is stale or near-stale (the action is still permitted at this step).
2. **Given** the auditor opens an application whose provider has a stale field, **When** the audit screen renders, **Then** the same warning is shown ahead of any advance attempt.
3. **Given** one or more providers have stale required regulatory values, **When** the daily notification step runs, **Then** a digest email listing those providers is sent to the auditors scoped to them, through the existing outbox + allowlist pipeline.

---

### Edge Cases

- **Never-reviewed field**: a null last-reviewed timestamp counts as stale and blocks (US1 #3).
- **Multiple stale providers/fields**: the block message and warnings enumerate all of them (US1 #4).
- **Concurrent auditor edit during sync**: the job uses optimistic concurrency; if an auditor edits a provider while the job is updating it, the job must not overwrite the auditor's change — it skips/retries that provider and the run still completes.
- **API outage spanning more than the window**: if the daily job keeps failing for a provider, its Hacienda value goes stale and (per §28.7 resolution) blocks application advancement just like CCSS/SICOP — the failure surface (US3) explains why.
- **Provider not referenced by any application**: still synced daily (coverage is all providers) but its staleness only matters when an application relies on it.
- **Application advanced past the audit stage already**: the freshness gate applies at the audit-stage advance actions; it does not retroactively block already-released/executed agreements.
- **Window reconfigured**: changing `FreshnessWindowDays` changes which existing timestamps count as stale immediately on the next check (no data migration).
- **"actividades" empty or regimen "No tiene"**: irrelevant to status mapping; only `situacion` drives the Hacienda status.

## Requirements *(mandatory)*

### Functional Requirements

**Freshness model**

- **FR-001**: The system MUST treat a provider regulatory field (Hacienda, CCSS/Caja, SICOP/CCOP) as **stale** when its last-reviewed timestamp is empty OR older than a configurable freshness window measured from the current time.
- **FR-002**: The freshness window MUST be configurable (`Regulatory:FreshnessWindowDays`) with a default of **30 days**. (Resolves §28.6: "one month" = configurable number of days, default 30.)
- **FR-003**: Staleness MUST be computed by comparing absolute instants so it is independent of display timezone.

**Blocking gate**

- **FR-004**: When an auditor attempts an audit-stage advance action that moves an application toward/into signature (confirm/generate the funding agreement, release for signature), the system MUST block the action if any provider the application relies on has any stale required regulatory field.
- **FR-005**: All three regulatory fields — Hacienda, CCSS/Caja, and SICOP/CCOP — MUST block equally when stale. (Resolves §28.7: Hacienda included; a Hacienda value that has gone stale, e.g. through repeated sync failure, blocks the same as CCSS/SICOP.)
- **FR-006**: The set of "providers the application relies on" MUST be the providers whose quotations are selected for the application's line items (the providers that would appear in the funding agreement). *(Exact selection semantics confirmed at plan; see Open Questions.)*
- **FR-007**: The block message MUST identify, for each offending provider, which provider, which field is stale, when it was last reviewed, and that an auditor must review/update or re-authorize the value before the application can continue. When multiple providers/fields are stale, all MUST be listed.
- **FR-008**: Re-authorizing a stale field via the existing slice-A "Reviewed — No Change" action (which refreshes the last-reviewed timestamp without changing the value) MUST clear the block for that field.
- **FR-009**: The freshness gate MUST be enforced server-side at the advance action so a crafted request cannot bypass it.

**Early warning**

- **FR-010**: The system MUST display a **non-blocking** warning naming at-risk providers/fields on the reviewer's send-to-audit screen and on the auditor screen, before the hard block is reached. The warning MUST NOT prevent the reviewer's send-to-audit action.

**Daily Hacienda sync**

- **FR-011**: The system MUST run an automated job **once per day at a configurable time (default morning)** that iterates all providers.
- **FR-012**: For each provider, the job MUST consult the Hacienda tax-status service using the provider's identification number, via a replaceable integration seam so that automated tests inject a fake and the live service is never called in tests.
- **FR-013**: On a successful lookup, the job MUST map the Hacienda result onto the provider's Hacienda status; if the mapped status differs from the current value, it MUST update the value.
- **FR-014**: On every successful lookup (changed or unchanged), the job MUST refresh the Hacienda last-reviewed timestamp and record the source as automated, and MUST write an audit entry — recording the previous and new value with automated source when changed, and recording that the provider was checked when unchanged. (Reuses slice A's `AdminAuditEvent` shape and `RegulatoryReviewSource.Api`.)
- **FR-015**: When the lookup returns a definite **200 `estado:"No inscrito"`**, the job MUST set the Hacienda status to "sin inscripción" and audit the change. An **HTTP 404** ("information not available") is distinct and MUST set "sin información" (not "sin inscripción") — see FR-016 / [research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1). *(Refined at plan: 404 ≠ "No inscrito".)*
- **FR-016**: The mapping from Hacienda result to status MUST be (resolved at plan, [research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1)): registered + not in arrears + not omitted → "al día"; registered + in arrears → "moroso"; registered + omitted (not in arrears) → "cobro administrativo"; de-registered + not in arrears → "desinscrito al día"; de-registered + in arrears → "desinscrito moroso"; **200 "No inscrito" → "sin inscripción"**; **HTTP 404 → "sin información"**; transport/5xx/timeout/unparseable/unrecognized-`estado`/malformed-local-id → **failure (no value change)**. "Desinscrito de oficio" is not distinguishable from `fe/ae` and is never auto-set (manual-only). *(Open Question 1 resolved.)*
- **FR-017**: The sync coverage MUST be all providers (per §16.2), processed in a way that a large catalog does not exhaust resources (batched/throttled). *(Batching detail at plan.)*

**Sync failure handling & visibility**

- **FR-018**: If a lookup fails (service unavailable, invalid/missing identification, unexpected response), the job MUST NOT change the provider's regulatory data and MUST continue processing the remaining providers.
- **FR-019**: Each provider MUST carry a last-sync outcome (attempt time, success/failure, and a failure reason when applicable) that the system records on every run.
- **FR-020**: The supplier detail screen MUST show the provider's last Hacienda sync outcome; the admin supplier list MUST let an administrator find providers whose last automatic verification failed (filter and/or badge).
- **FR-021**: Each sync failure MUST be recorded as an audit entry so failures are never silently ignored.

**Stale-value notification**

- **FR-022**: The system MUST send a daily notification (digest) listing providers with stale required regulatory values to the auditors scoped to those providers. Resolved at plan ([research D3](research.md#d3--stale-value-notification-daily-digest-direct-send-audit-pipeline-scoped-resolves-oq3)): the digest is sent **directly via `IEmailSender`** (the `StageExpiryReminderService` pattern, allowlist applied) **rather than the per-application transactional outbox** — a recurring multi-application per-auditor digest does not fit the outbox's per-application idempotency key, and this adds **no new `NotificationEvent`**. Scope: applications in the audit pipeline (`PendingAudit`/`ReturnedFromAudit`) → their `Group` → that group's `Auditor`-role members. (Resolves §25.3 toward "notify"; Open Question 3 resolved.)

**Cross-cutting**

- **FR-023**: All user-facing copy added by this slice (block messages, warnings, notification, failure labels, sync-outcome display) MUST be in es-CR.
- **FR-024**: The daily job MUST NOT crash the host on exceptions, and a single provider's failure MUST NOT abort the batch.
- **FR-025**: The job MUST use optimistic concurrency when writing provider updates so a concurrent auditor edit is not overwritten.

### Key Entities *(include if feature involves data)*

- **Provider (Supplier)** — existing slice-A aggregate. This slice **reads** its regulatory statuses and per-field freshness metadata to compute staleness, **writes** Hacienda status + Hacienda freshness metadata from the sync, and gains **last-sync outcome** information (attempt time, outcome, failure reason).
- **Regulatory audit entry (`AdminAuditEvent`)** — existing. This slice writes new entries for automated Hacienda changes/checks (source = automated) and for sync failures. No schema change to the audit model.
- **Hacienda lookup result** — transient value obtained from the external tax-status service for one identification: name, identification type, regime, and the `situacion` (estado / moroso / omiso / administración tributaria) that drives the status mapping. Not persisted as-is; only the mapped status + freshness metadata + audit history are.
- **Stale-value notification** — a daily digest message to auditors, composed from the set of currently-stale providers in each auditor's scope, dispatched through the existing notification outbox.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An application that relies on a provider with a stale required regulatory value cannot be advanced through the audit stage; the auditor is shown a message that names the provider, the stale field, and the last-reviewed date. (100% of stale-provider advance attempts are blocked.)
- **SC-002**: After an auditor re-authorizes a stale field, the previously-blocked application can be advanced — with no other change. (Re-authorization clears the block in 100% of cases.)
- **SC-003**: The daily job, run over a set of providers, updates every Hacienda status whose mapped result changed, refreshes the freshness timestamp for every successfully-checked provider (changed or not), and records one audit entry per check — verifiable from the audit trail with source = automated.
- **SC-004**: Every provider the daily job could not verify is discoverable afterward (per-provider failure reason on the detail screen and findable from the admin list), and none of those providers had their regulatory data altered by the failed attempt. (0% silent failures; 0% data corruption on failure.)
- **SC-005**: When providers have stale required regulatory values, a daily digest naming them is delivered to the scoped auditors through the existing mail pipeline.
- **SC-006**: A non-blocking warning naming at-risk providers/fields appears on both the reviewer send-to-audit screen and the auditor screen for applications relying on stale providers, ahead of any blocked advance attempt.

## Assumptions

- The Hacienda endpoint is `GET https://api.hacienda.go.cr/fe/ae?identificacion={id}`, requires no authentication, returns HTTP 200 with `{ nombre, tipoIdentificacion, regimen{codigo,descripcion}, situacion{moroso,omiso,estado,administracionTributaria}, actividades[] }`, and returns HTTP 404 for an unregistered identification. (Verified live against the documented example during brainstorming.)
- Only Hacienda has an automated source; CCSS/Caja and SICOP/CCOP remain manual-only (no equivalent public API), so their freshness depends on auditors using the slice-A edit / "Reviewed — No Change" actions.
- The slice-A regulatory model is sufficient and unchanged: per-field `LastReviewedAt/By/Source`, the `RegulatoryReviewSource` enum (with `Api`/`System` already reserved), the `AdminAuditEvent` audit shape, and the "Reviewed — No Change" action are all reused as-is.
- The audit stage (PendingAudit / ReturnedFromAudit and the auditor advance actions) from slice C (spec 040) exists and is where the hard gate is enforced.
- Auditor group-scoping (slice C / FR-017) governs which auditors are "scoped to" a provider for the stale-value notification, consistent with other auditor notifications.
- No new managed (NuGet) dependency is required: the live client uses the platform's built-in HTTP client facilities; the integration seam allows a test fake. (Reuse-first per project conventions; a new dependency would need separate approval.)
- "Morning" scheduling uses the application's operating timezone (Costa Rica); the exact run time is configurable/documented (§16.5). Staleness math itself is timezone-agnostic (FR-003).

## Dependencies

- **Slice A** (`specs/038-auditor-provider-compliance/`, shipped) — provides the regulatory fields, freshness metadata, audit trail, `RegulatoryReviewSource` enum, and "Reviewed — No Change" action this slice consumes.
- **Slice C** (`specs/040-auditor-workflow-stage/`, shipped) — provides the audit stage and the auditor advance actions where the hard block is enforced, and the auditor group-scoping used for notifications.
- **Email notification subsystem** (specs 021/028) — the outbox + allowlist + dispatch pipeline reused for the stale-value digest.
- **External**: the Hacienda `fe/ae` tax-status service.

## Out of Scope

- Other feedback-3 slices (E fund/process windows, F funding limits, G applicant timeline, H UX grab-bag).
- Any change to slice A's audit/timestamp/re-authorize model or to the regulatory enums themselves.
- Real-time / on-demand per-request Hacienda lookups during application review — only the daily batch (and existing manual auditor edits) update Hacienda.
- An automated API for CCSS/Caja or SICOP/CCOP — those remain manual-only.
- Retroactive blocking of applications already released for signature or with executed agreements.

## Open Questions

All three were resolved at plan time (research.md) and the resolutions are now folded into the FRs above.

1. **Less-common Hacienda mapping** — **RESOLVED** ([research D1](research.md#d1--hacienda-feae-contract--status-mapping-resolves-oq1), FR-016): full mapping table fixed via live sampling; 404→"sin información" distinct from 200 "No inscrito"→"sin inscripción"; `Inscrito`+`omiso=SI`→"cobro administrativo" (best-effort, stakeholder-confirm task T043); "Desinscrito de oficio" never auto-set.
2. **Referenced-provider selection semantics** (FR-006) — **RESOLVED** ([research D2](research.md#d2--referenced-provider-scope-for-the-gate-resolves-oq2)): the distinct `Supplier`s referenced by the application's approved items via `Item.SelectedSupplierId` (the agreement's counterparties), not every attached quotation.
3. **Notification cadence** (FR-022) — **RESOLVED** ([research D3](research.md#d3--stale-value-notification-daily-digest-direct-send-audit-pipeline-scoped-resolves-oq3)): daily digest, sent directly (not the outbox), scoped to audit-pipeline applications → group → auditors.
