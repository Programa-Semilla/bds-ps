
---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the 029-fund-entity implementation,
focusing on high-level questions that need human judgment. Compliance is 100%
(21/21 FRs); the points below are where reviewer expertise adds the most value.

**Changed files:** ~50 across Domain (Fund/Process/Application + enum/exception),
Application (IFundService, query-filter seam, report DTOs), Infrastructure
(FundService, EF configs, ReportQueryService, anchor resolution, freeze filter),
Web (AdminFundsController, FundRegulationController, Application/Process/Reports
controllers + views + resources), Database (3 schema objects + seed), plus unit/
integration/E2E tests and E2E fixture-compat fixes.

### Understanding the changes (8 min)

- Start with [`Fund.cs`](../../src/FundingPlatform.Domain/Entities/Fund.cs) and
  [`Application.cs`](../../src/FundingPlatform.Domain/Entities/Application.cs): the
  Fund aggregate (lifecycle + regulation invariants) and the new `GroupId` anchor +
  `IsFrozen` freeze overlay are the conceptual core.
- Then [`ApplicationQueryFilter.cs`](../../src/FundingPlatform.Infrastructure/Persistence/ApplicationQueryFilter.cs)
  (`ExcludeArchivedFund`) and its ~8 compose sites in
  [`ApplicationRepository.cs`](../../src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs).
- Question: the feature deliberately closed a latent data-model gap (apps were
  never anchored to a Process) under the "Fund" banner — is folding the
  [`Application.GroupId` anchor](spec.md#fr-017) into this feature still the right
  call now that you see the ripple (≈30 test sites, fixture changes)?

### Key decisions that need your eyes (12 min)

**Anchor is the Group, not the Process** (`Application.GroupId`, relates to [FR-017](spec.md#requirements-mandatory))
Process and Fund derive from `Group.Process.Fund`. Applicants hold *group* memberships, so the Group is the natural anchor — but it means an applicant in ≥2 groups of the *same* Process must still choose. Question: is the Group-level anchor + the "≥2 → required choice" UX ([FR-018](spec.md#requirements-mandatory)) acceptable, or should multiple groups under one Process auto-resolve?

**Null-tolerant freeze filter** (`ApplicationQueryFilter.ExcludeArchivedFund`)
The predicate guards each nav hop (`a.Group == null || ... || Fund.Status != Archived`) so EF emits LEFT JOINs and never silently drops rows with an incomplete chain. In production the chain is always complete (required FKs), so it filters exactly on status; the null-tolerance exists for robustness (and made EF-InMemory integration tests behave). Question: is the added robustness worth the slightly looser predicate, or would a strict INNER-JOIN form be clearer?

**Fund→Process relationship configured on the Fund side** (`FundConfiguration.cs`, deviation from [tasks T015](tasks.md))
tasks.md/data-model suggested mapping the FK in `ProcessConfiguration`; I configured it once in `FundConfiguration` (matching the codebase's existing Process→Groups convention). Functionally identical. Question: acceptable, or prefer the data-model's stated placement?

**Admin dashboard KPIs excluded archived-Fund apps** (`AdminDashboardCountersReader.cs`)
tasks T046 lists this reader as a freeze site, so both counters (Personas activas, Fondos entregados) now exclude archived-Fund apps. This slightly tensions with "admins retain visibility" ([FR-020](spec.md#requirements-mandatory) admin opt-out). Question: should "Fondos entregados" (a historical/financial total) keep counting delivered funds even after their Fund is archived?

### Areas where I'm less certain (5 min)

- `AccountController.ResetAdminFixture` ([dev-only E2E backdoor]): I added a full
  Application-subtree wipe in FK-safe order before the Groups delete (the new
  `Applications.GroupId` FK otherwise blocks it). The order is derived from the
  dacpac FKs — worth a second look that no referencing table was missed.
- `FundRegulationController.Download`: non-admins are gated to Active funds, admins
  may download an archived fund's regulation. The spec ([FR-010](spec.md#requirements-mandatory))
  only specifies the applicant (Active) path; the admin-archived allowance is my
  addition for catalog curation — confirm that's desirable.

### Deviations and risks (5 min)

- US3 (download) and US4 (freeze) have **no dedicated Playwright E2E class**
  ([T045](tasks.md)/[T051](tasks.md) annotated `[~]`): a US4 archive E2E would have
  to archive the *shared* seed fund, freezing every other test's data. Both are
  covered by the `FundServiceTests` integration test (archived-fund app hidden +
  restored) and production rendering. Question: is integration coverage sufficient
  for these two stories, or is a dedicated isolated-fixture E2E required before merge?
- One full-suite E2E flake (`ReviewerAndAdmin_BothSee_ResponseFinalizedApplication
  WithoutAgreement`, a pre-existing agreement-queue test) failed once on a 10s
  Playwright timeout and passes on isolated re-run — unrelated to this feature.
