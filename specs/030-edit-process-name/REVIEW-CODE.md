# Code Review: Admin — Edit Process Name

**Spec compliance:** 100% (8/8 FR, 5/5 SC). Full E2E 302/0/5; Unit 522/0; Integration 343/0 (post deep-review fixes).

---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the implementation changes,
> focusing on high-level questions that need human judgment.

**Changed files:** 6 source files (1 domain constant, 1 application contract, 1
service method, 1 controller action + helper extraction, 1 view card) + 3 test
files (unit extension, new integration class, new E2E class + page-object hooks),
plus `tasks.md`/`CLAUDE.md` docs.

### Understanding the changes (8 min)

- Start with [`AdminProcessesController.Rename`](../../src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs):
  this is the entry point and where most of the judgment calls live (validation,
  exception mapping, error re-render). Read it alongside the extracted
  `BuildDetailsViewModelAsync` helper just above it.
- Then [`ProcessService.RenameAsync`](../../src/FundingPlatform.Infrastructure/Services/ProcessService.cs):
  the thin service mirroring `ReassignFundAsync` — load, capture `oldName`, call
  the domain, no-op short-circuit, audit, save.
- Question: the whole feature deliberately reuses the spec-029 Fund-reassignment
  seam verbatim. Does that reuse hold up, or does rename have enough of its own
  shape (inline ModelState error vs. ChangeFund's TempData flash) that mirroring
  obscures more than it helps?

### Key decisions that need your eyes (12 min)

**es-CR validation in the controller, not via domain message** (`AdminProcessesController.cs`, Rename action; relates to [FR-004](spec.md)/[FR-008](spec.md))

The domain `Process.Rename` throws English `ArgumentException` ("Process name is
required."). The contract said to map `ex.Message` into ModelState, but that
would surface English copy and violate FR-008. I pre-validate required/≤120 in
the controller with the **same es-CR strings the Create flow uses**, keeping the
domain as a defensive backstop (mapped to a generic es-CR message).
- Question: is duplicating the Create messages here acceptable, or should the
  required/≤120 copy be centralized (a shared resource/constants) so Create and
  Rename can't drift?

**`string? newName` action parameter** (`AdminProcessesController.cs`, Rename signature)

A non-nullable `string` parameter gets an implicit `[Required]`, and
`RequiredAttribute` treats whitespace-only as missing — so submitting "   "
produced the framework's English "The newName field is required." *before* my
es-CR check ran. Making the param nullable removes the implicit-required so my
es-CR message is the sole source.
- Question: this is a slightly non-obvious reason for a nullable param. Is the
  inline comment enough, or does this deserve a more defensive approach (e.g.
  clearing the ModelState key explicitly)?

**Error re-render rebuilds the full Details VM** (`BuildDetailsViewModelAsync`, relates to [FR-007](spec.md))

On a validation/duplicate error the action returns `View("Details", vm)` rather
than redirecting, so the inline error shows. The VM is rebuilt from the DB, which
means the user's rejected input is replaced by the persisted name in the input
box (proving "name unchanged").
- Question: re-showing the persisted name (not the rejected attempt) is the
  intended "name unchanged" UX here, but is losing the user's typed value on a
  duplicate-name error the right call, or should the attempted value be preserved?

### Areas where I'm less certain (5 min)

- [`ProcessService.RenameAsync`](../../src/FundingPlatform.Infrastructure/Services/ProcessService.cs)
  no-op detection compares `process.Name` to `oldName` *after* calling
  `Rename()`. This relies on the domain's ordinal short-circuit leaving `Name`
  untouched on an equal value. Correct today, but it couples the service's no-op
  decision to the domain's internal short-circuit behavior. Is that coupling fine,
  or should the service compare the trimmed input itself?
- The no-op path returns success TempData ("Nombre del proceso actualizado.")
  even though nothing changed — because `RenameAsync` is `Task` (void) per the
  contract and the controller can't distinguish. [FR-006](spec.md) only requires
  "no error"; a success toast is arguably misleading. Acceptable, or should the
  signature return a `bool changed` to suppress the toast on a true no-op?

### Deviations and risks (5 min)

- **Integration duplicate-name coverage is at E2E, not integration**
  ([`ProcessRenameServiceTests`](../../tests/FundingPlatform.Tests.Integration/Application/ProcessRenameServiceTests.cs)
  documents this): the established integration pattern uses EF InMemory, which
  does not enforce `UX_Processes_Name`, so the `DbUpdateException` path
  ([tasks.md](tasks.md) T005b) is exercised against the real dacpac DB in
  [`RenameProcessTests`](../../tests/FundingPlatform.Tests.E2E/Tests/Admin/RenameProcessTests.cs)
  instead. Question: is E2E-only coverage of the unique-index path acceptable, or
  is a real-SQL integration fixture worth standing up for the whole suite?
- **No optimistic-concurrency UX** ([plan.md](plan.md) marks the
  `DbUpdateConcurrencyException` → es-CR toast as optional/low-priority, R-1). The
  `RowVersion` token still guards correctness (last-writer is rejected), but there
  is no friendly "modified by someone else" message. Question: acceptable to defer?
- No deviations from [plan.md](plan.md)'s layer structure; all four layers landed
  exactly as planned, no schema change, no new deps.

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-06-10 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 0 | completed |
| Architecture & Idioms | 2 | completed |
| Security | 0 | completed |
| Production Readiness | 2 | completed |
| Test Quality | 3 | completed |
| CodeRabbit (external) | – | skipped (CLI not installed) |
| Copilot (external) | – | skipped (CLI not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 2 | 2 | 0 |
| Minor | 4 | 1 | 3 |
| Info | 1 | 0 | 1 |

### What was fixed automatically

Two Important findings: (1) a `DbUpdateConcurrencyException` (subclass of
`DbUpdateException`) was being mislabeled as a duplicate-name error — added a dedicated
catch ahead of it with a distinct es-CR message; (2) the over-length rejection path had
no service/controller-layer test (the E2E is blocked by `maxlength="120"`) — added an
integration test asserting a 121-char rename throws, persists nothing, and writes no
audit. Plus one Minor: the over-length message now interpolates `Process.MaxNameLength`
instead of a hardcoded `120`, and a Closed-Process rename audit-row integration test was
added. All suites green after fixes (Unit 522/0, Integration 343/0; rename E2E 4/4).

### What still needs human attention

All Critical and Important findings were resolved. Three Minor/Info findings remain (see
[review-findings.md](review-findings.md)) — framed as questions for the reviewer:

- The required/≤120 es-CR copy is duplicated between the Create ViewModel annotations and
  the `Rename` controller (the Create flow itself already mixes es-CR annotations with the
  English domain message). Worth centralizing into shared constants/resources, or accept?
- The `catch (ArgumentException)` backstop in `Rename` is unreachable given the
  pre-validation. Keep as defense-in-depth (current choice) or remove as dead code?
- The concurrent-collision edge case has no deterministic test (InMemory enforces neither
  the unique index nor RowVersion). Is the documented carve-out + E2E sequential-duplicate
  coverage sufficient, or is a real-SQL integration fixture warranted?

### Recommendation

All findings addressed. N Minor findings remain but are non-blocking. Code is ready for
human review with no known blockers.
