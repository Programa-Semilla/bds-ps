# Quickstart: Financial Disbursement Core

**Spec:** spec.md · **Date:** 2026-07-15

How to exercise the feature end-to-end and the E2E scenarios that form the delivery gate (Constitution III; CLAUDE.md filtered-E2E bar).

## Run

```bash
dotnet run --project src/FundingPlatform.AppHost      # dev (Aspire: Web + SQL + Azurite + smtp4dev)
dotnet build FundingPlatform.slnx
```

## Preconditions to reach the surface

A disbursement needs an **executed** application (`State == AgreementExecuted`). Reuse the E2E seeder that specs 036/040 use to fast-path an executed agreement (`FundingAgreementSeeder.SeedExecutedAgreementAsync`). Seed a `Financial Operator`:

```
RegisterUserAsync(page, "finop@programa-semilla.test", ...)   // SeedUser + AssignAllGroups
AssignRoleAsync("finop@programa-semilla.test", "Financial Operator")   // requires the role added to AccountController.AssignRole allow-list
LoginAsync(...)
```

Navigate to `/Applications/{id}/Disbursements`.

## Manual smoke (the AC-001 thread)

1. Record a disbursement: fecha, **₡85,800**, bank transaction ref.
2. Upload the **bank receipt** (amount ₡85,800) and the **invoice** (amount **₡85,728**).
3. Detail shows a **₡72** discrepancy, source = factura, state **Inconsistente**; **Validar** is refused.
4. Edit the invoice amount → ₡85,800. Discrepancy clears automatically; state **Registrado**; **Validar** enabled.
5. Validar → state **Validado**; balance `Validated` rises by ₡85,800, `Pending` returns to 0. Edit/Cancel now refused.

## E2E scenarios (delivery gate — filter to these classes)

**`DisbursementReconciliationTests`** (US1)
- `RecordWithMismatchedInvoice_FlagsColonDiscrepancy_BlocksValidation` (SC-001, AC-001).
- `MissingInvoice_CannotValidate_ShowsMissing` (SC-002, AC-005).
- `CorrectInvoice_ClearsDiscrepancy_AllowsValidation` (SC-003).

**`ParticipantBalanceTests`** (US2)
- `FiveDimensions_ReconcileExactly_AsDisbursementsRecordedAndValidated` (SC-004): assert Allocated/Paid/Validated/Pending/Available at zero, after-record, after-validate (Available unchanged by validation).

**`DisbursementPartialAndOverTests`** (US3)
- `PartialPayments_SumWithinTotal_Succeed_AvailableToZero` (SC-005 boundary).
- `OverDisbursement_Blocked_AvailableGoesNegative` (SC-005 + FR-020 negative signal).

**`DisbursementLifecycleTests`** (US4)
- `PreValidation_EditReplaceCancel_Allowed_ReconciliationReruns`.
- `Validated_EditAndDelete_Refused` (SC-006).
- `EveryAction_AppearsInAuditTrail_WithActorAndBeforeAfter` (SC-007) — assert via the admin audit surface / `AdminAuditEvents` `disbursement.*`.

**`DisbursementRoleScopingTests`** (US5)
- `FinancialOperator_InGroup_CanAct__OutOfGroup_404` (SC-008 no-disclosure).
- `AuditorAndAdmin_ReadOnly_NoWriteControls__WritePost_403`.
- `Applicant_Refused`.

## Real-SQL gotcha to prove

The `TINYINT` enum columns (`State`, `Kind`, `EntryType`) with `HasConversion<byte>()` **must** be exercised against real SQL — the `Byte→Int32` materialization throw is hidden by EF-InMemory and only surfaces in Integration (real DB) / E2E. Ensure at least one Integration test materializes each enum column from SQL Server.

## Integration tests (real DB, no mocks — CLAUDE.md)

- `DisbursementLedgerTests`: append-only invariant (validated disbursement posts exactly one ledger entry; filtered-unique blocks a second); balance projection math (`Paid = Validated + Pending`, `Available = Allocated − Paid`, negative allowed); Allocation snapshot equals Σ ConvertedCrcAmount.
- `DisbursementReconciliationEvaluatorTests` (unit, pure): the three comparisons, 1-colón detection, missing-evidence-not-a-discrepancy, over-disbursement difference.
