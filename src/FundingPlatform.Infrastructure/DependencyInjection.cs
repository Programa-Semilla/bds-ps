using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.Services;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.DocumentGeneration;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence.Reports;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Persistence.Services;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FundingPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IImpactTemplateRepository, ImpactTemplateRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IFundingAgreementRepository, FundingAgreementRepository>();
        services.AddScoped<Application.Interfaces.ISignedUploadRepository, SignedUploadRepository>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();

        // Spec 018 / FR-019 — FunderOptions and FundingAgreementOptions removed. Funder
        // identity is hardcoded in the sworn declaration partial; locale + currency are
        // resolved via EsCrCultureFactory and FundingAgreement:CurrencyIsoCode at use-time.
        services.Configure<AdminReportsOptions>(configuration.GetSection(AdminReportsOptions.SectionName));

        services.AddScoped<IAdminReportsService, AdminReportsService>();
        services.AddScoped<IReportQueryService, ReportQueryService>();

        services.AddSingleton<IFundingAgreementPdfRenderer, SyncfusionFundingAgreementPdfRenderer>();
        services.AddSingleton<SyncfusionLicenseValidator>();

        services.AddObjectStorage(configuration);

        // Spec 015 — multi-currency repositories + conversion service.
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IQuotationLegacyRepository, QuotationLegacyRepository>();
        services.AddScoped<IConversionService, ConversionService>();

        // Spec 015 / Phase 5 (US3) — admin currency-config + exchange-rate
        // application services. Phase 8 (US6) — legacy-quotation rate-attach.
        services.AddScoped<ICurrencyConfigService, CurrencyConfigService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<ILegacyQuotationRateAttachService, LegacyQuotationRateAttachService>();

        // Spec 016 — admin audit writer + group catalog service + reviewer scope.
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<Application.Admin.Groups.IGroupService, Services.GroupService>();
        services.AddScoped<Application.Reviewer.IReviewerScopeProvider, ReviewerScopeProvider>();

        // Spec 017 — admin dashboard reader + activity feed source + user-store reader.
        services.AddScoped<Application.Services.IAdminAuditEventReader, Persistence.AdminAuditEventReader>();
        services.AddScoped<Application.Services.IUserStoreReader, Identity.UserStoreReader>();

        return services;
    }
}
