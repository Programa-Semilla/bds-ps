using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.Services;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Options;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.DocumentGeneration;
using FundingPlatform.Infrastructure.FileStorage;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence.Reports;
using FundingPlatform.Infrastructure.Persistence.Repositories;
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
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();

        services.Configure<FunderOptions>(configuration.GetSection(FunderOptions.SectionName));
        services.Configure<FundingAgreementOptions>(configuration.GetSection(FundingAgreementOptions.SectionName));
        services.Configure<AdminReportsOptions>(configuration.GetSection(AdminReportsOptions.SectionName));

        services.AddScoped<IAdminReportsService, AdminReportsService>();
        services.AddScoped<IReportQueryService, ReportQueryService>();

        services.AddSingleton<IFundingAgreementPdfRenderer, SyncfusionFundingAgreementPdfRenderer>();
        services.AddSingleton<SyncfusionLicenseValidator>();

        services.AddObjectStorage(configuration);

        // Spec 014 (T024): bridge legacy IFileStorageService callers to the new
        // IObjectStorage abstraction. This registration replaces the previous
        // LocalFileStorageService one above, since this is registered last.
        // T053 deletes both IFileStorageService and this facade.
        services.AddScoped<IFileStorageService, Storage.Legacy.FileStorageServiceFacade>();

        return services;
    }
}
