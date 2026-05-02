using FundingPlatform.Application;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Options;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure;
using FundingPlatform.Infrastructure.DocumentGeneration;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Identity;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.Middleware;
using FundingPlatform.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<AppDbContext>("fundingdb");

// Spec 014: register the Azure Blob client integration for the Aspire-managed
// "blobs" resource so OTel + health checks work. ObjectStorageRegistration
// (called inside AddInfrastructure) defers to whichever BlobServiceClient is
// already registered. Only registered when the resource exists (production +
// local Azurite); LocalFilesystem provider skips this entirely.
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Azurite";
if (string.Equals(storageProvider, "Azurite", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(storageProvider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration["Storage:ConnectionString"]
        ?? builder.Configuration.GetConnectionString("blobs");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        builder.AddAzureBlobServiceClient("blobs");
    }
}

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IFundingAgreementHtmlRenderer, RazorFundingAgreementHtmlRenderer>();

// Admin Reports configuration (FR-007: fail fast when DefaultCurrency is missing).
builder.Services.Configure<AdminReportsOptions>(
    builder.Configuration.GetSection(AdminReportsOptions.SectionName));

var adminReportsDefaultCurrency = builder.Configuration[$"{AdminReportsOptions.SectionName}:DefaultCurrency"];
if (string.IsNullOrWhiteSpace(adminReportsDefaultCurrency) || adminReportsDefaultCurrency.Trim().Length != 3)
{
    throw new InvalidOperationException(
        "AdminReports:DefaultCurrency is required and must be a 3-character currency code (e.g., 'COP', 'USD'). Set the configuration value before starting the host.");
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddUserStore<SentinelAwareUserStore>()
    .AddErrorDescriber<EsCrIdentityErrorDescriber>()
    .AddDefaultTokenProviders();

// Spec 012: pin the request culture to es-CR with format overrides (research Decision 1).
// Single-locale; no negotiation; built-in providers cleared so Accept-Language cannot
// shift the culture per-request.
var esCrCulture = EsCrCultureFactory.Build();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(esCrCulture, esCrCulture);
    options.SupportedCultures = new[] { esCrCulture };
    options.SupportedUICultures = new[] { esCrCulture };
    options.RequestCultureProviders.Clear();
});

builder.Services.AddControllersWithViews(options =>
{
    // Spec 012: Spanish ModelBinding messages (FR-012).
    var p = options.ModelBindingMessageProvider;
    p.SetValueIsInvalidAccessor(v => $"El valor '{v}' no es válido.");
    p.SetMissingBindRequiredValueAccessor(name => $"El campo {name} es obligatorio.");
    p.SetMissingKeyOrValueAccessor(() => "Falta una clave o valor obligatorio.");
    p.SetMissingRequestBodyRequiredValueAccessor(() => "Se requiere un cuerpo de solicitud no vacío.");
    p.SetValueMustNotBeNullAccessor(v => $"El valor '{v}' no es válido.");
    p.SetAttemptedValueIsInvalidAccessor((v, name) => $"El valor '{v}' no es válido para {name}.");
    p.SetUnknownValueIsInvalidAccessor(name => $"El valor proporcionado no es válido para {name}.");
    p.SetValueMustBeANumberAccessor(name => $"El campo {name} debe ser un número.");
});
builder.Services.AddRazorPages();

builder.Services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(options =>
{
    options.ViewLocationExpanders.Add(new AdminAreaViewLocationExpander());
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddScoped<IClaimsTransformation, AdminImpliesReviewerClaimsTransformation>();

// Spec 012 / FR-014 — Spanish translation of Application-layer user-facing
// error codes. Stateless: a singleton matches the static-string convention.
builder.Services.AddSingleton<IUserFacingErrorTranslator, UserFacingErrorTranslator>();

var app = builder.Build();

// Spec 012: NFR-008 — warn at startup if FundingAgreement:LocaleCode diverges
// from the pinned request culture (es-CR). The PDF generator's LocaleCode
// remains overridable for testing flexibility, but production deployments
// should match the request culture.
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var configuredLocale = app.Configuration["FundingAgreement:LocaleCode"];
    if (!string.IsNullOrWhiteSpace(configuredLocale)
        && !string.Equals(configuredLocale, esCrCulture.Name, StringComparison.OrdinalIgnoreCase))
    {
        startupLogger.LogWarning(
            "FundingAgreement:LocaleCode is '{Configured}' but request culture is '{Pinned}'. PDF and UI locales will diverge.",
            configuredLocale, esCrCulture.Name);
    }
}

using (var scope = app.Services.CreateScope())
{
    try
    {
        scope.ServiceProvider.GetRequiredService<SyncfusionLicenseValidator>().ValidateOrThrow();
    }
    catch (InvalidOperationException ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Syncfusion license validation skipped: {Message}", ex.Message);
    }

    var bootstrapLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var identityLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("FundingPlatform.Infrastructure.Identity.IdentityConfiguration");

    // E2E fixture deploys the dacpac AFTER the web app starts; production deploys
    // it before (Aspire WaitFor). The seed steps below need the schema (and the
    // dacpac post-deploy role rows). Retry SqlException with bounded backoff so
    // both scenarios converge.
    const int maxAttempts = 60;
    var seeded = false;
    for (var attempt = 0; attempt < maxAttempts; attempt++)
    {
        try
        {
            await FundingPlatform.Infrastructure.Identity.IdentityConfiguration.SeedRolesAsync(scope.ServiceProvider);

            await FundingPlatform.Infrastructure.Identity.IdentityConfiguration.SeedSentinelAdminAsync(
                scope.ServiceProvider, app.Configuration, identityLogger);

            if (app.Environment.IsDevelopment())
            {
                await FundingPlatform.Infrastructure.Identity.IdentityConfiguration.SeedUsersAsync(scope.ServiceProvider);
            }
            seeded = true;
            break;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            if (attempt == maxAttempts - 1)
            {
                bootstrapLogger.LogWarning(
                    "Database schema not ready after {Attempts} attempts; seed skipped. Error: {Message}",
                    maxAttempts, ex.Message);
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
    if (seeded)
    {
        bootstrapLogger.LogInformation("Identity seed completed.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseRequestLocalization();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapStaticAssets();
app.MapRazorPages();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
