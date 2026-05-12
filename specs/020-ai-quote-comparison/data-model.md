# Data Model: AI-Powered Quote Comparison for Reviewers

**Spec**: `spec.md` | **Plan**: `plan.md` | **Date**: 2026-05-11

## Entities

### `ComparisonArtifact` (aggregate root) — Domain

Represents the cached output of a successful comparison for one `ApplicationItem`.

| Field | Type | Notes |
|---|---|---|
| `ApplicationItemId` | `Guid` | Primary key. 1:1 with `Item`. |
| `JsonContent` | `string` | Schema-validated artifact JSON (`ComparisonArtifact.v1.json`). Encrypted-at-rest is a host responsibility (NFR carry-over from spec 014). |
| `InputHash` | `string` (64 hex chars) | SHA-256 of canonical-JSON `InputDescriptor`. Determines staleness. |
| `PromptVersion` | `string` | Prompt-catalog version. Bumped with prompt-file changes. |
| `SchemaVersion` | `string` | Comparison-artifact schema version (e.g., `v1`). Bumped with schema-file changes. |
| `AiModel` | `string` | Anthropic model identifier (e.g., `claude-opus-4-7`). |
| `GeneratedAt` | `DateTimeOffset` | When the run completed. |
| `GeneratedByUserId` | `string` (Identity user id) | Who triggered the generation. |
| `TokenCostInput` | `int` | Input token count for full pipeline (extract + compare). |
| `TokenCostOutput` | `int` | Output token count for full pipeline. |
| `LatencyMs` | `int` | End-to-end wall-clock latency (extract dispatch through compare completion). |

**Invariants**:
- `JsonContent` non-empty + schema-valid (enforced by `JsonSchema.Net` in entity factory + `ReplaceWith`).
- `InputHash` matches `^[a-f0-9]{64}$`.
- `TokenCostInput`, `TokenCostOutput`, `LatencyMs` ≥ 0.
- `SchemaVersion` non-empty.

**Behavior methods**:
- `IsStaleAgainst(InputDescriptor descriptor) : FreshnessResult` — recomputes hash via `InputHasher.Compute(descriptor)`; returns `{IsFresh: bool, ChangedInputs: ChangedInput[]}`. `ChangedInput` enum values: `FileAdded`, `FileRemoved`, `LineEdited`, `SupplierAdded`, `SupplierRemoved`, `SnapshotChanged`, `SchemaBumped`, `PromptVersionBumped`.
- `ReplaceWith(string json, string inputHash, string promptVersion, string schemaVersion, string aiModel, string userId, int tokenIn, int tokenOut, int latencyMs)` — atomic in-place replace; rejects negative tokens, malformed hash, schema-invalid JSON.

**Factory**:
- `ComparisonArtifact.Create(Guid applicationItemId, string json, string inputHash, string promptVersion, string schemaVersion, string aiModel, string userId, int tokenIn, int tokenOut, int latencyMs, IClock clock)`.

### `ComparisonJob` (aggregate root) — Domain

Represents a queued or in-flight generation request triggered by "Generar todo" or a sync regeneration.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `ApplicationItemId` | `Guid` | The item being compared. |
| `RequestedByUserId` | `string` | Who enqueued. |
| `Status` | `ComparisonJobStatus` enum | `Pending` | `Running` | `Completed` | `Failed` |
| `BypassedRateLimit` | `bool` | Was the per-app rate limit overridden? |
| `BypassedTokenCap` | `bool` | Was the per-run token cap overridden? |
| `LastStatusChangeAt` | `DateTimeOffset` | Updated on every transition. Reaper input. |
| `ResultingArtifactId` | `Guid?` | Set on `Completed`. References `ComparisonArtifacts.ApplicationItemId`. |
| `FailureReason` | `string?` | Set on `Failed`. Constants: `provider_transient`, `provider_hard:<code>`, `schema_invalid`, `rate_limit_exceeded`, `token_cap_exceeded`, `worker_crashed`, `unsupported_format`, `pii_redaction_failed`, `application_closed`. |
| `StartedAt` | `DateTimeOffset?` | Set on `Running`. |
| `FinishedAt` | `DateTimeOffset?` | Set on `Completed` or `Failed`. |

**Invariants**:
- State machine: `Pending → Running → Completed`. Or `Pending → Failed`. Or `Running → Failed`. No other transitions.
- `ResultingArtifactId != null ⟺ Status == Completed`.
- `FailureReason != null ⟺ Status == Failed`.
- `StartedAt != null ⟺ Status ∈ {Running, Completed, Failed-after-Start}`. Pre-flight `Failed` (pre-`Start`) sets only `FinishedAt`.

**Behavior methods**:
- Static factory `Enqueue(Guid applicationItemId, string requestedByUserId, bool bypassedRateLimit, bool bypassedTokenCap, IClock clock) : ComparisonJob` — initial status `Pending`; sets `LastStatusChangeAt = clock.Now`.
- `Start(IClock clock)` — `Pending → Running`; updates `StartedAt` + `LastStatusChangeAt`.
- `RecordSuccess(Guid artifactId, int tokenIn, int tokenOut, int latencyMs, IClock clock)` — `Running → Completed`; sets `ResultingArtifactId`, `FinishedAt`, `LastStatusChangeAt`.
- `RecordFailure(string failureReason, IClock clock)` — to `Failed` from `Pending` (pre-flight guard reject) or `Running` (mid-run failure). Sets `FailureReason`, `FinishedAt`, `LastStatusChangeAt`.
- `Reap(IClock clock) : bool` — iff `Status == Running` AND `LastStatusChangeAt < clock.Now - OrphanReapWindow`, transitions to `Failed` with `failureReason = "worker_crashed"` and returns `true`; otherwise no-op returns `false`.

**Factory rejects**: empty `applicationItemId`, empty `requestedByUserId`.

### `InputDescriptor` (Application value object)

Pure data carrier for hash computation. Not persisted.

| Field | Type | Source |
|---|---|---|
| `ApplicationItemId` | `Guid` | Caller |
| `OrderedSupplierIds` | `Guid[]` | Live state via repository |
| `OrderedBranchIds` | `Guid[]` | Live state |
| `BlobReferences` | `(Guid blobId, string contentHash)[]` | `IObjectStorage` returns content hash via existing handle |
| `LineState` | `(Guid lineId, decimal quantity, decimal unitPrice, string currencyCode, Guid exchangeRateSnapshotId)[]` | Live state |
| `PromptVersion` | `string` | Constants in `AnthropicPromptCatalog` |
| `SchemaVersion` | `string` | Constants in `AnthropicPromptCatalog` |

Hashed by `InputHasher.Compute(InputDescriptor) → string` (canonical JSON: sorted keys, declared array order, no whitespace, then SHA-256 lower-case hex).

### `AdminAuditEvent` (existing — spec 016) — reused

No schema changes. Comparison events use:
- `Action = "AiComparisonGenerated"` (success) | `"AiComparisonFailed"` (failure). Bypass state lives on the same row via `bypassedRateLimit` / `bypassedTokenCap` flags in `PayloadJson` — no separate `AiComparisonBypassed` action. This keeps roll-ups single-row-per-attempt and matches the action constants enumerated in `contracts/audit-event-payload.md`.
- `TargetType = "ApplicationItem"`, `TargetId = applicationItemId.ToString()`.
- `PayloadJson` schema documented under `contracts/audit-event-payload.md`. Fields: `applicationId`, `supplierIds[]`, `inputHash`, `promptVersion`, `schemaVersion`, `aiModel`, `tokenCostInput`, `tokenCostOutput`, `latencyMs`, `success`, `failureReason?`, `bypassedRateLimit`, `bypassedTokenCap`, `actorRole`.

## Database Schema (dacpac)

### `dbo.ComparisonArtifacts.sql`

```sql
CREATE TABLE [dbo].[ComparisonArtifacts]
(
    [ApplicationItemId]    UNIQUEIDENTIFIER  NOT NULL,
    [JsonContent]          NVARCHAR(MAX)     NOT NULL,
    [InputHash]            CHAR(64)          NOT NULL,
    [PromptVersion]        NVARCHAR(64)      NOT NULL,
    [SchemaVersion]        NVARCHAR(32)      NOT NULL,
    [AiModel]              NVARCHAR(128)     NOT NULL,
    [GeneratedAt]          DATETIMEOFFSET    NOT NULL,
    [GeneratedByUserId]    NVARCHAR(450)     NOT NULL,
    [TokenCostInput]       INT               NOT NULL,
    [TokenCostOutput]      INT               NOT NULL,
    [LatencyMs]            INT               NOT NULL,
    CONSTRAINT [PK_ComparisonArtifacts]
        PRIMARY KEY CLUSTERED ([ApplicationItemId]),
    CONSTRAINT [FK_ComparisonArtifacts_Items]
        FOREIGN KEY ([ApplicationItemId])
        REFERENCES [dbo].[Items]([Id])
        ON DELETE CASCADE
);
GO
CREATE INDEX [IX_ComparisonArtifacts_InputHash]
    ON [dbo].[ComparisonArtifacts]([InputHash]);
```

### `dbo.ComparisonJobs.sql`

```sql
CREATE TABLE [dbo].[ComparisonJobs]
(
    [Id]                     UNIQUEIDENTIFIER  NOT NULL,
    [ApplicationItemId]      UNIQUEIDENTIFIER  NOT NULL,
    [RequestedByUserId]      NVARCHAR(450)     NOT NULL,
    [Status]                 NVARCHAR(16)      NOT NULL,  -- Pending|Running|Completed|Failed
    [BypassedRateLimit]      BIT               NOT NULL,
    [BypassedTokenCap]       BIT               NOT NULL,
    [LastStatusChangeAt]     DATETIMEOFFSET    NOT NULL,
    [ResultingArtifactId]    UNIQUEIDENTIFIER  NULL,
    [FailureReason]          NVARCHAR(128)     NULL,
    [StartedAt]              DATETIMEOFFSET    NULL,
    [FinishedAt]             DATETIMEOFFSET    NULL,
    CONSTRAINT [PK_ComparisonJobs]
        PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ComparisonJobs_Items]
        FOREIGN KEY ([ApplicationItemId])
        REFERENCES [dbo].[Items]([Id])
        ON DELETE CASCADE
);
GO
CREATE INDEX [IX_ComparisonJobs_Status_LastStatusChangeAt]
    ON [dbo].[ComparisonJobs]([Status], [LastStatusChangeAt]);
GO
CREATE INDEX [IX_ComparisonJobs_ApplicationItemId_Status]
    ON [dbo].[ComparisonJobs]([ApplicationItemId], [Status]);
```

Composite index choices:
- `(Status, LastStatusChangeAt)`: reaper scans for `Status = 'Running'` with `LastStatusChangeAt < cutoff`.
- `(ApplicationItemId, Status)`: status polling for an item / a "Generar todo" run.

### `dbo.AdminAuditEvents` — no schema change

Comparison events go into the existing table; `PayloadJson` carries the comparison-specific fields. No column additions; no indexes added.

## Entity ↔ Storage mapping

| Entity field | Column | Notes |
|---|---|---|
| `ComparisonArtifact.JsonContent` | `JsonContent NVARCHAR(MAX)` | Stored as JSON; not indexed. |
| `ComparisonArtifact.InputHash` | `InputHash CHAR(64)` | Index for diagnostic "what fed which artifact" lookups. |
| `ComparisonJob.Status` | `Status NVARCHAR(16)` | EF value converter to/from enum. |
| `ComparisonJob.FailureReason` | `FailureReason NVARCHAR(128)` | Nullable. |

EF configurations:
- `ComparisonArtifactConfiguration` — maps the entity; uses `HasConversion` on no non-trivial fields. Concurrency token not needed (writer is exclusive per item via the orchestrator's per-item lock + DB row PK).
- `ComparisonJobConfiguration` — maps the entity; status as string conversion.

## Repository contracts (Application abstractions)

- `IComparisonArtifactRepository.GetByItemIdAsync(Guid applicationItemId, CancellationToken) : Task<ComparisonArtifact?>`.
- `IComparisonArtifactRepository.UpsertAsync(ComparisonArtifact artifact, CancellationToken)` — entity factory enforces invariants; persists or replaces by `ApplicationItemId`.
- `IComparisonJobRepository.GetAsync(Guid id, ...)`, `GetPendingForApplicationAsync(Guid applicationId, ...)`, `GetByApplicationItemAsync(Guid applicationItemId, ...)`, `EnqueueAsync(ComparisonJob, ...)`, `UpdateAsync(ComparisonJob, ...)`, `GetOrphanedRunningAsync(DateTimeOffset cutoff, ...)`.

Authorization layer: `GetByItemIdAsync` does **not** filter by group; the caller (controller / orchestrator) applies the group-overlap predicate to the parent application via the existing spec-016 helper before invoking the orchestrator. Pattern matches the existing repository surface in `ApplicationRepository.GetByStateForReviewerAsync`.
