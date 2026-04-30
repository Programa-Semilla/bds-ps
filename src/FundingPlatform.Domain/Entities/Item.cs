using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

public class Item
{
    private readonly List<Quotation> _quotations = [];

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
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
    public Impact? Impact { get; private set; }
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
    /// Sets the impact assessment for this item using the specified template and parameter values.
    /// </summary>
    public void SetImpact(ImpactTemplate template, List<ImpactParameterValue> parameterValues)
    {
        Impact = new Impact(template.Id, parameterValues);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Determines whether this item has at least the specified minimum number of quotations.
    /// </summary>
    public bool HasMinimumQuotations(int min)
    {
        return _quotations.Count >= min;
    }

    /// <summary>
    /// Determines whether this item has a complete impact assessment with parameter values.
    /// </summary>
    public bool HasCompleteImpact()
    {
        return Impact is not null && Impact.ParameterValues.Count > 0;
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
}
