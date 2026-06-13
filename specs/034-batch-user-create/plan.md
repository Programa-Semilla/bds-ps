# Implementation Plan: Batch user creation (bulk applicant provisioning via CSV)

**Branch**: `feature/batch-user-create` | **Date**: 2026-06-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/034-batch-user-create/spec.md`

## Summary

Add an admin-only CSV upload that provisions up to 200 **Solicitante** accounts in one synchronous request. Each data row is validated and normalized independently; valid rows reuse the existing single-create path (`UserAdministrationService.CreateUserAsync`, which since spec 033 creates the account with no password) and then receive the existing spec-033 set-password invitation (`AdminUsersController.IssueAndSendInvitationAsync`). Invalid rows are skipped and reported. The admin sees a succeeded/errored report. `Grupo` resolves (by name) to a Group membership; `Proceso`/`Fondo` are validation-only guards confirming the spec-029 Fund→Process→Group chain. **No schema change, no new managed dependencies.**

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: ASP.NET Identity, existing `UserAdministrationService`, `IIssuePasswordResetTokenHandler` + `InvitationEmailFactory` (spec 033), `Identification` value object (spec 026). **No new NuGet packages** (FR-014).
**Storage**: SQL Server via existing `AppDbContext`. The uploaded CSV is parsed in-memory and discarded — it is **not** persisted to object storage. No new tables, no dacpac change (Proceso/Fondo are not persisted; membership reuses `dbo.UserGroupMemberships`).
**Testing**: NUnit + EF InMemory (Integration, real-DB-shaped per CLAUDE.md), Playwright (E2E) against the Aspire stack.
**Target Platform**: Linux container (Aspire-orchestrated), admin web UI.
**Project Type**: Web (ASP.NET MVC, Clean Architecture: Domain / Application / Infrastructure / Web).
**Performance Goals**: A 200-row batch completes within a single HTTP request without a background worker (synchronous, FR-001). No streaming/progress UI.
**Constraints**: es-CR copy throughout (FR-013); no CDN/new vendored assets; ≤200 data rows (FR-003); in-house CSV parsing (FR-014).
**Scale/Scope**: ≤200 rows per upload; one new controller surface under `/Admin/Users`; ~1 Application utility (CSV parser) + 1 Application phone normalizer + 1 orchestration method + 2 views + es-CR resources.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | PASS | CSV parser + phone normalizer + row/result DTOs live in Application (pure, no infra deps). Name-resolution + chain validation + creation orchestration live in Infrastructure (`UserAdministrationService`). Controller (Web) does upload intake, invitation dispatch (HTTP-context-bound link building), and report rendering. Dependencies point inward. |
| II. Rich Domain Model | PASS | No anemic logic added to services that belongs on entities. Reuses `Identification` value object validation, `Group.Create`/`UserGroupMembership` invariants, and the existing `CreateUserAsync` guards. No new entity behavior required. |
| III. E2E Testing (NON-NEGOTIABLE) | PASS | Each user story gets Playwright E2E coverage (US1 all-valid batch + invitations captured; US2 mixed file → succeeded/errored report; US3 chain mismatch skipped). Page Object `AdminBatchUsersPage`. |
| IV. Schema-First DB | PASS | **No schema change.** No EF migration (already prohibited). Membership rows use the existing `dbo.UserGroupMemberships`. dacpac untouched. |
| V. Specification-Driven Development | PASS | spec.md → plan.md → tasks.md → implement. Stories independently testable/deliverable. |
| VI. Simplicity & Progressive Complexity | PASS | Synchronous (no worker/queue), no new deps, no schema, reuses single-create + invitation seams. Deferred items (async, .xlsx, downloadable report, batch edit) explicitly out of scope. |
| Quality gate: collect all validation errors | PASS | Row errors are accumulated into one report (FR-012); file-level rejection is a single message (FR-003). |
| Tech standards (no new frameworks) | PASS | No new technology; in-house CSV parsing. |

**Result: PASS (no violations). Complexity Tracking left empty.**

## Project Structure

### Documentation (this feature)

```text
specs/034-batch-user-create/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── contracts.md     # CSV template contract + service/controller contracts
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
├── REVIEW-SPEC.md       # Spec review (SOUND)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Application/
    Admin/Users/
      Batch/                                  # NEW — batch DTOs + parsing/normalization (pure)
        CsvParser.cs                          # NEW — minimal RFC-4180 reader (no new dep)
        PhoneNormalizer.cs                    # NEW — strip 506 prefix, take first number
        BatchUserCsvColumns.cs                # NEW — canonical header names + order (es-CR)
        BatchUserImportRow.cs                 # NEW — raw cell strings + 1-based row number
        BatchUserCreateOutcome.cs             # NEW — per-row Succeeded | Errored(reason)
        BatchUserCreateResult.cs              # NEW — succeeded[] + errored[] partition
      IUserAdministrationService.cs           # EDIT — add CreateUsersBatchAsync(...)
  FundingPlatform.Infrastructure/
    Identity/
      UserAdministrationService.cs            # EDIT — implement CreateUsersBatchAsync: per-row
                                              #        normalize → resolve chain → CreateUserAsync
  FundingPlatform.Web/
    Controllers/Admin/
      AdminUsersController.cs                  # EDIT — add Batch (GET form), Batch (POST process),
                                              #        BatchTemplate (GET download); reuse
                                              #        IssueAndSendInvitationAsync per created row
    ViewModels/Admin/
      AdminUserBatchUploadViewModel.cs         # NEW
      AdminUserBatchResultViewModel.cs         # NEW — succeeded rows + errored rows
    Views/Admin/Users/
      Batch.cshtml                             # NEW — upload form + template download + 200/CSV hints
      BatchResult.cshtml                       # NEW — succeeded/errored report (es-CR)
    Resources/
      AdminUsersResources.cs                   # EDIT — Spec 034 es-CR strings
tests/
  FundingPlatform.Tests.Unit/
    Batch/CsvParserTests.cs                    # NEW
    Batch/PhoneNormalizerTests.cs              # NEW
  FundingPlatform.Tests.Integration/
    Application/BatchUserCreationTests.cs      # NEW — real-DB-shaped (EF InMemory) service tests
  FundingPlatform.Tests.E2E/
    PageObjects/Admin/AdminBatchUsersPage.cs   # NEW
    Tests/Admin/BatchUserCreateTests.cs        # NEW — US1/US2/US3
```

**Structure Decision**: Standard four-layer Clean Architecture already in use. Pure, deterministic pieces (CSV parsing, phone normalization, row/result models) go in **Application** so they are unit-testable without a DB. DB-touching orchestration (name resolution, chain validation, creation) extends the existing **Infrastructure** `UserAdministrationService`. The **Web** controller owns HTTP intake (IFormFile), the HTTP-context-bound invitation-link dispatch (reusing the existing private helper), and report rendering. This mirrors how spec 032/033 layered their work.

## Phase 0 — Research

See [research.md](./research.md). Resolves: CSV-parser scope (RFC-4180 subset, BOM, quoted fields), the per-row creation+invitation seam (reuse `CreateUserAsync` + `IssueAndSendInvitationAsync`), phone-normalization algorithm, name-resolution determinism (global unique indexes), chain-status policy (structural coherence only, no Active gate in v1), and the three REVIEW-SPEC watch-items.

## Phase 1 — Design & Contracts

- [data-model.md](./data-model.md) — the transient batch types (no persisted entity), reused entities, and validation/normalization rules per field.
- [contracts/contracts.md](./contracts/contracts.md) — the CSV template contract (exact header columns), the `CreateUsersBatchAsync` service contract (inputs, per-row outcome, error reasons), and the controller route contract (`/Admin/Users/Batch`, `/Admin/Users/Batch/Template`).
- [quickstart.md](./quickstart.md) — how to build + run the E2E for this feature and a sample CSV.
- Agent context: update the `<!-- SPECKIT START -->…<!-- SPECKIT END -->` block in `CLAUDE.md` to point at this plan.

**Post-Design Constitution Re-Check**: PASS — design introduces no schema change, no new dependency, no cross-layer violation; the only HTTP-context coupling (invitation link composition) correctly stays in the Web layer.

## Complexity Tracking

> No constitution violations — section intentionally empty.
