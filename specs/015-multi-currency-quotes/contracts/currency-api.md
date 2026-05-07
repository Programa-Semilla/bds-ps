# Contract: Admin Currency API

Authorization: `[Authorize(Roles = "Administrator")]` on every endpoint. Anti-forgery on every mutation.

## `GET /Admin/Currencies`

Returns the configured currencies, ordered by `DisplayOrder`.

**Response 200** (rendered HTML page; same data shape exposed for tests via `?json=1`):
```json
[
  { "code": "CRC", "symbol": "₡", "displayName": "Costa Rican colón", "isEnabled": true, "isBaseCurrency": true, "displayOrder": 1 },
  { "code": "USD", "symbol": "$", "displayName": "US dollar",        "isEnabled": true, "isBaseCurrency": false, "displayOrder": 2 }
]
```

## `POST /Admin/Currencies/{code}/Disable`

Disable a non-base currency.

| Status | Body | Trigger |
|---|---|---|
| `204 No Content` | — | Success. Audit-log event `Currency.Disabled` written. |
| `409 Conflict` | `{ "error": "CRC is the system base currency and cannot be disabled." }` | `code = "CRC"` (FR-002). |
| `404 Not Found` | — | Unknown code. |
| `403 Forbidden` | — | Caller is not Administrator. |

## `POST /Admin/Currencies/{code}/Enable`

Enable a previously-disabled currency. Idempotent — re-enabling an already-enabled currency returns `204`.

| Status | Body | Trigger |
|---|---|---|
| `204 No Content` | — | Success. Audit-log event `Currency.Enabled`. |
| `404 Not Found` | — | Unknown code. |
| `403 Forbidden` | — | Caller is not Administrator. |

## Side effects

- Disabling USD MUST NOT alter or invalidate existing supplier quotes denominated in USD (FR-003).
- Disabling USD MUST cause subsequent calls to `GET /SupplierQuotes/Create` to omit USD from the currency-selector options.
