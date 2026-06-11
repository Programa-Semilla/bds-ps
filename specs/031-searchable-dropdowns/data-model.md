# Phase 1 Data Model: Searchable Dropdowns

This feature is **presentation-only**. It introduces **no persistent entities, no database tables, no EF model changes, and no DTO/request-shape changes**. The dacpac is untouched (Constitution IV; spec FR-005, SC-003).

What follows documents the client-side component state and the option shape the enhancer reads — there is no server-side model.

## No persistent entities

- No new tables, columns, indexes, or seed data.
- No new or modified Application DTOs, Domain entities, or Infrastructure repositories.
- The only server-side artifact is a Web-layer **resource class** holding two es-CR display strings (not data).

## Client-side component state (per enhanced control)

Each `<select data-searchable>` the enhancer manages holds this in-memory state (no persistence):

| Field | Type | Meaning |
|---|---|---|
| `sourceSelect` | HTMLSelectElement | The authoritative native `<select>` (posted value lives here) |
| `options` | Array<{ value, label, normalizedLabel }> | Snapshot of current selectable options, rebuilt on `childList` mutation |
| `threshold` | number | `data-searchable-threshold` ?? global default (7); compared against selectable-option count |
| `enhanced` | boolean | Whether the combobox UI is currently shown (count > threshold) vs. plain native select |
| `query` | string | Current text in the combobox input (filter only — never a committed value) |
| `activeIndex` | number | Index of the highlighted option in the filtered view (for `aria-activedescendant`) |
| `committedValue` | string | Mirror of `sourceSelect.value`; the input display reverts to this label on blur |

### Option shape

Read from each `<option>` in the source select:

- `value` — the `<option value>` (entity id or code; empty string = placeholder/"all", excluded from the selectable count).
- `label` — the option's visible text content.
- `normalizedLabel` — `label.normalize('NFD').replace(/[̀-ͯ]/g,'').toLocaleLowerCase('es')`, precomputed for accent/case-insensitive substring matching.

## State transitions

```text
                 options.count > threshold
   [native select] ───────────────────────────▶ [enhanced: combobox shown]
        ▲                                                  │
        │   options rebuilt, new count <= threshold        │ type → filter (query changes; no commit)
        └──────────────────────────────────────────────────┤
                                                            │ Enter/click option → set sourceSelect.value
                                                            │                       + dispatch change(bubbles)
                                                            │ Esc / blur-without-commit → input reverts to committedValue label
                                                            ▼
                                                   [committed value updated]
```

- **Cascade refresh**: a `childList` mutation on `sourceSelect` rebuilds `options`, re-evaluates `enhanced`, re-syncs the displayed label to `sourceSelect.value`, and clears any stale `query`.
- **Must-pick invariant**: the only writer of `sourceSelect.value` is an explicit option commit; `query` never propagates to `value`.

## Validation rules (client) and their server mirror

- Required data-driven selects keep their existing server-side `[Required]`/`asp-validation-for` behavior. Because the enhancer never fabricates a value, an unmatched/empty commit leaves `sourceSelect.value` empty and server validation behaves exactly as for the plain dropdown (FR-003, US2 scenario 2). No new validation is introduced.
