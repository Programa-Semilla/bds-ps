# Implementation Plan: Admin — Edit Process Name

**Branch**: `030-edit-process-name` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/030-edit-process-name/spec.md`

## Summary

Add an inline **Name** edit affordance to the Process detail page (`/Admin/Processes/{id}`),
the one Process detail that is currently immutable in the UI. Primary requirement: an admin can
rename a Process in place (Active or Closed), with required/≤120-char/unique validation, an
es-CR success toast, the same duplicate-name error used at creation, and a new `process.renamed`
admin audit entry. The technical approach reuses the existing spec-029 Fund-reassignment seam
end-to-end: a thin `RenameProcessCommand` → `IProcessService.RenameAsync` → domain
`Process.Rename()` (already present), a `Rename` POST action mirroring `ChangeFund`, and an
inline form card on `Details.cshtml` mirroring the Fund/stage-window forms. No schema change, no
new dependencies.

## Technical Context

**Language/Version**: C# / .NET 10.0 (repo standard; constitution mandates latest LTS, project is on 10)
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire (no new managed deps)
**Storage**: SQL Server (`dbo.Processes`); reuses existing `Name` column + `UX_Processes_Name` unique index — **no schema change**
**Testing**: xUnit/NUnit unit + integration (real DB per CLAUDE.md, never mocks) + Playwright E2E (full suite green = delivered)
**Target Platform**: Linux server (Aspire-orchestrated web app)
**Project Type**: Web application (ASP.NET MVC, server-rendered) — existing 4-layer Clean Architecture
**Performance Goals**: N/A — single-row admin UPDATE; no perf-sensitive path introduced
**Constraints**: es-CR copy; optimistic concurrency via existing `Process.RowVersion` rowversion token
**Scale/Scope**: One editable field on one admin page; ~1 domain (none — method exists), 1 command, 1 service method, 1 controller action, 1 audit constant, 1 view card

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | ✅ PASS | Domain `Process.Rename()` (exists) · Application `RenameProcessCommand` + `IProcessService.RenameAsync` · Infrastructure `ProcessService.RenameAsync` · Web controller action + view. Dependencies point inward; no layer inversion. |
| II. Rich Domain Model | ✅ PASS | The rename invariant (required, trim, ≤120, equal-name no-op) lives on the entity (`Process.Rename`/`ValidateName`), not the controller/service. |
| III. E2E (NON-NEGOTIABLE) | ✅ PASS | Single P1 story; E2E covers rename Active + rename Closed + duplicate rejected + empty rejected, via stable `data-testid` hooks. Full suite must be green to ship. |
| IV. Schema-First | ✅ PASS | No schema change. `Name` column, `HasMaxLength(120)`, and `UX_Processes_Name` already exist in the dacpac. EF config unchanged. |
| V. SDD | ✅ PASS | spec.md → this plan → tasks.md → implement. |
| VI. Simplicity / YAGNI | ✅ PASS | Reuses an existing pattern verbatim; rejected a dedicated Edit page and new fields. One intentional, documented inconsistency (rename allowed when Closed). |

**Quality Gates (constitution Development Workflow):**
- "Optimistic concurrency for entities with concurrent edit risk" → `Process.RowVersion.IsRowVersion()` already maps a SQL rowversion concurrency token; the load-then-save rename inherits EF's automatic concurrency guard (see research.md R-1).
- "All validation errors collected and displayed at once" → trivially satisfied (single field; one ModelState key).
- "Authorization checks verify resource ownership" → admin-area authorization reused (`[Authorize(Roles="Admin,SupplierAdmin")]` + `[SupplierAdminDenied]` on `AdminProcessesController`); no per-resource ownership concept for Processes.

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/030-edit-process-name/
├── spec.md                  # Requirements (done)
├── implementation-notes.md  # Reuse-seam map (done, brainstorm output)
├── plan.md                  # This file
├── research.md              # Phase 0 output
├── data-model.md            # Phase 1 output
├── quickstart.md            # Phase 1 output
├── contracts/
│   └── rename-process.md    # Phase 1 — controller route + command contract
├── checklists/requirements.md
├── REVIEW-SPEC.md
├── review_brief.md
└── tasks.md                 # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   └── Entities/Process.cs                         # Rename() ALREADY EXISTS — no change expected
│   └── Entities/AdminAuditEvent.cs                 # + ProcessRenamed constant ("process.renamed")
├── FundingPlatform.Application/
│   └── Processes/IProcessService.cs                # + RenameAsync signature + RenameProcessCommand record
├── FundingPlatform.Infrastructure/
│   └── Services/ProcessService.cs                  # + RenameAsync impl (mirrors ReassignFundAsync)
└── FundingPlatform.Web/
    ├── Controllers/Admin/AdminProcessesController.cs   # + [HttpPost("{id:int}/Rename")] Rename action
    └── Views/Admin/Processes/Details.cshtml            # + inline Name edit card (mirrors Fund card)

tests/
├── FundingPlatform.Tests.Unit/                     # Process.Rename boundary cases (extend if gaps)
├── FundingPlatform.Tests.Integration/              # RenameAsync happy path + duplicate + no-op (real DB)
└── FundingPlatform.Tests.E2E/                      # rename Active/Closed, duplicate, empty (Playwright)
```

**Structure Decision**: Existing 4-layer Clean Architecture (Domain/Application/Infrastructure/Web)
under `src/`, with the three-tier test suite under `tests/`. This feature touches one file per
layer plus one audit constant — no new projects, folders, or structural changes.

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.
