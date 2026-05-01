# Contract: `IObjectStorage` port

**Feature**: 014-azure-blob-storage
**Layer**: `FundingPlatform.Application.Abstractions.Storage`
**Implementations**: `AzureBlobObjectStorage` (production + Azurite), `LocalFilesystemObjectStorage` (offline opt-in / test fallback).

## Surface

```csharp
namespace FundingPlatform.Application.Abstractions.Storage;

public interface IObjectStorage
{
    Task<StoredObject> UploadAsync(
        FileCategory category,
        ObjectKey key,
        Stream content,
        string contentType,
        long? contentLength,
        CancellationToken ct);

    Task<Stream> OpenReadAsync(
        FileCategory category,
        ObjectKey key,
        CancellationToken ct);

    Task<bool> ExistsAsync(
        FileCategory category,
        ObjectKey key,
        CancellationToken ct);

    Task DeleteAsync(
        FileCategory category,
        ObjectKey key,
        CancellationToken ct);

    Task<StorageHandle> ResolveServingHandleAsync(
        FileCategory category,
        ObjectKey key,
        ServingMode preferred,
        CancellationToken ct);
}

public enum ServingMode
{
    BackendStream,
    TimeLimitedUrl,
}
```

## Method semantics

### `UploadAsync`

- **Pre**: caller has authorized the operation. `key.OwnerSegment` matches the authorized actor's scope. `content` is positioned at the start.
- **Streaming**: above `Storage:StreamingThresholdBytes` (default 1 MiB), implementations MUST chunk; below, a single block write is allowed. Memory bounded for 100 MiB inputs.
- **Atomicity**: if the operation fails partway, no orphan blob remains under the key. Implementations MUST either commit atomically (Azure block-blob staged commit) or remove the partial.
- **Returns**: `StoredObject` with the actual size, content type, created-at (server UTC where available), and provider name.
- **Errors**:
  - `ArgumentException` for an invalid `ObjectKey`.
  - `ObjectStorageOperationException(reason: RetryExhausted)` after the configured retry budget (FR-edge).
  - `OperationCanceledException` if `ct` is signalled.

### `OpenReadAsync`

- **Returns**: an unbuffered `Stream` positioned at offset 0. The caller disposes.
- **Errors**:
  - `ObjectNotFoundException` if the blob is absent (FR-edge "Missing blob on download").
  - `ObjectStorageOperationException(reason: RetryExhausted)` after retries exhausted.

### `ExistsAsync`

- **Returns**: `true` if the blob is queryable, `false` otherwise. Never throws on absence.
- **Cost contract**: implementations SHOULD use a HEAD-equivalent (no body fetch). Used by the migration tool's idempotency check.

### `DeleteAsync`

- **Idempotent**: deleting a missing blob is not an error (returns silently).
- **Errors**: `ObjectStorageOperationException(reason: RetryExhausted)` only.

### `ResolveServingHandleAsync`

- **Behavior by `ServingMode`**:
  - `BackendStream`: returns `BackendStreamHandle` with an open `Stream` (caller disposes), the resolved content type, and length when known. All implementations support this.
  - `TimeLimitedUrl`: AzureBlob/Azurite implementations return `TimeLimitedUrlHandle` with a SAS-style URL whose expiry is `Storage:Categories:{Category}:UrlExpiry` (default ≤ 15 min, FR-019). The `LocalFilesystem` implementation throws `LocalProviderUrlNotSupportedException` (FR-edge "Local-mode parity gaps").
- **Authorization**: the port performs no authorization. The caller MUST have already authorized the operation (FR-018).

## Error type taxonomy

| Type | Surfaced when | Caller action |
|------|---------------|---------------|
| `ObjectNotFoundException` | Read/handle for an absent key. | Map to 404 / domain-specific not-found. |
| `OversizeException` | Caller-side guard (controllers); not the port itself. | Map to 413 / category-specific message. |
| `LocalProviderUrlNotSupportedException` | `ResolveServingHandleAsync(..., ServingMode.TimeLimitedUrl, ...)` against `LocalFilesystem`. | Switch the request to `BackendStream` mode or change provider. |
| `ObjectStorageOperationException(reason)` | Wraps a non-retryable SDK error after retry exhaustion. | Surface as 5xx; log alongside the diagnostic record (FR-025). |

The port does NOT throw `OversizeException`; controllers reject oversized requests *before* calling `UploadAsync` so no bytes ever reach the backend (FR-022).

## Concurrency

- All methods are safe to call concurrently across distinct keys.
- For the same key, last-writer-wins. The platform never relies on storage-level optimistic concurrency for blob writes; concurrency on the owning aggregate is enforced at the EF / domain layer.

## Logging contract (FR-025)

Each method MUST emit a single structured log event with the schema in `data-model.md` § Logging shape, regardless of provider. The wrapper class `ObjectStorageDiagnostics` enforces this so the three implementations stay in sync.

## Invariants

- Implementations MUST NOT make authorization decisions.
- Implementations MUST NOT log blob contents, signed URL strings, or auth tokens.
- Implementations MUST honor cancellation: `ct` is checked between SDK calls and on long-running streams.
- Implementations MUST surface a `Provider` value distinguishing `AzureBlob` from `Azurite` even when both use the same Azure SDK code path (the source-of-truth is the wired-up endpoint, not the SDK type).

## Test obligations (per implementation)

| Test | What it verifies |
|------|------------------|
| Roundtrip upload + download streams identical bytes | `UploadAsync` + `OpenReadAsync` |
| `ExistsAsync` reflects real state | `UploadAsync` → `ExistsAsync` true; `DeleteAsync` → `ExistsAsync` false |
| `DeleteAsync` is idempotent | Calling on an absent key returns silently |
| Oversize is rejected at the controller, not the port | Asserted via E2E that submits a > cap file and observes the 413 + log absence |
| Streaming threshold respected | Memory diagnostic during 100 MiB upload stays bounded |
| URL handle expires | Issue handle, wait > expiry, observe 403 from the URL (Azure / Azurite only) |
| Local provider rejects URL requests | `LocalProviderUrlNotSupportedException` thrown |
| Logging shape matches contract | Single structured event per call, no contents leaked |
