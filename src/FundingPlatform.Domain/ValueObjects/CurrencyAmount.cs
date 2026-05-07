namespace FundingPlatform.Domain.ValueObjects;

public sealed record CurrencyAmount
{
    public string Currency { get; }
    public decimal Amount { get; }

    /// <summary>
    /// Spec 015 / T416 — converted-CRC sum corresponding to <see cref="Amount"/>.
    /// For CRC stacks this equals <see cref="Amount"/> by definition. For non-CRC
    /// stacks this is the sum of the snapshot-converted CRC values across all
    /// rows that contributed to <see cref="Amount"/>. May be 0 when contributing
    /// rows had no snapshot (legacy quotations).
    /// </summary>
    public decimal ConvertedCrcAmount { get; }

    public CurrencyAmount(string currency, decimal amount)
        : this(currency, amount, EqualsCrc(currency) ? amount : 0m)
    {
    }

    public CurrencyAmount(string currency, decimal amount, decimal convertedCrcAmount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var canonical = currency.Trim().ToUpperInvariant();
        if (canonical.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-character code.", nameof(currency));
        }

        Currency = canonical;
        Amount = amount;
        ConvertedCrcAmount = convertedCrcAmount;
    }

    private static bool EqualsCrc(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return false;
        return string.Equals(currency.Trim(), "CRC", StringComparison.OrdinalIgnoreCase);
    }
}
