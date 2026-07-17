# Implementation Plan: Evidence Graph & Required-Document Rules

**Branch**: `047-evidence-graph-required-docs` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/047-evidence-graph-required-docs/spec.md`
**Program**: Financial-execution slice **P3 of 9** (builds on P1/045, P2/046).

## Summary

Turn P1/P2's thin, hard-coded disbursement evidence into a configurable, versioned **evidence graph** with per-line amount allocation, admin-defined **required-document rules**, a signed-acceptance reconciliation leg, and an explicit **budget-line closure gate**. Technical approach (from research): **additive-only** — a new `Evidence` aggregate + version chain + `Evidence↔Item` allocation table alongside the untouched `DisbursementEvidence` money-gate (D1); a stored `Item.ClosureState` + closure metadata surfaced through the derived `BudgetLineStatus` ladder (D3); a `DocumentRuleSet`/`DocumentRuleItem` admin matrix mirroring `ChecklistTemplate` but without a response-snapshot table (D5); and a new pure per-line equality-chain reconciliation leg (`LinePaid == LineAccepted`) with the invoice leg inherited from P1/P2 (D6). Reuses the `Financial Operator` role, `IObjectStorage` stack, `[UploadSizeGuard]`/magic-byte policy, and the two-SaveChanges audit pattern. **No new managed deps; 5 new tables + additive `Items` columns + one seed script.**

## Technical Context

**Language/Version**: C# / .NET 10 (net10.0), EF Core 10
**Primary Dependencies**: ASP.NET MVC, ASP.NET Identity, .NET Aspire, `IObjectStorage` (Azurite/Azure Blob), Syncfusion (unaffected). **No new managed dependency.**
**Storage**: SQL Server via dacpac (schema-first, no EF migrations). New: `dbo.Evidence`, `dbo.EvidenceVersions`, `dbo.EvidenceLineAllocations`, `dbo.DocumentRuleSets`, `dbo.DocumentRuleItems`; additive columns on `dbo.Items`; one post-deploy seed script. Blob for evidence files.
**Testing**: xUnit (Unit), Integration (real SQL), Playwright E2E (AspireFixture, ephemeral). Delivery bar = filtered E2E green for the changed classes.
**Target Platform**: Linux container (Aspire-orchestrated)
**Project Type**: Web (server-rendered MVC), clean architecture (Domain/Application/Infrastructure/Web)
**Performance Goals**: per-line completeness + equality checks recompute on read; per-line sums use covering indexes (`IX_EvidenceLineAlloc_ItemId`); closure re-reads fresh sums. No new N+1 on the disbursement `Index` (batch the completeness resolve).
**Constraints**: zero-colón reconciliation (0.01 tolerance); es-CR copy; additive dacpac-only; preserve P1/P2 behavior (SC-006 regression).
**Scale/Scope**: per-application evidence (tens of docs); admin matrix is small (≤ #categories + 1 default). 4 user stories, 5 tables, ~1 new controller + 1 admin surface.

## Constitution Check

*GATE: must pass before Phase 0 and re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| **I. Clean Architecture** | ✅ PASS | New `Evidence`/closure/docrule entities in Domain; `IEvidenceService`/`IBudgetLineClosureService`/`IDocumentRuleService` + reasons in Application; EF configs + service impls in Infrastructure; controllers/views in Web. Service-produced reasons live in Application (`EvidenceReasons`/`DocRuleReasons`), not Web (D7). |
| **II. Rich Domain Model** | ✅ PASS | `Evidence.Attach/ReplaceCurrent`, `EvidenceVersion` immutability + supersede transition, `Item.Close/Reopen`, `Item.MissingRequiredDocuments`, `DocumentRuleSet` full-replace, pure `EvaluateLineEquality` — behavior on entities/domain services. Service-enforced gates (no-open-work) follow the established `Item.cs` convention where the entity can't see cross-aggregate sums. |
| **III. E2E Testing (NON-NEGOTIABLE)** | ✅ PASS | Each user story gets Playwright E2E (see quickstart): `EvidenceGraphAllocation`, `RequiredDocMatrixCompleteness`, `BudgetLineClosure`, `EvidenceVersionHistory` + P1/P2 `Disbursement*` regression (SC-006). |
| **IV. Schema-First DB** | ✅ PASS | All schema via dacpac `.sql`; enums `TINYINT` + `HasConversion<byte>()`; nullable-safe inline `Items` adds (no backfill); seed via post-deploy script. No EF migrations. |
| **V. Specification-Driven** | ✅ PASS | spec.md → plan.md → tasks.md; stories independently deliverable/testable. |
| **VI. Simplicity/Progressive Complexity** | ✅ PASS | Additive-alongside (D1) over risky generalization; matrix drops the response-snapshot table since completeness is live + closure is stored (D5); invoice leg inherited from P1/P2 rather than re-modeled (D6); credit-note/refund evidence-only. Deferrals to P4–P9 explicit. |

**Complexity Tracking**: no violations to justify — the slice is additive and reuses established patterns. (Table omitted.)

**Post-Phase-1 re-check**: design in data-model.md/contracts stays within the above; no new deps, no cross-layer leaks, no cascade-path violations (the one two-path risk — `EvidenceLineAllocation` — is resolved with the `ItemImpacts`/`DisbursementLineAllocation` CASCADE/NO-ACTION topology). ✅ PASS.

## Project Structure

### Documentation (this feature)

```text
specs/047-evidence-graph-required-docs/
├── spec.md              # requirements (done)
├── plan.md              # this file
├── research.md          # Phase 0 — D1..D8 decisions (done)
├── data-model.md        # Phase 1 — entities/tables (done)
├── contracts/
│   └── interfaces.md     # Phase 1 — service interfaces + routes + audit (done)
├── quickstart.md        # Phase 1 — how to exercise each story (done)
├── REVIEW-SPEC.md        # spec gate (SOUND)
├── review_brief.md
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Domain/
    Entities/            Evidence.cs, EvidenceVersion.cs, EvidenceLineAllocation.cs,
                         DocumentRuleSet.cs, DocumentRuleItem.cs; Item.cs (+ClosureState/Close/Reopen/MissingRequiredDocuments)
    Enums/               EvidenceType.cs, ItemClosureState.cs
    Services/            DisbursementLineReconciliation.cs (+EvaluateLineEquality)
    Entities/            AdminAuditEvent.cs (+docrule.*/evidence.*/closure.* verbs)
  FundingPlatform.Application/
    Evidence/            IEvidenceService.cs, IBudgetLineClosureService.cs, DTOs, EvidenceReasons.cs
    DocRules/            IDocumentRuleService.cs, DTOs, DocRuleReasons.cs
    Disbursements/       ComposedBalanceDtos.cs (+Closed status, +EvidenceIncomplete), IParticipantBalanceProjection (completeness surface)
  FundingPlatform.Infrastructure/
    Services/            EvidenceService.cs, BudgetLineClosureService.cs, DocumentRuleService.cs,
                         ParticipantBalanceProjection.cs (+DeriveStatus Closed), completeness resolver
    Persistence/Configurations/  Evidence*, EvidenceVersion*, EvidenceLineAllocation*, DocumentRule*, ItemConfiguration (+closure cols)
    Persistence/AppDbContext.cs  (+DbSets)
    Audit/AdminAuditEventWriter.cs (+DeriveTarget branches)
    Storage/             FileCategory.Evidence + StorageOptions
    DependencyInjection.cs (+AddScoped service registrations)
  FundingPlatform.Web/
    Controllers/         EvidenceController.cs (new); DisbursementController.cs (+Close/Reopen, completeness badges); AdminController.cs (+DocumentRules)
    Views/Evidence/      Index.cshtml, Detail.cshtml, _EvidenceRow.cshtml, _VersionHistory.cshtml, _CompletenessMatrix.cshtml
    Views/Admin/         DocumentRules.cshtml, CreateDocumentRule.cshtml, EditDocumentRule.cshtml, _DocumentRuleItemsEditor.cshtml
    ViewModels/          EvidenceViewModels.cs, Admin/DocumentRuleAdminViewModels.cs
    Resources/           EvidenceResources.cs, DocRuleResources.cs
  FundingPlatform.Database/
    Tables/              dbo.Evidence.sql, dbo.EvidenceVersions.sql, dbo.EvidenceLineAllocations.sql,
                         dbo.DocumentRuleSets.sql, dbo.DocumentRuleItems.sql; dbo.Items.sql (+columns)
    PostDeployment/      NN_SeedDocumentRules.sql (global-default set)
tests/
  Unit/                EvidenceAllocationTests, LineEqualityReconciliationTests, ItemClosureTests, DocumentRuleResolutionTests, EvidenceVersionTests
  Integration/         EvidenceGraphTests, ClosureGateTests, DocumentRuleMatrixTests (real SQL)
  E2E/                 EvidenceGraphAllocationTests, RequiredDocMatrixCompletenessTests, BudgetLineClosureTests, EvidenceVersionHistoryTests
```

**Structure Decision**: Standard 4-layer clean architecture (already in place). New code slots into existing folders; the only cross-cutting edits are additive (`Item`, `AdminAuditEvent`, `AdminAuditEventWriter`, `AppDbContext`, `DependencyInjection`, `ComposedBalanceDtos`, `ParticipantBalanceProjection`, `DisbursementLineReconciliation`, `DisbursementController`, `AdminController`).

## Phasing (maps to spec user stories — independently deliverable)

1. **US1 (P1) Evidence graph + allocation** — `Evidence`/`EvidenceVersion`/`EvidenceLineAllocation` tables + `IEvidenceService` attach/allocate/download + `EvidenceController` + storage category. Checkpoint: AC-002/AC-003 via `EvidenceGraphAllocationTests`.
2. **US2 (P2) Required-doc matrix + completeness** — `DocumentRuleSet`/`Item.MissingRequiredDocuments` + admin surface + live completeness read (both sources D1). Checkpoint: AC-005 / `RequiredDocMatrixCompletenessTests`.
3. **US3 (P3) Closure gate** — `Item.ClosureState`/`Close`/`Reopen` + `IBudgetLineClosureService` + `EvaluateLineEquality` + `DeriveStatus` Closed. Checkpoint: `BudgetLineClosureTests` + **SC-006 P1/P2 regression green**.
4. **US4 (P4) Version history** — `EvidenceVersion` replace-appends + version view/download. Checkpoint: `EvidenceVersionHistoryTests` (can land independently of US3).

Commit + push at each story checkpoint (Speckit discipline). Keep the `Disbursement*` P1/P2 E2E regression green at every checkpoint.

## Open threads → `/speckit-tasks`

- Global-default seed contents (proposed: Bank Receipt + Invoice + Signed Acceptance = Required).
- Batch the per-line completeness resolve on the disbursement `Index` to avoid N+1.
- Whether closure/reopen live on a dedicated `IBudgetLineClosureService` or extend `IDisbursementService` (leaning dedicated for cohesion).
