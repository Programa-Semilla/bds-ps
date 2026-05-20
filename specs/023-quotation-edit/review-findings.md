# Deep Review Findings

**Date:** 2026-05-20
**Branch:** 023-quotation-edit
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** superpowers (ship pipeline)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 0 | 0 | 0 |
| Minor (Optional) | 9 | - | 9 |
| **Total** | **9** | **0** | **9** |

**Agents completed:** 5/5 (Correctness, Architecture & Idioms, Security, Production Readiness, Test Quality)
**External tools:** CodeRabbit skipped (CLI not installed), Copilot skipped (CLI not installed)
**Agents failed:** none

---

## Findings

### FINDING-1
- **Severity:** Minor
- **Confidence:** 75
- **File:** [`src/FundingPlatform.Domain/Entities/Quotation.cs`](../../src/FundingPlatform.Domain/Entities/Quotation.cs):203, [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):643
- **Category:** correctness
- **Source:** correctness-agent
- **Round found:** 1
- **Resolution:** documented (optional fix)

**What is wrong:**
Both the `Quotation.SetValidUntil` entity invariant and the `ApplicationService.EditQuotationAsync` field-validation use `DateOnly.FromDateTime(DateTime.UtcNow.Date)` as the "today" anchor. Costa Rica is UTC-6; between 18:00 and 23:59 local time, `DateTime.UtcNow.Date` has already rolled forward one day relative to the user's local calendar.

**Why this matters:**
[FR-005](spec.md#functional-requirements) requires `ValidUntil ≥ today (es-CR calendar)`. An applicant in Costa Rica at 19:00 local time on day N picks "today" as the validity end date; UTC has already rolled to N+1, so a `< today` comparison interprets the user's "today" as "yesterday" and the guard rejects with *"La fecha de vigencia debe ser hoy o futura."* — surprising the user. Impact is small (most edits use future dates) but the spec explicitly calls out the es-CR calendar.

**How to fix it:**
Inject an `IClock` (or hardcode `TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica")` for v1) and compute `DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, crZone).Date)`. The codebase already has clock abstractions for spec 021 (`IStageExpiryClock`); reuse the pattern.

---

### FINDING-2
- **Severity:** Minor
- **Confidence:** 90
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):26-41
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** documented (style nit)

**What is wrong:**
`EditQuotationReadDto` and `EditQuotationBranchDto` are declared inline at the top of `ApplicationService.cs` rather than in `Application/DTOs/`. Other DTOs in the same project (e.g. `QuotationDto`, `ConversionPreviewDto`) live under `Application/DTOs/`.

**Why this matters:**
Convention drift makes future grep-based discovery harder. The file holding the orchestrator now also holds the read DTO contract, which is mixed responsibility.

**How to fix it:**
Move `EditQuotationReadDto` and `EditQuotationBranchDto` to `src/FundingPlatform.Application/DTOs/EditQuotationReadDto.cs`. Pure mechanical refactor.

---

### FINDING-3
- **Severity:** Minor
- **Confidence:** 85
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):26-41 (`SupplierId` field)
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** documented (dead code)

**What is wrong:**
`EditQuotationReadDto.SupplierId` is populated in `GetQuotationForEditAsync` but never read by the controller (the view binds against `BranchOptions` + `SupplierName`, not `SupplierId`).

**Why this matters:**
Carrying an unused field invites future readers to think it's load-bearing. Encourages "just in case" data plumbing.

**How to fix it:**
Drop the `SupplierId` parameter from the record and stop populating it.

---

### FINDING-4
- **Severity:** Minor
- **Confidence:** 80
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):688-692
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** documented (style)

**What is wrong:**
The `else if (!currencyChanged && quotation.Currency == CurrencyCode.Crc.Value)` branch has an empty body with only a comment ("CRC, same price, same currency — nothing else to recompute"). Dead structure.

**Why this matters:**
Empty branches with comments confuse readers — they look like a forgotten TODO. The logic is correct (no action needed in that case), but the if/else-if structure overstates the case.

**How to fix it:**
Remove the empty `else if` and move its comment outside the conditional. The block becomes just `if (priceChanged) { quotation.EditAmount(command.Price); }`.

---

### FINDING-5
- **Severity:** Minor
- **Confidence:** 70
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):540
- **Category:** maintenance
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** documented (will go stale)

**What is wrong:**
The XML doc on `EditQuotationAsync` references `Application.cs:418-434` for the `SendBack` method's transition behaviour. Line-number references go stale on any refactor of that file.

**Why this matters:**
Comment rot — a reader chasing the reference six months from now lands on the wrong code.

**How to fix it:**
Replace the line range with a method-name reference: *"the reviewer's `SendBack` method (`Application.SendBack`) transitions back to `Draft`."*

---

### FINDING-6
- **Severity:** Minor
- **Confidence:** 70
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):740-746
- **Category:** production-readiness
- **Source:** production-readiness-agent
- **Round found:** 1
- **Resolution:** documented (observability gap)

**What is wrong:**
Cache-invalidation failures are caught and `LogWarning`'d but no metric counter is incremented. If the spec 020 cache-store has a transient failure pattern in production, the team will not detect it until a reviewer complains about a stale artifact.

**Why this matters:**
[FR-009](spec.md#functional-requirements) says the invalidation is "silent" from the applicant's perspective, but that does not mean it should be silent from the operator's perspective. Without a counter or a structured log field that an alerting rule can match, the team is blind to a real failure mode.

**How to fix it:**
Either (a) add a counter via the existing OpenTelemetry instrumentation, or (b) annotate the log entry with a structured field like `event=cache-invalidation-failed` that an alerting rule can grep for.

---

### FINDING-7
- **Severity:** Minor
- **Confidence:** 75
- **File:** [`tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditPriceTests.cs`](../../tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditPriceTests.cs):115-118
- **Category:** test-quality
- **Source:** test-quality-agent
- **Round found:** 1
- **Resolution:** documented (test-shape gap)

**What is wrong:**
`RejectsZeroPrice_FieldErrorReRendered` strips the `min` and `type` attributes from the price input via `Page.EvaluateAsync` to skip HTML5 client-side validation and force a server roundtrip. The test rationale explains this as a workaround for Chromium racing the unobtrusive-validation script.

**Why this matters:**
The production user enters `0`, sees the HTML5 client validation message ("Please enter a value greater than..."), and never reaches the server. The test no longer exercises that real-user path. The test asserts the server-side message but a real user may never see it.

**How to fix it:**
Either (a) split the test in two — one that asserts the client-side path is reached and rejects with the unobtrusive validation message, and one that asserts the server-side message via the bypass — or (b) accept the trade-off explicitly in the test name (`RejectsZeroPrice_ServerSideFallback_FieldErrorReRendered`).

---

### FINDING-8
- **Severity:** Minor
- **Confidence:** 80
- **File:** [`tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditAfterReturnTests.cs`](../../tests/FundingPlatform.Tests.E2E/Tests/Application/QuotationEditAfterReturnTests.cs):42-81
- **Category:** test-quality
- **Source:** test-quality-agent
- **Round found:** 1
- **Resolution:** documented (coverage gap)

**What is wrong:**
`SwapsBranchOnReturned_PreservesReviewerComments` is named for [US2](spec.md#user-story-2--applicant-applies-a-reviewer-requested-correction-priority-p1) but it does not actually exercise the reviewer-return journey. The seed produces a `Draft` application (the lifecycle note in the test header acknowledges no `ReturnedForChanges` state exists). The test asserts the branch swap persists but the part of US2 that says *"the reviewer's existing feedback on that quotation is preserved (no soft-delete cycle)"* is not verified — there is no reviewer-comment in the seed and no assertion that one survives.

**Why this matters:**
The test name implies coverage that the assertions do not deliver. The integration-test counterpart (`EditQuotation_BranchChangeOnDraft_PersistsAndDoesNotTouchSnapshot`) suffers the same gap.

**How to fix it:**
Either (a) extend the seed to attach a reviewer comment to the quotation row and assert it is still present after the edit, or (b) rename the test to drop the "PreservesReviewerComments" suffix until the lifecycle gap is closed.

---

### FINDING-9
- **Severity:** Minor
- **Confidence:** 85
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs):560-564
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** documented (defensive depth)

**What is wrong:**
`EditQuotationAsync` returns `Outcome.Forbidden` when `application.ApplicantId != command.ApplicantId`. The controller calls `VerifyOwnershipAsync(appId)` first, which throws on the same condition. The service-side guard is unreachable from the live controller.

**Why this matters:**
Defensive depth is not wrong, but the controller and service have inconsistent failure modes for the same fault — the controller throws (becomes 500 via the framework's default handler), the service returns a `Forbidden` outcome (becomes 403). If a future caller bypasses `VerifyOwnershipAsync`, they get a 403; the controller's only caller gets a 500. Tests assert the 403 path through the service, not the 500 path through the controller.

**How to fix it:**
Either (a) remove `VerifyOwnershipAsync` from the controller and let the service's `Outcome.Forbidden` drive the 403 (cleaner), or (b) keep both but document that the service-layer check is for internal callers and the controller's throw is for HTTP callers. Option (a) is the simpler reconciliation.

---

## Remaining Findings

All findings are Optional/Minor. Gate passes. The findings are documented here for future reference and are not blocking for merge.
