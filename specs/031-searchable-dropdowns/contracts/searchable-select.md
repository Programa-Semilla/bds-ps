# Contract: `searchable-select.js` enhancer

This is a **UI/markup contract** (the project has no external API surface for this feature). It defines the opt-in markup, the DOM/ARIA structure the enhancer produces, the behavior, and the E2E helper signature.

## 1. Opt-in markup contract (authored in views)

A control opts in by adding `data-searchable` to an existing native `<select>`:

```html
<select asp-for="FundId" asp-items="Model.FundOptions"
        class="form-select"
        data-testid="admin-process-fund-select"
        data-searchable>
    <option value="">— Seleccione un fondo —</option>
</select>
```

Attributes the enhancer reads on the `<select>`:

| Attribute | Required | Default | Meaning |
|---|---|---|---|
| `data-searchable` | yes | — | Marks the control for enhancement. |
| `data-searchable-threshold` | no | global `7` | Min selectable-option count (`>`) before the combobox appears. |
| `data-searchable-placeholder` | no | es-CR default from layout | Combobox input placeholder text. |

Rules:
- The enhancer **must not** alter `name`, `value`, `id`, `asp-for` binding, or existing `data-*` (e.g. `data-role`, `data-testid`, `data-cascade-*`). The native select stays the posted value.
- The enhancer **must not** enhance a select lacking `data-searchable` (guarantees static enums are excluded — FR-009).
- A select with `data-searchable` but `selectableOptionCount <= threshold` renders as the plain native select (FR-006).

## 2. Produced DOM / ARIA structure (when enhanced)

```html
<div class="fl-searchable" data-searchable-root>
  <!-- native select kept in DOM, hidden from pointer/AT, still posted -->
  <select ... data-searchable data-searchable-enhanced aria-hidden="true" tabindex="-1"> … </select>

  <input type="text" role="combobox"
         class="form-select fl-searchable-input"
         aria-expanded="false"
         aria-controls="<listboxId>"
         aria-activedescendant=""
         aria-labelledby="<original label id>"
         data-testid="<sourceTestId>-search"
         placeholder="Escriba para filtrar…">

  <ul id="<listboxId>" role="listbox" class="fl-searchable-list" hidden>
    <li role="option" id="<opt-0>" class="fl-searchable-option" data-value="1">Fondo Norte</li>
    …
    <li class="fl-searchable-empty" aria-live="polite" hidden>Sin coincidencias</li>
  </ul>
</div>
```

- `data-testid` on the input is derived as `<source data-testid>-search` so E2E can target it deterministically; the original `data-testid` stays on the native `<select>`.
- The listbox `id` and option `id`s are generated uniquely per control.

## 3. Behavior contract

| Interaction | Result |
|---|---|
| Type in input | Filter options by accent/case-insensitive substring of visible text; open listbox; highlight first match; announce count via live region. |
| ↑ / ↓ | Move highlight; update `aria-activedescendant`; wrap or clamp at ends. |
| Enter | Commit highlighted option: set `select.value`, dispatch bubbling `change`, set input text to option label, close listbox. |
| Click option | Same as Enter for that option. |
| Esc | Close listbox without changing committed value; restore input text to committed label. |
| Tab / blur | Commit current highlight if the listbox is open with a match; otherwise restore input text to committed label. Never writes typed text as a value. |
| No match | Show "Sin coincidencias"; committing is impossible; blur restores committed label. |
| Options rebuilt (cascade) | Rebuild option snapshot, re-evaluate threshold, re-sync input label to `select.value`, clear stale query. |
| Newly injected `[data-searchable]` (AJAX) | Auto-enhanced by the document-level observer. |
| JS unavailable / enhancement error | Native `<select>` remains fully usable (progressive enhancement). |

**Invariants**
- The committed value is always one of the source `<option>` values (FR-003).
- `select.value` after a search-and-pick equals the value the plain dropdown would have submitted for that option (SC-002).
- No network request is introduced.

## 4. Localization contract

- Spanish copy originates from `Resources/SearchableDropdownResources.cs` (`SearchPlaceholder`, `NoMatchMessage`) and reaches the enhancer through markup (`data-searchable-placeholder` per control and a single layout-level default for the empty-state). The JS contains **no** Spanish string literals.

## 5. E2E helper contract (`PageObjects/SearchableSelect.cs`)

```csharp
public sealed class SearchableSelect
{
    public SearchableSelect(IPage page, string sourceTestId);

    // Type a fragment into the combobox and click the option whose label matches.
    public Task SelectSearchableAsync(string labelFragment);

    // Type a fragment without committing (assert on the visible option list).
    public Task FilterAsync(string text);

    // The visible option locators currently shown in the listbox.
    public ILocator Options { get; }

    // The "Sin coincidencias" empty-state locator.
    public ILocator EmptyState { get; }
}
```

- Targets `[data-testid="<sourceTestId>-search"]` for the input and `[data-testid="<sourceTestId>"]` for the native select (value assertions).
- Below-threshold / non-enhanced controls continue to use `ILocator.SelectOptionAsync` directly (no helper needed).
