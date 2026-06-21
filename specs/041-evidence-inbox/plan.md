# Implementation Plan: Funds-Usage Evidence Inbox

**Branch**: `041-evidence-inbox` | **Date**: 2026-06-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/041-evidence-inbox/spec.md`

## Summary

Give reviewers/admins a persistent way back to executed applications for funds-usage evidence, and freeze evidence when the governing Process closes. Two thin slices over existing seams:

1. **Evidence inbox** — a new role-gated sidebar entry → a list of `AgreementExecuted` applications whose `Application → Group → Process` is `Active`, group-scoped via the existing `IReviewerScopeProvider`. Mirrors the signing-inbox pattern (`SignedUploadRepository.GetPendingInboxAsync` → DTO rows → controller VM → view).
2. **Process-close read-only gate** — `FundsUsageEvidenceController` learns whether the application's Process is `Closed`; when closed, `Index` renders read-only (download only, read-only notice) and the three mutation actions (Upload/EditNote/Delete) reject without mutating.

No new `ApplicationState`, no schema change, no new managed dependency. `ProcessStatus` (`Active`/`Closed`) already exists and is already EF-mapped (used by spec 029's archived-fund path), so no TINYINT-conversion gotcha is introduced.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire (existing stack; none added)
**Storage**: SQL Server via dacpac (no schema change — read-only feature over existing tables `Applications`, `Groups`, `Processes`, `FundsUsageEvidence`)
**Testing**: Playwright E2E (NUnit + Page Object Model), xUnit/NUnit unit + integration
**Target Platform**: Linux server (Aspire-orchestrated)
**Project Type**: Web (ASP.NET MVC server-rendered)
**Performance Goals**: Inbox list capped at 200 rows (mirrors reviewer queue); single indexed query
**Constraints**: es-CR copy; group-overlap enforced at the query level (NFR-001); no new state/schema/deps (NFR-002)
**Scale/Scope**: One new controller + one projection + one query + sidebar entry + read-only gate on an existing controller/view. ~1 new E2E class.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|-----------|
| **I. Clean Architecture** | ✅ Inbox query interface in Application (`IEvidenceInboxProjection`), EF impl in Infrastructure (mirrors `ReviewerDashboardProjection`). Controllers in Web. Read-only gate is a Web-controller authorization concern reading `Process.Status`. Dependencies point inward. |
| **II. Rich Domain Model** | ✅ No new state transitions. Read-only is a visibility/authorization gate over the existing `Process.Status`, not new domain behavior. A small read-only query helper `Application.Group.Process.Status` (the `IsFrozen` pattern already walks `Group?.Process?.Fund?.Status`) is the only domain touch, and it is a query, not a mutation. |
| **III. E2E (NON-NEGOTIABLE)** | ✅ New `EvidenceInboxTests` covering US1 (listed + reachable + uploadable), US2 (closed → de-listed + read-only + mutation rejected + download works), US3 (out-of-group/applicant refusal). Reuses `FundingAgreementSeeder.SeedExecutedAgreementAsync` + admin `POST /Admin/Processes/{id}/Close`. |
| **IV. Schema-First** | ✅ No schema change. No EF migration. Reads existing tables only. |
| **V. SDD** | ✅ Prioritized, independently testable user stories drive phased tasks. |
| **VI. Simplicity/YAGNI** | ✅ No search/pagination (capped list). No new state/schema/deps. Reuses scope seam, signing-inbox pattern, existing evidence controller/view. |

**Result: PASS** (no violations; Complexity Tracking not required).

## Project Structure

### Documentation (this feature)

```text
specs/041-evidence-inbox/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1..D8
├── data-model.md        # Phase 1 — query shape, row DTO, read-only derivation (no new tables)
├── quickstart.md        # Phase 1 — E2E setup + manual verification
├── contracts/
│   └── interfaces.md     # Phase 1 — projection interface, routes, view contracts
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Application/
    Reviewer/                         # existing IReviewerScope / IReviewerScopeProvider (reused)
    EvidenceInbox/                    # NEW
      IEvidenceInboxProjection.cs     #   interface + EvidenceInboxRowDto
  FundingPlatform.Infrastructure/
    Persistence/
      EvidenceInboxProjection.cs      # NEW — EF query: AgreementExecuted ∧ Process.Active ∧ group-overlap
                                      #   (mirrors ReviewerDashboardProjection location/style)
  FundingPlatform.Web/
    Controllers/
      EvidenceInboxController.cs       # NEW — [Authorize(Reviewer,Admin)], GET /Evidence → inbox
      FundsUsageEvidenceController.cs  # EDIT — add process-closed read-only gate (Index flag + reject mutations)
    ViewModels/
      EvidenceInboxViewModels.cs       # NEW — inbox VM + row VM
      FundsUsageEvidenceIndexViewModel.cs  # EDIT — add IsReadOnly
    Views/
      EvidenceInbox/
        Index.cshtml                   # NEW — inbox list + empty state
      FundsUsageEvidence/
        Index.cshtml                   # EDIT — hide upload form + read-only notice when IsReadOnly
        _EvidenceRow.cshtml            # EDIT — hide edit-note save + delete when IsReadOnly; keep download
    Resources/
      EvidenceInboxResources.*         # NEW — es-CR sidebar/page/empty copy
      FundsUsageEvidenceResources.*    # EDIT — add read-only notice + blocked-action copy
    Views/Shared/_Layout.cshtml        # EDIT — add operativoEntries sidebar entry (Reviewer,Admin)
tests/
  FundingPlatform.Tests.E2E/
    Tests/EvidenceInboxTests.cs        # NEW — US1/US2/US3
    PageObjects/…                      # NEW/edited page object(s) for the inbox + read-only assertions
  FundingPlatform.Tests.Unit/
    Application/EvidenceInboxProjectionTests.cs  # OPTIONAL — scope/filter unit coverage (InMemory)
  FundingPlatform.Tests.Integration/
    EvidenceInboxQueryTests.cs         # query against real DB: state×process-status×group matrix
```

**Structure Decision**: ASP.NET MVC web app, Clean Architecture four-layer. The inbox follows the spec-021 signing-inbox shape; the read-only gate is an in-place edit to the spec-036 controller/view. Inbox route is `/Evidence` (list, no application id) to stay distinct from the per-application `/Applications/{id}/Evidence`.

## Phase 0: Research

See [research.md](./research.md). All NEEDS CLARIFICATION resolved; key decisions:
- **D1** Inbox as a dedicated `EvidenceInboxController` at `/Evidence` (not a tab on `/Review`, not an action on `ReviewerDashboardController`) — per user's "separate sidebar entry" decision.
- **D2** Query lives in Infrastructure as `EvidenceInboxProjection` returning row DTOs (mirrors `ReviewerDashboardProjection` + `GetPendingInboxAsync`); group-overlap + `ExcludeDeleted` + `ExcludeArchivedFund` applied in-query (NFR-001).
- **D3** "Process active" = `Application.Group.Process.Status == Active`; evaluated live per request (FR-004 reopen).
- **D4** Read-only gate on `FundsUsageEvidenceController`: `Index` passes `IsReadOnly`; Upload/EditNote/Delete reject-with-redirect (es-CR toast) when closed — covers crafted POSTs (FR-007).
- **D5** Read-only freeze applies to admins too (spec assumption); admin's only bypass is group visibility.
- **D6** `AgreementExecuted` does **not** block process close (verified in `ProcessService.ListBlockingActiveApplicationPublicCodesAsync`), so the read-only scenario is reachable; E2E closes via existing `POST /Admin/Processes/{id}/Close`.
- **D7** Download remains available in read-only mode (FR-006).
- **D8** Row identity = application number (`APP-{id:D5}`), applicant name, fund + process names; ordering most-recently-executed first.

## Phase 1: Design & Contracts

- [data-model.md](./data-model.md) — no new persisted entities; documents `EvidenceInboxRowDto`, the query predicate, and the read-only derivation.
- [contracts/interfaces.md](./contracts/interfaces.md) — `IEvidenceInboxProjection`, controller routes, and view/markup contracts (testids).
- [quickstart.md](./quickstart.md) — E2E seed + close-process steps and manual verification.
- Agent context: CLAUDE.md SPECKIT markers updated to point at this plan.

## Complexity Tracking

No constitution violations. Table intentionally empty.
