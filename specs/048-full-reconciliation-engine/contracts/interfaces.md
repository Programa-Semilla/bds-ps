# Contracts: Full Reconciliation Engine (spec 048)

Interfaces live in **Application**; implementations in **Infrastructure**; controllers/views in **Web**; pure evaluators in **Domain**. Matches Clean Architecture (constitution I).

---

## Domain — pure evaluator (NEW)

```csharp
// Domain/Services/ReconciliationWarnings.cs — pure, static, deterministic (mirrors DisbursementReconciliation)
public static class ReconciliationWarnings
{
    public const decimal MinDetectableDifference = 0.01m;

    // (a) FR-010(a): evidence dated after its payment, or before agreement execution
    public static IReadOnlyList<WarningDescriptor> EvaluateEvidenceDateAnomalies(
        IReadOnlyList<EvidenceDateInput> evidence, DateOnly agreementExecutionDate);

    // (b) FR-010(b): same supplier + amount + date across non-cancelled disbursements
    public static IReadOnlyList<WarningDescriptor> EvaluatePossibleDuplicatePayments(
        IReadOnlyList<PaymentFingerprint> payments);

    // (c) FR-010(c): validated line payment vs independently-allocated graph invoice (047 FINDING-13)
    public static IReadOnlyList<WarningDescriptor> EvaluateGraphInvoiceAllocationDrift(
        IReadOnlyList<LineInvoiceDriftInput> lines);
}

// scope + comparison + amounts the materializer maps onto a Discrepancy row (Severity=Warning)
public sealed record WarningDescriptor(
    DiscrepancyScopeType ScopeType, int ScopeEntityId, ReconciliationComparison Comparison,
    decimal Expected, decimal Actual, string SourceDocument);
```

Input records (`EvidenceDateInput`, `PaymentFingerprint`, `LineInvoiceDriftInput`) carry only primitives (ids, decimals, `DateOnly`) so the evaluator stays pure. Existing `DisbursementReconciliation` / `DisbursementLineReconciliation` are **unchanged**.

---

## Application — materializer (NEW)

```csharp
// Application/Reconciliation/IReconciliationMaterializer.cs
public interface IReconciliationMaterializer
{
    // Recomputes the application's full current discrepancy set (blocking via existing evaluators
    // + warnings via ReconciliationWarnings), reconciles against persisted rows by stable identity:
    //   present -> Refresh (keep state/assignee); cleared -> AutoResolve; new -> Detect(Open).
    //   Waived/Resolved rows that recur -> AutoReopen. Appends DiscrepancyEvents; own SaveChanges.
    // Called AFTER each mutating service method's domain SaveChanges (two-SaveChanges discipline).
    // Never throws on discrepancy content — it only persists; the money gates keep their own fresh throw-path.
    Task MaterializeAsync(int applicationId, string actorUserId, CancellationToken ct);
}
```

**Callers (FR-001 trigger surface):** `DisbursementService` (Record/Edit/Validate/Cancel), `EvidenceService` (Attach/Replace/Delete/Allocate), `BudgetLineClosureService` (Close/Reopen), plus the line commit/uncommit path. Money gates (`ValidateAsync`, `CloseAsync`) still call the pure evaluators directly and throw on the fresh recompute — the materializer runs additionally, for visibility (FR-004 preserved).

---

## Application — lifecycle service (NEW)

```csharp
// Application/Reconciliation/IDiscrepancyLifecycleService.cs
public interface IDiscrepancyLifecycleService
{
    Task<DiscrepancyActionResult> AssignAsync(int discrepancyId, string assigneeUserId, string actorUserId, CancellationToken ct);
    Task<DiscrepancyActionResult> MarkUnderCorrectionAsync(int discrepancyId, string? note, string actorUserId, CancellationToken ct);
    Task<DiscrepancyActionResult> WaiveAsync(int discrepancyId, string reason, string actorUserId, CancellationToken ct); // Warning-only
}
// DiscrepancyActionResult: Ok | NotFound | Refused(DomainError). Refusals: CannotWaiveBlocking,
// ReasonRequired, Concurrency (DbUpdateConcurrencyException). Each success writes a DiscrepancyEvent
// + a discrepancy.* AdminAuditEvent (two-SaveChanges); AssignAsync also fires the best-effort email.
```

There is **no manual Resolve/Reopen** — those are materializer-only (auto). Resolution happens by fixing the numbers.

---

## Application — dashboard projection (NEW, group-scoped)

```csharp
// Application/Reconciliation/IReconciliationDashboardProjection.cs  (impl in Infrastructure/Persistence)
public interface IReconciliationDashboardProjection
{
    Task<ReconciliationSummaryDto> GetSummaryAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct);
    Task<IReadOnlyList<DiscrepancyRowDto>> GetDiscrepanciesAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct);
    Task<DiscrepancyDetailDto?> GetDetailAsync(IReviewerScope scope, int discrepancyId, CancellationToken ct); // incl. event timeline; scope-checked
}

public sealed record ReconciliationFilter(
    int? ParticipantApplicationId, int? TrancheId, int? ItemId, int? SupplierId,
    DateOnly? DateFrom, DateOnly? DateTo,
    DiscrepancySeverity? Severity, DiscrepancyState? State, string? ResponsibleUserId,
    bool OpenOnly = true); // default excludes Resolved

public sealed record ReconciliationSummaryDto(
    int OpenBlockingCount, decimal OpenBlockingAmount,
    int OpenWarningCount, decimal OpenWarningAmount,
    IReadOnlyList<ReconciliationFundRollup> ByFund);   // fund/process roll-ups (program/agency view)

public sealed record DiscrepancyRowDto(
    int Id, int ApplicationId, string ApplicationNumber /* APP-{id:D5} */, string ParticipantName,
    DiscrepancyScopeType ScopeType, string ScopeLabel, ReconciliationComparison Comparison,
    DiscrepancySeverity Severity, DiscrepancyState State,
    decimal Expected, decimal Actual, decimal Difference, string SourceDocument,
    string? TrancheName, string? LineLabel, string? SupplierName,
    string? AssigneeName, DateOnly FirstDetected);

public sealed record DiscrepancyDetailDto(DiscrepancyRowDto Row, string RequiredAction,
    IReadOnlyList<DiscrepancyEventDto> Timeline, bool CanWrite);
```

**Group-scoping (in-query):** admin short-circuit; group-overlap via `UserGroupMemberships` on `app.Applicant.UserId`; empty-group non-admin → empty; `ExcludeDeleted`/`ExcludeArchivedFund`; `MaxRows = 500`. Filter dims resolved by joining `ScopeEntityId` per `ScopeType`, then filtered in-memory (the `ParticipantBalanceProjection` build-then-filter pattern). Mirrors `EvidenceInboxProjection`.

---

## Web — controller + routes (NEW)

`ReconciliationDashboardController` — `[Authorize(Roles = "Financial Operator,Admin,Auditor")]`, `[Route("Reconciliation")]`:

| Verb | Route | Action | Auth |
|------|-------|--------|------|
| GET | `/Reconciliation` | `Index` — summary tiles + filter toolbar + list (scope-based: FinOp/Auditor group-scoped, Admin agency-wide) | any of the 3 roles |
| GET | `/Reconciliation/{id:int}` | `Detail` — one discrepancy + event timeline | scope-checked (flat 404 if out-of-scope) |
| POST | `/Reconciliation/{id:int}/Assign` | assign to a user | `CanWrite` (FinOp) — else 404-then-403 |
| POST | `/Reconciliation/{id:int}/UnderCorrection` | mark under correction | `CanWrite` |
| POST | `/Reconciliation/{id:int}/Waive` | waive (Warning only; reason required) | `CanWrite` |

**Per-discrepancy `GuardWriteAsync(id)`** (mirrors `DisbursementController`): load discrepancy → resolve `IReviewerScope` → `ApplicantSharesAnyGroupAsync(app, scope)` false → `NotFound()` (no disclosure); then `!CanWrite()` → `Forbid()` (Auditor/Admin read-only). `CanWrite() => User.IsInRole("Financial Operator")`.

**Per-application surface:** extend `Views/Disbursement/_DiscrepancyList.cshtml` to bind persisted rows (severity badge = text + icon, never color-alone, FR-025; lifecycle state; deep-link to `/Reconciliation/{id}`). Sidebar: `reconciliation` entry in `operativoEntries` for the 3 roles.

---

## Web — email (NEW, direct-send best-effort — research D6)

`DiscrepancyAssignmentEmailFactory` (mirror `InvitationEmailFactory` + `IEmailViewRenderer`): builds a branded `_EmailLayout` message to the assignee's email; `.cshtml` + `.text.cshtml` under `Views/Emails/`; sent inline in `AssignAsync` **best-effort** (log-and-continue on failure — never blocks assignment). es-CR. Allowlist applies on the send path (E2E capture via smtp4dev).

---

## Audit (extend `AdminAuditEvent`)

Add `discrepancy.*` action constants (`DiscrepancyAssigned`, `DiscrepancyUnderCorrection`, `DiscrepancyWaived`, `DiscrepancyResolved`, `DiscrepancyReopened`) + `TargetTypeDiscrepancy`; add a `discrepancy.` prefix branch to `AdminAuditEventWriter.DeriveTarget` extracting `discrepancyId`. Payloads: `{ discrepancyId, applicationId, before, after }`.

## es-CR resources
- Web: `ReconciliationResources` (view copy, tiles, filter labels, timeline labels). Extend `DisbursementResources.ComparisonLabel` for comparisons 5–7 and `SeverityLabel`/`SeverityBadge` (text+icon).
- Application: `DiscrepancyReasons` (refusal strings: cannot-waive-blocking, reason-required, concurrency).
