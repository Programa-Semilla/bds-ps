namespace FundingPlatform.Application.Regulatory;

/// <summary>
/// Spec 043 — configuration for the daily Hacienda sync worker and its API client.
/// Bound from the <c>Regulatory:HaciendaSync</c> configuration section. All values
/// have sensible defaults so absence of config never crashes the host
/// (Constitution VI).
/// </summary>
public sealed class HaciendaSyncOptions
{
    public const string SectionName = "Regulatory:HaciendaSync";

    /// <summary>Selects the client impl: <c>Live</c> → real API; anything else → the
    /// offline fake. Defaults to the offline <c>Fake</c> so dev/E2E never hit the live API
    /// even if read directly (the DI gate also defaults to Fake); real envs opt into
    /// <c>Live</c> via azd-env / container config. Mirrors <c>AiComparison:Provider</c>.</summary>
    public string Provider { get; set; } = "Fake";

    /// <summary>Gate the daily worker. When false the worker does not schedule cycles.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Local wall-clock time-of-day (America/Costa_Rica) for the daily run; §16.5.</summary>
    public string RunAtLocalTime { get; set; } = "06:00";

    /// <summary>FR-017 — per-cycle provider batch size (throttle).</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Optional inter-call delay (ms) to throttle the upstream API.</summary>
    public int PerCallDelayMs { get; set; } = 0;

    /// <summary>Live client base address.</summary>
    public string BaseUrl { get; set; } = "https://api.hacienda.go.cr";
}
