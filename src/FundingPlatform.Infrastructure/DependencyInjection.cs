using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.Services;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Options;
using FundingPlatform.Domain.Interfaces;
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

        services.Configure<FunderOptions>(configuration.GetSection(FunderOptions.SectionName));
        services.Configure<FundingAgreementOptions>(configuration.GetSection(FundingAgreementOptions.SectionName));
        services.Configure<AdminReportsOptions>(configuration.GetSection(AdminReportsOptions.SectionName));

        services.AddScoped<IAdminReportsService, AdminReportsService>();
        services.AddScoped<IReportQueryService, ReportQueryService>();

        services.AddSingleton<IFundingAgreementPdfRenderer, SyncfusionFundingAgreementPdfRenderer>();
        services.AddSingleton<SyncfusionLicenseValidator>();

        services.AddObjectStorage(configuration);

        // Spec 015 — multi-currency repositories + conversion service. The
        // currency-config / exchange-rate / legacy-quotation Application services
        // (US3, US6) are registered in FundingPlatform.Application.DependencyInjection
        // when their concrete classes land in their respective phases.
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddScoped<IConversionService, ConversionService>();

        return services;
    }
}
