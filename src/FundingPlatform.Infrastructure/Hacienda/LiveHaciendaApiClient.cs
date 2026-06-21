using System.Net;
using System.Text.Json;
using FundingPlatform.Application.Abstractions.Hacienda;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Hacienda;

/// <summary>
/// Spec 043 — live <c>IHaciendaApiClient</c> over <c>GET {BaseUrl}/fe/ae?identificacion={id}</c>.
/// Typed <see cref="HttpClient"/> (BaseAddress + timeout configured in DI via
/// <c>AddHttpClient</c>); no new managed dependency (built-in HTTP + System.Text.Json).
/// Never throws for transport/HTTP errors — they map to
/// <see cref="HaciendaLookupResult.Failed"/> so one provider's failure can't abort the batch.
/// </summary>
public sealed class LiveHaciendaApiClient : IHaciendaApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<LiveHaciendaApiClient> _logger;

    public LiveHaciendaApiClient(HttpClient http, ILogger<LiveHaciendaApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<HaciendaLookupResult> LookupAsync(string identificacion, CancellationToken ct)
    {
        var id = new string((identificacion ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(id))
        {
            return HaciendaLookupResult.Failed("Identificación inválida o vacía.");
        }

        try
        {
            using var resp = await _http
                .GetAsync($"fe/ae?identificacion={Uri.EscapeDataString(id)}", ct)
                .ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // "Information not available on this system" → SinInformacion (research D1).
                return HaciendaLookupResult.NotRegistered();
            }
            if (!resp.IsSuccessStatusCode)
            {
                return HaciendaLookupResult.Failed($"HTTP {(int)resp.StatusCode}.");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (!root.TryGetProperty("situacion", out var sit) || sit.ValueKind != JsonValueKind.Object)
            {
                return HaciendaLookupResult.Failed("Respuesta sin bloque 'situacion'.");
            }

            var estado = sit.TryGetProperty("estado", out var e) ? e.GetString() : null;
            if (string.IsNullOrWhiteSpace(estado))
            {
                return HaciendaLookupResult.Failed("Respuesta sin 'estado'.");
            }

            var nombre = root.TryGetProperty("nombre", out var n) ? n.GetString() : null;
            return HaciendaLookupResult.Found(
                nombre,
                new HaciendaSituacion(estado!, ParseSiNo(sit, "moroso"), ParseSiNo(sit, "omiso")));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine host-shutdown cancellation, not an HTTP timeout
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hacienda lookup for identificacion {Id} failed.", id);
            return HaciendaLookupResult.Failed(ex.Message);
        }
    }

    private static bool ParseSiNo(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v)
           && v.ValueKind == JsonValueKind.String
           && string.Equals(v.GetString(), "SI", StringComparison.OrdinalIgnoreCase);
}
