# Deep Review Findings

**Date:** 2026-06-11
**Branch:** 031-searchable-dropdowns
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** quality-gate (after_implement)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 4 | 4 | 0 |
| Minor | 10 | 6 | 4 |
| **Total** | **14** | **10** | **4** |

**Agents completed:** 5/5 (Correctness, Architecture, Security, Production Readiness, Test Quality). Security found no issues. External tools (CodeRabbit, Copilot): not installed — skipped.
**Scope:** the 24 source files of the spec-031 commit (the branch's pre-existing admin-cascade WIP was excluded, as it is not part of this spec).

## Findings

### FINDING-1
- **Severity:** Important
- **Confidence:** 88
- **File:** `src/FundingPlatform.Web/wwwroot/js/searchable-select.js` (`observe()` / `watchDocument()`)
- **Category:** production-readiness (also reported by: correctness as a slower-leak variant)
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:** Each enhanced `<select>` gets a dedicated per-select `MutationObserver` plus combobox listeners, but there was no teardown when the host subtree is discarded by an AJAX partial swap (e.g. the supplier-add legal-ID lookup does `region.innerHTML = html` repeatedly, detaching the enhanced Cantón/Distrito cascade selects). A `MutationObserver` retains its observed node, so the detached select + its `Controller` + combobox DOM could not be garbage-collected — an unbounded leak on long lookup sessions.

**Why this matters:** Memory growth on a real, reachable user path. Functional correctness is unaffected (native select stays authoritative), but it's a genuine resource leak.

**How it was resolved:** Added `Controller.prototype.dispose()` (disconnects the per-select observer, clears the `__searchableManaged`/`__searchableController` registry flags) and extended the document-level observer to walk `removedNodes`, disposing any enhanced select inside a removed subtree. Re-injected equivalents are re-enhanced fresh. Verified: the `SupplierLocationCascade` suite (which exercises the swap path) stays green.

### FINDING-2
- **Severity:** Important
- **Confidence:** 85
- **File:** `src/FundingPlatform.Web/wwwroot/js/searchable-select.js` (`commit()` / `enhance()`)
- **Category:** correctness
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:** If `select.value` was changed by something other than the enhancer's own `commit()` *without* a `childList` rebuild (e.g. a cascade restoring a server-selected value), the combobox input label would not re-sync — the visible label could disagree with the authoritative posted value.

**Why this matters:** Visual-integrity gap for the "pre-selected value displays correctly" case once a control is enhanced and later programmatically re-valued. No data corruption (native select still posts the right value), but the displayed label could mislead.

**How it was resolved:** `enhance()` now attaches a `change` listener on the native select that re-snapshots options and re-syncs the input label, guarded by a `_committing` flag so the enhancer's own commits don't double-fire. Re-snapshotting first avoids a stale-options read. Verified green across the cascade suites (which dispatch `change` on rebuild).

### FINDING-3
- **Severity:** Important
- **Confidence:** 95
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/SearchableDropdowns/` (suite gap)
- **Category:** test-quality
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:** FR-009 / SC-006 (static enum dropdowns excluded — show no search box) had zero test coverage. A regression that added `data-searchable` to an enum select would pass the whole suite.

**Why this matters:** SC-006 is an explicit success criterion with no guard.

**How it was resolved:** Added `EnumExclusionTests.EnumDropdown_IsNotEnhanced_NoSearchBox` — navigates to the Register form (whose Identification-type select is an enum named in FR-009) and asserts the select carries neither `data-searchable` nor `data-searchable-enhanced`, and that no `.fl-searchable-input` is rendered.

### FINDING-4
- **Severity:** Important
- **Confidence:** 88
- **File:** `tests/FundingPlatform.Tests.E2E/Tests/SearchableDropdowns/EditFormSearchTests.cs` (required-empty test)
- **Category:** test-quality
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:** `ProcessCreate_RequiredFundLeftEmpty_FailsServerValidation` claimed to prove FR-003 but was a wrong-reason pass — it only checked that a never-committed value stays empty (trivially true), never exercising the commit-then-revert path (commit a real value, type garbage, blur → revert to the prior value, not typed text).

**Why this matters:** The headline FR-003 regression (typed text leaking into the value, or blur stomping a real selection) was unguarded.

**How it was resolved:** Added `FilterToolbarSearchTests.Combobox_BlurAfterNoMatch_RevertsToCommittedValue` — commits a real Fund, types a no-match fragment, blurs, then asserts the committed value is unchanged and the input reverted to the committed option's label. (The original required-empty server-validation test is retained as separate coverage.)

## Minor Findings — fixed

- **Empty-state not announced (C-2/A-1, correctness+architecture):** the empty `<li>` had no `aria-live`, so AT heard only the bare count "0", not "Sin coincidencias". → added `aria-live="polite"` to the empty-state element.
- **Empty/"all" option pre-filled the input (C-3, correctness):** an unselected control showed the prompt label ("Todos los fondos") instead of an empty field with the placeholder. → added `displayLabel()` (empty for the empty value) used at enhance/refresh/blur/Escape sync points.
- **Threshold-boundary not asserted (T-3, test):** converted the comment-only "Provincia stays plain (7 options)" into a real `ToHaveCountAsync(0)` assertion on the province `-search` input.
- **Escape/Arrow untested (T-4, test):** extended the keyboard test to assert ArrowDown moves `aria-activedescendant` and Escape closes without committing.
- **Negative snapshot vs polling (T-6, test):** replaced a one-shot `AllTextContentsAsync()` negative assertion with a polling `Expect(...).ToHaveCountAsync(0)`.
- **Seed-cleanup backstop (T-7, test):** added `[OneTimeTearDown]` purges of `Spec031`-prefixed throwaway funds in both fund-seeding test classes.

## Remaining Findings (Minor — accepted, not blocking)

- **Hand-rolled `scrollIntoView` (A-2, confidence 72):** the manual list-scroll geometry could be `el.scrollIntoView({block:'nearest'})`. Current code is correct; left as-is (scopes scrolling to the listbox; native call would also work). Optional cleanup.
- **`fold()` duplication across the two enhancer files (A-3, confidence 70):** the 3-line accent matcher is duplicated in `group-drilldown-selector.js` (per research R9). The reviewing agent explicitly assessed this as acceptable (frozen es-CR contract, byte-identical with cross-referencing comments, house style is independent IIFEs). No change recommended.
- **Document observer scope (P-2, confidence 72):** the global observer runs `querySelectorAll` per body-subtree mutation rather than scoping to known dynamic regions. Acceptable for current pages; flagged as a future perf consideration if a high-churn page appears.
- **Drilldown filter gating not asserted (T-5, confidence 72):** the group-drilldown checkbox filter is intentionally always-on (FR-007 names it first-class) rather than threshold-gated. `CascadeSearchTests` exercises it with the 3 seeded groups; the always-on intent is documented in [tasks.md](tasks.md#implementation-deviations) and the [REVIEW-CODE.md](REVIEW-CODE.md) guide.

## Security

No issues. The agent verified all option/group labels are rendered via `textContent` (never `innerHTML` with user-influenced content); the user-supplied catalog flows view → `JsonSerializer` → Razor attribute encoding → `JSON.parse` → `textContent`/`setAttribute` with no HTML-parsing sink. The test-only SQL seed helper is fully parameterized. No new route/auth/schema (FR-005 holds).
