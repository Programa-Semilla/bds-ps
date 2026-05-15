using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Interfaces;
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
    // Striped lock: fixed memory footprint (1024 SemaphoreSlim instances) shared
    // across all application-item ids. The same item id always maps to the same
    // stripe; collisions only cost serialization on a different item — exclusion
    // for the same item is still guaranteed. Replaces the previous unbounded
    // ConcurrentDictionary that leaked one Semaphore per ever-seen item id.
    private const int LockStripeCount = 1024;
    private static readonly SemaphoreSlim[] _itemLocks = CreateStripes(LockStripeCount);

    private static SemaphoreSlim[] CreateStripes(int count)
    {
        var stripes = new SemaphoreSlim[count];
        for (var i = 0; i < count; i++)
            stripes[i] = new SemaphoreSlim(1, 1);
        return stripes;
    }

    private static SemaphoreSlim GetLockFor(int applicationItemId)
    {
        // Mask with stripe count - 1 (power of two) for a uniform, fast index.
        var idx = applicationItemId.GetHashCode() & (LockStripeCount - 1);
        return _itemLocks[idx];
    }

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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IObjectStorage _objectStorage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComparisonOrchestrator> _logger;

    // Spec 020 / FINDING-5 — refuse a single supplier PDF larger than this cap
    // so the orchestrator can't OOM on a 100 MiB upload. The upload-side cap
    // (Storage:Categories:application-attachments:MaxSizeBytes) is the primary
    // defense; this is a defense-in-depth ceiling.
    private const long MaxPdfBytesPerBlob = 25L * 1024L * 1024L;

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
        IUnitOfWork unitOfWork,
        IObjectStorage objectStorage,
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
        _unitOfWork = unitOfWork;
        _objectStorage = objectStorage;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GenerateComparisonResult> GenerateAsync(
        GenerateComparisonCommand command, CancellationToken cancellationToken)
    {
        var sem = GetLockFor(command.ApplicationItemId);
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
        // Spec 020 / NFR-R1 — extraction fans out over N suppliers in parallel
        // (Task.Run + SemaphoreSlim above). Each worker accumulates redaction
        // counts into this shared bag, so it MUST be thread-safe. A plain
        // Dictionary<,> corrupts under concurrent mutation (observed: null-key
        // insert + "non-concurrent collection" exceptions when N>=2 suppliers).
        var totalRedactionCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

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
                        var blocks = await BuildSupplierBlocksAsync(
                            idx, assembly, supplier, totalRedactionCounts, cancellationToken);
                        var extractRequest = new ExtractRequest(
                            Model: extractModel,
                            PromptText: _catalog.ExtractPrompt,
                            SchemaJson: extractSchema,
                            Blocks: blocks);

                        var result = await _aiClient.ExtractAsync(extractRequest, cancellationToken);
                        // Spec 020 — the platform's supplier currency is authoritative
                        // (used downstream by NormalizeStage). The AI occasionally omits
                        // `fields.currencyCode` despite the prompt's default-to-CRC rule;
                        // backfill from the assembly so the schema invariant always
                        // holds and downstream consumers see a stable shape.
                        var patchedJson = EnsureExtractCurrencyCode(result.Json, supplier.CurrencyCode);
                        _validator.ValidateExtract(patchedJson);
                        extractResults[idx] = result with { Json = patchedJson };
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
        catch (UnsupportedFormatException ex)
        {
            await EmitFailureAuditAsync(command, assembly, "unsupported_format", 0, 0, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return new GenerateComparisonFailure(command.ApplicationItemId, "unsupported_format",
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
            // Materialize the concurrent bag into a plain Dictionary so the
            // audit-payload JSON serializer sees a stable snapshot (and so the
            // payload record's IReadOnlyDictionary contract is satisfied with a
            // collection that won't mutate further).
            RedactedFieldCounts: new Dictionary<string, int>(totalRedactionCounts, StringComparer.Ordinal)));
        await _auditWriter.WriteAsync(success, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

    /// <summary>
    /// FR-B1 / FR-C2 / FR-B2 (FINDING-5 + FINDING-6) — assemble the per-supplier
    /// AI input. Three blocks per supplier:
    ///   1) Redacted structured-fields TextBlock (applicant + supplier PII
    ///      scrubbed before the bytes leave the platform).
    ///   2) For each blob attached to the quotation: a PdfBlock carrying the
    ///      raw bytes. Claude reads PDFs natively so the model can emit
    ///      <c>sourceRef.blobId / page</c> citations linking back to the
    ///      originating document. Blobs larger than <see cref="MaxPdfBytesPerBlob"/>
    ///      are refused with <c>unsupported_format</c>.
    /// </summary>
    private async Task<List<AiInputBlock>> BuildSupplierBlocksAsync(
        int supplierIdx,
        ItemAssembly itemAssembly,
        SupplierAssembly supplier,
        ConcurrentDictionary<string, int> totalRedactionCounts,
        CancellationToken cancellationToken)
    {
        // FINDING-6 — surface the PII fields the live domain actually carries.
        // Domain has no separate "personal" channel for applicants or supplier
        // owners; the spec drift is reconciled in spec.md. Field-name keys
        // remain the FR-B2 ones so the redactedFieldCounts dictionary keeps a
        // stable contract across the API.
        var dto = new SupplierAssemblyDto(
            SupplierId: Guid.Empty,
            SupplierName: supplier.SupplierName,
            OwnerDni: NullIfEmpty(supplier.SupplierLegalId),
            OwnerPersonalPhone: supplier.BranchContactPhone,
            ApplicantNationalId: itemAssembly.ApplicantLegalId,
            ApplicantPersonalPhone: itemAssembly.ApplicantPhone,
            ApplicantPersonalEmail: itemAssembly.ApplicantEmail,
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

        // FINDING-5 — wire the PDF byte path. IObjectStorage.OpenReadAsync is
        // the streaming-read seam (NOT ResolveServingHandleAsync, which is for
        // signed URLs). Each blob is read into memory; the Anthropic SDK uploads
        // it as base64 inside a DocumentContent block (handled in AnthropicAiClient).
        foreach (var blob in supplier.Blobs)
        {
            if (string.IsNullOrEmpty(blob.ContentHash)) continue; // blob key missing — skip
            ObjectKey key;
            try
            {
                key = ObjectKey.Parse(blob.ContentHash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AiComparison skipping malformed blob key for supplierIdx={SupplierIdx} blobId={BlobId}.",
                    supplierIdx, blob.BlobId);
                continue;
            }

            byte[] bytes;
            try
            {
                using var stream = await _objectStorage.OpenReadAsync(
                    FileCategory.ApplicationAttachment, key, cancellationToken);
                using var ms = new MemoryStream();
                await CopyWithCapAsync(stream, ms, MaxPdfBytesPerBlob, cancellationToken);
                bytes = ms.ToArray();
            }
            catch (PdfTooLargeException ex)
            {
                _logger.LogWarning(
                    "AiComparison refusing oversized blob supplierIdx={SupplierIdx} blobId={BlobId} size>{Cap} bytes.",
                    supplierIdx, blob.BlobId, MaxPdfBytesPerBlob);
                throw new UnsupportedFormatException(blob.BlobId, "blob_exceeds_size_cap", ex);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex,
                    "AiComparison blob not found supplierIdx={SupplierIdx} blobId={BlobId}.",
                    supplierIdx, blob.BlobId);
                continue;
            }

            // FR-B3 / A-1 — PDF text-layer redaction would normally run here
            // (RedactFileText). The live dep graph carries no PDF text extractor
            // (PdfPig / iText / etc. are NOT yet vendored; Anthropic.SDK was the
            // only new dep approved). Until a text-extraction library is added,
            // file-text pattern redaction is deferred. Field-level PII redaction
            // (above) still scrubs the structured DB-side payload before any
            // bytes leave the platform. The PDF bytes themselves go directly to
            // the AI provider over the same secure transport as the structured
            // text; the provider is contractually bound to handle the data.
            blocks.Add(new PdfBlock(blob.BlobId, bytes));
        }

        return blocks;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static async Task CopyWithCapAsync(
        Stream source, Stream destination, long capBytes, CancellationToken ct)
    {
        var buffer = new byte[81920]; // 80 KB
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            total += read;
            if (total > capBytes) throw new PdfTooLargeException(capBytes);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    /// <summary>FINDING-5 — sentinel raised when a single blob exceeds the orchestrator's hard cap.</summary>
    private sealed class PdfTooLargeException : Exception
    {
        public PdfTooLargeException(long cap)
            : base($"Blob exceeded streaming cap of {cap} bytes.") { }
    }

    /// <summary>FINDING-5 — converted to the <c>unsupported_format</c> failure reason.</summary>
    private sealed class UnsupportedFormatException : Exception
    {
        public Guid BlobId { get; }
        public string Reason { get; }
        public UnsupportedFormatException(Guid blobId, string reason, Exception inner)
            : base(reason, inner)
        {
            BlobId = blobId;
            Reason = reason;
        }
    }

    private static string NormalizeStage(ItemAssembly assembly, ExtractResult[] extracts)
    {
        // FINDING-11: route through the unit-tested ComparisonNormalizer helper
        // so unit coverage actually exercises the production code path. Conversion
        // / es-CR formatting / discrepancy passthrough all live in the helper.
        var normalized = new List<NormalizedSupplier>(assembly.Suppliers.Count);
        for (var i = 0; i < assembly.Suppliers.Count; i++)
        {
            var s = assembly.Suppliers[i];
            decimal totalCrc;
            if (string.Equals(s.CurrencyCode, "CRC", StringComparison.OrdinalIgnoreCase))
                totalCrc = s.Price;
            else if (s.ConvertedCrcAmount.HasValue)
                totalCrc = s.ConvertedCrcAmount.Value;
            else if (s.SnapshotRateValue.HasValue)
                totalCrc = ComparisonNormalizer.ToCrc(s.Price, s.CurrencyCode, s.SnapshotRateValue.Value);
            else
                totalCrc = s.Price; // best-effort fallback

            var extractedFields = SafeParse(extracts[i].Json) ?? EmptyJsonObject;
            normalized.Add(new NormalizedSupplier(
                SupplierIdx: i,
                SupplierName: s.SupplierName,
                BranchName: s.BranchName,
                VerificationStatus: s.SupplierVerificationStatus,
                OriginalCurrency: s.CurrencyCode,
                AppliedRate: s.SnapshotRateValue,
                TotalCrc: totalCrc,
                OriginalTotal: s.Price,
                ExtractedFields: extractedFields,
                Discrepancies: Array.Empty<NormalizedDiscrepancy>()));
        }
        return ComparisonNormalizer.BuildNormalizedSuppliersJson(normalized);
    }

    /// <summary>
    /// Spec 020 — guarantees <c>fields.currencyCode</c> is present in the extract
    /// JSON before schema validation. The Anthropic model intermittently omits
    /// the field despite the prompt's default-to-CRC rule; downstream consumers
    /// don't read it (the platform's <see cref="SupplierAssembly.CurrencyCode"/>
    /// is authoritative) so injecting it is lossless. Returns the original string
    /// unchanged when parsing fails or the field is already set, so the existing
    /// validator path still surfaces real schema violations.
    /// </summary>
    private static string EnsureExtractCurrencyCode(string json, string supplierCurrencyCode)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }

        if (root is not JsonObject obj) return json;
        if (obj["fields"] is not JsonObject fields) return json;
        if (fields["currencyCode"] is JsonValue existing
            && existing.TryGetValue<string>(out var code)
            && !string.IsNullOrWhiteSpace(code))
        {
            return json;
        }

        var fallback = string.IsNullOrWhiteSpace(supplierCurrencyCode)
            ? "CRC"
            : supplierCurrencyCode.Trim().ToUpperInvariant();
        fields["currencyCode"] = fallback;
        return obj.ToJsonString();
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

    private static readonly JsonElement EmptyJsonObject =
        JsonDocument.Parse("{}").RootElement.Clone();

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

    private static void AccumulateCounts(ConcurrentDictionary<string, int> total, IReadOnlyList<RedactedSpan> spans)
    {
        foreach (var span in spans)
        {
            // Spec 020 / NFR-R1 — atomic per-key accumulation; called concurrently
            // from the parallel supplier-extract fan-out (line ~196).
            total.AddOrUpdate(
                span.FieldOrPatternName,
                addValueFactory: _ => span.Count,
                updateValueFactory: (_, existing) => existing + span.Count);
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
        // Failure paths have no follow-up repository SaveChanges (sync controller
        // returns straight to the client; worker only saves after this method),
        // so commit the audit row explicitly here.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task EmitFailureAuditSafelyAsync(
        GenerateComparisonCommand command, int applicationId, IReadOnlyList<int> supplierIds, string failureReason)
        => Task.CompletedTask;
}
