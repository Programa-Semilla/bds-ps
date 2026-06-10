---
description: "Task list for 030-edit-process-name"
---

# Tasks: Admin — Edit Process Name

**Input**: Design documents from `specs/030-edit-process-name/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/rename-process.md, quickstart.md

**Tests**: INCLUDED — constitution Principle III (E2E) is non-negotiable and `contracts/rename-process.md`
specifies a unit/integration/E2E test contract. Test tasks are written before (or alongside) the
implementation they cover and must fail first.

**Organization**: One user story (US1 — rename a Process inline). All tasks are US1 except shared
domain plumbing in Foundational.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- All paths are repository-relative.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: None required — this is an additive change inside the existing solution
(`FundingPlatform.slnx`), no new project/package/tooling.

- [ ] T001 Confirm working tree is on branch `030-edit-process-name` and the solution builds clean: `dotnet build FundingPlatform.slnx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain audit primitive that the service layer depends on.

**⚠️ CRITICAL**: T002 must land before the service task (T008) compiles.

- [ ] T002 Add `ProcessRenamed` constant with string value `"process.renamed"` to `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`, following the existing `process.*` constants (`ProcessCreated`, `ActionProcessFundReassigned`); verify the `process.` prefix routes to the Process target in `AdminAuditEventWriter` target-derivation (no change needed there if prefix matches)

**Checkpoint**: Audit event available — user story work can begin.

---

## Phase 3: User Story 1 - Rename a Process inline (Priority: P1) 🎯 MVP

**Goal**: An admin can change a Process's `Name` in place on `/Admin/Processes/{id}`, for Active or
Closed Processes, with required/≤120/unique validation, an es-CR success toast, the duplicate-name
error reused from create, and a `process.renamed` audit entry.

**Independent Test**: Sign in as admin, open a Process's Details page, rename it to a new unique
name, save → new name on header + Index + a `process.renamed` audit row; duplicate/empty rejected
inline; Closed Process renames too.

### Application contract (blocks service + controller)

- [ ] T003 [US1] Add `RenameProcessCommand(int ProcessId, string NewName)` sealed record and the `Task RenameAsync(RenameProcessCommand command, string actorUserId, CancellationToken ct)` signature (with XML-doc citing spec 030, noting "no-op writes no audit; allowed at any status") to `src/FundingPlatform.Application/Processes/IProcessService.cs`, mirroring `ReassignProcessFundCommand` / `ReassignFundAsync`

### Tests for User Story 1 (write first — must fail before T008–T010 land) ⚠️

- [ ] T004 [P] [US1] Unit tests for `Process.Rename` in `tests/FundingPlatform.Tests.Unit` (extend the existing Process test class or add one): empty/whitespace → `ArgumentException`; 120 chars accepted, 121 rejected; surrounding whitespace trimmed; equal-name → no-op (Name unchanged). Skip any case already covered.
- [ ] T005 [P] [US1] Integration tests for `ProcessService.RenameAsync` in `tests/FundingPlatform.Tests.Integration` (real DB, no mocks): (a) happy path persists the new name and writes exactly one `process.renamed` audit row with `{processId, oldName, newName}`; (b) renaming to another Process's existing name throws `DbUpdateException` (via `UX_Processes_Name`), original name unchanged, no audit row; (c) re-submitting the current name is a no-op — name unchanged AND no audit row; (d) unknown id throws `KeyNotFoundException`
- [ ] T006 [P] [US1] E2E tests (Playwright) in `tests/FundingPlatform.Tests.E2E` using a Page Object + `data-testid` hooks `admin-process-rename-form` / `admin-process-rename-input` / `admin-process-rename-submit`: rename an Active Process (assert new name on Details header + `/Admin/Processes` Index row + success toast "Nombre del proceso actualizado."); rename a Closed Process (assert success); duplicate name → inline "Ya existe un proceso con ese nombre.", name unchanged; empty name → inline validation error, name unchanged

### Implementation for User Story 1

- [ ] T007 [US1] (Domain — verify only) Confirm `Process.Rename(string)` in `src/FundingPlatform.Domain/Entities/Process.cs` already trims/validates/no-ops and has **no** `ProcessClosedException` guard; make **no** change (FR-002 requires rename at any status). If a Closed guard is somehow present, do not add/keep it.
- [ ] T008 [US1] Implement `ProcessService.RenameAsync` in `src/FundingPlatform.Infrastructure/Services/ProcessService.cs`, mirroring `ReassignFundAsync`: null/whitespace guards; load Process by id (`KeyNotFoundException` if missing); capture `oldName`; call `process.Rename(command.NewName)`; **if the trimmed new name equals `oldName`, return without writing audit** (FR-006/SC-005); else `_audit.WriteAsync(AdminAuditEvent.ProcessRenamed, actorUserId, JsonSerializer.Serialize(new { processId = process.Id, oldName, newName = process.Name }), ct)`; `SaveChangesAsync(ct)` (let `DbUpdateException` bubble for the duplicate path) (depends on T002, T003)
- [ ] T009 [US1] Add `[HttpPost("{id:int}/Rename")] [ValidateAntiForgeryToken] Rename(int id, string newName, CancellationToken ct)` to `src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs`, mirroring `ChangeFund`: resolve `actorId` via `_userManager.GetUserId(User)`; call `_processes.RenameAsync(new RenameProcessCommand(id, newName), actorId, ct)`; map `ArgumentException` → `ModelState.AddModelError(nameof(newName), ex.Message)` + re-render `Details`; `DbUpdateException` → `ModelState` error "Ya existe un proceso con ese nombre." + re-render `Details`; `KeyNotFoundException` → `NotFound()`; success → `TempData["SuccessMessage"]="Nombre del proceso actualizado."` + `RedirectToAction(nameof(Details), new { id })`. (Re-render path must rebuild the `AdminProcessDetailsViewModel` the same way `Details` does, surfacing the inline error.) (depends on T003, T008)
- [ ] T010 [US1] Add the inline Name edit card to `src/FundingPlatform.Web/Views/Admin/Processes/Details.cshtml` near the top, mirroring the Fund card (`method="post" asp-action="Rename" asp-route-id="@detail.Id"`, `@Html.AntiForgeryToken()`, `<input name="newName" value="@detail.Name" maxlength="120" required data-testid="admin-process-rename-input">`, submit `data-testid="admin-process-rename-submit"`, form `data-testid="admin-process-rename-form"`). Render it **regardless of `detail.Status`** (Active AND Closed); surface the `newName` ModelState error inline; es-CR labels (e.g. "Nombre del proceso"). (depends on T009)

**Checkpoint**: US1 fully functional and independently testable.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [ ] T011 Run `dotnet test tests/FundingPlatform.Tests.Unit` and `dotnet test tests/FundingPlatform.Tests.Integration`; fix until green
- [ ] T012 Run the **full** `dotnet test tests/FundingPlatform.Tests.E2E` suite and confirm green (delivery bar per CLAUDE.md — partial runs do not count)
- [ ] T013 [P] Walk `specs/030-edit-process-name/quickstart.md` manual steps against a running AppHost (rename Active, rename Closed, duplicate, empty, no-op, audit-log entry)
- [ ] T014 [P] Update CLAUDE.md "Recent Changes" with a `030-edit-process-name` entry and flip the `<!-- SPECKIT -->` marker to "last shipped" (do at ship time, after E2E green)

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (T001)**: none.
- **Foundational (T002)**: after Setup; blocks the service implementation.
- **US1 (T003–T010)**: after Foundational.
- **Polish (T011–T014)**: after US1 implementation.

### Critical path
`T002 → T003 → T008 → T009 → T010 → T011 → T012`. Tests T004/T005/T006 are written after T003 (so the command type/`data-testid` names exist to compile/reference) and must fail before T008–T010 are completed.

### Within US1
- T003 (contract) before T008 (service) and T009 (controller).
- T007 is a no-op verification (domain already correct).
- T008 (service) before T009 (controller); T009 before T010 (view).
- Tests (T004/T005/T006) authored before their targets are completed; green confirmation in T011/T012.

### Parallel Opportunities
- **T004, T005, T006** — different test projects, no shared files → run/author in parallel (after T003).
- **T013, T014** — independent files → parallel.
- Implementation tasks T007→T008→T009→T010 are sequential (each depends on the prior layer).

---

## Parallel Example: User Story 1 tests

```bash
# After T003 (command + signature exist), author the three test layers in parallel:
Task: "Unit tests for Process.Rename in tests/FundingPlatform.Tests.Unit"          # T004
Task: "Integration tests for ProcessService.RenameAsync (real DB)"                 # T005
Task: "Playwright E2E: rename Active/Closed, duplicate, empty"                      # T006
```

---

## Implementation Strategy

### MVP (only story)
1. T001 Setup → T002 Foundational.
2. T003 contract → author tests T004–T006 (fail) → implement T007–T010 (tests go green).
3. T011 unit+integration green → **T012 full E2E green** (delivery bar).
4. T013 quickstart walk; T014 docs at ship time.

This is a single-increment feature: completing US1 delivers the entire spec.

---

## Notes
- [P] = different files, no dependencies.
- No schema change; no new managed dependencies.
- One intentional, documented deviation: rename allowed when Closed (FR-002) — do not "fix" it to match the other status-guarded mutations.
- Commit after each task or logical group (constitution Commit Discipline).
