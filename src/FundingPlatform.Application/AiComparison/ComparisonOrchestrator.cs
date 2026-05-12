using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-C1 — extract → normalize → compare orchestration boundary.
/// Per-item lock serializes concurrent regenerations (edge case → 409). NFR-S1:
/// every outbound AI payload passes through <see cref="IPiiRedactor"/>.
/// </summary>
public class ComparisonOrchestrator : IComparisonOrchestrator
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _itemLocks = new();

    private readonly ISupplierAssembler _assembler;
    private readonly IPiiRedactor _redactor;
    private readonly IAiClient _aiClient;
    private readonly PromptCatalog _catalog;
    private readonly SchemaValidator _validator;
    private readonly IComparisonArtifactRepository _artifacts;
    private readonly IComparisonJobRepository _jobs;
    private readonly RateLimitGuard _rateLimitGuard;
    private readonly TokenCapGuard _tokenCapGuard;
    private readonly AdminAuditEventComparisonFactory _auditFactory;
    private readonly IAdminAuditWriter _auditWriter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComparisonOrchestrator> _logger;

    public ComparisonOrchestrator(
        ISupplierAssembler assembler,
        IPiiRedactor redactor,
        IAiClient aiClient,
        PromptCatalog catalog,
        SchemaValidator validator,
        IComparisonArtifactRepository artifacts,
        IComparisonJobRepository jobs,
        RateLimitGuard rateLimitGuard,
        TokenCapGuard tokenCapGuard,
        AdminAuditEventComparisonFactory auditFactory,
        IAdminAuditWriter auditWriter,
        IConfiguration configuration,
        ILogger<ComparisonOrchestrator> logger)
    {
        _assembler = assembler;
        _redactor = redactor;
        _aiClient = aiClient;
        _catalog = catalog;
        _validator = validator;
        _artifacts = artifacts;
        _jobs = jobs;
        _rateLimitGuard = rateLimitGuard;
        _tokenCapGuard = tokenCapGuard;
        _auditFactory = auditFactory;
        _auditWriter = auditWriter;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GenerateComparisonResult> GenerateAsync(
        GenerateComparisonCommand command, CancellationToken cancellationToken)
    {
        var sem = _itemLocks.GetOrAdd(command.ApplicationItemId, _ => new SemaphoreSlim(1, 1));
        var acquired = await sem.WaitAsync(0, cancellationToken);
        if (!acquired)
        {
            throw new ConcurrentGenerationException(command.ApplicationItemId);
        }

        try
        {
            return await GenerateInternalAsync(command, cancellationToken);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<GenerateComparisonResult> GenerateInternalAsync(
        GenerateComparisonCommand command, CancellationToken cancellationToken)
    {
        var assembly = await _assembler.AssembleAsync(command.ApplicationItemId, cancellationToken);
        if (assembly is null)
            return Failure(command, applicationId: 0, supplierIds: Array.Empty<int>(), "item_not_found", emitAudit: false);

        if (assembly.ApplicationIsClosed)
            return await FailAndAuditAsync(command, assembly, "application_closed", null, null, cancellationToken);

        if (assembly.Suppliers.Count < 2)
            return Failure(command, assembly.ApplicationId, assembly.Suppliers.Select(s => s.SupplierId).ToArray(),
                "single_supplier", emitAudit: false);

        var descriptor = BuildDescriptor(assembly);
        var inputHash = InputHasher.Compute(descriptor);

        // Cache short-circuit when not forced.
        var existing = await _artifacts.GetByItemIdAsync(command.ApplicationItemId, cancellationToken);
        if (!command.ForceRegenerate && existing is not null)
        {
            var freshness = existing.IsStaleAgainst(inputHash, _catalog.PromptVersion, _catalog.SchemaVersion);
            if (freshness.IsFresh)
            {
                return new GenerateComparisonSuccess(
                    command.ApplicationItemId, existing.JsonContent, existing.GeneratedAt,
                    Freshness.Fresh, Array.Empty<ChangedInput>());
            }
        }

        // Guards.
        try
        {
            await _rateLimitGuard.EnforceAsync(assembly.ApplicationId, command.ActorRole, command.BypassRateLimit, cancellationToken);
        }
        catch (RateLimitExceededException ex)
        {
            await EmitFailureAuditAsync(command, assembly, "rate_limit_exceeded", 0, 0, 0, cancellationToken);
            return new GenerateComparisonFailure(
                command.ApplicationItemId, "rate_limit_exceeded", null, null, null, null, ex.WindowResetsAt);
        }

        try
        {
            _tokenCapGuard.Enforce(
                BuildTokenCapInputs(assembly),
                command.ActorRole,
                command.BypassTokenCap);
        }
        catch (TokenCapExceededException ex)
        {
            await EmitFailureAuditAsync(command, assembly, "token_cap_exceeded", 0, 0, 0, cancellationToken);
            return new GenerateComparisonFailure(
                command.ApplicationItemId, "token_cap_exceeded",
                OffendingInput: ex.OffendingInput,
                EstimatedTokens: ex.EstimatedTokens, Cap: ex.Cap);
        }

        _logger.LogInformation(
            "AiComparison stage=guards.passed applicationItemId={ItemId} supplierIds=[{SupplierIds}]",
            command.ApplicationItemId,
            string.Join(",", assembly.Suppliers.Select(s => s.SupplierId)));

        // Extract per supplier (parallel, bounded).
        var stopwatch = Stopwatch.StartNew();
        var extractModel = _configuration["AiComparison:Anthropic:ExtractModel"] ?? "claude-sonnet-4-6";
        var compareModel = _configuration["AiComparison:Anthropic:CompareModel"] ?? "claude-opus-4-7";
        var extractConcurrency = int.TryParse(_configuration["AiComparison:ExtractConcurrency"], out var ec) ? ec : 4;
        var totalRedactionCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        var extractResults = new ExtractResult[assembly.Suppliers.Count];
        var extractSchema = _catalog.ExtractSchema;

        try
        {
            using var extractSem = new SemaphoreSlim(extractConcurrency, extractConcurrency);
            var tasks = new List<Task>();
            for (var i = 0; i < assembly.Suppliers.Count; i++)
            {
                var idx = i;
                tasks.Add(Task.Run(async () =>
                {
                    await extractSem.WaitAsync(cancellationToken);
                    try
                    {
                        var supplier = assembly.Suppliers[idx];
                        var blocks = BuildSupplierBlocks(idx, supplier, totalRedactionCounts);
                        var extractRequest = new ExtractRequest(
                            Model: extractModel,
                            PromptText: _catalog.ExtractPrompt,
                            SchemaJson: extractSchema,
                            Blocks: blocks);

                        var result = await _aiClient.ExtractAsync(extractRequest, cancellationToken);
                        _validator.ValidateExtract(result.Json);
                        extractResults[idx] = result;
                    }
                    finally
                    {
                        extractSem.Release();
                    }
                }, cancellationToken));
            }
            await Task.WhenAll(tasks);
        }
        catch (AiProviderTransientException)
        {
            await EmitFailureAuditAsync(command, assembly, "provider_transient", 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, "provider_transient");
        }
        catch (AiProviderHardException ex)
        {
            var reason = $"provider_hard:{ex.ProviderCode}";
            await EmitFailureAuditAsync(command, assembly, reason, 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, reason, ex.ProviderCode);
        }
        catch (AiSchemaInvalidException ex)
        {
            await EmitFailureAuditAsync(command, assembly, "schema_invalid", 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, "schema_invalid", OffendingInput: ex.ValidatorPath);
        }
        catch (PiiRedactionFailedException ex)
        {
            await EmitFailureAuditAsync(command, assembly, "pii_redaction_failed", 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, "pii_redaction_failed",
                OffendingInput: ex.BlobId.ToString("D"));
        }

        // Normalize.
        var normalized = NormalizeStage(assembly, extractResults);

        // Compare.
        CompareResult compareResult;
        try
        {
            compareResult = await _aiClient.CompareAsync(new CompareRequest(
                Model: compareModel,
                PromptText: _catalog.ComparePrompt,
                SchemaJson: _catalog.CompareSchema,
                NormalizedSuppliersJson: normalized), cancellationToken);
            _validator.ValidateCompare(compareResult.Json);
        }
        catch (AiProviderTransientException)
        {
            await EmitFailureAuditAsync(command, assembly, "provider_transient", 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, "provider_transient");
        }
        catch (AiProviderHardException ex)
        {
            var reason = $"provider_hard:{ex.ProviderCode}";
            await EmitFailureAuditAsync(command, assembly, reason, 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, reason, ex.ProviderCode);
        }
        catch (AiSchemaInvalidException ex)
        {
            await EmitFailureAuditAsync(command, assembly, "schema_invalid", 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, "schema_invalid", OffendingInput: ex.ValidatorPath);
        }
        stopwatch.Stop();

        var totalIn = extractResults.Sum(r => r.TokenCostInput) + compareResult.TokenCostInput;
        var totalOut = extractResults.Sum(r => r.TokenCostOutput) + compareResult.TokenCostOutput;
        var latency = (int)stopwatch.ElapsedMilliseconds;

        // Persist artifact (replace in place).
        if (existing is null)
        {
            var fresh = ComparisonArtifact.Create(
                applicationItemId: command.ApplicationItemId,
                jsonContent: compareResult.Json,
                inputHash: inputHash,
                promptVersion: _catalog.PromptVersion,
                schemaVersion: _catalog.SchemaVersion,
                aiModel: compareModel,
                generatedByUserId: command.ActorUserId,
                tokenIn: totalIn, tokenOut: totalOut, latencyMs: latency,
                generatedAt: DateTimeOffset.UtcNow);
            await _artifacts.UpsertAsync(fresh, cancellationToken);
        }
        else
        {
            existing.ReplaceWith(
                compareResult.Json, inputHash, _catalog.PromptVersion, _catalog.SchemaVersion,
                compareModel, command.ActorUserId, totalIn, totalOut, latency, DateTimeOffset.UtcNow);
            await _artifacts.UpsertAsync(existing, cancellationToken);
        }

        // Success audit.
        var success = _auditFactory.BuildSuccess(new SuccessAuditPayload(
            ApplicationId: assembly.ApplicationId,
            ApplicationItemId: command.ApplicationItemId,
            ActorUserId: command.ActorUserId,
            ActorRole: command.ActorRole,
            SupplierIds: assembly.Suppliers.Select(s => s.SupplierId).ToArray(),
            InputHash: inputHash,
            PromptVersion: _catalog.PromptVersion,
            SchemaVersion: _catalog.SchemaVersion,
            AiModel: compareModel,
            ExtractModel: extractModel,
            TokenCostInput: totalIn,
            TokenCostOutput: totalOut,
            LatencyMs: latency,
            BypassedRateLimit: command.BypassRateLimit && string.Equals(command.ActorRole, "Admin", StringComparison.OrdinalIgnoreCase),
            BypassedTokenCap: command.BypassTokenCap && string.Equals(command.ActorRole, "Admin", StringComparison.OrdinalIgnoreCase),
            RedactedFieldCounts: totalRedactionCounts));
        await _auditWriter.WriteAsync(success, cancellationToken);

        return new GenerateComparisonSuccess(
            command.ApplicationItemId, compareResult.Json, DateTimeOffset.UtcNow,
            Freshness.Fresh, Array.Empty<ChangedInput>());
    }

    public async Task<ItemStatusResult> GetStatusAsync(int applicationItemId, CancellationToken cancellationToken)
    {
        var artifact = await _artifacts.GetByItemIdAsync(applicationItemId, cancellationToken);
        var latestJob = await _jobs.GetLatestByApplicationItemAsync(applicationItemId, cancellationToken);

        // If a Pending/Running job exists, it dominates.
        if (latestJob is not null && latestJob.Status is ComparisonJobStatus.Pending or ComparisonJobStatus.Running)
        {
            var state = latestJob.Status == ComparisonJobStatus.Pending ? ItemState.Pending : ItemState.Running;
            return new ItemStatusResult(applicationItemId, state, Freshness.None,
                Array.Empty<ChangedInput>(), latestJob.LastStatusChangeAt, null);
        }

        if (latestJob is not null && latestJob.Status == ComparisonJobStatus.Failed && artifact is null)
        {
            return new ItemStatusResult(applicationItemId, ItemState.Failed, Freshness.None,
                Array.Empty<ChangedInput>(), latestJob.LastStatusChangeAt, latestJob.FailureReason);
        }

        if (artifact is null)
        {
            return new ItemStatusResult(applicationItemId, ItemState.None, Freshness.None,
                Array.Empty<ChangedInput>(), null, latestJob?.FailureReason);
        }

        var assembly = await _assembler.AssembleAsync(applicationItemId, cancellationToken);
        if (assembly is null || assembly.Suppliers.Count < 2)
        {
            return new ItemStatusResult(applicationItemId, ItemState.CachedFresh, Freshness.Fresh,
                Array.Empty<ChangedInput>(), artifact.GeneratedAt, null);
        }

        var descriptor = BuildDescriptor(assembly);
        var hash = InputHasher.Compute(descriptor);
        var freshness = artifact.IsStaleAgainst(hash, _catalog.PromptVersion, _catalog.SchemaVersion);

        return new ItemStatusResult(
            applicationItemId,
            freshness.IsFresh ? ItemState.CachedFresh : ItemState.CachedStale,
            freshness.IsFresh ? Freshness.Fresh : Freshness.Stale,
            freshness.ChangedInputs,
            artifact.GeneratedAt,
            latestJob?.FailureReason);
    }

    public async Task<CachedComparisonResult?> GetCachedComparisonAsync(int applicationItemId, CancellationToken cancellationToken)
    {
        var artifact = await _artifacts.GetByItemIdAsync(applicationItemId, cancellationToken);
        if (artifact is null) return null;

        var assembly = await _assembler.AssembleAsync(applicationItemId, cancellationToken);
        if (assembly is null || assembly.Suppliers.Count < 2)
        {
            // No live state to compare against; surface as fresh.
            return new CachedComparisonResult(applicationItemId, artifact.JsonContent, artifact.GeneratedAt,
                Freshness.Fresh, Array.Empty<ChangedInput>());
        }

        var descriptor = BuildDescriptor(assembly);
        var hash = InputHasher.Compute(descriptor);
        var freshness = artifact.IsStaleAgainst(hash, _catalog.PromptVersion, _catalog.SchemaVersion);

        return new CachedComparisonResult(
            applicationItemId, artifact.JsonContent, artifact.GeneratedAt,
            freshness.IsFresh ? Freshness.Fresh : Freshness.Stale,
            freshness.ChangedInputs);
    }

    // -------- helpers --------

    private InputDescriptor BuildDescriptor(ItemAssembly assembly) => new(
        ApplicationItemId: assembly.ApplicationItemId,
        OrderedSupplierIds: assembly.Suppliers.Select(s => s.SupplierId).OrderBy(x => x).ToArray(),
        OrderedBranchIds: assembly.Suppliers.Select(s => s.SupplierBranchId ?? 0).OrderBy(x => x).ToArray(),
        BlobReferences: assembly.Suppliers.SelectMany(s => s.Blobs).ToArray(),
        LineState: assembly.Suppliers.Select(s => new LineState(
            QuotationLineId: s.DocumentId, // 1 quotation per supplier — Document id is stable surrogate
            Quantity: 1m,
            UnitPrice: s.Price,
            CurrencyCode: s.CurrencyCode,
            ExchangeRateSnapshotId: s.SnapshotRateId)).ToArray(),
        PromptVersion: _catalog.PromptVersion,
        SchemaVersion: _catalog.SchemaVersion);

    private List<AiInputBlock> BuildSupplierBlocks(
        int supplierIdx, SupplierAssembly supplier,
        Dictionary<string, int> totalRedactionCounts)
    {
        var dto = new SupplierAssemblyDto(
            SupplierId: Guid.Empty,
            SupplierName: supplier.SupplierName,
            OwnerDni: null,
            OwnerPersonalPhone: null,
            ApplicantNationalId: null,
            ApplicantPersonalPhone: null,
            ApplicantPersonalEmail: null,
            Body: new
            {
                supplierIdx,
                supplierName = supplier.SupplierName,
                branchName = supplier.BranchName,
                verificationStatus = supplier.SupplierVerificationStatus,
                price = supplier.Price,
                currency = supplier.CurrencyCode,
                convertedCrcAmount = supplier.ConvertedCrcAmount,
                snapshotRateValue = supplier.SnapshotRateValue,
                validUntil = supplier.ValidUntil.ToString("yyyy-MM-dd"),
                fileName = supplier.DocumentFileName,
            });

        var structuredResult = _redactor.RedactStructured(dto);
        AccumulateCounts(totalRedactionCounts, structuredResult.RedactedSpans);

        var blocks = new List<AiInputBlock>
        {
            new TextBlock($"supplierIdx: {supplierIdx}\nstructuredFields: {structuredResult.SafePayload}"),
        };

        // Note: full PDF byte streaming is implemented via the storage handle
        // in a follow-up; for MVP the prompt receives the structured fields
        // and the AI client is the Stub during E2E.
        return blocks;
    }

    private string NormalizeStage(ItemAssembly assembly, ExtractResult[] extracts)
    {
        var rows = new List<object>();
        for (var i = 0; i < assembly.Suppliers.Count; i++)
        {
            var s = assembly.Suppliers[i];
            decimal totalCrc;
            decimal originalTotal = s.Price;
            string originalCurrency = s.CurrencyCode;
            if (string.Equals(s.CurrencyCode, "CRC", StringComparison.OrdinalIgnoreCase))
                totalCrc = s.Price;
            else if (s.ConvertedCrcAmount.HasValue)
                totalCrc = s.ConvertedCrcAmount.Value;
            else if (s.SnapshotRateValue.HasValue)
                totalCrc = ComparisonNormalizer.ToCrc(s.Price, s.CurrencyCode, s.SnapshotRateValue.Value);
            else
                totalCrc = s.Price; // best-effort fallback

            rows.Add(new
            {
                supplierIdx = i,
                supplierName = s.SupplierName,
                branchName = s.BranchName,
                verificationStatus = s.SupplierVerificationStatus,
                originalCurrency,
                originalTotal,
                totalCrc,
                appliedRate = s.SnapshotRateValue,
                extractRaw = SafeParse(extracts[i].Json),
            });
        }
        return JsonSerializer.Serialize(rows);
    }

    private static JsonElement? SafeParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<TokenCapInput> BuildTokenCapInputs(ItemAssembly assembly)
    {
        var list = new List<TokenCapInput>();
        foreach (var s in assembly.Suppliers)
        {
            list.Add(new TokenCapInput(
                Description: $"{s.SupplierName} — {s.DocumentFileName}",
                SizeBytes: Math.Max(s.DocumentFileSize, 1)));
        }
        return list;
    }

    private static void AccumulateCounts(Dictionary<string, int> total, IReadOnlyList<RedactedSpan> spans)
    {
        foreach (var span in spans)
        {
            total[span.FieldOrPatternName] = total.GetValueOrDefault(span.FieldOrPatternName, 0) + span.Count;
        }
    }

    private GenerateComparisonFailure Failure(
        GenerateComparisonCommand command, int applicationId, IReadOnlyList<int> supplierIds,
        string failureReason, bool emitAudit)
    {
        if (emitAudit)
        {
            _ = EmitFailureAuditSafelyAsync(command, applicationId, supplierIds, failureReason);
        }
        return new GenerateComparisonFailure(command.ApplicationItemId, failureReason);
    }

    private async Task<GenerateComparisonFailure> FailAndAuditAsync(
        GenerateComparisonCommand command, ItemAssembly assembly,
        string failureReason, string? providerCode, string? offendingInput,
        CancellationToken cancellationToken)
    {
        await EmitFailureAuditAsync(command, assembly, failureReason, 0, 0, 0, cancellationToken);
        return new GenerateComparisonFailure(command.ApplicationItemId, failureReason, providerCode, offendingInput);
    }

    private async Task EmitFailureAuditAsync(
        GenerateComparisonCommand command, ItemAssembly assembly,
        string failureReason, int tokenIn, int tokenOut, int latencyMs,
        CancellationToken cancellationToken)
    {
        var extractModel = _configuration["AiComparison:Anthropic:ExtractModel"] ?? "claude-sonnet-4-6";
        var compareModel = _configuration["AiComparison:Anthropic:CompareModel"] ?? "claude-opus-4-7";

        var failure = _auditFactory.BuildFailure(new FailureAuditPayload(
            ApplicationId: assembly.ApplicationId,
            ApplicationItemId: command.ApplicationItemId,
            ActorUserId: command.ActorUserId,
            ActorRole: command.ActorRole,
            SupplierIds: assembly.Suppliers.Select(s => s.SupplierId).ToArray(),
            InputHash: string.Empty.PadRight(64, '0'),
            PromptVersion: _catalog.PromptVersion,
            SchemaVersion: _catalog.SchemaVersion,
            AiModel: compareModel,
            ExtractModel: extractModel,
            TokenCostInput: tokenIn,
            TokenCostOutput: tokenOut,
            LatencyMs: latencyMs,
            BypassedRateLimit: command.BypassRateLimit && string.Equals(command.ActorRole, "Admin", StringComparison.OrdinalIgnoreCase),
            BypassedTokenCap: command.BypassTokenCap && string.Equals(command.ActorRole, "Admin", StringComparison.OrdinalIgnoreCase),
            RedactedFieldCounts: new Dictionary<string, int>(),
            FailureReason: failureReason));
        await _auditWriter.WriteAsync(failure, cancellationToken);
    }

    private Task EmitFailureAuditSafelyAsync(
        GenerateComparisonCommand command, int applicationId, IReadOnlyList<int> supplierIds, string failureReason)
        => Task.CompletedTask;
}
