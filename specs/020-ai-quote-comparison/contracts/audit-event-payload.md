# AdminAuditEvent Payload Shape: AI Quote Comparison

**Spec**: `spec.md` | **Plan**: `plan.md` | **Date**: 2026-05-11

The existing `dbo.AdminAuditEvents` table is reused unchanged. Comparison-specific data lives in `PayloadJson`. The JSON structure below is documented here so future cost-rollup tooling can query it without schema changes (FR-H3 / SC-011).

## Action constants

| `Action` column | When emitted |
|---|---|
| `AiComparisonGenerated` | Successful generation persisted. `success = true` in payload. |
| `AiComparisonFailed` | Generation aborted at any stage. `success = false` and `failureReason` populated. |

`TargetType = "ApplicationItem"`, `TargetId = applicationItemId.ToString()`.

## `PayloadJson` shape (v1)

```json
{
  "v": 1,
  "applicationId":      "uuid",
  "applicationItemId":  "uuid",
  "actorUserId":        "<identity user id>",
  "actorRole":          "Reviewer | Admin",
  "supplierIds":        ["uuid", "uuid"],
  "inputHash":          "<64-hex>",
  "promptVersion":      "2026-05-11",
  "schemaVersion":      "v1",
  "aiModel":            "claude-opus-4-7",
  "extractModel":       "claude-sonnet-4-6",
  "tokenCostInput":     43210,
  "tokenCostOutput":    8120,
  "latencyMs":          27340,
  "success":            true,
  "failureReason":      null,
  "bypassedRateLimit":  false,
  "bypassedTokenCap":   false,
  "redactedFieldCounts": {
    "applicantNationalId":    1,
    "applicantPersonalPhone": 1,
    "applicantPersonalEmail": 1,
    "supplierOwnerDni":       2,
    "supplierOwnerPhone":     2,
    "filePatternCedula":      3,
    "filePatternPhone":       7,
    "filePatternEmail":       1
  }
}
```

### Field notes

- **`v`** — schema version of this payload shape. Bump only when payload shape changes; not coupled to `ComparisonArtifact` schema version.
- **`supplierIds`** — emitted in the same canonical order used in `InputDescriptor.OrderedSupplierIds` (the hash input). Lets downstream join supplier rollups deterministically.
- **`failureReason`** — when `success = false`, one of the constants in `data-model.md`: `provider_transient`, `provider_hard:<code>`, `schema_invalid`, `rate_limit_exceeded`, `token_cap_exceeded`, `worker_crashed`, `unsupported_format`, `pii_redaction_failed`, `application_closed`, `timeout`.
- **`bypassedRateLimit` / `bypassedTokenCap`** — true iff the admin invoked the override toggle for that flag. SC-007 verifies this end-to-end.
- **`redactedFieldCounts`** — observability signal only; counts of redacted spans per pattern/field. No raw values. Permits a future "are we redacting enough" dashboard.

### What is NEVER in `PayloadJson`

Per FR-H2 / NFR-S2 / NFR-O2:
- Raw prompt text.
- Raw model response.
- Raw file content.
- Anthropic API key.
- Redacted source values (only counts, no spans).

## Roll-up query exemplars (SC-011)

These illustrate that the audit row alone supports the future cost-rollup dashboard (no joins to deleted artifact data, only a join to `Applications` for the program dimension):

```sql
-- Token cost per application in a window
SELECT a.ProgramId,
       a.Id  AS ApplicationId,
       SUM(JSON_VALUE(e.PayloadJson, '$.tokenCostInput'))  AS InTokens,
       SUM(JSON_VALUE(e.PayloadJson, '$.tokenCostOutput')) AS OutTokens
FROM   dbo.AdminAuditEvents e
JOIN   dbo.Applications a
       ON  a.Id = TRY_CAST(JSON_VALUE(e.PayloadJson, '$.applicationId') AS uniqueidentifier)
WHERE  e.Action IN ('AiComparisonGenerated', 'AiComparisonFailed')
  AND  e.OccurredAt BETWEEN @from AND @to
GROUP BY a.ProgramId, a.Id;

-- Cost per reviewer
SELECT e.ActorUserId,
       COUNT(*)                                            AS Generations,
       SUM(JSON_VALUE(e.PayloadJson, '$.tokenCostInput'))  AS InTokens
FROM   dbo.AdminAuditEvents e
WHERE  e.Action = 'AiComparisonGenerated'
  AND  e.OccurredAt BETWEEN @from AND @to
GROUP BY e.ActorUserId;

-- Override audit trail
SELECT e.OccurredAt, e.ActorUserId, e.TargetId
FROM   dbo.AdminAuditEvents e
WHERE  JSON_VALUE(e.PayloadJson, '$.bypassedRateLimit') = 'true'
   OR  JSON_VALUE(e.PayloadJson, '$.bypassedTokenCap')  = 'true';
```
