namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 015 — direction of an exchange rate quote. The MVP applies <see cref="Buy"/>
/// when converting USD → CRC for storage; <see cref="Sell"/> is captured for audit
/// only and is reserved for a future spec.
/// </summary>
public enum RateType : byte
{
    Buy = 1,
    Sell = 2,
}
