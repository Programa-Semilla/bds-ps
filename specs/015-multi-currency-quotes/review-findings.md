# Deep Review Findings

**Date:** 2026-05-07
**Branch:** 015-multi-currency-quotes
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** quality-gate (autonomous ship pipeline)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 0 | 0 | 0 |
| Minor | 4 | - | 4 |
| **Total** | **4** | **0** | **4** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality) — all five lenses applied.
**External tools:**
- CodeRabbit: skipped — CLI not installed (`which coderabbit` returned no path).
- Copilot: skipped — CLI not installed (`which copilot` returned no path).

Note: This deep review was performed in a single agent context (the parallel `Agent` tool was not available in the dispatching skill's tool surface). The five lenses were applied sequentially against the same loaded code rather than in isolated subagent contexts. This is a reduced-isolation execution but the spec was checked against the same files at the same depth a parallel dispatch would cover.

## Findings

### FINDING-1
- **Severity:** Minor
- **Confidence:** 75
- **File:** `src/FundingPlatform.Infrastructure/Persistence/Services/ConversionService.cs:14-19`
- **Category:** correctness (documentation accuracy)
- **Source:** correctness-lens
- **Round found:** 1
- **Resolution:** pending (Minor — not in fix-loop scope)

**What is wrong:**
The XML doc-comment claims:
> "The caller is responsible for invoking `ExchangeRate.MarkUsed` after their save commits — this service does NOT mutate state on the rate row, so a failed save will not orphan a MarkUsed update."

The literal claim is true for `ConversionService` itself, but the actual `MarkUsed` call lives in [`Quotation.SetCurrencyAndAmountAsync`](../../src/FundingPlatform.Domain/Entities/Quotation.cs#L119) (`Snapshot = result.Snapshot; result.Source.MarkUsed();`), which runs BEFORE `_applicationRepository.SaveChangesAsync()` at [`ApplicationService.cs:276`](../../src/FundingPlatform.Application/Services/ApplicationService.cs#L276). EF tracks the rate's `IsUsed=true` change as a pending update and flushes it atomically with the Quotation insert. The atomicity holds (single SaveChanges), so there's no orphaned `MarkUsed` — but the doc-comment reads as if MarkUsed happens after save commits, which is not what the code does.

**Why this matters:**
A future maintainer reading the doc-comment may assume `MarkUsed` is called from a post-commit hook and try to add a parallel "after-commit" step. The current behavior depends on EF's change-tracker atomicity, which is correct but undocumented at the call-site.

**How it was resolved (or how to resolve):**
Update the doc-comment to:
> "This service does NOT mark the rate used. The caller (typically `Quotation.SetCurrencyAndAmountAsync`) is responsible for calling `ExchangeRate.MarkUsed()` on the returned `Source`. EF's change-tracker ensures the rate's `IsUsed=true` flip and the Quotation insert commit atomically."

Not auto-fixed because it is a Minor doc-comment improvement and the autonomous loop targets Critical/Important only.

---

### FINDING-2
- **Severity:** Minor
- **Confidence:** 75
- **File:** `src/FundingPlatform.Web/Controllers/QuotationController.cs:193-202` and `:238-249`
- **Category:** architecture (layering)
- **Source:** architecture-lens
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`QuotationController.Convert` reaches `_dbContext.Currencies` directly to validate the currency code and `LoadEnabledCurrenciesAsync` queries `_dbContext.Currencies` directly to populate the dropdown. Both bypass `ICurrencyConfigService`, which exists, is registered (`Program.cs` per T046), and exposes `ListEnabledAsync()` and `ListAllAsync()`.

**Why this matters:**
The Web layer becomes coupled to the EF persistence layer instead of going through the application service that the rest of the codebase uses (e.g., `AdminCurrenciesController` correctly routes through `ICurrencyConfigService`). This duplicates the catalog-read logic in two places and means any future change to currency-resolution (e.g., caching, soft-delete) needs to touch both controllers and the projection helper.

**How it resolved (or how to resolve):**
Inject `ICurrencyConfigService` into `QuotationController` and replace both `_dbContext.Currencies` reads with `await _currencyService.ListEnabledAsync(ct)`. Keep `_dbContext` for application-data queries (applicants, ownership) only.

Not auto-fixed: Minor refactor, the autonomous loop targets Critical/Important only.

---

### FINDING-3
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Domain/Entities/Quotation.cs:78-82`
- **Category:** architecture (dead code)
- **Source:** architecture-lens
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`[Obsolete("Use ChangeCurrency(CurrencyCode, IConversionService) so the rate snapshot is reset.", error: false)] public void EditCurrency(string code)` is preserved "for callers in legacy code paths" per T025. A `grep -rn "EditCurrency" src/` shows no callers in `src/`. The `error: false` flag means a future caller would compile silently.

**Why this matters:**
Dead-code with `[Obsolete(error: false)]` invites accidental new use because the compiler emits only a warning. The "legacy callers" justification has no concrete trace.

**How it was resolved (or how to resolve):**
Either:
1. Identify the actual caller(s) and document with a `// Used by: <file>:<line>` comment, OR
2. Flip `error: true` so any new caller fails to compile, OR
3. Delete the method outright. The Obsolete-comment claims "legacy code paths" but if none exist, it's pure entropy.

Not auto-fixed: requires judgment about whether reflection-only callers exist (DI containers, test serializers).

---

### FINDING-4
- **Severity:** Minor
- **Confidence:** 70
- **File:** `src/FundingPlatform.Web/Controllers/Admin/AdminCurrenciesController.cs:106-118` and lines `55, 76`
- **Category:** security/UX (status-code precision)
- **Source:** security-lens
- **Round found:** 1
- **Resolution:** pending

**What is wrong:**
`TryParseCode` swallows `ArgumentException` (malformed currency code) and returns `false`, which the calling action turns into `NotFound()`. This conflates "currency code malformed" (should be 400) with "currency code valid but not in catalog" (correctly 404).

**Why this matters:**
Low security impact — no data leaks. Slight UX issue: a CSRF probe with `/Admin/AdminCurrencies/XYZZY/Disable` returns the same 404 as `/Admin/AdminCurrencies/USD/Disable` would for an unknown-but-well-formed code. Tooling that distinguishes 4xx by purpose (e.g., monitoring) sees the wrong category.

**How it was resolved (or how to resolve):**
Split the failure paths: return `BadRequest(new { error = "..." })` for malformed codes and `NotFound()` for not-in-catalog codes. The structural difference is:
```csharp
if (!CurrencyCode.TryFrom(code, out var parsed))
    return BadRequest(new { error = "..." });
if (!rows.Any(c => c.Code == parsed))
    return NotFound();
```

Not auto-fixed: Minor and requires a small new helper (`CurrencyCode.TryFrom`) plus updating tests.

---

## Informational (not findings)

### Note A — performance posture (production-readiness lens)
`ConversionService.GetLatestAsync` is uncached per call. With `IX_ExchangeRates_PairEffectiveAtDesc` covering the read and the spec's MVP scale (~daily admin rate publishing, low-thousands of quotations), this meets the 200 ms p95 target documented in [plan.md](plan.md). The Convert preview action and the save-time `SetCurrencyAndAmountAsync` issue independent reads — no per-request memoization. Worth tracking if rate publication frequency rises.

### Note B — `ApplicationCurrencyTotal.Compute` dual output (architecture lens)
Method returns `(decimal? Total, bool HasNonCrc)`. Two unrelated computations share one walk. Item counts are bounded so the perf cost is negligible, but the API would be cleaner as two pure functions. Code-smell only.

### Note C — `[Authorize]` and `[ValidateAntiForgeryToken]` audit (security lens)
- `QuotationController` (Applicant): all state-changing endpoints have `[ValidateAntiForgeryToken]`. The new `Convert` endpoint also has it.
- `AdminCurrenciesController` / `AdminExchangeRatesController` / `AdminLegacyQuotationsController` (Admin): all POSTs have `[ValidateAntiForgeryToken]`.
- `FundingAgreementController.Generate` (any auth): has `[ValidateAntiForgeryToken]`.
No bypass surface identified.

### Note D — test-quality spot-check (test-quality lens)
- `ExchangeRateTests.ConvertUsdToCrc_RoundsHalfAwayFromZero_{05Up,04Down}` pin both midpoint rounding cases for FR-014 / FR-020 (verified at lines 77-92).
- The plan's seven story-aligned E2E classes are all present in `tests/FundingPlatform.Tests.E2E/Tests/`.
- `MigrationTests.cs` covers idempotency and pre-/post-migration legacy stamping per T053 + T600.

## Remaining Findings

All four remaining findings are Minor. None block the gate. The autonomous fix loop only targets Critical/Important findings, so no auto-fixes were applied. Each Minor finding is documented above with a concrete fix suggestion for human review.

## Gate Decision

**PASS.** Zero Critical, zero Important. Four Minor findings recorded for human consideration during code review.
