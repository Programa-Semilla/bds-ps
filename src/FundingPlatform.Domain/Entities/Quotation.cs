namespace FundingPlatform.Domain.Entities;

public class Quotation
{
    public int Id { get; private set; }
    public int ItemId { get; private set; }
    public int SupplierId { get; private set; }

    /// <summary>
    /// Spec 013: branch reference. Invariant (enforced at the application layer):
    /// SupplierBranch.SupplierId == this.SupplierId. The aggregate that loads the
    /// branch writes both fields atomically from the same source.
    /// </summary>
    public int SupplierBranchId { get; private set; }

    public decimal Price { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public int DocumentId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    public Supplier Supplier { get; private set; } = null!;
    public SupplierBranch SupplierBranch { get; private set; } = null!;
    public Document Document { get; private set; } = null!;

    private Quotation() { }

    public Quotation(
        int supplierId,
        int supplierBranchId,
        int documentId,
        decimal price,
        DateOnly validUntil,
        string currency)
    {
        SupplierId = supplierId;
        SupplierBranchId = supplierBranchId;
        DocumentId = documentId;
        Price = price;
        ValidUntil = validUntil;
        Currency = NormalizeCurrency(currency);
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces the document associated with this quotation and returns the old document ID.
    /// </summary>
    public int ReplaceDocument(int newDocumentId)
    {
        var oldDocumentId = DocumentId;
        DocumentId = newDocumentId;
        return oldDocumentId;
    }

    /// <summary>
    /// Replaces the currency code on this quotation. Validates length-equals-3 and uppercases.
    /// </summary>
    public void EditCurrency(string code)
    {
        Currency = NormalizeCurrency(code);
    }

    private static string NormalizeCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var canonical = currency.Trim().ToUpperInvariant();
        if (canonical.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-character code.", nameof(currency));
        }
        return canonical;
    }
}
