namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 015 — audit event-type constants for multi-currency admin actions and
/// legacy-quotation rate attachment. Lives in Domain.Entities to match the
/// project's existing audit-action pattern (see <see cref="SigningAuditActions"/>).
/// </summary>
public static class MultiCurrencyAuditActions
{
    public const string CurrencyEnabled = "Currency.Enabled";
    public const string CurrencyDisabled = "Currency.Disabled";
    public const string ExchangeRateCreated = "ExchangeRate.Created";
    public const string ExchangeRateEditAttemptBlocked = "ExchangeRate.EditAttemptBlocked";
    public const string ExchangeRateDeleteAttemptBlocked = "ExchangeRate.DeleteAttemptBlocked";
    public const string QuotationLegacyRateAttached = "Quotation.LegacyRateAttached";
}
