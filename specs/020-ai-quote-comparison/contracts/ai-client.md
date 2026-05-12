# Boundary Contracts: IAiClient, IPiiRedactor, IComparisonOrchestrator

**Spec**: `spec.md` | **Plan**: `plan.md` | **Date**: 2026-05-11

## `IAiClient` (Application abstraction)

**Purpose**: provider-agnostic seam over the AI provider. NFR-M1: adding a second provider in a future spec must touch only files inside the provider-implementation folder + DI registration.

```csharp
public interface IAiClient
{
    Task<ExtractResult> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken);
    Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken);
}

public sealed record ExtractRequest(
    string Model,
    string PromptText,
    string SchemaJson,
    IReadOnlyList<AiInputBlock> Blocks);

public sealed record CompareRequest(
    string Model,
    string PromptText,
    string SchemaJson,
    string NormalizedSuppliersJson);

public abstract record AiInputBlock;
public sealed record TextBlock(string Text) : AiInputBlock;
public sealed record PdfBlock(Guid BlobId, ReadOnlyMemory<byte> Bytes) : AiInputBlock;

public sealed record ExtractResult(
    string Json,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs);

public sealed record CompareResult(
    string Json,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs);
```

**Invariants**:
- All `AiInputBlock` payloads have already been redacted by `IPiiRedactor`. The client trusts its input. The orchestrator enforces this.
- The client does **not** retry. Transient failures bubble up as typed exceptions (`AiProviderTransientException`, `AiProviderHardException`). FR-I1/I2 surfacing is the orchestrator's job.
- The client does **not** validate the response against the schema. Schema validation lives in the orchestrator. The client just returns the JSON string.
- The client does **not** log raw bodies. Token counts + latency are returned for audit instrumentation.

**Anthropic implementation** (`AnthropicAiClient`):
- Uses `Anthropic.SDK` NuGet.
- `ExtractAsync`: uses Anthropic's tool-use / JSON-output mode with `SchemaJson` enforced. Default model `claude-sonnet-4-6`.
- `CompareAsync`: same JSON-output mode. Default model `claude-opus-4-7`.
- API key sourced from `AiComparison:Anthropic:ApiKey` via `IConfiguration`. Refuses to start (fail-fast at DI registration) if missing in `Production`.
- System prompts include prompt-injection mitigation language (NFR-S5): file content blocks are clearly delimited; the model is instructed to ignore in-document attempts to alter behavior.

## `IPiiRedactor` (Application abstraction)

**Purpose**: deterministic boundary that scrubs PII out of any payload before it reaches `IAiClient`. The only thing that constructs `AiInputBlock`s in the codebase.

```csharp
public interface IPiiRedactor
{
    RedactionResult RedactStructured(SupplierAssemblyDto assembly);
    RedactionResult RedactFileText(Guid blobId, string text);
}

public sealed record SupplierAssemblyDto(
    Guid SupplierId,
    string SupplierName,
    string? OwnerDni,
    string? OwnerPersonalPhone,
    string? ApplicantNationalId,
    string? ApplicantPersonalPhone,
    string? ApplicantPersonalEmail,
    /* other non-PII fields */ object Body);

public sealed record RedactionResult(
    string SafePayload,
    IReadOnlyList<RedactedSpan> RedactedSpans);

public sealed record RedactedSpan(string FieldOrPatternName, int Count);
```

**Invariants** (FR-B2, FR-B4, NFR-S1):
- Deterministic: identical input ⇒ identical output (no randomness, no per-call salt).
- Field-level redaction on structured data: the 5 enumerated PII fields are replaced with `[REDACTED]`.
- Pattern-level redaction on file text: cédula regex, phone regex, email regex applied.
- Unit-tested against `tests/Fixtures/Pii/*.txt` and `tests/Fixtures/Pii/*.pdf-text.txt`.
- Returns the count of redacted spans for observability (no raw value retained).
- Caller (orchestrator) treats a non-empty redaction-span list as a normal signal; an exception is thrown only if redaction *cannot succeed* on a payload (e.g., image-only PDF with no text layer).

## `IComparisonOrchestrator` (Application abstraction)

**Purpose**: the single entry point from Web → AI pipeline. Encapsulates extract → normalize → compare, guard checks, cache lookups, audit emission. Per Clean Architecture, `Web` depends on `Application` and references this interface only.

```csharp
public interface IComparisonOrchestrator
{
    Task<GenerateComparisonResult> GenerateAsync(GenerateComparisonCommand command, CancellationToken cancellationToken);
    Task<ItemStatusResult> GetStatusAsync(int applicationItemId, CancellationToken cancellationToken);
    Task<CachedComparisonResult?> GetCachedComparisonAsync(int applicationItemId, CancellationToken cancellationToken);
}

public sealed record GenerateComparisonCommand(
    int ApplicationItemId,
    string ActorUserId,
    string ActorRole,
    bool BypassRateLimit,
    bool BypassTokenCap,
    bool ForceRegenerate = false);

public abstract record GenerateComparisonResult;
public sealed record GenerateComparisonSuccess(
    int ApplicationItemId,
    string ArtifactJson,
    DateTimeOffset GeneratedAt,
    Freshness Freshness,
    IReadOnlyList<ChangedInput> ChangedInputs) : GenerateComparisonResult;
public sealed record GenerateComparisonFailure(
    int ApplicationItemId,
    string FailureReason,
    string? ProviderCode = null,
    string? OffendingInput = null,
    int? EstimatedTokens = null,
    int? Cap = null,
    DateTimeOffset? WindowResetsAt = null) : GenerateComparisonResult;

public sealed record ItemStatusResult(
    int ApplicationItemId,
    ItemState State,
    Freshness Freshness,
    IReadOnlyList<ChangedInput> ChangedInputs,
    DateTimeOffset? LastUpdatedAt,
    string? FailureReason);

public enum ItemState { None, CachedFresh, CachedStale, Pending, Running, Failed }
public enum Freshness { None, Fresh, Stale }
public enum ChangedInput { FileAdded, FileRemoved, LineEdited, SupplierAdded, SupplierRemoved, SnapshotChanged, SchemaBumped, PromptVersionBumped }
```

**Orchestration flow** (per item):
1. Acquire per-item `SemaphoreSlim` to serialize concurrent regenerations (FR edge case: concurrent regeneration → 409).
2. Build `InputDescriptor` from live state via repositories.
3. Look up existing artifact; if fresh AND command is not a forced regen, return `GenerateComparisonSuccess` immediately (cached path).
4. Apply `RateLimitGuard` (skip if `BypassRateLimit && actorRole == Admin`).
5. Build pre-flight token estimate via `TokenCapGuard`; reject (skip if `BypassTokenCap && actorRole == Admin`).
6. For each supplier: assemble payload via `IPiiRedactor` → call `IAiClient.ExtractAsync`. Run with bounded parallelism (`AiComparison:ExtractConcurrency`).
7. Schema-validate each extract result; fail the run on invalid JSON.
8. Run `ComparisonNormalizer` (pure server-side: units, dates, CRC conversion using each quotation's snapshot ID).
9. Call `IAiClient.CompareAsync` with the normalized array.
10. Schema-validate the compare result; fail on invalid JSON.
11. Persist via `ComparisonArtifact.ReplaceWith(...)`.
12. Emit `AdminAuditEvent` with the payload shape under `audit-event-payload.md`.
13. Return `GenerateComparisonSuccess`.

Failures at any step:
- Convert provider exceptions to typed `FailureReason` constants documented in `data-model.md`.
- Leave the prior cached artifact untouched (FR-I4).
- Emit failure audit event before returning.

`GetStatusAsync` reads `ComparisonArtifact` + the latest `ComparisonJob` for the item and composes the `ItemStatusResult`. No AI calls.
