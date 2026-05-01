# Data Model: Azure Blob Storage with Environment-Driven Provider Selection

**Feature**: 014-azure-blob-storage
**Date**: 2026-05-01

This document describes the new domain/persistence/configuration data shapes introduced by this feature and the schema deltas needed to land them.

## Application-layer types (new)

### `FileCategory` (enum)

```csharp
public enum FileCategory
{
    SignedFundingAgreement,    // signed-funding-agreements
    SupplierCatalogImport,     // supplier-catalog-imports
    ApplicationAttachment,     // application-attachments
    GeneratedArtifact,         // generated-artifacts
}
```

- 1:1 with the four containers in FR-013.
- Drives the per-category options surface (`Storage:Categories:{Name}:MaxSizeBytes`, `Storage:Categories:{Name}:Serving`) and the upload size cap (FR-021).
- New categories REQUIRE a spec amendment (per FR-013 / FR-017).

### `ObjectKey` (value object)

Concrete string with parser. Implements FR-014.

| Field | Source | Notes |
|-------|--------|-------|
| `OwnerSegment` | `applicants/{applicantId}` or `admin` | Rendered at write time. |
| `EntityId` | Owning aggregate's GUID | Mandatory. |
| `DeterministicSuffix` | Persisted on the owning row (`BlobKey`) | The owning entity's GUID, generated when the file is uploaded. |
| `Extension` | Original upload extension, lowercased | Defaults to `.bin`. |

`ObjectKey.Build(category, ownerSegment, entityId, suffix, extension)` returns the canonical string. `ObjectKey.Parse(string)` reverses it for diagnostics. Total length capped at 1024 bytes.

### `StoredObject` (record)

Returned from `IObjectStorage.UploadAsync` and emitted in logs.

| Field | Type | Source |
|-------|------|--------|
| `Container` | `string` | Derived from `FileCategory`. |
| `Key` | `string` | `ObjectKey.ToString()`. |
| `SizeBytes` | `long` | Bytes written. |
| `ContentType` | `string` | Negotiated MIME. |
| `CreatedAt` | `DateTimeOffset` | UTC; backend-supplied where possible, host clock otherwise. |
| `Provider` | `StorageProviderName` (`AzureBlob`/`Azurite`/`LocalFilesystem`) | For diagnostics. |

### `StorageHandle` (discriminated record)

```csharp
public abstract record StorageHandle;
public sealed record BackendStreamHandle(Stream Content, string ContentType, long? Length) : StorageHandle;
public sealed record TimeLimitedUrlHandle(Uri Url, DateTimeOffset ExpiresAt, string ContentType, long? Length) : StorageHandle;
```

Returned by `IObjectStorage.ResolveServingHandleAsync`. Default per-category serving model is `BackendStreamHandle` (FR-017). The `LocalFilesystem` provider returns `BackendStreamHandle` for any request; if the caller asks for `TimeLimitedUrl`, it throws `LocalProviderUrlNotSupportedException` (per FR-edge "Local-mode parity gaps").

### `StorageOptions` (configuration POCO)

Bound from the `Storage:` section.

```yaml
Storage:
  Provider: AzureBlob | Azurite | LocalFilesystem
  ConnectionString: string?               # only honored outside Production (FR-011)
  AccountReference: string?               # Aspire reference resolved at startup
  StreamingThresholdBytes: 1048576        # FR-020 default
  RetryBudget:
    MaxAttempts: 3
    BudgetSeconds: 30
  LocalFilesystem:
    RootPath: string?                     # only used when Provider = LocalFilesystem
  Categories:
    SignedFundingAgreement:
      MaxSizeBytes: 20971520              # default 20 MiB
      Serving: BackendStream              # default per FR-017
      Retention: NoPolicy                 # FR-023 seam — string ID, no enforcement
    SupplierCatalogImport:
      MaxSizeBytes: 52428800              # default 50 MiB
      Serving: BackendStream
      Retention: NoPolicy
    ApplicationAttachment:
      MaxSizeBytes: 20971520
      Serving: BackendStream
      Retention: NoPolicy
    GeneratedArtifact:
      MaxSizeBytes: 20971520
      Serving: BackendStream
      Retention: NoPolicy
  TestFallback:
    AllowFilesystem: false                # FR-008 — must be set to true to opt into the legacy fallback
```

## Persistence (dacpac)

### Existing tables that gain a `BlobKey` column

| Table | New column | Purpose |
|-------|------------|---------|
| `dbo.FundingAgreements` | `BlobKey nvarchar(1024) NOT NULL` | Canonical key for the generated agreement PDF. Set on insert. |
| `dbo.SignedUploads` | `BlobKey nvarchar(1024) NOT NULL` | Canonical key for the applicant's signed PDF. Set on insert. |
| `dbo.Documents` | `BlobKey nvarchar(1024) NOT NULL` | Canonical key for application attachments (quotation documents). Set on insert. |

`BlobKey` is `NOT NULL` from day one. There is no legacy on-disk corpus and no transition window; every row is created with a populated key.

### Constraints

- `BlobKey` is `NVARCHAR(1024) NOT NULL` on every table, matching `ObjectKey.MaxLengthBytes`.
- No FK changes. The blob-key column is a plain string the application owns.

## Logging shape (FR-025)

Single structured log event per storage operation:

```json
{
  "event": "ObjectStorage.Upload" | "ObjectStorage.Download" | "ObjectStorage.Exists" | "ObjectStorage.Delete" | "ObjectStorage.ResolveHandle",
  "container": "signed-funding-agreements",
  "key": "signed-funding-agreements/applicants/.../.../abcd1234.pdf",
  "sizeBytes": 184321,
  "durationMs": 47,
  "outcome": "Success" | "NotFound" | "RetryExhausted" | "OversizeRejected",
  "provider": "AzureBlob" | "Azurite" | "LocalFilesystem",
  "errorCode": null
}
```

Never includes blob contents, signed URL strings, or auth tokens.

## State transitions

There are no domain-state transitions introduced — files are either present (a single state) or absent.

## Validation rules

- `ObjectKey` parser rejects keys exceeding 1024 bytes, containing path-traversal segments (`..`), control characters, or uppercase characters in the container portion.
- `StorageOptions` validation at startup: provider must be a known value; `LocalFilesystem.RootPath` must exist & be writable when provider is `LocalFilesystem`; `RetryBudget.MaxAttempts` ≥ 0; `RetryBudget.BudgetSeconds` ≥ 1.
- Per-category `MaxSizeBytes` ≥ 1; if missing, falls back to the documented default in `StorageOptions` (FR-021).
