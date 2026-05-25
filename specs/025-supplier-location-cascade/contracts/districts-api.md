# Contract: Districts cascade API

Mirrors the existing `GET /api/cantons` contract (`CantonsApiController`).

## `GET /api/districts?cantonId={id}`

Returns the distritos of a cantón, for the third-tier dependent `<select>`.

- **Auth**: `[AllowAnonymous]` — non-confidential catalog shared across roles (same posture as `/api/cantons`).
- **Query param**: `cantonId` (int, required). Unknown/absent → empty array (not an error), matching the cantons endpoint behavior.
- **Response 200** (`application/json`):
  ```json
  [
    { "id": 312, "name": "Carmen" },
    { "id": 313, "name": "Merced" }
  ]
  ```
  Shape `[{ id: int, name: string }]`, ordered by `name` (`OrderBy(d => d.Name)`).
- **Caching**: `Cache-Control: public, max-age=3600` — the catalog is legislatively static. Identical rationale to `/api/cantons`.
- **Controller**: `DistrictsApiController : ControllerBase`, `[ApiController]`, `[Route("api/districts")]`, injects `AppDbContext`, filters `Districts.Where(d => d.CantonId == cantonId)`.

## Client wiring (data-driven cascade)

The cantón `<select>` becomes a cascade **source** for the distrito `<select>`:

| Attribute | Cantón source (NEW) | Province source (existing, retrofitted) |
|---|---|---|
| `data-cascade-source` | `"canton"` | `"province"` |
| `data-cascade-endpoint` | `/api/districts` | `/api/cantons` |
| `data-cascade-param` | `cantonId` | `provinceId` |
| `data-cascade-target` | `#district-{DistrictFieldName}` | `#canton-{CantonFieldName}` |
| `data-cascade-placeholder` | `"Seleccione un distrito"` | `"Seleccione un cantón"` |

`location-cascade.js` (generalized from `province-canton-cascade.js`) binds every `select[data-cascade-source]`, fetches `{endpoint}?{param}={value}` on change, replaces the target's options (preserving a prior selection via `data-cascade-current` when still valid), and dispatches a bubbling `change` so a province change chains through cantón to distrito (resetting both). Network failure leaves existing options in place (no silent submit of a blank value; the required rule still blocks submit).
