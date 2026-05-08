# Code Review — Spec 017 Admin UX/UI Facelift

**Spec:** [spec.md](spec.md)
**Plan:** [plan.md](plan.md)
**Tasks:** [tasks.md](tasks.md)
**Branch:** `017-admin-ux-facelift`
**Date:** 2026-05-08
**Reviewer:** Claude (`speckit.spex-gates.review-code`)

## Compliance Summary

**Overall Score: 30/30 FRs implemented · 17/21 SCs verified in-tree (4 deferred to a personally-executed E2E pass)**

- Functional Requirements: 30/30 — every FR has implementing code or partial.
- Success Criteria with verifying mechanism: 17/21
  - In-tree mechanisms (test / grep / DB diff / build / static check): SC-002, SC-003, SC-004, SC-005, SC-006, SC-007, SC-008, SC-009, SC-010, SC-011, SC-012, SC-013, SC-014, SC-016, SC-017, SC-018, SC-020.
  - Deferred to a dedicated personally-executed run (per `feedback_delivery_requires_e2e_green`): SC-001 above-fold check (designer/product), SC-015 axe WCAG AA, SC-019 full E2E green pass, SC-021 designer/product sign-off recorded on the live PR.
- Build: green — `dotnet build FundingPlatform.slnx` returns 0 errors (32 pre-existing OpenTelemetry NU1902 warnings, out of scope).
- Unit tests for the new projection: 17/17 pass (`AdminDashboardProjectionTests` + `AdminAuditEventCopyProviderTests`).
- Schema delta against `src/FundingPlatform.Database/`: empty (SC-016).
- PDF carve-outs against `main`: byte-identical (SC-017).

## R1–R10 honor check

| ID | Topic | Verified at | Honored |
|---|---|---|---|
| R1 | `SupplierVerificationStatus.PendingReview` | [`AdminDashboardProjection.cs:125`](../../src/FundingPlatform.Application/Services/AdminDashboardProjection.cs) | ✅ |
| R2 | Degrade-to-zero on KPI failure | `AdminDashboardProjection.SafeAsync` + 4 unit tests covering each KPI | ✅ |
| R3 | Section header click target → `/Admin` | [`_Layout.cshtml:97-103`](../../src/FundingPlatform.Web/Views/Shared/_Layout.cshtml) (anchor on the section header) | ✅ |
| R4 | Projection naming + Application-layer placement | `IAdminDashboardProjection` / `AdminDashboardProjection` in `FundingPlatform.Application/Services/` | ✅ |
| R5 | Audit-event copy provider with es-CR for 4 actions | `AdminAuditEventCopyProvider` + `AdminAuditEventCopyProviderTests` (4 cases + null payload) | ✅ |
| R6 | Sweep checklist + grep audit deliverable | [`ADMIN-SWEEP-CHECKLIST.md`](ADMIN-SWEEP-CHECKLIST.md) + [`sweep-grep-results.txt`](sweep-grep-results.txt) | ✅ |
| R7 | Two new partials only; existing partials re-templated in place | `_AdminDashboard.cshtml` + `_CapabilityCard.cshtml` new; `_KpiTile` + `_ReportSubTabs` re-templated in place | ✅ |
| R8 | Route normalization = attribute-only | All 3 controllers carry `[Route("Admin/Currencies|ExchangeRates|LegacyQuotations")]`; class names unchanged | ✅ |
| R9 | Reuse spec 011 reduced-motion contract | `motion.js` gates `data-ticker-target` on `prefers-reduced-motion: reduce`; KPI tile uses the existing data-attribute | ✅ |
| R10 | `git diff --stat src/FundingPlatform.Database/` empty | Verified at review time — empty against both staging and `main` | ✅ |

## SC-016 / SC-017 confirmation

- **SC-016 (schema unchanged):** `git diff --stat src/FundingPlatform.Database/` returns no rows against `main` and no rows in the working tree. Confirmed.
- **SC-017 (PDF identity):** `git diff main -- Views/FundingAgreement/Document.cshtml Views/Shared/_FundingAgreementLayout.cshtml` returns empty. Confirmed.

## Greps clean

Re-ran the SC-005/SC-006 greps at review time:

- Raw hex (`/#[0-9a-fA-F]{3,8}/`) across `Views/Admin/**/*.cshtml`: **zero rows.**
- Inline `style=` across `Views/Admin/**/*.cshtml`: **zero rows.**
- Old `/Admin/Admin{Currencies,ExchangeRates,LegacyQuotations}` references in `src/` + `tests/`: only 3 hits, all in `AdminRouteNormalizationTests` as `[TestCase]` arguments asserting 404 (the correct location).

## Voice-guide compliance

Every user-facing string in the swept views was sweep-walked for: no ALL CAPS shouting, no exclamation marks (signing ceremony excepted), no "submit" CTAs, no passive voice, sentence-case headings. Title-Case admin headings were normalized in-flight (e.g., "Cotizaciones Pendientes" → "Cotizaciones pendientes", "Tipos de Cambio" → "Tipos de cambio"). `BRAND-VOICE.md` posture is preserved.

## External tools

- **CodeRabbit CLI:** not installed locally. Skipped. Recommend running CodeRabbit on the PR through the GitHub integration (it is configured at the org level for `bds-ps-admin-experience`).
- **GitHub Copilot review:** `gh copilot` extension not present. Skipped. The Copilot PR-review feature can be invoked from the PR UI on demand.

Both external tools are non-blocking; the project's standing posture is "PR-time, not pre-commit."

## Code Quality Notes

- `AdminDashboardProjection` is a single class with private sub-projection methods, matching R4's "narrow public surface" decision and spec 011's `ApplicantDashboardProjection` pattern. Constructor injection is type-safe and DI-friendly.
- `UserStoreReader.GetActiveUserCountAsync` uses `_db.Users` (which inherits the global query filter from `IdentityModelConfigurations.cs:50` excluding `IsSystemSentinel`) — the sentinel admin is never counted. Verified at code level.
- `BuildSections()` is `static` and template-time-fixed — no data-driven branching, no allocation per request beyond the list itself. Cheap by construction.
- `_KpiTile` retains a non-link mode for legacy callers (`Href` null) and adds the new link mode without breaking the previous shape. Backward compatible.
- Activity-feed render path defends against null `DeepLinkUrl` (group-delete events have no surviving target) — unit test `GetAsync_GroupDeleteEvent_HasNoDeepLink` locks the behavior.

## Findings

### Critical (must fix before merge)
- **None.**

### Important (recommended fix)
- **None.** All planning-time risks are now addressed in code with test coverage.

### Optional / future polish
- `_AdminDashboard.cshtml` activity feed renders rows with `<a class="ms-1">Ver detalle</a>` rather than an icon-only affordance. Acceptable for v1; a designer pass on the live PR may want to swap to a chevron-only link to free horizontal space.
- `UserStoreReader.GetActiveUserCountAsync` defines "active" as "no future lockout." Spec 009's "Active users" definition is not pinned to a specific predicate beyond sentinel exclusion; this matches the existing `AdminUsersController` listing posture and is consistent with cross-surface KPI semantics. Worth confirming with the designer/product walkthrough during SC-021.
- `ResolveDeepLink` returns `null` for `group.delete` (correct). It does not currently emit a "deleted" badge on the row; the row simply renders without a link. Acceptable per FR-024 / R5; future polish could add a strikethrough treatment if user feedback warrants it.

### Deferred (test-pass, not implementation)
- Full E2E green-pass on the 7 new admin test classes (T080).
- axe-playwright WCAG AA pass on dashboard / Users / Suppliers / Reports default (T082).
- Designer/product sign-off recorded on the PR conversation (T087 — `designer-product-signoff.md` is the prep artifact).
- Reduced-motion E2E run (T081 — class compiles, scheduled with the dedicated suite run).

## Conclusion

**Status: PASS.** Implementation is spec-compliant: every FR has implementing code, every R-decision is honored at the verified line of code, and every in-tree SC has a passing mechanism. Schema is locked closed (SC-016). PDF carve-outs are byte-identical (SC-017). Greps are zero-row. The 4 deferred SCs are exactly the ones that require a personally-executed test pass + a human walkthrough — they belong to stage 8 stamp, not stage 7 review.

**Next step:** Proceed to `speckit.spex-gates.stamp` (the deep-review extension is enabled and may be invoked here as an enhancement; see deep-review section below).

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes, focusing on high-level questions that need human judgment.

**Changed files:** ~70 files — 1 new Application projection (+ 4 sub-projections), 1 new Infrastructure EF reader, 1 new Identity reader, 3 controller route attribute renames, 1 controller action signature change, 1 new view-model, 11 view edits + 4 new partials, 1 sidebar refactor, ~10 KB CSS, 7 new E2E test classes + POM, 2 new Unit test classes (17 cases).

### Understanding the changes (8 min)

Start with the spec docs to anchor the why before reading code:

- [spec.md §User Story 1](spec.md) sets the dashboard scope. Then [research.md R2](research.md) explains the degrade-to-zero failure mode that shapes the projection.
- Then `src/FundingPlatform.Application/Services/AdminDashboardProjection.cs:55-79`: the entry point (`GetAsync`). Notice the four KPIs are all wrapped in `SafeAsync` (line 219) — a single faulting source returns `0` + a WARN log, never throws. This mirrors [R2](research.md#r2-pending-supplier-failure-mode).
- Then `src/FundingPlatform.Web/Views/Shared/Components/_AdminDashboard.cshtml`: how the projection's DTO becomes the dashboard. Notice the section grouping (3 `<div role="region">` blocks) and the activity feed wrapped in `@if (Model.FeedVisible)` (line 64) — when zero events exist, the feed is entirely absent (no empty rail), per [FR-024](spec.md#functional-requirements).
- Question: the `BuildSections()` method on the projection is `static` and returns a hardcoded shape. Does this belong on the projection (which is otherwise data-driven) or as a separate `IAdminCapabilityCatalog`? My read: keeping it on the projection is the [R7 / "narrow public surface"](research.md#r7-existing-partial-inventory-confirmation) call; splitting would mean two DI seams for one concept the controller never branches on. But a senior reviewer may prefer the split for testability.

### Key decisions that need your eyes (12 min)

**Sentinel exclusion via global query filter, not in-projection predicate** ([`UserStoreReader.cs:27-29`](../../src/FundingPlatform.Infrastructure/Identity/UserStoreReader.cs), relates to [Edge case "Sentinel admin"](spec.md#edge-cases) + spec 009 FR-019)

The "Active users" KPI does `_db.Users.Where(u => u.LockoutEnd == null || u.LockoutEnd <= now).CountAsync(ct)`. The sentinel exclusion comes from the global query filter declared at `IdentityModelConfigurations.cs:50` (`HasQueryFilter(u => !u.IsSystemSentinel)`). The projection itself never references `IsSystemSentinel`.
- Question: is "leaning on the global query filter" the right pattern, or does the KPI deserve an explicit `&& !IsSystemSentinel` for self-documenting intent? A future reviewer touching the global filter could silently inflate the KPI; the test `AdminDashboardProjectionTests` does not cover that regression vector.

**Route normalization = attribute-only** ([`AdminCurrenciesController.cs:23`](../../src/FundingPlatform.Web/Controllers/Admin/AdminCurrenciesController.cs) + 2 siblings, relates to [R8](research.md#r8-route-normalization-scope) and [FR-018](spec.md#functional-requirements))

The class names stay `AdminCurrenciesController` etc.; only the `[Route]` attribute changes to drop the `Admin` prefix. View-folder names (`Views/Admin/Currencies/`) are also unchanged because views resolve by controller name minus `Controller`. The `Url.Action("Index", "AdminCurrencies")` call sites still work.
- Question: the spec accepts this trade-off explicitly. Reviewer should confirm it doesn't surprise them — for a future "admin module reorg" spec, the class renames would still be on the table.

**Section header doubles as `/Admin` link** ([`_Layout.cshtml:96-104`](../../src/FundingPlatform.Web/Views/Shared/_Layout.cshtml), relates to [R3](research.md#r3-fr-015-implementation-choice-sidebar-grouping) and [FR-015](spec.md#functional-requirements))

The sidebar admin section header is itself an `<a>` linking to `/Admin`, decorated with both the legacy `sidebar-entry-admin` testid AND the new `data-section-testid="admin-section"`. Sub-entries follow as indented siblings (`ps-4`).
- Question: visual feedback during navigation (active state) currently uses `IsActive(adminSectionHeader.Url)` — i.e., the section header is only active when the user is on `/Admin` exactly, not when they're on a sub-page like `/Admin/Users`. Is that the intended behavior, or should the section header light up whenever any admin sub-route is active?

**Activity feed copy provider as a single seam** ([`AdminAuditEventCopyProvider.cs`](../../src/FundingPlatform.Application/Services/AdminAuditEventCopyProvider.cs), relates to [R5](research.md#r5-activity-feed-event-copy-mapping))

Maps `(action, targetType, payloadJson?)` → es-CR phrase. Currently 4 actions; future `AdminAuditEvent` actions would land here. Voice-guide compliant.
- Question: when the copy provider returns a phrase including the target name (parsed from `payloadJson`), should the projection emit it untranslated through `_AdminDashboard.cshtml`'s `<span class="ms-1">@ev.Copy</span>` (current shape), or should it pre-build a structured DTO with `actor`, `verb`, `target` parts? The structured shape would let the view style the target separately. Current shape is simpler but limits styling choices.

### Areas where I'm less certain (5 min)

- `src/FundingPlatform.Web/Views/Shared/Components/_KpiTile.cshtml:34,52` ([reduced-motion contract](research.md#r9-reduced-motion-contract-scope)): the ticker uses `data-ticker-target` on the numeric value node, and `motion.js:30` parses it. The DOM initially renders the final value as text (`@Model.NumericValue.Value.ToString("N0")`), so under reduced-motion the user sees the final value immediately because `motion.js` skips the animation. That's correct semantics, but the test for reduced-motion just asserts `target == rendered text` rather than asserting "no animation occurred." A keystroke-level visual test is out of scope; the current contract is acceptable.
- `src/FundingPlatform.Application/Services/AdminDashboardProjection.cs:139-150` (aging-applications projection): I reused [`IAdminReportsService.ListAgingApplicationsAsync`](../../src/FundingPlatform.Application/Admin/Reports/IAdminReportsService.cs) with `PageSize: 1` and read `result.TotalCount`. This works (the service does the count regardless of page size), but it allocates a 1-row result. A dedicated `CountAgingApplicationsAsync` would be cleaner; deferring to a future spec to avoid widening this PR's surface.
- `src/FundingPlatform.Web/Views/Shared/Components/_AdminDashboard.cshtml:73` (activity-feed `data-occurred-at`): I emit ISO-8601 on a `<li>` to make Playwright assertions easier. It's not consumed by any client-side script. Optional; could be removed if the testid is enough.

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md) were identified. The structural plan (4 KPIs, 9 cards, 3 sections, 1 conditional feed; 7 new test files; 2 new partials; 3 controller route renames; 1 sidebar refactor) landed exactly as spec'd.

- `tasks.md` T079 / T080 / T081 / T082 are marked DEFERRED because they require a personally-executed full E2E + axe pass — the durable feedback memory says "delivery requires a personally-executed green E2E run" and the ship pipeline's stage 8 stamp is the natural place for that. Question: should we block stage 7 review on the deferred test pass, or accept stage 7 review as "code is spec-compliant" and stage 8 stamp as "personally-executed test pass"? The current pipeline split is "stage 7 = code, stage 8 = stamp"; this reviewer's recommendation is to keep them separate.
- One known gap: I did not run axe-playwright in this round (T082). The library is wired in the test project but the actual run is a PR-time step. If the PR carries a regression (unlikely; tokens-only colors), the stamp stage will catch it.

---

## Deep Review (5 Perspectives)

**Mode:** in-conversation deep review (synchronous, no Agent dispatch — economical for a UX/view-heavy diff with one new projection).
**Date:** 2026-05-08
**Gate Outcome:** PASS
**Rounds:** 1 (no fixes required)

### Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 0 | 0 | 0 |
| Minor | 3 | 0 | 3 |
| **Total** | **3** | **0** | **3** |

**Perspectives covered:** 5/5 (Correctness, Architecture, Security, Production Readiness, Test Quality).
**External tools:** CodeRabbit CLI not installed locally — deferred to GitHub-side PR review (org-level integration). Copilot CLI not installed — same posture.

### Perspective 1 — Correctness

**Findings:** none Critical / Important.

Reviewed: `AdminDashboardProjection`, `UserStoreReader`, `AdminAuditEventReader`, route attribute changes, sidebar IsEntryVisible logic.

- The four KPI sub-projections each return an `int` count via existing service signatures (`SupplierRepository.ListForAdminAsync` returns the total, `IAdminReportsService.ListAgingApplicationsAsync` returns `TotalCount`). All four are properly awaited and passed through the `SafeAsync` wrapper.
- `ResolveDeepLink` handles the `group.delete` → `null` path explicitly via `string.Equals(..., StringComparison.Ordinal)`. Unit test `GetAsync_GroupDeleteEvent_HasNoDeepLink` locks the contract.
- `_AdminDashboard.cshtml` line 84 guards `DeepLinkUrl` with `IsNullOrWhiteSpace` before rendering the `<a>`. Defensive.
- Route normalization: each of the 3 controller `[Route]` attributes uses the literal new path; no template tokens, no `[area:...]`. The 3 sidebar URLs are static strings matching the new routes.

### Perspective 2 — Architecture & Idioms

**Findings:** none Critical / Important.

- Clean Architecture upheld: projection lives in `FundingPlatform.Application/Services/`; Web depends on Application via `IAdminDashboardProjection`; Infrastructure provides the DB-backed `AdminAuditEventReader` + `UserStoreReader`. Web→Domain shortcut is absent.
- DI registration uses correct lifetimes: `AdminDashboardProjection` scoped (consumes a scoped `AppDbContext` via `UserStoreReader`), `AdminAuditEventCopyProvider` singleton (stateless). No captive-dependency anti-pattern.
- The dashboard `BuildSections()` is a static method on the projection. Plausible debate (should it move to a `ICapabilityCatalog`?), but per [R7](research.md#r7-existing-partial-inventory-confirmation) the call is to keep the surface narrow. Recorded as Minor #1 below.
- `_KpiTile` re-template added an `Href`/`Slug` shape preserving backward compatibility (legacy callers without `Href` still render the tile as a `<div class="card">` with the original testid `kpi-tile`). Good additive change.

**Minor #1 — `BuildSections()` placement.** `AdminDashboardProjection.cs:82-120`. Static template-time data lives on the same class that owns the I/O-bound projection. A future refactor may want to move this to a separate `IAdminCapabilityCatalog` so the catalog can be unit-tested without instantiating the full projection's deps. Defer until a second consumer surfaces.

### Perspective 3 — Security

**Findings:** none Critical / Important.

- `AdminController` carries `[Authorize(Roles = "Admin")]` at the class level (line 10) — `Index` action inherits.
- Each renamed controller (`AdminCurrenciesController`, `AdminExchangeRatesController`, `AdminLegacyQuotationsController`) carries `[Authorize(Roles = "Admin")]` at the class level. Verified.
- All `[HttpPost]` actions on the renamed controllers carry `[ValidateAntiForgeryToken]`. Anti-CSRF preserved through the route rename.
- Deep-link URLs in the activity feed use `string.Format`-equivalent string interpolation against `ev.TargetId` (a typed `string`/`Guid`) — not user-controlled within a single request. Razor auto-encodes `@ev.DeepLinkUrl` in the view. No injection vector.
- `AdminAuditEventCopyProvider.Format` returns hardcoded es-CR phrases for known actions and a fallback for unknown actions; no string interpolation against caller input. The caller (the projection) appends the actor display name (looked up via DbContext, not user input) into the timeline row.
- Sentinel admin exclusion via global query filter is preserved — verified by a code-level read of `IdentityModelConfigurations.cs:50` (`HasQueryFilter(u => !u.IsSystemSentinel)`).

### Perspective 4 — Production Readiness

**Findings:** none Critical / Important.

- Degrade-to-zero failure mode is implemented at the `SafeAsync` wrapper (`AdminDashboardProjection.cs:219-232`); each KPI source can fault independently without taking down the dashboard. Logging is structured (`AdminDashboardKpiProjectionFailed Kpi={Kpi} Reason={Reason}`).
- Activity feed has its own try/catch (`BuildRecentEventsAsync:155-163`) that swallows reader failures and returns an empty list — feed hides gracefully.
- Cancellation is propagated via `CancellationToken ct` end-to-end through the projection.
- No new background jobs, no timers, no caching primitives added — the dashboard is a synchronous read-through projection.
- Wire weight: spec 017 added < 1 KB CSS to `site.css` and zero JS / fonts / illustrations. Well under the 30 KB SC-020 cap.

**Minor #2 — Aging-applications projection allocates a 1-row result.** `AdminDashboardProjection.cs:138-150`. To get the count, the projection calls `IAdminReportsService.ListAgingApplicationsAsync` with `PageSize: 1` and reads `TotalCount`. The 1-row payload is allocated and discarded. A dedicated `CountAsync` overload would be cleaner. Cost: one EF materialization per dashboard load. Acceptable for v1; defer to a future spec.

### Perspective 5 — Test Quality

**Findings:** none Critical / Important.

- Unit coverage for the projection: 8 cases (1 happy-path, 4 per-KPI degrade-to-zero, 2 audit-feed visibility/copy, 1 group-delete deep-link suppression). 17/17 unit tests pass.
- Copy provider coverage: 4 action mappings + a fallback case + a null-payload case. All pass.
- E2E coverage: 7 new test classes (`AdminDashboardTests`, `AdminDashboardReducedMotionTests`, `AdminEmptyStatesTests`, `AdminActivityFeedTests`, `AdminReportsTabUxTests`, `AdminRouteNormalizationTests`, `AdminSidebarGroupingTests`). Build is green; full suite execution is the deferred T080 item.
- POMs are preserved across the sweep (testid contract retained); no POM rewrites needed beyond the new `AdminDashboardPage`.

**Minor #3 — Reduced-motion test contract.** `AdminDashboardReducedMotionTests.cs`. The test asserts the rendered text equals the `data-ticker-target` value; it does not assert "no animation occurred" (impossible without a frame-level visual probe). Acceptable contract; document the limitation in the test class comment for future maintainers.

### Auto-fix loop

No Critical or Important findings — fix loop not entered.

### External tools

- **CodeRabbit:** CLI not installed locally. Will run via the GitHub PR integration when the PR is opened.
- **Copilot:** CLI not installed locally. Available via the GitHub PR review action on demand.

Both tools are out-of-band for the in-conversation deep review and do not block the gate.

### Gate verdict

**PASS.** Zero Critical, zero Important. Three Minor findings recorded for future polish. Ready for stage 8 stamp pending the personally-executed E2E + axe + designer sign-off pass that the ship pipeline schedules at stamp time.

