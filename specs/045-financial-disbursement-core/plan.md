# Implementation Plan: Financial Disbursement Core

**Branch**: `045-financial-disbursement-core` | **Date**: 2026-07-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/045-financial-disbursement-core/spec.md`

## Summary

Stand up the money-execution spine downstream of an executed funding agreement (slice P1 of a 9-slice program). A group-scoped **Financial Operator** records **Disbursements** against an executed application, attaches a typed **bank receipt + invoice**, and a **pure reconciliation evaluator** compares the three amounts to the colón (all discrepancies blocking). Balances are projected from an **append-only ledger** (Allocation + Disbursement entries) plus mutable off-ledger pending disbursements, yielding five dimensions (Allocated/Paid/Validated/Pending/Available). Disbursements are freely correctable until an explicit **Validar**, then locked; every action is audited. Reuses the shipped FundsUsageEvidence (036) storage/upload stack, the Auditor (038/040) role+group-scoping machinery, and the AdminAuditEvent trail. **No new managed dependencies; additive dacpac-only schema.**

Key research findings (see [research.md](./research.md)): `FundingAgreement` carries no total → allocation is computed as Σ `Quotation.ConvertedCrcAmount` and snapshotted into a single `Allocation` ledger entry; disbursement audit uses `AdminAuditEvent` `disbursement.*` (VersionHistory can't carry before/after and is Application-scoped); `TINYINT` enums require `HasConversion<byte>()` verified against real SQL.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), EF Core 10
**Primary Dependencies**: ASP.NET MVC, ASP.NET Identity, .NET Aspire, SQL Server (dacpac), `IObjectStorage` (Azurite/Azure Blob), Playwright (E2E) — all existing. **No new managed dependency.**
**Storage**: SQL Server via EF (data-access only); blobs via existing `IObjectStorage`. New tables: `dbo.Disbursements`, `dbo.DisbursementEvidence`, `dbo.DisbursementLedgerEntries`. Schema authored in `FundingPlatform.Database` (dacpac) — no EF migrations.
**Testing**: Unit (pure reconciliation evaluator), Integration (real SQL — ledger/projection/enum-materialization), E2E (Playwright, Page Object Model) as the primary gate.
**Target Platform**: Linux container (Aspire-orchestrated); es-CR default culture.
**Project Type**: Web application (server-rendered MVC), Clean Architecture 4-layer.
**Performance Goals**: interactive admin/operator CRUD + reconciliation on write; balance projection is a small per-application aggregate query (indexed by `ApplicationId`), no batch/throughput target.
**Constraints**: exact-decimal money (`decimal(18,2)`, no float); deterministic reconciliation; transactional balance (satisfied structurally — no denormalized balance column); optimistic concurrency (`RowVersion`); zero-tolerance reconciliation; CRC only.
**Scale/Scope**: one new aggregate cluster (3 tables), one new role, one controller + views, one pure domain service + two Application services. Bounded thin slice; no cross-cutting migration of existing data.

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. Result: PASS (one tracked complexity).*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | PASS. Domain: `Disbursement`, `DisbursementEvidence`, `DisbursementLedgerEntry`, enums, pure `DisbursementReconciliation`, VO `ReconciliationDiscrepancy`/`ParticipantBalance`. Application: `IDisbursementService`, `IParticipantBalanceProjection`, DTOs/commands. Infrastructure: service impls, EF configs, repo query. Web: `DisbursementController`, views, VMs. Dependencies point inward; no Web/Infra leakage into Domain/Application. |
| **II. Rich Domain Model** | PASS. State machine (Record/EditDetails/ApplyReconciliation/Validate/Cancel) and invariants (executed-gate, amount>0, CRC, IsValidatable, locked-after-validated, append-only ledger factories) live on the entities; the pure evaluator holds reconciliation logic. Services orchestrate, don't own rules. |
| **III. E2E (non-negotiable)** | PASS. Five E2E classes, one per user story (US1–US5), golden + error paths, Page Object Model; filtered-E2E is the delivery gate (see quickstart.md). |
| **IV. Schema-First (dacpac)** | PASS. Three new `.sql` tables + `10_SeedFinancialOperatorRole.sql` post-deploy (`:r` from `SeedData.sql`, dual-listed in `.sqlproj`). EF = data access only; no migrations/`EnsureCreated`. |
| **V. Spec-Driven** | PASS. spec.md → plan.md → (next) tasks.md; user stories independently testable/deliverable. |
| **VI. Simplicity / Progressive Complexity** | PASS with one tracked item (the ledger table — see Complexity Tracking). Everything else reuses shipped seams; YAGNI honored (discrepancies computed not persisted; no payment-type enum; groups optional; no Money VO). |

**Additional constitution gates (Quality Gates section):** all validation errors surfaced at once (`Result`/`Result<T>`); optimistic concurrency via `RowVersion`; authorization verifies group-overlap ownership + executed-state (flat 404 no-disclosure). All satisfied.

## Project Structure

### Documentation (this feature)

```text
specs/045-financial-disbursement-core/
├── spec.md              # complete
├── plan.md              # this file
├── research.md          # Phase 0 (R1–R10)
├── data-model.md        # Phase 1 — entities, tables, enums, projection
├── contracts/
│   └── interfaces.md    # Phase 1 — service interfaces, HTTP surface, audit vocab
├── quickstart.md        # Phase 1 — run + E2E gate
├── checklists/
│   └── requirements.md  # spec quality gate (passed)
├── REVIEW-SPEC.md       # spec review (SOUND)
├── review_brief.md      # reviewer guide
└── tasks.md             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/           Disbursement.cs, DisbursementEvidence.cs, DisbursementLedgerEntry.cs
│   ├── Enums/              DisbursementState.cs, EvidenceKind.cs, LedgerEntryType.cs,
│   │                       DiscrepancySeverity.cs, ReconciliationComparison.cs
│   ├── ValueObjects/       ReconciliationDiscrepancy.cs, ParticipantBalance.cs
│   └── Services/           DisbursementReconciliation.cs (pure)
├── FundingPlatform.Application/
│   └── Disbursements/      IDisbursementService.cs, IParticipantBalanceProjection.cs,
│                           DisbursementDtos.cs  (+ es-CR reasons if service-produced)
├── FundingPlatform.Infrastructure/
│   ├── Services/           DisbursementService.cs, ParticipantBalanceProjection.cs
│   ├── Persistence/
│   │   └── Configurations/ Disbursement/DisbursementEvidence/DisbursementLedgerEntry Configuration.cs
│   ├── Persistence/AppDbContext.cs      (+3 DbSets)
│   ├── Identity/           IdentityConfiguration.cs, UserAdministrationService.cs (role wiring)
│   ├── Audit/              AdminAuditEventWriter.cs (disbursement.* DeriveTarget branch)
│   └── DependencyInjection.cs           (register services)
├── FundingPlatform.Application/Abstractions/Storage/  FileCategory.cs, StorageOptions.cs (new category)
├── FundingPlatform.Web/
│   ├── Controllers/        DisbursementController.cs; Admin/AdminUsersController.cs (role gating);
│   │                       AccountController.cs (AssignRole allow-list)
│   ├── Views/Disbursements/ Index.cshtml, Detail.cshtml, _DisbursementRow, _BalanceCard, _DiscrepancyList
│   ├── Views/Shared/_Layout.cshtml      (sidebar entry)
│   ├── Views/Admin/Users/  Create.cshtml, Edit.cshtml (role list + group-selector JS)
│   ├── ViewModels/Disbursements/
│   └── Resources/          DisbursementResources.resx (es-CR)
└── FundingPlatform.Database/
    ├── Tables/             dbo.Disbursements.sql, dbo.DisbursementEvidence.sql, dbo.DisbursementLedgerEntries.sql
    ├── PostDeployment/     10_SeedFinancialOperatorRole.sql  (+ :r in SeedData.sql)
    └── FundingPlatform.Database.sqlproj  (Build Remove + None Include for the new script)

tests/
├── FundingPlatform.Tests.Unit/         DisbursementReconciliationEvaluatorTests
├── FundingPlatform.Tests.Integration/  DisbursementLedgerTests, DisbursementProjectionTests (real SQL)
└── FundingPlatform.Tests.E2E/          DisbursementReconciliation/ParticipantBalance/
                                        PartialAndOver/Lifecycle/RoleScoping Tests + PageObjects
```

**Structure Decision**: Existing 4-layer Clean Architecture web app (`FundingPlatform.slnx`). The feature is additive across all four layers plus the dacpac, mirroring the `FundsUsageEvidence` (036) footprint. No new project; no structural change.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Dedicated append-only **ledger table** (`dbo.DisbursementLedgerEntries`) when P1 balances are derivable from `dbo.Disbursements` by state + a computed allocation | Mandated by spec FR-017/018 as the seed's **Risk-2 (mutable-balance) mitigation** and the crux invariant the user ratified; it is the substrate P6 (refunds/reversals/credit-notes/interest/fees) and P2 (budget-line dimension) build on. A current requirement, not speculative. | A state-only projection over `Disbursements` is simpler for P1 but (a) contradicts the approved spec's explicit ledger mandate, (b) establishes no immutable audit substrate for the reconciliation/audit requirements, and (c) forces a balance-logic migration when P6 introduces non-disbursement entry types. |

## Phase 2 note

`/speckit-tasks` will generate `tasks.md` (phased, per-user-story, with dependencies and parallel markers). Suggested phasing: Foundation (enums, entities, dacpac tables, EF configs, DbSets, DI, role wiring + seed) → US1 (record + evidence + pure evaluator + Validar) → US2 (balance projection + card) → US3 (partial/over-disbursement) → US4 (lifecycle/lock/audit) → US5 (role scoping/read-only). US1 is the MVP checkpoint.
