# Deep Review Findings

**Date:** 2026-06-17
**Branch:** 038-auditor-provider-compliance
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** quality-gate (via /speckit-implement → review-code → deep-review)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 5 | 4 | 1 (accepted design tradeoff) |
| Minor | 16 | 14 | 2 (accepted) |
| **Total** | **21** | **18** | **3** |

**Agents completed:** 5/5 (Correctness, Architecture, Security, Production-Readiness, Test-Quality). External tools (CodeRabbit, Copilot): not installed — skipped.
**Security agent:** 0 findings (all checklist items verified PASS with file:line evidence — reviewer-cannot-edit-warning, antiforgery, HTML-encoding, no SQLi, role-deny matrix, dev-seam gating).

## Findings

### FINDING-1 — Broad `catch (ArgumentException)` could mis-map an unrelated error to "warning too long"
- **Severity:** Minor · **Confidence:** 75 · **Category:** correctness
- **File:** `src/FundingPlatform.Infrastructure/Services/SupplierComplianceService.cs`
- **Resolution:** fixed (round 1) — catch narrowed to `when (ex.ParamName == "warningNote")`; other ArgumentExceptions now propagate instead of being masked.

### FINDING-2 — Tampered `RegulatoryField` enum in `ConfirmReviewed` would 500 instead of a clean error
- **Severity:** Important · **Confidence:** 72 · **Category:** correctness
- **File:** `AdminSuppliersController.cs` / `Supplier.ConfirmRegulatoryReviewed`
- **Resolution:** fixed (round 1) — `Enum.IsDefined(field)` guard at the controller returns an es-CR "Campo regulatorio inválido." before reaching the domain `default:` throw.

### FINDING-3 — Warning note-only edit audited as "True → True" (non-informative old/new)
- **Severity:** Minor · **Confidence:** 70 · **Category:** correctness
- **File:** `Supplier.cs ApplyRegulatoryEdit`
- **Resolution:** fixed (round 1) — warning audit old/new now encode `"{flag}|{note}"`, so a note-only change records a visible delta.

### FINDING-4 — Unreachable `ReviewedNoChange` branch in `ActionFor` (dead code)
- **Severity:** Minor · **Confidence:** 85 · **Category:** architecture
- **Resolution:** fixed (round 1) — collapsed to `SupplierRegulatoryChanged` with a comment noting `ConfirmReviewedAsync` writes the reviewed action directly.

### FINDING-5 — Provider name change flows through the audited service but emits no audit row
- **Severity:** Minor · **Confidence:** 70 · **Category:** architecture
- **Resolution:** fixed (round 1, by documentation) — added a comment that name is intentionally out of the regulatory audit scope (spec audits regulatory/PME/warning only). Behavior unchanged by design.

### FINDING-6 — Implicit numeric coupling in `(RegulatoryChangeField)field` cast
- **Severity:** Minor · **Confidence:** 72 · **Category:** architecture
- **Resolution:** fixed (round 1) — added a comment documenting the shared 1/2/3 numbering invariant.

### FINDING-7 — `RegulatoryStatusFavorability` collapses distinct SICOP states (semantic loss)
- **Severity:** Minor · **Confidence:** 70 · **Category:** architecture
- **Resolution:** fixed (round 1, by documentation) — expanded the helper's XML doc to flag the lossiness ("sin suscripción" == "con sanciones") as an interim slice-A stopgap; redesign deferred to slice B. (A rename to `IsFullyCompliant` was considered but deferred to avoid call-site churn.)

### FINDING-8 — Stale "four shipped actions" doc comment on `AdminAuditEventCopyProvider`
- **Severity:** Minor · **Confidence:** 80 · **Category:** architecture
- **Resolution:** fixed (round 1) — comment updated to drop the stale count.

### FINDING-9 — dacpac drops 4 BIT columns but prod no-drop publish leaves them orphaned (dev/prod drift)
- **Severity:** Important · **Confidence:** 75 · **Category:** production-readiness
- **File:** `src/FundingPlatform.Database/Tables/dbo.Suppliers.sql`
- **Resolution:** fixed (round 1) — added idempotent, guarded post-deploy script `06_DropLegacySupplierComplianceColumns.sql` (wired via the sqlproj `None Include` + SeedData `:r`). No-op in dev (already dropped); performs the explicit drop in the no-drop prod publish.

### FINDING-10 — Auditor notification sends synchronously on the applicant's create-supplier request
- **Severity:** Important · **Confidence:** 78 · **Category:** production-readiness
- **File:** `SupplierCatalogService.cs` → `ProviderCreatedNotifier.cs`
- **Resolution:** **accepted (not changed) for slice A.** Best-effort, swallows failures, and the Auditor recipient set is small in slice A. Routing through the spec-021 outbox/worker (off-request dispatch) is the right long-term fix but is a larger change; deferred and recorded here + in EVOLUTION.md. Latency grows with auditor count — revisit if the role grows.

### FINDING-11 — Template cache was per-(scoped)-instance, defeating its purpose
- **Severity:** Minor · **Confidence:** 70 · **Category:** production-readiness
- **Resolution:** fixed (round 1) — `_cachedTemplate` made `static` so the template is read from disk once per process.

### FINDING-12 — Allowlist-dropped notifications were not logged (non-prod diagnosability gap)
- **Severity:** Minor · **Confidence:** 70 · **Category:** production-readiness
- **Resolution:** fixed (round 1) — `BlockedByAllowlist` now logs per-recipient, plus a per-call summary ("N sent, N blocked, of M auditors").

### FINDING-13 — FR-023 allowlist drop never asserted (only the allowlisted recipient checked)
- **Severity:** Important · **Confidence:** 90 · **Category:** test-quality
- **Resolution:** fixed (round 1) — `ProviderCreatedNotificationTests` now provisions a non-allowlisted `@example.com` auditor and asserts it receives nothing while the allowlisted seed auditor receives.

### FINDING-14 — `ProviderCreatedNotifier` had no isolated unit/integration coverage
- **Severity:** Important · **Confidence:** 88 · **Category:** test-quality
- **Resolution:** fixed (round 1) — new `ProviderCreatedNotifierTests` (InMemory + fake/throwing `IEmailSender`): one-message-per-auditor, body fields (name/legalId/link), FR-024 failure-non-blocking, and the no-auditors no-op.

### FINDING-15 — Audit payload contents (field/old/new/source/kind) never asserted
- **Severity:** Minor · **Confidence:** 85 · **Category:** test-quality
- **Resolution:** fixed (round 1) — `SupplierComplianceServiceTests` now deserializes the Hacienda audit row and asserts field/oldValue/newValue/source/kind/supplierId.

### FINDING-16 — Optimistic-concurrency path untested + false "covered by E2E" comment
- **Severity:** Minor · **Confidence:** 82 · **Category:** test-quality
- **Resolution:** fixed (round 1, comment) — corrected the misleading comment to state the OC conflict path is unverified by automated tests (InMemory can't enforce ROWVERSION); tracked in EVOLUTION.md §D-E. A real-SQL OC test remains a follow-up.

### FINDING-17 — Warning-note 1000-char guard untested
- **Severity:** Minor · **Confidence:** 80 · **Category:** test-quality
- **Resolution:** fixed (round 1) — added a domain unit test (throws) and a service integration test (es-CR failure result).

### FINDING-18 — No-op test covered only Hacienda, not the (complex) warning equality branch
- **Severity:** Minor · **Confidence:** 75 · **Category:** test-quality
- **Resolution:** fixed (round 1) — added warning no-op + mixed-change (only-changed-field) unit tests.

### FINDING-19 — `ReviewFreshness` singular "1 día" and Api/System suffixes untested
- **Severity:** Minor · **Confidence:** 72 · **Category:** test-quality
- **Resolution:** fixed (round 1) — added unit tests for the day==1 singular branch and the (API)/(sistema) suffixes.

### FINDING-20 — Re-confirm E2E doesn't strongly prove the timestamp refreshed
- **Severity:** Minor · **Confidence:** 70 · **Category:** test-quality
- **Resolution:** **accepted (minor).** The integration test asserts the timestamp refresh + actor change (`auditor-2`); the E2E confirms the action succeeds and freshness/value persist. Strengthening the E2E to a strict before/after delta is a low-value follow-up given the integration coverage.

### FINDING-21 — Non-blocking (FR-020) only asserted via review-surface rendering, not a completed decision
- **Severity:** Minor · **Confidence:** 70 · **Category:** test-quality
- **Resolution:** **accepted (minor).** FR-020 holds structurally — no code path gates the workflow on a warning, and the reviewer-cannot-edit assertion (FR-018) is explicit. Driving a full approve/advance in the warned-provider E2E is a possible future strengthening.

## Remaining (accepted, non-blocking)

- **FINDING-10** (sync email on create request) — deliberate slice-A tradeoff; outbox routing deferred.
- **FINDING-20, FINDING-21** — minor E2E assertion-strength items; the behaviors are covered functionally and at the integration layer.

No Critical or unresolved Important findings. Gate: **PASS.**
