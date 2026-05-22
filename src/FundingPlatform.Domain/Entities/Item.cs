using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

public class Item
{
    private readonly List<Quotation> _quotations = [];

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
    public string TechnicalSpecifications { get; private set; } = string.Empty;
    public ItemReviewStatus ReviewStatus { get; private set; } = ItemReviewStatus.Pending;
    public string? ReviewComment { get; private set; }
    public int? SelectedSupplierId { get; private set; }
    public bool IsNotTechnicallyEquivalent { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Category Category { get; private set; } = null!;
    // Spec 021 / FR-005 — Impact relocated from Item to Application; no
    // per-Item Impact nav property remains. Read paths that historically
    // joined Item → Impact now route through Application.Impact (R-6).
    public Supplier? SelectedSupplier { get; private set; }

    public IReadOnlyList<Quotation> Quotations => _quotations.AsReadOnly();

    private Item() { }

    public Item(string productName, int categoryId, string technicalSpecifications)
    {
        ProductName = productName;
        CategoryId = categoryId;
        TechnicalSpecifications = technicalSpecifications;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the item's product name, category, and technical specifications.
    /// </summary>
    public void Update(string productName, int categoryId, string technicalSpecifications)
    {
        ProductName = productName;
        CategoryId = categoryId;
        TechnicalSpecifications = technicalSpecifications;
        UpdatedAt = DateTime.UtcNow;
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
        string currency)
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

        var quotation = new Quotation(supplier.Id, branch.Id, document.Id, price, validUntil, currency);
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

        if (!_quotations.Any(q => q.SupplierId == supplierId))
        {
            throw new InvalidOperationException(
                "Selected supplier must have a quotation on this item.");
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
        ReviewComment = "Rejected: quotations are not technically equivalent";
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
    /// Spec 018 — stable discriminator key on <see cref="ArgumentException.Data"/> for
    /// the application-layer exception-mapping path to read instead of brittle
    /// message-string matching. Co-located with the entity so renaming the
    /// validation messages does not silently break the user-facing error mapping.
    /// </summary>
    public const string ValidationReasonKey = "FundingPlatform.ValidationReason";
    public const string LineCodeRequiredReason = "LineCodeRequired";
    public const string LineCodeTooLongReason = "LineCodeTooLong";
}
