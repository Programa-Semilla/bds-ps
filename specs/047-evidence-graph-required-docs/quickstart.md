# Quickstart: Evidence Graph & Required-Document Rules (spec 047)

How to exercise each user story once implemented. Roles/seeds per CLAUDE.md (ephemeral E2E: `admin@programa-semilla.test` / `Sentinel123!`; a `Financial Operator` seed in the application's group). Requires an application in state `AgreementExecuted` with budget-lines (reuse `FundingAgreementSeeder.SeedExecutedAgreementAsync` + a validated P1/P2 disbursement).

## Run

```bash
dotnet build FundingPlatform.slnx
dotnet run --project src/FundingPlatform.AppHost      # dev
# Filtered E2E (delivery bar):
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~EvidenceGraphAllocation|FullyQualifiedName~RequiredDocMatrixCompleteness|FullyQualifiedName~BudgetLineClosure|FullyQualifiedName~EvidenceVersionHistory"
# P1/P2 regression (must stay green — SC-006):
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~Disbursement"
```

## US1 — Evidence graph + per-line allocation (AC-002 / AC-003)

1. As Financial Operator, open `/Applications/{id}/Evidence`.
2. Attach one **Invoice** (₡400,000) and allocate ₡100,000 to each of lines 1–4 → all four link; total 400,000 reconciles to the invoice amount. **Verify**: allocating > 400,000 total is refused (`AllocationExceedsAmount`).
3. Attach a **Signed Acceptance** with no disbursement selected, allocate across lines → stored (payment-independent).
4. Attach five separate Invoices to one line → all five retained, per-line sum shown.
5. **Verify**: an evidence with no line link and no disbursement is refused (`Orphaned`).

## US2 — Required-doc matrix + completeness (AC-005)

1. As Admin, open `/Admin/DocumentRules`. For category *Producto*, mark **Invoice** + **Signed Acceptance** = Required. Save (audited `docrule.upserted`).
2. As Financial Operator, open a *Producto* budget-line with only a bank receipt → completeness matrix lists **Invoice** and **Signed Acceptance** as *missing*; `EvidenceIncomplete` badge shows.
3. A category with no rule falls back to the **global default** set.
4. **Verify**: a disbursement's Bank Receipt/Invoice (from `DisbursementEvidence`) counts as *present* for the lines that validated disbursement paid (both-source completeness, D1).

## US3 — Budget-line closure gate

1. Build a line where all required docs present, its payment Validated, and `LinePaid == Σ signed-acceptance allocations` to the colón.
2. `POST /Applications/{id}/Lines/{itemId}/Close` → line reaches **Closed**; status pill shows Closed.
3. **Verify blocks**: introduce a ₡72 acceptance shortfall → close refused (`LineEqualityMismatch`, discrepancy shown); remove the required invoice → refused (`MissingRequiredDocuments`); leave a payment `Recorded` (not Validated) → refused (`PaymentNotValidated`).
4. On a Closed line, attaching/replacing/allocating evidence is refused (`EvidenceLocked`).
5. `POST /Lines/{itemId}/Reopen` with a reason → line unlocks (audited `closure.line_reopened`); **no ledger entry / balance change** (assert balances identical before/after).

## US4 — Evidence version history (AC-008 portion)

1. Attach an Invoice; then `POST /Evidence/{id}/Replace` with a corrected file + reason.
2. Open `/Applications/{id}/Evidence/{id}` → both versions listed; original marked **superseded**, each with actor/timestamp/reason + hash.
3. Download `?v=1` (original) and `?v=2` (current) → both retrievable.
4. **Verify**: replace with no reason → refused (`ReasonRequired`); editing the Amount also appends a version.

## Cross-cutting checks

- Out-of-group Financial Operator → flat 404 on every `/Evidence` and `/Lines/{}/Close` route (SC-007).
- Auditor/Admin can read evidence + version history but cannot attach/replace/allocate/close (403 on write).
- Upload > 20 MiB or non-allow-listed magic bytes → rejected at the controller boundary (NFR-005).
