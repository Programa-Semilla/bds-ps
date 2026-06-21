using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FundingPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddScoped<ApplicationService>();
        services.AddScoped<AdminService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<ApplicantResponseService>();
        services.AddScoped<FundingAgreementService>();
        services.AddScoped<SignedUploadService>();
        services.AddScoped<SupplierCatalogService>();

        // Spec 011 — facelift projection services + copy providers (FR-024..FR-060, research §7).
        services.AddSingleton<IStageMappingProvider, StageMappingProvider>();
        services.AddScoped<IJourneyStageResolver, JourneyStageResolver>();
        services.AddScoped<IJourneyProjector, JourneyProjector>();
        services.AddScoped<IApplicantDashboardProjection, ApplicantDashboardProjection>();
        services.AddScoped<IReviewerQueueProjection, ReviewerQueueProjection>();
        // Spec 040 — group-scoped auditor inbox.
        services.AddScoped<Audit.IAuditorQueueProjection, AuditorQueueProjection>();
        services.AddSingleton<IApplicantCopyProvider, ApplicantCopyProvider>();
        services.AddSingleton<IReviewerCopyProvider, ReviewerCopyProvider>();
        services.AddSingleton<ICeremonyCopyProvider, CeremonyCopyProvider>();

        // Spec 017 — admin dashboard projection + activity feed copy provider.
        services.AddScoped<IAdminDashboardProjection, AdminDashboardProjection>();
        services.AddSingleton<IAdminAuditEventCopyProvider, AdminAuditEventCopyProvider>();

        // Spec 027 / US4 — shared per-line decision summary projection (pure mapping).
        services.AddSingleton<IDecisionSummaryProjection, DecisionSummaryProjection>();

        // Spec 021 — Application abstractions; implementations bound in
        // FundingPlatform.Infrastructure.DependencyInjection.
        // (IPasswordResetTokenStore, IApplicationQueryFilter,
        // IStageExpiryEvaluator, IAdminAuditEventWriter — see Infrastructure DI.)

        // Spec 021 / US2 — applicant draft handler interfaces (autosave,
        // submit, review projection, supplier search, create-branch).
        // Implementations bound in FundingPlatform.Infrastructure.DependencyInjection.

        if (configuration is not null)
        {
            services.Configure<SignedUploadOptions>(
                configuration.GetSection(SignedUploadOptions.SectionName));
        }

        return services;
    }
}
