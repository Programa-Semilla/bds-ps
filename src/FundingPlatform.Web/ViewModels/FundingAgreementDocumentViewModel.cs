using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Options;

namespace FundingPlatform.Web.ViewModels;

public class FundingAgreementDocumentViewModel
{
    public string AgreementReference { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }

    public FunderOptions Funder { get; set; } = new();

    public string ApplicantLegalName { get; set; } = string.Empty;
    public string ApplicantLegalId { get; set; } = string.Empty;
    public string ApplicantEmail { get; set; } = string.Empty;
    public string? ApplicantPhone { get; set; }

    public string LocaleCode { get; set; } = "es-CR";
    public string CurrencyIsoCode { get; set; } = "CRC";

    public IReadOnlyList<FundingAgreementItemRowDto> Items { get; set; } = Array.Empty<FundingAgreementItemRowDto>();
    public decimal TotalAmount { get; set; }
    public IReadOnlyList<CurrencyTotal> TotalsByCurrency { get; set; } = Array.Empty<CurrencyTotal>();
}

public sealed record CurrencyTotal(string Currency, decimal Amount);
