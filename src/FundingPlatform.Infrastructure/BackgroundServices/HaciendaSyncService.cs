using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Hacienda;
using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Hacienda;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.BackgroundServices;

/// <summary>Spec 043 — per-run tally returned by <see cref="HaciendaSyncService.RunOnceAsync"/>.</summary>
public sealed record HaciendaSyncSummary(int Checked, int Changed, int Unchanged, int Failed);

/// <summary>
/// Spec 043 (US2) — daily background worker that refreshes every provider's Hacienda
/// status from the <c>fe/ae</c> API. Modeled on <c>StageExpiryReminderService</c>: a
/// startup-resilient loop that schedules to a wall-clock local time (research D4) and a
/// public <see cref="RunOnceAsync"/> seam for deterministic tests + the dev trigger.
/// Each provider is synced in its own DI scope under <c>RowVersion</c> so a concurrent
/// auditor edit is skipped (FR-025) and one provider's failure never aborts the run (FR-024).
/// </summary>
public sealed class HaciendaSyncService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<HaciendaSyncOptions> _options;
    private readonly ILogger<HaciendaSyncService> _logger;

    private enum SyncOutcome { Changed, Unchanged, Failed, Skipped }

    public HaciendaSyncService(
        IServiceProvider services,
        IOptions<HaciendaSyncOptions> options,
        ILogger<HaciendaSyncService> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("HaciendaSyncService disabled (Regulatory:HaciendaSync:Enabled=false).");
            return;
        }

        _logger.LogInformation("HaciendaSyncService starting — daily at {RunAt} (America/Costa_Rica).", opts.RunAtLocalTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DailyRunSchedule.TimeUntilNextRun(opts.RunAtLocalTime, DateTime.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var summary = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Hacienda sync cycle done: checked={Checked} changed={Changed} unchanged={Unchanged} failed={Failed}.",
                    summary.Checked, summary.Changed, summary.Unchanged, summary.Failed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Hacienda sync cycle threw; will retry on the next schedule.");
            }
        }
    }

    /// <summary>Public test/dev seam — runs exactly one full sync pass over all providers.</summary>
    public async Task<HaciendaSyncSummary> RunOnceAsync(CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.Value.BatchSize);
        var perCallDelayMs = Math.Max(0, _options.Value.PerCallDelayMs);

        List<int> supplierIds;
        string? systemActorId;
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierIds = await db.Suppliers.Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
            // The audit ActorUserId + per-field LastReviewedBy carry FKs to AspNetUsers, so the
            // automated actor must be a real id — the system sentinel (excluded by a global query
            // filter, hence IgnoreQueryFilters).
            systemActorId = await db.Users.IgnoreQueryFilters()
                .Where(u => u.IsSystemSentinel)
                .Select(u => u.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(systemActorId))
        {
            _logger.LogError("Hacienda sync aborted: no system sentinel user found to attribute the sync to.");
            return new HaciendaSyncSummary(0, 0, 0, 0);
        }

        int checkedCount = 0, changed = 0, unchanged = 0, failed = 0;

        foreach (var batch in supplierIds.Chunk(batchSize))
        {
            foreach (var supplierId in batch)
            {
                ct.ThrowIfCancellationRequested();
                checkedCount++;
                try
                {
                    switch (await SyncOneAsync(supplierId, systemActorId, ct).ConfigureAwait(false))
                    {
                        case SyncOutcome.Changed: changed++; break;
                        case SyncOutcome.Unchanged: unchanged++; break;
                        case SyncOutcome.Failed: failed++; break;
                        case SyncOutcome.Skipped: break; // concurrency conflict — retried next cycle
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // FR-024 — one provider's exception never aborts the batch.
                    failed++;
                    _logger.LogError(ex, "Hacienda sync threw for supplier {SupplierId}; continuing.", supplierId);
                }

                if (perCallDelayMs > 0)
                {
                    await Task.Delay(perCallDelayMs, ct).ConfigureAwait(false);
                }
            }
        }

        return new HaciendaSyncSummary(checkedCount, changed, unchanged, failed);
    }

    private async Task<SyncOutcome> SyncOneAsync(int supplierId, string systemActorId, CancellationToken ct)
    {
        // Fresh scope per provider so a RowVersion conflict (or any failure) is fully
        // isolated and cannot poison a shared DbContext.
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var client = sp.GetRequiredService<IHaciendaApiClient>();
        var audit = sp.GetRequiredService<IAdminAuditEventWriter>();

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct).ConfigureAwait(false);
        if (supplier is null) return SyncOutcome.Skipped; // deleted mid-run

        var now = DateTime.UtcNow;

        // Validate the local id before any network call: a CR taxpayer identification is
        // ≥9 digits. A malformed/blank id is recorded as a failure with NO API call.
        var digits = new string(supplier.LegalId.Where(char.IsDigit).ToArray());
        var lookup = digits.Length < 9
            ? HaciendaLookupResult.Failed("Identificación inválida para consulta en Hacienda.")
            : await client.LookupAsync(supplier.LegalId, ct).ConfigureAwait(false);
        var mapped = HaciendaStatusMapper.Map(lookup);

        SyncOutcome outcome;
        if (mapped is null)
        {
            var reason = lookup.Reason ?? "No se pudo verificar el estado en Hacienda.";
            supplier.RecordHaciendaSyncFailure(now, reason);
            await audit.WriteAsync(
                AdminAuditEvent.SupplierHaciendaSyncFailed, systemActorId,
                FailurePayload(supplierId, supplier.LegalId, reason), ct).ConfigureAwait(false);
            outcome = SyncOutcome.Failed;
        }
        else
        {
            var change = supplier.ApplyHaciendaSyncResult(mapped.Value, now, systemActorId);
            var action = change.Kind == RegulatoryChangeKind.Changed
                ? AdminAuditEvent.SupplierRegulatoryChanged
                : AdminAuditEvent.SupplierRegulatoryReviewed;
            await audit.WriteAsync(action, systemActorId, SuccessPayload(supplierId, change), ct).ConfigureAwait(false);
            outcome = change.Kind == RegulatoryChangeKind.Changed ? SyncOutcome.Changed : SyncOutcome.Unchanged;
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return outcome;
        }
        catch (DbUpdateConcurrencyException)
        {
            // FR-025 — a concurrent auditor edit wins; skip this provider this cycle.
            _logger.LogInformation(
                "Supplier {SupplierId} was modified concurrently during sync; skipped this cycle.", supplierId);
            return SyncOutcome.Skipped;
        }
    }

    private static string SuccessPayload(int supplierId, RegulatoryChange change) =>
        JsonSerializer.Serialize(new
        {
            supplierId,
            field = change.Field.ToString(),
            oldValue = change.OldValue,
            newValue = change.NewValue,
            source = change.Source.ToString(),
            kind = change.Kind.ToString(),
        });

    private static string FailurePayload(int supplierId, string identificacion, string reason) =>
        JsonSerializer.Serialize(new { supplierId, identificacion, reason });
}
