// Spec 021 — see specs/021-feedback-session-may13/tasks.md T147 and
// contracts/public-routes.md (Public landing) / spec FR-031.

using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 021 / US7 / T147 / FR-031 — anonymous download surface for the two
/// public-landing slot files. The slot identifier in the URL is one of
/// <c>reglamento</c> | <c>ejemplo</c>; everything else 404s. The serving mode
/// stays on <see cref="ServingMode.BackendStream"/> so the application
/// boundary remains the only authorization point, matching the spec 014 /
/// FR-018 pattern used by every other download surface (Funding Agreement, etc.).
/// </summary>
[AllowAnonymous]
[Route("files/public-landing/{slot}")]
public class PublicLandingFilesController : Controller
{
    private const FileCategory Category = FileCategory.PublicLandingFile;

    private readonly IObjectStorage _storage;
    private readonly ISystemConfigurationRepository _systemConfig;

    public PublicLandingFilesController(
        IObjectStorage storage,
        ISystemConfigurationRepository systemConfig)
    {
        _storage = storage;
        _systemConfig = systemConfig;
    }

    [HttpGet]
    public async Task<IActionResult> Download(string slot, CancellationToken ct)
    {
        // Resolve the configured SystemConfiguration key for the requested slot.
        // Anything not in the allow-list returns 404 (never disclose whether
        // the slot exists for an unknown identifier).
        var configKey = slot?.ToLowerInvariant() switch
        {
            "reglamento" => HomeController.ReglamentoStorageKeyConfig,
            "ejemplo" => HomeController.EjemploStorageKeyConfig,
            _ => null,
        };
        if (configKey is null) return NotFound();

        var config = await _systemConfig.GetByKeyAsync(configKey);
        if (config is null || string.IsNullOrWhiteSpace(config.Value))
        {
            // Slot not configured yet — the public landing renders the
            // *Próximamente* placeholder, but a direct URL hit deserves a 404.
            return NotFound();
        }

        ObjectKey objectKey;
        try
        {
            objectKey = ObjectKey.Parse(config.Value);
        }
        catch (ArgumentException)
        {
            // Stored pointer is malformed — treat as missing so the public
            // surface remains honest. The admin upload flow rejects malformed
            // keys at creation time, so this is a defensive branch only.
            return NotFound();
        }

        BackendStreamHandle handle;
        try
        {
            var resolved = await _storage.ResolveServingHandleAsync(
                Category,
                objectKey,
                ServingMode.BackendStream,
                ct);
            handle = (BackendStreamHandle)resolved;
        }
        catch (ObjectNotFoundException)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=300";
        var fileName = slot!.ToLowerInvariant() switch
        {
            "reglamento" => "Reglamento.pdf",
            "ejemplo" => "EjemploCotizacion.pdf",
            _ => $"{slot}.pdf",
        };
        return File(handle.Content, handle.ContentType ?? "application/pdf",
            fileDownloadName: fileName);
    }
}
