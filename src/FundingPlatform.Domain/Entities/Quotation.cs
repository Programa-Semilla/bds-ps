using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;

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

    // Spec 015 — multi-currency snapshot fields. CRC quotes leave Snapshot null
    // and copy Price into ConvertedCrcAmount; non-CRC quotes embed the rate that
    // was applied at save time so the converted CRC value stays stable across
    // subsequent rate changes (FR-013, FR-016).
    public decimal? ConvertedCrcAmount { get; private set; }
    public ExchangeRateSnapshot? Snapshot { get; private set; }
    public bool LegacyNeedsReview { get; private set; }

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

        // Legacy/free-text constructor: stamp ConvertedCrcAmount only for CRC; non-CRC
        // rows constructed via this path will be flagged for review by the post-deploy
        // migration or by the Application layer when SetCurrencyAndAmount is preferred.
        if (Currency == CurrencyCode.Crc.Value)
        {
            ConvertedCrcAmount = price;
        }
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
    [Obsolete("Use ChangeCurrency(CurrencyCode, IConversionService) so the rate snapshot is reset.", error: false)]
    public void EditCurrency(string code)
    {
        Currency = NormalizeCurrency(code);
    }

    /// <summary>
    /// Spec 015 — sets currency + price and computes the converted CRC amount.
    ///
    /// CRC: snapshot stays null, ConvertedCrcAmount = price.
    /// Non-CRC: converts via <paramref name="conversion"/>, embeds the resulting
    /// snapshot, marks the source rate used (FR-008), and stores the converted
    /// CRC amount.
    /// </summary>
    public async Task SetCurrencyAndAmountAsync(
        CurrencyCode currency,
        decimal price,
        IConversionService conversion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(conversion);
        if (price <= 0m)
        {
            throw new ArgumentException("Price must be greater than zero.", nameof(price));
        }

        Currency = currency.Value;
        Price = price;
        LegacyNeedsReview = false;

        if (currency.IsBase)
        {
            ConvertedCrcAmount = price;
            Snapshot = null;
            return;
        }

        var result = await conversion.ConvertAsync(currency, CurrencyCode.Crc, price, ct).ConfigureAwait(false);
        ConvertedCrcAmount = result.Converted;
        Snapshot = result.Snapshot;
        result.Source.MarkUsed();
    }

    /// <summary>
    /// Spec 015 — amount-only edit. Re-applies the existing snapshot (rate stays
    /// pinned to the originally-snapshotted value per FR-016). Throws if the
    /// quotation is in the legacy-needs-review state.
    /// </summary>
    public void EditAmount(decimal newPrice)
    {
        if (newPrice <= 0m)
        {
            throw new ArgumentException("Price must be greater than zero.", nameof(newPrice));
        }
        if (LegacyNeedsReview)
        {
            throw new InvalidOperationException(
                "Cannot edit amount: this quotation is flagged for legacy rate review.");
        }

        Price = newPrice;

        if (Currency == CurrencyCode.Crc.Value)
        {
            ConvertedCrcAmount = newPrice;
            return;
        }

        if (Snapshot is null)
        {
            throw new InvalidOperationException(
                "Cannot edit amount on a non-CRC quotation without a rate snapshot.");
        }

        ConvertedCrcAmount = Math.Round(newPrice * Snapshot.RateValue, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Spec 015 / FR-017a — currency change with re-conversion. Clears the existing
    /// snapshot and re-applies a fresh one drawn from the latest published rate.
    /// </summary>
    public async Task ChangeCurrencyAsync(
        CurrencyCode newCurrency,
        IConversionService conversion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newCurrency);
        ArgumentNullException.ThrowIfNull(conversion);

        Snapshot = null;
        ConvertedCrcAmount = null;
        await SetCurrencyAndAmountAsync(newCurrency, Price, conversion, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec 023 / FR-004 — reassigns this quotation to a different branch of the
    /// SAME supplier. Enforces the spec-013 invariant <c>branch.SupplierId ==
    /// this.SupplierId</c> on the entity itself so no future caller can bypass
    /// it. No exchange-rate side effects: Currency / Snapshot / ConvertedCrcAmount
    /// stay untouched.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="branch"/> is null.</exception>
    /// <exception cref="ArgumentException">When the branch belongs to a different supplier.</exception>
    public void ChangeBranch(SupplierBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.SupplierId != SupplierId)
        {
            throw new ArgumentException(
                "Sucursal no válida para este proveedor.", nameof(branch));
        }

        SupplierBranchId = branch.Id;
        SupplierBranch = branch;
    }

    /// <summary>
    /// Spec 023 / FR-005 — sets the quotation's <see cref="ValidUntil"/> with the
    /// es-CR calendar "today-or-future" guard enforced on the entity. No
    /// exchange-rate side effects.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="newValidUntil"/> is in the past.</exception>
    public void SetValidUntil(DateOnly newValidUntil)
    {
        if (newValidUntil < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            throw new ArgumentException(
                "La fecha de vigencia debe ser hoy o futura.", nameof(newValidUntil));
        }
        ValidUntil = newValidUntil;
    }

    /// <summary>
    /// Spec 015 / US6 — admin attaches a historical rate to a flagged legacy
    /// quotation, clears the flag, and stamps the converted CRC amount.
    /// </summary>
    public void AttachLegacyRate(ExchangeRateSnapshot snapshot, decimal convertedCrc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (convertedCrc <= 0m)
        {
            throw new ArgumentException("Converted CRC amount must be greater than zero.", nameof(convertedCrc));
        }
        if (Currency == CurrencyCode.Crc.Value)
        {
            throw new InvalidOperationException(
                "AttachLegacyRate is only valid for non-CRC quotations.");
        }

        Snapshot = snapshot;
        ConvertedCrcAmount = convertedCrc;
        LegacyNeedsReview = false;
    }

    /// <summary>
    /// Internal helper used by infrastructure-level migration shims that need to
    /// flag a row as legacy without going through the rate-conversion path.
    /// Not used by application code paths.
    /// </summary>
    internal void MarkLegacyNeedsReview()
    {
        LegacyNeedsReview = true;
        ConvertedCrcAmount = null;
        Snapshot = null;
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
