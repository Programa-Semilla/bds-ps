using System.Diagnostics;
using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Storage;

/// <summary>
/// FR-025: emits a single structured log event per storage operation with the
/// fields documented in <c>data-model.md § Logging shape</c>. Wraps every
/// implementation method so the three providers stay in sync.
/// MUST NOT log blob contents or signed URLs.
/// </summary>
public sealed class ObjectStorageDiagnostics
{
    private readonly ILogger<ObjectStorageDiagnostics> _logger;

    public ObjectStorageDiagnostics(ILogger<ObjectStorageDiagnostics> logger)
    {
        _logger = logger;
    }

    public async Task<T> TrackAsync<T>(
        string operation,
        FileCategory category,
        ObjectKey key,
        StorageProviderName provider,
        Func<DiagnosticContext, Task<T>> body,
        long? sizeBytes = null,
        CancellationToken ct = default)
    {
        var ctx = new DiagnosticContext { SizeBytes = sizeBytes };
        var sw = Stopwatch.StartNew();
        string outcome = "Success";
        string? errorCode = null;
        Exception? error = null;
        try
        {
            return await body(ctx).ConfigureAwait(false);
        }
        catch (ObjectNotFoundException ex)
        {
            outcome = "NotFound";
            errorCode = "ObjectNotFound";
            error = ex;
            throw;
        }
        catch (LocalProviderUrlNotSupportedException ex)
        {
            outcome = "Error";
            errorCode = "LocalProviderUrlNotSupported";
            error = ex;
            throw;
        }
        catch (ObjectStorageOperationException ex)
        {
            outcome = ex.Reason == ObjectStorageOperationReason.RetryExhausted ? "RetryExhausted" : "Error";
            errorCode = ex.Reason.ToString();
            error = ex;
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "Cancelled";
            errorCode = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            outcome = "Error";
            errorCode = "Backend";
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            EmitEvent(
                operation,
                key.Container,
                key.Value,
                ctx.SizeBytes,
                sw.ElapsedMilliseconds,
                outcome,
                provider,
                errorCode,
                error);
        }
    }

    public async Task TrackAsync(
        string operation,
        FileCategory category,
        ObjectKey key,
        StorageProviderName provider,
        Func<DiagnosticContext, Task> body,
        long? sizeBytes = null,
        CancellationToken ct = default)
    {
        await TrackAsync<bool>(operation, category, key, provider, async ctx =>
        {
            await body(ctx).ConfigureAwait(false);
            return true;
        }, sizeBytes, ct).ConfigureAwait(false);
    }

    private void EmitEvent(
        string operation,
        string container,
        string key,
        long? sizeBytes,
        long durationMs,
        string outcome,
        StorageProviderName provider,
        string? errorCode,
        Exception? error)
    {
        var level = outcome == "Success" || outcome == "NotFound"
            ? LogLevel.Information
            : LogLevel.Warning;

        _logger.Log(
            level,
            error,
            "ObjectStorage.{Operation} container={Container} key={Key} sizeBytes={SizeBytes} durationMs={DurationMs} outcome={Outcome} provider={Provider} errorCode={ErrorCode}",
            operation,
            container,
            key,
            sizeBytes,
            durationMs,
            outcome,
            provider,
            errorCode);
    }

    public sealed class DiagnosticContext
    {
        public long? SizeBytes { get; set; }
    }
}
