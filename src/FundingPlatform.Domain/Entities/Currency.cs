using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 015 — administrator-configurable currency. CRC is the platform's
/// permanent base currency: it must always be enabled (FR-002) and cannot be
/// disabled (<see cref="Disable"/> throws when <see cref="IsBaseCurrency"/> is
/// true). USD ships enabled by default and can be toggled by an administrator.
/// </summary>
public class Currency
{
    public CurrencyCode Code { get; private set; } = null!;
    public string Symbol { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public byte DecimalPrecision { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsBaseCurrency { get; private set; }
    public short DisplayOrder { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Currency() { }

    public Currency(
        CurrencyCode code,
        string symbol,
        string displayName,
        byte decimalPrecision,
        bool isEnabled,
        bool isBaseCurrency,
        short displayOrder)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (isBaseCurrency && !isEnabled)
        {
            throw new InvalidOperationException(
                "A base currency must be enabled. CRC cannot be created in a disabled state.");
        }

        Code = code;
        Symbol = symbol;
        DisplayName = displayName;
        DecimalPrecision = decimalPrecision;
        IsEnabled = isEnabled;
        IsBaseCurrency = isBaseCurrency;
        DisplayOrder = displayOrder;
    }

    /// <summary>Idempotent: enabling an already-enabled currency is a no-op.</summary>
    public void Enable()
    {
        IsEnabled = true;
    }

    /// <summary>Disables a non-base currency. Throws if this row is the base currency (CRC).</summary>
    public void Disable()
    {
        if (IsBaseCurrency)
        {
            throw new InvalidOperationException(
                "The base currency cannot be disabled.");
        }
        IsEnabled = false;
    }
}
