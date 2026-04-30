
---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the spec-013 implementation,
> focusing on high-level questions that need human judgment.

**Changed files:** 79 files changed (4 implementation commits on top of plan/tasks).
Roughly: 5 schema files, 9 domain/value-object files, 8 application-service files,
6 controller/viewmodel files, 7 view/partial files, 7 unit/integration test files,
2 E2E test/POM files, 13 spec/doc artifacts.

### Understanding the changes (8 min)

The reading order that makes the change shape clear:

1. Start with [`src/FundingPlatform.Domain/Entities/Supplier.cs`](../../src/FundingPlatform.Domain/Entities/Supplier.cs)
   — this is the aggregate root that owns all branch CRUD plus the lifecycle methods
   ([FR-021](spec.md#fr-021), [FR-024](spec.md#fr-024), [FR-035](spec.md#fr-035)).
   It is the single source of truth for the supplier invariants; everything else
   reads or mutates state through it.
2. Then [`src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs`](../../src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs)
   — the application-layer orchestration: `SearchByLegalIdAsync` applies the
   visibility filter, `AddBranchUnderExistingSupplierAsync` and
   `CreateDraftWithBranchAsync` are the write paths, and the `IsUniqueConstraintViolation`
   reflection trick avoids a `Microsoft.Data.SqlClient` reference from the
   Application layer.
3. Then [`src/FundingPlatform.Database/PostDeployment/SeedData.sql`](../../src/FundingPlatform.Database/PostDeployment/SeedData.sql)
   lines 127-201 — the inlined post-deployment migration with the three guard `THROW`s.
   - **Question:** Is the supplier aggregate the right place to enforce the
     "exactly one default branch" invariant when the SQL filtered unique index
     also enforces it? Two-layer enforcement is intentional but worth a fresh look —
     if a future feature ever needs to swap defaults atomically, the in-process
     check will need a Remove+Add transaction.

### Key decisions that need your eyes (12 min)

**Migration script lives inline in `SeedData.sql`, not as a separate `Migrations/013_*.sql` file** ([`SeedData.sql:128-201`](../../src/FundingPlatform.Database/PostDeployment/SeedData.sql), relates to [Phase 2 / T007](tasks.md#phase-2-foundational-blocking-prerequisites))

The plan ([R3 in research.md](research.md), reflected in [T007](tasks.md)) said the
migration would land in a separate `PostDeployment/Migrations/013_SupplierCatalog.sql`
referenced via `:r` from `SeedData.sql`. The implementation inlines the body in
`SeedData.sql` with a comment explaining that `Microsoft.Build.Sql 2.1.0` only
supports a single PostDeploy script. The behavior is identical; the file shape
differs.
- **Question:** Is the inline form acceptable for the team's long-term dacpac
  hygiene? A separate Migrations folder gives future migrations a natural home;
  inline now means the next migration repeats the same constraint discussion.

**`SupplierScore.ComputeForItem` does not mask compliance flags for non-Verified suppliers** ([`SupplierScore.cs:38-67`](../../src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs), relates to [FR-051](spec.md#fr-051))

[FR-051](spec.md#fr-051) requires that pending suppliers contribute zero points
to the four compliance/e-invoice factors. The implementation reads the supplier
flags directly: `bool ccss = q.Supplier.IsCompliantCCSS; ...`. In normal flow
this works because [FR-021](spec.md#fr-021) initializes Drafts with all flags
`false` and `Supplier.EditByAdmin` is gated by the `Admin` role. But
`Supplier.EditByAdmin` (lines 162-176) has **no status guard** — an admin who
toggles compliance flags on a `PendingReview` supplier without then clicking
Verify would persist a state where the score awards compliance points to a
non-Verified supplier.
- **Question:** Should `ComputeForItem` mask flags when
  `Supplier.VerificationStatus != Verified`, or should `Supplier.EditByAdmin`
  refuse to set compliance flags `true` while status is not `Verified`? Either
  fix closes the latent contract gap; the question is which layer owns the
  invariant. The current code passes all 113 E2E tests because the admin path
  edits-then-verifies in one sitting.

**`ApplicationService.SubmitApplicationAsync` walks every quotation's supplier
to flip Drafts** ([`ApplicationService.cs:85-105`](../../src/FundingPlatform.Application/Services/ApplicationService.cs), relates to [FR-024](spec.md#fr-024))

The submit transaction iterates all quotations on the application, loads each
supplier via `GetByIdWithBranchesAsync` (one round-trip per distinct supplier),
checks `(Status == Draft && CreatedByApplicantId == application.ApplicantId)`,
and calls `SubmitForReview()` then `UpdateAsync`. With `[Distinct()]` on
SupplierId this is `O(distinct suppliers)`.
- **Question:** At expected scale (5–10 quotations × 1–3 suppliers per app)
  this is fine. If applications grow to 50+ quotations, this becomes a
  significant N+1. Worth a follow-up to batch-load via
  `Where(s => supplierIds.Contains(s.Id))`?

**Applicant-side `EditDraft` and `EditBranch` actions exist as POST-only with
no GET surface, no view file, no UI affordance** ([`SupplierController.cs:259-321`](../../src/FundingPlatform.Web/Controllers/SupplierController.cs), relates to [US3 AS-3](spec.md#user-story-3---create-a-brand-new-supplier-in-draft-priority-p1) / [FR-013](spec.md#fr-013) / [FR-014](spec.md#fr-014))

Both POST endpoints exist, are wired to domain methods, and have ViewModels. But
there is no GET route, no `EditDraft.cshtml` / `EditBranch.cshtml`, and no link
or button anywhere in the applicant-facing views that posts to them. On
ModelState validation failure each action calls `View(model)` against a
non-existent view — that path will throw `InvalidOperationException` ("view not
found") at runtime. There is also no E2E test exercising these endpoints.
- **Question:** Are applicants supposed to edit Draft suppliers and own
  branches before submission via the existing Add flow only, or is a dedicated
  edit screen needed to satisfy [US3 acceptance scenario 3](spec.md#user-story-3---create-a-brand-new-supplier-in-draft-priority-p1)?
  Either delete the dead POST handlers (and clarify the spec) or add the GET
  surfaces and views.

**Migration parity test (`SupplierMigrationParityTests.cs`) does not exercise
the actual SQL migration script** ([`SupplierMigrationParityTests.cs`](../../tests/FundingPlatform.Tests.Integration/Persistence/SupplierMigrationParityTests.cs), relates to [T028](tasks.md) / [SC-003](spec.md#sc-003) / [SC-006](spec.md#sc-006))

[T028](tasks.md) called for an integration test that (a) seeds the OLD schema
state via raw SQL, (b) runs the actual migration body, (c) asserts byte-for-byte
`SupplierScore` parity, (d) measures wall-clock to assert SC-006's 60-second
budget. The implemented test is in-memory only, uses `EditByAdmin` to fabricate
a "pre" supplier, and never invokes any SQL. Its 1000-row "performance" test
measures EF Core in-memory SaveChanges, not the dacpac script.
- **Question:** Is SC-003 byte-for-byte parity protected anywhere else? The
  E2E suite (113 green) exercises the dacpac on each fixture spin-up but does
  not assert score parity against a pre-migration corpus. If a future change
  to the inline migration body breaks parity, would CI catch it before
  production rollout?

### Areas where I'm less certain (5 min)

- [`Quotations.SupplierBranchId` defaults to `0` at the column level](../../src/FundingPlatform.Database/Tables/dbo.Quotations.sql)
  ([dbo.Quotations.sql:10](../../src/FundingPlatform.Database/Tables/dbo.Quotations.sql)).
  The migration backfills real branch IDs and asserts no rows remain at 0/NULL.
  This is correct for a fresh deploy and a one-shot migration, but if a future
  bug ever inserts a `Quotation` without setting `SupplierBranchId`, the row
  would land with `SupplierBranchId = 0` and silently violate the FK
  (`FK_Quotations_SupplierBranches`) only at next FK validation time. The FK
  prevents it at write, but defaulting to a non-existent FK target is a
  smell.
- [`SupplierController.EditDraft` and `EditBranch` `View(model)` on validation
  failure](../../src/FundingPlatform.Web/Controllers/SupplierController.cs)
  with no view file — covered above. I'm not sure whether the spec intent was
  POST-only and these are dead, or whether a UI for them was dropped.
- [`SupplierCatalogService.IsUniqueConstraintViolation` uses reflection on the
  exception type name](../../src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs:185-205)
  to avoid a `Microsoft.Data.SqlClient` reference from the Application layer.
  This is intentional Clean Architecture compliance, but if a future provider
  swap (or a wrapped exception) ever changes the type name or the `Number`
  property, the R4 concurrent-insert recovery silently breaks and falls back
  to a generic 500.
- The `SeedData.sql` migration uses
  `IF EXISTS (SELECT 1 FROM sys.columns WHERE name = N'ContactName') AND NOT
  EXISTS (SELECT 1 FROM SupplierBranches)` as the idempotency guard. Once the
  `TODO[013-cleanup]` releases drop the legacy columns, the first part of the
  guard becomes false forever — meaning future fresh deploys will skip the
  migration entirely (correct behavior since there's nothing to migrate). Worth
  confirming before the cleanup PR lands.

### Deviations and risks (5 min)

The implementation is largely faithful to [plan.md](plan.md), but four deviations
are worth flagging:

- **Migration script location**: inline in [`SeedData.sql`](../../src/FundingPlatform.Database/PostDeployment/SeedData.sql)
  vs. the planned separate [`PostDeployment/Migrations/013_SupplierCatalog.sql`](plan.md#schema-dacpac).
  Justified inline by the `Microsoft.Build.Sql 2.1.0` single-script constraint.
  - **Question:** Is the comment/`-- See PostDeployment/Migrations/...` reference
    in `SeedData.sql` (which points to a non-existent file) misleading? Either
    create the separate file as a documentation-only copy or delete the
    reference.
- **Pending verification badge & rejected suppliers banner**: planned as
  separate partials [`_PendingVerificationBadge.cshtml`](tasks.md) /
  [`_RejectedSuppliersBanner.cshtml`](tasks.md); implemented inline in
  [`Views/Review/Review.cshtml`](../../src/FundingPlatform.Web/Views/Review/Review.cshtml).
  Functional behavior matches; reusability across other review screens is
  reduced.
  - **Question:** Are these partials likely to be reused by another review
    surface? If yes, extract; if no, inline is fine.
- **E2E coverage breadth**: [tasks.md](tasks.md) called for 7+ dedicated E2E
  test files (one per user story) under `tests/.../Tests/Suppliers/` and
  `Tests/Admin/Suppliers/`. The implementation ships 2 files
  ([`ApplicantReusesVerifiedSupplierTests.cs`](../../tests/FundingPlatform.Tests.E2E/Tests/Suppliers/ApplicantReusesVerifiedSupplierTests.cs)
  and [`SupplierCatalogTests.cs`](../../tests/FundingPlatform.Tests.E2E/Tests/Suppliers/SupplierCatalogTests.cs))
  totalling ~7 tests. US1, US3 (partial), US7 (partial), and US4/US5 (transitively,
  via the seed flow in `ApplicantReusesVerifiedSupplierTests.SeedVerifiedSupplierAsync`)
  are exercised. US2 (add new branch under existing supplier), US5 explicit
  reject path, US6 (admin edits Verified), US7 explicit filter switching, and
  the R4 concurrent-insert recovery path have no dedicated E2E coverage.
  - **Question:** [Constitution Principle III](../../.specify/memory/constitution.md)
    is "End-to-End Testing — NON-NEGOTIABLE." Does the transitive coverage
    via `SeedVerifiedSupplierAsync` count as US4/US5 happy-path E2E, or are
    dedicated tests required before this can ship?
- **Migration parity test**: in-memory C# instead of the planned raw-SQL
  integration test. SC-003/SC-006 protections are weaker than promised. See
  Decision #5 above.



---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-04-30 | **Rounds:** 2/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 4 | completed (orchestrator-applied; subagent dispatch unavailable) |
| Architecture & Idioms | 5 | completed |
| Security | 3 | completed |
| Production Readiness | 5 | completed |
| Test Quality | 6 | completed |
| CodeRabbit (external) | 0 | skipped (not installed) |
| Copilot (external) | 0 | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 7 | 7 | 0 |
| Minor | 14 | 0 | 14 |

### What was fixed automatically

Round 1 addressed all 7 Important findings:
- **Correctness:** Removed two unreachable POST handlers (`EditDraft`,
  `EditBranch` on [`SupplierController`](../../src/FundingPlatform.Web/Controllers/SupplierController.cs))
  whose `View(model)` validation-failure path crashed at runtime due to
  missing view files; deleted the orphaned [`EditDraftSupplierViewModel.cs`](../../src/FundingPlatform.Web/ViewModels/).
  Hardened [`IsUniqueConstraintViolation`](../../src/FundingPlatform.Application/Suppliers/Services/SupplierCatalogService.cs)
  to walk the entire exception chain (was missing the outer exception).
- **Architecture:** Removed a misleading `SeedData.sql` comment that referenced
  a non-existent `Migrations/013_SupplierCatalog.sql` file.
- **Production readiness:** Replaced the N+1 supplier-load loop in
  [`ApplicationService.SubmitApplicationAsync`](../../src/FundingPlatform.Application/Services/ApplicationService.cs)
  with a single batch-load via a new `ISupplierRepository.ListByIdsWithBranchesAsync`
  method. Added best-effort file cleanup in `AddQuotationToExistingBranchAsync`
  to prevent orphaned files when a Quotation save fails (orphaned Document
  rows remain a Minor follow-up — see [FINDING-15](review-findings.md)).
- **Test quality:** Rewrote the [`SupplierMigrationParityTests`](../../tests/FundingPlatform.Tests.Integration/Persistence/SupplierMigrationParityTests.cs)
  docstring to honestly describe its scope (in-memory score-math parity, NOT
  real-SQL migration verification). Added [`AdditionalUserStoryCoverageTests.cs`](../../tests/FundingPlatform.Tests.E2E/Tests/Suppliers/AdditionalUserStoryCoverageTests.cs)
  with three targeted E2E tests covering US5 reject-without-reason, US5 reject-
  with-reason+banner, and US7 status-filter-switching.

Round 2 re-review found one residual Minor (orphaned Document rows on
Quotation save failure) and zero new Important issues. Build clean (24
NU1902 warnings, all pre-existing OpenTelemetry vulnerabilities); 123/123
unit tests passing.

### What still needs human attention

14 Minor findings remain (see [review-findings.md](review-findings.md) for
details). Highlights worth a glance during code review:

- US2 (add new branch) and US6 (admin edits Verified) still lack dedicated
  E2E tests. They are exercised transitively inside other tests' seed flows
  but the spec-level coverage is incomplete. Question: is transitive coverage
  acceptable, or should follow-up tests be added before merge?
- The remaining Document orphan on Quotation save failure ([FINDING-15](review-findings.md))
  needs a single-transaction wrap or a periodic cleanup job. Question: which
  fix path fits the team's broader transaction-management pattern?
- The supplier-search route is not rate-limited ([FINDING-13](review-findings.md))
  and can act as a catalog-enumeration oracle for any authenticated applicant.
  Question: is per-user rate limiting in scope for this PR or a follow-up?
- The Quotations.SupplierBranchId column has a `DEFAULT (0)` that should be
  dropped after the migration ships ([FINDING-16](review-findings.md)). Already
  tracked under `TODO[013-cleanup]`.
- Several minor readability/observability improvements (extract helper methods
  in the 150-line POST `/Add` action, add structured logging to the admin
  Verify/Reject error paths, define typed exceptions in the catalog service).

### Recommendation

All Critical and Important findings were addressed automatically in 2 rounds.
14 Minor findings remain — none are blocking. The full E2E suite was last
confirmed green at 113/113 (commit c4005bb) before this review; the new
`AdditionalUserStoryCoverageTests.cs` file adds 3 new E2E tests that should
be run as part of the final verification gate. **Recommendation: proceed to
verification with the Minor findings tracked as follow-up issues.**

