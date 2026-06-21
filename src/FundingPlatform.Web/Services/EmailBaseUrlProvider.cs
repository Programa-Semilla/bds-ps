using FundingPlatform.Application.Notifications.Email;
using Microsoft.AspNetCore.Http;

namespace FundingPlatform.Web.Services;

/// <summary>
/// Default <see cref="IEmailBaseUrlProvider"/>. Mirrors the request-aware precedence
/// of <c>AdminUsersController.ComposeResetLink</c> so email images and links resolve to
/// the same host. Safe in a BackgroundService scope (outbox / stage-reminder workers):
/// <see cref="IHttpContextAccessor.HttpContext"/> is null there, so it returns the
/// configured base URL.
/// </summary>
public sealed class EmailBaseUrlProvider : IEmailBaseUrlProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public EmailBaseUrlProvider(
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
        _configuration = configuration;
    }

    public string GetBaseUrl()
    {
        var configured = _configuration["Notifications:BaseUrl"];

        // Outside Development, trust an explicitly-configured absolute base URL — this is
        // the only source available to the background dispatch worker (no request context).
        if (!_environment.IsDevelopment()
            && !string.IsNullOrWhiteSpace(configured)
            && Uri.TryCreate(configured, UriKind.Absolute, out _))
        {
            return configured.TrimEnd('/');
        }

        // Prefer the live request host (Development, or any env with no valid config).
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null && request.Host.HasValue)
        {
            return $"{request.Scheme}://{request.Host}";
        }

        // No request in scope (worker) and no usable config: best-effort config value.
        return (configured ?? string.Empty).TrimEnd('/');
    }
}
