# Phase 0 Research: Centralized Supplier Catalog

**Date:** 2026-04-30
**Spec:** [spec.md](./spec.md)
**Plan:** [plan.md](./plan.md)

The spec was already pinned through two iterations of `speckit-spex-gates-review-spec`, so the open questions reaching this research phase are technical (HOW) rather than scope (WHAT). The Technical Context block has zero `NEEDS CLARIFICATION` markers; this document records decisions for the technical questions that emerged while drafting `plan.md`.

---

## R1 — Quotation Uniqueness Constraint Under the Branch Model

### Decision

Keep the existing `UX_Quotations_ItemId_SupplierId UNIQUE (ItemId, SupplierId)` constraint after migration. **One quotation per (item, supplier)**, regardless of branch. Branch is contact metadata, not a separate quote source.

### Rationale

The current rule (one quotation per supplier per item) reflects a real business invariant: an applicant should not be allowed to add two competing quotations against the same supplier on the same item, because the item-level recommendation algorithm (spec 003) would treat them as two separate suppliers and double-count price/compliance points. Branches don't change that invariant — a supplier with two offices is still one supplier from a procurement standpoint, and the applicant should pick one office and submit one quotation.

If the rule were relaxed to `(ItemId, SupplierBranchId)`, a malicious or confused applicant could add Branch A's quote AND Branch B's quote on the same item from the same supplier, gaming the recommendation by showing two compliance-1 entries. The current rule blocks this naturally.

### Alternatives considered

- **Tighten to `(ItemId, SupplierBranchId)`**: rejected — see above.
- **Drop the constraint entirely, rely on UI**: rejected — controllers run in concurrent contexts; database-level enforcement is the only safe place.

### Implementation note

The dacpac script keeps the existing unique constraint untouched. No DDL change for this row.

---

## R2 — Repository Surface for Branches

### Decision

Expose branch operations through the `Supplier` aggregate root and `ISupplierRepository` only. **Do not introduce a separate `ISupplierBranchRepository`.** All branch CRUD goes through `Supplier.AddBranch(...)`, `Supplier.EditBranch(...)`, etc., persisted via the existing `_supplierRepository.UpdateAsync(supplier)`.

### Rationale

`SupplierBranch` is conceptually part of the `Supplier` aggregate. Constitution Principle II (Rich Domain Model) explicitly forbids exposing raw state for external manipulation; branch CRUD that bypasses the parent aggregate would let a controller create orphaned branches or violate the "exactly one default" invariant from outside the entity. Routing every write through the aggregate root naturally enforces invariants in one place. The query path (e.g., `GetByLegalIdWithBranchesAsync`) already uses `Include(s => s.Branches)`; no separate repo needed.

### Alternatives considered

- **Separate `ISupplierBranchRepository`**: rejected — leaks aggregate boundaries; requires duplicated invariant enforcement.
- **Direct EF DbContext access from controllers for branches**: rejected — violates Clean Architecture (Web → Infrastructure direct access bypasses Application).

### Implementation note

`ISupplierRepository` gains:

- `Task<Supplier?> GetByLegalIdWithBranchesAsync(string legalId)` (used for lookup + load-for-update)
- `Task<Supplier?> GetByIdWithBranchesAsync(int id)` (used for admin detail)
- `Task<List<Supplier>> ListForAdminAsync(SupplierAdminFilter filter)` (status / legalId / name / has-incomplete-compliance)
- `Task<int> CountReferencingApplicationsAsync(int supplierId)` (admin detail "currently referenced by N applications")

Existing `GetByLegalIdAsync` is kept for backwards-compat callers (e.g., `ReviewService` only needs the supplier scalar, not branches), then phased out as call sites migrate.

---

## R3 — Migration Mechanics under dacpac (Constitution IV)

### Decision

Implement the migration as a single idempotent `PostDeployment/Migrations/013_SupplierCatalog.sql` script invoked from the existing `SeedData.sql`. Wrap the entire script in a `BEGIN TRY ... BEGIN TRANSACTION ... COMMIT ... END TRY BEGIN CATCH ... ROLLBACK ... END CATCH` block with explicit assertion checks (`THROW 50001, 'msg', 1` on inconsistency). Execute exactly once per dacpac deploy by guarding the body with a sentinel check on `Suppliers` schema state (e.g., `IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Suppliers') AND name = 'ContactName')` — i.e., only run while the legacy columns still exist).

### Rationale

Constitution Principle IV mandates dacpac is the single source of truth and prohibits EF Core migrations and `EnsureCreated`. The dacpac model itself is declarative — adding `dbo.SupplierBranches.sql` and editing `dbo.Suppliers.sql` updates the schema target, but a deploy will run on the **target** that already has data (existing Suppliers rows + Quotations referencing them). The dacpac deploy engine handles column-add and table-add automatically, but it cannot:

1. Backfill new columns with status `Verified` and a sentinel verifier user ID.
2. Create one `SupplierBranches` row per existing `Suppliers` row.
3. Repoint `Quotations.SupplierBranchId`.
4. Drop the migrated columns from `Suppliers` (which dacpac would attempt as DROP COLUMN; we want it to happen AFTER the post-deploy script has read those columns).

The deploy engine therefore needs a sequence: **(a) ALTER TABLE adds + new SupplierBranches table** declared by dacpac → **(b) post-deploy script reads legacy columns, populates new ones, sets Quotations FKs, asserts** → **(c) declarative DROP of legacy columns** (dacpac picks this up in the same deploy because the model says they're gone).

The standard dacpac mechanism for this is the **"Generate smart-defaults" plus split deploy via `/p:PreserveData=true`** but the simpler approach the codebase already uses (per spec 010's currency rollout) is: keep the legacy columns in `dbo.Suppliers.sql` for ONE deploy carrying the post-deploy backfill, then a second deploy drops them. To avoid two deploys, we use SQL Server's deploy-engine ordering: pre-deploy script handles rename/save-then-drop, post-deploy backfills new columns and drops the saved temp.

After reviewing the spec 010 currency-rollout migration (the closest precedent — column add + backfill + NOT NULL tightening), **we adopt the spec 010 pattern exactly**: declarative dacpac changes ship the new shape; the post-deploy script does the backfill and assertion in one transaction; legacy columns stay in `dbo.Suppliers.sql` as nullable for one release with a TODO comment, then are dropped in a follow-up cleanup PR. This is the minimum-risk path and matches an established team pattern.

### Alternatives considered

- **Two-phase deploy (deploy 1 = add new shape, deploy 2 = drop legacy)**: rejected — operational overhead doubles the production-deploy ceremony for no benefit on a small dataset. See R4 for risk assessment.
- **Pre-deploy script doing the save-then-drop dance**: rejected — pre-deploy runs before dacpac's column-drops, which means the dacpac would still try to drop columns the post-deploy needs to read; ordering is fragile.
- **EF Core migration**: rejected — Constitution IV violation.

### Implementation note

The migration script's outline (full SQL in `data-model.md`):

```sql
-- 013_SupplierCatalog.sql, idempotent
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Suppliers') AND name = 'ContactName')
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Backfill VerificationStatus / VerifiedByUserId / VerifiedAt on Suppliers
        UPDATE dbo.Suppliers SET ...;

        -- 2. INSERT one default SupplierBranches row per Suppliers row
        INSERT INTO dbo.SupplierBranches ...;

        -- 3. Backfill Quotations.SupplierBranchId via JOIN
        UPDATE dbo.Quotations SET SupplierBranchId = ...;

        -- 4. Assertions (THROW on failure)
        IF EXISTS (SELECT 1 FROM dbo.Quotations WHERE SupplierBranchId IS NULL) THROW 50001, '...';
        IF EXISTS (SELECT 1 FROM dbo.Quotations q JOIN dbo.SupplierBranches b ON q.SupplierBranchId = b.Id WHERE q.SupplierId <> b.SupplierId) THROW 50002, '...';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        ;THROW;
    END CATCH;
END;
```

Legacy columns (`ContactName`, `Email`, `Phone`, `Location`, `ShippingDetails`, `WarrantyInfo`) stay declared in `dbo.Suppliers.sql` for this release with a `-- TODO[013-cleanup]: drop after one release` comment; a follow-up PR removes them once production telemetry confirms the migration ran cleanly.

---

## R4 — Concurrent Insert Recovery When Two Applicants Type the Same New Legal ID

### Decision

Catch `SqlException` with error number `2627` (unique constraint violation) inside `ApplicationService.AddSupplierQuotationAsync`. On catch, re-run the lookup; the supplier now exists. If the applicant had filled in the new-supplier form, redirect them to the existing-supplier branch picker with a localized message: "Este proveedor acaba de ser registrado por otro postulante. Selecciona una sucursal o agrega una nueva." Do NOT auto-merge their typed branch data; the applicant explicitly re-decides between picking an existing branch or adding theirs.

### Rationale

The spec edge case "Concurrent creation of the same legal ID" calls out the unique-constraint serialization but doesn't specify the recovery UX. Auto-creating a branch on the now-existing supplier risks polluting the catalog with a branch the applicant might not actually want (their original input was *attempting* to create the supplier, but their branch data may have been a placeholder). Asking the applicant to re-decide is the safest cooperative behavior.

### Alternatives considered

- **Optimistic concurrency token on `Suppliers.LegalId` lookup → 409 Conflict**: rejected — the unique constraint already provides the same protection; layering optimistic concurrency adds machinery without benefit.
- **Auto-create the applicant's typed branch under the existing supplier**: rejected — creates a UX where the applicant types data and never sees what happened.

### Implementation note

`SupplierCatalogService.CreateDraftWithBranchAsync(...)` wraps the `Supplier`-creation path. On `SqlException(Number == 2627)`, it returns a `Result.RetryWithExisting(int existingSupplierId)` discriminated result; the controller maps that to a 303 redirect to `Add` with `?supplierId=...&banner=concurrent` query params.

---

## R5 — `SupplierScore.ComputeForItem` Signature Migration

### Decision

Change the static method signature from `List<(Quotation, Supplier)>` to `List<(Quotation, Supplier, SupplierBranch)>`. Callers in `ReviewService` are updated to project the branch alongside the supplier when materializing review-screen data. The two new flags (`IsSupplierVerified`, `IsSupplierRejected`) are derived inside `ComputeForItem` from `Supplier.VerificationStatus`. The `IsRecommended` rule becomes `Total == maxScore && !IsSupplierRejected`.

### Rationale

Three options were considered:

- **Pass the full `Supplier` aggregate (already loaded with branches) and let the score compute branch info on its own**: this is what the current code does for compliance fields, but it forces the score to know which branch the quotation references. Quotations carry that linkage via `SupplierBranchId`, so the score would need either the navigation loaded or a separate lookup. Either way, branch knowledge leaks into the value object's responsibilities.
- **Pass `(Quotation, Supplier, SupplierBranch)` triples — the chosen approach**: keeps `SupplierScore` purely a math function, lets the caller (which is the only place that knows whether to load branches eagerly or lazily) handle the materialization. The branch is read by reviewer-UI consumers of the score result, not by the math.
- **Add a `BranchContact` field to the `SupplierScore` record**: rejected — bloats the value object with display-only data; reviewer UI should read the branch directly, not via the score.

### Implementation note

`SupplierScore` record record gains:

```csharp
public record SupplierScore(
    int Total,
    bool IsCompliantCCSS,
    bool IsCompliantHacienda,
    bool IsCompliantSICOP,
    bool HasElectronicInvoice,
    bool HasLowestPrice,
    bool IsRecommended,
    bool IsPreSelected,
    bool IsSupplierVerified,    // NEW
    bool IsSupplierRejected);   // NEW
```

`ComputeForItem` math is unchanged for the four compliance/e-invoice factors and the price factor. Pre-selection rule is unchanged. `IsRecommended` masks `Rejected` suppliers as described.

The branch passed in is currently used only by reviewer UI rendering (contact display next to the score row) and is not consumed by the score math; documenting this explicitly in the value object's xmldoc avoids implementer confusion.

---

## R6 — Permission Enforcement Layer

### Decision

Enforce the spec's permission matrix (FR-070) at the **controller layer** via attribute-based authorization (`[Authorize(Roles = "...")]`) plus explicit method-level ownership checks (`VerifyOwnershipAsync` patterns already used by `SupplierController` and `ApplicationController`). Domain-layer guards are also added on lifecycle methods (e.g., `Supplier.SubmitForReview` is no-op when status is not `Draft`; `Supplier.Verify` throws if status is `Verified`).

### Rationale

The permission matrix has both role-level concerns (Admin vs. Applicant vs. Reviewer) and ownership concerns (which Applicant created which Draft). Role-level rules are cleanly handled by `[Authorize(Roles = "Admin")]` at the `AdminSuppliersController` class level. Ownership concerns are richer (depends on `Supplier.CreatedByApplicantId == currentApplicantId AND parentApplication.Status == Draft`); these are already handled in the codebase by per-action helpers, so the existing pattern is reused.

Domain guards on lifecycle methods are not strictly necessary for security (controllers gate it), but they're cheap and make unit tests cleaner — the entity refuses illegal transitions even if controller bugs let them through.

### Alternatives considered

- **Custom authorization policies (`IAuthorizationRequirement`)**: rejected — overkill for this codebase's existing patterns; spec 009 (admin area) didn't introduce them and we don't need the abstraction yet.
- **EF query filters for "applicant only sees own drafts"**: rejected — query filters are too implicit for the readability bar this team has set; explicit `WHERE` clauses in repository methods are clearer.

---

## R7 — Localization Strategy for New User-Facing Strings

### Decision

All new applicant-facing and admin-facing strings ship localized to es-CR per spec 012's existing `.resx` infrastructure (`SharedResource`, `Suppliers.resx`, `AdminSuppliers.resx`). Hard-code the migrated default branch name as `"Sede principal"` directly in the `013_SupplierCatalog.sql` script (this is a one-time historical record, not a live UI string).

### Rationale

Spec 012 established the localization contract (`IStringLocalizer<SharedResource>` for shared strings, per-feature resx files for feature-specific strings). Following that pattern is the only sensible choice — anything else creates inconsistency.

The migrated default branch label is a special case: it's persisted data, not a runtime-rendered string. If the platform later localizes to another language, those rows will stay Spanish unless an admin renames them. This is acceptable per the spec 012 scope (es-CR is the only locale in v1) and is documented in the spec's open threads (Q3 in `review_brief.md`).

### Implementation note

Two new resx files: `src/FundingPlatform.Web/Resources/Suppliers.resx` (applicant-facing) and `src/FundingPlatform.Web/Resources/AdminSuppliers.resx` (admin-facing). Wire them up in `Program.cs` via the existing localization-options block from spec 012.

---

## R8 — E2E Test Strategy for the Migration

### Decision

Add a dedicated `MigrationTests.cs` integration test (NOT E2E) that:

1. Spins up the AppHost with ephemeral SQL.
2. Seeds the OLD schema (legacy columns populated) directly via raw SQL — bypassing the dacpac on test startup.
3. Runs the migration script.
4. Asserts: (a) every `Suppliers` row has `VerificationStatus = Verified` + sentinel verifier; (b) every supplier has exactly one default branch carrying its prior contact data; (c) every quotation has a non-null `SupplierBranchId`; (d) the spec 003 `SupplierScore` math returns identical results for every existing application before-and-after migration (this is the SC-003 byte-for-byte parity check).

E2E (Playwright) tests cover the post-migration flows only.

### Rationale

The migration is one-shot SQL; testing it through Playwright would require staging an old-schema browser session, which doesn't exist. An integration test that operates at the SQL + EF Core layer is the right tool.

The byte-for-byte parity test (SC-003) is the most valuable assertion in the migration test suite — it catches any subtle change to score math caused by the signature update.

### Alternatives considered

- **Test via the dacpac DeployScript pipeline directly**: rejected — slower (full schema deploy), harder to seed a known "old" state.
- **Skip the migration test, rely on staging dry run**: rejected — staging dry run is a manual operational gate, not a regression-protected CI signal.

---

## R9 — Validation of NFR-004 (Lookup Debounce + Rate Limit)

### Decision

Client-side debounce (250 ms) implemented in the `Add.cshtml` page's vanilla JS (`PlatformMotion.debounce` helper from spec 011, or a new 5-line equivalent if that helper isn't reusable). Server-side rate limit handled by the existing global IP rate limiter middleware introduced by spec 008 — no new middleware needed. We verify the existing rate limit covers `/Application/{appId}/Item/{itemId}/Supplier/Search` by inspecting the rate limit options in `Program.cs`.

### Rationale

The spec set the budget low (250 ms client debounce) so even unauthenticated abuse is throttled at the global IP rate limit. Adding a per-route rate limit would be premature optimization.

### Alternatives considered

- **Per-route rate limiter**: deferred — only revisit if telemetry shows the lookup endpoint is being hammered.

---

## Summary

All eight technical questions resolved. Decisions are consistent with the constitution and existing codebase patterns (spec 008 for rate limiting, spec 009 for admin area layout, spec 010 for dacpac migration ordering, spec 011 for JS conventions, spec 012 for localization). Phase 1 (data-model.md, contracts/, quickstart.md) can proceed without further blocking questions.
