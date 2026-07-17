# Research: Evidence Graph & Required-Document Rules (spec 047)

**Date:** 2026-07-16 · **Branch:** `047-evidence-graph-required-docs`

Phase 0 resolves the three spec Open Questions (OQ-1/2/3) plus the reconciliation/completeness modeling, grounded in a full read of the P1 (045) / P2 (046) code and the closest reusable precedents (spec 040 ChecklistTemplate, spec 035 Category/CategoryField, spec 036 FundsUsageEvidence). All decisions favor **additive, low-churn** changes that leave P1's clean money-gate untouched (Constitution VI).

---

## D1 — Evidence storage shape (resolves OQ-1)

**Decision:** **Add a new `Evidence` aggregate + table alongside `DisbursementEvidence`. Do NOT generalize `DisbursementEvidence`.**

**Rationale:**
- P1's `DisbursementEvidence` is the **money-gate**: exactly one Bank Receipt + one Invoice per disbursement, `UX_(DisbursementId,Kind)` plain unique, CRC-only, positive-amount CHECKs, pre-validation guard, in-place `Replace`. Its coupling is narrow — 7 query sites in one service (`DisbursementService`) — and the pure reconciler `DisbursementReconciliation.Evaluate` takes amounts as **`decimal?` params**, never touching the table. `ParticipantBalanceProjection` never reads evidence at all.
- Generalizing forces risk onto that critical path: make `DisbursementId` nullable, convert the plain unique into a **filtered** unique, relax both CHECKs, and expand `EvidenceKind` — a real migration on the audited money path, for invariants the graph does not want.
- There is already a **two-evidence-table precedent**: `FundsUsageEvidence` (spec 036) is a standalone Application-scoped evidence aggregate coexisting with `DisbursementEvidence`, sharing only the `IObjectStorage` plumbing. A third evidence aggregate is idiomatic here, not a smell.

**Consequences / division of responsibility:**
- `DisbursementEvidence` (unchanged) remains the source of the disbursement's mandatory **Bank Receipt + Invoice**. Its per-line distribution is the **existing** `DisbursementLineAllocation` (P2 payment split) — no new modeling for disbursement invoices.
- The new `Evidence` graph hosts the **payment-independent** evidence — **Signed Acceptance, Credit Note, Refund Receipt, Other** — plus optional supplementary Bank Receipt/Invoice not tied to a disbursement, each with the version chain and M:N line allocation.
- **Completeness (per line) reads BOTH sources** (resolves the REVIEW-SPEC plan note): a required Bank Receipt / Invoice is *present* for a line if a **validated disbursement paying that line** carries that `DisbursementEvidence.Kind`, OR a graph `Evidence` of that type is linked to the line. Acceptance/credit-note/refund/other are checked only against the graph.

**Alternatives considered:** Option A (generalize `DisbursementEvidence`) — rejected: highest churn on the risk-bearing money path (see above).

---

## D2 — Per-line evidence allocation (`Evidence ↔ Item`)

**Decision:** New M:N join `EvidenceLineAllocation`, mirroring `DisbursementLineAllocation` **exactly**.

**Shape (copied from `DisbursementLineAllocation` + `dbo.DisbursementLineAllocations.sql`):**
- Columns: `Id`, `EvidenceId`, `ItemId`, `Amount decimal(18,2)`, `RowVersion`.
- **FK cascade topology (load-bearing):** Evidence FK = `CASCADE`; Item FK = EF `ClientCascade` / DB **`NO ACTION`** — because both `Evidence→Application` and `Item→Application` reach `Applications`, and two cascade paths fail the dacpac publish (the spec-029/035 `ItemImpacts` lesson).
- `UX_EvidenceLineAlloc_Evidence_Item UNIQUE (EvidenceId, ItemId)` (≤1 row per evidence/line); `CK_..._Amount_Positive CHECK (Amount > 0)`; covering `IX_..._ItemId` for per-line sums.
- **No mutators** on the entity; a `static For(evidenceId, itemId, amount)` factory throws when `amount <= 0`. Allocation edits are **replace-all** (delete existing rows for the evidence, re-insert), copying `DisbursementService.ReplaceSplitAsync`.

**Allocation integrity:** Σ of an evidence's per-line allocations ≤ its amount, enforced at attach/edit (blocking to save); must **equal** the amount at closure for *required* evidence.

---

## D3 — Budget-line `Closed` state + closure metadata (resolves OQ-2)

**Decision:** **Stored** closure state on `Item` (not purely derived), mirroring P2's `CommitState`; surface it through the derived `BudgetLineStatus` ladder.

**Rationale:** `Closed` is an operator decision with **no monetary signal** to derive it from (the balance-projection ladder is a pure function of commit state + payment sums; nothing there means "closed"). So the trigger must be stored. But `BudgetLineStatus` is the established read-surface for line state, so also thread it through.

**Model:**
- New `Item.ClosureState` enum stored as `TINYINT NOT NULL DEFAULT(0)` (`ItemClosureState : byte` = Open=0 / Closed=1), with `.HasConversion<byte>().IsRequired().HasDefaultValue(Open)` (the mandatory byte-conversion gotcha) — nullable-safe inline add, no backfill (spec 032/037/046 precedent).
- Nullable closure metadata on `Item`, following `Disbursement.Validate`'s actor+timestamp stamp + `FundingAgreement.ConfirmByAuditor`/clear-on-reversal precedent: `ClosedByUserId NVARCHAR(450) NULL` (FK → AspNetUsers, `ON DELETE NO ACTION`), `ClosedAtUtc DATETIME2 NULL`, `ClosureReason NVARCHAR(500) NULL`, `ReopenReason NVARCHAR(500) NULL` (modeled on `Item.ReviewComment`).
- `internal void Close(userId)` / `internal void Reopen(userId, reason)` idempotent mutators (mirror `Commit()`/`Uncommit()` + `Validate` stamping). The **"no open work / gate satisfied" guard is enforced in the service**, not the entity (the `Item.cs` convention — the entity can't see attributions/evidence). `Reopen` clears `ClosedBy/ClosedAt`, sets `ReopenReason`, flips state to Open.
- Extend `DeriveStatus` (`ParticipantBalanceProjection`) with a leading `if (closed) return Closed;` and add `Closed` to `BudgetLineStatus`. Add an `EvidenceIncomplete` indicator (derived: line has ≥1 missing required doc) — surfaced as a flag on the composed line DTO, not a status-ladder rung (it can co-exist with Paid/Validated).

**Alternatives considered:** purely-derived `Closed` — rejected: no monetary derivation source exists.

---

## D4 — Evidence version history (resolves OQ-3)

**Decision:** Append-only child `EvidenceVersion` table, shaped like the `SignedUpload` collection (append new current row + mark prior superseded), **not** `DisbursementEvidence.Replace`'s in-place overwrite.

**Model:**
- `Evidence` (parent) owns `List<EvidenceVersion> _versions`; each version is an **immutable** row (private setters, no post-insert mutators) capturing: `VersionNumber` (seed 1, increments), the file pointer (`BlobKey`, `OriginalFileName`, `FileSize`, `ContentType`), the reconciliation-critical field snapshot (`Amount`, `Currency`, `DocumentReferenceNumber`, `DocumentDate`), `Reason` (required), `CreatedByUserId`, `CreatedAtUtc`, `FileHash` (SHA-256 integrity marker, FR-170), and an `IsCurrent` discriminator.
- **Exactly one current** enforced by a **filtered unique index** `UX_EvidenceVersions_OneCurrent WHERE [IsCurrent] = 1` (copying `UX_SignedUploads_OnePending_PerAgreement`).
- Replace flow (copy `FundingAgreement.ReplacePendingUpload`): mark the current version superseded (`IsCurrent = 0`), append a new current version, require a reason. Triggered by a **file replace** OR an edit to a reconciliation-critical field.
- FK to parent `Evidence` = `CASCADE`; FK to `AspNetUsers` (author) = `NO ACTION`.
- The parent `Evidence` row carries the *current* denormalized values (for query/reconciliation); the version table is the audit chain.

**File hash:** compute SHA-256 over the uploaded stream at attach/replace (no new dependency — `System.Security.Cryptography`).

**Alternatives considered:** in-place overwrite + a version counter (the `FundingAgreement.GeneratedVersion` shape) — rejected: loses the prior file/values, failing FR-042/043/AC-008.

---

## D5 — Required-document rule matrix (admin config)

**Decision:** New `DocumentRuleSet` aggregate keyed by nullable `CategoryId` (null = global default), owning `DocumentRuleItem(EvidenceType, IsRequired)` rows. Mirror `ChecklistTemplateService`'s admin CRUD + two-SaveChanges audit, but **simpler** — no per-line response snapshot table.

**Rationale for the simplification vs ChecklistTemplate:** ChecklistTemplate needs `ApplicationChecklistResponse` (snapshot + NO-ACTION FK) because responses are recorded per application and must survive template edits. Here, **completeness is computed live** (like `Item.MissingRequiredCategoryFields()`, which iterates the category's *current* field set) and **closure is a stored terminal**, so a closed line is "unaffected by later edits" simply because we never recompute it. Nothing references `DocumentRuleItem` rows, so **no snapshot/response table and no soft-deactivate is needed** — the matrix edit is a plain full-replace of items, audited via `docrule.*`.

**Model:**
- `DocumentRuleSet`: `Id`, `CategoryId int? NULL` (null = global default), `RowVersion`; `UNIQUE (CategoryId)` (one set per category; one global-default row). Owns `DocumentRuleItem`.
- `DocumentRuleItem`: `Id`, `DocumentRuleSetId`, `EvidenceType` (TINYINT `HasConversion<byte>()`), `IsRequired BIT`. `UNIQUE (DocumentRuleSetId, EvidenceType)`. FK to parent `ON DELETE CASCADE`.
- Resolution: a line's required set = `DocumentRuleSet` for its `Category.Id`, else the global-default set (`CategoryId IS NULL`), else empty (no requirements).
- Live completeness: new `Item.MissingRequiredDocuments(requiredTypes, presentTypes)` helper mirroring `MissingRequiredCategoryFields()` — pure, yields missing type labels.
- Seed a global-default set (Bank Receipt + Invoice + Signed Acceptance = Required) via a post-deploy script mirroring `07_SeedChecklistTemplates.sql`.

**Admin surface:** extend `AdminController` with `DocumentRules` / `CreateDocumentRule` / `EditDocumentRule` actions (`[Authorize(Roles="Admin")]`, antiforgery, `TempData` es-CR flashes), ViewModels + views mirroring the `Checklists`/`_ChecklistItemsEditor` set. This is the **only Admin write** in the slice (FR-028).

---

## D6 — Reconciliation legs (per-line equality chain)

**Decision:** Add a pure static `EvaluateLineEquality` sibling in `DisbursementLineReconciliation` (0.01 tolerance, `Blocking` discrepancies), driven from a service method that re-reads **fresh** sums (the P2 "R5 race lesson").

**Semantics (resolving the invoice/payment redundancy):**
- `LinePaid` = Σ validated `DisbursementLineAllocation.Amount` for the line (P2, exists).
- `LineAccepted` = Σ `EvidenceLineAllocation.Amount` for the line where `Evidence.Type == SignedAcceptance`.
- **Blocking leg (new):** `LinePaid == LineAccepted` to the colón. This is the substantive new check — the seed's paid↔acceptance equality (FR-052).
- **Invoice leg** (`paid == invoiced`) is **inherited by construction** for disbursement-anchored invoices: P1 forces disbursement ↔ invoice to the colón and P2 forces Σ split = disbursement, so per-line invoice coverage already equals `LinePaid`. No new per-line "invoiced sum" check is needed for disbursement invoices (avoids double-counting). Supplementary graph invoices, if allocated, are constrained only by evidence-allocation integrity (D2).
- **Evidence-allocation integrity** (D2): Σ evidence allocations ≤ amount (attach/edit); = amount at closure for required evidence (AC-002).

**Closure gate composition (FR-015), all re-checked against fresh reads at close time:**
1. Completeness — every required type present (D5, both sources D1).
2. Every payment attributed to the line is `Validated` (read `DisbursementLineAllocation` → `Disbursement.State`).
3. `LinePaid == LineAccepted` to the colón (this D6).
4. Each required evidence fully allocated (Σ allocations = amount).

**No warnings / severity / lifecycle** — all P3 checks are zero-tolerance blocking (FR-025); a failure returns a `Blocking` discrepancy reusing P1/P2's `ReconciliationDiscrepancy`/`LineOverpaymentDiscrepancy` display VOs. Credit Note & Refund Receipt contribute nothing to any sum (FR-026).

---

## D7 — Audit, reasons, es-CR copy

- **Audit:** extend `AdminAuditEvent` with `docrule.*` (create/edit), `evidence.*` (attached/replaced/allocated), `closure.*` (line closed/reopened) action keys + `TargetTypeDocRule/Evidence/Closure` discriminators; add matching `StartsWith` branches in `AdminAuditEventWriter.DeriveTarget` (parse `categoryId`/`evidenceId`/`itemId` from payload via the existing `ExtractIntId`). Evidence/closure writes go through `DisbursementService`'s established two-SaveChanges audit pattern.
- **Service-produced reasons** (refusals) → new `FundingPlatform.Application/Evidence/EvidenceReasons.cs` + `DocRuleReasons.cs` with nested `Codes` (the `DisbursementReasons` / spec-034 `BatchUserRowReasons` / spec-043 `RegulatoryFreshnessCopy` cross-layer precedent — services in Infrastructure must not depend on Web).
- **View/controller copy** → new `FundingPlatform.Web/Resources/EvidenceResources.cs` + `DocRuleResources.cs` (es-CR labels, evidence-type + status label/badge switch helpers, mirroring `TrancheResources`).

---

## D8 — Storage, roles, non-functionals (reuse)

- **Storage:** new `FileCategory.Evidence` (container `evidence`, 20 MiB cap, `BackendStream`) reusing the `IObjectStorage` + `ObjectKey.Build` + best-effort-blob-cleanup plumbing from P1/036; magic-byte `EvidenceFileTypePolicy` + `[UploadSizeGuard]` gate (covers FR-049/NFR-005).
- **Roles:** reuse `Financial Operator` (group-scoped writer) + Auditor/Admin read-only; `DisbursementController`/new `EvidenceController` reuse `IsAccessibleAsync` (flat 404) + `GuardWriteAsync` (`CanWrite() => IsInRole("Financial Operator")`). Executed-state gate reused. Admin-only for the matrix surface.
- **Transactions:** evidence attach/replace/allocate/close writes use the two-SaveChanges pattern (retrying execution strategy forbids explicit `BeginTransaction` — the shipping `FundService`/`DisbursementService` convention).
- **No new managed deps; additive dacpac-only** (5 new tables — `Evidence`, `EvidenceVersion`, `EvidenceLineAllocation`, `DocumentRuleSets`, `DocumentRuleItems` — + Item column adds + one seed script).

---

## Open items carried to `/speckit-tasks` / implementation

- Confirm the global-default seed set (proposed: Bank Receipt + Invoice + Signed Acceptance = Required; Credit Note / Refund Receipt / Other = Not-required).
- Decide whether `Evidence` needs its own `RowVersion` on the parent (yes — concurrency on allocation/replace) in addition to per-version rows.
- Confirm the `EvidenceIncomplete` indicator surfaces on both the disbursement `Index` line rows and the closure UI.
- Whether a supplementary graph Invoice/Bank Receipt (not tied to a disbursement) should also count toward the disbursement-level P1 gate — **no** for this slice (keep P1's gate reading only `DisbursementEvidence`); revisit if operators need it.
