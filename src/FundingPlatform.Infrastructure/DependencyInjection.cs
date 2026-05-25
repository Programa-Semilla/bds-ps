using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.Abstractions.Comparison;
using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.Services;
using FundingPlatform.Application.Admin.Users;
using FundingPlatform.Application.AiComparison;
using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.AiComparison.Anthropic;
using FundingPlatform.Infrastructure.AiComparison.Redaction;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.BackgroundServices;
using FundingPlatform.Infrastructure.DocumentGeneration;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Reports;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Persistence.Services;
using FundingPlatform.Infrastructure.PublicCodes;
using FundingPlatform.Infrastructure.StageExpiry;
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

        // Spec 020 — explicit commit boundary for orchestrator audit paths.
        services.AddScoped<IUnitOfWork, Persistence.UnitOfWork>();

        // Spec 016 — admin audit writer + group catalog service + reviewer scope.
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<Application.Admin.Groups.IGroupService, Services.GroupService>();
        services.AddScoped<Application.Reviewer.IReviewerScopeProvider, ReviewerScopeProvider>();

        // Spec 017 — admin dashboard reader + activity feed source + user-store reader.
        services.AddScoped<Application.Services.IAdminAuditEventReader, Persistence.AdminAuditEventReader>();
        services.AddScoped<Application.Services.IUserStoreReader, Identity.UserStoreReader>();

        // Spec 021 / US6 / T135 / FR-032 — narrative KPI counters reader
        // (Personas activas + Fondos entregados). Consumed by AdminDashboardProjection.
        services.AddScoped<Application.Services.IAdminDashboardCountersReader,
            Persistence.AdminDashboardCountersReader>();

        // Spec 021 / US6 / T136 / FR-033 — reviewer dashboard projection
        // (pending-quotation tile moved from admin per R-12).
        services.AddScoped<Application.ReviewerDashboard.IReviewerDashboardProjection,
            Persistence.ReviewerDashboardProjection>();

        // Spec 021 — public-code generator, password-reset token store, soft-delete
        // query filter, terse audit writer for the new event-kind discriminators.
        services.AddScoped<IPublicCodeGenerator, PublicCodeGenerator>();
        services.AddScoped<IPasswordResetTokenStore, PasswordResetTokenStore>();

        // Spec 021 / US5 / T126 — Identity-flow handlers (forgot/reset password, profile update).
        services.AddScoped<Application.Identity.IIssuePasswordResetTokenHandler, Identity.IssuePasswordResetTokenHandler>();
        services.AddScoped<Application.Identity.IConsumePasswordResetTokenHandler, Identity.ConsumePasswordResetTokenHandler>();
        services.AddScoped<Application.Identity.IUpdateProfileHandler, Identity.UpdateProfileHandler>();
        services.AddSingleton<IApplicationQueryFilter, ApplicationQueryFilter>();
        services.AddScoped<IAdminAuditEventWriter, AdminAuditEventWriter>();
        // Spec 021 — production stage-expiry clock (R-11). Integration tests
        // replace this binding with a fake clock that advances deterministically.
        services.AddSingleton<IStageExpiryClock, Clocks.SystemStageExpiryClock>();

        // Spec 021 / T115 — stage-expiry evaluator (per-Process override →
        // SystemConfiguration default → safety fallback) used by both the
        // hosted reminder service and the per-page banner ViewModel.
        services.AddScoped<IStageExpiryEvaluator, StageExpiryEvaluator>();

        // Spec 021 / FR-025 — email transport. SMTP is the production binding;
        // when Smtp:Host is empty (dev / E2E) we fall back to the logger so the
        // platform boots without a real relay. NFR-005: System.Net.Mail.SmtpClient
        // is the only built-in SMTP client; no MailKit / new managed dep.
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        var smtpHost = configuration[$"{SmtpOptions.SectionName}:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }

        // Spec 021 / T118 — template loader for the three stage-reminder emails.
        services.AddSingleton<StageReminderEmailFactory>();

        // Spec 021 / T129 — template loader for the password-reset email.
        services.AddSingleton<ForgotPasswordEmailFactory>();

        // Spec 021 / T117 — hourly stage-expiry reminder hosted service.
        services.AddHostedService<StageExpiryReminderService>();

        // Spec 021 / US2 — applicant draft handlers (autosave, submit, review
        // projection, supplier search + inline create-branch).
        services.AddScoped<Application.Applications.IAutosaveFieldHandler, Services.AutosaveFieldHandler>();
        services.AddScoped<Application.Applications.ISubmitApplicationHandler, Services.SubmitApplicationHandler>();
        services.AddScoped<Application.Applications.Queries.IGetApplicationReviewProjection, Services.GetApplicationReviewProjection>();
        services.AddScoped<Application.Suppliers.ISearchSuppliersHandler, Services.SearchSuppliersHandler>();
        services.AddScoped<Application.Suppliers.ICreateSupplierBranchHandler, Services.CreateSupplierBranchHandler>();

        // Spec 025 — location-chain resolver (distrito → cantón → provincia) used by
        // both supplier-branch write paths for server-side hierarchy validation +
        // composed-display-string construction.
        services.AddScoped<Application.Abstractions.Location.ILocationCatalogReader, Location.LocationCatalogReader>();

        // Spec 021 / US3 / T110 — admin-side supplier autocomplete (Admin OR
        // SupplierAdmin). Wired to GET /api/suppliers/search via
        // SuppliersApiController (T109).
        services.AddScoped<Application.Suppliers.Queries.ISearchSuppliersForAdminHandler,
            Services.SearchSuppliersForAdminHandler>();

        // Spec 021 / US1 — Process + Plantilla admin services (T077 / T078 / T079).
        // ProcessService implements both the command and query interfaces; one
        // registration per interface so consumers can take the narrower seam.
        services.AddScoped<Services.ProcessService>();
        services.AddScoped<Application.Processes.IProcessService>(
            sp => sp.GetRequiredService<Services.ProcessService>());
        services.AddScoped<Application.Processes.Queries.IProcessQueryService>(
            sp => sp.GetRequiredService<Services.ProcessService>());
        services.AddScoped<Application.Plantillas.IPlantillaService, Services.PlantillaService>();

        // Spec 020 — AI quote comparison wiring.
        services.AddAiComparison(configuration);

        // Spec 023 / FR-009 — silent cache invalidation when an applicant
        // edits a quotation field. The next reviewer Generar todo regenerates.
        services.AddScoped<IComparisonCacheInvalidator,
            Comparison.ComparisonCacheInvalidator>();

        return services;
    }

    /// <summary>Spec 020 — AI quote comparison wiring extracted for clarity.</summary>
    public static IServiceCollection AddAiComparison(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection("AiComparison:Anthropic"));

        services.AddSingleton<PromptCatalog>();
        services.AddSingleton<SchemaValidator>();
        services.AddSingleton<IPiiRedactor, PiiRedactor>();

        services.AddScoped<IComparisonArtifactRepository, ComparisonArtifactRepository>();
        services.AddScoped<IComparisonJobRepository, ComparisonJobRepository>();

        // Orchestrator + handler + guards.
        services.AddScoped<Application.AiComparison.ISupplierAssembler, Infrastructure.AiComparison.SupplierAssembler>();
        services.AddScoped<Application.AiComparison.IRateLimitCounter, Infrastructure.AiComparison.AdminAuditRateLimitCounter>();
        services.AddScoped<Application.AiComparison.RateLimitGuard>();
        services.AddScoped<Application.AiComparison.TokenCapGuard>();
        services.AddScoped<Application.AiComparison.AdminAuditEventComparisonFactory>();
        services.AddScoped<IComparisonOrchestrator, Application.AiComparison.ComparisonOrchestrator>();
        services.AddScoped<Application.AiComparison.Commands.GenerateComparisonCommandHandler>();

        // Hosted services — comparison worker + reaper.
        services.AddHostedService<Infrastructure.AiComparison.ComparisonJobWorker>();
        services.AddHostedService<Infrastructure.AiComparison.ComparisonJobReaper>();

        var provider = configuration["AiComparison:Provider"] ?? "Stub";
        if (string.Equals(provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAiClient, AnthropicAiClient>();
        }
        else
        {
            // FINDING-8 — keep Scoped for parity with the live provider. The
            // stub's static call counters are independent of DI lifetime.
            services.AddScoped<IAiClient, StubAiClient>();
        }

        return services;
    }
}
