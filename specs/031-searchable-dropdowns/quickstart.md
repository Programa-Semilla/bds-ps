# Quickstart: Searchable Dropdowns

How to make a data-driven dropdown searchable, and how to verify it.

## Enhance an existing control

1. Find the data-driven `<select>` in its Razor view (or shared partial).
2. Add `data-searchable` to the `<select>` tag. Keep everything else (`asp-for`, `asp-items`, `class="form-select"`, `data-testid`) unchanged.

   ```html
   <select asp-for="GroupId" asp-items="Model.EligibleGroups"
           class="form-select" data-testid="application-group-select"
           data-searchable>
       <option value="">— Seleccione —</option>
   </select>
   ```

3. That's it for flat controls. The globally-loaded `searchable-select.js` enhances it on load when the list has more than 7 real options; smaller lists stay plain.
4. To override the threshold for one control: `data-searchable-threshold="3"`.

### Controls already covered by shared partials

Adding `data-searchable` once in these files covers all their usages:
- `Views/Shared/Components/_CascadingFundFilter.cshtml` — Fund/Process/Group cascade levels.
- `Views/Shared/_LocationCascade.cshtml` — Provincia/Cantón/Distrito.
- `Views/Shared/_QuoteFields.cshtml` — Currency (stays plain below threshold).
- `Views/Supplier/_BranchPicker.cshtml` — supplier branch.

### Group-drilldown group level

The drilldown's group level is a checkbox list, not a `<select>`. Its searchable filter lives in `group-drilldown-selector.js` (a text input above the checkboxes). Its Fund/Process `<select>`s use the generic `data-searchable` enhancer.

## Don't enhance these

Static enum dropdowns — application status/state, role, supplier verification status, process status, stage kind, identification type, yes/no. Leave them with **no** `data-searchable` attribute (FR-009).

## Localized copy

Spanish strings live in `Resources/SearchableDropdownResources.cs`:
- `SearchPlaceholder = "Escriba para filtrar…"`
- `NoMatchMessage = "Sin coincidencias"`

The enhancer reads them from markup (no Spanish literals in JS).

## Verify

### Manual

1. Run the app: `dotnet run --project src/FundingPlatform.AppHost`.
2. Open a page with an enhanced control (e.g. `/Admin/Processes/Create`).
3. Focus the Fund field, type a fragment (try one with accents, e.g. "carta" → matches "Cartago"), confirm the list narrows, pick with Enter, submit, and confirm the persisted value is correct.
4. Toggle JS off (or break the script) and confirm the plain `<select>` still works.

### Asset budget

```bash
bash scripts/verify-asset-budget.sh   # must stay green; JS/CSS are not counted
```

### E2E (filtered — the delivery gate)

Run only the classes that exercise this feature plus any whose page objects were migrated to the combobox helper:

```bash
dotnet test tests/FundingPlatform.Tests.E2E \
  --filter "FullyQualifiedName~SearchableDropdowns"
# plus the migrated suites, e.g. ~AdminProcess, ~ExchangeRates, ~Cascading, as touched
```

Per repo conventions, the full ~30-min E2E suite is run only for critical/cross-cutting changes or on explicit request.

## E2E helper

Use `PageObjects/SearchableSelect.cs` for enhanced controls:

```csharp
var fund = new SearchableSelect(Page, "admin-process-fund-select");
await fund.SelectSearchableAsync("Cartago");
// assert the native select committed the right value via [data-testid="admin-process-fund-select"]
```

Below-threshold controls keep using `SelectOptionAsync` directly.
