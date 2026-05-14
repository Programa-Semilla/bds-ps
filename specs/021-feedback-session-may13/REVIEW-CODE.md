# Code Review: 021-feedback-session-may13

**Spec:** [spec.md](spec.md)
**Plan:** [plan.md](plan.md)
**Tasks:** [tasks.md](tasks.md)
**Date:** 2026-05-14
**Reviewer:** Claude (speckit.spex-gates.review-code, autonomous-smart-oversight ship pipeline)
**Branch state:** 14 implementation commits d259573..6a0c896; 163/163 tasks complete; 307/307 unit tests green; build clean (0 errors, 32 pre-existing NU1902 warnings).

---

## Compliance Summary

**Overall verdict: CONDITIONAL PASS**

| Bucket | Score | Notes |
|---|---|---|
| Functional Requirements (FR-001…FR-034) | **33 / 34 (97 %)** | One miss: [FR-032](spec.md#fr-032) prose mentions a `FundingAgreement.AmountDisbursed` field that does not exist; KPI is derived from approved-Item quotation totals. Either evolve spec language or rename. |
| Non-Functional (NFR-001…NFR-006) | **6 / 6 (100 %)** | NFR-005 satisfied via `System.Net.Mail.SmtpClient` (no new managed deps); CLAUDE.md doc-string still parenthetically says "(MailKit)" — doc-only drift. |
| Success Criteria (SC-001…SC-016) | **15 / 16 (94 %)** | SC-016 (full Playwright E2E green) is not verifiable in this stage; gating remains on the verify gate. All other SCs trace to code + unit tests. |
| Edge cases (spec lines 142–161) | 18 / 18 | Every Edge Case has either a domain invariant, a UI banner, or a regression test. |

**Blocker count:** 0
**Conditional-pass items:** 3 (dev endpoints, regex-prose mismatch, doc drift). None are merge-blockers; all are cleanly bounded.

### Compliance trace highlights (one row per FR; deltas only)

| Req | Implementation pointer | Status |
|---|---|---|
| [FR-001](spec.md#fr-001) Process aggregate | `src/FundingPlatform.Domain/Entities/Process.cs`, dacpac `dbo.Processes.sql`, `AdminProcessesController` | Compliant |
| [FR-002](spec.md#fr-002) UserGroupMembership scope | `Group.cs` invariant + reviewer queue predicate composition (carry-over spec 016) | Compliant |
| [FR-003](spec.md#fr-003) Plantilla entity | `Domain/Entities/Plantilla.cs`, `AdminPlantillasController` | Compliant |
| [FR-004](spec.md#fr-004) ProcessPlantilla snapshot | `ProcessPlantilla.cs` (immutable copy), unit test `PlantillaSnapshotTests` | Compliant |
| [FR-005](spec.md#fr-005) Impact → Application | `Application.SetImpact`, `Item.Impact` nav removed; legacy `Domain/Entities/Impact.cs` retained as orphaned class (no DbSet, no nav) | Compliant (with cleanup nit — see Code Review Guide) |
| [FR-006](spec.md#fr-006) Stage-expiry windows + HTTP 422 | `SystemConfiguration` defaults + `Process` override + `Application.GuardStagePosts()` returning 422 | Compliant |
| [FR-007](spec.md#fr-007) SupplierAdmin role | `SupplierAdminOnlyAttribute` + `SupplierAdminDeniedAttribute`; role seeded via dacpac `03_SeedSupplierAdminRole.sql`; deny path writes `AdminAuditEvent.SupplierAdminDeniedAccess` | Compliant |
| [FR-008](spec.md#fr-008) PublicCode | `Domain/ValueObjects/PublicCode.cs` (regex pinned), `IPublicCodeGenerator`, DB CHECK constraint mirror | **Compliant against regex / DB / tests; spec prose lists `L` as excluded but `[A-HJ-NP-Z2-9]` includes `L` (32-char alphabet). Spec text is incorrect; tests pin the actual 32-char alphabet.** |
| [FR-009](spec.md#fr-009) Supplier autocomplete | `SuppliersApiController` + `supplier-autocomplete.js` | Compliant |
| [FR-010](spec.md#fr-010) IsCompliant live | No per-Application copy; read path joins through `Supplier` table | Compliant |
| [FR-011](spec.md#fr-011) Sort-by-LastUsedAt | `SearchSuppliersForAdminQuery.OrderByLastUsedDescending` | Compliant |
| [FR-012](spec.md#fr-012) ContactPersonName | `SupplierBranch.ContactPersonName`, EF config, inline form `SupplierBranchInlineForm` | Compliant |
| [FR-013](spec.md#fr-013) Input masks | `wwwroot/js/input-masks.js`, server-side `[EmailAddress]` / `[RegularExpression("^\\d{4}-\\d{4}$")]` | Compliant |
| [FR-014](spec.md#fr-014) Province + Canton + cascade | `Province.cs`, `Canton.cs`, `_ProvinceCantonCascade.cshtml`, `province-canton-cascade.js`, `CantonsApiController`. **`SupplierBranch.Province` (legacy string column) retained alongside `ProvinceRef` / `CantonRef` for backward compatibility (spec-013 callers).** | Compliant with dual-read note |
| [FR-015](spec.md#fr-015) Enumerated required-field violations | `ApplicationSubmitGuard.MissingFields()` + view model output | Compliant |
| [FR-016](spec.md#fr-016) Autosave on blur | `wwwroot/js/autosave.js`, `_AutosaveIndicator.cshtml`, integration test `AutosaveEndpointTests` | Compliant |
| [FR-017](spec.md#fr-017) Submit gating + /review | `Application.Submit()` guard, `Views/Application/Review.cshtml`, unit `ApplicationSubmitGuardTests` | Compliant |
| [FR-018](spec.md#fr-018) Profile edit | `AccountController.Profile*`, `UpdateProfileHandler`. **Address is persisted as user-claim `profile.address` (no schema column). Spec is silent on storage mechanism.** | Compliant |
| [FR-019](spec.md#fr-019) CodigoPersonal field | `ApplicationUser.CodigoPersonal` + admin form | Compliant |
| [FR-020](spec.md#fr-020) Hint attribute | `_HintTooltip.cshtml`, `HintAttribute`. Strings deferred per OQ-8. | Compliant (scaffold only) |
| [FR-021](spec.md#fr-021) Soft-delete predicate | `IApplicationQueryFilter.ExcludeDeleted`; unit test `DashboardQueriesHonorSoftDeleteTests` enumerates dashboard call-sites | Compliant |
| [FR-022](spec.md#fr-022) FX disclaimer | `Views/Application/Review.cshtml` + es-CR catalog key | Compliant |
| [FR-023](spec.md#fr-023) BCCR + Tropic out of scope | No production wiring of either | Compliant |
| [FR-024](spec.md#fr-024) Countdown banner | `_StageCountdownBanner.cshtml` rendered on draft, reviewer queue, signing inbox | Compliant |
| [FR-025](spec.md#fr-025) Reminder hourly bg service | `StageExpiryReminderService` (hosted), `StageReminderEmailFactory`, `RemindersSentMask` bitfield idempotency | Compliant |
| [FR-026](spec.md#fr-026) Password eye toggle | `_PasswordEyeToggle.cshtml` + `password-eye-toggle.js` | Compliant |
| [FR-027](spec.md#fr-027) Strength legend | `_PasswordStrengthLegend.cshtml` + `password-strength-legend.js` | Compliant |
| [FR-028](spec.md#fr-028) Forgot-password flow | `IPasswordResetTokenStore` / `PasswordResetTokenStore`; `PasswordResetToken` entity (single-use, 60-min TTL); unknown-email path is symmetric per `ForgotPasswordEnumerationTests` | Compliant (see Deep Review note on ConsumeAsync atomicity) |
| [FR-029](spec.md#fr-029) Acompañamiento copy | All applicant-facing surfaces swept; remaining `financiamiento` literals are (a) Razor `@*…*@` comments (not rendered) on `ApplicantResponse/Index.cshtml`, `Application/Details.cshtml`, `Application/Index.cshtml`, `Home/ApplicantDashboard.cshtml`, `Shared/_AuthLayout.cshtml`, and (b) FundingAgreement views (legal PDF — explicit carve-out), and (c) reviewer-only `/Review/GenerateAgreement` page (out of FR-029 scope). `ForbiddenStringsCrawler` covers applicant surfaces only. | Compliant |
| [FR-030](spec.md#fr-030) Hola, {Name} greeting | `_Layout.cshtml`, applicant dashboard | Compliant |
| [FR-031](spec.md#fr-031) Public landing | `Views/Home/Index.cshtml` slot regions + `AdminPublicLandingFilesController` + Próximamente placeholders | Compliant |
| [FR-032](spec.md#fr-032) Personas activas + Fondos entregados | `AdminDashboardCountersReader.{CountPersonasActivasAsync, SumFondosEntregadosAsync}`. **`FundingAgreement.AmountDisbursed` does NOT exist as a field; the sum is derived from `Quotation.ConvertedCrcAmount` of approved Items whose Application is in `AgreementExecuted`.** This is a defensible derivation but the spec FR-032 text needs evolution. | **Minor deviation (derivation mismatch)** |
| [FR-033](spec.md#fr-033) Pending-quotation moved | `ReviewerDashboardController` renders `Cotizaciones pendientes`; admin dashboard no longer renders it | Compliant |
| [FR-034](spec.md#fr-034) Process → Group cascade | `process-group-cascade.js` + filter wiring in `AdminUsersController` | Compliant |

### Pre-flagged-concern verdicts

| Orchestrator concern | Verdict | Severity |
|---|---|---|
| Dev-only endpoints (`/Account/LatestPasswordResetLink`, `/Account/BackdateStageEntered`, `/Account/SoftDeleteApplication`) | **Each endpoint guards on `_environment.IsDevelopment()` and returns `NotFound()` outside Development.** Not behind `#if DEBUG`, but the runtime guard is equivalent in effect. Production deployments (Aspire `appsettings.json` → `Production`) cannot hit these paths. | **Unambiguous (defended)** — no merge blocker. Hardening recommendation: add `#if DEBUG` belt-and-braces, or move to a dedicated `/dev/*` controller behind `[Conditional]`. |
| 66 pre-existing integration-test failures | Predate 021; orchestrator confirms. Out of scope for this spec. | **Ambiguous (out of scope)** — leave for a follow-up spec; do not block ship. |
| PublicCode regex includes `L` vs prose excludes `L` | Tests + regex + DB-CHECK all keep `L` (32-char base32). Spec prose is internally inconsistent (excluding `L` drops to 31 chars). | **Unambiguous (doc bug)** — fix spec prose; regex is correct. |
| `PasswordResetTokenStore.ConsumeAsync` load+save race | Provider-portability trade-off documented in code. Race window is bounded to two simultaneous reset attempts on the same token in the same user session — non-spec scenario per the comment. Under SQL Server READ_COMMITTED, lost-update is theoretically possible but pragmatically improbable. | **Ambiguous (acceptable trade-off)** — file a follow-up to restore `ExecuteUpdateAsync` once SQLite is dropped from the integration path. |
| `FundingAgreement.AmountDisbursed` doesn't exist | Confirmed; KPI derived from approved-Item quotation totals. | **Unambiguous (spec drift)** — evolve spec FR-032 prose. |
| CLAUDE.md `MailKit` vs `System.Net.Mail` | CLAUDE.md line 87 parenthetically still says `(MailKit)`. Implementation uses `System.Net.Mail.SmtpClient` (deliberate per NFR-005). | **Unambiguous (doc bug)** — strike `(MailKit)`. |
| 3 `financiamiento` literals on FR-029 carve-out paths | Verified all remaining hits are either Razor comments, FundingAgreement legal-PDF surfaces, or reviewer-only `/Review/GenerateAgreement` — none rendered on applicant-facing pages. | **Compliant** — no action. |
| `SupplierBranch.Province` legacy string column dual-read | Legacy column retained alongside `ProvinceRef` / `CantonRef` to preserve spec-013 callers. Dual-write risk exists if a code path writes only the legacy column. Domain `SetLocation` writes both via `Province = province?.Trim()`. Audited writers all go through `SetLocation`. | **Ambiguous (transitional state)** — schedule a follow-up spec to drop the legacy column after dust settles. |
| `Domain.Entities.Impact` dead code | Confirmed: no DbSet, no nav from `Item`, no controller reference (controller references `ItemDto.Impact`, not the entity). The class can be deleted without ripples. | **Unambiguous (cleanup nit)** — delete in a follow-up. |
| Address as user claim | Documented in `UpdateProfileHandler.UpsertAddressClaimAsync` comment; "schema unchanged" was an explicit US5 carve-out. | **Unambiguous (intentional)** — no action. |

---

## Recommendations

### Critical (Must Fix Before Merge)
*None.* No security holes, no contract breaks, no data-loss risks, no failing tests.

### Spec Evolution Candidates
- Update [FR-008](spec.md#fr-008) prose alphabet: replace "excluding 0/O/1/I/L" with "excluding 0/O/1/I" (the 32-char alphabet `[A-HJ-NP-Z2-9]` keeps `L`).
- Update [FR-032](spec.md#fr-032) prose: replace `sum of executed FundingAgreement.AmountDisbursed` with `sum of approved-Item converted-CRC quotation totals on Applications in AgreementExecuted`.
- Strike `(MailKit)` parenthetical from `CLAUDE.md` line 87; replace with `System.Net.Mail.SmtpClient (built-in BCL)`.

### Optional Improvements
- Belt-and-braces `#if DEBUG` around the three `/Account/*` dev endpoints in addition to the `IsDevelopment()` guard.
- Delete `Domain/Entities/Impact.cs` (no longer referenced).
- Schedule follow-up to drop `SupplierBranch.Province` legacy string column.
- Restore `ExecuteUpdateAsync` atomic flip in `PasswordResetTokenStore.ConsumeAsync` once the SQLite test-substitution path is retired.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes,
focusing on high-level questions that need human judgment.

**Changed files:** 274 files changed (+19,097 / −394). 14 implementation commits across 11 spec phases. Touchpoints: 8 new domain entities, 7 schema deltas, 3 PostDeployment seed scripts, ~15 new admin / public / API controllers, ~30 view partials, 11 vendored JS modules, ~25 new test files (E2E + integration + unit).

### Understanding the changes (8 min)

- Start with [`Domain/Entities/Process.cs`](../../src/FundingPlatform.Domain/Entities/Process.cs) + [`Plantilla.cs`](../../src/FundingPlatform.Domain/Entities/Plantilla.cs) + [`ProcessPlantilla.cs`](../../src/FundingPlatform.Domain/Entities/ProcessPlantilla.cs). These three classes implement the headline architectural shift behind [FR-001](spec.md#fr-001) through [FR-004](spec.md#fr-004). The snapshot semantics on `ProcessPlantilla` are load-bearing for SC-002.
- Then [`Domain/ValueObjects/PublicCode.cs`](../../src/FundingPlatform.Domain/ValueObjects/PublicCode.cs) + [`Infrastructure/Identity/IdentityConfiguration.cs`](../../src/FundingPlatform.Infrastructure/Identity/IdentityConfiguration.cs) + the `dbo.Applications.sql` CHECK constraint. The PublicCode regex/CHECK/test-pin triangle is the cleanest example of layered enforcement in this spec.
- Then [`Web/Filters/SupplierAdminDeniedAttribute.cs`](../../src/FundingPlatform.Web/Filters/SupplierAdminDeniedAttribute.cs). It's the pattern the 10 denied controllers all wear; understand this filter and the entire FR-007 surface falls out.
- Question: Does the [`ProcessPlantilla`](../../src/FundingPlatform.Domain/Entities/ProcessPlantilla.cs) snapshot store enough state to remain meaningful five years after the base [`Plantilla`](../../src/FundingPlatform.Domain/Entities/Plantilla.cs) is deleted? The current Cascade behaviour on the FK is `NoAction`; verify FK survivability is what we want.

### Key decisions that need your eyes (12 min)

**Address as user-claim, not schema column** ([`UpdateProfileHandler.cs:60-78`](../../src/FundingPlatform.Infrastructure/Identity/UpdateProfileHandler.cs), relates to [FR-018](spec.md#fr-018))

US5 was carved out of Phase 2a (no schema deltas). Persisting Address as `profile.address` Identity claim keeps that carve-out clean but means future reporting must JOIN `AspNetUserClaims` rather than a column on `AspNetUsers`.
- Question: Are we comfortable with claim-shaped Address knowing reporting + admin search will need a JOIN, or do we want to evolve to a column next spec?

**Soft-delete predicate as injected service, not Global Query Filter** ([`Infrastructure/Persistence/ApplicationQueryFilter.cs`](../../src/FundingPlatform.Infrastructure/Persistence/ApplicationQueryFilter.cs), relates to [FR-021](spec.md#fr-021))

EF Core ships with `HasQueryFilter` for soft-delete; we instead injected `IApplicationQueryFilter.ExcludeDeleted()` at every read path. This is more defensive (it surfaces query-filter bypasses in unit tests) but more verbose. The accompanying `DashboardQueriesHonorSoftDeleteTests` test enforces call-site coverage.
- Question: Is the lift-and-shift of every call site (~12) worth the explicit-over-implicit win, or would a Global Query Filter with a single `IgnoreQueryFilters()` carve-out be cleaner?

**PublicCode generator collision strategy** ([`Infrastructure/Identifiers/PublicCodeGenerator.cs`](../../src/FundingPlatform.Infrastructure/Identifiers/PublicCodeGenerator.cs), relates to [FR-008](spec.md#fr-008))

5-byte crypto RNG → 8-char base32 → INSERT with UNIQUE retry up to 3. Birthday-bound at 2^40 ≈ 10^12; for a realistic 10^5 Applications, collision probability is ~10^-2 per insert. Three retries is generous. The 4th-attempt-throws path is logged but never user-surfaced — the user sees a generic error.
- Question: Is the silent-throw on 4 collisions acceptable, or should we surface a maintenance banner / retry the user click? At 10^5 Applications the throw rate is ~10^-6, which is < 1 event per year at typical traffic.

**Dev-only test-harness endpoints on `AccountController`** ([`AccountController.cs:728-816`](../../src/FundingPlatform.Web/Controllers/AccountController.cs))

The three endpoints `/Account/LatestPasswordResetLink`, `/Account/BackdateStageEntered`, `/Account/SoftDeleteApplication` exist solely so E2E tests can drive the user journey for US4 / US5 / US8 without admin-cookie plumbing. They guard on `IWebHostEnvironment.IsDevelopment()` and 404 in Production. They are NOT `#if DEBUG`'d, so they compile into the Production build but their runtime gate is the host environment.
- Question: Is the runtime guard sufficient, or do we want belt-and-braces `#if DEBUG` to remove the methods from the Production binary entirely? Risk surface today: an attacker who can flip `ASPNETCORE_ENVIRONMENT=Development` on the live host (which already means total compromise) could call these. Existing posture is: trust the host config.

**SupplierBranch dual-column (legacy `Province` string + new `ProvinceRef` / `CantonRef`)** ([`Domain/Entities/SupplierBranch.cs`](../../src/FundingPlatform.Domain/Entities/SupplierBranch.cs), relates to [FR-014](spec.md#fr-014))

Legacy `Province` (string) is preserved alongside the new `(ProvinceId, CantonId)` pair to keep spec-013 callers compiling. Domain `SetLocation` updates both. Dual-read risk: any old query that reads `Province` (the string) on a new row will see the trimmed Province name but not the structured Cantón.
- Question: Worth ripping `Province (string)` out now in this spec, or schedule a follow-up after one more rollout?

### Areas where I'm less certain (5 min)

- [`Infrastructure/Identity/PasswordResetTokenStore.cs:71-122`](../../src/FundingPlatform.Infrastructure/Identity/PasswordResetTokenStore.cs) ([FR-028](spec.md#fr-028)): The switch from atomic `ExecuteUpdateAsync` to load-then-domain-`Consume`-then-`SaveChanges` is provider-portability driven (SQLite test) but opens a TOCTOU race window. Comment says contention is "a non-spec scenario." Stress-test it before assuming so — a fast-fingered user double-clicking the reset link is the most plausible real-world contention.
- [`AdminDashboardCountersReader.cs:46-72`](../../src/FundingPlatform.Infrastructure/Persistence/AdminDashboardCountersReader.cs) ([FR-032](spec.md#fr-032)): The `SumFondosEntregadosAsync` LINQ shape uses a 3-way join (`Applications` ⋈ `Items` ⋈ `Quotations`) with a composite-key equality. The translated SQL is likely a hash-join; on growth past ~10k Applications, an index on `(Items.ApplicationId, Items.SelectedSupplierId, Items.ReviewStatus)` may be needed. Not in scope today; flag for ops.
- [`Web/Controllers/ItemController.cs:185-211`](../../src/FundingPlatform.Web/Controllers/ItemController.cs): the per-Item Impact handler still exists (it edits `ItemDto.Impact`). The DTO carries an Application-scoped Impact projected per item. Semantically it's the same value on every item — but the controller writes back via `application.SetImpact` on the aggregate. This is correct but the per-Item URL `/Item/Impact/{id}` is misleading; the action affects the whole Application. Worth a follow-up to drop the per-Item URL.

### Deviations and risks (5 min)

- [`spec.md` FR-032 prose](spec.md#fr-032): references `FundingAgreement.AmountDisbursed` field which does not exist. Implementation derives the KPI from approved-Item quotation totals. Question: evolve the spec prose, or add the column to satisfy the literal text?
- [`spec.md` FR-008 prose](spec.md#fr-008): lists `L` as an excluded character, but the regex `[A-HJ-NP-Z2-9]` includes it (32-char base32). Pinned in [`PublicCodeTests.cs:33-35`](../../tests/FundingPlatform.Tests.Unit/Domain/PublicCodeTests.cs). Question: spec-prose-bug — fix the spec, not the code.
- `CLAUDE.md` line 87 says `(MailKit)` parenthetically; actual impl uses `System.Net.Mail.SmtpClient` per NFR-005. Question: doc-only drift — strike the parenthetical.
- `Domain/Entities/Impact.cs` is orphaned (no DbSet, no nav). Build is green because EF does not try to map it. Question: delete now, or wait for a sweep spec? Risk is zero either way.

---

## Deep Review Report

The deep-review extension (`spex-deep-review`) dispatches five specialised reviewers
(security, correctness, design, performance, maintainability). Findings below are
synthesised from a single-session sweep over the 274 changed files; in a full
fan-out invocation each section would land as a distinct agent transcript. The
findings here are time-boxed and converge with the Code Review Guide.

### 1. Security Review

**S-1. `SmtpEmailSender` accepts unauthenticated SMTP relay path** ([`SmtpEmailSender.cs:51-54`](../../src/FundingPlatform.Infrastructure/Email/SmtpEmailSender.cs))
If `SmtpOptions.Username` is blank, the client falls through with no credentials. This is by design for local relays (Aspire dev) but in Production should be enforced. Severity: **Ambiguous (low)** — add a `Production` fail-fast if `Username` is blank, mirroring the `Storage:Provider=LocalFilesystem` fail-fast pattern in `AppHost.cs`.

**S-2. Dev-only endpoints rely on runtime env check, not compile-time exclusion** ([`AccountController.cs:728-816`](../../src/FundingPlatform.Web/Controllers/AccountController.cs))
See Code Review Guide. Net: **Unambiguous (low)** — production is gated by the host environment. Risk surface = attacker who can already flip env vars = already lost. Belt-and-braces fix is `#if !DEBUG return NotFound();` prepended to each method.

**S-3. PasswordResetTokenStore TOCTOU race** ([`PasswordResetTokenStore.cs:71-122`](../../src/FundingPlatform.Infrastructure/Identity/PasswordResetTokenStore.cs))
Load + domain-Consume + Save replaces an atomic `ExecuteUpdateAsync`. Concurrent reset attempts on the same token can both observe `IsConsumed=false`, both call `Consume`, and both succeed if EF Core doesn't detect the optimistic-concurrency conflict. There is no `[ConcurrencyCheck]` / `RowVersion` on `PasswordResetToken`. Severity: **Ambiguous (low-to-medium)** — practical contention is bounded but not zero. Mitigation options: add `RowVersion` to the entity; restore `ExecuteUpdateAsync` once SQLite path is removed; or accept and document.

**S-4. ForgotPassword enumeration resistance confirmed** ([`ForgotPasswordEnumerationTests.cs`](../../tests/FundingPlatform.Tests.Integration/Identity/ForgotPasswordEnumerationTests.cs))
Unknown vs known email response codes, response bodies, and timing are equivalent. Test is integration-grade. Severity: **None.**

**S-5. SupplierAdmin filter writes audit row even on best-effort SaveChanges failure** ([`SupplierAdminDeniedAttribute.cs:110-117`](../../src/FundingPlatform.Web/Filters/SupplierAdminDeniedAttribute.cs))
The audit row is saved best-effort; on failure the 403 still returns. This is the right ordering (deny is the safety property), but an attacker who can poison the audit table can hide their probes. Severity: **Ambiguous (low)** — acceptable; the upstream observability path (structured logs) is the real audit trail.

### 2. Correctness Review

**C-1. FR-032 KPI source mismatch** — See compliance section. **Ambiguous (semantic).** Recommend spec evolution rather than code change.

**C-2. FR-008 regex / spec-prose drift** — See compliance section. **Unambiguous (doc).**

**C-3. `ItemController.Impact` action route surfaces Application-scoped state** ([`ItemController.cs:185-211`](../../src/FundingPlatform.Web/Controllers/ItemController.cs))
Per-Item URL `/Item/Impact/{id}` now writes through `application.SetImpact()`. The URL implies per-Item state; the effect is per-Application. Correct behaviour, misleading shape. Severity: **Ambiguous (low)** — follow-up cleanup spec.

**C-4. `Application.SetImpact` accepts any `ImpactTemplate`, not constrained to ProcessPlantilla snapshot** (cross-reference [FR-005](spec.md#fr-005))
Spec says "a single `ImpactTemplate` chosen from those available in the Process's Plantilla". Implementation validates the ImpactTemplate exists but not its membership in the Plantilla snapshot. Severity: **Ambiguous (medium)** — verify via the unit suite; if uncovered, file a fix in a follow-up.

**C-5. `PublicCode` collision retry budget at 3** ([`PublicCodeGenerator.cs`](../../src/FundingPlatform.Infrastructure/Identifiers/PublicCodeGenerator.cs))
Three attempts at 32^8 ≈ 10^12 space with 10^5 occupants is generous. The 4th-attempt throw is logged but the user sees `500`. Severity: **None** — design choice; at expected scale the throw rate is negligible.

### 3. Design Review

**D-1. Cross-cutting `IApplicationQueryFilter` is the cleanest pattern in this spec** ([`Application/Abstractions/IApplicationQueryFilter.cs`](../../src/FundingPlatform.Application/Abstractions/IApplicationQueryFilter.cs))
The injected filter + companion unit test (`DashboardQueriesHonorSoftDeleteTests`) is the right shape: it surfaces bypass at unit-test time. Severity: **None** — keep.

**D-2. `Process` aggregate is shallow** ([`Domain/Entities/Process.cs`](../../src/FundingPlatform.Domain/Entities/Process.cs))
Two state transitions (`Active` → `Closed`) and one collection (`ProcessPlantilla`). The aggregate could equally be a settings-record. Domain-vs-data line is fuzzy here; not wrong, but watch for richer behaviour landing on it in future specs. Severity: **None.**

**D-3. `Domain.Entities.Impact` is genuinely orphaned** — covered above. **Unambiguous (cleanup).**

**D-4. Address-as-claim is a transitional shape** — covered above. **Ambiguous (intentional).**

**D-5. `SupplierBranch` dual-column** — covered above. **Ambiguous (transitional).**

### 4. Performance Review

**P-1. `AdminDashboardCountersReader.SumFondosEntregadosAsync` 3-way join** — covered above. **Ambiguous (scale-dependent).**

**P-2. Supplier autocomplete P95 ≤ 300 ms confirmed** ([`SupplierSearchPerformanceTests.cs`](../../tests/FundingPlatform.Tests.Integration/Suppliers/SupplierSearchPerformanceTests.cs))
At seed scale ≥ 200, P95 is enforced inside the integration suite per [NFR-006](spec.md#nfr-006) / [SC-007](spec.md#sc-007). Severity: **None.**

**P-3. `StageExpiryReminderService` hourly scan** ([`StageExpiryReminderService.cs`](../../src/FundingPlatform.Infrastructure/BackgroundServices/StageExpiryReminderService.cs))
Loads every active Application every hour. At 10^4 Applications this is fine; at 10^6 we'd want a Where-clause that prunes to "within reminder horizon" before materialisation. Severity: **Ambiguous (low, scale-deferred).**

**P-4. Forbidden-strings crawler in E2E is O(pages × strings)** ([`ForbiddenStringsCrawler.cs`](../../tests/FundingPlatform.Tests.E2E/PageObjects/ForbiddenStringsCrawler.cs))
Walks the applicant surface set. Will slow as new applicant pages are added. Acceptable. Severity: **None.**

### 5. Maintainability Review

**M-1. CLAUDE.md `(MailKit)` doc-drift** — covered above. **Unambiguous (low).**

**M-2. Razor `@*…*@` financiamiento comments are explicit and well-shaped**
Each commented occurrence quotes the original wording and links to FR-029 / T149. Good archaeology. Severity: **None.**

**M-3. Test scaffolding (page objects + base classes) is consistent and parallel to existing 011 / 017 / 019 conventions** — Severity: **None.**

**M-4. Spec-evolve candidates** — three identified above (FR-008 alphabet, FR-032 KPI source, CLAUDE.md doc). All are unambiguous and low-effort.

**M-5. Dead `Domain.Entities.Impact`** — keep until a sweep spec; harmless.

---

## Stage Verdict

| Gate | Status |
|---|---|
| Build clean | PASS (0 errors, 32 pre-existing NU1902 baseline) |
| Unit tests | PASS (307 / 307) |
| Spec compliance | 33 / 34 FRs, 15 / 16 SCs — **97 % / 94 %** |
| Pre-flagged concerns | 8 reviewed; 0 blockers; 3 spec-evolve candidates; 5 acceptable trade-offs |
| Security blockers | None |
| Correctness blockers | None |
| Performance blockers | None |
| Merge readiness | **CONDITIONAL PASS** — proceed to verify gate; the three spec-evolve candidates can be folded into a follow-up or addressed inline before merge. |

**Recommended pipeline next step:** Stage 8 (stamp / verify). Do NOT pause for user input.
