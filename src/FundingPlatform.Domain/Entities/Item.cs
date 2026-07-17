using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

public class Item
{
    /// <summary>Spec 035 (evolved 2026-06-16, FR-008) — hard cap on the per-item justification.</summary>
    public const int ImpactJustificationMaxLength = 300;

    private readonly List<Quotation> _quotations = [];
    private readonly List<ItemImpact> _itemImpacts = [];
    private readonly List<CategoryFieldValue> _categoryFieldValues = [];

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    /// <summary>
    /// Spec 018 / FR-012 / FR-013 / FR-014 — reviewer-assigned line code (e.g. "T1-1").
    /// Nullable until a reviewer assigns it; ≤16 chars after trim; per-Application
    /// uniqueness enforced at the aggregate root via <see cref="Application.AssignLineCodeToItem"/>.
    /// Mutated only via <see cref="AssignLineCode"/>, which is internal so the
    /// aggregate root is the single entry point.
    /// </summary>
    public string? LineCode { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public int CategoryId { get; private set; }
    /// <summary>
    /// Spec 035 (evolved 2026-06-16, FR-008) — a single short explanation of why this
    /// line item supports its attributed impact(s). Required (non-empty) at submit;
    /// ≤<see cref="ImpactJustificationMaxLength"/> chars. Set via
    /// <see cref="SetImpactJustification"/>.
    /// </summary>
    public string? ImpactJustification { get; private set; }
    public ItemReviewStatus ReviewStatus { get; private set; } = ItemReviewStatus.Pending;
    public string? ReviewComment { get; private set; }
    public int? SelectedSupplierId { get; private set; }
    public bool IsNotTechnicallyEquivalent { get; private set; }

    /// <summary>
    /// Spec 046 — the tranche (funding phase) this budget-line belongs to, or <c>null</c>
    /// when it falls into the application's virtual default ("General") tranche. Assigned by
    /// the reviewer pre-execution via <see cref="Application.AssignItemToTranche"/>; mutated
    /// only through <see cref="AssignTranche"/> so the aggregate root is the single entry point.
    /// </summary>
    public int? TrancheId { get; private set; }

    /// <summary>
    /// Spec 046 / FR-009 (research D1/D2) — the Financial Operator's off-ledger commit status.
    /// Default <see cref="ItemCommitState.Uncommitted"/>. A line must be committed before a
    /// payment can be attributed to it; reversible until the first payment lands (the "no
    /// recorded payment" guard is enforced by the disbursement service, which can see
    /// attributions, not by this entity).
    /// </summary>
    public ItemCommitState CommitState { get; private set; } = ItemCommitState.Uncommitted;

    /// <summary>
    /// Spec 047 / FR-015 (research D3) — the Financial Operator's off-ledger closure status.
    /// Default <see cref="ItemClosureState.Open"/>. A line is <see cref="ItemClosureState.Closed"/>
    /// only when its closure gate is satisfied; closing writes no ledger entry (off-ledger, FR-018)
    /// and is reversible with a reason. The gate ("required docs + payments validated + equality
    /// chain + fully allocated") is enforced by the closure service, which can see attributions/
    /// evidence; the entity cannot (the <see cref="Item"/> convention, mirrors <see cref="Commit"/>).
    /// </summary>
    public ItemClosureState ClosureState { get; private set; } = ItemClosureState.Open;
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosureReason { get; private set; }
    public string? ReopenReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Category Category { get; private set; } = null!;
    public Supplier? SelectedSupplier { get; private set; }

    public IReadOnlyList<Quotation> Quotations => _quotations.AsReadOnly();

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D14) — the application impacts this line item is
    /// attributed to (one or more). The attribution targets must be among the
    /// application's declared impacts (enforced by <see cref="Application.Validate"/>).
    /// </summary>
    public IReadOnlyList<ItemImpact> ItemImpacts => _itemImpacts.AsReadOnly();

    /// <summary>Spec 035 / D1 — per-item category field values (EAV).</summary>
    public IReadOnlyList<CategoryFieldValue> CategoryFieldValues => _categoryFieldValues.AsReadOnly();

    private Item() { }

    public Item(string productName, int categoryId)
    {
        ProductName = productName;
        CategoryId = categoryId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the item's product name and category. When the category changes,
    /// the previous category's field values are discarded (see <see cref="ChangeCategory"/>).
    /// </summary>
    public void Update(string productName, int categoryId)
    {
        ProductName = productName;
        ChangeCategory(categoryId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 035 / D11 — sets the item's category, clearing any category field
    /// values captured under the previous category (they no longer apply). No-op
    /// when the category is unchanged.
    /// </summary>
    public void ChangeCategory(int newCategoryId)
    {
        if (CategoryId != newCategoryId)
        {
            CategoryId = newCategoryId;
            _categoryFieldValues.Clear();
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D14) — replaces the item's impact attribution: the
    /// set of application impacts this line supports. Replace-all semantics; duplicates
    /// in the input are collapsed. The caller is responsible for ensuring each id belongs
    /// to the application's declared impacts (validated at submit by
    /// <see cref="Application.Validate"/>).
    /// </summary>
    public void AttributeImpacts(IEnumerable<int> applicationImpactIds)
    {
        ArgumentNullException.ThrowIfNull(applicationImpactIds);

        _itemImpacts.Clear();
        foreach (var id in applicationImpactIds.Distinct())
        {
            _itemImpacts.Add(new ItemImpact(id));
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, FR-008) — sets the short impact justification.
    /// Trims; stores null when blank. Enforces the
    /// <see cref="ImpactJustificationMaxLength"/> hard cap.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the trimmed text exceeds the cap.</exception>
    public void SetImpactJustification(string? justification)
    {
        var trimmed = justification?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > ImpactJustificationMaxLength)
        {
            throw new ArgumentException(
                $"Impact justification must be {ImpactJustificationMaxLength} characters or fewer.",
                nameof(justification));
        }

        ImpactJustification = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D14) — internal mutation used by the aggregate root
    /// (<see cref="Application.RemoveImpact"/>) to drop attributions to a declared impact
    /// that is being removed (the DB FK is NO ACTION, so the domain does the cleanup).
    /// </summary>
    internal void RemoveAttribution(int applicationImpactId)
    {
        var removed = _itemImpacts.RemoveAll(ii => ii.ApplicationImpactId == applicationImpactId);
        if (removed > 0)
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Spec 035 / D1 — replaces the item's category field values (replace-all,
    /// mirroring <see cref="SetImpact"/>).
    /// </summary>
    public void SetCategoryFieldValues(IEnumerable<CategoryFieldValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _categoryFieldValues.Clear();
        _categoryFieldValues.AddRange(values);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 035 / SC-006 — display labels of the category's required fields whose
    /// value is blank or absent, used by <see cref="Application.Validate"/> to gate
    /// submission. Iterates the category's CURRENT field set (so a field an admin
    /// adds after the item was saved is caught), cross-referenced against the
    /// item's stored values. No-op when the <see cref="Category"/> nav (with its
    /// Fields) is not loaded — the caller must Include it for the gate to fire.
    /// </summary>
    public IEnumerable<string> MissingRequiredCategoryFields()
    {
        if (Category is null)
        {
            yield break;
        }

        foreach (var field in Category.Fields.Where(f => f.IsRequired))
        {
            var value = _categoryFieldValues.FirstOrDefault(v => v.CategoryFieldId == field.Id)?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return field.DisplayLabel;
            }
        }
    }

    /// <summary>
    /// Adds a quotation for the specified supplier branch. Prevents duplicate
    /// suppliers on the same item per the (item, supplier) UNIQUE constraint
    /// (research.md R1 — branches do not split a supplier into multiple quote
    /// sources). The branch must belong to the supplier; caller asserts via
    /// `branch.SupplierId == supplier.Id` before invoking.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the supplier already has a quotation on this item OR the
    /// branch belongs to a different supplier.
    /// </exception>
    public void AddQuotation(
        Supplier supplier,
        SupplierBranch branch,
        Document document,
        decimal price,
        DateOnly validUntil,
        string currency,
        TimeDuration deliveryLeadTime,
        TimeDuration warranty)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(document);

        if (branch.SupplierId != 0 && branch.SupplierId != supplier.Id)
        {
            throw new InvalidOperationException(
                $"Branch {branch.Id} (supplier {branch.SupplierId}) does not belong to supplier {supplier.Id}.");
        }

        if (_quotations.Any(q => q.SupplierId == supplier.Id))
        {
            throw new InvalidOperationException(
                $"Supplier '{supplier.Name}' already has a quotation on this item.");
        }

        var quotation = new Quotation(
            supplier.Id, branch.Id, document.Id, price, validUntil, currency,
            deliveryLeadTime, warranty);
        _quotations.Add(quotation);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 015 / US1 — attaches a pre-constructed <see cref="Quotation"/> to this Item,
    /// preserving the same (item, supplier) UNIQUE invariant as <see cref="AddQuotation"/>.
    /// Used by the application layer when the quotation was built via
    /// <see cref="Quotation.SetCurrencyAndAmountAsync"/> so that the snapshot
    /// fields are populated before the entity enters the aggregate.
    /// </summary>
    public void AttachQuotation(Supplier supplier, SupplierBranch branch, Quotation quotation)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(quotation);

        if (branch.SupplierId != 0 && branch.SupplierId != supplier.Id)
        {
            throw new InvalidOperationException(
                $"Branch {branch.Id} (supplier {branch.SupplierId}) does not belong to supplier {supplier.Id}.");
        }

        if (quotation.SupplierId != supplier.Id)
        {
            throw new InvalidOperationException(
                $"Quotation supplier {quotation.SupplierId} does not match supplier {supplier.Id}.");
        }

        if (_quotations.Any(q => q.SupplierId == supplier.Id))
        {
            throw new InvalidOperationException(
                $"Supplier '{supplier.Name}' already has a quotation on this item.");
        }

        _quotations.Add(quotation);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes a quotation from this item by its identifier.
    /// </summary>
    public void RemoveQuotation(int quotationId)
    {
        var quotation = _quotations.FirstOrDefault(q => q.Id == quotationId);
        if (quotation is not null)
        {
            _quotations.Remove(quotation);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Determines whether this item has at least the specified minimum number of quotations.
    /// </summary>
    public bool HasMinimumQuotations(int min)
    {
        return _quotations.Count >= min;
    }

    /// <summary>
    /// Approves the item with a selected supplier and optional comment.
    /// </summary>
    public void Approve(int supplierId, string? comment)
    {
        if (IsNotTechnicallyEquivalent)
        {
            throw new InvalidOperationException(
                "Cannot approve an item flagged as not technically equivalent.");
        }

        var selectedQuotation = _quotations.FirstOrDefault(q => q.SupplierId == supplierId);
        if (selectedQuotation is null)
        {
            throw new InvalidOperationException(
                "Selected supplier must have a quotation on this item.");
        }

        // Spec 039 / FR-019 — a provider with CCSS sin inscripción is a hard block:
        // the reviewer may select it, but the item cannot be approved with it. null
        // CCSS (sin revisar) is NOT a block (research D4). Guarded in the domain so
        // the invariant is un-bypassable (Constitution II). The gate requires the
        // Supplier nav to be loaded; the production review flow eager-loads it via
        // IApplicationRepository.GetByIdWithDetailsAsync (Items→Quotations→Supplier),
        // and ReviewService re-checks at finalize time (FR-019 defence-in-depth).
        if (selectedQuotation.Supplier?.CcssStatus == CcssStatus.SinInscripcion)
        {
            throw new Exceptions.SupplierIneligibleException(
                selectedQuotation.Supplier.Name);
        }

        ReviewStatus = ItemReviewStatus.Approved;
        SelectedSupplierId = supplierId;
        ReviewComment = comment;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Rejects the item with an optional comment.
    /// </summary>
    public void Reject(string? comment)
    {
        ReviewStatus = ItemReviewStatus.Rejected;
        ReviewComment = comment;
        SelectedSupplierId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Requests more information on the item with an optional comment.
    /// </summary>
    public void RequestMoreInfo(string? comment)
    {
        ReviewStatus = ItemReviewStatus.NeedsInfo;
        ReviewComment = comment;
        SelectedSupplierId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Flags this item's quotations as not technically equivalent, automatically rejecting it.
    /// </summary>
    public void FlagNotEquivalent()
    {
        IsNotTechnicallyEquivalent = true;
        ReviewStatus = ItemReviewStatus.Rejected;
        // The not-equivalent state is carried by IsNotTechnicallyEquivalent; views
        // render the localized message from that flag. Do NOT persist an English
        // sentence here — it leaked onto the applicant Details page (es-CR).
        ReviewComment = null;
        SelectedSupplierId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the not-technically-equivalent flag and resets the review status to Pending.
    /// </summary>
    public void ClearNotEquivalentFlag()
    {
        IsNotTechnicallyEquivalent = false;
        ReviewStatus = ItemReviewStatus.Pending;
        ReviewComment = null;
        SelectedSupplierId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Resets review status to Pending for a new review round. Preserves ReviewComment.
    /// </summary>
    public void ResetReviewStatus()
    {
        ReviewStatus = ItemReviewStatus.Pending;
        SelectedSupplierId = null;
        IsNotTechnicallyEquivalent = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 018 / FR-012 / FR-013 / FR-014 — writes the reviewer-supplied line code
    /// to this item. Trims whitespace, rejects null/empty/whitespace-only input, and
    /// enforces a 16-character maximum after trim. Marked <c>internal</c> so only the
    /// aggregate root (<see cref="Application.AssignLineCodeToItem"/>) can call it,
    /// which lets the aggregate enforce per-Application uniqueness in the same call.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="lineCode"/> is null/whitespace or exceeds 16 chars after trim.
    /// </exception>
    internal void AssignLineCode(string lineCode)
    {
        if (lineCode is null)
        {
            var ex = new ArgumentException("Line code is required.", nameof(lineCode));
            ex.Data[ValidationReasonKey] = LineCodeRequiredReason;
            throw ex;
        }
        var trimmed = lineCode.Trim();
        if (trimmed.Length == 0)
        {
            var ex = new ArgumentException("Line code is required.", nameof(lineCode));
            ex.Data[ValidationReasonKey] = LineCodeRequiredReason;
            throw ex;
        }
        if (trimmed.Length > 16)
        {
            var ex = new ArgumentException("Line code must be 16 characters or fewer.", nameof(lineCode));
            ex.Data[ValidationReasonKey] = LineCodeTooLongReason;
            throw ex;
        }

        LineCode = trimmed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 046 — sets (or clears, when <paramref name="trancheId"/> is null) the budget-line's
    /// tranche membership. <c>internal</c> so the aggregate root
    /// (<see cref="Application.AssignItemToTranche"/>) is the single entry point, which validates
    /// the tranche belongs to the same application and enforces the execution freeze.
    /// </summary>
    internal void AssignTranche(int? trancheId)
    {
        TrancheId = trancheId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 046 / FR-009 — obligates the budget-line (Uncommitted → Committed). Idempotent: a
    /// repeat on an already-committed line is a no-op. Commit is post-execution and operator-owned,
    /// driven by the disbursement service (which owns the "no payment" un-commit guard), so it is
    /// <c>internal</c> rather than aggregate-root-frozen.
    /// </summary>
    internal void Commit()
    {
        if (CommitState == ItemCommitState.Committed)
        {
            return;
        }
        CommitState = ItemCommitState.Committed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 046 / FR-007 — reverses a commitment (→ Uncommitted). The "no recorded payment" guard
    /// is enforced by the service (it queries <see cref="DisbursementLineAllocation"/>); the entity
    /// cannot see attributions. Idempotent.
    /// </summary>
    internal void Uncommit()
    {
        if (CommitState == ItemCommitState.Uncommitted)
        {
            return;
        }
        CommitState = ItemCommitState.Uncommitted;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 047 / FR-016 — closes the budget-line (Open → Closed), stamping the actor + timestamp and
    /// an optional note (mirrors <see cref="Commit"/> + <c>Disbursement.Validate</c> stamping).
    /// Idempotent: a repeat on an already-closed line is a no-op. The gate is enforced by the closure
    /// service (it can see attributions/evidence); this method only records the decision. Clears any
    /// prior reopen note.
    /// </summary>
    internal void Close(string userId, string? reason)
    {
        if (ClosureState == ItemClosureState.Closed)
        {
            return;
        }
        ClosureState = ItemClosureState.Closed;
        ClosedByUserId = userId;
        ClosedAtUtc = DateTime.UtcNow;
        ClosureReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ReopenReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 047 / FR-017 — reopens a closed budget-line (Closed → Open) with a required reason,
    /// clearing the closed-by/at stamp. Off-ledger — no balance change. Idempotent on an already-open
    /// line.
    /// </summary>
    internal void Reopen(string userId, string reason)
    {
        if (ClosureState == ItemClosureState.Open)
        {
            return;
        }
        ClosureState = ItemClosureState.Open;
        ClosedByUserId = null;
        ClosedAtUtc = null;
        ClosureReason = null;
        ReopenReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 018 — stable discriminator key on <see cref="ArgumentException.Data"/> for
    /// the application-layer exception-mapping path to read instead of brittle
    /// message-string matching. Co-located with the entity so renaming the
    /// validation messages does not silently break the user-facing error mapping.
    /// </summary>
    public const string ValidationReasonKey = "FundingPlatform.ValidationReason";
    public const string LineCodeRequiredReason = "LineCodeRequired";
    public const string LineCodeTooLongReason = "LineCodeTooLong";
}
