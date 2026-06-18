# Code Review: Auditor Role + Provider Regulatory Compliance (Spec 038)

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on
high-level questions that need human judgment.

**Changed files:** ~50 source/view files (Domain enums + `Supplier`, EF config,
dacpac table + post-deploy script, 2 Application services + DTOs, 1 notifier,
admin controller + VMs + Detail view, reviewer review chain + shared partial,
email template, es-CR helpers) + role-rename sweep across controllers/views +
9 new/updated test files.

### Understanding the changes (8 min)

- Start with [`Supplier.cs`](../../src/FundingPlatform.Domain/Entities/Supplier.cs):
  the 4 compliance booleans are gone, replaced by 3 nullable enum statuses + per-field
  last-reviewed metadata + PME/warning/RowVersion, and two new rich-domain methods
  `ApplyRegulatoryEdit` / `ConfirmRegulatoryReviewed` that return `RegulatoryChange`
  records. This is the heart of the model change.
- Then [`SupplierComplianceService.cs`](../../src/FundingPlatform.Infrastructure/Services/SupplierComplianceService.cs):
  the audited-mutation orchestrator (load → domain method → one audit row per change →
  single `SaveChangesAsync`, with `RowVersion` optimistic concurrency).
- Question: the four booleans had a wider compile-time blast radius than
  [tasks.md](tasks.md) scoped (scoring, DTOs, repository filters, lookup badges). Is the
  re-sourcing strategy in [EVOLUTION.md §D-A](EVOLUTION.md) the right call, or should
  scoring have been neutralized entirely until slice B?

### Key decisions that need your eyes (12 min)

**Scoring re-sourced from "favorable" status, e-invoice point dropped**
(`SupplierScore.cs`, `RegulatoryStatusFavorability.cs`, relates to [FR-008](spec.md), [FR-009](spec.md))

The 3 compliance points now come from each status's favorable value (`al día` /
`sin sanciones`); max score 5 → 4. Scoring redesign is deferred to slice B.
- Question: is mapping "favorable" to the single `al día`/`sin sanciones` value the
  right interim semantic, or does it mislead reviewers who see the old recommendation
  pills until slice B?

**Audit `TargetId` = real supplier id** (`AdminAuditEventWriter.cs`, relates to [FR-012](spec.md))

The `supplier.` prefix parses `supplierId` out of the payload JSON and sets it as
`TargetId` (every other prefix uses the `"0"` sentinel) so the trail is queryable per
provider.
- Question: is parsing the id out of the payload acceptable, or should the writer API
  have taken an explicit target?

**Notifier trigger site** (`SupplierCatalogService.cs:CreateDraftWithBranchAsync`, relates to [FR-021](spec.md))

[tasks.md T038](tasks.md) named `CreateSupplierBranchHandler`, but that path is orphaned
(no live UI). The notifier fires from the live applicant create path instead. See
[EVOLUTION.md §D-C](EVOLUTION.md).
- Question: is the live path the correct (and only) "any creation path" for the current
  surface?

**Reviewer compliance/warning surface** (`_SupplierComplianceBadge.cshtml`, `ReviewService.cs`, relates to [FR-016](spec.md), [FR-019](spec.md))

A shared partial renders the warning banner + per-field status/freshness on the reviewer
Review screen, read-only.
- Question: the snapshot is threaded through `ReviewQuotationDto`; is that the right seam,
  or should reviewer-facing provider compliance be its own projection?

### Areas where I'm less certain (5 min)

- `_SupplierComplianceBadge.cshtml` / `ReviewService.cs` ([FR-016](spec.md)): the reviewer
  freshness line shows *when* + *source* but **omits "by whom"** — `ReviewService` has no
  user-name resolver and adding one to the core review projection felt too invasive for a
  foundation slice. The provider (auditor) screen shows the full "por {name}". This is the
  one place the implementation is partial against FR-016. Is omitting the auditor name on
  the reviewer surface acceptable, or worth the extra resolver?
- `AdminSuppliersController.Edit` ([FR-011](spec.md)): the `<select>` prevents free-text,
  but a hand-crafted POST of an out-of-range enum byte would bind (default MVC enum
  binding). Stored unknown codes render as "sin revisar". Worth an `Enum.IsDefined`
  guard, or is the UI constraint sufficient?

### Deviations and risks (5 min)

All deviations from [plan.md](plan.md) / [tasks.md](tasks.md) are catalogued in
[EVOLUTION.md](EVOLUTION.md) (boolean blast radius, `Name` in the command, notifier
trigger site, nullable notifier dependency, OC-tested-only-at-E2E, inline es-CR strings,
the FR-016 review-surface "by whom" gap).

- Optimistic concurrency (`RowVersion`) is enforced only against real SQL Server (E2E);
  the InMemory integration tests can't exercise it. Question: is E2E-only coverage of the
  concurrency branch acceptable?
- The Azure prod publish uses `--no-drop`; dropping the 4 BIT columns there must be handled
  deliberately (dev/E2E are greenfield and drop freely). Question: does the prod migration
  path need an explicit pre-step before this ships beyond dev?

---

## Deep Review Report

> Automated multi-perspective code review results.

**Date:** 2026-06-17 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 3 | completed |
| Architecture & Idioms | 5 | completed |
| Security | 0 | completed |
| Production Readiness | 4 | completed |
| Test Quality | 9 | completed |
| CodeRabbit (external) | — | skipped (not installed) |
| Copilot (external) | — | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 5 | 4 | 1 (accepted) |
| Minor | 16 | 14 | 2 (accepted) |

### What was fixed automatically

- **Robustness:** narrowed an over-broad `ArgumentException` catch; added an `Enum.IsDefined` guard on the `ConfirmReviewed` field; richer warning audit old/new delta.
- **Prod safety:** added a guarded post-deploy script (`06_DropLegacySupplierComplianceColumns.sql`) to drop the four legacy BIT columns under the Azure no-drop publish (closes the dev/prod drift risk).
- **Observability:** allowlist-dropped notifications + a per-call send summary are now logged; the email template cache is now process-wide.
- **Test coverage:** new isolated [`ProviderCreatedNotifierTests`](review-findings.md) (multi-auditor, body fields, FR-024 failure-non-blocking, no-auditors no-op); the FR-023 allowlist *drop* is now asserted in E2E; audit *payload contents* (field/old/new/source/kind) are asserted; warning-length, warning no-op, and `ReviewFreshness` singular/source-suffix branches now covered.
- **Clarity:** documented the favorability lossiness, the name-out-of-audit-scope decision, the enum-cast invariant, and corrected the stale "concurrency covered by E2E" comment.

### What still needs human attention

- **[FINDING-10](review-findings.md)** — the auditor notification sends synchronously on the applicant's create-supplier request. Accepted as a slice-A tradeoff (best-effort, small recipient set). Question: should this route through the spec-021 outbox/worker before the Auditor role grows?
- **[FINDING-20](review-findings.md) / [FINDING-21](review-findings.md)** — two E2E assertions are weaker than ideal (re-confirm timestamp-refresh proof; FR-020 non-blocking proven by rendering, not a completed decision). The behaviors are covered at the integration layer. Question: worth strengthening the E2E, or is integration coverage sufficient?
- The optimistic-concurrency conflict path is verified by neither unit nor integration tests (InMemory limitation), only by design. A real-SQL OC test is a reasonable follow-up.

### Recommendation

All Critical and Important findings are resolved or accepted with rationale. 3 Minor/accepted items remain (documented in [review-findings.md](review-findings.md) and [EVOLUTION.md](EVOLUTION.md)). Code is ready for human review with no known blockers.
