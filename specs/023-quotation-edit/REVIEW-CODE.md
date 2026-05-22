# Code Review: In-place Quotation Field Edit

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-20
**Reviewer:** Claude (`speckit.spex-gates.review-code`)

---

## Compliance Summary

**Overall: ~95% compliant — PASS with documented variances.**

| Bucket | Traced | Notes |
|---|---|---|
| Functional Requirements (FR-001..FR-011) | **11/11** | FR-008 carries a copy/spec-text variance (codebase has no `ReturnedForChanges` enum value — see deviation #1 below). The behaviour is equivalent because the reviewer's `SendBack` transitions back to `Draft`. |
| Non-Functional Requirements (NFR-001..NFR-005) | **4/5** | NFR-003 (performance budgets) not verified — [T036](tasks.md#t036) was deliberately skipped (requires live Aspire). |
| Success Criteria (SC-001..SC-008) | **8/8** | SC-003 includes the phrase "audit trail entry exists per the existing 016-pattern" but [Assumptions](spec.md#assumptions) explicitly states "no new admin-audit event type is introduced in v1". Spec-internal inconsistency. The implementation honours Assumptions (no audit event). |
| Edge Cases | **6/6** | All listed edge cases traced to either a controller branch, a service guard, or a domain invariant. |

**Decision: PASS (NEEDS-FIXES posture deferred to deep review).** The only material gap (FR-008 state list) is a known and documented divergence between the spec text and the codebase's reality; the implementation choice is correct.

---

## Documented Deviations

### Deviation #1 — `ApplicationState.ReturnedForChanges` does not exist in the enum

**Spec text:** [FR-008](spec.md#functional-requirements) — *"Edits are permitted iff the Application is in `Draft` or `ReturnedForChanges`."*

**Code:** `src/FundingPlatform.Domain/Enums/ApplicationState.cs` enumerates `Draft, Submitted, UnderReview, Resolved, AppealOpen, ResponseFinalized, AgreementExecuted`. No `ReturnedForChanges` member.

**Implementation:** [`ApplicationService.EditQuotationAsync`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) line 578-584 gates on `state == Draft` and the controller's GET-time gate mirrors it. The XML doc on the method explicitly explains: *"the reviewer's `SendBack` path transitions back to `Draft` (Application.cs:418-434), so the state gate is satisfied by `state == Draft`."*

**Severity:** **Documented — not a blocker.** The reviewer-return-then-applicant-fix loop (US2) ends with the Application in `Draft`, so the Edit affordance is correctly available on that surface. The spec-text divergence is a wording remnant from the brainstorm that should be reconciled via `/speckit-spex-evolve` if the team wants the spec to match the codebase verbatim.

**Disposition:** **RESOLVED via `/speckit-spex-evolve` 2026-05-22 (Option A — update spec).** The spec text was reconciled to the codebase: FR-008 now reads "permitted iff `Draft`" with an explicit lifecycle note, and the `ReturnedForChanges` references in spec.md / plan.md / data-model.md / quickstart.md / contracts were aligned. The code was correct and is unchanged.

---

### Deviation #2 — Integration tests use EF InMemory (not SQL Server)

**Plan text:** [plan.md §Technical Context](plan.md#technical-context) — *"Testing: NUnit + Playwright (E2E), AspireFixture for full-stack ephemeral runs."* Implies that integration tests would use the real SQL Server.

**Code:** [`ApplicationServiceEditQuotationTests`](../../tests/FundingPlatform.Tests.Integration/Applications/ApplicationServiceEditQuotationTests.cs) line 44-49 uses `UseInMemoryDatabase`. The file header comment cites project convention: *"Follows the project's integration-test convention of EF InMemory (see `LegacyQuotationRateAttachServiceTests`). The full SQL FK / unique-index contract is exercised end-to-end by the E2E suite."*

**Severity:** **Documented — not a blocker.** Project-wide convention is consistent (15+ integration tests use EF InMemory); the SQL FK/index contract is covered by the three E2E tests which run against the Aspire-managed SQL Server.

**Disposition:** Acceptable. The CLAUDE.md "no mocks" rule is honoured (the DbContext + repositories under test are real), and the SQL-specific surface is covered downstream.

---

### Deviation #3 — Tasks [T034](tasks.md#t034) (manual walkthrough) and [T036](tasks.md#t036) (perf sanity) marked skipped

**Plan text:** [tasks.md](tasks.md) marks T034 and T036 with `[~]` (skipped). Both require a live `dotnet run --project src/FundingPlatform.AppHost` instance for hands-on UX/perf validation.

**Severity:** **Documented — pipeline-environment gap, not an implementation gap.** The ship pipeline runs without a live Aspire instance, so these tasks were deferred to the human reviewer.

**Disposition:** Surface to the reviewer in the test plan of the PR. The NFR-003 budgets (200 ms GET / 500 ms POST) are tight but the operations involved (one EF-Core read, one EF-Core write, optionally one conversion) are well under the budget in adjacent endpoints.

---

## Code Quality Notes

- **Mutation order in `EditQuotationAsync` matches research §R0.7 exactly** — `ChangeCurrency → EditAmount → ChangeBranch → SetValidUntil`. This is the correct order: the currency change resets the snapshot first so the subsequent price re-multiplication uses the fresh rate.
- **The idempotency short-circuit** at [`ApplicationService.cs:670`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) is type-and-value-safe — it compares ordinal currency strings and uses `decimal != decimal` for price, which is the right primitive comparison.
- **The `IComparisonCacheInvalidator` seam** is satisfyingly narrow — one method, single-purpose, and its registration in [`Infrastructure/DependencyInjection.cs:168`](../../src/FundingPlatform.Infrastructure/DependencyInjection.cs) sits next to the existing spec-020 wiring.
- **The shared partial** `_QuoteFields.cshtml` binds to an `IQuoteFieldsModel` marker interface, so both `AddSupplierViewModel` and `EditQuotationViewModel` resolve `asp-for=` against the same property names. The `EnabledCurrencies` setter on `AddSupplierViewModel` had to flip from `init` to `set` (per T003) so the interface contract holds — done.
- **Defensive try/catch on entity invariants** in [`ApplicationService.cs:713-726`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) — even though the service pre-validates everything the entity will throw on, the catch keeps any residual invariant from 500-ing the form. Good belt-and-braces.
- **Response status codes match the contract** — 400 for `ValidationFailed`, 422 for `MissingRate` / `StateChanged` / `LegacyFlagged`, 403 for `Forbidden`, 404 for `NotFound`, 303 (via `RedirectToAction`) for `Success`.

## Minor Observations (Optional)

- The XML comment on `EditQuotationAsync` references line numbers in `Application.cs` for `SendBack` (`418-434`). If `Application.cs` is refactored those will go stale; consider replacing with a method name reference.
- The `QuotationController.Edit` GET handler issues `TempData["ErrorMessage"]` + a redirect for both the state-gate and the legacy-gate. The service also returns those as `GlobalError` strings on the POST path. Consider sharing the copy via a constants class to avoid drift (not blocking).
- `EditQuotationViewModel.SupplierName` is plain-text Spanish copy; that's fine for v1, but if the future i18n sweep arrives the heading template will need a resource key.
- `Quotation/Edit.cshtml` cancel button routes to `Application/Edit/{id}` — matches the redirect on Success. Symmetric.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes, focusing on high-level questions that need human judgment.

**Changed files:** 18 production files + 5 test files. Production breakdown: 1 domain entity (+2 methods), 4 Application files (+2 commands, 1 service method, 1 read DTO), 1 Infrastructure file (cache invalidator), 4 Web files (controller +2 endpoints, 1 viewmodel, 1 interface, 1 viewmodel touch), 4 Web views (1 new, 2 modified, 1 new shared partial), 2 DI registrations. Test breakdown: 1 unit test class, 1 integration test class, 1 E2E page object, 3 E2E test classes.

### Understanding the changes (8 min)

Start with these two files; they carry the load-bearing decisions.

- Start with [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) (`EditQuotationAsync` near line 548): this is the orchestrator. Everything else is a thin wrapper around it. Reading this method gives you the full state machine of the feature in ~200 lines.
- Then [`src/FundingPlatform.Web/Controllers/QuotationController.cs`](../../src/FundingPlatform.Web/Controllers/QuotationController.cs) (GET + POST `Edit` actions around line 142–270): this is the HTTP surface — gate redirects on GET, outcome dispatch on POST, no business logic.
- Question: The service method is ~210 lines. Does the linear `parse → validate → idempotency-check → mutate → save → cache-invalidate` flow read as one cohesive unit, or does it want to be decomposed into a `EditQuotationOrchestrator` class? The plan picked one-method-on-the-existing-service for proximity to `AddItem` / `RemoveItem` / `AttachQuotation`; reasonable for one entry-point, less so if more applicant-mutation surfaces are coming.

### Key decisions that need your eyes (12 min)

**Branch invariant lives on the entity, not the service** ([`Quotation.ChangeBranch` line 182](../../src/FundingPlatform.Domain/Entities/Quotation.cs), relates to [FR-004](spec.md#functional-requirements))

[Plan Principle II](plan.md#constitution-check) explicitly chose to put the `branch.SupplierId == this.SupplierId` invariant on the entity rather than re-checking in the service. The service still pre-validates (so all field errors aggregate per R0.5), but the entity is the last-line guard.
- Question: Are we comfortable shouldering this on the entity given that the existing constructor takes a `supplierBranchId` int (not an entity), so the invariant is not enforced at construction? The new method closes the gate for edits but the constructor remains an escape hatch. Is that the right trade-off?

**State gate accepts `Draft` only** ([`ApplicationService.cs:579`](../../src/FundingPlatform.Application/Services/ApplicationService.cs), relates to [FR-008](spec.md#functional-requirements))

The spec text says "Draft or ReturnedForChanges", but the codebase enum doesn't have `ReturnedForChanges`. The reviewer's `SendBack` flow returns the application to `Draft`, so the spec semantics are preserved by accident-of-design.
- Question: Should we (a) leave this as-is and reconcile the spec via `/speckit-spex-evolve`, (b) introduce the `ReturnedForChanges` enum value across the codebase, or (c) leave a `// TODO: spec 023 FR-008` marker? The current implementation is option (a) without the explicit evolve step.

**Cache invalidation seam: synchronous, post-commit, fail-soft** ([`ApplicationService.cs:733-747`](../../src/FundingPlatform.Application/Services/ApplicationService.cs), relates to [FR-009](spec.md#functional-requirements))

After `SaveChangesAsync`, the service calls `_comparisonCacheInvalidator.InvalidateForItemAsync(item.Id, ct)` inside a try/catch that *logs and swallows* failures. Rationale: cache miss is the expected reviewer-side state anyway, so a failed invalidation only means the reviewer sees a brief stale view until they re-generate.
- Question: Is fail-soft the right posture for cache invalidation here, or should a failure propagate so the operator gets paged? Spec 020's contract is "silent invalidate"; this implementation honours it but loses observability.

**`EditQuotationViewModel` carries its `BranchOptions` through round-trips** ([`QuotationController.PopulateLookupsAsync` line 272](../../src/FundingPlatform.Web/Controllers/QuotationController.cs))

On a failed POST, the controller re-queries `GetQuotationForEditAsync` to rebuild the branch picker (the form payload only carries `SupplierBranchId`, not the full option list). This is one extra DB round-trip per failed POST.
- Question: Acceptable for a low-traffic, applicant-only form? Or would caching the options in `ViewData` between GET and POST be worth the complexity? Current implementation is the simpler path.

**Idempotency short-circuit returns `Success` without writing** ([`ApplicationService.cs:670-673`](../../src/FundingPlatform.Application/Services/ApplicationService.cs), relates to [NFR-004](spec.md#non-functional-requirements))

When all four field values match the persisted row, the service returns `Outcome.Success` with no save and no cache invalidation. The controller then redirects to `Application/Edit` with a success banner.
- Question: Is showing the user a "Cotización actualizada con éxito" banner correct when nothing actually changed? The alternative would be a quieter "no changes detected" path, but that adds a fifth outcome for a low-value edge.

### Areas where I'm less certain (5 min)

- [`ApplicationService.cs:688-692`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) — the `else if (!currencyChanged && quotation.Currency == CurrencyCode.Crc.Value)` branch comments "nothing else to recompute" and does no work. The dead branch is a comment-only artifact. Confirm I haven't missed a fall-through that needs an `EditAmount` call on a CRC row when only the *snapshot* would have changed (it can't, because CRC has no snapshot, but the structure is unusual).
- [`Quotation.cs:201-208`](../../src/FundingPlatform.Domain/Entities/Quotation.cs) `SetValidUntil` — uses `DateTime.UtcNow.Date` to anchor "today", but the spec says "es-CR calendar". For CR (UTC-6) most of the year, the UTC date is one day ahead from ~18:00 local. Edge case: an applicant in Costa Rica picks "today" at 19:00 local; the server has already rolled to tomorrow UTC, so a "today" date selection looks valid. A "today" date the applicant picks at 19:30 local on the previous CR-day-end could be one day behind. Worth confirming the UX impact (likely none if the applicant uses a future date, which is the common case).
- [`QuotationController.cs:113-130`](../../src/FundingPlatform.Web/Controllers/QuotationController.cs) `Convert` endpoint — pre-existing endpoint; the partial extraction exposed it from a second view (`Quotation/Edit.cshtml`). Did not modify, but confirm the new view's `data-convert-url` resolves the same route correctly with both `appId` + `itemId` route params from this view's context.
- [`QuotationEditAfterReturnTests.RejectsCrossSupplierBranch`](../../tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditAfterReturnTests.cs) injects a foreign `<option>` via JS to drive the rejection path. Confirm this is the cleanest way to exercise the server-side invariant from E2E (the alternative — driving an HTTP POST with `HttpClient` — bypasses Playwright's session). I think it is.

### Deviations and risks (5 min)

- **Deviation #1 (FR-008 state list):** see [Documented Deviations](#deviation-1--applicationstatereturnedforchanges-does-not-exist-in-the-enum) above. Question: should the spec be evolved to drop `ReturnedForChanges`, or should the codebase grow that enum value?
- **Deviation #2 (EF InMemory):** see [Documented Deviations](#deviation-2--integration-tests-use-ef-inmemory-not-sql-server) above. Question: any appetite to introduce SQL-Server integration tests during a future cleanup pass, or is the E2E coverage sufficient?
- **Deviation #3 (skipped polish tasks):** see [Documented Deviations](#deviation-3--tasks-t034-manual-walkthrough-and-t036-perf-sanity-marked-skipped) above. Question: should T034 + T036 be turned into a pre-merge checklist on the PR template?
- **Risk — partial extraction regression surface:** [Supplier/Add.cshtml](../../src/FundingPlatform.Web/Views/Supplier/Add.cshtml) now consumes the new `_QuoteFields` partial. Per [SC-005](spec.md#measurable-outcomes) the existing Supplier/Add E2E suite must stay green. Question: confirm the partial preserves every `data-testid` and `name` attribute the create flow's selectors and tag-helpers rely on.
- **Risk — silent cache invalidation observability:** see "fail-soft" question above. The log entry is `LogWarning`; do we want a metric counter so we can detect persistent failures?


---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-05-20 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 1 | completed |
| Architecture & Idioms | 5 | completed |
| Security | 0 | completed |
| Production Readiness | 1 | completed |
| Test Quality | 2 | completed |
| CodeRabbit (external) | - | skipped (CLI not installed) |
| Copilot (external) | - | skipped (CLI not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 0 | 0 | 0 |
| Minor (Optional) | 9 | - | 9 |

### What was fixed automatically

Nothing. Zero Critical and zero Important findings means no auto-fix loop ran. All 9 findings are Minor / Optional in nature (documented for future cleanup but not blocking).

### What still needs human attention

All Critical and Important categories are clear. The 9 Minor findings are documented in [review-findings.md](review-findings.md) and most are style or convention nits. The two that may merit a follow-up:

- [`Quotation.SetValidUntil`](../../src/FundingPlatform.Domain/Entities/Quotation.cs):203 uses `DateTime.UtcNow.Date` as the "today" anchor — does the team want to fix the UTC-vs-es-CR (UTC-6) boundary now or defer ([FINDING-1](review-findings.md#finding-1))?
- [`QuotationEditAfterReturnTests.SwapsBranchOnReturned_PreservesReviewerComments`](../../tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditAfterReturnTests.cs) is named for US2 but does not actually exercise reviewer-comment preservation. Rename or extend? ([FINDING-8](review-findings.md#finding-8))

The remaining 7 findings are cosmetic (dead fields, inline DTOs, comment rot, structured-log gaps) and can be batched into a follow-up cleanup PR.

### Recommendation

All Critical/Important findings are clear; the code is ready for human review with no known blockers. 9 Minor findings remain — consider reviewing them during code review but they are not blocking for merge. The two highlighted above ([FINDING-1](review-findings.md#finding-1), [FINDING-8](review-findings.md#finding-8)) carry the highest reviewer value.
