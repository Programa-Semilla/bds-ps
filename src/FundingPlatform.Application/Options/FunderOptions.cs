namespace FundingPlatform.Application.Options;

public class FunderOptions
{
    public const string SectionName = "FundingAgreement:Funder";

    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
}

public class FundingAgreementOptions
{
    public const string SectionName = "FundingAgreement";

    public string LocaleCode { get; set; } = "es-CR";
    public string CurrencyIsoCode { get; set; } = "CRC";
    public FunderOptions Funder { get; set; } = new();
}
