using FundingPlatform.Application.Notifications;
using FundingPlatform.Infrastructure.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using FundingPlatform.Infrastructure.Notifications.Providers;
using FundingPlatform.Infrastructure.Notifications.Resolvers;
using FundingPlatform.Infrastructure.Notifications.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FundingPlatform.Web.Services;

/// <summary>
/// Spec 021 / T026 / T073 — wires the email-notification subsystem into the
/// host. Placement note: the DI extension lives in the Web project because
/// the Razor renderer it registers (<c>RazorEmailRenderer</c>) depends on
/// ASP.NET Core MVC types; placing the extension here keeps the AddNotifications
/// surface single-call from <c>Program.cs</c>.
///
/// <para>Selection matrix (from contracts/IEmailSender.md):</para>
/// <list type="bullet">
///   <item><c>Notifications:Provider=Mailtrap</c> → <c>MailtrapSmtpEmailSender</c></item>
///   <item><c>Notifications:Provider=Mailgun</c> → <c>MailgunHttpEmailSender</c></item>
///   <item><c>Notifications:Provider=NoOp</c> or absent → <c>NoOpEmailSender</c></item>
/// </list>
/// <para>
/// Outside Production the chosen sender is wrapped by
/// <c>RecipientAllowlistFilter</c> (FR-017 / FR-019). In Production the
/// decorator is bypassed and Mailgun configuration is enforced fail-fast
/// (FR-016).
/// </para>
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotifications(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // FR-001 — transactional outbox writer (scoped: shares AppDbContext with caller).
        services.AddScoped<INotificationOutboxWriter, NotificationOutboxWriter>();
        services.AddScoped<IWorkflowTransactionScope, EfWorkflowTransactionScope>();

        // FR-006 — recipient resolver (scoped: reads DbContext at dispatch time).
        services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
        services.AddScoped<ParticipatingAdminPredicate>();

        // FR-023 — Razor renderer (scoped to align with view-engine lifetime).
        services.AddScoped<IEmailTemplateRenderer, RazorEmailRenderer>();

        // FR-014 / FR-015 — provider selection.
        var provider = configuration["Notifications:Provider"];
        var providerName = string.IsNullOrWhiteSpace(provider) ? "" : provider.Trim();

        // FR-016 — Production fail-fast when Mailgun is selected but config is incomplete.
        if (environment.IsProduction() && string.Equals(providerName, "Mailgun", StringComparison.OrdinalIgnoreCase))
        {
            string[] required =
            {
                "Notifications:Mailgun:ApiKey",
                "Notifications:Mailgun:Domain",
                "Notifications:Sender:Email",
                "Notifications:BaseUrl",
            };
            foreach (var key in required)
            {
                if (string.IsNullOrWhiteSpace(configuration[key]))
                {
                    throw new InvalidOperationException(
                        $"Notifications:Provider=Mailgun requires '{key}' to be set in Production.");
                }
            }
        }

        // Register the concrete sender. Then wrap with the allowlist decorator outside Production.
        switch (providerName.ToLowerInvariant())
        {
            case "mailgun":
                services.AddHttpClient<MailgunHttpEmailSender>();
                services.AddScoped<MailgunHttpEmailSender>();
                if (environment.IsProduction())
                {
                    services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<MailgunHttpEmailSender>());
                }
                else
                {
                    services.AddScoped<IEmailSender>(sp => new RecipientAllowlistFilter(
                        sp.GetRequiredService<MailgunHttpEmailSender>(),
                        sp.GetRequiredService<IConfiguration>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RecipientAllowlistFilter>>()));
                }
                break;

            case "noop":
                services.AddScoped<NoOpEmailSender>();
                if (environment.IsProduction())
                {
                    services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<NoOpEmailSender>());
                }
                else
                {
                    services.AddScoped<IEmailSender>(sp => new RecipientAllowlistFilter(
                        sp.GetRequiredService<NoOpEmailSender>(),
                        sp.GetRequiredService<IConfiguration>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RecipientAllowlistFilter>>()));
                }
                break;

            case "mailtrap":
            default:
                services.AddScoped<MailtrapSmtpEmailSender>();
                if (environment.IsProduction())
                {
                    services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<MailtrapSmtpEmailSender>());
                }
                else
                {
                    services.AddScoped<IEmailSender>(sp => new RecipientAllowlistFilter(
                        sp.GetRequiredService<MailtrapSmtpEmailSender>(),
                        sp.GetRequiredService<IConfiguration>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RecipientAllowlistFilter>>()));
                }
                break;
        }

        // FR-003 — single hosted poller.
        services.AddHostedService<EmailDispatchWorker>();

        return services;
    }
}
