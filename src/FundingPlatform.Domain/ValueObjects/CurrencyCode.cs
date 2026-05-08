namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 015 — ISO 4217 currency identifier value object. Two canonical instances
/// in MVP: <see cref="Crc"/> (base, always enabled) and <see cref="Usd"/>.
///
/// Construction normalises to upper-case and rejects any string that is not
/// exactly three letters. Equality is value-based by virtue of being a record.
/// </summary>
public sealed record CurrencyCode
{
    public string Value { get; }

    public CurrencyCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var canonical = value.Trim().ToUpperInvariant();
        if (canonical.Length != 3)
        {
            throw new ArgumentException("Currency code must be exactly 3 characters.", nameof(value));
        }
        Value = canonical;
    }

    public static readonly CurrencyCode Crc = new("CRC");
    public static readonly CurrencyCode Usd = new("USD");

    /// <summary>True when this code identifies the platform's base currency (CRC).</summary>
    public bool IsBase => this == Crc;

    public static CurrencyCode From(string code) => new(code);

    public override string ToString() => Value;
}
