# HTTP Contracts: AI Quote Comparison

**Spec**: `spec.md` | **Plan**: `plan.md` | **Date**: 2026-05-11

All routes are added to `ReviewController` under the existing `/Review` prefix. Authentication: existing ASP.NET Identity cookie. Authorization: `[Authorize(Roles="Reviewer,Admin")]` + spec-016 group-overlap predicate enforced inside the action.

## `POST /Review/GenerateComparison/{applicationItemId:int}`

Synchronously generates (or regenerates) the comparison for a single item. Returns within `AiComparison:SyncHardTimeoutSeconds` (default 90 s).

**Request body** (JSON):

```json
{
  "bypassRateLimit": false,
  "bypassTokenCap":  false
}
```

Both flags are ignored unless the caller is in role `Admin`. Default both `false`.

**Responses**:

| HTTP | Body | Notes |
|---|---|---|
| `200 OK` | `ItemComparisonViewModel` JSON | Success. Includes the artifact JSON + `freshness: "Fresh"` + `lastUpdatedAt`. |
| `400 Bad Request` | `{ "code": "single_supplier" }` | Item has < 2 supplier quotations (button shouldn't render; defensive). |
| `400 Bad Request` | `{ "code": "unsupported_format", "offendingInput": "<blobId>" }` | Spreadsheet / non-PDF attachment that can't be processed. |
| `400 Bad Request` | `{ "code": "pii_redaction_failed", "offendingInput": "<blobId>" }` | Image-only PDF refused; tells the reviewer which file to replace. |
| `403 Forbidden` | empty | Reviewer outside the application's group scope (spec 016). |
| `409 Conflict` | `{ "code": "concurrent_generation" }` | Another generation is already in flight for this item. |
| `422 Unprocessable Entity` | `{ "code": "rate_limit_exceeded", "remaining": 0, "windowResetsAt": "..." }` | FR-G1. Admin can retry with `bypassRateLimit: true`. |
| `422 Unprocessable Entity` | `{ "code": "token_cap_exceeded", "estimatedTokens": N, "cap": 200000, "offendingInput": "..." }` | FR-G2. Admin can retry with `bypassTokenCap: true`. |
| `502 Bad Gateway` | `{ "code": "provider_transient" }` \| `{ "code": "provider_hard", "providerCode": "..." }` | Provider error. Show retry button. |
| `500 Internal Server Error` | `{ "code": "schema_invalid", "validatorPath": "..." }` | AI returned invalid JSON. Show retry button. |
| `504 Gateway Timeout` | `{ "code": "timeout" }` | Server hard timeout hit (90 s default). |

Every response (success or failure) emits one `AdminAuditEvent` per the contract in `audit-event-payload.md`.

## `POST /Review/GenerateAll/{applicationId:int}`

Enqueues per-item `ComparisonJob` rows. Returns immediately.

**Request body**:

```json
{
  "forceAll":         false,
  "bypassRateLimit":  false,
  "bypassTokenCap":   false
}
```

`forceAll = true` is ignored unless the caller is in role `Admin`. The two-step "Anular límites + Forzar regeneración total" UX in spec FR-A4 is enforced client-side; server-side accepts the body as documented.

**Responses**:

| HTTP | Body | Notes |
|---|---|---|
| `202 Accepted` | `{ "enqueued": [ { "applicationItemId": "...", "jobId": "..." } ], "skippedFresh": ["..."] }` | Success. |
| `403 Forbidden` | empty | Group-scope violation. |
| `409 Conflict` | `{ "code": "application_closed" }` | Application is archived / closed. |
| `422 Unprocessable Entity` | `{ "code": "no_eligible_items" }` | Every item has only 1 supplier; nothing to compare. |

## `GET /Review/ItemStatus/{applicationItemId:int}`

Polled by the review screen while any job for the parent application is `Pending` or `Running`.

**Response (200 OK)**:

```json
{
  "applicationItemId": 42,
  "state": "None | Cached-Fresh | Cached-Stale | Pending | Running | Failed",
  "freshness": "Fresh | Stale | None",
  "changedInputs": ["FileAdded", "LineEdited"],
  "lastUpdatedAt": "2026-05-11T18:42:11Z",
  "failureReason": "provider_transient" 
}
```

`changedInputs` is non-empty iff `freshness == "Stale"`. `failureReason` is set iff `state == "Failed"`.

ETag / cache headers: `Cache-Control: no-store`. The client side computes a per-application "all-done" predicate over the per-item statuses to stop polling.

## `GET /Review/Citations/{applicationItemId:int}/{sourceRefId}`

Resolves a citation `SourceRef` to a signed URL via the existing `IObjectStorage.ResolveServingHandleAsync` (spec 014). `sourceRefId` is the originating document/blob id — the `Document.Id` (INT) that the orchestrator projects through `DeriveBlobGuid` to populate the artifact's `sourceRefs[].blobId`. The view layer renders the marker with `sourceRefId = documentId` so the controller can look up the `Document` row and stream the same blob through the existing spec-014 SAS-TTL policy. (The earlier draft described a position-based `<itemIdx>:<rowOrSectionLocator>:<sourceRefIdx>` locator; the live implementation is simpler — a direct document id.)

**Response**:

| HTTP | Body | Notes |
|---|---|---|
| `302 Found` | (redirect to signed URL) | Default behavior. TTL respects the per-category storage policy. |
| `404 Not Found` | empty | Document unknown or no blob key on the row. |
| `403 Forbidden` | empty | Group-scope violation. |

## Notes

- Group-overlap predicate is applied at the EF query level by loading the parent `Application` via the existing `ApplicationRepository.GetByStateForReviewerAsync` shape; the comparison endpoints share that predicate.
- Concurrency control: orchestrator acquires a per-item lock (in-process `SemaphoreSlim` keyed by `applicationItemId`, scoped to the web app). Cross-process safety is unnecessary in MVP — Aspire deploys a single web instance for dev/test and a single replica plan is the documented prod posture. Cross-process safety becomes a future concern if/when web is scaled out.
- No raw prompts or model responses are persisted (FR-H2).
