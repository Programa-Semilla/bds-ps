// Spec 021 — see specs/021-feedback-session-may13/tasks.md T145 and
// contracts/public-routes.md (Public landing) / spec FR-031.

using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Web.Controllers;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 021 / US7 / T145 / FR-031 — admin surface for managing the two FR-031
/// public-landing slot files (Reglamento + Ejemplo de cotización). Files are
/// persisted through <see cref="IObjectStorage"/> under the
/// <see cref="FileCategory.PublicLandingFile"/> category. The canonical
/// <c>ObjectKey</c> for each slot is recorded against a dedicated
/// <see cref="SystemConfiguration"/> row keyed by
/// <see cref="HomeController.ReglamentoStorageKeyConfig"/> /
/// <see cref="HomeController.EjemploStorageKeyConfig"/> so the anonymous landing
/// can decide between a download link and the *Próximamente* placeholder.
///
/// Authorization: Admin only — SupplierAdmin must not be able to mutate public
/// brand content, so <see cref="SupplierAdminDeniedAttribute"/> is applied to
/// match the rest of the admin sweep.
/// </summary>
[Authorize(Roles = "Admin")]
[SupplierAdminDenied]
[Route("Admin/PublicLanding")]
public class AdminPublicLandingFilesController : Controller
{
    private const FileCategory Category = FileCategory.PublicLandingFile;

    // Slot identifiers exposed to admin POST routes and to the public-side
    // download controller. Lowercased to match ObjectKey owner-segment rules.
    public const string SlotReglamento = "reglamento";
    public const string SlotEjemplo = "ejemplo";

    private readonly IObjectStorage _storage;
    private readonly ISystemConfigurationRepository _systemConfig;

    public AdminPublicLandingFilesController(
        IObjectStorage storage,
        ISystemConfigurationRepository systemConfig)
    {
        _storage = storage;
        _systemConfig = systemConfig;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var reglamento = await _systemConfig.GetByKeyAsync(HomeController.ReglamentoStorageKeyConfig);
        var ejemplo = await _systemConfig.GetByKeyAsync(HomeController.EjemploStorageKeyConfig);

        var vm = new AdminPublicLandingIndexViewModel(
            ReglamentoStorageKey: NullIfBlank(reglamento?.Value),
            EjemploStorageKey: NullIfBlank(ejemplo?.Value));
        return View(vm);
    }

    [HttpPost("UploadReglamento")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.PublicLandingFile)]
    public Task<IActionResult> UploadReglamento(IFormFile? file, CancellationToken ct)
        => UploadSlotAsync(SlotReglamento, HomeController.ReglamentoStorageKeyConfig,
            "Reglamento", file, ct);

    [HttpPost("UploadEjemplo")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.PublicLandingFile)]
    public Task<IActionResult> UploadEjemplo(IFormFile? file, CancellationToken ct)
        => UploadSlotAsync(SlotEjemplo, HomeController.EjemploStorageKeyConfig,
            "Ejemplo de cotización", file, ct);

    [HttpPost("Clear/{slot}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(string slot, CancellationToken ct)
    {
        var (configKey, label) = ResolveSlot(slot);
        if (configKey is null)
        {
            return NotFound();
        }

        var current = await _systemConfig.GetByKeyAsync(configKey);
        if (current is not null && !string.IsNullOrWhiteSpace(current.Value))
        {
            try
            {
                var key = ObjectKey.Parse(current.Value);
                await _storage.DeleteAsync(Category, key, ct);
            }
            catch
            {
                // The stored key may be malformed or the blob may already be
                // gone; either way we still want to clear the system-config
                // pointer so the public landing reverts to the placeholder.
            }

            current.UpdateValue(string.Empty);
            await _systemConfig.UpdateAsync(current);
            await _systemConfig.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = $"{label} eliminado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> UploadSlotAsync(
        string slot,
        string configKey,
        string label,
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Debe seleccionar un archivo PDF.";
            return RedirectToAction(nameof(Index));
        }

        // FR-031 / CLAUDE.md table — slot files are PDFs surfaced from the
        // landing page; reject everything else at the controller boundary.
        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;
        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "Solo se aceptan archivos PDF.";
            return RedirectToAction(nameof(Index));
        }

        // Delete the prior blob (best-effort) so we don't leak storage when
        // an admin replaces a slot file. The pointer in SystemConfiguration is
        // the only authority on what the public landing serves.
        var existing = await _systemConfig.GetByKeyAsync(configKey);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Value))
        {
            try
            {
                var priorKey = ObjectKey.Parse(existing.Value);
                await _storage.DeleteAsync(Category, priorKey, ct);
            }
            catch
            {
                // Same posture as Clear — drop the pointer regardless.
            }
        }

        var newKey = ObjectKey.Build(
            Category,
            ownerSegment: "public-landing",
            entityId: slot,
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: ".pdf");

        await using (var stream = file.OpenReadStream())
        {
            await _storage.UploadAsync(
                Category,
                newKey,
                stream,
                "application/pdf",
                file.Length,
                ct);
        }

        if (existing is null)
        {
            // First-time upload — create the SystemConfiguration row. The
            // PostDeployment seed for spec 021 does not pre-create these keys
            // (they are slot pointers, not config knobs), so we insert lazily.
            existing = new SystemConfiguration(configKey, newKey.Value,
                description: $"FR-031 — public landing slot key for '{slot}'.");
            await _systemConfig.AddAsync(existing);
        }
        else
        {
            existing.UpdateValue(newKey.Value);
            await _systemConfig.UpdateAsync(existing);
        }

        await _systemConfig.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{label} actualizado.";
        return RedirectToAction(nameof(Index));
    }

    private static (string? ConfigKey, string Label) ResolveSlot(string slot) => slot switch
    {
        SlotReglamento => (HomeController.ReglamentoStorageKeyConfig, "Reglamento"),
        SlotEjemplo => (HomeController.EjemploStorageKeyConfig, "Ejemplo de cotización"),
        _ => (null, slot),
    };

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
