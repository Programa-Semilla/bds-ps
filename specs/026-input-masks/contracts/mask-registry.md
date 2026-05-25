# Contract: Client Mask Registry (`wwwroot/js/input-masks.js`)

The script exposes a registry keyed by mask name. An input opts in with `data-mask="<name>"`. Handling is **event-delegated** on `document` so dynamically-injected nodes (supplier lookup partials) are covered.

## Registry entry shape

```js
// MASKS[name] =
{
  mode: 'strict' | 'soft',
  maxLength: <int>,                 // hard cap (also set as the input's maxlength)
  format: function(raw) -> string,  // strict: strip+regroup as typed; soft: identity or null
  validate: function(value) -> bool // soft: on blur; strict: optional final check
}
```

## Catalogue (v1)

| `data-mask` | mode | format (as-typed) | maxLength | validate (canonical) |
|---|---|---|---|---|
| `email` | soft | — | 256 | lax RFC `^[^@\s]+@[^@\s]+\.[^@\s]+$` |
| `phone-cr` | strict | digits→`0000-0000` | 9 | `^\d{4}-\d{4}$` |
| `cedula` | strict | digits→`0-0000-0000` | 11 | `^\d-\d{4}-\d{4}$` |
| `cedula-jur` | strict | digits→`0-000-000000` | 12 | `^\d-\d{3}-\d{6}$` |
| `dimex` | strict | digits only (no separators) | 12 | `^\d{11,12}$` |
| `nite` | strict | digits→`0-000-000000` | 12 | `^\d-\d{3}-\d{6}$` |
| `pasaporte` | soft | uppercase alnum | 20 | `^[A-Z0-9]{1,20}$` |

`maxLength` counts separators (e.g. cédula = 9 digits + 2 hyphens = 11).

## Behavior

- **strict**: on `input`, strip disallowed chars, regroup with separators, cap to `maxLength`; format any server-rendered value once when the node appears.
- **soft**: on `blur`, if non-empty and `!validate(value)`, add Tabler `.is-invalid` + an adjacent `.invalid-feedback` sibling with an es-CR message and set `aria-invalid="true"`; clear when valid; empty defers to `Required`.

## Identification type-selector controller

A `<select>` adjacent to an identification input drives which mask is active:

```html
<select data-mask-controller="<group-id>"> ... options carry data-mask-for="cedula|cedula-jur|dimex|nite|pasaporte" ... </select>
<input data-mask-group="<group-id>" data-mask="cedula" ... >
```

- On `change`: set the grouped input's `data-mask` to the chosen option's `data-mask-for`, re-apply (reformat/strip the current value to the new type), and re-validate. Entered digits are preserved where they fit; incompatible remainder is flagged, never silently dropped.
- On load: the selector's current value (server-restored persisted type) determines the initial mask, and the server-rendered value is formatted through it.

## Extensibility (SC-006)

Adding a new structured field = one new `MASKS[name]` entry + `data-mask="name"` on the input (and, if type-switchable, a `data-mask-for` option). No other JS change. The cédula masks added here are the demonstration.

## Surfaces that must load the script (FR-016)

`Account/Register.cshtml`, `Admin/Users/Create.cshtml`, `Admin/Users/Edit.cshtml`, `Supplier/Add.cshtml` (covers the AJAX-injected `_LookupEmpty`/`_BranchPicker` via delegation), and any other view rendering an `email`/`phone-cr`/identification field. Loaded via `@section Scripts { <script src="~/js/input-masks.js" asp-append-version="true" defer></script> }`.
