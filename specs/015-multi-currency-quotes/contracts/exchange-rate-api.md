# Contract: Admin Exchange-Rate API

Authorization: `[Authorize(Roles = "Administrator")]` on every endpoint. Anti-forgery on every mutation.

## `GET /Admin/AdminExchangeRates`

Lists all CRC↔USD rates, newest first. Renders the rate-history view.

**Response 200** (HTML; JSON via `?json=1`):
```json
[
  {
    "id": "8b1d5a2e-…",
    "sourceCurrencyCode": "USD",
    "targetCurrencyCode": "CRC",
    "buyRate": "520.000000",
    "sellRate": "525.000000",
    "effectiveAtUtc": "2026-05-06T14:00:00Z",
    "createdByUserName": "admin@FundingPlatform.com",
    "createdAtUtc": "2026-05-06T14:00:13Z",
    "isUsed": true
  }
]
```

`buyRate` and `sellRate` are **CRC per 1 USD** (per spec clarification Q1).

## `POST /Admin/AdminExchangeRates`

Create a new rate. The current "active" rate is whatever has the latest `EffectiveAtUtc` for the pair.

**Request**:
```json
{
  "sourceCurrencyCode": "USD",
  "targetCurrencyCode": "CRC",
  "buyRate":  520.000000,
  "sellRate": 525.000000,
  "effectiveAtUtc": "2026-05-06T14:00:00Z"
}
```

**Responses**:

| Status | Body | Trigger |
|---|---|---|
| `201 Created` | The created record (same shape as list above). Audit-log event `ExchangeRate.Created`. | Success. |
| `400 Bad Request` | `{ "error": "BuyRate must be greater than zero." }` | `buyRate <= 0` (FR-006). |
| `400 Bad Request` | `{ "error": "SellRate must be greater than zero." }` | `sellRate <= 0` (FR-006). |
| `400 Bad Request` | `{ "error": "Effective timestamp must be in the past or now." }` | `effectiveAtUtc > now` (FR-007a). |
| `409 Conflict` | `{ "error": "Rate at this timestamp already exists." }` | Duplicate `(source, target, effectiveAtUtc)` (FR-007). |
| `403 Forbidden` | — | Caller not Administrator. |

## `PUT/DELETE /Admin/AdminExchangeRates/{id}` — explicitly NOT IMPLEMENTED

Both verbs MUST return:

| Status | Body |
|---|---|
| `405 Method Not Allowed` | `{ "error": "Exchange rates are immutable. Supersede by creating a new rate." }` |

The audit log MUST record an `ExchangeRate.EditAttemptBlocked` / `DeleteAttemptBlocked` event with the calling user's id and the rate id (FR-008, FR-010).

## Latest-rate read (internal)

Used by the conversion-preview endpoint and quote-save:

```sql
SELECT TOP 1 * FROM ExchangeRates
 WHERE SourceCurrencyCode = @source AND TargetCurrencyCode = @target
 ORDER BY EffectiveAtUtc DESC
```

Backed by `IX_ExchangeRates_PairEffectiveAtDesc`.
