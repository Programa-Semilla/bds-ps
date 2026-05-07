using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Errors;

/// <summary>
/// Spec 015 / FR-018 — thrown by <see cref="FundingPlatform.Domain.Interfaces.IConversionService"/>
/// when no published rate exists for the requested source → target pair. The
/// Web layer maps this to <see cref="UserFacingErrorCode.MissingExchangeRate"/>
/// for an inline form error and a 409 on the AJAX preview endpoint.
/// </summary>
public sealed class MissingRateException : Exception
{
    public CurrencyCode SourceCurrency { get; }
    public CurrencyCode TargetCurrency { get; }

    public MissingRateException(CurrencyCode source, CurrencyCode target)
        : base($"No published exchange rate found for pair {source}->{target}.")
    {
        SourceCurrency = source;
        TargetCurrency = target;
    }
}
