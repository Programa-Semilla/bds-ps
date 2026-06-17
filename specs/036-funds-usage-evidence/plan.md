# Implementation Plan: Funds-Usage Evidence Stage

**Branch**: `036-funds-usage-evidence` | **Date**: 2026-06-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/036-funds-usage-evidence/spec.md`

## Summary

Add a post-disbursement reviewer stage where in-scope reviewers/admins upload, annotate (≤250-char note), download, and delete evidence files on an application that has reached `AgreementExecuted`. Built as a thin slice over existing seams: a new `FundsUsageEvidence` aggregate (own table, FK to `Applications`), a new `FileCategory.FundsUsageEvidence` storage container (20 MiB cap, BackendStream), a reviewer-scoped `FundsUsageEvidenceController` mounted under the application route and surfaced as a per-application stage card, reusing `IReviewerScopeProvider` + `IApplicationRepository.ApplicantSharesAnyGroupAsync` for group-scoped auth, `UploadSizeGuard` for the size boundary, `IObjectStorage` for blob I/O, and `IAdminAuditWriter` for the three audited mutations. No new managed dependencies, no new `ApplicationState`.

## Technical Context

**Language/Version**: C# / .NET 10.0 (ASP.NET MVC, EF Core 10)
**Primary Dependencies**: ASP.NET Identity, .NET Aspire, `IObjectStorage` (spec 014), `IReviewerScope*` (spec 016), `IAdminAuditWriter` (spec 016), Tabler.io vendored UI — **no new NuGet packages**
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`) — one new table `dbo.FundsUsageEvidence`; blob files via `IObjectStorage` container `funds-usage-evidence`
**Testing**: NUnit unit + integration (real DB), Playwright E2E via `AspireFixture` (Page Object Model)
**Target Platform**: Linux container (Aspire-orchestrated)
**Project Type**: Web application (server-rendered MVC), Clean Architecture (Domain / Application / Infrastructure / Web)
**Performance Goals**: Interactive admin/reviewer surface; per-file cap 20 MiB; evidence list is a flat projection (no aggregate hydration on read)
**Constraints**: es-CR copy throughout; schema-first (dacpac, no EF migrations); group-scoped reviewer auth with no-disclosure refusals; serve via BackendStream
**Scale/Scope**: One new stage on a single per-application route; ~1 entity, 1 controller (5 actions), 1 service, 1 repository, 1 table, 1 stage view + 1 partial, 4 prioritized user stories

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| **I. Clean Architecture** | ✅ PASS | Entity in Domain; `IFundsUsageEvidenceService` + DTOs + file-type policy in Application; EF config + repository + service impl + audit in Infrastructure; controller/views/VMs in Web. Dependencies point inward. |
| **II. Rich Domain Model** | ✅ PASS | The `AgreementExecuted` gate and the ≤250 note invariant live on the domain entity (`FundsUsageEvidence.CreateForExecutedApplication`, `EditNote`), not in the service/controller. |
| **III. E2E Testing (NON-NEGOTIABLE)** | ✅ PASS | Playwright E2E per user story (upload+list+download, note add/edit, delete+confirm, scoped-access refusals). Seed must reach an `AgreementExecuted` application (see research D8). |
| **IV. Schema-First DB** | ✅ PASS | New table authored as `dbo.FundsUsageEvidence.sql` in the Database project; EF Core data-access only; no migrations/`EnsureCreated`. |
| **V. Specification-Driven Development** | ✅ PASS | spec.md → plan.md → tasks.md → implement, against the approved spec. |
| **VI. Simplicity / Progressive Complexity** | ✅ PASS | Reuses every existing seam; no new state, no new deps, applicant visibility + evidence-review workflow explicitly deferred (YAGNI). |

**Result: PASS (pre-design). No complexity-tracking entries required.**

Project-specific quality gates (constitution §Development Workflow):
- **Validation errors collected and displayed together** — single-file upload + single note field; ModelState aggregation applies where multiple fields exist (note form). ✅
- **Optimistic concurrency for concurrent-edit entities** — add `RowVersion` to `FundsUsageEvidence` (mirrors `SignedUpload`); concurrent delete resolves to not-found (FR edge). ✅
- **Authorization verifies resource ownership** — group-overlap (`ApplicantSharesAnyGroupAsync`) + reviewer/admin role gate at every action incl. download. ✅

## Project Structure

### Documentation (this feature)

```text
specs/036-funds-usage-evidence/
├── plan.md              # This file
├── spec.md              # Approved spec
├── research.md          # Phase 0 — decisions D1..D10
├── data-model.md        # Phase 1 — entity, table DDL, EF config, audit keys
├── implementation-notes.md  # Brainstorm-captured technical context
├── review_brief.md      # Reviewer guide
├── REVIEW-SPEC.md       # Spec soundness review (SOUND)
├── contracts/
│   ├── ui-and-routes.md     # Routes, actions, status codes, view contracts
│   └── interfaces.md        # Domain + Application interface signatures
├── quickstart.md        # Phase 1 — how to validate the feature
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   └── Entities/
│       ├── FundsUsageEvidence.cs           # NEW — aggregate (ApplicationId FK, blob meta, note, RowVersion)
│       ├── AdminAuditEvent.cs              # EDIT — add funds_evidence.* action keys + target type
│       └── Application.cs                  # (read-only) State == AgreementExecuted consulted by the factory
├── FundingPlatform.Application/
│   ├── Abstractions/Storage/
│   │   ├── FileCategory.cs                 # EDIT — add FundsUsageEvidence member + container mapping
│   │   └── StorageOptions.cs               # EDIT — add FundsUsageEvidence category options + For() case
│   └── FundsUsageEvidence/                 # NEW folder
│       ├── IFundsUsageEvidenceService.cs   # NEW — list/upload/edit-note/delete/download orchestration
│       ├── FundsUsageEvidenceDtos.cs       # NEW — command + view DTOs
│       └── EvidenceFileTypePolicy.cs       # NEW — pure allow-list (ext + content-type + magic-byte family)
├── FundingPlatform.Infrastructure/
│   ├── Persistence/Configurations/
│   │   └── FundsUsageEvidenceConfiguration.cs   # NEW — EF mapping
│   ├── Persistence/Repositories/
│   │   └── FundsUsageEvidenceRepository.cs       # NEW — flat group-scoped queries
│   └── FundsUsageEvidence/
│       └── FundsUsageEvidenceService.cs          # NEW — IObjectStorage + audit + transaction
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   └── FundsUsageEvidenceController.cs        # NEW — [Authorize(Roles="Reviewer,Admin")], per-app route
│   ├── ViewModels/
│   │   └── FundsUsageEvidenceViewModels.cs        # NEW
│   ├── Resources/
│   │   └── FundsUsageEvidenceResources.*          # NEW — es-CR copy
│   └── Views/FundsUsageEvidence/
│       ├── Index.cshtml                           # NEW — stage view (list + upload form + empty state)
│       └── _EvidenceRow.cshtml                    # NEW — per-item row (name/note/uploader/when/download/delete)
└── FundingPlatform.Database/
    └── Tables/dbo.FundsUsageEvidence.sql          # NEW — table + FK + indexes + CK

tests/
├── FundingPlatform.Tests.Unit/                    # FundsUsageEvidence domain + EvidenceFileTypePolicy
├── FundingPlatform.Tests.Integration/             # service against real DB (upload/list/edit/delete, scope, gate)
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/FundsUsageEvidencePage.cs      # NEW
    └── FundsUsageEvidenceTests.cs                 # NEW — 4 user stories
```

**Structure Decision**: Standard 4-layer Clean Architecture as mandated by Constitution Principle I and the existing `src/` layout. The feature is mounted as a per-application route (`/Applications/{applicationId:int}/Evidence`) mirroring `FundingAgreementController`'s `Applications/{applicationId:int}/FundingAgreement` pattern, and surfaced as a stage card on the application's reviewer surface (research D7).

## Complexity Tracking

> No Constitution Check violations. Table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
