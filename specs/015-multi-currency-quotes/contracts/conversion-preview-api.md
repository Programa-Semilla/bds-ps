# Contract: Quote Conversion-Preview API

Authorization: `[Authorize]` (any authenticated user that can already access the quote form). Anti-forgery on the POST.

## `POST /SupplierQuotes/Convert`

Server-computed conversion preview. Called from `quote-conversion-preview.js` on currency-or-amount blur. **The client MUST NOT compute the conversion locally.** (FR-019)

**Request**:
```json
{
  "currencyCode": "USD",
  "amount":       1000.00
}
```

**Responses**:

| Status | Body | Trigger |
|---|---|---|
| `200 OK` | See `ConversionPreviewResponse` below | Success. |
| `200 OK` | `{ "isCrc": true, "amount": 1000.00 }` | When `currencyCode == "CRC"` (no conversion necessary). |
| `400 Bad Request` | `{ "error": "Amount must be greater than zero." }` | `amount <= 0`. |
| `400 Bad Request` | `{ "error": "Currency '<X>' is not enabled." }` | `currencyCode` exists but `IsEnabled = 0`. |
| `404 Not Found` | `{ "error": "Currency '<X>' is not configured." }` | Unknown ISO code. |
| `409 Conflict` | `{ "error": "No reference exchange rate is configured. Contact an administrator." }` | No rate exists for the pair (FR-018). |
| `401 Unauthorized` / `403 Forbidden` | — | Caller unauthenticated / unauthorized. |

### `ConversionPreviewResponse` (200 success when conversion happens)

```json
{
  "isCrc": false,
  "originalCurrencyCode": "USD",
  "originalAmount":  1000.00,
  "convertedCrcAmount": 520000.00,
  "rate": {
    "rateRecordId":   "8b1d5a2e-…",
    "rateValue":      520.000000,
    "rateType":       "Buy",
    "effectiveAtUtc": "2026-05-06T14:00:00Z"
  }
}
```

## Save-time semantics

The form's `POST /SupplierQuotes/Create` endpoint **does not trust the preview**. It re-reads the latest applicable rate at save time and snapshots it onto the quote (FR-015). The preview is for UX only.

When the form posts:
- If the latest rate at save time differs from the rate the preview returned, the server uses the new rate (no warning UI in MVP — simple last-write-wins; the user sees the snapshot fields after save). This matches spec edge case "Rate change between preview and save".
- If no rate exists for the pair at save time, save fails with the same FR-018 message and the form re-renders with the validation error.
