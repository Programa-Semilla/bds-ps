# Phase 0 Research: Financial Disbursement Core

**Spec:** spec.md · **Date:** 2026-07-15 · **Branch:** 045-financial-disbursement-core

All decisions are grounded in shipped code (seams mapped from FundingAgreement / FundsUsageEvidence (036) / Auditor role (038/040) / dacpac + EF conventions). Format: Decision → Rationale → Alternatives rejected.

## R1 — Allocation amount source (the "approved total")

**Decision:** The allocation (participant's approved ceiling) is the executed application's CRC rollup, obtained by **reusing the existing single-source-of-truth `ApplicationCurrencyTotal.Compute(application).Total`** (`src/FundingPlatform.Application/Services/ApplicationCurrencyTotal.cs`) — NOT a hand-rolled join. It is **snapshotted into a single immutable `Allocation` ledger entry** the first time a disbursement is recorded.

**Rationale:** `FundingAgreement` carries **no monetary total** — it is a PDF-artifact aggregate. Selection is a **supplier** FK (`Item.SelectedSupplierId`, spec 038/043), not a quotation FK; the selected quote is `item.Quotations.FirstOrDefault(q => q.SupplierId == item.SelectedSupplierId)`, unique per approved item (`UX_Quotations_ItemId_SupplierId`). `Quotation.ConvertedCrcAmount` is **nullable** (`decimal(18,2)`) — a legacy/unconverted non-CRC quote leaves it null with `LegacyNeedsReview = true`. `ApplicationCurrencyTotal.Compute` already encodes exactly the right semantics: skip items with no `SelectedSupplierId`, skip `LegacyNeedsReview`, sum `ConvertedCrcAmount` — returning `(decimal? Total, bool HasNonCrc)`. Reusing it (already the rollup behind `ApplicantDashboardProjection` / `ReviewerQueueProjection`) avoids reinventing the join and guarantees the disbursement allocation equals the number shown elsewhere.

**Execution-time completeness:** there is no domain invariant that every selected non-CRC quote is converted, BUT funding-agreement PDF generation runs a pre-flight that throws `MissingConversionMetadataException` and refuses the document for any unconverted non-CRC line (spec 015). Since `AgreementExecuted` requires a generated+signed agreement, every selected non-CRC quote **was** converted by execution, and post-execution quotations are immutable — so `Compute.Total` is complete and stable for an executed application. `Compute.Total` being null (no selected supplier at all) cannot occur on a validly executed application.

**Load requirement:** `Compute` needs the aggregate loaded with `Items → Quotations (→ Supplier)`. `RecordAsync` and the balance projection's pre-first-disbursement fallback must Include these; once the `Allocation` entry is snapshotted, the projection reads the ledger (no heavy load).

**Alternatives rejected:** (a) hand-rolled `Σ ConvertedCrcAmount` — duplicates and risks drifting from `ApplicationCurrencyTotal` (the canonical rollup) and mishandles the nullable/legacy cases it already encodes; (b) a stored total on `FundingAgreement` — doesn't exist; (c) eager Allocation-entry backfill for all executed apps — unnecessary; the compute-fallback covers the pre-first-disbursement read. For SQL-side reporting over many apps, `ReportQueryService.ApplicationsApprovedTotalsAsync` is the per-currency equivalent (not needed in P1's per-application path).

## R2 — Disbursement as a standalone aggregate, not an Application child

**Decision:** `Disbursement`, `DisbursementEvidence`, and `DisbursementLedgerEntry` are **standalone aggregates keyed by `ApplicationId`** (FK, no navigation collection on `Application`), exactly like `FundsUsageEvidence`.

**Rationale:** Keeps the already-large `Application` aggregate from growing; matches the shipped 036 precedent; enables flat, index-friendly queries by `ApplicationId`. `VersionHistory` is Application-scoped and cannot host a standalone aggregate's audit anyway.

**Alternatives rejected:** child-of-`Application` collection — bloats the aggregate, complicates loading, and diverges from 036.

## R3 — Ledger physical model + the crux invariant

**Decision:** Two operational tables + one append-only ledger table:
- `dbo.Disbursements` — the **mutable operational record** (states Recorded/Inconsistent/Validated/Cancelled).
- `dbo.DisbursementEvidence` — one row per (disbursement, kind) with a `TINYINT EvidenceKind` discriminator (BankReceipt/Invoice), unique on `(DisbursementId, EvidenceKind)`.
- `dbo.DisbursementLedgerEntries` — **append-only**, entry types `Allocation`/`Disbursement`; a `Disbursement` entry is posted **at Validar** (immutable); the `Allocation` entry is posted at first disbursement.

The balance projection reads **both** sources and never double-counts: `Allocated` = ledger Allocation entry (or computed Σ before it exists); `Validated` = Σ ledger Disbursement entries; `Pending` = Σ `Disbursements` in {Recorded, Inconsistent}; `Paid = Validated + Pending`; `Available = Allocated − Paid`. A validated disbursement contributes **only** via its ledger entry (its `Disbursements` row is state=Validated and excluded from the Pending sum).

**Rationale:** This is the spec's ⚠️ crux invariant made physical — "the ledger holds only committed facts; pending records are mutable and off-ledger." It satisfies FR-017/018 (append-only ledger is the balance source, mutable-balance anti-pattern avoided, seed Risk 2) and is the substrate P6 needs (refund/reversal/credit-note/interest/fee entry types) so balance logic isn't rewritten later. NFR-002 (transactional, no partial writes) is satisfied for free: there is **no denormalized balance column** to corrupt — validation posts the ledger entry and flips state in one `SaveChanges`.

**Alternatives rejected:** projecting balances purely from `dbo.Disbursements` by state (no ledger table) — simpler for P1 but contradicts the approved spec's ledger mandate, leaves no immutable audit substrate, and forces a balance-logic migration at P6. Tracked in plan Complexity Tracking.

## R4 — Reconciliation engine representation

**Decision:** A **pure domain evaluator** `DisbursementReconciliation.Evaluate(disbursementAmount, bankReceiptAmount?, invoiceAmount?, sumOfNonCancelledIncludingThis, allocation)` → an ordered list of `ReconciliationDiscrepancy` value objects (comparison type, expected, actual, difference, source document, severity). It runs the three comparisons (disbursement↔receipt, disbursement↔invoice, Σ↔allocation), zero-tolerance, difference detected at 1 colón. Discrepancies are **computed, not persisted** in P1; only the derived `State` (Inconsistent vs Recorded) is stored on the `Disbursement`, recomputed on every mutation. Detail views recompute the discrepancy list on read.

**Rationale:** Determinism (NFR-020) is guaranteed by purity; storing only the derived state avoids stale-discrepancy bugs and a churny discrepancy table. The full persisted discrepancy aggregate with lifecycle (open→assigned→under-correction→waived) is explicitly P4. A missing evidence item is **incompleteness, not a discrepancy** — it leaves State=Recorded but fails the Validar completeness gate (FR-009); comparisons (1)/(2) simply don't run until their document exists.

**Alternatives rejected:** persisting a discrepancy table now — premature (P4 owns the lifecycle) and risks drift with the pure evaluator.

## R5 — Over-disbursement attribution

**Decision:** Comparison (3) is evaluated **per-disbursement at write time**: recording/editing disbursement D computes `Σ(non-cancelled disbursements, using D's new amount)`; if it exceeds the allocation, **D** becomes Inconsistent with an over-disbursement discrepancy (`difference = sum − allocation`). It clears if earlier disbursements are cancelled or D is reduced. Under-disbursement is never a discrepancy.

**Rationale:** Deterministic and local to the write; matches the spec Assumption. `Available` legibly goes **negative** (FR-020) — the projection counts the blocked pending disbursement toward `Paid` because the money left the bank.

**Concurrency (race resolution):** comparison (3) at write time is only an *early signal* — two operators recording partial payments concurrently could each read a Σ that individually fits under the ceiling (neither seeing the other), so both persist as `Recorded`, and single-row `RowVersion` optimistic concurrency cannot catch a *cross-row* invariant. The **authoritative over-disbursement gate is re-run inside `ValidateAsync`** against the freshly-read committed Σ (ledger Validated entries + non-cancelled pending): the second disbursement to validate sees the breach and is refused. This is lock-free and consistent with the append-only + all-blocking design — and it must be lock-free, because `AddSqlServerDbContext`'s retrying execution strategy forbids a user-initiated serializing transaction. A validation-time re-check is cheap (one aggregate query) and closes the race deterministically.

**Alternatives rejected:** a separate agreement-scoped discrepancy record — deferred to P4's discrepancy aggregate; over-engineered for P1. A serializable transaction / app-lock around the Σ check — forbidden by the retrying execution strategy and heavier than the validation-time re-check.

## R6 — Financial Operator role: group-scoped, consistent

**Decision:** Introduce `"Financial Operator"` as a **group-scoped** Identity role, reusing the Auditor (038/040) machinery. Groups are **optional** (empty → empty inbox, like Auditor), but the admin create/edit form **shows the group selector** and does **not** treat the role as groupless. Fix the lingering Auditor 038/040 drift *only* for this new role (do not replicate `Create.cshtml`'s selector-hidden-for-Auditor behavior).

**Rationale:** Correct authorization from slice 1 avoids retrofitting across P2–P9. `NormalizeGroupIdsForRole` already keeps groups for any non-Admin role, so no change there. Consistency (selector shown in both Create and Edit) avoids inheriting the known drift.

**Alternatives rejected:** reuse `Reviewer` (defers a cross-slice authorization migration); make groups mandatory via `RoleRequiresGroups` (diverges from Auditor and blocks a platform-wide operator setup — kept optional for parity).

## R7 — Audit trail: `disbursement.*` on AdminAuditEvent

**Decision:** Disbursement lifecycle audit (create/edit/replace/validate/cancel) uses **`AdminAuditEvent`** with a new `disbursement.*` event family and `TargetTypeDisbursement = "disbursement"`, following `funds_evidence.*`. Payload JSON carries the disbursement id + before/after values (FR-030's structured requirement). Add a `DeriveTarget` prefix branch; parse the real disbursement id from payload (supplier.* precedent) so `IX_AdminAuditEvents_Target` gives per-disbursement queryability.

**Rationale:** `VersionHistory` is Application-scoped and stores only an action string — no structured before/after. `AdminAuditEvent.PayloadJson` carries before/after and is the shipped pattern for standalone aggregates (funds_evidence, fund, company). The writer only `.Add()`s; the caller owns the SaveChanges boundary (two-SaveChanges pattern).

**Alternatives rejected:** `VersionHistory` — can't carry before/after and would wrongly tie a standalone aggregate to the Application timeline.

## R8 — Storage & upload flow (reuse 036 verbatim)

**Decision:** Add `FileCategory.DisbursementEvidence` (`[Description("disbursement-evidence")]`, container name; 20 MiB cap, `ServingMode.BackendStream`) + a `StorageCategoryOptions` block. Reuse `IObjectStorage`, `ObjectKey.Build`, `EvidenceFileTypePolicy` (magic-byte allow-list), and the `[UploadSizeGuard(FileCategory.DisbursementEvidence)]` Web filter. Upload = blob-first, then row (`SaveChanges #1`), then audit (`SaveChanges #2`), with best-effort blob cleanup on row-insert failure. Owner segment `application/{id}`.

**Rationale:** Byte-identical to `FundsUsageEvidenceService`; no new storage infrastructure; magic-byte validation and the 413 size guard come free.

**Alternatives rejected:** any new storage seam — violates reuse posture; FR-008 explicitly reuses existing controls.

## R9 — Concurrency, decimal, enums (house style)

**Decision:** Every table gets `RowVersion ROWVERSION` + `.IsRowVersion()`; services surface concurrency via the string-name filter in the Application layer (`ex.GetType().Name == "DbUpdateConcurrencyException"`) or the concrete type in Infrastructure. Money = raw `decimal` mapped `HasColumnType("decimal(18,2)")` / `DECIMAL(18,2)`; currency = separate `CurrencyCode` (`char(3)`). `LedgerEntryType`, `DisbursementState`, `EvidenceKind`, `DiscrepancySeverity` = `TINYINT` + **`HasConversion<byte>()`** (must be exercised against real SQL via E2E — InMemory hides the `Byte→Int32` throw). New `DbSet`s added to `AppDbContext`; EF configs auto-registered by `ApplyConfigurationsFromAssembly`.

**Rationale:** Matches Quotation/ExchangeRate/ProcessEvent/Fund precedents exactly; no `Money` value object exists in the codebase, so raw decimal + separate currency is house style.

**Alternatives rejected:** introducing a `Money` value object — not house style, out of scope, would ripple.

## R10 — Routing & read surface

**Decision:** New `DisbursementController` at route `Applications/{applicationId:int}/Disbursements` (mirrors `FundsUsageEvidenceController`), `[Authorize(Roles = "Financial Operator,Admin")]` for write, with Auditor/Admin **read-only** access to a balance + disbursement list (controls hidden by role). Out-of-group → flat `NotFound()`; applicant → 403 (role attribute). Scope via `IReviewerScopeProvider` + `ApplicantSharesAnyGroupAsync`. The surface shows only disbursements + the five-dimension balance (the **financial surface only**, not the full application).

**Rationale:** Reuses the exact reviewer/auditor scoping and 404-no-disclosure patterns; the financial-surface-only scope is the spec's Open-Question default.

**Secure-by-default sequencing:** group-overlap scoping + the executed-state gate + no-disclosure 404 are built into the controller in **US1** (not deferred), so the surface is never reachable out-of-group even at the US1 checkpoint. US5 then adds only the Auditor **read-only write-guard** (403 on POST) and the edge-case tests (applicant refusal, read-only vs write). This removes the window where an in-development US1 build could expose out-of-group agreements.

**Alternatives rejected:** embedding into the reviewer application detail — leaks the full application to the operator and entangles with review UI. Deferring all scoping to US5 — leaves US1 insecure if demoed/deployed alone.

## Resolved unknowns

No `NEEDS CLARIFICATION` remain. The two spec Open Questions were pre-resolved with defaults (single free-text bank/account reference, no payment-type enum; financial-surface-only). The plan-time thread (over-disbursement discrepancy shape) is resolved in R5 (attach to the crossing disbursement, computed not persisted).
