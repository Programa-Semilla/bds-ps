# Deep Review Findings

**Date:** 2026-06-10
**Branch:** 030-edit-process-name
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** quality-gate (after_implement)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 2 | 2 | 0 |
| Minor | 4 | 1 | 3 |
| Info | 1 | 0 | 1 |
| **Total** | **7** | **3** | **4** |

**Agents completed:** 5/5 (CodeRabbit + Copilot skipped — CLIs not installed)
**Agents failed:** none

Correctness agent: 0 findings. Security agent: 0 findings.

## Findings

### FINDING-1
- **Severity:** Important
- **Confidence:** 80
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs:244 (pre-fix)
- **Category:** production-readiness
- **Source:** production-readiness-agent
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
`DbUpdateConcurrencyException` derives from `DbUpdateException`. The `Rename` action
caught only `DbUpdateException` and mapped it to the duplicate-name message. A losing
optimistic-concurrency writer (RowVersion conflict) would therefore be told
"Ya existe un proceso con ese nombre." — a wrong, misleading message — when no name
collision occurred.

**Why this matters:**
Two admins editing the same Process concurrently is a realistic admin scenario. The
transaction stays correct (atomic, no partial write), but the user-facing diagnosis is
wrong, which erodes trust and misdirects troubleshooting.

**How it was resolved:**
Added a `catch (DbUpdateConcurrencyException)` ordered **before** the
`catch (DbUpdateException)`, surfacing a distinct es-CR message
("El proceso fue modificado por otra persona; vuelva a intentarlo."). This also closes
the plan's deferred R-1 concurrency UX with a correct (non-conflated) message.

### FINDING-2
- **Severity:** Important
- **Confidence:** 92
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs:219-222 (over-length branch)
- **Category:** test-quality
- **Source:** test-quality-agent
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
The over-length rejection path (FR-004 / SC-003, spec "120/121 boundary" edge case) had
no service- or controller-layer test. The E2E can't reach it (the input has
`maxlength="120"`), and the only over-length test was a unit test against the *domain*
layer, which exercises a different code path/message.

**Why this matters:**
A spec-named boundary could regress (off-by-one, wrong message, missing trim) with every
existing test still green.

**How it was resolved:**
Added integration test `Rename_OverLength_Throws_PersistsNothing` (121-char name →
`ArgumentException`, stored name unchanged, no audit row) against the service+domain with
real persistence. Combined with the existing unit boundary test (120 ok / 121 reject),
the over-length path is now covered at domain and service layers; the controller's es-CR
string remains a thin presentation mapping (the empty-name E2E already proves the
controller surfaces es-CR inline errors).

### FINDING-3
- **Severity:** Minor
- **Confidence:** 78
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs:219-221
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
The over-length message hardcoded the literal `120`, independent of
`Process.MaxNameLength` used in the adjacent length check — a silent drift risk if the
constant ever changes.

**How it was resolved:**
Interpolated the constant: `$"El nombre debe tener {Process.MaxNameLength} caracteres o
menos."`. (The broader observation — that required/≤120 copy is duplicated across the
Create ViewModel annotations and this controller — is left as accepted tech-debt; the
Create flow already mixes es-CR annotations with the English domain message, so
centralizing is a separate, cross-cutting cleanup.)

### FINDING-4
- **Severity:** Minor
- **Confidence:** 78
- **File:** tests/FundingPlatform.Tests.E2E/Tests/Admin/RenameProcessTests.cs:59-84
- **Category:** test-quality
- **Source:** test-quality-agent
- **Round found:** 1
- **Resolution:** fixed (round 1)

**What is wrong:**
The Closed-Process rename E2E asserted the toast + new name but not the audit row that
FR-003 requires "regardless of status" (SC-004).

**How it was resolved:**
Added integration test `Rename_ClosedProcess_PersistsName_AndWritesAuditRow` (close →
rename → assert name persisted, status still Closed, exactly one `process.renamed` audit
row).

### FINDING-5
- **Severity:** Minor
- **Confidence:** 72
- **File:** src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs:238-243
- **Category:** architecture
- **Source:** architecture-agent
- **Round found:** 1
- **Resolution:** accepted (not fixed)

**What is wrong:**
The `catch (ArgumentException)` backstop is effectively unreachable because the
pre-validation rejects exactly the empty/over-length cases the domain throws for, and if
it ever did fire on an over-length value it would attach the "required" message.

**Why this is accepted:**
It is genuine defense-in-depth: without it, an unforeseen domain `ArgumentException`
would surface as a 500 instead of an inline message. Removing a backstop to satisfy a
"dead code" nit is not clearly an improvement. Left in place intentionally.

### FINDING-6
- **Severity:** Minor
- **Confidence:** 72
- **File:** spec Edge Case "Concurrent collision" (no test)
- **Category:** test-quality
- **Source:** test-quality-agent
- **Round found:** 1
- **Resolution:** documented (not fixed)

**What is wrong:**
No deterministic test exercises the concurrent-collision edge case.

**Why this is documented rather than fixed:**
The edge case depends on the `UX_Processes_Name` unique index + the `RowVersion` token,
neither of which the EF InMemory provider (the established integration pattern) enforces.
A deterministic concurrent-collision test is not reproducible on InMemory. The carve-out
is now documented in `ProcessRenameServiceTests`' class summary alongside the existing
duplicate-path note; the sequential duplicate path is covered by the E2E suite.

### FINDING-7
- **Severity:** Info
- **Confidence:** 85
- **File:** src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs:60-63
- **Category:** production-readiness
- **Source:** production-readiness-agent
- **Round found:** 1
- **Resolution:** accepted (not fixed)

**What is wrong:**
The `process.renamed` audit row stores `TargetId = "0"` (sentinel); the real `processId`
lives only in `PayloadJson`.

**Why this is accepted:**
This is the established codebase-wide convention for every `process.*` / `fund.*` event
(spec 021/029), not a regression introduced here. FR-003 is fully satisfied (actor +
old/new name + timestamp captured). Changing the sentinel scheme is a cross-cutting change
beyond this feature's scope.

## Remaining Findings

All remaining findings are Minor (FINDING-5 accepted, FINDING-6 documented) or Info
(FINDING-7 accepted). None block the gate. No human action required to proceed; reviewers
may weigh in on the accepted items during code review.
